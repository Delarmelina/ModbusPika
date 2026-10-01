using System.Net;
using System.Net.Sockets;
using ModbusTcpTroubleshooter.Core;

var classifierRequest = ModbusProtocol.BuildReadRequest(1, 1, 3, 0, 1);
if (ModbusFrameClassifier.Classify(classifierRequest) != "Request"
    || ModbusFrameClassifier.Classify(Array.Empty<byte>()) != ""
    || ModbusFrameClassifier.Classify(new byte[] { 0, 1, 0, 0, 0, 5, 1, 3, 2, 0x12, 0x34 }) != "Response"
    || ModbusFrameClassifier.Classify(new byte[] { 0, 1, 0, 0, 0, 3, 1, 0x83, 2 }) != "Exception"
    || ModbusFrameClassifier.Classify(classifierRequest.AsSpan(0, 8)) != "")
    throw new InvalidOperationException("Complete Modbus ADU classification failed.");

var map = new ModbusDataMap();
map.LoadDefaults();

var server = new ModbusTcpServer(map);
using var cts = new CancellationTokenSource();
var serverTask = server.StartAsync(IPAddress.Loopback, 1502, cts.Token);

await Task.Delay(250);

var client = new ModbusTcpClientProbe();
var initial = await client.ReadRegistersAsync("127.0.0.1", 1502, 1, ModbusProtocol.ReadHoldingRegisters, 0, 2, CancellationToken.None);

if (initial.Count != 2 || initial[0] != 1000 || initial[1] != 1001)
{
    throw new InvalidOperationException($"Leitura inicial inesperada: {string.Join(",", initial)}");
}

await client.WriteSingleRegisterAsync("127.0.0.1", 1502, 1, 0, 4321, CancellationToken.None);
var afterWrite = await client.ReadRegistersAsync("127.0.0.1", 1502, 1, ModbusProtocol.ReadHoldingRegisters, 0, 1, CancellationToken.None);

if (afterWrite.Count != 1 || afterWrite[0] != 4321)
{
    throw new InvalidOperationException($"Escrita FC06 nao refletiu no mapa: {string.Join(",", afterWrite)}");
}

await client.WriteSingleCoilAsync("127.0.0.1", 1502, 1, 0, false, CancellationToken.None);
var coilAfterWrite = await client.ReadBitsAsync("127.0.0.1", 1502, 1, ModbusProtocol.ReadCoils, 0, 1, CancellationToken.None);

if (coilAfterWrite.Count != 1 || coilAfterWrite[0])
{
    throw new InvalidOperationException($"Escrita FC05 nao refletiu no mapa: {string.Join(",", coilAfterWrite.Select(x => x ? "1" : "0"))}");
}

map.AddPoint(ModbusPointType.HoldingRegister, 10, 1010, writable: false);
await ExpectRejectedAsync(() => client.WriteSingleRegisterAsync("127.0.0.1", 1502, 1, 10, 9, CancellationToken.None), "read-only write");
await ExpectRejectedAsync(() => client.WriteSingleCoilAsync("127.0.0.1", 1502, 1, 100, true, CancellationToken.None), "out-of-map write");
if (map.Coils.ContainsKey(100) || map.HoldingRegisters[10] != 1010)
    throw new Exception("Rejected write modified the map.");

await cts.CancelAsync();
server.Stop();

try
{
    await serverTask;
}
catch (OperationCanceledException)
{
}

foreach (var scenario in new[] { "tid", "uid", "fc", "byte-count", "length", "write-echo" })
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var testPort = ((IPEndPoint)listener.LocalEndpoint).Port;
    var respond = Task.Run(async () =>
    {
        using var socket = await listener.AcceptTcpClientAsync();
        var stream = socket.GetStream();
        var request = new byte[12];
        await stream.ReadExactlyAsync(request);
        var frame = ModbusProtocol.Parse(request);
        var response = scenario == "write-echo" ? request.ToArray() : ModbusProtocol.BuildReadRegistersResponse(frame, new ushort[] { 1000 });
        switch (scenario)
        {
            case "tid": response[1] ^= 1; break;
            case "uid": response[6] ^= 1; break;
            case "fc": response[7] = 4; break;
            case "byte-count": response[8] = 1; break;
            case "length": response[4] = 1; response[5] = 0; break;
            case "write-echo": response[11] ^= 1; break;
        }
        await stream.WriteAsync(response);
    });
    await ExpectRejectedAsync(async () =>
    {
        if (scenario == "write-echo") await client.WriteSingleRegisterAsync("127.0.0.1", testPort, 1, 0, 7, CancellationToken.None);
        else await client.ReadRegistersAsync("127.0.0.1", testPort, 1, 3, 0, 1, CancellationToken.None);
    }, scenario);
    await respond;
}

Console.WriteLine("Smoke tests OK: FC01/03/05/06, read-only/out-of-map writes, TID/UID/FC, byte count, MBAP length and write echo.");

static async Task ExpectRejectedAsync(Func<Task> action, string scenario)
{
    try { await action(); }
    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { return; }
    throw new Exception($"Invalid transaction accepted: {scenario}");
}
