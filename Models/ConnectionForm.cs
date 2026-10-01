namespace Veil.Models;

/// <summary>
/// Values of the Connection page. Applying them to the saved configuration keeps the routing state,
/// which is edited on the Routing page.
/// </summary>
internal sealed record ConnectionForm(
    string Hostname,
    string Address,
    string Port,
    string Username,
    string Password,
    string Dns,
    string UpstreamProtocol,
    string LogLevel,
    string CustomSni,
    bool HasIpv6,
    bool SkipVerification,
    bool AntiDpi,
    bool PostQuantumGroupEnabled,
    VpnConnectionMode ConnectionMode)
{
    public static ConnectionForm FromConfig(ServerConfig config) => new(
        config.Hostname,
        config.Address,
        config.Port.ToString(),
        config.Username,
        config.Password,
        config.Dns,
        config.UpstreamProtocol,
        config.LogLevel,
        config.CustomSni,
        config.HasIpv6,
        config.SkipVerification,
        config.AntiDpi,
        config.PostQuantumGroupEnabled,
        config.ConnectionMode);

    public static int ParsePort(string value)
    {
        if (!int.TryParse(value.Trim(), out var port) || !ServerConfig.IsValidPort(port))
        {
            throw new InvalidOperationException("Enter a port between 1 and 65535.");
        }

        return port;
    }

    /// <summary>
    /// Returns a copy of <paramref name="saved"/> with the form values applied.
    /// </summary>
    public ServerConfig ApplyTo(ServerConfig saved)
    {
        var config = saved.Clone();
        config.Hostname = Hostname.Trim();
        config.Address = Address.Trim();
        config.Port = ParsePort(Port);
        config.Username = Username.Trim();
        config.Password = Password;
        config.Dns = Dns.Trim();
        config.UpstreamProtocol = UpstreamProtocol;
        config.LogLevel = LogLevel;
        config.CustomSni = CustomSni.Trim();
        config.HasIpv6 = HasIpv6;
        config.SkipVerification = SkipVerification;
        config.AntiDpi = AntiDpi;
        config.PostQuantumGroupEnabled = PostQuantumGroupEnabled;
        config.ConnectionMode = ConnectionMode;
        return config;
    }
}
