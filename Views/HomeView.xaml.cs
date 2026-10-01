using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Veil.Models;
using Veil.Services;
using Veil.Ui;

namespace Veil.Views;

public partial class HomeView : UserControl
{
    private readonly AppServices _services;
    private readonly Func<Task> _toggleConnection;
    private readonly Action<AppPage> _navigate;
    private readonly DispatcherTimer _durationTimer;
    private readonly DoubleAnimation _pulseAnimation = new(1, 0.35, TimeSpan.FromMilliseconds(700))
    {
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever
    };

    private ServerConfig _config = ServerConfig.DefaultConfig();
    private DateTime? _connectedSince;

    public HomeView(AppServices services, Func<Task> toggleConnection, Action<AppPage> navigate)
    {
        _services = services;
        _toggleConnection = toggleConnection;
        _navigate = navigate;
        InitializeComponent();

        _durationTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _durationTimer.Tick += (_, _) => RefreshDuration();

        _services.Vpn.StateChanged += (_, _) => this.OnUi(RefreshStatus);
        _services.Config.ConfigChanged += (_, _) => this.OnUi(() => _ = ReloadConfigAsync());
        Loaded += async (_, _) => await ReloadConfigAsync();
    }

    private async Task ReloadConfigAsync()
    {
        _config = await _services.Config.LoadConfigAsync();
        RefreshSummary();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var status = _services.Vpn.Status;
        var configured = _config.HasCredentials;
        var host = string.IsNullOrWhiteSpace(_config.Hostname) ? "the server" : _config.Hostname;

        string pill, title, detail, caption;
        Brush accent;
        switch (status)
        {
            case VpnStatus.Connected:
                pill = "PROTECTED";
                title = "Connected";
                detail = $"{DisplayText.ConnectionMode(_config.ConnectionMode)} through {host}";
                caption = "Disconnect";
                accent = Brush("SuccessBrush");
                break;
            case VpnStatus.Connecting:
                pill = "CONNECTING";
                title = "Connecting…";
                detail = $"Starting the tunnel to {host}";
                caption = "Please wait";
                accent = Brush("WarningBrush");
                break;
            case VpnStatus.Disconnecting:
                pill = "DISCONNECTING";
                title = "Disconnecting…";
                detail = _config.ConnectionMode == VpnConnectionMode.SystemProxy
                    ? "Restoring the Windows proxy settings"
                    : "Releasing the virtual network adapter";
                caption = "Please wait";
                accent = Brush("WarningBrush");
                break;
            default:
                pill = "NOT PROTECTED";
                title = "Disconnected";
                detail = configured
                    ? $"Ready to connect to {host}"
                    : "Enter your server details to start using Veil.";
                caption = "Connect";
                accent = Brush("AccentBrush");
                break;
        }

        var connected = status == VpnStatus.Connected;
        var idle = status is VpnStatus.Connected or VpnStatus.Disconnected;

        StatePillText.Text = pill;
        StatePillText.Foreground = connected ? accent : Brush(status == VpnStatus.Disconnected ? "MutedBrush" : "WarningBrush");
        StateDot.Fill = connected ? accent : Brush(status == VpnStatus.Disconnected ? "SubtleBrush" : "WarningBrush");
        StatePill.Background = Brush(connected ? "SuccessSoftBrush" : status == VpnStatus.Disconnected ? "SurfaceRaisedBrush" : "WarningSoftBrush");
        StatusTitle.Text = title;
        StatusDetail.Text = detail;

        PowerButton.IsEnabled = idle;
        PowerButton.ToolTip = caption;
        PowerCaption.Text = caption;
        PowerRing.Stroke = accent;
        PowerHalo.Fill = accent;
        PowerHalo.Opacity = connected ? 0.22 : 0.1;
        PowerGlyph.Foreground = status == VpnStatus.Disconnected ? Brush("TextBrush") : accent;
        PowerRing.BeginAnimation(OpacityProperty, idle ? null : _pulseAnimation);

        var error = _services.Vpn.ErrorMessage;
        ErrorBox.Visibility = (!string.IsNullOrWhiteSpace(error) && !connected).ToVisibility();
        ErrorText.Text = error ?? "";
        SetupLink.Visibility = (!configured && status == VpnStatus.Disconnected).ToVisibility();

        if (connected)
        {
            _connectedSince ??= DateTime.Now;
            _durationTimer.Start();
        }
        else
        {
            _connectedSince = null;
            _durationTimer.Stop();
        }

        RefreshDuration();
        HintText.Text = status switch
        {
            VpnStatus.Connected when _config.ConnectionMode == VpnConnectionMode.SystemProxy =>
                "System proxy carries browsers and other proxy-aware apps only. Closing the window keeps Veil running in the tray.",
            VpnStatus.Connected => "Closing the window keeps Veil running in the tray.",
            VpnStatus.Disconnected when configured => "Settings are applied when you connect.",
            _ => ""
        };
    }

    private void RefreshDuration()
    {
        DurationText.Text = _connectedSince is { } since
            ? $"Connected for {DisplayText.Duration(DateTime.Now - since)}"
            : "";
        DurationText.Visibility = (_connectedSince != null).ToVisibility();
    }

    private void RefreshSummary()
    {
        var configured = _config.HasCredentials;
        ServerValueText.Text = configured && !string.IsNullOrWhiteSpace(_config.Hostname) ? _config.Hostname : "Not configured";
        ServerDetailText.Text = configured
            ? $"{_config.Address}:{_config.Port} · {DisplayText.Protocol(_config.UpstreamProtocol)}"
            : "Add the server address and credentials";

        TunnelValueText.Text = DisplayText.ConnectionMode(_config.ConnectionMode);
        var options = new List<string> { $"DNS {(string.IsNullOrWhiteSpace(_config.Dns) ? "auto" : _config.Dns)}" };
        if (_config.HasIpv6)
        {
            options.Add("IPv6");
        }

        if (_config.AntiDpi)
        {
            options.Add("Anti-DPI");
        }

        if (_config.PostQuantumGroupEnabled)
        {
            options.Add("Post-quantum");
        }

        TunnelDetailText.Text = string.Join(" · ", options);

        if (_config.ConnectionMode == VpnConnectionMode.SystemProxy)
        {
            RoutingValueText.Text = "Proxy-aware apps";
            RoutingDetailText.Text = "Routing rules apply in Full tunnel mode only";
            return;
        }

        RoutingValueText.Text = DisplayText.RoutingMode(_config.VpnMode);
        var rules = DisplayText.RulesSummary(_config);
        RoutingDetailText.Text = rules.Length > 0
            ? rules
            : _config.VpnMode == VpnMode.General
                ? "No bypass rules"
                : "No rules yet, so nothing uses the VPN";
    }

    private async void PowerButton_Click(object sender, RoutedEventArgs e) => await _toggleConnection();

    private void SetupLink_Click(object sender, RoutedEventArgs e) => _navigate(AppPage.Settings);

    private void EditConnection_Click(object sender, RoutedEventArgs e) => _navigate(AppPage.Settings);

    private void EditRules_Click(object sender, RoutedEventArgs e) => _navigate(AppPage.Routing);

    private Brush Brush(string key) => (Brush)FindResource(key);
}
