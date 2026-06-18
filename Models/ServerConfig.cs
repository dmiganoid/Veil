using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Veil.Services;

namespace Veil.Models;

public enum VpnMode
{
    General,
    Selective
}

public sealed class ServerConfig
{
    public string Hostname { get; set; } = "vpn.example.com";
    public string Address { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 443;
    public bool HasIpv6 { get; set; } = true;
    public string Username { get; set; } = "your-username";
    public string Password { get; set; } = "";
    public bool SkipVerification { get; set; }
    public string UpstreamProtocol { get; set; } = "http2";
    public bool AntiDpi { get; set; }
    public string Dns { get; set; } = "8.8.8.8";
    public string LogLevel { get; set; } = "info";
    public string CustomSni { get; set; } = "";
    public bool PostQuantumGroupEnabled { get; set; } = true;
    public VpnMode VpnMode { get; set; } = VpnMode.General;
    public List<string> SplitTunnelDomains { get; set; } = [];
    public List<string> SplitTunnelApps { get; set; } = [];
    public List<string> SplitTunnelCountries { get; set; } = [];

    [JsonIgnore]
    public string VpnModeTomlValue => VpnMode == VpnMode.General ? "general" : "selective";

    public static ServerConfig DefaultConfig() => new();

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    public void ValidateRequiredClientFields()
    {
        if (string.IsNullOrWhiteSpace(Hostname))
        {
            throw new InvalidOperationException("Enter hostname.");
        }

        if (string.IsNullOrWhiteSpace(Address))
        {
            throw new InvalidOperationException("Enter IP address.");
        }

        if (!IsValidPort(Port))
        {
            throw new InvalidOperationException("Enter a valid port.");
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            throw new InvalidOperationException("Enter username.");
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            throw new InvalidOperationException("Enter password.");
        }

        if (string.IsNullOrWhiteSpace(Dns))
        {
            throw new InvalidOperationException("Enter DNS server.");
        }
    }

    public static ServerConfig FromJsonElement(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Server config JSON root must be an object.");
        }

        var defaults = DefaultConfig();
        return new ServerConfig
        {
            Hostname = GetString(json, "hostname", defaults.Hostname),
            Address = GetString(json, "address", defaults.Address),
            Port = GetInt(json, "port", defaults.Port),
            HasIpv6 = GetBool(json, "hasIpv6", defaults.HasIpv6),
            Username = GetString(json, "username", defaults.Username),
            Password = GetString(json, "password", defaults.Password),
            SkipVerification = GetBool(json, "skipVerification", defaults.SkipVerification),
            UpstreamProtocol = GetString(json, "upstreamProtocol", defaults.UpstreamProtocol),
            AntiDpi = GetBool(json, "antiDpi", defaults.AntiDpi),
            Dns = GetString(json, "dns", defaults.Dns),
            LogLevel = GetString(json, "logLevel", defaults.LogLevel),
            CustomSni = GetString(json, "customSni", defaults.CustomSni),
            PostQuantumGroupEnabled = GetBool(json, "postQuantumGroupEnabled", defaults.PostQuantumGroupEnabled),
            VpnMode = GetVpnMode(json, "vpnMode", defaults.VpnMode),
            SplitTunnelDomains = GetStringList(json, "splitTunnelDomains"),
            SplitTunnelApps = GetStringList(json, "splitTunnelApps"),
            SplitTunnelCountries = GetStringList(json, "splitTunnelCountries")
        };
    }

