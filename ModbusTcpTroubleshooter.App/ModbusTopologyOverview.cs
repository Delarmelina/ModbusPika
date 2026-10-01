using System.Net;

namespace ModbusTcpTroubleshooter.App;

public sealed record ModbusTopologyPeer(string Ip, string Role, string Evidence, string Ports, long PacketCount, bool Confirmed);

public sealed record ModbusTopologyOverview(
    IReadOnlyList<ModbusTopologyPeer> Peers,
    string LocalRole,
    string InfrastructureTitle,
    string InfrastructureDetail,
    bool InfrastructureAnnounced,
    int OtherHosts,
    int HiddenPeers,
    int AnnouncedNeighbors)
{
    public static ModbusTopologyOverview Build(IEnumerable<NetworkDiscoveryRow> devices,
        IEnumerable<TopologyLink> links, IEnumerable<NeighborAdvertisement> neighbors, bool serverMode)
    {
        var rows = devices.ToArray();
        var flows = links.Where(x => x.Evidence == "Captura Modbus TCP").ToArray();
        var peers = rows.Where(x => x.IsModbusConfirmed || x.IsModbusObserved || x.IsModbusClientObserved)
            .Where(x => x.DeviceIdentity != "Este computador" && !x.IsSpecialAddress)
            .GroupBy(x => x.Ip).Select(group =>
            {
                var row = group.OrderByDescending(x => x.IsModbusConfirmed).ThenByDescending(x => x.IsModbusObserved).First();
                var packets = flows.Where(x => x.Source == row.Ip || x.Destination == row.Ip).Sum(x => x.Count);
                var role = row.IsModbusConfirmed ? "Servidor validado"
                    : row.IsModbusObserved ? "Resposta Modbus capturada" : "Cliente observado";
                var evidence = packets > 0 ? $"{packets} pacotes Modbus capturados"
                    : row.IsModbusConfirmed ? "Resposta ativa validada" : "Captura passiva";
                var ports = row.IsModbusConfirmed ? row.ConfirmedModbusPorts : row.ObservedModbusPorts;
                return new ModbusTopologyPeer(row.Ip, role, evidence,
                    ports.Length > 0 ? "TCP " + ports : "Escuta não confirmada", packets, row.IsModbusConfirmed);
            }).OrderBy(x => IPAddress.TryParse(x.Ip, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? ((uint)ip.GetAddressBytes()[0] << 24) | ((uint)ip.GetAddressBytes()[1] << 16)
                    | ((uint)ip.GetAddressBytes()[2] << 8) | ip.GetAddressBytes()[3] : uint.MaxValue).ToArray();
        var announced = neighbors.OrderByDescending(x => x.SeenAt).ToArray();
        var infrastructure = announced.FirstOrDefault(x => x.Description.Contains("switch", StringComparison.OrdinalIgnoreCase)
            || x.Description.Contains("catalyst", StringComparison.OrdinalIgnoreCase));
        infrastructure ??= announced.FirstOrDefault();
        var title = infrastructure is null ? "L2 não identificado"
            : infrastructure.Description.Contains("switch", StringComparison.OrdinalIgnoreCase)
                || infrastructure.Description.Contains("catalyst", StringComparison.OrdinalIgnoreCase)
                    ? "Switch anunciado" : "Vizinho de rede anunciado";
        var detail = infrastructure is null ? "Sem LLDP/CDP; switch não identificado."
            : $"{infrastructure.Protocol}: {(string.IsNullOrWhiteSpace(infrastructure.Name) ? infrastructure.Chassis : infrastructure.Name)}"
                + (string.IsNullOrWhiteSpace(infrastructure.Port) ? "" : $" · porta {infrastructure.Port}");
        var other = rows.Where(x => x.DeviceIdentity != "Este computador" && !x.IsSpecialAddress
            && !x.IsModbusConfirmed && !x.IsModbusObserved && !x.IsModbusClientObserved)
            .Select(x => x.Ip).Distinct().Count();
        return new ModbusTopologyOverview(peers.Take(12).ToArray(), serverMode ? "Servidor / Escravo" : "Cliente / Mestre",
            title, detail, infrastructure is not null, other, Math.Max(0, peers.Length - 12), announced.Length);
    }
}
