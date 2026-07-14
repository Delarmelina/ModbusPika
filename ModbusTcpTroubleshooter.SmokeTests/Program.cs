using System.Net;
using ModbusTcpTroubleshooter.Core;

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

await cts.CancelAsync();
server.Stop();

try
{
    await serverTask;
}
catch (OperationCanceledException)
{
}

Console.WriteLine("Smoke test OK: FC03 e FC06 funcionando contra servidor local.");
