using System;
using System.Windows;
using Veil.Services;

namespace Veil;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _singleInstanceGuard;
    private VpnService? _vpnService;
    private ServerSetupService? _serverSetupService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceGuard = SingleInstanceGuard.Acquire(@"Global\Veil.SingleInstance");
        if (!_singleInstanceGuard.OwnsMutex)
        {
            AppMessageBox.Show(
                "Veil is already running.\nCheck the system tray or taskbar.",
                "Veil",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var configService = new ConfigService();
        _vpnService = new VpnService(configService);
        _serverSetupService = new ServerSetupService();
        AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

        var window = new MainWindow(
            configService,
            _vpnService,
            _serverSetupService,
            new DomainDiscoveryService(),
            new InstalledAppService(),
            new SplitTunnelSuggestionService());
        MainWindow = window;
        window.Show();
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
        try
        {
            vpnService?.Dispose();
        }
        catch
        {
        }

        try
        {
            serverSetupService?.Dispose();
        }
        catch
        {
        }

        try
        {
            singleInstanceGuard?.Dispose();
        }
        catch
        {
        }
    }
}
