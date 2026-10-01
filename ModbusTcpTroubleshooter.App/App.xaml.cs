using System.Windows;
using Serilog;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ModbusTcpTroubleshooter.App;

public partial class App : Application
{
    private bool _reportingFatalError;
    private string? _logDirectory;

    protected override void OnStartup(StartupEventArgs e)
    {
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(ManualHelp.HandleF1));
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            ReportFatalError(args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteFailure(args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject.ToString()));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Falha de tarefa em segundo plano");
            args.SetObserved();
        };
        try
        {
            _logDirectory = CreateLogDirectory();
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(Path.Combine(_logDirectory, "modbus-troubleshooter-.log"), rollingInterval: RollingInterval.Day)
                .CreateLogger();
            Log.Information("Inicializacao: OS {OS}; arquitetura {Architecture}; runtime {Runtime}; pasta {Directory}",
                RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture, RuntimeInformation.FrameworkDescription, AppContext.BaseDirectory);
            base.OnStartup(e);
            MainWindow = new MainWindow();
            MainWindow.Show();
            Log.Information("Janela principal aberta");
        }
        catch (Exception ex) { ReportFatalError(ex); }
    }

    private static string CreateLogDirectory()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.GetTempPath() })
        {
            try
            {
                var folder = Path.Combine(root, "ModbusTcpTroubleshooter", "logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "startup.log"), $"{DateTimeOffset.Now:O} Inicializando aplicativo\n");
                return folder;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        throw new IOException("Nao foi possivel criar uma pasta de logs no perfil do usuario ou TEMP.");
    }

    private string WriteFailure(Exception exception)
    {
        var details = BuildStartupErrorReport(exception);
        try
        {
            _logDirectory ??= CreateLogDirectory();
            var path = Path.Combine(_logDirectory, "startup-error.log");
            File.AppendAllText(path, details + Environment.NewLine);
            return path;
        }
        catch { return "Nao foi possivel gravar o log. Detalhe: " + exception.GetBaseException().Message; }
    }

    public static string BuildStartupErrorReport(Exception exception) =>
        $"{DateTimeOffset.Now:O}\nOS: {RuntimeInformation.OSDescription}\nArquitetura: {RuntimeInformation.ProcessArchitecture}\n.NET: {RuntimeInformation.FrameworkDescription}\nPasta: {AppContext.BaseDirectory}\n{exception}\n";

    private void ReportFatalError(Exception exception)
    {
        if (_reportingFatalError) return;
        _reportingFatalError = true;
        var path = WriteFailure(exception);
        Log.Fatal(exception, "Falha fatal do aplicativo");
        try
        {
            MessageBox.Show("Nao foi possivel iniciar ou continuar o aplicativo.\n\n" + exception.GetBaseException().Message
                + "\n\nDiagnostico: " + path, "Falha no Diagnostico Modbus TCP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { Shutdown(-1); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
