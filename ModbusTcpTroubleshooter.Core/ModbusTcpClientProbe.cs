using System.Net.Sockets;

namespace ModbusTcpTroubleshooter.Core;

public sealed class ModbusTcpClientProbe
{
    private ushort _transactionId;

    public event EventHandler<TrafficEvent>? TrafficObserved;

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

    private async Task<byte[]> SendAsync(string host, int port, byte[] request, ushort transactionId, byte unitId, byte functionCode, ushort startAddress, ushort quantity, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));

        await client.ConnectAsync(host, port, timeout.Token);
        var endpoint = $"{host}:{port}";
        Emit(TrafficDirection.ClientToServer, endpoint, transactionId, unitId, functionCode, startAddress, quantity, $"Request FC{functionCode} addr={startAddress} qty={quantity}", request);

        var stream = client.GetStream();
        await stream.WriteAsync(request, timeout.Token);

        var header = await ReadExactAsync(stream, 7, timeout.Token);
        var length = ModbusProtocol.ReadUInt16(header, 4);
        var body = await ReadExactAsync(stream, length - 1, timeout.Token);
        var response = header.Concat(body).ToArray();
        Emit(TrafficDirection.ServerToClient, endpoint, transactionId, unitId, functionCode, startAddress, quantity, $"Response FC{response.ElementAtOrDefault(7)}", response);
        return response;
    }

    private ushort NextTransactionId() => ++_transactionId;

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
