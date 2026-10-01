using Veil.Models;

namespace Veil.Views;

public enum AppPage
{
    Home,
    Routing,
    Settings,
    Logs,
    Server
}

/// <summary>
/// Human-readable labels shared by the pages and the tray.
/// </summary>
internal static class DisplayText
{
    public static string Protocol(string protocol) => protocol.Trim().ToLowerInvariant() switch
    {
        "http2" => "HTTP/2",
        "http3" => "HTTP/3 (QUIC)",
        "" => "-",
        var other => other.ToUpperInvariant()
    };

    public static string ConnectionMode(VpnConnectionMode mode) =>
        mode == VpnConnectionMode.SystemProxy ? "System proxy" : "Full tunnel";

    public static string RoutingMode(VpnMode mode) =>
        mode == VpnMode.General ? "Everything through VPN" : "Only selected through VPN";

    /// <summary>What the split-tunnel rules do in the given mode.</summary>
    public static string RuleRoute(VpnMode mode) =>
        mode == VpnMode.General ? "bypass the VPN" : "go through the VPN";

    public static string Count(int count, string singular, string? plural = null) =>
        $"{count} {(count == 1 ? singular : plural ?? singular + "s")}";

    public static string RulesSummary(ServerConfig config)
    {
        var parts = new List<string>();
        if (config.SplitTunnelDomains.Count > 0)
        {
            parts.Add(Count(config.SplitTunnelDomains.Count, "domain rule"));
        }

        if (config.SplitTunnelExceptions.Count > 0)
        {
            parts.Add(Count(config.SplitTunnelExceptions.Count, "exception"));
        }

        if (config.SplitTunnelApps.Count > 0)
        {
            parts.Add(Count(config.SplitTunnelApps.Count, "app"));
        }

        if (config.SplitTunnelCountries.Count > 0)
        {
            parts.Add(Count(config.SplitTunnelCountries.Count, "country", "countries"));
        }

        return string.Join(" · ", parts);
    }

    public static string Duration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
}
