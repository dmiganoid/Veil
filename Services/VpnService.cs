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
    private readonly List<string> _logs = [];
    private IVpnClientProcess? _process;
    private string? _lastMessagePattern;
    private int _duplicateCount;
    private bool _disposed;

    public event EventHandler? StateChanged;
    public event Action<string>? LogAdded;

    public VpnStatus Status { get; private set; } = VpnStatus.Disconnected;
    public string? ErrorMessage { get; private set; }
    public IReadOnlyList<string> Logs => _logs.AsReadOnly();

    public VpnService(ConfigService configService)
        : this(configService, startInfo => new VpnClientProcess(startInfo))
    {
    }

    internal VpnService(
        ConfigService configService,
        Func<ProcessStartInfo, IVpnClientProcess> processFactory,
        TimeSpan? startupProbeDelay = null,
        TimeSpan? wintunReleaseDelay = null,
        TimeSpan? processExitReleaseDelay = null,
        GeoIpService? geoIpService = null)
    {
        _configService = configService;
        _geoIpService = geoIpService ?? new GeoIpService(configService.AppDataDirectory);
        _processFactory = processFactory;
        _startupProbeDelay = startupProbeDelay ?? TimeSpan.FromSeconds(2);
        _wintunReleaseDelay = wintunReleaseDelay ?? TimeSpan.FromSeconds(5);
        _processExitReleaseDelay = processExitReleaseDelay ?? TimeSpan.FromSeconds(3);
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
            AddLog($"Connecting to {config.Hostname}...");

            var exePath = await _configService.GetTrustTunnelExecutableAsync();
            if (!File.Exists(exePath))
            {
                var clientDir = await _configService.GetClientDirectoryAsync();
                throw new FileNotFoundException(
                    $"Veil engine not found. Place trusttunnel_client.exe or trusttunnel.exe in: {clientDir}",
                    Path.Combine(clientDir, "trusttunnel_client.exe"));
            }

            var wintunPath = Path.Combine(Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory, "wintun.dll");
            if (!File.Exists(wintunPath))
            {
                throw new FileNotFoundException(
                    $"Wintun driver not found. Place wintun.dll next to {Path.GetFileName(exePath)} in: {Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory}",
                    wintunPath);
            }

            AddLog("Creating configuration file...");
            var geoIpResult = await ResolveGeoIpExclusionsAsync(config);
            await _configService.WriteConfigFileAsync(config, geoIpResult.Cidrs);
            var configPath = await _configService.GetConfigFilePathAsync();
            LogSplitTunnelConfiguration(config, geoIpResult);

            AddLog("Starting Veil client...");
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(configPath);
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
                }

                _lastMessagePattern = null;
                _duplicateCount = 0;

                if (wasCurrentProcess && Status is VpnStatus.Connected or VpnStatus.Connecting)
                {
                    await Task.Delay(_processExitReleaseDelay);
                    if (_process == null && Status is VpnStatus.Connected or VpnStatus.Connecting)
                    {
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
                    var startupError = VpnStartupErrorClassifier.Classify(exitCode, _logs);
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
                SetStatus(VpnStatus.Disconnected);
            }

            throw;
        }
    }

    public Task DisconnectAsync() => DisconnectAsync(clearError: true);

    private async Task DisconnectAsync(bool clearError)
    {
        if (Status == VpnStatus.Disconnected && _process == null)
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
            AddLog("Waiting for Wintun adapter to release...");
            await Task.Delay(_wintunReleaseDelay);
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
            _process = null;
            SetStatus(VpnStatus.Disconnected);
        }
    }

    public async Task ShutdownAsync()
    {
        if (_process != null)
        {
            await DisconnectAsync();
        }
    }

    public void ClearLogs()
    {
        _logs.Clear();
        _lastMessagePattern = null;
        _duplicateCount = 0;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatLogLine(string line)
    {
        if (line.Contains(" ERROR ", StringComparison.Ordinal)) return $"ERROR {line}";
        if (line.Contains(" WARN ", StringComparison.Ordinal)) return $"WARN {line}";
        if (line.Contains(" INFO ", StringComparison.Ordinal)) return $"INFO {line}";
        if (line.Contains(" DEBUG ", StringComparison.Ordinal) || line.Contains(" TRACE ", StringComparison.Ordinal)) return $"DEBUG {line}";
        return line;
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
        AddLog($"Split tunnel mode: {config.VpnModeTomlValue}; runtime exclusions: {exclusions.Count}.");
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
        if (pattern == _lastMessagePattern && _logs.Count > 0)
        {
            _duplicateCount++;
            var last = _logs[^1];
            var baseText = Regex.Replace(last, @" \(x\d+\)$", "");
            _logs[^1] = $"{baseText} (x{_duplicateCount + 1})";
            LogAdded?.Invoke(_logs[^1]);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _lastMessagePattern = pattern;
        _duplicateCount = 0;

        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logs.Add(entry);

        if (_logs.Count > 500)
        {
            _logs.RemoveAt(0);
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
            _process?.Dispose();
            _geoIpService.Dispose();
        }
        catch
        {
            // Ignore shutdown cleanup errors.
        }
    }
}
