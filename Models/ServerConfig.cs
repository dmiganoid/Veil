using System.Text.Json;
using System.Text.Json.Serialization;
using Veil.Services;

namespace Veil.Models;

public enum VpnMode
{
    /// <summary>Everything goes through the VPN except the split-tunnel rules.</summary>
    General,

    /// <summary>Only the split-tunnel rules go through the VPN.</summary>
    Selective
}

public enum VpnConnectionMode
{
    FullTunnel,
    SystemProxy
}

public sealed class ServerConfig
{
    public const int SystemProxySocksPort = 17880;

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
    public VpnConnectionMode ConnectionMode { get; set; } = VpnConnectionMode.FullTunnel;
    public VpnMode VpnMode { get; set; } = VpnMode.General;
    public List<string> SplitTunnelDomains { get; set; } = [];

    /// <summary>
    /// Domain patterns that take the opposite route of <see cref="SplitTunnelDomains"/> and override
    /// broader rules. Emitted to the engine with the <c>!</c> prefix.
    /// </summary>
    public List<string> SplitTunnelExceptions { get; set; } = [];

    public List<string> SplitTunnelApps { get; set; } = [];
    public List<string> SplitTunnelCountries { get; set; } = [];

    [JsonIgnore]
    public string VpnModeTomlValue => VpnMode == VpnMode.General ? "general" : "selective";

    [JsonIgnore]
    public bool HasCredentials => !string.IsNullOrWhiteSpace(Password);

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

    public ServerConfig Clone() => new()
    {
        Hostname = Hostname,
        Address = Address,
        Port = Port,
        HasIpv6 = HasIpv6,
        Username = Username,
        Password = Password,
        SkipVerification = SkipVerification,
        UpstreamProtocol = UpstreamProtocol,
        AntiDpi = AntiDpi,
        Dns = Dns,
        LogLevel = LogLevel,
        CustomSni = CustomSni,
        PostQuantumGroupEnabled = PostQuantumGroupEnabled,
        ConnectionMode = ConnectionMode,
        VpnMode = VpnMode,
        SplitTunnelDomains = SplitTunnelDomains.ToList(),
        SplitTunnelExceptions = SplitTunnelExceptions.ToList(),
        SplitTunnelApps = SplitTunnelApps.ToList(),
        SplitTunnelCountries = SplitTunnelCountries.ToList()
    };

    public static ServerConfig FromJsonElement(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Server config JSON root must be an object.");
        }

        var defaults = DefaultConfig();
        return new ServerConfig
        {
            Hostname = JsonFields.LenientString(json, "hostname", defaults.Hostname),
            Address = JsonFields.LenientString(json, "address", defaults.Address),
            Port = JsonFields.LenientInt(json, "port", defaults.Port),
            HasIpv6 = JsonFields.LenientBool(json, "hasIpv6", defaults.HasIpv6),
            Username = JsonFields.LenientString(json, "username", defaults.Username),
            Password = JsonFields.LenientString(json, "password", defaults.Password),
            SkipVerification = JsonFields.LenientBool(json, "skipVerification", defaults.SkipVerification),
            UpstreamProtocol = JsonFields.LenientString(json, "upstreamProtocol", defaults.UpstreamProtocol),
            AntiDpi = JsonFields.LenientBool(json, "antiDpi", defaults.AntiDpi),
            Dns = JsonFields.LenientString(json, "dns", defaults.Dns),
            LogLevel = JsonFields.LenientString(json, "logLevel", defaults.LogLevel),
            CustomSni = JsonFields.LenientString(json, "customSni", defaults.CustomSni),
            PostQuantumGroupEnabled = JsonFields.LenientBool(json, "postQuantumGroupEnabled", defaults.PostQuantumGroupEnabled),
            ConnectionMode = ReadConnectionMode(json, defaults.ConnectionMode),
            VpnMode = ReadVpnMode(json, defaults.VpnMode),
            SplitTunnelDomains = JsonFields.LenientStringList(json, "splitTunnelDomains"),
            SplitTunnelExceptions = JsonFields.LenientStringList(json, "splitTunnelExceptions"),
            SplitTunnelApps = JsonFields.LenientStringList(json, "splitTunnelApps"),
            SplitTunnelCountries = JsonFields.LenientStringList(json, "splitTunnelCountries")
        };
    }

    public string ToToml(IEnumerable<string>? runtimeExclusions = null) =>
        TrustTunnelToml.Write(this, runtimeExclusions);

    internal List<string> BuildTomlExclusions(IEnumerable<string>? runtimeExclusions = null) =>
        SplitTunnelExclusions.Build(this, runtimeExclusions);

    private static VpnMode ReadVpnMode(JsonElement json, VpnMode fallback)
    {
        var value = JsonFields.LenientString(json, "vpnMode", "");
        return value.Equals("selective", StringComparison.OrdinalIgnoreCase)
            ? VpnMode.Selective
            : value.Equals("general", StringComparison.OrdinalIgnoreCase)
                ? VpnMode.General
                : fallback;
    }

    private static VpnConnectionMode ReadConnectionMode(JsonElement json, VpnConnectionMode fallback)
    {
        var value = JsonFields.LenientString(json, "connectionMode", "");
        return value.Equals("systemProxy", StringComparison.OrdinalIgnoreCase)
            ? VpnConnectionMode.SystemProxy
            : value.Equals("fullTunnel", StringComparison.OrdinalIgnoreCase)
                ? VpnConnectionMode.FullTunnel
                : fallback;
    }
}
