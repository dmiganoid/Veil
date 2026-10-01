using System.IO;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using Veil.Models;

namespace Veil.Services;

public sealed class ConfigService
{
    private const string ConfigFileName = "config.json";
    private const string DomainGroupsFileName = "domain_groups.json";
    private const string ServerSetupConfigFileName = "server_setup_config.json";
    private const string PreferencesFileName = "preferences.json";
    private const string ClientConfigFileName = "trusttunnel_client.toml";

    /// <summary>
    /// Serializes reads and writes of Veil's state files. Pages re-read the configuration whenever it changes,
    /// and Windows refuses to replace a file another handle has open; a reader that fails then silently falls
    /// back to the backup or to defaults, which a later save would persist.
    /// </summary>
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public event EventHandler? ConfigChanged;

    public string AppDataDirectory { get; }
    private readonly IReadOnlyList<string>? _clientDirectoryCandidates;
    private readonly string? _legacyFlutterPreferencesPath;

    static ConfigService()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public ConfigService(
        string? appDataDirectory = null,
        IReadOnlyList<string>? clientDirectoryCandidates = null,
        string? legacyFlutterPreferencesPath = null)
    {
        AppDataDirectory = appDataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Veil");
        _clientDirectoryCandidates = clientDirectoryCandidates;
        _legacyFlutterPreferencesPath = legacyFlutterPreferencesPath;
    }

    public async Task<ServerConfig> LoadConfigAsync()
    {
        var path = Path.Combine(AppDataDirectory, ConfigFileName);
        if (!File.Exists(path) &&
            !File.Exists(BackupPathFor(path)) &&
            await TryLoadLegacyFlutterStateAsync() is { } legacyState)
        {
            var runtimeConfig = NormalizeConfigRuntimeState(legacyState.Config);
            await WriteJsonFileAtomicAsync(path, runtimeConfig);

            var domainGroups = legacyState.DomainGroups ??
                               CreateDomainGroupsFromConfig(runtimeConfig);
            await SaveDomainGroupsAsync(domainGroups);
            ConfigChanged?.Invoke(this, EventArgs.Empty);
            return runtimeConfig;
        }

        return NormalizeConfigRuntimeState(await ReadServerConfigWithBackupAsync(path));
    }

    public async Task<ServerConfig> LoadConnectionConfigAsync()
    {
        var config = await LoadConfigAsync();
        config.ValidateRequiredClientFields();
        return config;
    }