    public string ToToml(IEnumerable<string>? runtimeExclusions = null)
    {
        if (string.IsNullOrWhiteSpace(Hostname))
        {
            throw new InvalidOperationException("Hostname is empty. Check server settings.");
        }

        if (string.IsNullOrWhiteSpace(Address))
        {
            throw new InvalidOperationException("IP address is empty. Check server settings.");
        }

        if (string.IsNullOrWhiteSpace(Username))
        {
            throw new InvalidOperationException("Username is empty. Check server settings.");
        }

        var dnsUpstreams = string.IsNullOrWhiteSpace(Dns)
            ? "[]"
            : $"[\"{EscapeToml(Dns)}\"]";

        var exclusions = BuildTomlExclusions(runtimeExclusions);

        var exclusionsToml = exclusions.Count == 0
            ? "[]"
            : "[\n" + string.Join(",\n", exclusions.Select(item => $"  \"{EscapeToml(item)}\"")) + "\n]";

        var boolIpv6 = ToTomlBool(HasIpv6);
        var boolSkip = ToTomlBool(SkipVerification);
        var boolDpi = ToTomlBool(AntiDpi);
        var boolPq = ToTomlBool(PostQuantumGroupEnabled);

        var sb = new StringBuilder();
        sb.AppendLine("# Logging level [info, debug, trace]");
        sb.AppendLine($"loglevel = \"{EscapeToml(LogLevel)}\"");
        sb.AppendLine();
        sb.AppendLine("# VPN mode.");
        sb.AppendLine("# Defines client connections routing policy:");
        sb.AppendLine("# * general: route through a VPN endpoint all connections except ones which destinations are in exclusions,");
        sb.AppendLine("# * selective: route through a VPN endpoint only the connections which destinations are in exclusions.");
        sb.AppendLine($"vpn_mode = \"{VpnModeTomlValue}\"");
        sb.AppendLine();
        sb.AppendLine("killswitch_enabled = true");
        sb.AppendLine("killswitch_allow_ports = []");
        sb.AppendLine($"post_quantum_group_enabled = {boolPq}");
        sb.AppendLine();
        sb.AppendLine("# Domains and addresses which should be routed in a special manner.");
        sb.AppendLine($"exclusions = {exclusionsToml}");
        sb.AppendLine();
        sb.AppendLine("# DNS upstreams.");
        sb.AppendLine($"dns_upstreams = {dnsUpstreams}");
        sb.AppendLine();
        sb.AppendLine("[endpoint]");
        sb.AppendLine($"hostname = \"{EscapeToml(Hostname)}\"");
        sb.AppendLine($"addresses = [\"{EscapeToml(Address)}:{Port}\"]");
        sb.AppendLine($"has_ipv6 = {boolIpv6}");
        sb.AppendLine($"username = \"{EscapeToml(Username)}\"");
        sb.AppendLine($"password = \"{EscapeToml(Password)}\"");
        sb.AppendLine("client_random_prefix = \"\"");
        sb.AppendLine($"skip_verification = {boolSkip}");
        sb.AppendLine("certificate = \"\"");
        sb.AppendLine($"upstream_protocol = \"{EscapeToml(UpstreamProtocol)}\"");
        sb.AppendLine($"anti_dpi = {boolDpi}");
        sb.AppendLine($"custom_sni = \"{EscapeToml(CustomSni)}\"");
        sb.AppendLine();
        sb.AppendLine("[listener]");
        sb.AppendLine();
        sb.AppendLine("[listener.tun]");
        sb.AppendLine("bound_if = \"\"");
        sb.AppendLine("included_routes = [\"0.0.0.0/0\", \"2000::/3\"]");
        sb.AppendLine("excluded_routes = [\"0.0.0.0/8\", \"10.0.0.0/8\", \"169.254.0.0/16\", \"172.16.0.0/12\", \"192.168.0.0/16\", \"224.0.0.0/3\"]");
        sb.AppendLine("mtu_size = 1280");
        sb.AppendLine("change_system_dns = true");
        return sb.ToString();
    }

    private static string EscapeToml(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string ToTomlBool(bool value) => value ? "true" : "false";

    internal List<string> BuildTomlExclusions(IEnumerable<string>? runtimeExclusions = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var seenAppProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exclusions = new List<string>();

        foreach (var domain in SplitTunnelDomains.Select(SplitTunnelEntry.Normalize))
        {
            AddTomlExclusion(domain, seen, exclusions);
        }

        foreach (var exclusion in runtimeExclusions ?? [])
        {
            AddTomlExclusion(SplitTunnelEntry.Normalize(exclusion), seen, exclusions);
        }

        foreach (var app in SplitTunnelApps.Select(NormalizeSplitTunnelAppProcessName))
        {
            if (app.Length == 0 || !seenAppProcesses.Add(app))
            {
                continue;
            }

            foreach (var appAlias in BuildSplitTunnelAppTomlAliases(app))
            {
                AddTomlExclusion(appAlias, seen, exclusions);
            }

            foreach (var routingHint in BuildSplitTunnelAppRoutingHints(app))
            {
                AddTomlExclusion(routingHint, seen, exclusions);
            }
        }

        return exclusions;
    }

    private static void AddTomlExclusion(string value, HashSet<string> seen, List<string> exclusions)
    {
        if (value.Length > 0 && seen.Add(value))
        {
            exclusions.Add(value);
        }
    }

    internal static string NormalizeSplitTunnelAppProcessName(string value)
    {
        value = value.Trim();
        if (value.Length == 0)
        {
            return "";
        }

        if (value.StartsWith('"'))
        {
            var closingQuote = value.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                value = value[1..closingQuote];
            }
        }

        value = value.Trim().Trim('"');
        var exeIndex = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            value = value[..(exeIndex + 4)];
        }

