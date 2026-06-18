using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Veil.Models;
using Veil.Services;
using Forms = System.Windows.Forms;

namespace Veil;

public partial class MainWindow : Window
{
    internal const int GeneratedVpnPasswordLength = 16;
    internal const string GeneratedVpnPasswordAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#%^&*";

    private readonly ConfigService _configService;
    private readonly VpnService _vpnService;
    private readonly ServerSetupService _serverSetupService;
    private readonly DomainDiscoveryService _domainDiscoveryService;
    private readonly InstalledAppService _installedAppService;
    private readonly SplitTunnelSuggestionService _suggestionService;

    private readonly HashSet<string> _selectedApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedCountries = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _suggestions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource?> _appIconCache = new(StringComparer.OrdinalIgnoreCase);

    private ServerConfig _currentConfig = ServerConfig.DefaultConfig();
    private DomainGroupsData _domainGroups = new();
    private List<InstalledApp> _installedApps = [];
    private Forms.ContextMenuStrip? _trayMenu;
    private string? _trayConnectionMenuLabel;
    private Forms.NotifyIcon? _notifyIcon;
    private bool _allowClose;
    private bool _isLoading;
    private bool _passwordVisible;
    private bool _serverSshPasswordVisible;
    private bool _serverVpnPasswordVisible;
    private bool _updatingSplitSectionToggles;

    public MainWindow(
        ConfigService configService,
        VpnService vpnService,
        ServerSetupService serverSetupService,
        DomainDiscoveryService domainDiscoveryService,
        InstalledAppService installedAppService,
        SplitTunnelSuggestionService suggestionService)
    {
        _configService = configService;
        _vpnService = vpnService;
        _serverSetupService = serverSetupService;
        _domainDiscoveryService = domainDiscoveryService;
        _installedAppService = installedAppService;
        _suggestionService = suggestionService;

        InitializeComponent();
        Opacity = 0;

        Loaded += MainWindow_Loaded;
        _vpnService.StateChanged += (_, _) => DispatchUi(RefreshVpnState);
        _vpnService.LogAdded += line => DispatchUi(() =>
        {
            CollectSuggestionsFromLog(line);
            RefreshLogs();
        });
        _serverSetupService.StateChanged += (_, _) => DispatchUi(RefreshServerState);
    }

    private void DispatchUi(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = Dispatcher.InvokeAsync(action);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowClip();
        BeginWindowIntroAnimation();
        SetupTray();
        ServerUseKeyCheckBox_Changed(this, new RoutedEventArgs());
        await LoadConfigAsync();
        await LoadServerSetupConfigAsync();
        _ = LoadInstalledAppsAsync();
        RefreshVpnState();
        RefreshLogs();
        RefreshServerState();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyWindowClip();

    private void ApplyWindowClip()
    {
        if (RootShell == null)
        {
            return;
        }

        var width = RootShell.ActualWidth > 0 ? RootShell.ActualWidth : ActualWidth;
        var height = RootShell.ActualHeight > 0 ? RootShell.ActualHeight : ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        RootShell.Clip = new RectangleGeometry(
            new Rect(0, 0, width, height),
            radiusX: 30,
            radiusY: 30);
    }

    private void SetupTray()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Veil",
            Visible = true
        };

