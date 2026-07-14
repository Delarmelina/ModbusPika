using PacketDotNet;
using SharpPcap;

namespace ModbusTcpTroubleshooter.App;

public sealed class NetworkCaptureService : IDisposable
{
    private ICaptureDevice? _device;
    private DateTimeOffset? _firstPacketAt;
    private int _packetNumber;

    public event EventHandler<TcpTimelineRow>? PacketCaptured;

    public IReadOnlyList<CaptureDeviceOption> GetDevices()
    {
        try
        {
            return CaptureDeviceList.Instance
                .Select((device, index) => new CaptureDeviceOption(index, Clean(device.Description), device.Name))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<CaptureDeviceTrafficSample>> SampleDeviceTrafficAsync(string filter, TimeSpan duration, CancellationToken cancellationToken)
    {
        try
        {
            var devices = CaptureDeviceList.Instance
                .Select((device, index) => SampleDeviceTrafficAsync(index, device, filter, duration, cancellationToken))
                .ToArray();

            return await Task.WhenAll(devices);
        }
        catch
        {
            return [];
        }
    }

    public void Start(CaptureDeviceOption deviceOption, string filter)
    {
        Stop();

        var devices = CaptureDeviceList.Instance;
        if (deviceOption.Index < 0 || deviceOption.Index >= devices.Count)
        {
            throw new InvalidOperationException("Interface de captura invalida.");
        }

        _device = devices[deviceOption.Index];
        _device.OnPacketArrival += OnPacketArrival;
        _device.Open(DeviceModes.Promiscuous, 1000);

        if (!string.IsNullOrWhiteSpace(filter))
        {
            _device.Filter = filter;
        }

        _firstPacketAt = null;
        _packetNumber = 0;
        _device.StartCapture();
    }

    public void Stop()
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            _device.StopCapture();
            _device.OnPacketArrival -= OnPacketArrival;
            _device.Close();
        }
        finally
        {
            _device = null;
        }
    }

    public void UpdateFilter(string filter)
    {
        if (_device is null)
        {
            return;
        }

        _device.Filter = string.IsNullOrWhiteSpace(filter) ? null : filter;
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        try
        {
            var raw = e.GetPacket();
            var packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            var timestamp = raw.Timeval.Date.ToLocalTime();
            _firstPacketAt ??= timestamp;

            var row = BuildRow(packet, timestamp, _firstPacketAt.Value, raw.Data.Length);
            PacketCaptured?.Invoke(this, row);
        }
        catch
        {
            // Capture callbacks must not crash the UI.
        }
    }

    private TcpTimelineRow BuildRow(Packet packet, DateTimeOffset timestamp, DateTimeOffset firstPacketAt, int length)
    {
        var ip = packet.Extract<IPPacket>();
        var tcp = packet.Extract<TcpPacket>();
        var udp = packet.Extract<UdpPacket>();
        var arp = packet.Extract<ArpPacket>();

        var protocol = "Other";
        var source = "";
        var destination = "";
        var info = packet.GetType().Name;

        if (tcp is not null && ip is not null)
        {
            var isModbusTcp = IsLikelyModbusTcp(tcp);
            protocol = isModbusTcp || tcp.SourcePort == 502 || tcp.DestinationPort == 502 ? "Modbus/TCP" : "TCP";
            source = $"{ip.SourceAddress}:{tcp.SourcePort}";
            destination = $"{ip.DestinationAddress}:{tcp.DestinationPort}";
            info = isModbusTcp ? BuildModbusTcpInfo(tcp) : BuildTcpInfo(tcp);
        }
        else if (udp is not null && ip is not null)
        {
            protocol = "UDP";
            source = $"{ip.SourceAddress}:{udp.SourcePort}";
            destination = $"{ip.DestinationAddress}:{udp.DestinationPort}";
            info = $"UDP {udp.SourcePort} -> {udp.DestinationPort}";
        }
        else if (arp is not null)
        {
            protocol = "ARP";
            source = arp.SenderProtocolAddress?.ToString() ?? "";
            destination = arp.TargetProtocolAddress?.ToString() ?? "";
            info = $"Who has {destination}? Tell {source}";
        }
        else if (ip is not null)
        {
            protocol = ip.Protocol.ToString().Equals("Icmp", StringComparison.OrdinalIgnoreCase) ? "ICMP" : ip.Protocol.ToString();
            source = ip.SourceAddress.ToString();
            destination = ip.DestinationAddress.ToString();
            info = protocol == "ICMP" ? "ICMP packet" : ip.Protocol.ToString();
        }

        return new TcpTimelineRow
        {
            Number = ++_packetNumber,
            RelativeTime = (timestamp - firstPacketAt).TotalSeconds,
            Source = source,
            Destination = destination,
            Protocol = protocol,
            Length = length,
            Info = info
        };
    }