    public async Task SaveConfigAsync(ServerConfig config)
    {
        Directory.CreateDirectory(AppDataDirectory);
        var path = Path.Combine(AppDataDirectory, ConfigFileName);
        await WriteJsonFileAtomicAsync(path, NormalizeConfigRuntimeState(config));
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<DomainGroupsData> LoadDomainGroupsAsync()
    {
        var path = Path.Combine(AppDataDirectory, DomainGroupsFileName);
        return (await ReadDomainGroupsWithBackupAsync(path)).NormalizeEntries();
    }

    public async Task SaveDomainGroupsAsync(DomainGroupsData data)
    {
        Directory.CreateDirectory(AppDataDirectory);
        var path = Path.Combine(AppDataDirectory, DomainGroupsFileName);
        await WriteJsonFileAtomicAsync(path, data.NormalizeEntries());
    }

    public async Task<ServerConfig> SaveSplitTunnelStateAsync(
        DomainGroupsData domainGroups,
        VpnMode vpnMode,
        IEnumerable<string> selectedApps,
        IEnumerable<string>? selectedCountries = null)
    {
        // Snapshot the caller's collections before the first await; they are owned by the UI.
        var normalizedDomainGroups = domainGroups.NormalizeEntries();
        var apps = NormalizeSplitTunnelApps(selectedApps);
        var countries = selectedCountries == null ? null : NormalizeSplitTunnelCountries(selectedCountries);
        await SaveDomainGroupsAsync(normalizedDomainGroups);

        var config = await LoadConfigAsync();
        config.VpnMode = vpnMode;
        config.SplitTunnelDomains = normalizedDomainGroups.FlattenDomains();
        config.SplitTunnelExceptions = normalizedDomainGroups.FlattenExceptions();
        config.SplitTunnelApps = apps;
        config.SplitTunnelCountries = countries ?? NormalizeSplitTunnelCountries(config.SplitTunnelCountries);

        await SaveConfigAsync(config);
        return config;
    }

    public async Task<ServerSetupConfig> LoadServerSetupConfigAsync()
    {
        var path = Path.Combine(AppDataDirectory, ServerSetupConfigFileName);
        return (await ReadServerSetupConfigWithBackupAsync(path)).NormalizePersistedDraft();
    }

    public Task<AppPreferences> LoadPreferencesAsync() =>
        ReadJsonFileWithBackupAsync(Path.Combine(AppDataDirectory, PreferencesFileName), () => new AppPreferences());

    public async Task SavePreferencesAsync(AppPreferences preferences)
    {
        Directory.CreateDirectory(AppDataDirectory);
        await WriteJsonFileAtomicAsync(Path.Combine(AppDataDirectory, PreferencesFileName), preferences);
    }

    public async Task SaveServerSetupConfigAsync(ServerSetupConfig config)
    {
        Directory.CreateDirectory(AppDataDirectory);
        var path = Path.Combine(AppDataDirectory, ServerSetupConfigFileName);
        await WriteJsonFileAtomicAsync(path, config.NormalizePersistedDraft());
    }

    public async Task<DomainGroupsData> MigrateFlatDomainsToGroupsAsync()
    {
        var path = Path.Combine(AppDataDirectory, DomainGroupsFileName);
        if (File.Exists(path))
        {
            return await LoadDomainGroupsAsync();
        }

        var config = await LoadConfigAsync();
        var data = CreateDomainGroupsFromConfig(config);

        if (data.StandaloneDomains.Count > 0 || data.ExceptionDomains.Count > 0)
        {
            await SaveDomainGroupsAsync(data);
        }

        return data;
    }

    public async Task<ServerConfig> ImportConfigAndPersistAsync(string filePath)
    {
        var config = await ImportConfigAsync(filePath);
        var importedGroups = CreateDomainGroupsFromConfig(config);
        config.SplitTunnelDomains = importedGroups.StandaloneDomains.ToList();
        config.SplitTunnelExceptions = importedGroups.ExceptionDomains.ToList();
        var runtimeConfig = NormalizeConfigRuntimeState(config);

        // Domain groups first: saving config.json raises ConfigChanged, and listeners re-read both files.
        await SaveDomainGroupsAsync(importedGroups);
        await SaveConfigAsync(runtimeConfig);
        return runtimeConfig;
    }

    public Task<string> GetClientDirectoryAsync()
    {
        var candidates = (_clientDirectoryCandidates ?? BuildDefaultClientDirectoryCandidates())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var clientDir = candidates.FirstOrDefault(ContainsClientExecutable) ??
                        candidates.FirstOrDefault() ??
                        Path.Combine(AppContext.BaseDirectory, "client");
        Directory.CreateDirectory(clientDir);
        return Task.FromResult(clientDir);
    }

    public async Task<string> GetTrustTunnelExecutableAsync()
    {
        var clientDir = await GetClientDirectoryAsync();
        var preferred = Path.Combine(clientDir, "trusttunnel_client.exe");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        return Path.Combine(clientDir, "trusttunnel.exe");
    }

    public async Task<bool> IsTrustTunnelInstalledAsync()
    {
        var exe = await GetTrustTunnelExecutableAsync();
        return File.Exists(exe);
    }

    /// <summary>
    /// The engine configuration contains the VPN password, so it lives in the user's profile rather than
    /// next to the executable, where every local user could read it in a Program Files install.
    /// </summary>
    public Task<string> GetConfigFilePathAsync()
    {
        Directory.CreateDirectory(AppDataDirectory);
        return Task.FromResult(Path.Combine(AppDataDirectory, ClientConfigFileName));
    }

    public async Task WriteConfigFileAsync(ServerConfig config, IEnumerable<string>? runtimeExclusions = null)
    {
        var configPath = await GetConfigFilePathAsync();
        await WriteTextFileAtomicAsync(configPath, config.ToToml(runtimeExclusions));
        await DeleteLegacyClientConfigAsync();
    }

    /// <summary>
    /// Versions before 0.2 wrote the engine configuration, including the password, next to the engine.
    /// </summary>
    private async Task DeleteLegacyClientConfigAsync()
    {
        try
        {
            var legacyPath = Path.Combine(await GetClientDirectoryAsync(), ClientConfigFileName);
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the file is no longer read by Veil.
        }
    }

    public async Task ExportConfigAsync(ServerConfig config, string filePath)
    {
        await WriteJsonFileAtomicAsync(filePath, NormalizeConfigRuntimeState(config), createBackup: false);
    }

    public async Task<ServerConfig> ImportConfigAsync(string filePath)
    {
        return await ReadServerConfigFileAsync(filePath);
    }

    private static DomainGroupsData CreateDomainGroupsFromConfig(ServerConfig config) =>
        new DomainGroupsData
        {
            StandaloneDomains = config.SplitTunnelDomains.ToList(),
            ExceptionDomains = config.SplitTunnelExceptions.ToList()
        }.NormalizeEntries();

    private static ServerConfig NormalizeConfigRuntimeState(ServerConfig config)
    {
        var normalized = config.Clone();
        normalized.SplitTunnelDomains = NormalizeSplitTunnelDomains(config.SplitTunnelDomains);
        normalized.SplitTunnelExceptions = NormalizeSplitTunnelExceptions(config.SplitTunnelExceptions);
        normalized.SplitTunnelApps = NormalizeSplitTunnelApps(config.SplitTunnelApps);
        normalized.SplitTunnelCountries = NormalizeSplitTunnelCountries(config.SplitTunnelCountries);
        return normalized;
    }

    private static List<string> NormalizeSplitTunnelDomains(IEnumerable<string> domains)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return domains
            .Select(SplitTunnelEntry.Normalize)
            .Where(domain => domain.Length > 0 && seen.Add(domain))
            .ToList();
    }