        _notifyIcon.Icon = LoadTrayIcon();

        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                ShowFromTray();
            }
        };

        UpdateTrayMenu();
    }

    private System.Drawing.Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray_icon.ico");
        if (!File.Exists(iconPath))
        {
            return System.Drawing.SystemIcons.Application;
        }

        return new System.Drawing.Icon(iconPath, GetTrayIconSize(), GetTrayIconSize());
    }

    private int GetTrayIconSize()
    {
        try
        {
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return scale <= 1.0 ? 16
                : scale <= 1.25 ? 20
                : scale <= 1.5 ? 24
                : scale <= 2.0 ? 32
                : 48;
        }
        catch
        {
            return Forms.SystemInformation.SmallIconSize.Width;
        }
    }

    private void UpdateTrayMenu()
    {
        if (_notifyIcon == null)
        {
            return;
        }

        var connectionLabel = _vpnService.Status == VpnStatus.Connected ? "Disconnect" : "Connect";
        if (_trayMenu != null && string.Equals(_trayConnectionMenuLabel, connectionLabel, StringComparison.Ordinal))
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show Window", null, (_, _) => ShowFromTray());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(connectionLabel, null, async (_, _) => await ToggleConnectionAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitApplicationAsync());

        var previousMenu = _trayMenu;
        _trayMenu = menu;
        _trayConnectionMenuLabel = connectionLabel;
        _notifyIcon.ContextMenuStrip = menu;
        previousMenu?.Dispose();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async Task LoadConfigAsync()
    {
        _isLoading = true;
        _currentConfig = await _configService.LoadConfigAsync();
        _domainGroups = await _configService.MigrateFlatDomainsToGroupsAsync();
        _selectedApps.Clear();
        foreach (var app in _currentConfig.SplitTunnelApps)
        {
            _selectedApps.Add(app);
        }

        _selectedCountries.Clear();
        foreach (var country in _currentConfig.SplitTunnelCountries)
        {
            _selectedCountries.Add(country);
        }

        FillSettingsFromConfig();
        RefreshSplitView(allowDuringLoading: true);
        _isLoading = false;
    }

    private void FillSettingsFromConfig()
    {
        HostnameTextBox.Text = _currentConfig.Hostname;
        AddressTextBox.Text = _currentConfig.Address;
        PortTextBox.Text = _currentConfig.Port.ToString();
        UsernameTextBox.Text = _currentConfig.Username;
        SetSettingsPassword(_currentConfig.Password);
        DnsTextBox.Text = _currentConfig.Dns;
        SetComboValue(ProtocolComboBox, _currentConfig.UpstreamProtocol);
        SetComboValue(LogLevelComboBox, _currentConfig.LogLevel);
        Ipv6CheckBox.IsChecked = _currentConfig.HasIpv6;
        SkipVerificationCheckBox.IsChecked = _currentConfig.SkipVerification;
        AntiDpiCheckBox.IsChecked = _currentConfig.AntiDpi;
        PostQuantumCheckBox.IsChecked = _currentConfig.PostQuantumGroupEnabled;
        CustomSniTextBox.Text = _currentConfig.CustomSni;
        RefreshHomeConfigSummary();
    }

    private ServerConfig BuildConfigFromUi()
    {
        var config = BuildConfigDraftFromUi();
        config.ValidateRequiredClientFields();
        return config;
    }

    private ServerConfig BuildConfigDraftFromUi()
    {
        return BuildConfigDraftFromUi(ParseClientPort(PortTextBox.Text));
    }

    private ServerConfig BuildConfigDraftFromUi(int port)
    {
        return CreateConfigDraft(
            _currentConfig,
            HostnameTextBox.Text,
            AddressTextBox.Text,
            port,
            UsernameTextBox.Text,
            GetSettingsPassword(),
            DnsTextBox.Text,
            GetComboValue(ProtocolComboBox, "http2"),
            GetComboValue(LogLevelComboBox, "info"),
            Ipv6CheckBox.IsChecked == true,
            SkipVerificationCheckBox.IsChecked == true,
            AntiDpiCheckBox.IsChecked == true,
            PostQuantumCheckBox.IsChecked == true,
            CustomSniTextBox.Text,
            GetSelectedVpnMode(),
            _domainGroups.FlattenDomains(),
            _selectedApps,
            _selectedCountries);
    }

    internal static ServerConfig CreateConfigDraft(
        ServerConfig currentConfig,
        string hostname,
        string address,
        int port,
        string username,
        string password,
        string dns,
        string upstreamProtocol,
        string logLevel,
        bool hasIpv6,
        bool skipVerification,
        bool antiDpi,
        bool postQuantumGroupEnabled,
        string customSni,
        VpnMode vpnMode,
        IEnumerable<string> splitTunnelDomains,
        IEnumerable<string> selectedApps,
        IEnumerable<string>? selectedCountries = null)
    {
        _ = currentConfig ?? throw new ArgumentNullException(nameof(currentConfig));

        return new ServerConfig
        {
            Hostname = hostname.Trim(),
            Address = address.Trim(),
            Port = port,
            Username = username.Trim(),
            Password = password,
            Dns = dns.Trim(),
            UpstreamProtocol = upstreamProtocol,
            LogLevel = logLevel,
            HasIpv6 = hasIpv6,
            SkipVerification = skipVerification,
            AntiDpi = antiDpi,
            PostQuantumGroupEnabled = postQuantumGroupEnabled,
            CustomSni = customSni.Trim(),
            VpnMode = vpnMode,
            SplitTunnelDomains = splitTunnelDomains.ToList(),
            SplitTunnelApps = selectedApps.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            SplitTunnelCountries = ConfigService.NormalizeSplitTunnelCountries(selectedCountries ?? currentConfig.SplitTunnelCountries)
        };
    }

    internal static int ParseClientPort(string value)
    {
        if (!int.TryParse(value.Trim(), out var port) || !ServerConfig.IsValidPort(port))
        {
            throw new InvalidOperationException("Invalid port.");
        }

        return port;
    }

    private async Task SaveConfigAsync(bool showMessage = false)
    {
        var config = BuildConfigFromUi();
        await _configService.SaveConfigAsync(config);
        await _configService.SaveDomainGroupsAsync(_domainGroups);
        _currentConfig = config;
        RefreshHomeConfigSummary();
        RefreshSplitView();

        if (showMessage)
        {
            AppMessageBox.Show(this, "Settings saved.", "Veil", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task SaveSplitTunnelConfigAsync()
    {
        _currentConfig = await _configService.SaveSplitTunnelStateAsync(
            _domainGroups,
            _currentConfig.VpnMode,
            _selectedApps,
            _selectedCountries);
        RefreshSplitView();
    }

    private async Task LoadServerSetupConfigAsync()
    {
        var config = await _configService.LoadServerSetupConfigAsync();
        FillServerSetupFieldsFromConfig(config, includePasswords: false);
        UpdateServerAuthControls();
    }

    private void FillServerSetupFieldsFromConfig(ServerSetupConfig config, bool includePasswords)
    {
        ServerHostTextBox.Text = config.Host;
        ServerSshPortTextBox.Text = config.SshPort.ToString();
        ServerSshUserTextBox.Text = config.SshUsername;
        if (includePasswords)
        {
            SetServerSshPassword(config.SshPassword);
        }
        else
        {
            SetServerSshPassword("");
        }

        ServerSshKeyPathTextBox.Text = config.SshKeyPath ?? "";
        ServerUseKeyCheckBox.IsChecked = config.UseKeyAuth;
        ServerDomainTextBox.Text = config.Domain;
        ServerEmailTextBox.Text = config.Email;
        ServerListenPortTextBox.Text = config.ListenPort.ToString();
        ServerVpnUsernameTextBox.Text = config.VpnUsername;
        if (includePasswords)
        {
            SetServerVpnPassword(config.VpnPassword);
        }
        else
        {
            SetServerVpnPassword("");
        }
    }

    private async Task SaveServerSetupDraftAsync()
    {
        await _configService.SaveServerSetupConfigAsync(BuildServerSetupDraftFromUi());
    }

    private ServerSetupConfig BuildServerSetupDraftFromUi()
    {
        _ = int.TryParse(ServerSshPortTextBox.Text.Trim(), out var sshPort);
        _ = int.TryParse(ServerListenPortTextBox.Text.Trim(), out var listenPort);

        return new ServerSetupConfig
        {
            Host = ServerHostTextBox.Text.Trim(),
            SshPort = sshPort is > 0 and <= 65535 ? sshPort : 22,
            SshUsername = string.IsNullOrWhiteSpace(ServerSshUserTextBox.Text) ? "root" : ServerSshUserTextBox.Text.Trim(),
            SshKeyPath = ServerSshKeyPathTextBox.Text.Trim(),
            UseKeyAuth = ServerUseKeyCheckBox.IsChecked == true,
            Domain = ServerDomainTextBox.Text.Trim(),
            Email = ServerEmailTextBox.Text.Trim(),
            ListenPort = listenPort is > 0 and <= 65535 ? listenPort : 443,
            VpnUsername = ServerVpnUsernameTextBox.Text.Trim()
        };
    }

    private void RefreshVpnState()
    {
        StatusText.Text = _vpnService.Status.DisplayText();
        ConnectButton.IsEnabled = _vpnService.Status is VpnStatus.Disconnected or VpnStatus.Connected;
        ConnectButton.ToolTip = _vpnService.Status == VpnStatus.Connected ? "Disconnect" : _vpnService.Status.IsActive() ? "Please wait..." : "Connect";
        RefreshConnectOrbState();

        HomeErrorText.Text = _vpnService.ErrorMessage ?? "";
        HomeErrorText.Visibility = string.IsNullOrWhiteSpace(_vpnService.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
        StatusDetailText.Text = _vpnService.Status switch
        {
            VpnStatus.Connected => $"Connected to {_currentConfig.Hostname}.",
            VpnStatus.Connecting => "Starting Veil client...",
            VpnStatus.Disconnecting => "Stopping Veil client...",
            _ => "Configure the server settings, then connect."
        };

        var connected = _vpnService.Status.IsActive();
        SettingsWarningText.Text = connected ? "Disconnect from VPN before changing settings." : "";
        SettingsWarningBanner.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        SettingsNoticeText.Text = connected
            ? "Locked while Veil is active. Disconnect to edit."
            : "Editable now. Save, then reconnect Veil.";
        SettingsNoticeBorder.Background = (Brush)FindResource(connected ? "ActiveLockBackgroundBrush" : "ConfigStateBackgroundBrush");
        SettingsNoticeBorder.BorderBrush = (Brush)FindResource(connected ? "ActiveLockBorderBrush" : "ConfigStateBorderBrush");
        SettingsNoticeText.Foreground = (Brush)FindResource(connected ? "WarningBrush" : "MutedBrush");
        SplitWarningText.Text = connected ? "Disconnect from VPN before changing split tunnel settings." : "Settings are saved automatically.";
        HomeHintText.Text = _vpnService.Status switch
        {
            VpnStatus.Connected => "Private tunnel is active. Tap the Veil orb to disconnect.",
            VpnStatus.Connecting => "Starting the private tunnel. Settings are temporarily locked.",
            VpnStatus.Disconnecting => "Stopping the tunnel and releasing the Wintun adapter.",
            _ => "Ready to connect. Tap the Veil orb to start the private tunnel."
        };
        SetConfigurationEditingEnabled(!connected);
        RefreshHomeConfigSummary();
        RefreshSuggestions();
        RefreshAppsList();
        RefreshGeoIpCountriesList();
        UpdateTrayMenu();
    }

    private void RefreshConnectOrbState()
    {
        if (ConnectOrbStateText == null)
        {
            return;
        }

        var muted = (Brush)FindResource("MutedBrush");
        var success = (Brush)FindResource("SuccessBrush");
        var warning = (Brush)FindResource("WarningBrush");
        var violet = (Brush)FindResource("VioletBrush");
        var strong = (Brush)FindResource("BorderBrushStrong");

        switch (_vpnService.Status)
        {
            case VpnStatus.Connected:
                ConnectOrbStateText.Text = "ONLINE";
                ConnectOrbStateText.Foreground = success;
                ConnectOrbStateBadge.Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x07, 0x2B, 0x25));
                ConnectOrbStateBadge.BorderBrush = success;
                ConnectOrbSurface.BorderBrush = success;
                ConnectOrbSurface.Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x05, 0x1F, 0x20));
                ConnectOrbRing.Stroke = success;
                ConnectOrbRing.Opacity = 0.78;
                break;
            case VpnStatus.Connecting:
                ConnectOrbStateText.Text = "CONNECTING";
                ConnectOrbStateText.Foreground = warning;
                ConnectOrbStateBadge.Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x2A, 0x22, 0x16));
                ConnectOrbStateBadge.BorderBrush = warning;
                ConnectOrbSurface.BorderBrush = warning;
                ConnectOrbSurface.Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x16, 0x16, 0x28));
                ConnectOrbRing.Stroke = warning;
                ConnectOrbRing.Opacity = 0.74;
                break;
            case VpnStatus.Disconnecting:
                ConnectOrbStateText.Text = "STOPPING";
                ConnectOrbStateText.Foreground = warning;
                ConnectOrbStateBadge.Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x2A, 0x22, 0x16));
                ConnectOrbStateBadge.BorderBrush = warning;
                ConnectOrbSurface.BorderBrush = warning;
                ConnectOrbSurface.Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x16, 0x16, 0x28));
                ConnectOrbRing.Stroke = warning;
                ConnectOrbRing.Opacity = 0.74;
                break;
            default:
                ConnectOrbStateText.Text = "OFFLINE";
                ConnectOrbStateText.Foreground = muted;
                ConnectOrbStateBadge.Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x0B, 0x15, 0x28));
                ConnectOrbStateBadge.BorderBrush = strong;
                ConnectOrbSurface.BorderBrush = strong;
                ConnectOrbSurface.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x09, 0x13, 0x26));
                ConnectOrbRing.Stroke = violet;
                ConnectOrbRing.Opacity = 0.62;
                break;
        }
    }

    private void RefreshHomeConfigSummary()
    {
        var hostname = string.IsNullOrWhiteSpace(_currentConfig.Hostname) ? "Not configured" : _currentConfig.Hostname;
        var address = string.IsNullOrWhiteSpace(_currentConfig.Address) ? "Open Settings" : _currentConfig.Address;
        var protocol = string.IsNullOrWhiteSpace(_currentConfig.UpstreamProtocol) ? "-" : _currentConfig.UpstreamProtocol.ToUpperInvariant();
        var port = _currentConfig.Port > 0 ? _currentConfig.Port.ToString() : "-";
        var mode = _currentConfig.VpnMode == VpnMode.General ? "General" : "Selective";
        var domainsCount = _domainGroups.FlattenDomains().Count();
        var appsCount = _selectedApps.Count;
        var countriesCount = _selectedCountries.Count;
        var exclusionsCount = domainsCount + appsCount + countriesCount;
        var splitSummary = _currentConfig.VpnMode == VpnMode.General
            ? exclusionsCount == 0 ? "All traffic via VPN" : $"{exclusionsCount} exclusions"
            : $"{domainsCount} domains, {appsCount} apps, {countriesCount} countries";

        HomeServerSummaryText.Text = hostname;
        HomeAddressSummaryText.Text = address;
        HomeModeSummaryText.Text = mode;
        HomeSplitSummaryText.Text = splitSummary;
        HomeProtocolSummaryText.Text = protocol;
        HomePortSummaryText.Text = $"Port {port}";
        TopProtocolText.Text = protocol == "-" ? "PROTOCOL" : protocol;
        TopDnsText.Text = string.IsNullOrWhiteSpace(_currentConfig.Dns) ? "DNS AUTO" : "DNS READY";
        TopRouteText.Text = mode.ToUpperInvariant();
        HomeHostnameValueText.Text = hostname;
        HomeAddressValueText.Text = address;
        HomePortValueText.Text = port;
        HomeProtocolValueText.Text = protocol;
        HomeIpv6ValueText.Text = _currentConfig.HasIpv6 ? "enabled" : "disabled";
        HomeAntiDpiValueText.Text = _currentConfig.AntiDpi ? "active" : "off";
        HomeDnsValueText.Text = string.IsNullOrWhiteSpace(_currentConfig.Dns) ? "-" : _currentConfig.Dns;
        HomePostQuantumValueText.Text = _currentConfig.PostQuantumGroupEnabled ? "hybrid" : "off";
        SetProtectionTileState(HomeIpv6Tile, _currentConfig.HasIpv6, "success");
        SetProtectionTileState(HomeAntiDpiTile, _currentConfig.AntiDpi, "success");
        SetProtectionTileState(HomeDnsTile, !string.IsNullOrWhiteSpace(_currentConfig.Dns), "blue");
        SetProtectionTileState(HomePostQuantumTile, _currentConfig.PostQuantumGroupEnabled, "warning");
    }

    private static void SetProtectionTileState(Border tile, bool enabled, string accent)
    {
        if (!enabled)
        {
            tile.Background = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
            tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xDC, 0xEB, 0xFF));
            tile.Opacity = 0.72;
            return;
        }

        tile.Opacity = 1;
        (byte r, byte g, byte b) = accent switch
        {
            "blue" => ((byte)0x56, (byte)0xD9, (byte)0xFF),
            "warning" => ((byte)0xFF, (byte)0xD4, (byte)0x6B),
            _ => ((byte)0x5F, (byte)0xF1, (byte)0xB7)
        };
        tile.Background = new SolidColorBrush(Color.FromArgb(0x18, r, g, b));
        tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, r, g, b));
    }

    private void RefreshLogs()
    {
        LogsTextBox.Text = string.Join(Environment.NewLine, _vpnService.Logs);
        LogsCountText.Text = $"Total entries: {_vpnService.Logs.Count}";
        var hasLogs = _vpnService.Logs.Count > 0;
        CopyLogsButton.IsEnabled = hasLogs;
        ClearLogsButton.IsEnabled = hasLogs;
        LogsEmptyState.Visibility = hasLogs ? Visibility.Collapsed : Visibility.Visible;
        if (AutoScrollCheckBox.IsChecked == true)
        {
            LogsTextBox.ScrollToEnd();
        }
    }

    private bool CanEditConfiguration() => !_vpnService.Status.IsActive();

    private void SetConfigurationEditingEnabled(bool enabled)
    {
        if (HostnameTextBox == null || GeneralModeRadio == null)
        {
            return;
        }

        HostnameTextBox.IsEnabled = enabled;
        AddressTextBox.IsEnabled = enabled;
        PortTextBox.IsEnabled = enabled;
        UsernameTextBox.IsEnabled = enabled;
        PasswordInput.IsEnabled = enabled;
        PasswordVisibleTextBox.IsEnabled = enabled;
        TogglePasswordVisibilityButton.IsEnabled = enabled;
        DnsTextBox.IsEnabled = enabled;
        ProtocolComboBox.IsEnabled = enabled;
        LogLevelComboBox.IsEnabled = enabled;
        Ipv6CheckBox.IsEnabled = enabled;
        SkipVerificationCheckBox.IsEnabled = enabled;
        AntiDpiCheckBox.IsEnabled = enabled;
        PostQuantumCheckBox.IsEnabled = enabled;
        CustomSniTextBox.IsEnabled = enabled;
        SaveSettingsButton.IsEnabled = enabled;
        ImportConfigButton.IsEnabled = enabled;
        ExportConfigButton.IsEnabled = true;

        GeneralModeRadio.IsEnabled = enabled;
        SelectiveModeRadio.IsEnabled = enabled;
        DomainEntryTextBox.IsEnabled = enabled;
        AddDomainButton.IsEnabled = enabled;
        RemoveStandaloneDomainButton.IsEnabled = enabled;
        AddDomainToSelectedGroupButton.IsEnabled = enabled;
        RenameGroupButton.IsEnabled = enabled;
        DeleteGroupButton.IsEnabled = enabled;
        RemoveGroupDomainButton.IsEnabled = enabled;
        AppSearchTextBox.IsEnabled = enabled;
        RefreshAppsButton.IsEnabled = enabled;
        ManualAppTextBox.IsEnabled = enabled;
        AddManualAppButton.IsEnabled = enabled;
        GeoIpCountrySearchTextBox.IsEnabled = enabled;
        ClearGeoIpCountriesButton.IsEnabled = enabled;
    }

    private void RefreshServerState()
    {
        ServerStepText.Text = _serverSetupService.CurrentStep.DisplayText();
        var index = _serverSetupService.CurrentStep.StepIndex();
        ServerProgressBar.Value = index < 0 ? 0 : Math.Min(7, index);
        ServerLogTextBox.Text = string.Join(Environment.NewLine, _serverSetupService.Logs);
        ServerLogEmptyState.Visibility = _serverSetupService.Logs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ServerLogTextBox.ScrollToEnd();
        RefreshSetupMilestones(index, _serverSetupService.CurrentStep);

        var isRunning = IsServerSetupRunning();
        SetServerSetupEditingEnabled(!isRunning);
        InstallServerButton.IsEnabled = !isRunning;
        ApplyServerSettingsButton.IsEnabled = _serverSetupService.CurrentStep == SetupStep.Completed;
    }

    private void RefreshSetupMilestones(int stepIndex, SetupStep step)
    {
        if (step == SetupStep.Failed)
        {
            SetSetupMilestoneState(SetupMilestoneSsh, SetupMilestoneSshStatus, "FAILED", "failed");
            SetSetupMilestoneState(SetupMilestoneInstall, SetupMilestoneInstallStatus, "QUEUED", "queued");
            SetSetupMilestoneState(SetupMilestoneCertificate, SetupMilestoneCertificateStatus, "QUEUED", "queued");
            SetSetupMilestoneState(SetupMilestoneService, SetupMilestoneServiceStatus, "QUEUED", "queued");
            return;
        }

        SetSetupMilestoneState(SetupMilestoneSsh, SetupMilestoneSshStatus, stepIndex < 0 ? "READY" : stepIndex <= 1 ? "RUNNING" : "DONE", GetMilestoneVisualState(stepIndex, 0, 1));
        SetSetupMilestoneState(SetupMilestoneInstall, SetupMilestoneInstallStatus, stepIndex < 2 ? "QUEUED" : stepIndex <= 3 ? "RUNNING" : "DONE", GetMilestoneVisualState(stepIndex, 2, 3));
        SetSetupMilestoneState(SetupMilestoneCertificate, SetupMilestoneCertificateStatus, stepIndex < 4 ? "QUEUED" : stepIndex == 4 ? "RUNNING" : "DONE", GetMilestoneVisualState(stepIndex, 4, 4));
        SetSetupMilestoneState(SetupMilestoneService, SetupMilestoneServiceStatus, stepIndex < 5 ? "QUEUED" : stepIndex < 7 ? "RUNNING" : "DONE", GetMilestoneVisualState(stepIndex, 5, 7));
    }

    private static string GetMilestoneVisualState(int stepIndex, int start, int end)
    {
        if (stepIndex < start)
        {
            return "queued";
        }

        return stepIndex <= end ? "active" : "done";
    }

    private void SetSetupMilestoneState(Border border, TextBlock statusText, string label, string state)
    {
        statusText.Text = label;
        border.Opacity = state == "queued" ? 0.72 : 1;
        var (background, borderColor, foreground) = state switch
        {
            "done" => (Color.FromArgb(0x18, 0x5F, 0xF1, 0xB7), Color.FromArgb(0x55, 0x5F, 0xF1, 0xB7), Color.FromRgb(0x5F, 0xF1, 0xB7)),
            "active" => (Color.FromArgb(0x20, 0x56, 0xD9, 0xFF), Color.FromArgb(0x66, 0x56, 0xD9, 0xFF), Color.FromRgb(0x56, 0xD9, 0xFF)),
            "failed" => (Color.FromArgb(0x18, 0xFF, 0x7B, 0x92), Color.FromArgb(0x66, 0xFF, 0x7B, 0x92), Color.FromRgb(0xFF, 0x7B, 0x92)),
            _ => (Color.FromArgb(0x0E, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x32, 0xDC, 0xEB, 0xFF), Color.FromArgb(0xB8, 0xDA, 0xE5, 0xFF))
        };
        border.Background = new SolidColorBrush(background);
        border.BorderBrush = new SolidColorBrush(borderColor);
        statusText.Foreground = new SolidColorBrush(foreground);
    }

    private bool IsServerSetupRunning() =>
        _serverSetupService.CurrentStep is not (SetupStep.Idle or SetupStep.Completed or SetupStep.Failed);

    private void SetServerSetupEditingEnabled(bool enabled)
    {
        if (ServerHostTextBox == null)
        {
            return;
        }

        ServerHostTextBox.IsEnabled = enabled;
        ServerSshPortTextBox.IsEnabled = enabled;
        ServerSshUserTextBox.IsEnabled = enabled;
        ServerUseKeyCheckBox.IsEnabled = enabled;
        ServerDomainTextBox.IsEnabled = enabled;
        ServerEmailTextBox.IsEnabled = enabled;
        ServerListenPortTextBox.IsEnabled = enabled;
        ServerVpnUsernameTextBox.IsEnabled = enabled;
        ServerVpnPasswordInput.IsEnabled = enabled;
        ServerVpnPasswordVisibleTextBox.IsEnabled = enabled;
        ToggleServerVpnPasswordVisibilityButton.IsEnabled = enabled;
        GenerateVpnPasswordButton.IsEnabled = enabled;
        BrowseSshKeyButton.IsEnabled = enabled;
        UpdateServerAuthControls();
    }

    private void RefreshSplitView(bool allowDuringLoading = false)
    {
        if ((_isLoading && !allowDuringLoading) || GeneralModeRadio == null || SelectiveModeRadio == null)
        {
            return;
        }

        GeneralModeRadio.IsChecked = _currentConfig.VpnMode == VpnMode.General;
        SelectiveModeRadio.IsChecked = _currentConfig.VpnMode == VpnMode.Selective;
        DomainModeHelpText.Text = _currentConfig.VpnMode == VpnMode.General
            ? "Domains that will not go through VPN."
            : "Domains that will go through VPN.";
        AppsModeHelpText.Text = _currentConfig.VpnMode == VpnMode.General
            ? "Apps that will not use VPN."
            : "Apps that will use VPN.";
        CountriesModeHelpText.Text = _currentConfig.VpnMode == VpnMode.General
            ? "Countries whose IP ranges will not use VPN."
            : "Countries whose IP ranges will use VPN.";

        StandaloneDomainsList.ItemsSource = null;
        var standaloneDomains = _domainGroups.StandaloneDomains.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        StandaloneDomainsList.ItemsSource = standaloneDomains;
        StandaloneDomainsEmptyState.Visibility = standaloneDomains.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        DomainGroupsList.ItemsSource = null;
        var domainGroups = _domainGroups.Groups
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        DomainGroupsList.ItemsSource = domainGroups;
        DomainGroupsEmptyState.Visibility = domainGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectedDomainsText.Text = $"{_domainGroups.FlattenDomains().Distinct(StringComparer.OrdinalIgnoreCase).Count()} selected";
        RefreshSelectedGroupDomains();
        RefreshSuggestions();
        RefreshAppsList();
        RefreshGeoIpCountriesList();
    }

    private VpnMode GetSelectedVpnMode()
    {
        if (GeneralModeRadio.IsChecked == true)
        {
            return VpnMode.General;
        }

        if (SelectiveModeRadio.IsChecked == true)
        {
            return VpnMode.Selective;
        }

        return _currentConfig.VpnMode;
    }

    private void SplitSectionToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_updatingSplitSectionToggles ||
            AppsSectionToggle == null ||
            DomainSectionToggle == null ||
            GeoIpSectionToggle == null)
        {
            return;
        }

        _updatingSplitSectionToggles = true;
        try
        {
            if (sender == AppsSectionToggle)
            {
                DomainSectionToggle.IsChecked = false;
                GeoIpSectionToggle.IsChecked = false;
            }
            else if (sender == DomainSectionToggle)
            {
                AppsSectionToggle.IsChecked = false;
                GeoIpSectionToggle.IsChecked = false;
            }
            else if (sender == GeoIpSectionToggle)
            {
                AppsSectionToggle.IsChecked = false;
                DomainSectionToggle.IsChecked = false;
            }
        }
        finally
        {
            _updatingSplitSectionToggles = false;
        }
    }

    private void SplitSectionToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_updatingSplitSectionToggles ||
            AppsSectionToggle == null ||
            DomainSectionToggle == null ||
            GeoIpSectionToggle == null ||
            AppsSectionToggle.IsChecked == true ||
            DomainSectionToggle.IsChecked == true ||
            GeoIpSectionToggle.IsChecked == true)
        {
            return;
        }

        if (sender is not System.Windows.Controls.Primitives.ToggleButton toggle)
        {
            return;
        }

        _updatingSplitSectionToggles = true;
        try
        {
            toggle.IsChecked = true;
        }
        finally
        {
            _updatingSplitSectionToggles = false;
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) => await ToggleConnectionAsync();

    private async Task ToggleConnectionAsync()
    {
        try
        {
            if (_vpnService.Status == VpnStatus.Connected)
            {
                await _vpnService.DisconnectAsync();
                return;
            }

            if (_vpnService.Status == VpnStatus.Disconnected)
            {
                var config = await _configService.LoadConnectionConfigAsync();
                _currentConfig = config;
                await _vpnService.ConnectAsync(config);
            }
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(this, ex.Message, "Connection error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        try
        {
            await SaveConfigAsync(showMessage: true);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(this, ex.Message, "Save error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "veil-config.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _configService.ExportConfigAsync(BuildConfigDraftFromUi(), dialog.FileName);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(this, ex.Message, "Export error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _currentConfig = await _configService.ImportConfigAndPersistAsync(dialog.FileName);
            _domainGroups = await _configService.LoadDomainGroupsAsync();
            _selectedApps.Clear();
            foreach (var app in _currentConfig.SplitTunnelApps)
            {
                _selectedApps.Add(app);
            }

            _selectedCountries.Clear();
            foreach (var country in _currentConfig.SplitTunnelCountries)
            {
                _selectedCountries.Add(country);
            }

            FillSettingsFromConfig();
            RefreshSplitView();
            AppMessageBox.Show(this, "Settings imported and saved. Review before connecting.", "Veil", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(this, ex.Message, "Import error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void VpnModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading || !IsLoaded || !CanEditConfiguration())
        {
            return;
        }

        _currentConfig.VpnMode = GeneralModeRadio.IsChecked == true ? VpnMode.General : VpnMode.Selective;
        await SaveSplitTunnelConfigAsync();
    }

    private async void AddDomainButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        var domain = NormalizeDomain(DomainEntryTextBox.Text);
        if (string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        if (IsDomainAlreadyAdded(domain))
        {
            AppMessageBox.Show(this, "This domain is already added.", "Split Tunnel", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (!SplitTunnelEntry.ShouldDiscoverRelatedDomains(domain))
            {
                _domainGroups.AddStandaloneDomain(domain);
                DomainEntryTextBox.Clear();
                await SaveSplitTunnelConfigAsync();
                return;
            }

            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            var discovery = await _domainDiscoveryService.DiscoverRelatedDomainsAsync(domain);
            Mouse.OverrideCursor = null;

            var result = ShowDiscoveryDialog(domain, discovery);
            if (!ApplyDiscoveryDialogResult(_domainGroups, domain, result))
            {
                return;
            }

            DomainEntryTextBox.Clear();
            await SaveSplitTunnelConfigAsync();
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            AppMessageBox.Show(this, ex.Message, "Domain discovery", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RemoveStandaloneDomainButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        if (StandaloneDomainsList.SelectedItem is not string domain)
        {
            return;
        }

        _domainGroups.StandaloneDomains.RemoveAll(x => x.Equals(domain, StringComparison.OrdinalIgnoreCase));
        await SaveSplitTunnelConfigAsync();
    }

    private async void AddDomainToSelectedGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        if (DomainGroupsList.SelectedItem is not DomainGroup group)
        {
            return;
        }

        var domain = NormalizeDomain(DomainEntryTextBox.Text);
        if (string.IsNullOrWhiteSpace(domain) || IsDomainAlreadyAdded(domain))
        {
            return;
        }

        _domainGroups.AddDomainToGroup(group, domain);
        DomainEntryTextBox.Clear();
        await SaveSplitTunnelConfigAsync();
    }

    private async void RenameGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        if (DomainGroupsList.SelectedItem is not DomainGroup group)
        {
            return;
        }

        var name = Prompt("Rename Group", "Group name", group.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        group.Name = name.Trim();
        await SaveSplitTunnelConfigAsync();
    }

    private async void DeleteGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        if (DomainGroupsList.SelectedItem is not DomainGroup group)
        {
            return;
        }

        if (AppMessageBox.Show(this, $"Delete group \"{group.Name}\" and all its domains?", "Delete group", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _domainGroups.Groups.Remove(group);
        await SaveSplitTunnelConfigAsync();
    }

    private async void RemoveGroupDomainButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        if (DomainGroupsList.SelectedItem is not DomainGroup group || GroupDomainsList.SelectedItem is not string domain)
        {
            return;
        }

        _domainGroups.RemoveDomainFromGroup(group, domain);
        await SaveSplitTunnelConfigAsync();
    }

    private void DomainGroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshSelectedGroupDomains();

    private void RefreshSelectedGroupDomains()
    {
        GroupDomainsList.ItemsSource = null;
        if (DomainGroupsList.SelectedItem is DomainGroup group)
        {
            var domains = group.Domains.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            GroupDomainsList.ItemsSource = domains;
            GroupDomainsEmptyState.Visibility = domains.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        GroupDomainsEmptyState.Visibility = Visibility.Visible;
    }

    private async Task LoadInstalledAppsAsync()
    {
        try
        {
            _installedApps = await _installedAppService.GetInstalledAppsAsync();
            Dispatcher.Invoke(RefreshAppsList);
        }
        catch
        {
            // App discovery is best effort.
        }
    }

    private void RefreshAppsButton_Click(object sender, RoutedEventArgs e) => _ = LoadInstalledAppsAsync();

    private void AppSearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshAppsList();

    private void RefreshAppsList()
    {
        if (AppsList == null)
        {
            return;
        }

        var query = AppSearchTextBox.Text.Trim();
        AppsList.Items.Clear();

        var installedExecutableNames = new HashSet<string>(
            _installedApps.Select(app => app.ExecutableName),
            StringComparer.OrdinalIgnoreCase);
        var manualSelectedApps = _selectedApps
            .Where(app => !installedExecutableNames.Contains(app))
            .OrderBy(app => app, StringComparer.OrdinalIgnoreCase)
            .Select(app => new InstalledApp
            {
                DisplayName = Path.GetFileNameWithoutExtension(app),
                ExecutableName = app,
                Path = "Manual entry"
            });
        var appSource = manualSelectedApps.Concat(_installedApps);

        var filtered = appSource
            .Where(app => string.IsNullOrWhiteSpace(query) ||
                          app.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          app.ExecutableName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(250);

        foreach (var app in filtered)
        {
            var checkBox = new CheckBox
            {
                Content = BuildAppListItem(app),
                Tag = app.ExecutableName,
                ToolTip = string.IsNullOrWhiteSpace(app.Path) ? app.ExecutableName : app.Path,
                IsChecked = _selectedApps.Contains(app.ExecutableName),
                IsEnabled = CanEditConfiguration(),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                MinHeight = 42
            };
            checkBox.Checked += AppCheckBox_Changed;
            checkBox.Unchecked += AppCheckBox_Changed;
            AppsList.Items.Add(checkBox);
        }

        SelectedAppsText.Text = $"{_selectedApps.Count} selected";
        AppsEmptyState.Visibility = AppsList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement BuildAppListItem(InstalledApp app)
    {
        var root = new Grid
        {
            MinHeight = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconFrame = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Center
        };

        var iconSource = GetAppIconSource(app);
        if (iconSource != null)
        {
            var image = new Image
            {
                Source = iconSource,
                Stretch = Stretch.Uniform,
                Width = 22,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            iconFrame.Child = image;
        }
        else
        {
            iconFrame.Child = new TextBlock
            {
                Text = "\uECAA",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        var textPanel = new Grid
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        textPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        textPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(app.DisplayName) ? app.ExecutableName : app.DisplayName,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var exeName = new TextBlock
        {
            Text = app.ExecutableName,
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 11,
            Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(exeName, 1);

        textPanel.Children.Add(title);
        textPanel.Children.Add(exeName);

        Grid.SetColumn(textPanel, 1);
        root.Children.Add(iconFrame);
        root.Children.Add(textPanel);
        return root;
    }

    private ImageSource? GetAppIconSource(InstalledApp app)
    {
        var path = app.Path;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        if (_appIconCache.TryGetValue(path, out var cachedIcon))
        {
            return cachedIcon;
        }

        ImageSource? iconSource = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon != null)
            {
                iconSource = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(24, 24));
                iconSource.Freeze();
            }
        }
        catch (ArgumentException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (Win32Exception)
        {
        }

        _appIconCache[path] = iconSource;
        return iconSource;
    }

    private async void AddManualAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        var executableName = NormalizeManualExecutableName(ManualAppTextBox.Text);
        if (executableName.Length == 0)
        {
            return;
        }

        ManualAppTextBox.Clear();
        if (_selectedApps.Add(executableName))
        {
            await SaveSplitTunnelConfigAsync();
        }
        else
        {
            RefreshAppsList();
        }
    }

    private async void AppCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        if (sender is not CheckBox checkBox || checkBox.Tag is not string executableName)
        {
            return;
        }

        if (checkBox.IsChecked == true)
        {
            _selectedApps.Add(executableName);
        }
        else
        {
            _selectedApps.Remove(executableName);
        }

        SelectedAppsText.Text = $"{_selectedApps.Count} selected";
        await SaveSplitTunnelConfigAsync();
    }

    private void GeoIpCountrySearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshGeoIpCountriesList();

    private void RefreshGeoIpCountriesList()
    {
        if (GeoIpCountriesList == null)
        {
            return;
        }

        var query = GeoIpCountrySearchTextBox.Text.Trim();
        GeoIpCountriesList.Items.Clear();

        var countries = GeoIpCountryCatalog.GetCountries()
            .Where(country => string.IsNullOrWhiteSpace(query) ||
                              country.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                              country.Code.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(300);

        foreach (var country in countries)
        {
            var checkBox = new CheckBox
            {
                Content = country.DisplayName,
                Tag = country.Code,
                IsChecked = _selectedCountries.Contains(country.Code),
                IsEnabled = CanEditConfiguration()
            };
            checkBox.Checked += GeoIpCountryCheckBox_Changed;
            checkBox.Unchecked += GeoIpCountryCheckBox_Changed;
            GeoIpCountriesList.Items.Add(checkBox);
        }

        SelectedCountriesText.Text = $"{_selectedCountries.Count} selected";
        GeoIpCountriesEmptyState.Visibility = GeoIpCountriesList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearGeoIpCountriesButton.IsEnabled = CanEditConfiguration() && _selectedCountries.Count > 0;
    }

    private async void GeoIpCountryCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        if (sender is not CheckBox checkBox || checkBox.Tag is not string countryCode)
        {
            return;
        }

        if (checkBox.IsChecked == true)
        {
            _selectedCountries.Add(countryCode);
        }
        else
        {
            _selectedCountries.Remove(countryCode);
        }

        SelectedCountriesText.Text = $"{_selectedCountries.Count} selected";
        ClearGeoIpCountriesButton.IsEnabled = _selectedCountries.Count > 0;
        await SaveSplitTunnelConfigAsync();
    }

    private async void ClearGeoIpCountriesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration() || _selectedCountries.Count == 0)
        {
            return;
        }

        _selectedCountries.Clear();
        await SaveSplitTunnelConfigAsync();
    }

    internal static string NormalizeManualExecutableName(string value) =>
        ServerConfig.NormalizeSplitTunnelAppProcessName(value);

    private void CollectSuggestionsFromLog(string line)
    {
        var newSuggestions = _suggestionService.ExtractSuggestions(
            line,
            _domainGroups.FlattenDomains(),
            _suggestions,
            [],
            _currentConfig.Hostname);

        foreach (var domain in newSuggestions)
        {
            _suggestions.Add(domain);
        }

        RefreshSuggestions();
    }

    private void RefreshSuggestions()
    {
        SuggestionsList.ItemsSource = null;
        var visible = _suggestions
            .Where(s => !IsDomainAlreadyAdded(s))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SuggestionsList.ItemsSource = visible;
        SuggestionsPanel.Visibility = visible.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var canEdit = !_vpnService.Status.IsActive();
        AddSuggestionStandaloneButton.IsEnabled = canEdit;
        SuggestionGroupComboBox.IsEnabled = canEdit && _domainGroups.Groups.Count > 0;
        AddSuggestionToGroupButton.IsEnabled = canEdit && _domainGroups.Groups.Count > 0;

        var selectedGroupId = (SuggestionGroupComboBox.SelectedItem as DomainGroup)?.Id;
        SuggestionGroupComboBox.ItemsSource = null;
        SuggestionGroupComboBox.ItemsSource = _domainGroups.Groups
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SuggestionGroupComboBox.SelectedItem = SuggestionGroupComboBox.Items
            .OfType<DomainGroup>()
            .FirstOrDefault(group => group.Id == selectedGroupId);
        if (SuggestionGroupComboBox.SelectedItem == null && SuggestionGroupComboBox.Items.Count > 0)
        {
            SuggestionGroupComboBox.SelectedIndex = 0;
        }
    }

    private async void AddSuggestionStandaloneButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        if (SuggestionsList.SelectedItem is not string domain)
        {
            return;
        }

        var added = _domainGroups.AddStandaloneDomain(domain);
        _suggestions.Remove(domain);
        if (added)
        {
            await SaveSplitTunnelConfigAsync();
        }
        else
        {
            RefreshSuggestions();
        }
    }

    private async void AddSuggestionToGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditConfiguration())
        {
            return;
        }

        if (SuggestionsList.SelectedItem is not string domain ||
            SuggestionGroupComboBox.SelectedItem is not DomainGroup group ||
            IsDomainAlreadyAdded(domain))
        {
            return;
        }

        var added = _domainGroups.AddDomainToGroup(group, domain);
        _suggestions.Remove(domain);
        if (added)
        {
            await SaveSplitTunnelConfigAsync();
        }
        else
        {
            RefreshSuggestions();
        }
    }

    private void HideSuggestionButton_Click(object sender, RoutedEventArgs e)
    {
        if (SuggestionsList.SelectedItem is not string domain)
        {
            return;
        }

        _suggestions.Remove(domain);
        RefreshSuggestions();
    }

    private void HideAllSuggestionsButton_Click(object sender, RoutedEventArgs e)
    {
        _suggestions.Clear();
        RefreshSuggestions();
    }

    private void CopyLogsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vpnService.Logs.Count == 0)
        {
            return;
        }

        Clipboard.SetText(string.Join(Environment.NewLine, _vpnService.Logs));
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vpnService.Logs.Count == 0)
        {
            return;
        }

        if (AppMessageBox.Show(this, "All log entries will be deleted.", "Clear logs?", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            _vpnService.ClearLogs();
            RefreshLogs();
        }
    }

    private void ServerUseKeyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateServerAuthControls();
    }

    private void UpdateServerAuthControls()
    {
        var useKey = ServerUseKeyCheckBox.IsChecked == true;
        var canEdit = !IsServerSetupRunning();
        ServerSshPasswordInput.IsEnabled = canEdit && !useKey;
        ServerSshPasswordVisibleTextBox.IsEnabled = canEdit && !useKey;
        ToggleServerSshPasswordVisibilityButton.IsEnabled = canEdit && !useKey;
        ServerSshPasswordPanel.Visibility = useKey ? Visibility.Collapsed : Visibility.Visible;
        ServerSshKeyPathTextBox.IsEnabled = canEdit && useKey;
        BrowseSshKeyButton.IsEnabled = canEdit && useKey;
        ServerSshKeyPanel.Visibility = useKey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseSshKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "SSH keys (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            ServerSshKeyPathTextBox.Text = dialog.FileName;
        }
    }

    private void GenerateVpnPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        SetServerVpnPassword(GeneratePassword(GeneratedVpnPasswordLength));
        if (!_serverVpnPasswordVisible)
        {
            ToggleServerVpnPasswordVisibility();
        }
    }

    private async void InstallServerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = BuildServerSetupConfig();
            await _configService.SaveServerSetupConfigAsync(config);
            InstallServerButton.IsEnabled = false;
            await _serverSetupService.InstallAndRememberAsync(config);
            FillServerSetupFieldsFromConfig(config, includePasswords: true);
            await _configService.SaveServerSetupConfigAsync(config);

            if (_serverSetupService.CurrentStep == SetupStep.Completed)
            {
                AppMessageBox.Show(this, "Server installed and running.", "Veil", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (_serverSetupService.ErrorMessage != null)
            {
                AppMessageBox.Show(this, _serverSetupService.ErrorMessage, "Server setup error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(this, ex.Message, "Server setup error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshServerState();
        }
    }

    private async void ApplyServerSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _serverSetupService.ApplyToClientConfigAsync(_configService);
            await LoadConfigAsync();
            AppMessageBox.Show(this, "Client settings updated.", "Veil", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(this, ex.Message, "Veil", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearServerLogButton_Click(object sender, RoutedEventArgs e)
    {
        _serverSetupService.ClearLogs();
        RefreshServerState();
    }

    private ServerSetupConfig BuildServerSetupConfig()
    {
        if (!int.TryParse(ServerSshPortTextBox.Text.Trim(), out var sshPort) || sshPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("Invalid SSH port.");
        }

        if (!int.TryParse(ServerListenPortTextBox.Text.Trim(), out var listenPort) || listenPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("Invalid listen port.");
        }

        var config = new ServerSetupConfig
        {
            Host = Required(ServerHostTextBox.Text, "VPS IP Address"),
            SshPort = sshPort,
            SshUsername = Required(ServerSshUserTextBox.Text, "SSH username"),
            SshPassword = GetServerSshPassword(),
            SshKeyPath = ServerSshKeyPathTextBox.Text.Trim(),
            UseKeyAuth = ServerUseKeyCheckBox.IsChecked == true,
            Domain = Required(ServerDomainTextBox.Text, "Domain"),
            Email = Required(ServerEmailTextBox.Text, "Email"),
            ListenPort = listenPort,
            VpnUsername = Required(ServerVpnUsernameTextBox.Text, "VPN username"),
            VpnPassword = Required(GetServerVpnPassword(), "VPN password")
        };

        ValidateServerSetupConfig(config);
        return config;
    }

    private static void ValidateServerSetupConfig(ServerSetupConfig config)
    {
        if (config.UseKeyAuth)
        {
            if (string.IsNullOrWhiteSpace(config.SshKeyPath))
            {
                throw new InvalidOperationException("Enter SSH key path.");
            }

            if (!File.Exists(config.SshKeyPath))
            {
                throw new FileNotFoundException("SSH key not found.", config.SshKeyPath);
            }
        }
        else if (string.IsNullOrWhiteSpace(config.SshPassword))
        {
            throw new InvalidOperationException("Enter SSH password.");
        }
    }

    private string GetSettingsPassword() => _passwordVisible ? PasswordVisibleTextBox.Text : PasswordInput.Password;

    private void SetSettingsPassword(string password)
    {
        PasswordInput.Password = password;
        PasswordVisibleTextBox.Text = password;
    }

    private string GetServerSshPassword() => _serverSshPasswordVisible ? ServerSshPasswordVisibleTextBox.Text : ServerSshPasswordInput.Password;

    private void SetServerSshPassword(string password)
    {
        ServerSshPasswordInput.Password = password;
        ServerSshPasswordVisibleTextBox.Text = password;
    }

    private string GetServerVpnPassword() => _serverVpnPasswordVisible ? ServerVpnPasswordVisibleTextBox.Text : ServerVpnPasswordInput.Password;

    private void SetServerVpnPassword(string password)
    {
        ServerVpnPasswordInput.Password = password;
        ServerVpnPasswordVisibleTextBox.Text = password;
    }

    private void TogglePasswordVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_passwordVisible)
        {
            PasswordInput.Password = PasswordVisibleTextBox.Text;
        }
        else
        {
            PasswordVisibleTextBox.Text = PasswordInput.Password;
        }

        _passwordVisible = !_passwordVisible;
        PasswordInput.Visibility = _passwordVisible ? Visibility.Collapsed : Visibility.Visible;
        PasswordVisibleTextBox.Visibility = _passwordVisible ? Visibility.Visible : Visibility.Collapsed;
        TogglePasswordVisibilityButton.Content = _passwordVisible ? "Hide" : "Show";
    }

    private void ToggleServerSshPasswordVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_serverSshPasswordVisible)
        {
            ServerSshPasswordInput.Password = ServerSshPasswordVisibleTextBox.Text;
        }
        else
        {
            ServerSshPasswordVisibleTextBox.Text = ServerSshPasswordInput.Password;
        }

        _serverSshPasswordVisible = !_serverSshPasswordVisible;
        ServerSshPasswordInput.Visibility = _serverSshPasswordVisible ? Visibility.Collapsed : Visibility.Visible;
        ServerSshPasswordVisibleTextBox.Visibility = _serverSshPasswordVisible ? Visibility.Visible : Visibility.Collapsed;
        ToggleServerSshPasswordVisibilityButton.Content = _serverSshPasswordVisible ? "Hide" : "Show";
    }

    private void ToggleServerVpnPasswordVisibilityButton_Click(object sender, RoutedEventArgs e) => ToggleServerVpnPasswordVisibility();

    private void ToggleServerVpnPasswordVisibility()
    {
        if (_serverVpnPasswordVisible)
        {
            ServerVpnPasswordInput.Password = ServerVpnPasswordVisibleTextBox.Text;
        }
        else
        {
            ServerVpnPasswordVisibleTextBox.Text = ServerVpnPasswordInput.Password;
        }

        _serverVpnPasswordVisible = !_serverVpnPasswordVisible;
        ServerVpnPasswordInput.Visibility = _serverVpnPasswordVisible ? Visibility.Collapsed : Visibility.Visible;
        ServerVpnPasswordVisibleTextBox.Visibility = _serverVpnPasswordVisible ? Visibility.Visible : Visibility.Collapsed;
        ToggleServerVpnPasswordVisibilityButton.Content = _serverVpnPasswordVisible ? "Hide" : "Show";
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HomeView == null || SettingsView == null || SplitView == null || LogsView == null || ServerView == null)
        {
            return;
        }

        var selectedView = NavList.SelectedIndex switch
        {
            0 => HomeView,
            1 => SettingsView,
            2 => SplitView,
            3 => LogsView,
            4 => ServerView,
            _ => HomeView
        };

        foreach (var view in new[] { HomeView, SettingsView, SplitView, LogsView, ServerView })
        {
            if (view == selectedView)
            {
                ShowView(view);
            }
            else
            {
                HideView(view);
            }
        }
    }

    private void BeginWindowIntroAnimation()
    {
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void ShowView(FrameworkElement view)
    {
        if (view.Visibility == Visibility.Visible)
        {
            return;
        }

        view.Visibility = Visibility.Visible;
        if (!IsLoaded)
        {
            view.Opacity = 1;
            return;
        }

        var translate = view.RenderTransform as TranslateTransform;
        if (translate == null)
        {
            translate = new TranslateTransform();
            view.RenderTransform = translate;
        }

        view.Opacity = 0;
        translate.Y = 10;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        view.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = easing
            });
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = easing
            });
    }

    private static void HideView(FrameworkElement view)
    {
        if (view.Visibility != Visibility.Visible)
        {
            return;
        }

        view.BeginAnimation(UIElement.OpacityProperty, null);
        if (view.RenderTransform is TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.YProperty, null);
        }

        view.Opacity = 1;
        view.Visibility = Visibility.Collapsed;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        var result = AppMessageBox.Show(
            this,
            "Do you want to exit?\n\nChoose No to minimize to tray. If VPN is connected, it will be disconnected on exit.",
            "Close Application?",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.No)
        {
            await SaveServerSetupDraftAsync();
            Hide();
        }
        else if (result == MessageBoxResult.Yes)
        {
            await ExitApplicationAsync();
        }
    }

    private async Task ExitApplicationAsync()
    {
        _allowClose = true;
        _trayMenu?.Dispose();
        _trayMenu = null;
        _trayConnectionMenuLabel = null;
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        await SaveServerSetupDraftAsync();
        await _vpnService.ShutdownAsync();
        Application.Current.Shutdown();
    }

    private bool IsDomainAlreadyAdded(string domain) => _domainGroups.ContainsDomain(domain);

    private static string NormalizeDomain(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return SplitTunnelEntry.Normalize(value);
    }

    private static string Required(string value, string label)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        return value;
    }

    private static string GetComboValue(System.Windows.Controls.ComboBox comboBox, string fallback)
    {
        if (comboBox.SelectedItem is ComboBoxItem item && item.Content is string content)
        {
            return content;
        }

        return fallback;
    }

    private static void SetComboValue(System.Windows.Controls.ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    internal static string GeneratePassword(int length)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        var builder = new StringBuilder(length);
        foreach (var value in bytes)
        {
            builder.Append(GeneratedVpnPasswordAlphabet[value % GeneratedVpnPasswordAlphabet.Length]);
        }

        return builder.ToString();
    }

    private string? Prompt(string title, string label, string defaultValue)
    {
        var box = new TextBox { Text = defaultValue, MinWidth = 320 };
        var window = new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Children =
                {
                    new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) },
                    box,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 12, 0, 0),
                        Children =
                        {
                            new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 },
                            new Button { Content = "Save", IsDefault = true, MinWidth = 80 }
                        }
                    }
                }
            }
        };

        ((Button)((StackPanel)((StackPanel)window.Content).Children[2]).Children[1]).Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true ? box.Text : null;
    }

    internal static bool ApplyDiscoveryDialogResult(DomainGroupsData domainGroups, string domain, DiscoveryDialogResult? result)
    {
        if (result == null)
        {
            return false;
        }

        if (result.CreateGroup)
        {
            domainGroups.AddDiscoveryResult(domain, true, result.GroupName, result.Domains);
        }
        else
        {
            domainGroups.AddStandaloneDomain(domain);
        }

        return true;
    }

    private DiscoveryDialogResult? ShowDiscoveryDialog(string domain, DomainDiscoveryResult discovery)
    {
        var discoveredDomains = discovery.DiscoveredDomains;
        var hasDiscoveredDomains = discoveredDomains.Count > 0;
        var checks = discoveredDomains
            .Select(item => new CheckBox { Content = item, IsChecked = true })
            .ToList();
        var groupNameBox = new TextBox { Text = BuildDefaultGroupName(domain), MinWidth = 360 };
        DiscoveryDialogResult? result = null;

        var listPanel = new StackPanel();
        foreach (var check in checks)
        {
            listPanel.Children.Add(check);
        }

        var contentPanel = new StackPanel();
        if (hasDiscoveredDomains)
        {
            contentPanel.Children.Add(new TextBlock { Text = "Related domains found:", FontWeight = FontWeights.SemiBold });
            contentPanel.Children.Add(new TextBlock { Text = "Choose domains to add to a group.", Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 10) });
            contentPanel.Children.Add(new TextBlock { Text = "Group name" });
            contentPanel.Children.Add(groupNameBox);
            contentPanel.Children.Add(new TextBlock { Text = domain, Margin = new Thickness(0, 12, 0, 4), FontWeight = FontWeights.SemiBold });
            contentPanel.Children.Add(listPanel);
        }
        else
        {
            contentPanel.Children.Add(new TextBlock { Text = "No related domains found.", FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrWhiteSpace(discovery.Error))
            {
                contentPanel.Children.Add(new TextBlock { Text = discovery.Error, Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0) });
            }

            contentPanel.Children.Add(new TextBlock { Text = "Domain will be added standalone.", Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0) });
        }

        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        var withoutGroupButton = new Button { Content = "Without group", MinWidth = 110 };
        var addGroupButton = new Button { Content = hasDiscoveredDomains ? "Add group" : "Add", IsDefault = true, MinWidth = 100 };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                cancelButton
            }
        };
        if (hasDiscoveredDomains)
        {
            buttons.Children.Add(withoutGroupButton);
        }

        buttons.Children.Add(addGroupButton);
        DockPanel.SetDock(buttons, Dock.Bottom);

        var dock = new DockPanel
        {
            Margin = new Thickness(18),
            LastChildFill = true,
            Children =
            {
                buttons,
                new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = contentPanel
                }
            }
        };

        var window = new Window
        {
            Title = $"Add {domain}",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 520,
            Height = 520,
            Content = dock
        };

        withoutGroupButton.Click += (_, _) =>
        {
            result = new DiscoveryDialogResult(false, "", []);
            window.DialogResult = true;
        };
        addGroupButton.Click += (_, _) =>
        {
            result = hasDiscoveredDomains
                ? new DiscoveryDialogResult(
                    true,
                    groupNameBox.Text.Trim(),
                    checks.Where(c => c.IsChecked == true).Select(c => c.Content?.ToString() ?? "").Where(s => s.Length > 0).ToList())
                : new DiscoveryDialogResult(false, "", []);
            window.DialogResult = true;
        };

        return window.ShowDialog() == true ? result : null;
    }

    private static string BuildDefaultGroupName(string domain)
    {
        var firstPart = domain
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstPart)
            ? domain
            : char.ToUpperInvariant(firstPart[0]) + firstPart[1..];
    }

    internal sealed record DiscoveryDialogResult(bool CreateGroup, string GroupName, List<string> Domains);
}