    private static string BuildTcpInfo(TcpPacket tcp)
    {
        var flags = new List<string>();
        if (tcp.Synchronize) flags.Add("SYN");
        if (tcp.Acknowledgment) flags.Add("ACK");
        if (tcp.Push) flags.Add("PSH");
        if (tcp.Finished) flags.Add("FIN");
        if (tcp.Reset) flags.Add("RST");

        var flagText = flags.Count == 0 ? "TCP" : string.Join(",", flags);
        return $"{tcp.SourcePort} -> {tcp.DestinationPort} [{flagText}] Seq={tcp.SequenceNumber} Ack={tcp.AcknowledgmentNumber} Win={tcp.WindowSize}";
    }

    private static bool IsLikelyModbusTcp(TcpPacket tcp)
    {
        var payload = tcp.PayloadData;
        if (payload is null || payload.Length < 8)
        {
            return false;
        }

        var protocolId = (payload[2] << 8) | payload[3];
        var length = (payload[4] << 8) | payload[5];
        var functionCode = payload[7] & 0x7F;

        return protocolId == 0
            && length >= 2
            && length <= payload.Length - 6
            && functionCode is >= 1 and <= 127;
    }

    private static string BuildModbusTcpInfo(TcpPacket tcp)
    {
        var payload = tcp.PayloadData;
        if (payload is null || payload.Length < 8)
        {
            return BuildTcpInfo(tcp);
        }

        var transactionId = (payload[0] << 8) | payload[1];
        var unitId = payload[6];
        var functionCode = payload[7];
        return $"{tcp.SourcePort} -> {tcp.DestinationPort} Modbus/TCP TID={transactionId} UID={unitId} FC={functionCode}";
    }

    private static string Clean(string? text) => string.IsNullOrWhiteSpace(text) ? "Interface sem descricao" : text.Trim();

    private static async Task<CaptureDeviceTrafficSample> SampleDeviceTrafficAsync(int index, ICaptureDevice device, string filter, TimeSpan duration, CancellationToken cancellationToken)
    {
        var packetCount = 0;
        long byteCount = 0;
        var description = Clean(device.Description);
        var name = device.Name;
        var error = "";

        void OnPacketArrival(object sender, PacketCapture e)
        {
            try
            {
                var raw = e.GetPacket();
                Interlocked.Increment(ref packetCount);
                Interlocked.Add(ref byteCount, raw.Data.Length);
            }
            catch
            {
                // Sampling must not fail due to a malformed packet callback.
            }
        }

        try
        {
            device.OnPacketArrival += OnPacketArrival;
            device.Open(DeviceModes.Promiscuous, 500);
            if (!string.IsNullOrWhiteSpace(filter))
            {
                device.Filter = filter;
            }

            device.StartCapture();
            await Task.Delay(duration, cancellationToken);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            try
            {
                device.StopCapture();
            }
            catch
            {
            }

            try
            {
                device.OnPacketArrival -= OnPacketArrival;
                device.Close();
            }
            catch
            {
            }
        }

        return new CaptureDeviceTrafficSample(
            new CaptureDeviceOption(index, description, name),
            packetCount,
            byteCount,
            error);
    }
}

public sealed record CaptureDeviceOption(int Index, string Description, string Name)
{
    public override string ToString() => $"{Index}: {Description}";
}

public sealed record CaptureDeviceTrafficSample(CaptureDeviceOption Device, int PacketCount, long ByteCount, string Error);
