using System.Net.Sockets;

namespace ModbusTcpTroubleshooter.Core;

public sealed class ModbusTcpClientProbe
{
    private int _transactionId;
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private TcpClient? _persistentClient;
    private string? _persistentEndpoint;
    private bool _keepConnectionOpen = true;

    public event EventHandler<TrafficEvent>? TrafficObserved;
    public bool KeepConnectionOpen => _keepConnectionOpen;

    public async Task ConfigureConnectionModeAsync(bool keepConnectionOpen, CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            ClosePersistentClient();
            _keepConnectionOpen = keepConnectionOpen;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            var endpoint = $"{host}:{port}";
            if (!_keepConnectionOpen) return;
            if (_persistentClient?.Connected == true && _persistentEndpoint == endpoint) return;
            ClosePersistentClient();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(host, port, timeout.Token);
                _persistentClient = client;
                _persistentEndpoint = endpoint;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try { ClosePersistentClient(); }
        finally { _ioGate.Release(); }
    }

    public async Task<IReadOnlyList<ushort>> ReadRegistersAsync(string host, int port, byte unitId, byte functionCode, ushort startAddress, ushort quantity, CancellationToken cancellationToken)
    {
        var transactionId = NextTransactionId();
        var request = ModbusProtocol.BuildReadRequest(transactionId, unitId, functionCode, startAddress, quantity);
        var response = await SendAsync(host, port, request, transactionId, unitId, functionCode, startAddress, quantity, cancellationToken);
        var frame = ModbusProtocol.Parse(response);

        if ((frame.FunctionCode & 0x80) != 0)
        {
            throw new InvalidOperationException($"Modbus exception {frame.Pdu.ElementAtOrDefault(1)}.");
        }

        var byteCount = frame.Pdu[1];
        var values = new List<ushort>();
        for (var i = 0; i < byteCount / 2; i++)
        {
            values.Add(ModbusProtocol.ReadUInt16(frame.Pdu, 2 + i * 2));
        }

        return values;
    }

    public async Task<IReadOnlyList<bool>> ReadBitsAsync(string host, int port, byte unitId, byte functionCode, ushort startAddress, ushort quantity, CancellationToken cancellationToken)
    {
        var transactionId = NextTransactionId();
        var request = ModbusProtocol.BuildReadRequest(transactionId, unitId, functionCode, startAddress, quantity);
        var response = await SendAsync(host, port, request, transactionId, unitId, functionCode, startAddress, quantity, cancellationToken);
        var frame = ModbusProtocol.Parse(response);

        if ((frame.FunctionCode & 0x80) != 0)
        {
            throw new InvalidOperationException($"Modbus exception {frame.Pdu.ElementAtOrDefault(1)}.");
        }

        var values = new List<bool>();
        var byteCount = frame.Pdu[1];
        for (var i = 0; i < quantity && i < byteCount * 8; i++)
        {
            var byteValue = frame.Pdu[2 + i / 8];
            values.Add((byteValue & (1 << (i % 8))) != 0);
        }

        return values;
    }

    public async Task WriteSingleRegisterAsync(string host, int port, byte unitId, ushort address, ushort value, CancellationToken cancellationToken)
    {
        var transactionId = NextTransactionId();
        var request = ModbusProtocol.BuildWriteSingleRegisterRequest(transactionId, unitId, address, value);
        var response = await SendAsync(host, port, request, transactionId, unitId, ModbusProtocol.WriteSingleRegister, address, 1, cancellationToken);
        var frame = ModbusProtocol.Parse(response);

        if ((frame.FunctionCode & 0x80) != 0)
        {
            throw new InvalidOperationException($"Modbus exception {frame.Pdu.ElementAtOrDefault(1)}.");
        }
    }

    public async Task WriteSingleCoilAsync(string host, int port, byte unitId, ushort address, bool value, CancellationToken cancellationToken)
    {
        var transactionId = NextTransactionId();
        var request = ModbusProtocol.BuildWriteSingleCoilRequest(transactionId, unitId, address, value);
        var response = await SendAsync(host, port, request, transactionId, unitId, ModbusProtocol.WriteSingleCoil, address, 1, cancellationToken);
        var frame = ModbusProtocol.Parse(response);

        if ((frame.FunctionCode & 0x80) != 0)
        {
            throw new InvalidOperationException($"Modbus exception {frame.Pdu.ElementAtOrDefault(1)}.");
        }
    }