        try
        {
            var fileName = Path.GetFileName(value);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                value = fileName;
            }
        }
        catch
        {
            // Keep the raw text if it is not a valid path.
        }

        value = value.Trim();
        if (value.Length == 0)
        {
            return "";
        }

        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"{value}.exe";
    }

    private static IEnumerable<string> BuildSplitTunnelAppTomlAliases(string executableName)
    {
        yield return executableName;

        if (executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var withoutExtension = executableName[..^4];
            if (withoutExtension.Length > 0)
            {
                yield return withoutExtension;
            }
        }

        var lowerExecutableName = executableName.ToLowerInvariant();
        if (!lowerExecutableName.Equals(executableName, StringComparison.Ordinal))
        {
            yield return lowerExecutableName;
        }

        if (lowerExecutableName.EndsWith(".exe", StringComparison.Ordinal))
        {
            var lowerWithoutExtension = lowerExecutableName[..^4];
            if (lowerWithoutExtension.Length > 0 &&
                !lowerWithoutExtension.Equals(executableName[..^4], StringComparison.Ordinal))
            {
                yield return lowerWithoutExtension;
            }
        }
    }

    private static IEnumerable<string> BuildSplitTunnelAppRoutingHints(string executableName)
    {
        if (!NeedsSteamRoutingHints(executableName))
        {
            yield break;
        }

        foreach (var companionProcess in SteamCompanionProcesses())
        {
            foreach (var alias in BuildSplitTunnelAppTomlAliases(companionProcess))
            {
                yield return alias;
            }
        }

        foreach (var domain in SteamRoutingDomains())
        {
            yield return domain;
        }
    }

    private static bool NeedsSteamRoutingHints(string executableName)
    {
        var lower = executableName.ToLowerInvariant();
        return lower is "deadlock.exe"
            or "dota2.exe"
            or "steam.exe"
            or "steamwebhelper.exe"
            or "gameoverlayui.exe"
            or "gameoverlayui64.exe"
            or "steamservice.exe";
    }

    private static IEnumerable<string> SteamCompanionProcesses()
    {
        yield return "steam.exe";
        yield return "steamwebhelper.exe";
        yield return "gameoverlayui.exe";
        yield return "gameoverlayui64.exe";
        yield return "steamservice.exe";
    }

    private static IEnumerable<string> SteamRoutingDomains()
    {
        yield return "valve.net";
        yield return "*.valve.net";
        yield return "steamserver.net";
        yield return "*.steamserver.net";
        yield return "steampowered.com";
        yield return "*.steampowered.com";
        yield return "steamcommunity.com";
        yield return "*.steamcommunity.com";
        yield return "steamcontent.com";
        yield return "*.steamcontent.com";
        yield return "steamstatic.com";
        yield return "*.steamstatic.com";
        yield return "steamusercontent.com";
        yield return "*.steamusercontent.com";
        yield return "steam-chat.com";
        yield return "*.steam-chat.com";
        yield return "steamgames.com";
        yield return "*.steamgames.com";
    }

    private static string GetString(JsonElement json, string name, string fallback)
    {
        return TryGetProperty(json, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static int GetInt(JsonElement json, string name, int fallback)
    {
        if (!TryGetProperty(json, name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => fallback
        };
    }

    private static bool GetBool(JsonElement json, string name, bool fallback)
    {
        if (!TryGetProperty(json, name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private static VpnMode GetVpnMode(JsonElement json, string name, VpnMode fallback)
    {
        var value = GetString(json, name, "");
        return value.Equals("selective", StringComparison.OrdinalIgnoreCase)
            ? VpnMode.Selective
            : value.Equals("general", StringComparison.OrdinalIgnoreCase)
                ? VpnMode.General
                : fallback;
    }

    private static List<string> GetStringList(JsonElement json, string name)
    {
        if (!TryGetProperty(json, name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString())
            .ToList();
    }

    private static bool TryGetProperty(JsonElement json, string name, out JsonElement value)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in json.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
