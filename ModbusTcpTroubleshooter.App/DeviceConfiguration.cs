using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ModbusTcpTroubleshooter.App;

public sealed partial class MainViewModel
{
    [ObservableProperty] private string clientStationName = "Estacao cliente";
    [ObservableProperty] private string serverName = "Servidor local";

    public async Task ConfigureDeviceAsync(bool client, ClientConnectionSession session, string name,
        string address, int port, byte unitId, int scanMs, bool persistent)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 1024)
            throw new ArgumentException("Informe um nome de ate 1024 caracteres.");
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("Informe um endereco IPv4 valido.");
        if (port is < 1 or > 65535 || client && scanMs is < 100 or > 86400000)
            throw new ArgumentException("Porta: 1 a 65535. Intervalo: 100 a 86400000 ms.");
        if (IsFullTestRunning || IsDeviceProbeRunning || (client
            ? !ClientSessions.Contains(session) || session.IsScanning || session.IsReading
            : IsServerRunning))
            throw new InvalidOperationException("Pare a comunicacao deste dispositivo e aguarde os testes antes de configurar.");

        if (client)
        {
            // Close an idle persistent socket before changing the endpoint.
            await session.DisconnectAsync();
            await session.Client.ConfigureConnectionModeAsync(persistent);
            var changedEndpoint = session.Address != address || session.Port != port || session.UnitId != unitId;
            session.Name = name.Trim();
            session.Address = address;
            session.Port = port;
            session.UnitId = unitId;
            session.ScanRateMs = scanMs;
            session.KeepConnectionOpen = persistent;
            if (changedEndpoint) session.ResetReadings();
        }
        else
        {
            ServerName = name.Trim();
            LocalIp = address;
            ServerPort = port;
            ServerUnitId = unitId;
        }
        Status = "Configuracao aplicada somente ao dispositivo selecionado. Mapa preservado; comunicacao parada.";
    }
}
