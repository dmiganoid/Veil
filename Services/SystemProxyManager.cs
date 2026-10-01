using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace Veil.Services;

internal interface ISystemProxyManager : IDisposable
{
    bool IsEnabled { get; }
    void RecoverStaleState();
    void EnableSocksProxy(int port);
    void Restore();
}

internal sealed class WindowsSystemProxyManager : ISystemProxyManager
{
    private const string InternetSettingsPath =
        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string StateFileName = "system_proxy_state.json";
    private const int InternetOptionRefresh = 37;
    private const int InternetOptionSettingsChanged = 39;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _statePath;
    private readonly string _internetSettingsPath;
    private readonly bool _notifySettingsChanges;
    private ProxyState? _activeState;
    private bool _disposed;

    public WindowsSystemProxyManager(
        string appDataDirectory,
        string? internetSettingsPath = null,
        bool notifySettingsChanges = true)
    {
        _statePath = Path.Combine(appDataDirectory, StateFileName);
        _internetSettingsPath = internetSettingsPath ?? InternetSettingsPath;
        _notifySettingsChanges = notifySettingsChanges;
    }

    public bool IsEnabled
    {
        get
        {
            lock (_sync)
            {
                return _activeState != null;
            }
        }
    }

    public void RecoverStaleState()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_activeState != null || !File.Exists(_statePath))
            {
                return;
            }

            var state = ReadState();
            RestoreStateIfOwned(state);
            DeleteStateFile();
        }
    }

    public void EnableSocksProxy(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        lock (_sync)
        {
            ThrowIfDisposed();
            if (_activeState != null)
            {
                return;
            }

            if (File.Exists(_statePath))
            {
                var staleState = ReadState();
                RestoreStateIfOwned(staleState);
                DeleteStateFile();
            }

            using var key = OpenInternetSettings(writable: true);
            // An unqualified value in Windows' "socks=" slot is interpreted as
            // SOCKS4 by Chromium. TrustTunnel exposes SOCKS5, so keep the
            // scheme explicit for Chrome, Edge, and Chromium-based apps.
            var expectedProxyServer = $"socks=socks5://127.0.0.1:{port}";
            var state = CaptureState(key, expectedProxyServer);
            WriteState(state);

            try
            {
                key.SetValue("ProxyServer", state.ExpectedProxyServer, RegistryValueKind.String);
                key.SetValue("ProxyOverride", state.ExpectedProxyOverride, RegistryValueKind.String);
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
                NotifyInternetSettingsChangedIfEnabled();
                _activeState = state;
            }
            catch
            {
                RestoreState(key, state);
                DeleteStateFile();
                throw;
            }
        }
    }

    public void Restore()
    {
        lock (_sync)
        {
            if (_disposed && _activeState == null && !File.Exists(_statePath))
            {
                return;
            }

            var state = _activeState;
            if (state == null && File.Exists(_statePath))
            {
                state = ReadState();
            }

            if (state == null)
            {
                return;
            }

            RestoreStateIfOwned(state);
            _activeState = null;
            DeleteStateFile();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var state = _activeState;
                if (state == null && File.Exists(_statePath))
                {
                    state = ReadState();
                }

                if (state != null)
                {
                    RestoreStateIfOwned(state);
                    DeleteStateFile();
                }
            }
            catch
            {
                // Best-effort cleanup during application shutdown.
            }
            finally
            {
                _activeState = null;
                _disposed = true;
            }
        }
    }

    private void RestoreStateIfOwned(ProxyState state)
    {
        using var key = OpenInternetSettings(writable: true);
        var currentProxyServer = ReadString(key, "ProxyServer");
        if (!string.Equals(currentProxyServer, state.ExpectedProxyServer, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        RestoreState(key, state);
        NotifyInternetSettingsChangedIfEnabled();
    }

    private static ProxyState CaptureState(RegistryKey key, string expectedProxyServer)
    {
        var proxyOverride = ReadStringValue(key, "ProxyOverride");
        return new ProxyState
        {
            ProxyEnable = ReadIntValue(key, "ProxyEnable"),
            ProxyServer = ReadStringValue(key, "ProxyServer"),
            ProxyOverride = proxyOverride,
            AutoConfigUrl = ReadStringValue(key, "AutoConfigURL"),
            ExpectedProxyServer = expectedProxyServer,
            ExpectedProxyOverride = BuildProxyOverride(proxyOverride.Value)
        };
    }

    private static void RestoreState(RegistryKey key, ProxyState state)
    {
        RestoreIntValue(key, "ProxyEnable", state.ProxyEnable);
        RestoreStringValue(key, "ProxyServer", state.ProxyServer);
        RestoreStringValue(key, "ProxyOverride", state.ProxyOverride);
        RestoreStringValue(key, "AutoConfigURL", state.AutoConfigUrl);
    }

    private static string BuildProxyOverride(string? existing)
    {
        var entries = (existing ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        foreach (var localEntry in new[] { "<local>", "localhost", "127.*", "[::1]" })
        {
            if (!entries.Contains(localEntry, StringComparer.OrdinalIgnoreCase))
            {
                entries.Add(localEntry);
            }
        }

        return string.Join(';', entries);
    }

    private RegistryKey OpenInternetSettings(bool writable) =>
        Registry.CurrentUser.OpenSubKey(_internetSettingsPath, writable)
        ?? throw new InvalidOperationException("Windows Internet Settings registry key is unavailable.");

    private static RegistryStringValue ReadStringValue(RegistryKey key, string name)
    {
        var exists = key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase);
        return new RegistryStringValue(exists, exists ? ReadString(key, name) : null);
    }

    private static string? ReadString(RegistryKey key, string name) =>
        key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();

    private static RegistryIntValue ReadIntValue(RegistryKey key, string name)
    {
        var exists = key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase);
        if (!exists)
        {
            return new RegistryIntValue(false, 0);
        }

        var raw = key.GetValue(name);
        return new RegistryIntValue(true, raw == null ? 0 : Convert.ToInt32(raw));
    }

    private static void RestoreStringValue(
        RegistryKey key,
        string name,
        RegistryStringValue value)
    {
        if (value.Exists)
        {
            key.SetValue(name, value.Value ?? "", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    private static void RestoreIntValue(
        RegistryKey key,
        string name,
        RegistryIntValue value)
    {
        if (value.Exists)
        {
            key.SetValue(name, value.Value, RegistryValueKind.DWord);
        }
        else
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    private ProxyState ReadState()
    {
        var json = File.ReadAllText(_statePath);
        return JsonSerializer.Deserialize<ProxyState>(json, JsonOptions)
               ?? throw new InvalidDataException("Saved system proxy state is empty.");
    }

    private void WriteState(ProxyState state)
    {
        var directory = Path.GetDirectoryName(_statePath)
                        ?? throw new InvalidOperationException("System proxy state directory is unavailable.");
        Directory.CreateDirectory(directory);

        var tempPath = $"{_statePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(tempPath, _statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void DeleteStateFile()
    {
        if (File.Exists(_statePath))
        {
            File.Delete(_statePath);
        }
    }

    private static void NotifyInternetSettingsChanged()
    {
        _ = InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        _ = InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    private void NotifyInternetSettingsChangedIfEnabled()
    {
        if (_notifySettingsChanges)
        {
            NotifyInternetSettingsChanged();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(
        IntPtr internetHandle,
        int option,
        IntPtr buffer,
        int bufferLength);

    private sealed class ProxyState
    {
        public RegistryIntValue ProxyEnable { get; init; } = new(false, 0);
        public RegistryStringValue ProxyServer { get; init; } = new(false, null);
        public RegistryStringValue ProxyOverride { get; init; } = new(false, null);
        public RegistryStringValue AutoConfigUrl { get; init; } = new(false, null);
        public string ExpectedProxyServer { get; init; } = "";
        public string ExpectedProxyOverride { get; init; } = "";
    }

    private sealed record RegistryStringValue(bool Exists, string? Value);
    private sealed record RegistryIntValue(bool Exists, int Value);
}

internal sealed class NullSystemProxyManager : ISystemProxyManager
{
    public bool IsEnabled => false;
    public void RecoverStaleState() { }
    public void EnableSocksProxy(int port) { }
    public void Restore() { }
    public void Dispose() { }
}
