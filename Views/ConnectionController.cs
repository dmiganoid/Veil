using System.Windows;
using Veil.Models;
using Veil.Services;

namespace Veil.Views;

/// <summary>
/// Connect, disconnect and reconnect actions shared by the pages and the tray menu.
/// <paramref name="showPage"/> brings the window to the front (it may be hidden in the tray) on the given page.
/// </summary>
public sealed class ConnectionController(AppServices services, Func<Window?> owner, Action<AppPage> showPage)
{
    public async Task ToggleAsync()
    {
        switch (services.Vpn.Status)
        {
            case VpnStatus.Connected:
                await DisconnectAsync();
                break;
            case VpnStatus.Disconnected:
                await ConnectAsync();
                break;
        }
    }

    public async Task ReconnectAsync()
    {
        if (services.Vpn.Status == VpnStatus.Connected)
        {
            await DisconnectAsync();
        }

        if (services.Vpn.Status == VpnStatus.Disconnected)
        {
            await ConnectAsync();
        }
    }

    private async Task ConnectAsync()
    {
        ServerConfig config;
        try
        {
            config = await services.Config.LoadConnectionConfigAsync();
        }
        catch (InvalidOperationException ex)
        {
            showPage(AppPage.Settings);
            AppMessageBox.Show(owner(), $"{ex.Message}\n\nFinish the setup on the Connection page.", "Connection settings are incomplete",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await services.Vpn.ConnectAsync(config);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(owner(), ex.Message, "Could not connect", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task DisconnectAsync()
    {
        try
        {
            await services.Vpn.DisconnectAsync();
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(owner(), ex.Message, "Could not disconnect", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
