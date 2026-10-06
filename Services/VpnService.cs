using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Veil.Models;

namespace Veil.Services;

public sealed class VpnService : IDisposable
{
    private readonly ConfigService _configService;
    private readonly GeoIpService _geoIpService;
    private readonly Func<ProcessStartInfo, IVpnClientProcess> _processFactory;
    private readonly TimeSpan _startupProbeDelay;
    private readonly TimeSpan _wintunReleaseDelay;
    private readonly TimeSpan _processExitReleaseDelay;
    private readonly ISystemProxyManager _systemProxyManager;
    private const int MaxLogEntries = 500;
    private readonly object _logSync = new();
    private readonly List<string> _logs = [];
    private IVpnClientProcess? _process;
    private VpnConnectionMode? _activeConnectionMode;
    private string? _lastMessagePattern;
    private int _duplicateCount;
    private bool _disposed;

    public event EventHandler? StateChanged;
    public event Action<string>? LogAdded;

    public VpnStatus Status { get; private set; } = VpnStatus.Disconnected;
    public string? ErrorMessage { get; private set; }
    /// <summary>
    /// A snapshot of the session log. Engine output arrives on background threads, so callers get a copy.
    /// </summary>
    public IReadOnlyList<string> Logs
    {
        get
        {
            lock (_logSync)
            {
                return _logs.ToArray();
            }
        }
    }

    public VpnService(ConfigService configService)
        : this(
            configService,
            startInfo => new VpnClientProcess(startInfo),
            systemProxyManager: new WindowsSystemProxyManager(configService.AppDataDirectory))
    {
    }

    internal VpnService(
        ConfigService configService,
        Func<ProcessStartInfo, IVpnClientProcess> processFactory,
        TimeSpan? startupProbeDelay = null,
        TimeSpan? wintunReleaseDelay = null,
        TimeSpan? processExitReleaseDelay = null,
        GeoIpService? geoIpService = null,
        ISystemProxyManager? systemProxyManager = null)
    {
        _configService = configService;
        _geoIpService = geoIpService ?? new GeoIpService(configService.AppDataDirectory);
        _processFactory = processFactory;
        _startupProbeDelay = startupProbeDelay ?? TimeSpan.FromSeconds(2);
        _wintunReleaseDelay = wintunReleaseDelay ?? TimeSpan.FromSeconds(5);
        _processExitReleaseDelay = processExitReleaseDelay ?? TimeSpan.FromSeconds(3);
        _systemProxyManager = systemProxyManager ?? new NullSystemProxyManager();

        try
        {
            _systemProxyManager.RecoverStaleState();
        }
        catch (Exception ex)
        {
            AddLog($"WARN Could not restore stale system proxy settings: {ex.Message}");
        }
    }

