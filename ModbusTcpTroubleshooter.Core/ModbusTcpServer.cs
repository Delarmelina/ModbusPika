using System.Net;
using System.Net.Sockets;

namespace ModbusTcpTroubleshooter.Core;

public sealed class ModbusTcpServer
{
    private readonly ModbusDataMap _map;
    private TcpListener? _listener;

    public event EventHandler<TrafficEvent>? TrafficObserved;

    public ModbusTcpServer(ModbusDataMap map)
    {
        _map = map;
    }

    public async Task StartAsync(IPAddress localAddress, int port, CancellationToken cancellationToken)
    {
        _listener = new TcpListener(localAddress, port);
        _listener.Start();
        Emit(TrafficDirection.System, $"{localAddress}:{port}", null, null, null, null, null, "Servidor Modbus TCP iniciado.", []);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Stop();
        }
    }

    public void Stop()
    {
        _listener?.Stop();
        _listener = null;
        Emit(TrafficDirection.System, "local", null, null, null, null, null, "Servidor parado.", []);
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "cliente";
        Emit(TrafficDirection.System, endpoint, null, null, null, null, null, "Cliente conectado.", []);

        using var _ = client;
        var stream = client.GetStream();

        while (!cancellationToken.IsCancellationRequested && client.Connected)
        {
            var header = await ReadExactAsync(stream, 7, cancellationToken);
            if (header.Length == 0)
            {
                break;
            }

            var length = ModbusProtocol.ReadUInt16(header, 4);
            var body = await ReadExactAsync(stream, length - 1, cancellationToken);
            var raw = header.Concat(body).ToArray();
            var request = ModbusProtocol.Parse(raw);
            ModbusProtocol.TryGetAddressRange(request, out var address, out var quantity);
            Emit(TrafficDirection.ClientToServer, endpoint, request.TransactionId, request.UnitId, request.FunctionCode, address, quantity, DescribeRequest(request, address, quantity), raw);

            var response = BuildResponse(request, out var responseSummary);
            await stream.WriteAsync(response, cancellationToken);
            Emit(TrafficDirection.ServerToClient, endpoint, request.TransactionId, request.UnitId, request.FunctionCode, address, quantity, responseSummary, response);
        }

        Emit(TrafficDirection.System, endpoint, null, null, null, null, null, "Cliente desconectado.", []);
    }

    private byte[] BuildResponse(ModbusTcpFrame request, out string summary)
    {
        if (!ModbusProtocol.TryGetAddressRange(request, out var address, out var quantity))
        {
            summary = "Requisicao invalida: PDU curta. Resposta exception 03.";
            return ModbusProtocol.BuildException(request, 3);
        }

        switch (request.FunctionCode)
        {
            case ModbusProtocol.ReadCoils:
                return BuildReadBits(request, ModbusPointType.Coil, address, quantity, out summary);
            case ModbusProtocol.ReadDiscreteInputs:
                return BuildReadBits(request, ModbusPointType.DiscreteInput, address, quantity, out summary);
            case ModbusProtocol.ReadHoldingRegisters:
                return BuildReadRegisters(request, ModbusPointType.HoldingRegister, address, quantity, out summary);
            case ModbusProtocol.ReadInputRegisters:
                return BuildReadRegisters(request, ModbusPointType.InputRegister, address, quantity, out summary);
            case ModbusProtocol.WriteSingleCoil:
                var coilValue = request.Pdu.Length >= 5 && ModbusProtocol.ReadUInt16(request.Pdu, 3) == 0xFF00;
                _map.TryWriteCoil(address, coilValue);
                summary = $"FC05 escrita coil {address} = {coilValue}.";
                return ModbusProtocol.BuildWriteEcho(request);
            case ModbusProtocol.WriteSingleRegister:
                var registerValue = ModbusProtocol.ReadUInt16(request.Pdu, 3);
                _map.TryWriteHoldingRegister(address, registerValue);
                summary = $"FC06 escrita holding register {address} = {registerValue}.";
                return ModbusProtocol.BuildWriteEcho(request);
            case ModbusProtocol.WriteMultipleCoils:
                WriteMultipleCoils(request, address, quantity);
                summary = $"FC15 escrita de {quantity} coil(s) desde {address}.";
                return ModbusProtocol.BuildWriteEcho(request);
            case ModbusProtocol.WriteMultipleRegisters:
                WriteMultipleRegisters(request, address, quantity);
                summary = $"FC16 escrita de {quantity} holding register(s) desde {address}.";
                return ModbusProtocol.BuildWriteEcho(request);
            default:
                summary = $"Function code {request.FunctionCode} nao suportado. Resposta exception 01.";
                return ModbusProtocol.BuildException(request, 1);
        }
    }

    private void WriteMultipleCoils(ModbusTcpFrame request, ushort address, ushort quantity)
    {
        for (var i = 0; i < quantity; i++)
        {
            var byteIndex = 6 + i / 8;
            if (request.Pdu.Length <= byteIndex)
            {
                return;
            }

            var value = (request.Pdu[byteIndex] & (1 << (i % 8))) != 0;
            _map.TryWriteCoil((ushort)(address + i), value);
        }
    }

    private void WriteMultipleRegisters(ModbusTcpFrame request, ushort address, ushort quantity)
    {
        for (var i = 0; i < quantity; i++)
        {
            var offset = 6 + i * 2;
            if (request.Pdu.Length < offset + 2)
            {
                return;
            }

            var value = ModbusProtocol.ReadUInt16(request.Pdu, offset);
            _map.TryWriteHoldingRegister((ushort)(address + i), value);
        }
    }

    private byte[] BuildReadBits(ModbusTcpFrame request, ModbusPointType type, ushort address, ushort quantity, out string summary)
    {
        var values = new List<bool>();
        for (var i = 0; i < quantity; i++)
        {
            var currentAddress = (ushort)(address + i);
            if (!_map.TryReadBit(type, currentAddress, out var value))
            {
                summary = $"Range {address}-{address + quantity - 1} fora do mapa para {type}. Resposta exception 02.";
                return ModbusProtocol.BuildException(request, 2);
            }

            values.Add(value);
        }

        summary = $"{request.FunctionCodeName()} {quantity} ponto(s) desde {address}.";
        return ModbusProtocol.BuildReadBitsResponse(request, values);
    }

    private byte[] BuildReadRegisters(ModbusTcpFrame request, ModbusPointType type, ushort address, ushort quantity, out string summary)
    {
        var values = new List<ushort>();
        for (var i = 0; i < quantity; i++)
        {
            var currentAddress = (ushort)(address + i);
            if (!_map.TryReadRegister(type, currentAddress, out var value))
            {
                summary = $"Range {address}-{address + quantity - 1} fora do mapa para {type}. Resposta exception 02.";
                return ModbusProtocol.BuildException(request, 2);
            }

            values.Add(value);
        }

        summary = $"{request.FunctionCodeName()} {quantity} registrador(es) desde {address}.";
        return ModbusProtocol.BuildReadRegistersResponse(request, values);
    }

    private static string DescribeRequest(ModbusTcpFrame frame, ushort address, ushort quantity)
    {
        return frame.FunctionCode switch
        {
            ModbusProtocol.ReadCoils or ModbusProtocol.ReadDiscreteInputs or ModbusProtocol.ReadHoldingRegisters or ModbusProtocol.ReadInputRegisters => $"{frame.FunctionCodeName()} addr={address} qty={quantity}",
            ModbusProtocol.WriteSingleCoil or ModbusProtocol.WriteSingleRegister => $"{frame.FunctionCodeName()} addr={address}",
            _ => $"Function code {frame.FunctionCode}"
        };
    }

    private async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken);
            if (count == 0)
            {
                return read == 0 ? [] : buffer[..read];
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

public static class ModbusFrameExtensions
{
    public static string FunctionCodeName(this ModbusTcpFrame frame) => frame.FunctionCode switch
    {
        ModbusProtocol.ReadCoils => "FC01 Read Coils",
        ModbusProtocol.ReadDiscreteInputs => "FC02 Read Discrete Inputs",
        ModbusProtocol.ReadHoldingRegisters => "FC03 Read Holding Registers",
        ModbusProtocol.ReadInputRegisters => "FC04 Read Input Registers",
        ModbusProtocol.WriteSingleCoil => "FC05 Write Single Coil",
        ModbusProtocol.WriteSingleRegister => "FC06 Write Single Register",
        ModbusProtocol.WriteMultipleCoils => "FC15 Write Multiple Coils",
        ModbusProtocol.WriteMultipleRegisters => "FC16 Write Multiple Registers",
        _ => $"FC{frame.FunctionCode}"
    };
}
