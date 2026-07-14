using System.Windows;
using Serilog;

namespace ModbusTcpTroubleshooter.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File("logs/modbus-troubleshooter-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