    public async Task ConnectAsync(ServerConfig config)
    {
        if (Status.IsActive())
        {
            AddLog("Already connected or connecting.");
            return;
        }

        if (_process != null)
        {
            AddLog("Detected unfinished process, terminating it first.");
            await DisconnectAsync();
        }

        try
        {
            SetStatus(VpnStatus.Connecting);
            ErrorMessage = null;
            _activeConnectionMode = config.ConnectionMode;
            AddLog(config.ConnectionMode == VpnConnectionMode.SystemProxy
                ? $"Connecting to {config.Hostname} in System Proxy mode..."
                : $"Connecting to {config.Hostname} in Full Tunnel mode...");

            var exePath = await _configService.GetTrustTunnelExecutableAsync();
            if (!File.Exists(exePath))
            {
                var clientDir = await _configService.GetClientDirectoryAsync();
                throw new FileNotFoundException(
                    $"Veil engine not found. Place trusttunnel_client.exe or trusttunnel.exe in: {clientDir}",
                    Path.Combine(clientDir, "trusttunnel_client.exe"));
            }

            if (config.ConnectionMode == VpnConnectionMode.FullTunnel)
            {
                var wintunPath = Path.Combine(Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory, "wintun.dll");
                if (!File.Exists(wintunPath))
                {
                    throw new FileNotFoundException(
                        $"Wintun driver not found. Place wintun.dll next to {Path.GetFileName(exePath)} in: {Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory}",
                        wintunPath);
                }
            }

            AddLog("Creating configuration file...");
            var geoIpResult = config.ConnectionMode == VpnConnectionMode.SystemProxy
                ? new GeoIpResolutionResult([], [], [], UsedCache: false)
                : await ResolveGeoIpExclusionsAsync(config);
            await _configService.WriteConfigFileAsync(config, geoIpResult.Cidrs);
            var configPath = await _configService.GetConfigFilePathAsync();
            if (config.ConnectionMode == VpnConnectionMode.SystemProxy)
            {
                AddLog($"Local SOCKS5 listener: 127.0.0.1:{ServerConfig.SystemProxySocksPort}.");
                AddLog("System Proxy mode routes proxy-aware applications only; TUN split-tunnel rules are not applied.");
            }
            else
            {
                LogSplitTunnelConfiguration(config, geoIpResult);
            }

            AddLog("Starting Veil client...");
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(ToEngineSafePath(configPath));
            startInfo.ArgumentList.Add("--loglevel");
            startInfo.ArgumentList.Add(config.LogLevel);

            var process = _processFactory(startInfo);
            process.OutputLine += line => AddLog(line.Trim());
            process.ErrorLine += line => AddLog(FormatLogLine(line.Trim()));
            process.Exited += async (_, _) =>
            {
                var exitCode = process.ExitCode;
                var wasCurrentProcess = ReferenceEquals(_process, process);
                var shouldDisposeExitedProcess = wasCurrentProcess && Status == VpnStatus.Connected;
                AddLog($"Process exited with code: {exitCode}");
                if (wasCurrentProcess)
                {
                    _process = null;
                    RestoreSystemProxyIfNeeded();
                }

                lock (_logSync)
                {
                    _lastMessagePattern = null;
                    _duplicateCount = 0;
                }

                if (wasCurrentProcess && Status is VpnStatus.Connected or VpnStatus.Connecting)
                {
                    await Task.Delay(_processExitReleaseDelay);
                    if (_process == null && Status is VpnStatus.Connected or VpnStatus.Connecting)
                    {
                        _activeConnectionMode = null;
                        SetStatus(VpnStatus.Disconnected);
                    }
                }

                if (shouldDisposeExitedProcess)
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                    }
                }
            };

            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("Process.Start returned false.");
                }
            }
            catch
            {
                process.Dispose();
                throw;
            }

            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await Task.Delay(_startupProbeDelay);

            if (process.HasExited)
            {
                var exitCode = process.ExitCode;
                _process = null;

                if (exitCode != 0)
                {
                    var startupError = VpnStartupErrorClassifier.Classify(exitCode, Logs);
                    ErrorMessage = startupError.Message;
                    foreach (var message in startupError.LogMessages.Take(startupError.WaitForWintunRelease ? 1 : int.MaxValue))
                    {
                        AddLog(message);
                    }

                    if (startupError.WaitForWintunRelease)
                    {
                        await Task.Delay(_wintunReleaseDelay);
                        foreach (var message in startupError.LogMessages.Skip(1))
                        {
                            AddLog(message);
                        }
                    }
                }

                process.Dispose();
                throw new InvalidOperationException(ErrorMessage ?? "Process exited immediately after start.");
            }

            if (!ReferenceEquals(_process, process))
            {
                process.Dispose();
                throw new InvalidOperationException("Process exited immediately after start.");
            }

            if (config.ConnectionMode == VpnConnectionMode.SystemProxy)
            {
                _systemProxyManager.EnableSocksProxy(ServerConfig.SystemProxySocksPort);

                if (process.HasExited || !ReferenceEquals(_process, process))
                {
                    RestoreSystemProxyIfNeeded();
                    process.Dispose();
                    throw new InvalidOperationException("Process exited while System Proxy was being enabled.");
                }

                AddLog("Windows System Proxy enabled through the local TrustTunnel SOCKS5 listener.");
            }

            AddLog("Connected successfully.");
            SetStatus(VpnStatus.Connected);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AddLog($"Error: {ex.Message}");

            if (_process != null)
            {
                await DisconnectAsync(clearError: false);
            }
            else
            {
                RestoreSystemProxyIfNeeded();
                _activeConnectionMode = null;
                SetStatus(VpnStatus.Disconnected);
            }

            throw;
        }
    }

    public Task DisconnectAsync() => DisconnectAsync(clearError: true);

    private async Task DisconnectAsync(bool clearError)
    {
        if (Status == VpnStatus.Disconnected &&
            _process == null &&
            !_systemProxyManager.IsEnabled)
        {
            return;
        }

        try
        {
            SetStatus(VpnStatus.Disconnecting);
            AddLog("Disconnecting...");

            var process = _process;
            if (process != null && !process.HasExited)
            {
                AddLog("Stopping Veil client...");
                var stopResult = await Task.Run(() => VpnProcessStopper.StopAsync(
                    process,
                    gracefulTimeout: TimeSpan.FromSeconds(3),
                    forceTimeout: TimeSpan.FromSeconds(2)));

                if (stopResult.GracefulStopSucceeded)
                {
                    AddLog("Process terminated gracefully.");
                }
                else if (stopResult.ForceKillSucceeded)
                {
                    AddLog(stopResult.GracefulStopRequested
                        ? "Process did not terminate gracefully, force terminated."
                        : "Process has no graceful close channel, force terminated.");
                }
                else
                {
                    AddLog("Process did not terminate after force kill.");
                }
            }
            else
            {
                AddLog("Process already terminated.");
            }

            _process = null;
            process?.Dispose();
            RestoreSystemProxyIfNeeded();
            if (_activeConnectionMode == VpnConnectionMode.FullTunnel)
            {
                AddLog("Waiting for Wintun adapter to release...");
                await Task.Delay(_wintunReleaseDelay);
            }

            _activeConnectionMode = null;
            AddLog("Disconnected.");
            if (clearError)
            {
                ErrorMessage = null;
            }

            SetStatus(VpnStatus.Disconnected);
        }
        catch (Exception ex)
        {
            AddLog($"Error during disconnect: {ex.Message}");
            RestoreSystemProxyIfNeeded();
            _process = null;
            _activeConnectionMode = null;
            SetStatus(VpnStatus.Disconnected);
        }
    }

    public async Task ShutdownAsync()
    {
        if (_process != null || _systemProxyManager.IsEnabled)
        {
            await DisconnectAsync();
        }
    }

    public void ClearLogs()
    {
        lock (_logSync)
        {
            _logs.Clear();
            _lastMessagePattern = null;
            _duplicateCount = 0;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The engine may fail to open a config whose path has non-ASCII characters, so hand it the ASCII-only
    /// 8.3 form when the volume provides one. Falls back to the original path otherwise.
    /// </summary>
    internal static string ToEngineSafePath(string path)
    {
        if (path.All(char.IsAscii) || !OperatingSystem.IsWindows())
        {
            return path;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var length = GetShortPathName(path, buffer, buffer.Capacity);
            if (length > 0 && length < buffer.Capacity)
            {
                var shortPath = buffer.ToString();
                if (shortPath.All(char.IsAscii))
                {
                    return shortPath;
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return path;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
    private static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);

    private static string FormatLogLine(string line)
    {
        if (line.Contains(" ERROR ", StringComparison.Ordinal)) return $"ERROR {line}";
        if (line.Contains(" WARN ", StringComparison.Ordinal)) return $"WARN {line}";
        if (line.Contains(" INFO ", StringComparison.Ordinal)) return $"INFO {line}";
        if (line.Contains(" DEBUG ", StringComparison.Ordinal) || line.Contains(" TRACE ", StringComparison.Ordinal)) return $"DEBUG {line}";
        return line;
    }

    private void RestoreSystemProxyIfNeeded()
    {
        if (!_systemProxyManager.IsEnabled)
        {
            return;
        }

        try
        {
            _systemProxyManager.Restore();
            AddLog("Windows System Proxy settings restored.");
        }
        catch (Exception ex)
        {
            AddLog($"WARN Could not restore Windows System Proxy settings: {ex.Message}");
        }
    }

    private async Task<GeoIpResolutionResult> ResolveGeoIpExclusionsAsync(ServerConfig config)
    {
        if (config.SplitTunnelCountries.Count == 0)
        {
            return new GeoIpResolutionResult([], [], [], UsedCache: false);
        }

        AddLog($"Resolving GeoIP exclusions: {FormatCountryList(config.SplitTunnelCountries)}...");
        var result = await _geoIpService.ResolveCountryCidrsAsync(config.SplitTunnelCountries);
        if (result.FailedCountries.Count > 0)
        {
            throw new InvalidOperationException(
                $"GeoIP ranges are unavailable for {FormatCountryList(result.FailedCountries)}. Check the internet connection or try again after these countries are cached.");
        }

        AddLog($"GeoIP exclusions loaded: {result.Cidrs.Count} CIDR ranges for {FormatCountryList(result.LoadedCountries)}.");
        if (result.UsedCache)
        {
            AddLog("GeoIP cache was used for at least one country.");
        }

        return result;
    }

    private void LogSplitTunnelConfiguration(ServerConfig config, GeoIpResolutionResult geoIpResult)
    {
        var exclusions = config.BuildTomlExclusions(geoIpResult.Cidrs);
        var exceptionCount = exclusions.Count(entry => entry.StartsWith(SplitTunnelEntry.ExceptionPrefix, StringComparison.Ordinal));
        AddLog($"Split tunnel mode: {config.VpnModeTomlValue}; runtime exclusions: {exclusions.Count - exceptionCount}; exceptions: {exceptionCount}.");
        if (config.SplitTunnelCountries.Count > 0)
        {
            AddLog($"Split tunnel GeoIP countries: {FormatCountryList(config.SplitTunnelCountries)}.");
        }

        if (exclusions.Count == 0)
        {
            return;
        }

        var visibleExclusions = exclusions.Take(32).ToList();
        var suffix = exclusions.Count > visibleExclusions.Count
            ? $", ... +{exclusions.Count - visibleExclusions.Count} more"
            : "";
        AddLog($"Split tunnel exclusions: {string.Join(", ", visibleExclusions)}{suffix}");
    }

    private static string FormatCountryList(IEnumerable<string> countryCodes) =>
        string.Join(", ", countryCodes.Select(GeoIpCountryCatalog.DisplayNameForCode));

    private void AddLog(string message)
    {
        var pattern = Regex.Replace(message, @"\d+", "#");
        string entry;
        lock (_logSync)
        {
            if (pattern == _lastMessagePattern && _logs.Count > 0)
            {
                // Collapse repeated messages that differ only in numbers into "message (xN)".
                _duplicateCount++;
                var baseText = Regex.Replace(_logs[^1], @" \(x\d+\)$", "");
                entry = $"{baseText} (x{_duplicateCount + 1})";
                _logs[^1] = entry;
            }
            else
            {
                _lastMessagePattern = pattern;
                _duplicateCount = 0;
                entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
                _logs.Add(entry);
                if (_logs.Count > MaxLogEntries)
                {
                    _logs.RemoveAt(0);
                }
            }
        }

        LogAdded?.Invoke(entry);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(VpnStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_process is { HasExited: false })
            {
                _process.ForceKill();
            }
        }
        catch
        {
            // Continue with the independent proxy and resource cleanup below.
        }

        try
        {
            _process?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _process = null;
        }

        try
        {
            RestoreSystemProxyIfNeeded();
        }
        catch
        {
        }

        try
        {
            _systemProxyManager.Dispose();
        }
        catch
        {
        }

        try
        {
            _geoIpService.Dispose();
        }
        catch
        {
            // Ignore shutdown cleanup errors.
        }
    }
}
