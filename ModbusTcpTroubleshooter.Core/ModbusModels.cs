namespace ModbusTcpTroubleshooter.Core;

public enum ModbusPointType
{
    Coil,
    DiscreteInput,
    HoldingRegister,
    InputRegister
}

public enum TrafficDirection
{
    ClientToServer,
    ServerToClient,
    System
}

public sealed record ModbusPoint(
    ModbusPointType Type,
    ushort Address,
    string Name,
    ushort Value,
    bool IsWritable = true);

public sealed record TrafficEvent(
    DateTimeOffset Timestamp,
    TrafficDirection Direction,
    string Endpoint,
    ushort? TransactionId,
    byte? UnitId,
    byte? FunctionCode,
    ushort? StartAddress,
    ushort? Quantity,
    string Summary,
    string Hex);

public sealed record DiagnosticFinding(
    DateTimeOffset Timestamp,
    string Severity,
    string Message,
    string Recommendation);

public sealed class TroubleshootCase
{
    public string Name { get; set; } = "Novo caso Modbus TCP";
    public string LocalIp { get; set; } = "0.0.0.0";
    public string TargetIp { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;
    public List<ModbusPoint> Map { get; set; } = [];
    public List<TrafficEvent> Traffic { get; set; } = [];
    public List<DiagnosticFinding> Diagnostics { get; set; } = [];
}

public sealed class ModbusDataMap
{
    private readonly Dictionary<ushort, bool> _coils = [];
    private readonly Dictionary<ushort, bool> _discreteInputs = [];
    private readonly Dictionary<ushort, ushort> _holdingRegisters = [];
    private readonly Dictionary<ushort, ushort> _inputRegisters = [];

    public IReadOnlyDictionary<ushort, bool> Coils => _coils;
    public IReadOnlyDictionary<ushort, bool> DiscreteInputs => _discreteInputs;
    public IReadOnlyDictionary<ushort, ushort> HoldingRegisters => _holdingRegisters;
    public IReadOnlyDictionary<ushort, ushort> InputRegisters => _inputRegisters;

    public void Clear()
    {
        _coils.Clear();
        _discreteInputs.Clear();
        _holdingRegisters.Clear();
        _inputRegisters.Clear();
    }

    public void AddPoint(ModbusPointType type, ushort address, ushort value)
    {
        switch (type)
        {
            case ModbusPointType.Coil:
                _coils[address] = value != 0;
                break;
            case ModbusPointType.DiscreteInput:
                _discreteInputs[address] = value != 0;
                break;
            case ModbusPointType.HoldingRegister:
                _holdingRegisters[address] = value;
                break;
            case ModbusPointType.InputRegister:
                _inputRegisters[address] = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    public void LoadDefaults()
    {
        for (ushort i = 0; i < 20; i++)
        {
            _coils[i] = i % 2 == 0;
            _discreteInputs[i] = i % 3 == 0;
            _holdingRegisters[i] = (ushort)(1000 + i);
            _inputRegisters[i] = (ushort)(2000 + i);
        }
    }

    public IReadOnlyList<ModbusPoint> ToPoints()
    {
        var points = new List<ModbusPoint>();
        points.AddRange(_coils.Select(x => new ModbusPoint(ModbusPointType.Coil, x.Key, $"Coil {x.Key}", x.Value ? (ushort)1 : (ushort)0)));
        points.AddRange(_discreteInputs.Select(x => new ModbusPoint(ModbusPointType.DiscreteInput, x.Key, $"Discrete {x.Key}", x.Value ? (ushort)1 : (ushort)0, false)));
        points.AddRange(_holdingRegisters.Select(x => new ModbusPoint(ModbusPointType.HoldingRegister, x.Key, $"HR {x.Key}", x.Value)));
        points.AddRange(_inputRegisters.Select(x => new ModbusPoint(ModbusPointType.InputRegister, x.Key, $"IR {x.Key}", x.Value, false)));
        return points.OrderBy(x => x.Type).ThenBy(x => x.Address).ToList();
    }

    public bool TryReadBit(ModbusPointType type, ushort address, out bool value)
    {
        value = false;
        return type switch
        {
            ModbusPointType.Coil => _coils.TryGetValue(address, out value),
            ModbusPointType.DiscreteInput => _discreteInputs.TryGetValue(address, out value),
            _ => false
        };
    }

    public bool TryWriteCoil(ushort address, bool value)
    {
        _coils[address] = value;
        return true;
    }

    public bool TryReadRegister(ModbusPointType type, ushort address, out ushort value)
    {
        value = 0;
        return type switch
        {
            ModbusPointType.HoldingRegister => _holdingRegisters.TryGetValue(address, out value),
            ModbusPointType.InputRegister => _inputRegisters.TryGetValue(address, out value),
            _ => false
        };
    }

    public bool TryWriteHoldingRegister(ushort address, ushort value)
    {
        _holdingRegisters[address] = value;
        return true;
    }
}
