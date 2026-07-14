using System.Buffers.Binary;

namespace ModbusTcpTroubleshooter.Core;

public static class ModbusProtocol
{
    public const byte ReadCoils = 1;
    public const byte ReadDiscreteInputs = 2;
    public const byte ReadHoldingRegisters = 3;
    public const byte ReadInputRegisters = 4;
    public const byte WriteSingleCoil = 5;
    public const byte WriteSingleRegister = 6;
    public const byte WriteMultipleCoils = 15;
    public const byte WriteMultipleRegisters = 16;

    public static byte[] BuildReadRequest(ushort transactionId, byte unitId, byte functionCode, ushort startAddress, ushort quantity)
    {
        var frame = new byte[12];
        WriteMbap(frame, transactionId, unitId, 6);
        frame[7] = functionCode;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(8), startAddress);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(10), quantity);
        return frame;
    }

    public static byte[] BuildWriteSingleRegisterRequest(ushort transactionId, byte unitId, ushort address, ushort value)
    {
        var frame = new byte[12];
        WriteMbap(frame, transactionId, unitId, 6);
        frame[7] = WriteSingleRegister;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(8), address);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(10), value);
        return frame;
    }

    public static ModbusTcpFrame Parse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 8)
        {
            throw new InvalidDataException("Frame Modbus TCP menor que MBAP + function code.");
        }

        var transactionId = BinaryPrimitives.ReadUInt16BigEndian(frame[..2]);
        var protocolId = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(2, 2));
        var length = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(4, 2));
        var unitId = frame[6];
        var functionCode = frame[7];
        var pdu = frame[7..].ToArray();

        if (protocolId != 0)
        {
            throw new InvalidDataException("Protocol ID diferente de zero.");
        }

        if (length != frame.Length - 6)
        {
            throw new InvalidDataException($"Length MBAP invalido. Esperado {frame.Length - 6}, recebido {length}.");
        }

        return new ModbusTcpFrame(transactionId, unitId, functionCode, pdu, frame.ToArray());
    }

    public static byte[] BuildException(ModbusTcpFrame request, byte exceptionCode)
    {
        var frame = new byte[9];
        WriteMbap(frame, request.TransactionId, request.UnitId, 3);
        frame[7] = (byte)(request.FunctionCode | 0x80);
        frame[8] = exceptionCode;
        return frame;
    }

    public static byte[] BuildReadBitsResponse(ModbusTcpFrame request, IReadOnlyList<bool> values)
    {
        var byteCount = (values.Count + 7) / 8;
        var frame = new byte[9 + byteCount];
        WriteMbap(frame, request.TransactionId, request.UnitId, (ushort)(3 + byteCount));
        frame[7] = request.FunctionCode;
        frame[8] = (byte)byteCount;

        for (var i = 0; i < values.Count; i++)
        {
            if (values[i])
            {
                frame[9 + i / 8] |= (byte)(1 << (i % 8));
            }
        }

        return frame;
    }

    public static byte[] BuildReadRegistersResponse(ModbusTcpFrame request, IReadOnlyList<ushort> values)
    {
        var byteCount = values.Count * 2;
        var frame = new byte[9 + byteCount];
        WriteMbap(frame, request.TransactionId, request.UnitId, (ushort)(3 + byteCount));
        frame[7] = request.FunctionCode;
        frame[8] = (byte)byteCount;

        for (var i = 0; i < values.Count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(9 + i * 2), values[i]);
        }

        return frame;
    }

    public static byte[] BuildWriteEcho(ModbusTcpFrame request)
    {
        var frame = new byte[12];
        WriteMbap(frame, request.TransactionId, request.UnitId, 6);
        request.Raw.AsSpan(7, 5).CopyTo(frame.AsSpan(7));
        return frame;
    }

    public static string ToHex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).Chunk(2).Aggregate(string.Empty, (current, pair) => current + new string(pair) + " ").TrimEnd();

    public static bool TryGetAddressRange(ModbusTcpFrame frame, out ushort address, out ushort quantity)
    {
        address = 0;
        quantity = 0;
        if (frame.Pdu.Length < 5)
        {
            return false;
        }

        address = BinaryPrimitives.ReadUInt16BigEndian(frame.Pdu.AsSpan(1, 2));
        quantity = BinaryPrimitives.ReadUInt16BigEndian(frame.Pdu.AsSpan(3, 2));
        return true;
    }

    public static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));

    private static void WriteMbap(Span<byte> frame, ushort transactionId, byte unitId, ushort length)
    {
        BinaryPrimitives.WriteUInt16BigEndian(frame[..2], transactionId);
        BinaryPrimitives.WriteUInt16BigEndian(frame.Slice(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(frame.Slice(4, 2), length);
        frame[6] = unitId;
    }
}

public sealed record ModbusTcpFrame(
    ushort TransactionId,
    byte UnitId,
    byte FunctionCode,
    byte[] Pdu,
    byte[] Raw);
