using System.Text.Json;

namespace ModbusTcpTroubleshooter.Core;

public static class CaseStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static async Task SaveAsync(TroubleshootCase troubleshootCase, string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, troubleshootCase, Options, cancellationToken);
    }

    public static async Task<TroubleshootCase> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<TroubleshootCase>(stream, Options, cancellationToken)
            ?? new TroubleshootCase();
    }
}
