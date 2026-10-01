using System.Threading;
using System.Windows;
using Veil.Services;

namespace Veil;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Global\Veil.SingleInstance";
    private const string ActivationEventName = @"Local\Veil.ShowWindow";

    private SingleInstanceGuard? _singleInstanceGuard;
    private VpnService? _vpnService;
    private ServerSetupService? _serverSetupService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceGuard = SingleInstanceGuard.Acquire(SingleInstanceMutexName);
        if (!_singleInstanceGuard.OwnsMutex)
        {
            // Veil usually lives in the tray; opening it again should bring the running copy forward.
            if (!SignalRunningInstance())
            {
                AppMessageBox.Show(
                    "Veil is already running.\nLook for its icon in the system tray.",
                    "Veil",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        // UI event handlers are async void; report their failures instead of letting one crash the app
        // while the VPN (and possibly the system proxy) is active.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            AppMessageBox.Show($"Something went wrong: {args.Exception.Message}", "Veil", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        var configService = new ConfigService();
        _vpnService = new VpnService(configService);
        _serverSetupService = new ServerSetupService();
        AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

        var services = new AppServices(
            configService,
            _vpnService,
            _serverSetupService,
            new DomainDiscoveryService(),
            new InstalledAppService(),
            new SplitTunnelSuggestionService());

        var window = new MainWindow(services);
        MainWindow = window;
        ListenForActivation(window);
        _ = LegacyTvGatewayCleanup.RunOnceAsync(configService);

        // Started at sign-in by the installer's scheduled task: stay in the tray until opened.
        if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
        {
            window.Show();
        }
    }

    private static bool SignalRunningInstance()
    {
        try
        {
            using var activation = EventWaitHandle.OpenExisting(ActivationEventName);
            return activation.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private void ListenForActivation(MainWindow window)
    {
        var activation = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        var listener = new Thread(() =>
        {
            while (activation.WaitOne())
            {
                Dispatcher.BeginInvoke(window.BringToFront);
            }
        })
        {
            IsBackground = true,
            Name = "Veil activation listener"
        };
        listener.Start();
    }

    private void CurrentDomain_ProcessExit(object? sender, EventArgs e) =>
        CleanupApplicationServices(_vpnService, _serverSetupService, _singleInstanceGuard);

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_vpnService != null)
        {
            await _vpnService.ShutdownAsync();
        }

        CleanupApplicationServices(_vpnService, _serverSetupService, _singleInstanceGuard);
        base.OnExit(e);
    }

    internal static void CleanupApplicationServices(
        VpnService? vpnService,
        ServerSetupService? serverSetupService,
        SingleInstanceGuard? singleInstanceGuard)
    {
        TryRun(() => vpnService?.Dispose());
        TryRun(() => serverSetupService?.Dispose());
        TryRun(() => singleInstanceGuard?.Dispose());
    }

    private static void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Shutdown cleanup is best effort; each service keeps its own recovery state.
        }
    }
}
