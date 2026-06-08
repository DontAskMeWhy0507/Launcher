using Microsoft.Extensions.DependencyInjection;
using RevoltLauncher.Services;
using RevoltLauncher.ViewModels;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace RevoltLauncher;

public partial class App : Application
{
    private const string AppGuid = "RevoltLauncher-Unique-Mutex-Guid-12345";
    private static Mutex? _mutex;
    private ServiceProvider? _serviceProvider;
    private CancellationTokenSource? _pipeCts;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _mutex = new Mutex(true, AppGuid, out bool createdNew);

        if (!createdNew)
        {
            SignalFirstInstance();
            Shutdown();
            return;
        }

        StartNamedPipeServer();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILauncherService, LauncherService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IControllerService, ControllerService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
        var controllerService = _serviceProvider.GetRequiredService<IControllerService>();
        controllerService.Start();

        _serviceProvider.GetRequiredService<MainWindow>().Show();
    }

    private void StartNamedPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (!_pipeCts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(AppGuid, PipeDirection.In);
                    await server.WaitForConnectionAsync(_pipeCts.Token);
                    using var reader = new StreamReader(server);
                    var message = await reader.ReadLineAsync();

                    if (message == "show")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            var mainWindow = MainWindow as MainWindow;
                            if (mainWindow == null)
                            {
                                mainWindow = _serviceProvider?.GetService<MainWindow>();
                                mainWindow?.Show();
                            }
                            mainWindow?.BringToFront();
                        });
                    }
                }
                catch (OperationCanceledException) { }
                catch { /* Ignore pipe errors */ }
            }
        });
    }

    private void SignalFirstInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", AppGuid, PipeDirection.Out);
            client.Connect(500);
            using var writer = new StreamWriter(client);
            writer.WriteLine("show");
            writer.Flush();
        }
        catch { /* First instance might be closing */ }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception);
        MessageBox.Show(e.Exception.Message, "Launcher error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogException(exception);
        }
    }

    private static void LogException(Exception exception)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "launcher-error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\n\n");
        }
        catch
        {
            // Last-resort logging must never crash startup.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts?.Cancel();
        _serviceProvider?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
