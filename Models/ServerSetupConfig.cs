using System.Text.Json;
using System.Text.Json.Serialization;
using Veil.Services;

namespace Veil.Models;

public sealed class ServerSetupConfig
{
    public string Host { get; set; } = "";
    public int SshPort { get; set; } = 22;
    public string SshUsername { get; set; } = "root";
    [JsonIgnore]
    public string SshPassword { get; set; } = "";
    public string? SshKeyPath { get; set; }
    public bool UseKeyAuth { get; set; }
    public string Domain { get; set; } = "";
    public string Email { get; set; } = "";
    public int ListenPort { get; set; } = 443;
    public string VpnUsername { get; set; } = "";
    [JsonIgnore]
    public string VpnPassword { get; set; } = "";

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    public static ServerSetupConfig DefaultConfig() => new()
    {
        SshKeyPath = GetDefaultSshKeyPath()
    };

    public ServerSetupConfig NormalizePersistedDraft()
    {
        var defaults = DefaultConfig();
        return new ServerSetupConfig
        {
            Host = CleanString(Host, defaults.Host),
            SshPort = IsValidPort(SshPort) ? SshPort : defaults.SshPort,
            SshUsername = CleanString(SshUsername, defaults.SshUsername),
            SshKeyPath = CleanNullableString(SshKeyPath, defaults.SshKeyPath),
            UseKeyAuth = UseKeyAuth,
            Domain = CleanString(Domain, defaults.Domain),
            Email = CleanString(Email, defaults.Email),
            ListenPort = IsValidPort(ListenPort) ? ListenPort : defaults.ListenPort,
            VpnUsername = CleanString(VpnUsername, defaults.VpnUsername),
            SshPassword = "",
            VpnPassword = ""
        };
    }

    public static ServerSetupConfig FromJsonElement(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Server setup JSON root must be an object.");
        }

        var defaults = DefaultConfig();
        var sshPort = JsonFields.StrictInt(json, "sshPort", defaults.SshPort);
        var listenPort = JsonFields.StrictInt(json, "listenPort", defaults.ListenPort);
        return new ServerSetupConfig
        {
            Host = JsonFields.StrictString(json, "host", defaults.Host),
            SshPort = IsValidPort(sshPort) ? sshPort : defaults.SshPort,
            SshUsername = JsonFields.StrictString(json, "sshUsername", defaults.SshUsername),
            SshKeyPath = JsonFields.StrictNullableString(json, "sshKeyPath", defaults.SshKeyPath),
            UseKeyAuth = JsonFields.StrictBool(json, "useKeyAuth", defaults.UseKeyAuth),
            Domain = JsonFields.StrictString(json, "domain", defaults.Domain),
            Email = JsonFields.StrictString(json, "email", defaults.Email),
            ListenPort = IsValidPort(listenPort) ? listenPort : defaults.ListenPort,
            VpnUsername = JsonFields.StrictString(json, "vpnUsername", defaults.VpnUsername),
            SshPassword = "",
            VpnPassword = ""
        }.NormalizePersistedDraft();
    }

    private static string GetDefaultSshKeyPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home)
            ? ""
            : Path.Combine(home, ".ssh", "id_ed25519");
    }

    public string GenerateVpnToml() =>
        $"listen_address = \"0.0.0.0:{ListenPort}\"\n" +
        "credentials_file = \"/opt/trusttunnel/credentials.toml\"\n\n" +
        "[listen_protocols.http1]\n" +
        "[listen_protocols.http2]\n" +
        "[listen_protocols.quic]\n";

    public string GenerateCredentialsToml() =>
        "[[client]]\n" +
        $"username = \"{TrustTunnelToml.Escape(VpnUsername)}\"\n" +
        $"password = \"{TrustTunnelToml.Escape(VpnPassword)}\"\n";

    public string GenerateHostsToml() =>
        "[[main_hosts]]\n" +
        $"hostname = \"{TrustTunnelToml.Escape(Domain)}\"\n" +
        $"cert_chain_path = \"/etc/letsencrypt/live/{TrustTunnelToml.Escape(Domain)}/fullchain.pem\"\n" +
        $"private_key_path = \"/etc/letsencrypt/live/{TrustTunnelToml.Escape(Domain)}/privkey.pem\"\n";

    private static string CleanString(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? CleanNullableString(string? value, string? fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
