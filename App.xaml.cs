using Microsoft.Extensions.DependencyInjection;
using Launcher.Services;
using Launcher.ViewModels;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace Launcher;

public partial class App : Application
{
    private const string AppGuid = "Launcher-Unique-Mutex-Guid-12345";
    private static Mutex? _mutex;
    private ServiceProvider? _serviceProvider;
    private CancellationTokenSource? _pipeCts;
    private MainWindow? _mainWindow;
    private bool _controllerStarted;
    private bool _ownsMutex;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var startInBackground = e.Args.Any(arg =>
            arg.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("/background", StringComparison.OrdinalIgnoreCase));

        _mutex = new Mutex(true, AppGuid, out bool createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            if (!startInBackground)
            {
                SignalFirstInstance();
            }
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
        if (!startInBackground)
        {
            ShowMainWindow();
        }
        else
        {
            PreloadMainWindowInBackground();
        }
    }

    private void ShowMainWindow()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        _mainWindow ??= _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = _mainWindow;
        _mainWindow.Opacity = 1;
        _mainWindow.ShowActivated = true;
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.Show();
        _mainWindow.BringToFront();

        StartControllerWhenWindowReady();
    }

    private void PreloadMainWindowInBackground()
    {
        Task.Run(async () =>
        {
            try
            {
                await Dispatcher.InvokeAsync(PreloadMainWindow, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Preloading is an optimization; normal show still works if it fails.
            }
        });
    }

    private void PreloadMainWindow()
    {
        if (_serviceProvider is null || _mainWindow is not null)
        {
            return;
        }

        _mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = _mainWindow;

        _mainWindow.ShowActivated = false;
        _mainWindow.ShowInTaskbar = false;
        _mainWindow.Opacity = 0;
        _mainWindow.Show();
        _mainWindow.Hide();
        _mainWindow.Opacity = 1;
        _mainWindow.ShowActivated = true;
        _mainWindow.ShowInTaskbar = true;

        StartControllerWhenWindowReady();
    }

    private void StartControllerWhenWindowReady()
    {
        var mainWindow = _mainWindow;
        var serviceProvider = _serviceProvider;
        if (_controllerStarted || mainWindow is null || serviceProvider is null)
        {
            return;
        }

        _controllerStarted = true;
        mainWindow.Dispatcher.BeginInvoke(new Action(() =>
        {
            serviceProvider.GetRequiredService<IControllerService>().Start();
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
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
                    using var server = new NamedPipeServerStream(
                        AppGuid,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(_pipeCts.Token);
                    using var reader = new StreamReader(server);
                    var message = await reader.ReadLineAsync();

                    if (message == "show")
                    {
                        _ = Dispatcher.BeginInvoke(new Action(ShowMainWindow));
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
        UserDialog.ShowError(e.Exception.Message);
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
            AppPaths.EnsureDataDirectories();
            File.AppendAllText(
                AppPaths.ErrorLogPath,
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
        _mainWindow?.DisposeTrayIcon();
        _serviceProvider?.Dispose();
        if (_ownsMutex)
        {
            _mutex?.ReleaseMutex();
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
