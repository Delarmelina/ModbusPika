using System.IO;
using System.Net;
using System.Text.Json;
using ModbusTcpTroubleshooter.Core;

namespace ModbusTcpTroubleshooter.App;

public sealed class ApplicationConfiguration
{
    [System.Text.Json.Serialization.JsonRequired]
    public string Format { get; set; } = "ModbusTcpTroubleshooter.FFD";
    [System.Text.Json.Serialization.JsonRequired]
    public int Version { get; set; } = 1;
    public string CaseName { get; set; } = "";
    public string ClientStationName { get; set; } = "Estacao cliente";
    public string ServerName { get; set; } = "Servidor local";
    public string TestMode { get; set; } = "Client";
    public List<int>? TestTargetIndexes { get; set; }
    public string Mode { get; set; } = "Client";
    public int SelectedClient { get; set; }
    public string ServerAddress { get; set; } = "0.0.0.0";
    public int ServerPort { get; set; } = 1502;
    public byte ServerUnitId { get; set; } = 1;
    [System.Text.Json.Serialization.JsonRequired]
    public List<ClientSessionCase> Clients { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public List<ServerRangeConfiguration> ServerRanges { get; set; } = [];
    [System.Text.Json.Serialization.JsonRequired]
    public List<ModbusPoint> ServerPoints { get; set; } = [];
}

public sealed record ServerRangeConfiguration(bool Enabled, ModbusPointType Type, ushort StartAddress,
    ushort Quantity, ushort InitialValue, bool IncrementValue, bool Writable, string NamePrefix);

public static class ConfigurationFile
{
    private const int MaxBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static async Task<ApplicationConfiguration> LoadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Arquivo de configuracao excede 32 MB.");
        var configuration = await JsonSerializer.DeserializeAsync<ApplicationConfiguration>(stream, Options)
            ?? throw new InvalidDataException("Arquivo de configuracao vazio.");
        Validate(configuration);
        return configuration;
    }

    public static async Task SaveAsync(string path, ApplicationConfiguration configuration)
    {
        Validate(configuration);
        var fullPath = Path.GetFullPath(path);
        var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, configuration, Options);
            if (new FileInfo(temp).Length > MaxBytes) throw new InvalidDataException("Configuracao excede 32 MB.");
            File.Move(temp, fullPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static void Validate(ApplicationConfiguration c)
    {
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidDataException(message); }
        static bool Address(string? address) => IPAddress.TryParse(address, out var ip)
            && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        Require(c.Format == "ModbusTcpTroubleshooter.FFD" && c.Version == 1, "Formato ou versao FFD nao suportado.");
        Require(!string.IsNullOrWhiteSpace(c.ClientStationName) && c.ClientStationName.Length <= 1024
            && !string.IsNullOrWhiteSpace(c.ServerName) && c.ServerName.Length <= 1024, "Nome de estacao ou servidor invalido.");
        Require(c.TestMode is "Client" or "Server", "Papel do teste invalido.");
        Require(c.CaseName is not null && c.CaseName.Length <= 1024 && c.Mode is "Client" or "Server", "Nome ou modo invalido.");
        Require(Address(c.ServerAddress) && c.ServerPort is >= 1 and <= 65535, "Endereco ou porta do servidor invalido.");
        Require(c.Clients is { Count: <= 64 } && (c.Clients.Count == 0 ? c.SelectedClient == -1
            : c.SelectedClient >= 0 && c.SelectedClient < c.Clients.Count), "Lista ou selecao de clientes invalida (0 a 64 alvos).");
        Require(c.TestTargetIndexes is null || c.TestTargetIndexes.All(i => i >= 0 && i < c.Clients!.Count), "Selecao de alvos de teste invalida.");
        foreach (var client in c.Clients!)
        {
            Require(client is not null && client.Name is { Length: <= 1024 } && Address(client.Address)
                && client.Port is >= 1 and <= 65535 && client.ScanRateMs is >= 1 and <= 86400000, "Configuracao de cliente invalida.");
            Require(client!.Blocks is { Count: <= 1000 }, "Lista de blocos invalida (maximo 1000 por cliente).");
            long points = 0;
            foreach (var block in client.Blocks!)
            {
                Require(block is not null && block.Name is { Length: <= 1024 } && block.FunctionCode is >= 1 and <= 4
                    && block.Quantity > 0 && block.Quantity <= (block.FunctionCode <= 2 ? 2000 : 125)
                    && (int)block.StartAddress + block.Quantity <= 65536, "Bloco de leitura invalido: funcao, quantidade ou endereco fora dos limites Modbus.");
                points += block!.Quantity;
            }
            Require(points <= 65536, "Mapa do cliente excede 65536 pontos.");
        }
        Require(c.ServerRanges is { Count: <= 1000 } && c.ServerPoints is { Count: <= 262144 }, "Mapa do servidor excede os limites.");
        var expected = new Dictionary<(ModbusPointType, ushort), bool>();
        foreach (var range in c.ServerRanges!)
        {
            Require(range is not null && Enum.IsDefined(range.Type) && range.NamePrefix is { Length: <= 1024 }
                && (!range.Enabled || range.Quantity > 0 && (int)range.StartAddress + range.Quantity <= 65536), "Faixa do servidor invalida.");
            if (!range!.Enabled) continue;
            for (var i = 0; i < range.Quantity; i++)
                expected[(range.Type, (ushort)(range.StartAddress + i))] = range.Writable && range.Type is ModbusPointType.Coil or ModbusPointType.HoldingRegister;
        }
        Require(c.ServerPoints!.Count == expected.Count, "Valores do servidor nao correspondem as faixas configuradas. Aplique o mapa antes de salvar.");
        var seen = new HashSet<(ModbusPointType, ushort)>();
        foreach (var point in c.ServerPoints)
            Require(point is not null && point.Name is { Length: <= 1024 }
                && seen.Add((point.Type, point.Address)) && expected.TryGetValue((point.Type, point.Address), out var writable)
                && point.IsWritable == writable && (point.Type is not (ModbusPointType.Coil or ModbusPointType.DiscreteInput) || point.Value <= 1),
                "Valor, acesso ou endereco do mapa do servidor invalido.");
    }
}