    private async Task<byte[]> SendAsync(string host, int port, byte[] request, ushort transactionId, byte unitId, byte functionCode, ushort startAddress, ushort quantity, CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var endpoint = $"{host}:{port}";
            TcpClient? requestClient = null;
            var client = _persistentClient;

            if (_keepConnectionOpen)
            {
                if (client?.Connected != true || _persistentEndpoint != endpoint)
                {
                    ClosePersistentClient();
                    requestClient = new TcpClient();
                    try
                    {
                        await requestClient.ConnectAsync(host, port, timeout.Token);
                        _persistentClient = requestClient;
                        _persistentEndpoint = endpoint;
                        client = requestClient;
                        requestClient = null;
                    }
                    finally { requestClient?.Dispose(); }
                }
            }
            else
            {
                requestClient = new TcpClient();
                await requestClient.ConnectAsync(host, port, timeout.Token);
                client = requestClient;
            }

            try
            {
                Emit(TrafficDirection.ClientToServer, endpoint, transactionId, unitId, functionCode, startAddress, quantity, $"Request FC{functionCode} addr={startAddress} qty={quantity}", request);
                var stream = client!.GetStream();
                await stream.WriteAsync(request, timeout.Token);

                var header = await ReadExactAsync(stream, 7, timeout.Token);
                var length = ModbusProtocol.ReadUInt16(header, 4);
                if (length is < 2 or > 254)
                    throw new InvalidDataException($"Length MBAP fora dos limites: {length}.");
                var body = await ReadExactAsync(stream, length - 1, timeout.Token);
                var response = header.Concat(body).ToArray();
                Emit(TrafficDirection.ServerToClient, endpoint, transactionId, unitId, functionCode, startAddress, quantity, $"Response FC{response.ElementAtOrDefault(7)}", response);
                ValidateResponse(response, request, transactionId, unitId, functionCode, quantity);
                return response;
            }
            catch
            {
                if (_keepConnectionOpen) ClosePersistentClient();
                throw;
            }
            finally
            {
                requestClient?.Dispose();
            }
        }
        finally { _ioGate.Release(); }
    }

    private static void ValidateResponse(byte[] response, byte[] request, ushort transactionId, byte unitId, byte functionCode, ushort quantity)
    {
        var frame = ModbusProtocol.Parse(response);
        if (frame.TransactionId != transactionId || frame.UnitId != unitId ||
            (frame.FunctionCode != functionCode && frame.FunctionCode != (functionCode | 0x80)))
        {
            throw new InvalidDataException("Resposta nao corresponde ao TID, Unit ID ou function code da requisicao.");
        }

        if ((frame.FunctionCode & 0x80) != 0)
        {
            if (frame.Pdu.Length != 2)
                throw new InvalidDataException("Resposta de exception Modbus malformada.");
        }
        else if (functionCode is ModbusProtocol.ReadCoils or ModbusProtocol.ReadDiscreteInputs or
                 ModbusProtocol.ReadHoldingRegisters or ModbusProtocol.ReadInputRegisters)
        {
            var expectedBytes = functionCode is ModbusProtocol.ReadCoils or ModbusProtocol.ReadDiscreteInputs
                ? (quantity + 7) / 8 : quantity * 2;
            if (frame.Pdu.Length != expectedBytes + 2 || frame.Pdu[1] != expectedBytes)
                throw new InvalidDataException($"Quantidade de dados divergente: esperado {expectedBytes} bytes.");
        }
        else if (functionCode is ModbusProtocol.WriteSingleCoil or ModbusProtocol.WriteSingleRegister)
        {
            if (!frame.Pdu.AsSpan().SequenceEqual(request.AsSpan(7)))
                throw new InvalidDataException("Confirmacao de escrita diverge do endereco/valor enviado.");
        }
    }

    private void ClosePersistentClient()
    {
        try { _persistentClient?.Dispose(); }
        finally
        {
            _persistentClient = null;
            _persistentEndpoint = null;
        }
    }

    private ushort NextTransactionId() => unchecked((ushort)Interlocked.Increment(ref _transactionId));

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken);
            if (count == 0)
            {
                throw new EndOfStreamException("Conexao encerrada antes do frame completo.");
            }

            read += count;
        }

        return buffer;
    }

    private void Emit(TrafficDirection direction, string endpoint, ushort? transactionId, byte? unitId, byte? functionCode, ushort? startAddress, ushort? quantity, string summary, byte[] raw)
    {
        TrafficObserved?.Invoke(this, new TrafficEvent(DateTimeOffset.Now, direction, endpoint, transactionId, unitId, functionCode, startAddress, quantity, summary, ModbusProtocol.ToHex(raw)));
    }
}