    private static List<string> NormalizeSplitTunnelExceptions(IEnumerable<string> exceptions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var entry in exceptions)
        {
            if (SplitTunnelEntry.TryNormalizeException(entry, out var exception) && seen.Add(exception))
            {
                result.Add(exception);
            }
        }

        return result;
    }

    internal static List<string> NormalizeSplitTunnelApps(IEnumerable<string> apps)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return apps
            .Where(app => !string.IsNullOrWhiteSpace(app))
            .Select(SplitTunnelEntry.NormalizeAppProcessName)
            .Where(app => app.Length > 0)
            .Where(app => seen.Add(app))
            .OrderBy(app => app, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static List<string> NormalizeSplitTunnelCountries(IEnumerable<string>? countries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return (countries ?? [])
            .Select(country => country.Trim().ToUpperInvariant())
            .Where(country => country.Length == 2 &&
                              country.All(char.IsAsciiLetter) &&
                              GeoIpCountryCatalog.ContainsCode(country))
            .Where(country => seen.Add(country))
            .OrderBy(country => country, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildDefaultClientDirectoryCandidates()
    {
        var appClient = Path.Combine(AppContext.BaseDirectory, "client");
        var currentDirectory = Directory.GetCurrentDirectory();
        var currentClient = Path.Combine(currentDirectory, "client");
        var candidates = new List<string>
        {
            LooksLikeProjectDirectory(currentDirectory) ? currentClient : appClient,
            LooksLikeProjectDirectory(currentDirectory) ? appClient : currentClient
        };

        var probe = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && probe != null; i++, probe = probe.Parent)
        {
            candidates.Add(Path.Combine(probe.FullName, "client"));
            candidates.Add(Path.Combine(probe.FullName, "veil", "client"));
        }

        return candidates;
    }

    private async Task<LegacyFlutterState?> TryLoadLegacyFlutterStateAsync()
    {
        var path = _legacyFlutterPreferencesPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = OpenSharedRead(path);
            using var document = await JsonDocument.ParseAsync(stream);
            if (!TryGetStringProperty(document.RootElement, "flutter.server_config", out var configJson))
            {
                return null;
            }

            using var configDocument = JsonDocument.Parse(configJson);
            var config = ServerConfig.FromJsonElement(configDocument.RootElement);

            DomainGroupsData? domainGroups = null;
            if (TryGetStringProperty(document.RootElement, "flutter.domain_groups", out var domainGroupsJson))
            {
                try
                {
                    using var domainGroupsDocument = JsonDocument.Parse(domainGroupsJson);
                    domainGroups = DomainGroupsData.FromJsonElement(domainGroupsDocument.RootElement).NormalizeEntries();
                }
                catch
                {
                    domainGroups = null;
                }
            }

            return new LegacyFlutterState(config, domainGroups);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetStringProperty(JsonElement json, string name, out string value)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in json.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind == JsonValueKind.String)
                {
                    value = property.Value.GetString() ?? "";
                    return true;
                }
            }
        }

        value = "";
        return false;
    }

    private static bool LooksLikeProjectDirectory(string directory) =>
        File.Exists(Path.Combine(directory, "Veil.csproj")) ||
        File.Exists(Path.Combine(directory, "Veil.sln"));

    private static bool ContainsClientExecutable(string directory) =>
        Directory.Exists(directory) &&
        (File.Exists(Path.Combine(directory, "trusttunnel_client.exe")) ||
         File.Exists(Path.Combine(directory, "trusttunnel.exe")));

    // Delete sharing keeps external readers (backup tools, editors) from blocking a save.
    private static FileStream OpenSharedRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, useAsync: true);

    private static async Task<T> WithFileLockAsync<T>(Func<Task<T>> action)
    {
        await FileLock.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            FileLock.Release();
        }
    }

    private static Task<T> ReadJsonAsync<T>(string path, Func<JsonElement, T> map) =>
        WithFileLockAsync(async () =>
        {
            await using var stream = OpenSharedRead(path);
            using var document = await JsonDocument.ParseAsync(stream);
            return map(document.RootElement);
        });

    private static Task WriteJsonFileAtomicAsync<T>(string path, T value) =>
        WriteJsonFileAtomicAsync(path, value, createBackup: true);

    private static Task WriteJsonFileAtomicAsync<T>(string path, T value, bool createBackup) =>
        WithFileLockAsync(async () =>
        {
            await WriteJsonFileUnlockedAsync(path, value, createBackup);
            return true;
        });

    private static async Task WriteJsonFileUnlockedAsync<T>(string path, T value, bool createBackup)
    {
        var stream = CreateTemporaryFile(path, out var tempPath);
        try
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions);
            await stream.FlushAsync();
            await stream.DisposeAsync();
            ReplaceFile(tempPath, path, createBackup);
        }
        finally
        {
            await stream.DisposeAsync();
            DeleteTemporaryFile(tempPath);
        }
    }

    private static Task WriteTextFileAtomicAsync(string path, string content) =>
        WithFileLockAsync(async () =>
        {
            await WriteTextFileUnlockedAsync(path, content);
            return true;
        });

    private static async Task WriteTextFileUnlockedAsync(string path, string content)
    {
        var stream = CreateTemporaryFile(path, out var tempPath);
        try
        {
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(content);
                await writer.FlushAsync();
            }

            ReplaceFile(tempPath, path, createBackup: false);
        }
        finally
        {
            await stream.DisposeAsync();
            DeleteTemporaryFile(tempPath);
        }
    }

    private static FileStream CreateTemporaryFile(string targetPath, out string tempPath)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory,
            $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        return new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 8192,
            FileOptions.WriteThrough);
    }

    private static void ReplaceFile(string tempPath, string targetPath, bool createBackup)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                ReplaceFileOnce(tempPath, targetPath, createBackup);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 5)
            {
                // Antivirus scanners and indexers briefly lock freshly written files.
                Thread.Sleep(40 * attempt);
            }
        }
    }

    private static void ReplaceFileOnce(string tempPath, string targetPath, bool createBackup)
    {
        if (File.Exists(targetPath))
        {
            File.Replace(tempPath, targetPath, createBackup ? BackupPathFor(targetPath) : null);
        }
        else
        {
            File.Move(tempPath, targetPath);
        }
    }

    private static async Task<T> ReadJsonFileWithBackupAsync<T>(string path, Func<T> defaultFactory)
    {
        if (await TryReadJsonFileAsync<T>(path) is { } value)
        {
            return value;
        }

        if (await TryReadJsonFileAsync<T>(BackupPathFor(path)) is { } backupValue)
        {
            return backupValue;
        }

        return defaultFactory();
    }

    private static async Task<ServerConfig> ReadServerConfigWithBackupAsync(string path)
    {
        if (await TryReadServerConfigFileAsync(path) is { } value)
        {
            return value;
        }

        if (await TryReadServerConfigFileAsync(BackupPathFor(path)) is { } backupValue)
        {
            return backupValue;
        }

        return ServerConfig.DefaultConfig();
    }

    private static async Task<DomainGroupsData> ReadDomainGroupsWithBackupAsync(string path)
    {
        if (await TryReadDomainGroupsFileAsync(path) is { } value)
        {
            return value;
        }

        if (await TryReadDomainGroupsFileAsync(BackupPathFor(path)) is { } backupValue)
        {
            return backupValue;
        }

        return new DomainGroupsData();
    }

    private static async Task<ServerSetupConfig> ReadServerSetupConfigWithBackupAsync(string path)
    {
        if (await TryReadServerSetupConfigFileAsync(path) is { } value)
        {
            return value;
        }

        if (await TryReadServerSetupConfigFileAsync(BackupPathFor(path)) is { } backupValue)
        {
            return backupValue;
        }

        return ServerSetupConfig.DefaultConfig();
    }

    private static Task<ServerSetupConfig> ReadServerSetupConfigFileAsync(string path) =>
        ReadJsonAsync(path, ServerSetupConfig.FromJsonElement);

    private static async Task<ServerSetupConfig?> TryReadServerSetupConfigFileAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            return await ReadServerSetupConfigFileAsync(path);
        }
        catch
        {
            return default;
        }
    }

    private static Task<DomainGroupsData> ReadDomainGroupsFileAsync(string path) =>
        ReadJsonAsync(path, DomainGroupsData.FromJsonElement);

    private static async Task<DomainGroupsData?> TryReadDomainGroupsFileAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            return await ReadDomainGroupsFileAsync(path);
        }
        catch
        {
            return default;
        }
    }

    private static Task<ServerConfig> ReadServerConfigFileAsync(string path) =>
        ReadJsonAsync(path, ServerConfig.FromJsonElement);

    private static async Task<ServerConfig?> TryReadServerConfigFileAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            return await ReadServerConfigFileAsync(path);
        }
        catch
        {
            return default;
        }
    }

    private static async Task<T?> TryReadJsonFileAsync<T>(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            return await WithFileLockAsync(async () =>
            {
                await using var stream = OpenSharedRead(path);
                return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
            });
        }
        catch
        {
            return default;
        }
    }

    private static string BackupPathFor(string targetPath) => $"{targetPath}.bak";

    private sealed record LegacyFlutterState(ServerConfig Config, DomainGroupsData? DomainGroups);

    private static void DeleteTemporaryFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // A stale temp file is less harmful than hiding the original write error.
        }
    }
}
