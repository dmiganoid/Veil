using System.Text;
using Veil.Models;

namespace Veil.Services;

/// <summary>
/// Renders the TrustTunnel client configuration file consumed by the VeilEngine runtime.
/// </summary>
internal static class TrustTunnelToml
{
    public static string Write(ServerConfig config, IEnumerable<string>? runtimeExclusions = null)
    {
        if (string.IsNullOrWhiteSpace(config.Hostname))
        {
            throw new InvalidOperationException("Hostname is empty. Check server settings.");
        }

        if (string.IsNullOrWhiteSpace(config.Address))
        {
            throw new InvalidOperationException("IP address is empty. Check server settings.");
        }

        if (string.IsNullOrWhiteSpace(config.Username))
        {
            throw new InvalidOperationException("Username is empty. Check server settings.");
        }

        var useSystemProxy = config.ConnectionMode == VpnConnectionMode.SystemProxy;

        // System Proxy mode only carries proxy-aware applications, so the split-tunnel rules do not apply.
        var exclusions = useSystemProxy
            ? []
            : SplitTunnelExclusions.Build(config, runtimeExclusions);

        var sb = new StringBuilder();
        sb.AppendLine("# Logging level [info, debug, trace]");
        sb.AppendLine($"loglevel = {Quote(config.LogLevel)}");
        sb.AppendLine();
        sb.AppendLine("# VPN mode.");
        sb.AppendLine("# Defines client connections routing policy:");
        sb.AppendLine("# * general: route through a VPN endpoint all connections except ones which destinations are in exclusions,");
        sb.AppendLine("# * selective: route through a VPN endpoint only the connections which destinations are in exclusions.");
        sb.AppendLine($"vpn_mode = {Quote(useSystemProxy ? "general" : config.VpnModeTomlValue)}");
        sb.AppendLine();
        sb.AppendLine($"killswitch_enabled = {Bool(!useSystemProxy)}");
        sb.AppendLine("killswitch_allow_ports = []");
        sb.AppendLine($"post_quantum_group_enabled = {Bool(config.PostQuantumGroupEnabled)}");
        sb.AppendLine();
        sb.AppendLine("# Domains and addresses which should be routed in a special manner.");
        sb.AppendLine("# Entries prefixed with ! are exceptions that override broader domain entries.");
        sb.AppendLine($"exclusions = {StringArray(exclusions)}");
        sb.AppendLine();
        sb.AppendLine("# DNS upstreams.");
        sb.AppendLine($"dns_upstreams = {(string.IsNullOrWhiteSpace(config.Dns) ? "[]" : $"[{Quote(config.Dns)}]")}");
        sb.AppendLine();
        sb.AppendLine("[endpoint]");
        sb.AppendLine($"hostname = {Quote(config.Hostname)}");
        sb.AppendLine($"addresses = [{Quote($"{config.Address}:{config.Port}")}]");
        sb.AppendLine($"has_ipv6 = {Bool(config.HasIpv6)}");
        sb.AppendLine($"username = {Quote(config.Username)}");
        sb.AppendLine($"password = {Quote(config.Password)}");
        sb.AppendLine("client_random_prefix = \"\"");
        sb.AppendLine($"skip_verification = {Bool(config.SkipVerification)}");
        sb.AppendLine("certificate = \"\"");
        sb.AppendLine($"upstream_protocol = {Quote(config.UpstreamProtocol)}");
        sb.AppendLine($"anti_dpi = {Bool(config.AntiDpi)}");
        sb.AppendLine($"custom_sni = {Quote(config.CustomSni)}");
        sb.AppendLine();
        sb.AppendLine("[listener]");
        sb.AppendLine();
        if (useSystemProxy)
        {
            sb.AppendLine("[listener.socks]");
            sb.AppendLine($"address = \"127.0.0.1:{ServerConfig.SystemProxySocksPort}\"");
        }
        else
        {
            sb.AppendLine("[listener.tun]");
            sb.AppendLine("bound_if = \"\"");
            sb.AppendLine("included_routes = [\"0.0.0.0/0\", \"2000::/3\"]");
            sb.AppendLine("excluded_routes = [\"0.0.0.0/8\", \"10.0.0.0/8\", \"127.0.0.0/8\", \"169.254.0.0/16\", \"172.16.0.0/12\", \"192.168.0.0/16\", \"224.0.0.0/3\"]");
            sb.AppendLine("mtu_size = 1280");
            sb.AppendLine("change_system_dns = true");
        }

        return sb.ToString();
    }

    public static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    public static string Quote(string value) => $"\"{Escape(value)}\"";

    public static string Bool(bool value) => value ? "true" : "false";

    private static string StringArray(IReadOnlyCollection<string> items) =>
        items.Count == 0
            ? "[]"
            : "[\n" + string.Join(",\n", items.Select(item => $"  {Quote(item)}")) + "\n]";
}
