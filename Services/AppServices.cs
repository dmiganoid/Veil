namespace Veil.Services;

/// <summary>
/// The long-lived services shared by the main window and its pages.
/// </summary>
public sealed class AppServices(
    ConfigService config,
    VpnService vpn,
    ServerSetupService serverSetup,
    DomainDiscoveryService domainDiscovery,
    InstalledAppService installedApps,
    SplitTunnelSuggestionService suggestions)
{
    public ConfigService Config { get; } = config;
    public VpnService Vpn { get; } = vpn;
    public ServerSetupService ServerSetup { get; } = serverSetup;
    public DomainDiscoveryService DomainDiscovery { get; } = domainDiscovery;
    public InstalledAppService InstalledApps { get; } = installedApps;
    public SplitTunnelSuggestionService Suggestions { get; } = suggestions;
}
