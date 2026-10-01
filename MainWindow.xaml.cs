using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Veil.Dialogs;
using Veil.Models;
using Veil.Services;
using Veil.Ui;
using Veil.Views;

namespace Veil;

/// <summary>
/// Application shell: window chrome, navigation between pages, tray icon and exit handling.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly ConnectionController _connection;
    private readonly HomeView _homeView;
    private readonly RoutingView _routingView;
    private readonly SettingsView _settingsView;
    private readonly LogsView _logsView;
    private readonly ServerSetupView _serverSetupView;
    private TrayIcon? _tray;
    private AppPreferences _preferences = new();
    private string _serverName = "";
    private AppPage? _currentPage;
    private bool _syncingNavigation;
    private bool _allowClose;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        WindowEffects.ApplyWindowFrame(this);

        _connection = new ConnectionController(services, () => IsVisible ? this : null, page =>
        {
            BringToFront();
            _ = NavigateAsync(page);
        });
        _homeView = new HomeView(services, _connection.ToggleAsync, page => _ = NavigateAsync(page));
        _routingView = new RoutingView(services, _connection, page => _ = NavigateAsync(page));
        _settingsView = new SettingsView(services, _connection);
        _logsView = new LogsView(services);
        _serverSetupView = new ServerSetupView(services, page => _ = NavigateAsync(page));

        // The tray exists before the window is shown, because Veil can start hidden with --tray.
        _tray = new TrayIcon(LoadTrayIcon(), BringToFront, _connection.ToggleAsync, ExitApplicationAsync);
        _services.Vpn.StateChanged += (_, _) => this.OnUi(RefreshStatus);
        _services.Config.ConfigChanged += (_, _) => this.OnUi(async () => await RefreshServerNameAsync());
        Application.Current.SessionEnding += (_, _) => _allowClose = true;

        _ = NavigateAsync(AppPage.Home);
        _ = RefreshServerNameAsync();
    }

    public async Task NavigateAsync(AppPage page)
    {
        if (page == _currentPage)
        {
            SyncNavigationSelection();
            return;
        }

        if (_currentPage == AppPage.Settings && _settingsView.HasUnsavedChanges && !await ConfirmLeaveSettingsAsync())
        {
            SyncNavigationSelection();
            return;
        }

        if (_currentPage == AppPage.Server)
        {
            await _serverSetupView.SaveDraftAsync();
        }

        _currentPage = page;
        PageHost.Content = page switch
        {
            AppPage.Routing => _routingView,
            AppPage.Settings => _settingsView,
            AppPage.Logs => _logsView,
            AppPage.Server => _serverSetupView,
            _ => _homeView
        };
        SyncNavigationSelection();
    }

    private async Task<bool> ConfirmLeaveSettingsAsync()
    {
        var choice = AppMessageBox.ShowChoice(
            this,
            "You have unsaved changes on the Connection page.",
            "Save changes?",
            MessageBoxImage.Question,
            MessageBoxResult.Cancel,
            new DialogButton("Save", MessageBoxResult.Yes, IsDefault: true, IsPrimary: true),
            new DialogButton("Discard", MessageBoxResult.No),
            new DialogButton("Cancel", MessageBoxResult.Cancel, IsCancel: true));

        switch (choice)
        {
            case MessageBoxResult.Yes:
                return await _settingsView.SaveAsync();
            case MessageBoxResult.No:
                await _settingsView.DiscardChangesAsync();
                return true;
            default:
                return false;
        }
    }

    private void SyncNavigationSelection()
    {
        _syncingNavigation = true;
        NavList.SelectedItem = NavList.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item => item.Tag as string == _currentPage?.ToString());
        _syncingNavigation = false;
    }

    private async void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingNavigation ||
            NavList.SelectedItem is not ListBoxItem { Tag: string tag } ||
            !Enum.TryParse<AppPage>(tag, out var page))
        {
            return;
        }

        await NavigateAsync(page);
    }

    private async Task RefreshServerNameAsync()
    {
        var config = await _services.Config.LoadConfigAsync();
        _serverName = config.HasCredentials ? config.Hostname : "";
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var status = _services.Vpn.Status;
        SidebarStatusText.Text = status.DisplayText();
        SidebarStatusDot.Fill = (Brush)FindResource(status switch
        {
            VpnStatus.Connected => "SuccessBrush",
            VpnStatus.Connecting or VpnStatus.Disconnecting => "WarningBrush",
            _ => "SubtleBrush"
        });
        SidebarServerText.Text = string.IsNullOrWhiteSpace(_serverName) ? "No server configured" : _serverName;
        _tray?.Update(status, _serverName);
    }

    // Window chrome

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        // A maximized borderless window extends past the screen by the resize border; pad it back in.
        RootGrid.Margin = WindowState == WindowState.Maximized
            ? SystemParameters.WindowResizeBorderThickness
            : new Thickness(0);
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\xE923" : "\xE922";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        _preferences = await _services.Config.LoadPreferencesAsync();
        var action = _preferences.CloseAction;
        if (action == CloseAction.Ask)
        {
            var (choice, remember) = AppMessageBox.ShowChoiceWithOption(
                this,
                "Veil can keep running in the notification area so you can connect and disconnect from there. " +
                "Exiting also disconnects the VPN.",
                "Close Veil",
                MessageBoxImage.Question,
                MessageBoxResult.Cancel,
                "Don't ask again",
                new DialogButton("Keep running", MessageBoxResult.No, IsDefault: true, IsPrimary: true),
                new DialogButton("Exit Veil", MessageBoxResult.Yes),
                new DialogButton("Cancel", MessageBoxResult.Cancel, IsCancel: true));

            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            action = choice == MessageBoxResult.Yes ? CloseAction.Exit : CloseAction.MinimizeToTray;
            if (remember)
            {
                _preferences.CloseAction = action;
                await _services.Config.SavePreferencesAsync(_preferences);
            }
        }

        if (action == CloseAction.Exit)
        {
            await ExitApplicationAsync();
            return;
        }

        await _serverSetupView.SaveDraftAsync();
        Hide();
        _tray?.ShowBackgroundHint();
    }

    /// <summary>Shows the window from the tray (or another launch of Veil) and activates it.</summary>
    internal void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();

        // Windows only lets the foreground process steal focus; a topmost toggle brings us up anyway.
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private async Task ExitApplicationAsync()
    {
        if (_settingsView.HasUnsavedChanges)
        {
            BringToFront();
            await NavigateAsync(AppPage.Settings);
            if (AppMessageBox.Show(this, "Save the changes on the Connection page before exiting?", "Unsaved changes",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes &&
                !await _settingsView.SaveAsync())
            {
                return;
            }
        }

        _allowClose = true;
        try
        {
            await _serverSetupView.SaveDraftAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The server setup draft is a convenience; never let it block exiting.
        }

        try
        {
            await _services.Vpn.ShutdownAsync();
        }
        finally
        {
            _tray?.Dispose();
            _tray = null;
            Application.Current.Shutdown();
        }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray_icon.ico");
        if (!File.Exists(iconPath))
        {
            return System.Drawing.SystemIcons.Application;
        }

        // SmallIconSize already accounts for the display scale.
        var size = System.Windows.Forms.SystemInformation.SmallIconSize;
        return new System.Drawing.Icon(iconPath, size);
    }
}
