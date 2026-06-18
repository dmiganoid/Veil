using System.Text.Json;
using System.Text.Json.Serialization;

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
        var sshPort = GetInt(json, "sshPort", defaults.SshPort);
        var listenPort = GetInt(json, "listenPort", defaults.ListenPort);
        return new ServerSetupConfig
        {
            Host = GetString(json, "host", defaults.Host),
            SshPort = IsValidPort(sshPort) ? sshPort : defaults.SshPort,
            SshUsername = GetString(json, "sshUsername", defaults.SshUsername),
            SshKeyPath = GetNullableString(json, "sshKeyPath", defaults.SshKeyPath),
            UseKeyAuth = GetBool(json, "useKeyAuth", defaults.UseKeyAuth),
            Domain = GetString(json, "domain", defaults.Domain),
            Email = GetString(json, "email", defaults.Email),
            ListenPort = IsValidPort(listenPort) ? listenPort : defaults.ListenPort,
            VpnUsername = GetString(json, "vpnUsername", defaults.VpnUsername),
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
        $"username = \"{EscapeToml(VpnUsername)}\"\n" +
        $"password = \"{EscapeToml(VpnPassword)}\"\n";

    public string GenerateHostsToml() =>
        "[[main_hosts]]\n" +
        $"hostname = \"{EscapeToml(Domain)}\"\n" +
        $"cert_chain_path = \"/etc/letsencrypt/live/{EscapeToml(Domain)}/fullchain.pem\"\n" +
        $"private_key_path = \"/etc/letsencrypt/live/{EscapeToml(Domain)}/privkey.pem\"\n";

    private static string EscapeToml(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string CleanString(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? CleanNullableString(string? value, string? fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string GetString(JsonElement json, string name, string fallback)
    {
        return TryGetProperty(json, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? fallback
                : throw new InvalidDataException($"{name} must be a string.");
    }

    private static string? GetNullableString(JsonElement json, string name, string? fallback)
    {
        if (!TryGetProperty(json, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new InvalidDataException($"{name} must be a string.");
    }

    private static int GetInt(JsonElement json, string name, int fallback)
    {
        if (!TryGetProperty(json, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new InvalidDataException($"{name} must be a number.");
    }

    private static bool GetBool(JsonElement json, string name, bool fallback)
    {
        if (!TryGetProperty(json, name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"{name} must be a boolean.")
        };
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
