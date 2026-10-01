using System.Diagnostics;

namespace ModbusTcpTroubleshooter.App;

public sealed record TrafficWindowSummary(double Seconds, long Packets, long Bytes, long TcpPackets,
    long Resets, long ZeroWindows, long RepeatedSegments, long ModbusFrames, long ModbusExceptions,
    int PeakPacketsPerSecond, long PeakBytesPerSecond, bool DetailLimitReached,
    IReadOnlyDictionary<string, long> Protocols, IReadOnlyDictionary<string, long> Conversations,
    IReadOnlySet<string> ObservedHosts, IReadOnlySet<string> ConfirmedServers,
    IReadOnlyDictionary<string, TcpSignalSummary> Signals)
{
    public IReadOnlySet<string> ObservedClients { get; init; } = new HashSet<string>();
}

public sealed record TcpSignalSummary(long Resets, long ZeroWindows, long RepeatedSegments);

public sealed class TrafficWindowAnalysis
{
    private readonly object _gate = new();
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<string, long> _protocols = [];
    private readonly Dictionary<string, long> _conversations = [];
    private readonly HashSet<string> _hosts = [];
    private readonly HashSet<string> _servers = [];
    private readonly HashSet<string> _clients = [];
    private readonly Dictionary<string, HashSet<(uint Sequence, int Length)>> _segments = [];
    private readonly Dictionary<int, (int Packets, long Bytes)> _buckets = [];
    private readonly Dictionary<string, TcpSignalSummary> _signals = [];
    private bool _active, _limited;
    private DateTimeOffset _startedAt;
    private double? _requestedSeconds;
    private long _packets, _bytes, _tcp, _resets, _zeros, _repeated, _modbus, _exceptions;

    public void Begin(TimeSpan? duration = null)
    {
        lock (_gate)
        {
            _protocols.Clear(); _conversations.Clear(); _hosts.Clear(); _servers.Clear();
            _clients.Clear();
            _segments.Clear(); _buckets.Clear(); _signals.Clear();
            _packets = _bytes = _tcp = _resets = _zeros = _repeated = _modbus = _exceptions = 0;
            _limited = false; _active = true; _startedAt = DateTimeOffset.Now;
            _requestedSeconds = duration?.TotalSeconds; _clock.Restart();
        }
    }

    public void Observe(TcpTimelineRow row)
    {
        lock (_gate)
        {
            if (!_active || row.Timestamp != default && row.Timestamp < _startedAt) return;
            var elapsed = row.Timestamp == default ? _clock.Elapsed.TotalSeconds : (row.Timestamp - _startedAt).TotalSeconds;
            if (_requestedSeconds is double limit && elapsed > limit) return;
            _packets++; _bytes += row.Length;
            _protocols[row.Protocol] = _protocols.GetValueOrDefault(row.Protocol) + 1;
            var second = (int)elapsed;
            var bucket = _buckets.GetValueOrDefault(second);
            _buckets[second] = (bucket.Packets + 1, bucket.Bytes + row.Length);
            AddBounded(_hosts, row.SourceHost); AddBounded(_hosts, row.DestinationHost);
            var flow = row.Source + " -> " + row.Destination;
            if (_conversations.Count < 4096 || _conversations.ContainsKey(flow))
                _conversations[flow] = _conversations.GetValueOrDefault(flow) + 1;
            else _limited = true;
            if (row.IsTcp)
            {
                _tcp++;
                if (row.TcpSynchronize) _segments.Remove(flow);
                var signals = _signals.GetValueOrDefault(flow) ?? new TcpSignalSummary(0, 0, 0);
                if (row.TcpReset) { _resets++; signals = signals with { Resets = signals.Resets + 1 }; }
                if (row.TcpWindow == 0 && row.TcpAcknowledgment && !row.TcpSynchronize && !row.TcpReset)
                { _zeros++; signals = signals with { ZeroWindows = signals.ZeroWindows + 1 }; }
                if (row.TcpPayloadLength > 0)
                {
                    if (!_segments.TryGetValue(flow, out var seen) && _segments.Count < 4096)
                        _segments[flow] = seen = [];
                    if (seen is not null)
                    {
                        if (!seen.Add((row.TcpSequence, row.TcpPayloadLength)))
                        { _repeated++; signals = signals with { RepeatedSegments = signals.RepeatedSegments + 1 }; }
                        if (seen.Count > 256) { seen.Clear(); _limited = true; }
                    }
                    else _limited = true;
                }
                if (signals.Resets + signals.ZeroWindows + signals.RepeatedSegments > 0)
                {
                    if (_signals.Count < 4096 || _signals.ContainsKey(flow)) _signals[flow] = signals;
                    else _limited = true;
                }
            }
            if (row.ModbusKind.Length > 0)
            {
                _modbus++;
                if (row.ModbusKind == "Exception") _exceptions++;
                if (row.ModbusKind is "Response" or "Exception") AddBounded(_servers, row.Source);
                if (row.ModbusKind == "Request") AddBounded(_clients, row.SourceHost);
            }
        }
    }

    private void AddBounded(HashSet<string> set, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (set.Count < 4096 || set.Contains(value)) set.Add(value); else _limited = true;
    }

    public TrafficWindowSummary End()
    {
        lock (_gate)
        {
            _active = false; _clock.Stop();
            return new(Math.Min(_clock.Elapsed.TotalSeconds, _requestedSeconds ?? double.MaxValue), _packets, _bytes, _tcp, _resets, _zeros,
                _repeated, _modbus, _exceptions, _buckets.Count == 0 ? 0 : _buckets.Values.Max(x => x.Packets),
                _buckets.Count == 0 ? 0 : _buckets.Values.Max(x => x.Bytes), _limited,
                new Dictionary<string, long>(_protocols), new Dictionary<string, long>(_conversations),
                new HashSet<string>(_hosts), new HashSet<string>(_servers), new Dictionary<string, TcpSignalSummary>(_signals))
                { ObservedClients = new HashSet<string>(_clients) };
        }
    }
}
