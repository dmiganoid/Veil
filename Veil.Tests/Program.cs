using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Veil;
using Veil.Dialogs;
using Veil.Models;
using Veil.Services;

namespace Veil.Tests;

public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--hold-abandoned-mutex")
        {
            HoldAbandonedMutexAndExit(args[1]);
            return;
        }

        var tests = new (string Name, Func<Task> Run)[]
        {
            ("ServerConfig generates Veil TOML", TestServerConfigToml),
            ("ServerConfig generates System Proxy TOML", TestServerConfigSystemProxyToml),
            ("Windows System Proxy restores exact previous settings", TestWindowsSystemProxyRestoresExactSettings),
            ("Windows System Proxy recovers stale crash state", TestWindowsSystemProxyRecoversStaleState),
            ("Windows System Proxy preserves external ownership changes", TestWindowsSystemProxyPreservesExternalChanges),
            ("ServerConfig canonicalizes split-tunnel TOML exclusions", TestServerConfigCanonicalizesTomlExclusions),
            ("ServerConfig emits domain exceptions after the rules they override", TestServerConfigEmitsDomainExceptions),
            ("ServerConfig skips malformed rules instead of splitting them", TestServerConfigSkipsMalformedRules),
            ("SplitTunnelEntry validates routing rules", TestSplitTunnelEntryValidatesRules),
            ("SplitTunnelEntry converts international domains to punycode", TestSplitTunnelEntryConvertsInternationalDomains),
            ("SplitTunnelEntry accepts only domain patterns as exceptions", TestSplitTunnelEntryValidatesExceptions),
            ("SplitTunnelEntry coverage follows engine matching", TestSplitTunnelEntryCoverage),
            ("DomainGroupsData keeps rules and exceptions exclusive", TestDomainGroupsRulesAndExceptionsAreExclusive),
            ("DomainGroupsData reports which rules an exception overrides", TestDomainGroupsReportsOverriddenRules),
            ("ConfigService handles non-ASCII paths with spaces", TestConfigServiceHandlesNonAsciiPathWithSpaces),
            ("ConfigService persists routing exceptions", TestConfigServicePersistsRoutingExceptions),
            ("ConfigService imports and exports routing exceptions", TestConfigServiceImportsRoutingExceptions),
            ("ConfigService loads legacy TV Gateway configs", TestConfigServiceLoadsLegacyTvGatewayConfig),
            ("ConfigService round-trips window preferences", TestConfigServiceRoundTripsPreferences),
            ("DomainGroupsData treats www. names like the engine", TestDomainGroupsCanonicalizeWww),
            ("DomainGroupsData keeps !-prefixed exceptions and drops rules they replace", TestDomainGroupsNormalizesExceptionPrefixes),
            ("ConfigService import writes domain groups before raising ConfigChanged", TestConfigServiceImportWritesGroupsBeforeNotifying),
            ("ConfigService tolerates overlapping saves and reads", TestConfigServiceToleratesOverlappingSaves),
            ("ServerSetupService parses endpoint version output", TestServerSetupParsesEndpointVersion),
            ("ServerConfig emits one entry per app, keeping spaces", TestServerConfigEmitsOneEntryPerApp),
            ("ServerConfig expands VMware Workstation to guest networking processes", TestServerConfigExpandsVmwareProcesses),
            ("ServerConfig emits GeoIP runtime CIDR exclusions", TestServerConfigEmitsGeoIpRuntimeCidrs),
            ("ServerConfig escapes Veil TOML strings", TestServerConfigEscapesTomlStrings),
            ("GeoIpService downloads and caches country CIDRs", TestGeoIpServiceDownloadsAndCachesCountryCidrs),
            ("Status and setup display text mirrors Flutter English strings", TestStatusDisplayTextMirrorsFlutter),
            ("DomainGroupsData flattens normalized domains in Flutter insertion order", TestDomainGroupFlattening),
            ("DomainGroupsData removes empty groups after last domain removal", TestDomainGroupLastDomainRemovalDeletesGroup),
            ("DomainGroupsData applies discovery selections like Flutter", TestDomainGroupDiscoverySelection),
            ("DomainGroupsData keeps AddGroup primary domain valid after duplicate cleanup", TestDomainGroupAddGroupPrimaryDomainSurvivesDuplicateCleanup),
            ("ConfigService loads domain groups with null-list defaults", TestDomainGroupsNullListDefaults),
            ("ConfigService normalizes persisted domain groups", TestConfigServiceNormalizesPersistedDomainGroups),
            ("ServerSetupConfig generates remote server TOML files", TestServerSetupToml),
            ("ServerSetupConfig escapes remote server TOML strings", TestServerSetupTomlEscapesStrings),
            ("ConfigService imports legacy Flutter JSON and exports camelCase", TestConfigServiceJsonCompatibility),
            ("ConfigService migrates legacy Flutter shared preferences", TestConfigServiceMigratesLegacyFlutterPreferences),
            ("ConfigService import persists split-tunnel runtime state", TestConfigServiceImportPersistsSplitTunnelState),
            ("ConfigService imports passwordless draft configuration", TestPasswordlessConfigImport),
            ("ConfigService imports partial Flutter JSON with defaults", TestPartialFlutterConfigImportDefaults),
            ("ConfigService rejects malformed manual imports", TestConfigServiceRejectsMalformedImport),
            ("ConfigService saves split tunnel without validating client settings", TestConfigServiceSplitTunnelSavePreservesClientDraft),
            ("ConfigService exports passwordless draft configuration", TestPasswordlessConfigExport),
            ("ConfigService save/load round-trips connection and split tunnel modes", TestConfigServiceSaveLoadRoundTrip),
            ("ConfigService normalizes persisted split-tunnel entries", TestConfigServiceNormalizesPersistedSplitTunnelEntries),
            ("ConfigService normalizes persisted GeoIP countries", TestConfigServiceNormalizesPersistedGeoIpCountries),
            ("ConfigService normalizes split-tunnel app paths", TestConfigServiceNormalizesSplitTunnelAppPaths),
            ("ConfigService normalizes saved apps without mutating caller config", TestConfigServiceSaveDoesNotMutateCallerConfig),
            ("ConfigService loads validated persisted connection config", TestConfigServiceLoadConnectionConfig),
            ("ConnectionForm rejects invalid ports", TestConnectionFormRejectsInvalidPorts),
            ("PasswordGenerator uses the Flutter alphabet", TestPasswordGeneratorUsesFlutterAlphabet),
            ("ConnectionForm applies to a detached copy and keeps routing", TestConnectionFormAppliesToDetachedCopy),
            ("DomainGroupsData applies discovery dialog choices like Flutter", TestDomainGroupsApplyDiscoveryChoices),
            ("ConfigService writes JSON files through temp replacement", TestConfigServiceAtomicJsonWrites),
            ("ConfigService exports JSON without recovery backup files", TestConfigServiceExportDoesNotCreateBackup),
            ("ConfigService restores persisted JSON from backups", TestConfigServiceRestoresFromBackup),
            ("ConfigService restores invalid domain groups from backup", TestConfigServiceRestoresInvalidDomainGroupsFromBackup),
            ("ConfigService prefers client folders with executables", TestConfigServiceClientDirectorySelection),
            ("ConfigService persists non-sensitive server setup fields only", TestServerSetupPersistenceNoSecrets),
            ("ConfigService loads server setup with null-field defaults", TestServerSetupNullFieldDefaults),
            ("ConfigService normalizes server setup draft text fields", TestServerSetupDraftTextNormalization),
            ("ConfigService normalizes invalid server setup draft ports", TestServerSetupInvalidDraftPortsUseDefaults),
            ("ConfigService restores invalid server setup from backup", TestConfigServiceRestoresInvalidServerSetupFromBackup),
            ("ServerSetupService applies only completed setup configs", TestServerSetupApplyRequiresCompletedSetup),
            ("ServerSetupService preserves split tunnel state when applying completed setup", TestServerSetupApplyPreservesSplitTunnelState),
            ("ServerSetupService ignores duplicate installs while running", TestServerSetupIgnoresDuplicateInstallsWhileRunning),
            ("ServerSetupService keeps running step when logs are cleared", TestServerSetupClearLogsKeepsRunningStep),
            ("ServerSetupService disconnects SSH session after completed install", TestServerSetupDisconnectsSshSessionAfterCompletedInstall),
            ("ServerSetupService disconnects SSH session after failed install", TestServerSetupDisconnectsSshSessionAfterFailedInstall),
            ("ServerSetupService stops installed services before reconfiguring", TestServerSetupStopsInstalledServiceBeforeReconfiguring),
            ("ServerSetupService tolerates port availability probe failures", TestServerSetupToleratesPortProbeFailures),
            ("ServerSetupService auto-selects available listen ports", TestServerSetupAutoSelectsAvailableListenPort),
            ("ConfigService persists auto-selected server setup listen ports", TestConfigServicePersistsAutoSelectedServerSetupListenPort),
            ("ServerSetupService shell-quotes certificate arguments", TestServerSetupShellQuotesCertificateArguments),
            ("ServerSetupService falls back when certbot standalone port is busy", TestServerSetupCertbotPortBusyFallback),
            ("ServerSetupService pins the automatic endpoint installation version", TestServerSetupPinsEndpointVersion),
            ("ServerSetupService rejects an unexpected installed endpoint version", TestServerSetupRejectsUnexpectedEndpointVersion),
            ("ServerSetupService preserves an inactive service after update failure", TestServerSetupPreservesInactiveServiceAfterUpdateFailure),
            ("ServerSetupService tolerates a missing unit for an installed binary", TestServerSetupToleratesMissingUnitForInstalledBinary),
            ("SplitTunnelSuggestionService mirrors Flutter log suggestion filters", TestSplitTunnelSuggestionFilters),
            ("SplitTunnelEntry preserves IP and CIDR entries", TestSplitTunnelEntryNormalization),
            ("DomainDiscoveryService extracts related domains from HTML", TestDomainDiscoveryHtmlExtraction),
            ("DomainDiscoveryService normalizes discovery input", TestDomainDiscoveryNormalizesInput),
            ("DomainDiscoveryService reports fetch failures as load errors", TestDomainDiscoveryReportsFetchFailuresAsLoadErrors),
            ("InstalledAppService adds common apps and filters installers", TestInstalledAppServiceNormalizeForDisplay),
            ("InstalledAppService derives Flutter-style directory display names", TestInstalledAppServiceDirectoryDisplayNames),
            ("InstalledAppService parses Steam library folders", TestInstalledAppServiceParsesSteamLibraryFolders),
            ("SplitTunnelEntry normalizes manual app entries", TestSplitTunnelEntryNormalizesManualAppEntries),
            ("VpnStartupErrorClassifier mirrors startup error hints", TestVpnStartupErrorClassifier),
            ("VpnService reports selected client directory when binary is missing", TestVpnServiceMissingClientBinaryMessage),
            ("VpnService reports selected client directory when Wintun is missing", TestVpnServiceMissingWintunMessage),
            ("VpnService writes config and launches client process", TestVpnServiceConnectWritesConfigAndLaunchesClient),
            ("VpnService starts and restores System Proxy without Wintun", TestVpnServiceSystemProxyWithoutWintun),
            ("VpnService restores System Proxy after spontaneous exit", TestVpnServiceSystemProxyRestoresAfterExit),
            ("VpnService restores System Proxy when the client exits during proxy activation", TestVpnServiceSystemProxyRestoresDuringActivationExit),
            ("VpnService disposes connected client process after spontaneous exit", TestVpnServiceDisposesConnectedClientProcessAfterSpontaneousExit),
            ("VpnService disposes client process when start throws", TestVpnServiceDisposesClientProcessWhenStartThrows),
            ("VpnService preserves connect error after internal cleanup", TestVpnServicePreservesConnectErrorAfterInternalCleanup),
            ("VpnService preserves stderr reader startup errors after cleanup", TestVpnServicePreservesStderrReaderStartupError),
            ("VpnService notifies subscribers when duplicate logs collapse", TestVpnServiceDuplicateLogCollapseNotifiesSubscribers),
            ("VpnService classifies immediate access-denied startup failures", TestVpnServiceClassifiesImmediateStartupFailure),
            ("VpnService classifies buried Wintun access-denied startup failures", TestVpnServiceClassifiesBuriedWintunAccessDeniedStartupFailure),
            ("VpnService does not report connected after startup transition exit", TestVpnServiceDoesNotReportConnectedAfterStartupTransitionExit),
            ("VpnProcessStopper prefers graceful stop before force kill", TestVpnProcessStopperGracefulFirst),
            ("VpnProcessStopper force kills after graceful timeout", TestVpnProcessStopperForceAfterTimeout),
            ("VpnProcessStopper force kills when graceful stop is unavailable", TestVpnProcessStopperNoGracefulChannel),
            ("StoppableProcessAdapter uses Windows console graceful fallback", TestStoppableProcessAdapterWindowsConsoleFallback),
            ("VpnProcessStopper reports force kill failures without throwing", TestVpnProcessStopperForceKillFailure),
            ("App process-exit cleanup disposes active services", TestAppProcessExitCleanupDisposesActiveServices),
            ("SingleInstanceGuard acquires abandoned mutexes", TestSingleInstanceGuardAcquiresAbandonedMutex),
            ("SingleInstanceGuard does not release a mutex it does not own", TestSingleInstanceGuardDoesNotReleaseForeignMutex)
        };

        var failures = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{tests.Length - failures} passed, {failures} failed.");
        if (failures > 0)
        {
            Environment.ExitCode = 1;
        }
    }

    private static Task TestServerConfigToml()
    {
        var config = new ServerConfig
        {
            Hostname = "vpn.example.com",
            Address = "203.0.113.10",
            Port = 8443,
            HasIpv6 = false,
            Username = "alice",
            Password = "secret",
            SkipVerification = true,
            UpstreamProtocol = "http3",
            AntiDpi = true,
            Dns = "tls://1.1.1.1",
            LogLevel = "debug",
            CustomSni = "front.example.com",
            PostQuantumGroupEnabled = false,
            VpnMode = VpnMode.Selective,
            SplitTunnelDomains = ["example.com", "api.example.com"],
            SplitTunnelApps = ["chrome.exe"]
        };

        var toml = config.ToToml();

        AssertContains(toml, "vpn_mode = \"selective\"");
        AssertContains(toml, "post_quantum_group_enabled = false");
        AssertContains(toml, "dns_upstreams = [\"tls://1.1.1.1\"]");
        AssertContains(toml, "hostname = \"vpn.example.com\"");
        AssertContains(toml, "addresses = [\"203.0.113.10:8443\"]");
        AssertContains(toml, "has_ipv6 = false");
        AssertContains(toml, "username = \"alice\"");
        AssertContains(toml, "password = \"secret\"");
        AssertContains(toml, "skip_verification = true");
        AssertContains(toml, "upstream_protocol = \"http3\"");
        AssertContains(toml, "anti_dpi = true");
        AssertContains(toml, "custom_sni = \"front.example.com\"");
        AssertContains(toml, "\"example.com\"");
        AssertContains(toml, "\"api.example.com\"");
        AssertContains(toml, "\"chrome.exe\"");
        AssertContains(toml, "killswitch_enabled = true");
        AssertContains(toml, "[listener.tun]");
        Assert(!toml.Contains("[listener.socks]", StringComparison.Ordinal), "Full Tunnel config must not create a SOCKS listener.");

        return Task.CompletedTask;
    }

    private static Task TestServerConfigSystemProxyToml()
    {
        var config = new ServerConfig
        {
            Hostname = "vpn.example.com",
            Address = "203.0.113.10",
            Username = "alice",
            Password = "secret",
            ConnectionMode = VpnConnectionMode.SystemProxy,
            VpnMode = VpnMode.Selective,
            SplitTunnelDomains = ["example.com"],
            SplitTunnelApps = ["chrome.exe"]
        };

        var toml = config.ToToml(["198.51.100.0/24"]);

        AssertContains(toml, "vpn_mode = \"general\"");
        AssertContains(toml, "killswitch_enabled = false");
        AssertContains(toml, "exclusions = []");
        AssertContains(toml, "[listener.socks]");
        AssertContains(toml, $"address = \"127.0.0.1:{ServerConfig.SystemProxySocksPort}\"");
        Assert(!toml.Contains("[listener.tun]", StringComparison.Ordinal), "System Proxy config must not create a TUN listener.");
        Assert(!toml.Contains("\"example.com\"", StringComparison.Ordinal), "TUN split-domain rules must not leak into System Proxy mode.");
        Assert(!toml.Contains("\"chrome.exe\"", StringComparison.Ordinal), "TUN app rules must not leak into System Proxy mode.");
        Assert(!toml.Contains("\"198.51.100.0/24\"", StringComparison.Ordinal), "Runtime GeoIP rules must not leak into System Proxy mode.");

        return Task.CompletedTask;
    }

    private static Task TestWindowsSystemProxyRestoresExactSettings()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        var registryRoot = $@"Software\Veil.Tests.{Guid.NewGuid():N}";
        var registryPath = $@"{registryRoot}\InternetSettings";
        Directory.CreateDirectory(tempDir);

        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(registryPath))
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", "http=old.proxy:8080", RegistryValueKind.String);
                key.SetValue("ProxyOverride", "internal.example", RegistryValueKind.String);
                key.SetValue("AutoConfigURL", "https://old.example/proxy.pac", RegistryValueKind.String);
            }

            using var manager = new WindowsSystemProxyManager(
                tempDir,
                registryPath,
                notifySettingsChanges: false);
            manager.EnableSocksProxy(ServerConfig.SystemProxySocksPort);

            using (var key = Registry.CurrentUser.OpenSubKey(registryPath))
            {
                Assert(Convert.ToInt32(key?.GetValue("ProxyEnable")) == 1, "System Proxy should enable the Windows proxy flag.");
                Assert(
                    string.Equals(
                        key?.GetValue("ProxyServer")?.ToString(),
                        $"socks=socks5://127.0.0.1:{ServerConfig.SystemProxySocksPort}",
                        StringComparison.Ordinal),
                    "System Proxy should explicitly select the local TrustTunnel SOCKS5 listener.");
                Assert(
                    key?.GetValue("ProxyOverride")?.ToString()?.Contains("<local>", StringComparison.OrdinalIgnoreCase) == true,
                    "System Proxy should bypass local hosts.");
                Assert(key?.GetValue("AutoConfigURL") == null, "System Proxy should temporarily disable a conflicting PAC URL.");
            }

            manager.Restore();

            using (var key = Registry.CurrentUser.OpenSubKey(registryPath))
            {
                Assert(Convert.ToInt32(key?.GetValue("ProxyEnable")) == 0, "Restore should recover the previous proxy-enabled flag.");
                Assert(key?.GetValue("ProxyServer")?.ToString() == "http=old.proxy:8080", "Restore should recover the previous proxy server.");
                Assert(key?.GetValue("ProxyOverride")?.ToString() == "internal.example", "Restore should recover the previous bypass list.");
                Assert(key?.GetValue("AutoConfigURL")?.ToString() == "https://old.example/proxy.pac", "Restore should recover the previous PAC URL.");
            }

            Assert(
                !File.Exists(Path.Combine(tempDir, "system_proxy_state.json")),
                "Successful restore should delete the crash-recovery state file.");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryRoot, throwOnMissingSubKey: false);
            Directory.Delete(tempDir, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task TestWindowsSystemProxyRecoversStaleState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        var registryRoot = $@"Software\Veil.Tests.{Guid.NewGuid():N}";
        var registryPath = $@"{registryRoot}\InternetSettings";
        Directory.CreateDirectory(tempDir);
        WindowsSystemProxyManager? crashedManager = null;

        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(registryPath))
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", "direct-before-crash", RegistryValueKind.String);
            }

            crashedManager = new WindowsSystemProxyManager(
                tempDir,
                registryPath,
                notifySettingsChanges: false);
            crashedManager.EnableSocksProxy(ServerConfig.SystemProxySocksPort);

            using var restartedManager = new WindowsSystemProxyManager(
                tempDir,
                registryPath,
                notifySettingsChanges: false);
            restartedManager.RecoverStaleState();

            using var restoredKey = Registry.CurrentUser.OpenSubKey(registryPath);
            Assert(Convert.ToInt32(restoredKey?.GetValue("ProxyEnable")) == 0, "Stale recovery should restore the pre-crash enable flag.");
            Assert(restoredKey?.GetValue("ProxyServer")?.ToString() == "direct-before-crash", "Stale recovery should restore the pre-crash proxy value.");
            Assert(
                !File.Exists(Path.Combine(tempDir, "system_proxy_state.json")),
                "Stale recovery should remove the consumed recovery state file.");
        }
        finally
        {
            crashedManager?.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(registryRoot, throwOnMissingSubKey: false);
            Directory.Delete(tempDir, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task TestWindowsSystemProxyPreservesExternalChanges()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        var registryRoot = $@"Software\Veil.Tests.{Guid.NewGuid():N}";
        var registryPath = $@"{registryRoot}\InternetSettings";
        Directory.CreateDirectory(tempDir);

        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(registryPath))
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            }

            using var manager = new WindowsSystemProxyManager(
                tempDir,
                registryPath,
                notifySettingsChanges: false);
            manager.EnableSocksProxy(ServerConfig.SystemProxySocksPort);

            using (var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true))
            {
                key?.SetValue("ProxyServer", "http=external.proxy:3128", RegistryValueKind.String);
                key?.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            }

            manager.Restore();

            using var preservedKey = Registry.CurrentUser.OpenSubKey(registryPath);
            Assert(
                preservedKey?.GetValue("ProxyServer")?.ToString() == "http=external.proxy:3128",
                "Veil must not overwrite proxy settings taken over by another application.");
            Assert(Convert.ToInt32(preservedKey?.GetValue("ProxyEnable")) == 1, "External proxy ownership should preserve its enable flag.");
            Assert(
                !File.Exists(Path.Combine(tempDir, "system_proxy_state.json")),
                "Ownership handoff should discard Veil's stale recovery state.");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registryRoot, throwOnMissingSubKey: false);
            Directory.Delete(tempDir, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task TestServerConfigCanonicalizesTomlExclusions()
    {
        var config = new ServerConfig
        {
            Hostname = "vpn.example.com",
            Address = "203.0.113.10",
            Username = "alice",
            Password = "secret",
            SplitTunnelDomains =
            [
                " HTTPS://Example.COM/path?q=1 ",
                "example.com",
                "2001:DB8::/32",
                ""
            ],
            SplitTunnelApps =
            [
                " Chrome.exe ",
                "chrome.EXE",
                "",
                "slack.exe"
            ]
        };

        var toml = config.ToToml();

        AssertContains(toml, "\"example.com\"");
        AssertContains(toml, "\"2001:db8::/32\"");
        AssertContains(toml, "\"Chrome.exe\"");
        AssertContains(toml, "\"slack.exe\"");
        Assert(!toml.Contains("HTTPS://Example.COM", StringComparison.Ordinal), "URL-style domain should not leak into TOML exclusions.");
        Assert(!toml.Contains(" Chrome.exe ", StringComparison.Ordinal), "App exclusions should not keep surrounding whitespace.");
        Assert(toml.IndexOf("\"Chrome.exe\"", StringComparison.Ordinal) == toml.LastIndexOf("\"Chrome.exe\"", StringComparison.Ordinal), "Duplicate app exclusions should be collapsed.");
        return Task.CompletedTask;
    }

    private static Task TestServerConfigEmitsOneEntryPerApp()
    {
        var config = new ServerConfig
        {
            Hostname = "vpn.example.com",
            Address = "203.0.113.10",
            Username = "alice",
            Password = "secret",
            SplitTunnelApps =
            [
                "Deadlock.exe",
                "deadlock.EXE",
                @"D:\Steam\steam.exe",
                @"D:\Games\Hollow Knight\Hollow Knight.exe"
            ]
        };

        var exclusions = config.BuildTomlExclusions();

        Assert(exclusions.Count(item => item.Equals("deadlock.exe", StringComparison.OrdinalIgnoreCase)) == 1, "Case variants of one app should collapse into one entry.");
        Assert(exclusions.Contains("Hollow Knight.exe"), "App names with spaces must stay one entry; VeilEngine reads one exclusion per line.");
        Assert(!exclusions.Contains("Deadlock") && !exclusions.Contains("steam") && !exclusions.Contains("Hollow Knight"),
            "Names without .exe would be parsed by the engine as domain rules.");
        foreach (var expected in new[] { "steam.exe", "steamwebhelper.exe", "gameoverlayui64.exe", "valve.net", "*.valve.net", "steamserver.net", "*.steampowered.com" })
        {
            Assert(exclusions.Contains(expected), $"Steam games should also route {expected}.");
        }

        Assert(exclusions.Count(item => item.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) == 1, "Steam companions should not duplicate a selected steam.exe.");
        AssertContains(config.ToToml(), "\"Hollow Knight.exe\"");
        return Task.CompletedTask;
    }

    private static Task TestServerConfigExpandsVmwareProcesses()
    {
        var config = new ServerConfig
        {
            Hostname = "vpn.example.com",
            Address = "203.0.113.10",
            Username = "alice",
            Password = "secret",
            SplitTunnelApps = [@"C:\Program Files (x86)\VMware\VMware Workstation\vmware.exe"]
        };

        var exclusions = config.BuildTomlExclusions();

        Assert(exclusions.Contains("vmware.exe"), "VMware Workstation launcher should remain excluded.");
        Assert(exclusions.Contains("vmware-vmx.exe"), "VMware VM process should be excluded with Workstation.");
        Assert(exclusions.Contains("vmnat.exe"), "VMware NAT service should be excluded with Workstation.");
        Assert(exclusions.Contains("vmnetdhcp.exe"), "VMware network service should be excluded with Workstation.");
        Assert(exclusions.Contains("vmplayer.exe"), "VMware Player should share Workstation's process family.");
        Assert(
            exclusions.Count(item => item.Equals("vmnat.exe", StringComparison.OrdinalIgnoreCase)) == 1,
            "VMware companion processes should not be duplicated.");
        return Task.CompletedTask;
    }

    private static Task TestServerConfigEmitsGeoIpRuntimeCidrs()
    {
        var config = new ServerConfig
        {
            Hostname = "vpn.example.com",
            Address = "203.0.113.10",
            Username = "alice",
            Password = "secret",
            SplitTunnelDomains = ["example.com"],
            SplitTunnelApps = ["chrome.exe"],
            SplitTunnelCountries = ["US"]
        };

        var toml = config.ToToml(["203.0.113.0/24", "2001:DB8::/32", "203.0.113.0/24"]);
        var exclusions = config.BuildTomlExclusions(["203.0.113.0/24", "2001:DB8::/32", "203.0.113.0/24"]);

        AssertContains(toml, "\"example.com\"");
        AssertContains(toml, "\"203.0.113.0/24\"");
        AssertContains(toml, "\"2001:db8::/32\"");
        AssertContains(toml, "\"chrome.exe\"");
        Assert(!toml.Contains("\"US\"", StringComparison.Ordinal), "GeoIP country codes should not be written directly to engine TOML.");
        Assert(exclusions.SequenceEqual(["example.com", "203.0.113.0/24", "2001:db8::/32", "chrome.exe"]), "Runtime CIDR exclusions should be normalized, deduped, and ordered before apps.");
        return Task.CompletedTask;
    }

    private static async Task TestGeoIpServiceDownloadsAndCachesCountryCidrs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var firstHandler = new FakeHttpMessageHandler(request =>
            {
                var uri = request.RequestUri?.ToString() ?? "";
                if (uri.EndsWith("/us-aggregated.zone", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("203.0.113.0/24\n203.0.113.0/24\nbad-entry\n")
                    };
                }

                if (uri.EndsWith("/ru-aggregated.zone", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("198.51.100.0/24\n")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            using var firstClient = new HttpClient(firstHandler);
            using var service = new GeoIpService(tempDir, firstClient);

            var first = await service.ResolveCountryCidrsAsync([" us ", "RU", "US", "bad"]);

            Assert(first.LoadedCountries.SequenceEqual(["RU", "US"]), "GeoIP country codes should normalize, dedupe, and sort before loading.");
            Assert(first.FailedCountries.Count == 0, "Countries with at least one address family should load successfully.");
            Assert(first.Cidrs.SequenceEqual(["198.51.100.0/24", "203.0.113.0/24"]), "Downloaded CIDR ranges should be normalized and deduped.");
            Assert(!first.UsedCache, "First successful load should come from the network.");

            var failingHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            using var failingClient = new HttpClient(failingHandler);
            using var cachedService = new GeoIpService(tempDir, failingClient);

            var cached = await cachedService.ResolveCountryCidrsAsync(["US"]);

            Assert(cached.UsedCache, "Network failure should fall back to cached GeoIP ranges.");
            Assert(cached.LoadedCountries.SequenceEqual(["US"]), "Cached country should still be reported as loaded.");
            Assert(cached.Cidrs.SequenceEqual(["203.0.113.0/24"]), "Cached CIDRs should be reused after network failure.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static Task TestServerConfigEscapesTomlStrings()
    {
        var config = new ServerConfig
        {
            Hostname = "vpn\"edge.example.com",
            Address = "203.0.113.10",
            Username = "alice\\ops",
            Password = "pa\"ss\\word",
            Dns = "https://dns.example.com/query?name=\"vpn\"",
            LogLevel = "debug",
            CustomSni = "front\\edge.example.com",
            SplitTunnelDomains = ["example.com"],
            SplitTunnelApps = ["custom\"tool.exe"]
        };

        var toml = config.ToToml();

        AssertContains(toml, "hostname = \"vpn\\\"edge.example.com\"");
        AssertContains(toml, "username = \"alice\\\\ops\"");
        AssertContains(toml, "password = \"pa\\\"ss\\\\word\"");
        AssertContains(toml, "dns_upstreams = [\"https://dns.example.com/query?name=\\\"vpn\\\"\"]");
        AssertContains(toml, "custom_sni = \"front\\\\edge.example.com\"");
        AssertContains(toml, "\"custom\\\"tool.exe\"");
        return Task.CompletedTask;
    }

    private static Task TestStatusDisplayTextMirrorsFlutter()
    {
        Assert(VpnStatus.Disconnected.DisplayText() == "Disconnected", "Disconnected status text should match Flutter.");
        Assert(VpnStatus.Connecting.DisplayText() == "Connecting...", "Connecting status text should match Flutter.");
        Assert(VpnStatus.Connected.DisplayText() == "Connected", "Connected status text should match Flutter.");
        Assert(VpnStatus.Disconnecting.DisplayText() == "Disconnecting...", "Disconnecting status text should match Flutter.");
        Assert(VpnStatus.Error.DisplayText() == "Error", "Error status text should match Flutter.");
        Assert(VpnStatus.Connecting.IsActive(), "Connecting should be active like Flutter.");
        Assert(VpnStatus.Connected.IsActive(), "Connected should be active like Flutter.");
        Assert(VpnStatus.Disconnecting.IsActive(), "Disconnecting should be active like Flutter.");
        Assert(!VpnStatus.Disconnected.IsActive(), "Disconnected should not be active.");
        Assert(!VpnStatus.Error.IsActive(), "Error should not be active.");

        Assert(SetupStep.Idle.DisplayText() == "Ready to install", "Idle setup text should match Flutter.");
        Assert(SetupStep.Connecting.DisplayText() == "Connecting via SSH...", "Connecting setup text should match Flutter.");
        Assert(SetupStep.CheckingSystem.DisplayText() == "Checking system...", "Checking setup text should match Flutter.");
        Assert(SetupStep.Installing.DisplayText() == "Installing Veil...", "Installing setup text should use the Veil brand.");
        Assert(SetupStep.ConfiguringServer.DisplayText() == "Configuring server...", "Configuring setup text should match Flutter.");
        Assert(SetupStep.ObtainingCertificate.DisplayText() == "Obtaining certificate...", "Certificate setup text should match Flutter.");
        Assert(SetupStep.StartingService.DisplayText() == "Starting service...", "Starting service setup text should match Flutter.");
        Assert(SetupStep.Verifying.DisplayText() == "Verifying...", "Verifying setup text should match Flutter.");
        Assert(SetupStep.Completed.DisplayText() == "Installation complete", "Completed setup text should match Flutter.");
        Assert(SetupStep.Failed.DisplayText() == "Installation failed", "Failed setup text should match Flutter.");

        Assert(SetupStep.Idle.StepIndex() == -1, "Idle step index should match Flutter.");
        Assert(SetupStep.Connecting.StepIndex() == 0, "Connecting step index should match Flutter.");
        Assert(SetupStep.CheckingSystem.StepIndex() == 1, "Checking step index should match Flutter.");
        Assert(SetupStep.Installing.StepIndex() == 2, "Installing step index should match Flutter.");
        Assert(SetupStep.ConfiguringServer.StepIndex() == 3, "Configuring step index should match Flutter.");
        Assert(SetupStep.ObtainingCertificate.StepIndex() == 4, "Certificate step index should match Flutter.");
        Assert(SetupStep.StartingService.StepIndex() == 5, "Starting service step index should match Flutter.");
        Assert(SetupStep.Verifying.StepIndex() == 6, "Verifying step index should match Flutter.");
        Assert(SetupStep.Completed.StepIndex() == 7, "Completed step index should match Flutter.");
        Assert(SetupStep.Failed.StepIndex() == -1, "Failed step index should match Flutter.");

        return Task.CompletedTask;
    }

    private static Task TestDomainGroupFlattening()
    {
        var data = new DomainGroupsData
        {
            Groups =
            [
                new DomainGroup
                {
                    Name = "Example",
                    PrimaryDomain = "example.com",
                    Domains = [" HTTPS://Example.COM/path?q=1 ", "cdn.example.com", "2001:DB8::/32"]
                }
            ],
            StandaloneDomains = ["example.com", " HTTPS://CDN.EXAMPLE.COM/asset ", "standalone.test", "2001:db8::/32"]
        };

        var flattened = data.FlattenDomains();
        var expected = new[] { "example.com", "cdn.example.com", "2001:db8::/32", "standalone.test" };

        Assert(flattened.Count == 4, $"Expected 4 unique domains, got {flattened.Count}.");
        Assert(flattened.SequenceEqual(expected), "Flattened domains should normalize entries, preserve group-first insertion order, and remove later duplicates.");
        Assert(flattened.Contains("example.com"), "Missing example.com.");
        Assert(flattened.Contains("cdn.example.com"), "Missing cdn.example.com.");
        Assert(flattened.Contains("standalone.test"), "Missing standalone.test.");
        return Task.CompletedTask;
    }

    private static Task TestDomainGroupLastDomainRemovalDeletesGroup()
    {
        var group = new DomainGroup
        {
            Id = "example",
            Name = "Example",
            PrimaryDomain = "example.com",
            Domains = ["example.com", "cdn.example.com"]
        };
        var data = new DomainGroupsData
        {
            Groups = [group],
            StandaloneDomains = ["standalone.test"]
        };

        var removedFirst = data.RemoveDomainFromGroup(group, "example.com");

        Assert(removedFirst, "Existing group domain should be removed.");
        Assert(data.Groups.Count == 1, "Group should remain while it still has domains.");
        Assert(data.Groups[0].Domains.SequenceEqual(["cdn.example.com"]), "Only the removed domain should be deleted.");

        var removedLast = data.RemoveDomainFromGroup(group, "CDN.EXAMPLE.COM");

        Assert(removedLast, "Last domain removal should be case-insensitive.");
        Assert(data.Groups.Count == 0, "Removing the last domain should delete the empty group, matching Flutter behavior.");
        Assert(data.FlattenDomains().SequenceEqual(["standalone.test"]), "Standalone domains should survive empty group cleanup.");

        return Task.CompletedTask;
    }

    private static Task TestDomainGroupDiscoverySelection()
    {
        var data = new DomainGroupsData
        {
            StandaloneDomains = ["existing.example.com"]
        };

        var emptySelectionAdded = data.AddDiscoveryResult(
            " HTTPS://Example.COM/path?q=1 ",
            createGroup: true,
            groupName: "Example",
            selectedDomains: []);

        Assert(emptySelectionAdded, "Empty discovery group selection should still add the primary domain.");
        Assert(data.Groups.Count == 0, "Empty discovery group selection should fall back to standalone, matching Flutter.");
        Assert(data.StandaloneDomains.SequenceEqual(["existing.example.com", "example.com"]), "Primary domain should be added as standalone.");

        var grouped = data.AddDiscoveryResult(
            " Shop.Example.COM ",
            createGroup: true,
            groupName: "Shop",
            selectedDomains:
            [
                "cdn.example.net",
                "CDN.EXAMPLE.NET",
                "existing.example.com"
            ]);

        Assert(grouped, "Discovery selection with related domains should create a group.");
        Assert(data.Groups.Count == 1, "Expected one discovery group.");
        Assert(data.Groups[0].Name == "Shop", "Discovery group name mismatch.");
        Assert(data.Groups[0].Domains.SequenceEqual(["shop.example.com", "cdn.example.net"]), "Discovery group should include primary first and remove duplicate or already-added domains.");

        var emptyNameGroup = data.AddDiscoveryResult(
            " HTTPS://Media.Example.ORG/path?q=1 ",
            createGroup: true,
            groupName: "",
            selectedDomains:
            [
                "assets.example.org"
            ]);

        Assert(emptyNameGroup, "Discovery selection with an empty group name should still create a group.");
        Assert(data.Groups[1].Name == "media.example.org", "Empty discovery group name should fall back to the normalized primary domain.");
        Assert(data.Groups[1].PrimaryDomain == "media.example.org", "Discovery primary domain should be normalized.");
        Assert(data.Groups[1].Domains.SequenceEqual(["media.example.org", "assets.example.org"]), "Discovery group should normalize URL-style primary domains.");

        var duplicateStandalone = data.AddStandaloneDomain("EXAMPLE.COM");
        Assert(!duplicateStandalone, "Standalone add should reject case-insensitive duplicates.");
        Assert(data.StandaloneDomains.SequenceEqual(["existing.example.com", "example.com"]), "Duplicate standalone add should not change persisted domains.");

        var normalizedCidr = data.AddDomainToGroup(data.Groups[0], "2001:DB8::/32");
        Assert(normalizedCidr, "Group add should accept normalized CIDR entries.");
        Assert(data.Groups[0].Domains.SequenceEqual(["shop.example.com", "cdn.example.net", "2001:db8::/32"]), "Group add should normalize domains, URLs, and CIDR entries.");

        var duplicateGroupDomain = data.AddDomainToGroup(data.Groups[0], "EXISTING.EXAMPLE.COM");
        Assert(!duplicateGroupDomain, "Group add should reject domains already present elsewhere.");
        Assert(data.Groups[0].Domains.SequenceEqual(["shop.example.com", "cdn.example.net", "2001:db8::/32"]), "Duplicate group add should not change group domains.");

        return Task.CompletedTask;
    }

    private static Task TestDomainGroupAddGroupPrimaryDomainSurvivesDuplicateCleanup()
    {
        var data = new DomainGroupsData
        {
            StandaloneDomains = ["example.com"]
        };

        var group = data.AddGroup(
            "",
            "https://example.com/duplicate-primary",
            ["API.Unique-Partner.NET"]);

        Assert(group != null, "Group with duplicate primary and unique child should still be created.");
        var createdGroup = group ?? throw new InvalidOperationException("Group with duplicate primary and unique child should still be created.");
        Assert(createdGroup.Name == "api.unique-partner.net", "Blank AddGroup name should fall back to the effective surviving primary domain.");
        Assert(createdGroup.PrimaryDomain == "api.unique-partner.net", "AddGroup primary domain should point to a surviving normalized domain.");
        Assert(createdGroup.Domains.SequenceEqual(["api.unique-partner.net"]), "Duplicate primary should be dropped while the unique child domain survives.");
        Assert(data.FlattenDomains().SequenceEqual(["api.unique-partner.net", "example.com"]), "Flattening should keep group domains before standalone domains.");

        return Task.CompletedTask;
    }

    private static async Task TestDomainGroupsNullListDefaults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "domain_groups.json"),
                """
                {
                  "version": null,
                  "groups": null,
                  "standaloneDomains": null
                }
                """);

            var service = new ConfigService(tempDir);
            var groups = await service.LoadDomainGroupsAsync();

            Assert(groups.Version == DomainGroupsData.CurrentVersion, "Null domain-groups version should use current version.");
            Assert(groups.Groups.Count == 0, "Null groups should import as an empty list.");
            Assert(groups.StandaloneDomains.Count == 0, "Null standaloneDomains should import as an empty list.");
            Assert(groups.FlattenDomains().Count == 0, "Flattening null-list defaults should be empty.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceNormalizesPersistedDomainGroups()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "domain_groups.json"),
                """
                {
                  "version": 1,
                  "groups": [
                    {
                      "id": "group-one",
                      "name": " Example Group ",
                      "primaryDomain": " HTTPS://Primary.Example.COM/path ",
                      "domains": [
                        " HTTPS://Example.COM/app ",
                        "example.com",
                        "CDN.Example.NET",
                        ""
                      ]
                    },
                    {
                      "id": "duplicate-only",
                      "name": "Duplicate Only",
                      "primaryDomain": "https://example.com/again",
                      "domains": ["EXAMPLE.COM"]
                    },
                    {
                      "id": "duplicate-primary-with-child",
                      "name": " ",
                      "primaryDomain": "https://example.com/duplicate-primary",
                      "domains": ["API.Unique-Partner.NET"]
                    }
                  ],
                  "standaloneDomains": [
                    " HTTPS://Standalone.TEST/path ",
                    "cdn.example.net",
                    "2001:DB8::/32",
                    ""
                  ]
                }
                """);

            var service = new ConfigService(tempDir);
            var loaded = await service.LoadDomainGroupsAsync();

            Assert(loaded.Groups.Count == 2, "Groups with only duplicate normalized domains should be removed on load while groups with unique children survive.");
            Assert(loaded.Groups[0].Id == "group-one", "Group id should be preserved.");
            Assert(loaded.Groups[0].Name == "Example Group", "Group name should be trimmed.");
            Assert(loaded.Groups[0].PrimaryDomain == "primary.example.com", "Primary domain should be normalized on load.");
            Assert(
                loaded.Groups[0].Domains.SequenceEqual(["primary.example.com", "example.com", "cdn.example.net"]),
                "Group domains should include normalized primary first and remove duplicates.");
            Assert(loaded.Groups[1].Id == "duplicate-primary-with-child", "Group with duplicate primary and unique child should survive.");
            Assert(loaded.Groups[1].Name == "api.unique-partner.net", "Blank group name should fall back to the effective primary domain.");
            Assert(loaded.Groups[1].PrimaryDomain == "api.unique-partner.net", "Effective primary domain should point to a domain that survived normalization.");
            Assert(loaded.Groups[1].Domains.SequenceEqual(["api.unique-partner.net"]), "Group with duplicate primary should keep its unique child domain.");
            Assert(
                loaded.StandaloneDomains.SequenceEqual(["standalone.test", "2001:db8::/32"]),
                "Standalone domains should be normalized and deduped against groups.");

            await service.SaveDomainGroupsAsync(loaded);
            var reloaded = await service.LoadDomainGroupsAsync();
            var json = await File.ReadAllTextAsync(Path.Combine(tempDir, "domain_groups.json"));

            Assert(reloaded.FlattenDomains().SequenceEqual(["primary.example.com", "example.com", "cdn.example.net", "api.unique-partner.net", "standalone.test", "2001:db8::/32"]), "Normalized domain groups should round-trip in group-first order.");
            Assert(!json.Contains("HTTPS://", StringComparison.OrdinalIgnoreCase), "Saved domain groups should not keep raw URL entries.");
            Assert(!json.Contains("CDN.Example.NET", StringComparison.Ordinal), "Saved domain groups should not keep mixed-case raw domains.");
            Assert(!json.Contains("duplicate-only", StringComparison.Ordinal), "Saved domain groups should omit groups that only contain duplicates.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static Task TestServerSetupToml()
    {
        var config = new ServerSetupConfig
        {
            Domain = "vpn.example.com",
            ListenPort = 443,
            VpnUsername = "alice",
            VpnPassword = "secret"
        };

        AssertContains(config.GenerateVpnToml(), "listen_address = \"0.0.0.0:443\"");
        AssertContains(config.GenerateVpnToml(), "[listen_protocols.http2]");
        AssertContains(config.GenerateCredentialsToml(), "username = \"alice\"");
        AssertContains(config.GenerateCredentialsToml(), "password = \"secret\"");
        AssertContains(config.GenerateHostsToml(), "hostname = \"vpn.example.com\"");
        AssertContains(config.GenerateHostsToml(), "/etc/letsencrypt/live/vpn.example.com/fullchain.pem");
        return Task.CompletedTask;
    }

    private static Task TestServerSetupTomlEscapesStrings()
    {
        var config = new ServerSetupConfig
        {
            Domain = "vpn\"edge.example.com",
            ListenPort = 8443,
            VpnUsername = "alice\\ops",
            VpnPassword = "pa\"ss\\word"
        };

        var credentialsToml = config.GenerateCredentialsToml();
        var hostsToml = config.GenerateHostsToml();

        AssertContains(credentialsToml, "username = \"alice\\\\ops\"");
        AssertContains(credentialsToml, "password = \"pa\\\"ss\\\\word\"");
        AssertContains(hostsToml, "hostname = \"vpn\\\"edge.example.com\"");
        AssertContains(hostsToml, "/etc/letsencrypt/live/vpn\\\"edge.example.com/fullchain.pem");
        return Task.CompletedTask;
    }

    private static async Task TestConfigServiceJsonCompatibility()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var legacyJson = """
                {
                  "hostname": "vpn.example.com",
                  "address": "203.0.113.10",
                  "port": 8443,
                  "hasIpv6": false,
                  "username": "alice",
                  "password": "secret",
                  "skipVerification": true,
                  "upstreamProtocol": "http3",
                  "antiDpi": true,
                  "dns": "8.8.8.8",
                  "logLevel": "debug",
                  "customSni": "front.example.com",
                  "postQuantumGroupEnabled": false,
                  "vpnMode": "selective",
                  "splitTunnelDomains": ["example.com"],
                  "splitTunnelApps": ["chrome.exe"],
                  "splitTunnelCountries": ["US"]
                }
                """;

            var importPath = Path.Combine(tempDir, "legacy.json");
            await File.WriteAllTextAsync(importPath, legacyJson);

            var service = new ConfigService(tempDir, [Path.Combine(tempDir, "client")]);
            var imported = await service.ImportConfigAsync(importPath);

            Assert(imported.VpnMode == VpnMode.Selective, "vpnMode was not imported as selective.");
            Assert(imported.SplitTunnelDomains.SequenceEqual(["example.com"]), "splitTunnelDomains import mismatch.");
            Assert(imported.SplitTunnelApps.SequenceEqual(["chrome.exe"]), "splitTunnelApps import mismatch.");
            Assert(imported.SplitTunnelCountries.SequenceEqual(["US"]), "splitTunnelCountries import mismatch.");

            var exportPath = Path.Combine(tempDir, "export.json");
            await service.ExportConfigAsync(imported, exportPath);
            var exported = await File.ReadAllTextAsync(exportPath);

            AssertContains(exported, "\"vpnMode\": \"selective\"");
            AssertContains(exported, "\"splitTunnelDomains\"");
            AssertContains(exported, "\"splitTunnelCountries\"");
            Assert(!exported.Contains("\"VpnMode\"", StringComparison.Ordinal), "Export should use camelCase property names.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceMigratesLegacyFlutterPreferences()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var legacyConfig = """
                {
                  "hostname": "vpn.example.com",
                  "address": "203.0.113.10",
                  "port": 443,
                  "username": "alice",
                  "password": "secret",
                  "vpnMode": "general",
                  "splitTunnelDomains": ["old.example.com"],
                  "splitTunnelApps": ["deadlock.exe"]
                }
                """;
            var legacyGroups = """
                {
                  "version": 1,
                  "groups": [],
                  "standaloneDomains": ["old.example.com"]
                }
                """;
            var preferencesPath = Path.Combine(tempDir, "shared_preferences.json");
            var preferences = new Dictionary<string, string>
            {
                ["flutter.server_config"] = legacyConfig,
                ["flutter.domain_groups"] = legacyGroups
            };
            await File.WriteAllTextAsync(preferencesPath, JsonSerializer.Serialize(preferences));

            var service = new ConfigService(tempDir, legacyFlutterPreferencesPath: preferencesPath);
            var migrated = await service.LoadConfigAsync();
            var groups = await service.LoadDomainGroupsAsync();
            var persisted = await File.ReadAllTextAsync(Path.Combine(tempDir, "config.json"));

            Assert(migrated.Hostname == "vpn.example.com", "Legacy Flutter hostname should migrate.");
            Assert(migrated.SplitTunnelApps.SequenceEqual(["deadlock.exe"]), "Legacy Flutter app exclusions should migrate.");
            Assert(groups.StandaloneDomains.SequenceEqual(["old.example.com"]), "Legacy Flutter domain groups should migrate.");
            AssertContains(persisted, "\"vpnMode\": \"general\"");
            AssertContains(persisted, "\"deadlock.exe\"");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceImportPersistsSplitTunnelState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            await service.SaveConfigAsync(new ServerConfig
            {
                Hostname = "old.example.com",
                Address = "198.51.100.5",
                Username = "old-user",
                Password = "old-password",
                SplitTunnelDomains = ["old.example.com"],
                SplitTunnelApps = ["old.exe"]
            });
            await service.SaveDomainGroupsAsync(new DomainGroupsData
            {
                StandaloneDomains = ["old.example.com"],
                Groups =
                [
                    new DomainGroup
                    {
                        Name = "Old",
                        PrimaryDomain = "old.example.com",
                        Domains = ["legacy.example.com"]
                    }
                ]
            });

            var importedJson = """
                {
                  "hostname": "vpn.example.com",
                  "address": "203.0.113.10",
                  "port": 8443,
                  "username": "alice",
                  "password": "secret",
                  "vpnMode": "selective",
                  "splitTunnelDomains": ["Example.COM", "https://cdn.example.com/path", "10.0.0.0/8"],
                  "splitTunnelApps": [" Steam.exe ", "chrome.exe", "steam.EXE", "", "  "]
                }
                """;

            var importPath = Path.Combine(tempDir, "import.json");
            await File.WriteAllTextAsync(importPath, importedJson);

            var imported = await service.ImportConfigAndPersistAsync(importPath);
            var loadedConfig = await service.LoadConfigAsync();
            var loadedGroups = await service.LoadDomainGroupsAsync();

            var expectedDomains = new[] { "example.com", "cdn.example.com", "10.0.0.0/8" };
            Assert(imported.SplitTunnelDomains.SequenceEqual(expectedDomains), "Imported domains were not normalized.");
            Assert(imported.SplitTunnelApps.SequenceEqual(["chrome.exe", "Steam.exe"]), "Imported runtime apps were not normalized.");
            Assert(loadedConfig.SplitTunnelDomains.SequenceEqual(expectedDomains), "Persisted config domains mismatch.");
            Assert(loadedConfig.SplitTunnelApps.SequenceEqual(["chrome.exe", "Steam.exe"]), "Persisted split-tunnel apps mismatch.");
            Assert(loadedGroups.Groups.Count == 0, "Import should replace stale domain groups.");
            Assert(loadedGroups.StandaloneDomains.SequenceEqual(expectedDomains), "Persisted domain groups did not mirror imported domains.");
            Assert(!loadedGroups.FlattenDomains().Contains("legacy.example.com"), "Stale grouped domain survived import.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestPasswordlessConfigImport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var draftJson = """
                {
                  "hostname": "vpn.example.com",
                  "address": "203.0.113.10",
                  "port": 443,
                  "username": "alice",
                  "vpnMode": "general",
                  "splitTunnelDomains": ["example.com"],
                  "splitTunnelApps": []
                }
                """;

            var importPath = Path.Combine(tempDir, "draft.json");
            await File.WriteAllTextAsync(importPath, draftJson);

            var service = new ConfigService(tempDir, [Path.Combine(tempDir, "client")]);
            var imported = await service.ImportConfigAsync(importPath);

            Assert(imported.Hostname == "vpn.example.com", "Hostname was not imported.");
            Assert(imported.Address == "203.0.113.10", "Address was not imported.");
            Assert(imported.Username == "alice", "Username was not imported.");
            Assert(imported.Password == "", "Missing password should import as empty draft value.");
            Assert(imported.Dns == "8.8.8.8", "Missing DNS should use default value.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestPartialFlutterConfigImportDefaults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var partialJson = """
                {
                  "hostname": null,
                  "address": "203.0.113.10",
                  "port": "8443",
                  "hasIpv6": null,
                  "username": null,
                  "password": null,
                  "skipVerification": true,
                  "upstreamProtocol": null,
                  "antiDpi": false,
                  "dns": null,
                  "logLevel": null,
                  "customSni": null,
                  "postQuantumGroupEnabled": null,
                  "vpnMode": "selective",
                  "splitTunnelDomains": ["Example.COM", 10],
                  "splitTunnelApps": null
                }
                """;

            var importPath = Path.Combine(tempDir, "partial.json");
            await File.WriteAllTextAsync(importPath, partialJson);

            var service = new ConfigService(tempDir, [Path.Combine(tempDir, "client")]);
            var imported = await service.ImportConfigAsync(importPath);
            await service.WriteConfigFileAsync(imported);
            var toml = await File.ReadAllTextAsync(await service.GetConfigFilePathAsync());

            Assert(imported.Hostname == "vpn.example.com", "Null hostname should use Flutter default.");
            Assert(imported.Address == "203.0.113.10", "Non-null address should be imported.");
            Assert(imported.Port == 8443, "String port should import as a convenience compatibility fallback.");
            Assert(imported.HasIpv6, "Null hasIpv6 should use Flutter default.");
            Assert(imported.Username == "your-username", "Null username should use Flutter default.");
            Assert(imported.Password == "", "Null password should use Flutter default.");
            Assert(imported.UpstreamProtocol == "http2", "Null upstreamProtocol should use Flutter default.");
            Assert(imported.Dns == "8.8.8.8", "Null DNS should use Flutter default.");
            Assert(imported.LogLevel == "info", "Null logLevel should use Flutter default.");
            Assert(imported.PostQuantumGroupEnabled, "Null postQuantumGroupEnabled should use Flutter default.");
            Assert(imported.VpnMode == VpnMode.Selective, "vpnMode should still import when other fields use defaults.");
            Assert(imported.SplitTunnelDomains.SequenceEqual(["Example.COM", "10"]), "Array entries should import with Flutter-style toString behavior.");
            Assert(imported.SplitTunnelApps.Count == 0, "Null splitTunnelApps should become an empty list.");
            AssertContains(toml, "hostname = \"vpn.example.com\"");
            AssertContains(toml, "loglevel = \"info\"");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceHandlesNonAsciiPathWithSpaces()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"), "Мой профиль", "Конфиг Veil");

        try
        {
            var service = new ConfigService(tempDir);
            var config = new ServerConfig { Hostname = "vpn.example.com", Address = "203.0.113.10" };
            await service.SaveConfigAsync(config);
            await service.WriteConfigFileAsync(config);

            var configPath = await service.GetConfigFilePathAsync();
            Assert(configPath.StartsWith(tempDir, StringComparison.Ordinal), "Client config should live in the non-ASCII app data directory.");
            Assert(File.Exists(configPath), "Client config should be written to a path with Cyrillic and spaces.");
            Assert((await service.LoadConfigAsync()).Hostname == "vpn.example.com", "Config should round-trip through a non-ASCII path.");

            var exportPath = Path.Combine(tempDir, "экспорт конфига.json");
            await service.ExportConfigAsync(config, exportPath);
            Assert((await service.ImportConfigAsync(exportPath)).Address == "203.0.113.10", "Export/import should work with a Cyrillic file name.");
        }
        finally
        {
            var root = Path.GetFullPath(Path.Combine(tempDir, "..", ".."));
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task TestConfigServiceRejectsMalformedImport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var importPath = Path.Combine(tempDir, "malformed.json");
            await File.WriteAllTextAsync(importPath, "{ this is not json");

            var service = new ConfigService(tempDir);
            await AssertThrowsAsync<System.Text.Json.JsonException>(() => service.ImportConfigAsync(importPath));

            var nonObjectPath = Path.Combine(tempDir, "array.json");
            await File.WriteAllTextAsync(nonObjectPath, "[]");

            await AssertThrowsAsync<InvalidDataException>(() => service.ImportConfigAsync(nonObjectPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceSplitTunnelSavePreservesClientDraft()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            await service.SaveConfigAsync(new ServerConfig
            {
                Hostname = "",
                Address = "",
                Port = 8443,
                Username = "",
                Password = "",
                Dns = "",
                LogLevel = "debug",
                CustomSni = "draft.example.com",
                VpnMode = VpnMode.General,
                SplitTunnelDomains = ["old.example.com"],
                SplitTunnelApps = ["old.exe"],
                SplitTunnelCountries = ["DE"]
            });

            var domainGroups = new DomainGroupsData
            {
                Groups =
                [
                    new DomainGroup
                    {
                        Id = "example",
                        Name = "Example",
                        PrimaryDomain = "example.com",
                        Domains = ["example.com", "cdn.example.com"]
                    }
                ],
                StandaloneDomains = ["example.com", "10.0.0.0/8"]
            };

            var saved = await service.SaveSplitTunnelStateAsync(
                domainGroups,
                VpnMode.Selective,
                ["chrome.exe", "Chrome.exe", "", "steam.exe"],
                [" us ", "RU", "usa"]);

            var loaded = await service.LoadConfigAsync();
            var loadedGroups = await service.LoadDomainGroupsAsync();

            Assert(saved.Hostname == "", "Split tunnel save should preserve draft hostname.");
            Assert(saved.Address == "", "Split tunnel save should preserve draft address.");
            Assert(saved.Username == "", "Split tunnel save should preserve draft username.");
            Assert(saved.Password == "", "Split tunnel save should preserve draft password.");
            Assert(saved.Dns == "", "Split tunnel save should preserve draft DNS.");
            Assert(saved.Port == 8443, "Split tunnel save should preserve unrelated port.");
            Assert(saved.LogLevel == "debug", "Split tunnel save should preserve unrelated log level.");
            Assert(saved.CustomSni == "draft.example.com", "Split tunnel save should preserve unrelated SNI.");
            Assert(loaded.VpnMode == VpnMode.Selective, "Split tunnel save should update VPN mode.");
            Assert(loaded.SplitTunnelDomains.Count == 3, "Split tunnel save should flatten unique domains.");
            Assert(loaded.SplitTunnelDomains.SequenceEqual(["example.com", "cdn.example.com", "10.0.0.0/8"]), "Split tunnel save should preserve flattened domain order.");
            Assert(loaded.SplitTunnelDomains.Contains("example.com"), "Flattened primary domain missing.");
            Assert(loaded.SplitTunnelDomains.Contains("cdn.example.com"), "Flattened grouped domain missing.");
            Assert(loaded.SplitTunnelDomains.Contains("10.0.0.0/8"), "Flattened standalone CIDR missing.");
            Assert(loaded.SplitTunnelApps.SequenceEqual(["chrome.exe", "steam.exe"]), "Split tunnel save should normalize app selections.");
            Assert(loaded.SplitTunnelCountries.SequenceEqual(["RU", "US"]), "Split tunnel save should normalize GeoIP country selections.");
            Assert(loadedGroups.Groups.Count == 1, "Domain groups should be persisted by split tunnel save.");
            Assert(loadedGroups.Groups[0].Domains.SequenceEqual(["example.com", "cdn.example.com"]), "Grouped domains should be persisted by split tunnel save.");
            Assert(loadedGroups.StandaloneDomains.SequenceEqual(["10.0.0.0/8"]), "Standalone domain duplicates should be removed by split tunnel save.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestPasswordlessConfigExport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            var exportPath = Path.Combine(tempDir, "draft-export.json");
            var draft = new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Port = 443,
                Username = "alice",
                Password = "",
                Dns = "8.8.8.8",
                VpnMode = VpnMode.General,
                SplitTunnelDomains = ["example.com"]
            };

            await service.ExportConfigAsync(draft, exportPath);
            var exported = await File.ReadAllTextAsync(exportPath);

            AssertContains(exported, "\"password\": \"\"");
            AssertContains(exported, "\"vpnMode\": \"general\"");
            AssertContains(exported, "\"splitTunnelDomains\"");
            Assert(!exported.Contains("\"Password\"", StringComparison.Ordinal), "Export should use camelCase property names.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceSaveLoadRoundTrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            var config = new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                ConnectionMode = VpnConnectionMode.SystemProxy,
                VpnMode = VpnMode.General,
                SplitTunnelDomains = ["example.com", "cdn.example.com"],
                SplitTunnelApps = ["chrome.exe", "steam.exe"],
                SplitTunnelCountries = ["US", "DE"]
            };

            await service.SaveConfigAsync(config);
            var loaded = await service.LoadConfigAsync();

            Assert(loaded.ConnectionMode == VpnConnectionMode.SystemProxy, "Connection mode should remain System Proxy after save/load.");
            Assert(loaded.VpnMode == VpnMode.General, "VPN mode should remain general after save/load.");
            Assert(loaded.SplitTunnelDomains.SequenceEqual(config.SplitTunnelDomains), "Domain list changed after save/load.");
            Assert(loaded.SplitTunnelApps.SequenceEqual(config.SplitTunnelApps), "App list changed after save/load.");
            Assert(loaded.SplitTunnelCountries.SequenceEqual(["DE", "US"]), "GeoIP country list should normalize and round-trip after save/load.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceNormalizesPersistedSplitTunnelEntries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "config.json"),
                """
                {
                  "hostname": "vpn.example.com",
                  "address": "203.0.113.10",
                  "port": 443,
                  "username": "alice",
                  "password": "secret",
                  "dns": "8.8.8.8",
                  "splitTunnelDomains": [" HTTPS://Example.COM/path ", "example.com", "", "10.0.0.0/8"],
                  "splitTunnelApps": [" Steam.exe ", "chrome.exe", "steam.EXE", "", "  "],
                  "splitTunnelCountries": [" us ", "DE", "US", "usa", "R1"]
                }
                """);

            var service = new ConfigService(tempDir);
            var loaded = await service.LoadConfigAsync();

            Assert(loaded.SplitTunnelDomains.SequenceEqual(["example.com", "10.0.0.0/8"]), "Persisted domain selections should be normalized, deduped, and kept in order on load.");
            Assert(loaded.SplitTunnelApps.SequenceEqual(["chrome.exe", "Steam.exe"]), "Persisted app selections should be trimmed, deduped, and sorted on load.");
            Assert(loaded.SplitTunnelCountries.SequenceEqual(["DE", "US"]), "Persisted GeoIP countries should be normalized, deduped, and sorted on load.");

            await service.SaveConfigAsync(new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                SplitTunnelDomains = [" https://Api.Example.COM/v1 ", "api.example.com", "192.0.2.0/24", ""],
                SplitTunnelApps = [" slack.exe ", "SLACK.EXE", "code.exe", ""],
                SplitTunnelCountries = [" ru ", "US", "RU", "usa"]
            });

            loaded = await service.LoadConfigAsync();
            var json = await File.ReadAllTextAsync(Path.Combine(tempDir, "config.json"));

            Assert(loaded.SplitTunnelDomains.SequenceEqual(["api.example.com", "192.0.2.0/24"]), "Saved domain selections should be canonicalized.");
            Assert(loaded.SplitTunnelApps.SequenceEqual(["code.exe", "slack.exe"]), "Saved app selections should be canonicalized.");
            Assert(loaded.SplitTunnelCountries.SequenceEqual(["RU", "US"]), "Saved GeoIP countries should be canonicalized.");
            Assert(!json.Contains("Api.Example.COM", StringComparison.Ordinal), "Raw URL-style domain casing should not be persisted.");
            Assert(!json.Contains("SLACK.EXE", StringComparison.Ordinal), "Duplicate app casing should not be persisted.");
            Assert(!json.Contains(" slack.exe ", StringComparison.Ordinal), "App selections should not persist surrounding whitespace.");
            Assert(!json.Contains("\"usa\"", StringComparison.OrdinalIgnoreCase), "Invalid GeoIP country codes should not be persisted.");

            await service.SaveSplitTunnelStateAsync(
                new DomainGroupsData(),
                VpnMode.Selective,
                [" Telegram.exe ", "telegram.EXE", "discord.exe", ""]);

            loaded = await service.LoadConfigAsync();
            Assert(loaded.SplitTunnelApps.SequenceEqual(["discord.exe", "Telegram.exe"]), "Split tunnel autosave should canonicalize app selections.");
            Assert(loaded.SplitTunnelCountries.SequenceEqual(["RU", "US"]), "Split tunnel autosave should preserve existing GeoIP selections when no country list is supplied.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceNormalizesPersistedGeoIpCountries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "config.json"), """
                {
                  "hostname": "vpn.example.com",
                  "address": "203.0.113.10",
                  "port": 443,
                  "username": "alice",
                  "password": "secret",
                  "splitTunnelCountries": [" us ", "DE", "R1", "usa", "de", ""]
                }
                """);

            var service = new ConfigService(tempDir);
            var loaded = await service.LoadConfigAsync();

            Assert(GeoIpCountryCatalog.ContainsCode("US"), "GeoIP country catalog should include United States.");
            Assert(GeoIpCountryCatalog.ContainsCode("RU"), "GeoIP country catalog should include Russia.");
            Assert(loaded.SplitTunnelCountries.SequenceEqual(["DE", "US"]), "GeoIP countries should normalize to distinct ISO alpha-2 codes.");

            await service.SaveConfigAsync(new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                SplitTunnelCountries = [" ru ", "US", "RU", "usa", "not-a-country"]
            });

            loaded = await service.LoadConfigAsync();
            var json = await File.ReadAllTextAsync(Path.Combine(tempDir, "config.json"));

            Assert(loaded.SplitTunnelCountries.SequenceEqual(["RU", "US"]), "Saved GeoIP country selections should be canonicalized.");
            AssertContains(json, "\"splitTunnelCountries\"");
            Assert(!json.Contains("\"usa\"", StringComparison.OrdinalIgnoreCase), "Invalid three-letter country code should not be persisted.");
            Assert(!json.Contains("not-a-country", StringComparison.OrdinalIgnoreCase), "Unknown country code should not be persisted.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceNormalizesSplitTunnelAppPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "config.json"), """
                {
                  "hostname": "vpn.example.com",
                  "address": "203.0.113.10",
                  "port": 443,
                  "username": "alice",
                  "password": "secret",
                  "splitTunnelApps": [
                    "\"D:\\Steam\\steamapps\\common\\Deadlock\\game\\bin\\win64\\deadlock.exe\" -steam",
                    "Deadlock.exe",
                    "steam"
                  ]
                }
                """);

            var service = new ConfigService(tempDir);
            var loaded = await service.LoadConfigAsync();

            Assert(loaded.SplitTunnelApps.SequenceEqual(["deadlock.exe", "steam.exe"]), "Persisted app paths and bare names should normalize to process executable names.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceSaveDoesNotMutateCallerConfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            var config = new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                SplitTunnelDomains = [" HTTPS://Example.COM/path ", "example.com", ""],
                SplitTunnelApps = [" slack.exe ", "SLACK.EXE", "code.exe", ""],
                SplitTunnelCountries = [" us ", "RU", "usa"]
            };
            var originalDomains = config.SplitTunnelDomains.ToList();
            var originalApps = config.SplitTunnelApps.ToList();
            var originalCountries = config.SplitTunnelCountries.ToList();

            await service.SaveConfigAsync(config);
            var loaded = await service.LoadConfigAsync();

            Assert(config.SplitTunnelDomains.SequenceEqual(originalDomains), "Saving config should not mutate the caller's domain draft.");
            Assert(config.SplitTunnelApps.SequenceEqual(originalApps), "Saving config should not mutate the caller's app draft.");
            Assert(config.SplitTunnelCountries.SequenceEqual(originalCountries), "Saving config should not mutate the caller's GeoIP country draft.");
            Assert(loaded.SplitTunnelDomains.SequenceEqual(["example.com"]), "Persisted domain selections should still be canonicalized.");
            Assert(loaded.SplitTunnelApps.SequenceEqual(["code.exe", "slack.exe"]), "Persisted app selections should still be canonicalized.");
            Assert(loaded.SplitTunnelCountries.SequenceEqual(["RU", "US"]), "Persisted GeoIP country selections should still be canonicalized.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceLoadConnectionConfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            await service.SaveConfigAsync(new ServerConfig
            {
                Hostname = "saved.example.com",
                Address = "203.0.113.10",
                Port = 8443,
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8",
                LogLevel = "debug"
            });

            var loaded = await service.LoadConnectionConfigAsync();

            Assert(loaded.Hostname == "saved.example.com", "Connection config should come from persisted settings.");
            Assert(loaded.Port == 8443, "Connection config port mismatch.");
            Assert(loaded.LogLevel == "debug", "Connection config log level mismatch.");

            await service.SaveConfigAsync(new ServerConfig
            {
                Hostname = "draft.example.com",
                Address = "203.0.113.11",
                Username = "alice",
                Password = "",
                Dns = "8.8.8.8"
            });

            var ex = await AssertThrowsAsync<InvalidOperationException>(() => service.LoadConnectionConfigAsync());
            Assert(ex.Message == "Enter password.", "Connection config should require password before connect.");

            await service.SaveConfigAsync(new ServerConfig
            {
                Hostname = "draft.example.com",
                Address = "203.0.113.11",
                Port = 0,
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            });

            ex = await AssertThrowsAsync<InvalidOperationException>(() => service.LoadConnectionConfigAsync());
            Assert(ex.Message == "Enter a valid port.", "Connection config should reject port numbers below 1 before connect.");

            await service.SaveConfigAsync(new ServerConfig
            {
                Hostname = "draft.example.com",
                Address = "203.0.113.11",
                Port = 65536,
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            });

            ex = await AssertThrowsAsync<InvalidOperationException>(() => service.LoadConnectionConfigAsync());
            Assert(ex.Message == "Enter a valid port.", "Connection config should reject port numbers above 65535 before connect.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConnectionFormRejectsInvalidPorts()
    {
        Assert(ConnectionForm.ParsePort("1") == 1, "Minimum valid client port should parse.");
        Assert(ConnectionForm.ParsePort("65535") == 65535, "Maximum valid client port should parse.");
        Assert(ConnectionForm.ParsePort(" 8443 ") == 8443, "Client port parser should trim whitespace.");

        foreach (var invalid in new[] { "0", "65536", "not-a-port", "" })
        {
            var error = await AssertThrowsAsync<InvalidOperationException>(() =>
                Task.FromResult(ConnectionForm.ParsePort(invalid)));
            Assert(error.Message.Contains("1 and 65535", StringComparison.Ordinal), $"Port '{invalid}' should be rejected with a helpful message.");
        }
    }

    private static Task TestPasswordGeneratorUsesFlutterAlphabet()
    {
        Assert(PasswordGenerator.DefaultLength == 16, "Generated VPN passwords should match Flutter's 16-character length.");
        Assert(PasswordGenerator.Alphabet == "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#%^&*", "Generated VPN password alphabet should match Flutter.");

        var seen = new HashSet<string>();
        for (var i = 0; i < 32; i++)
        {
            var password = PasswordGenerator.Generate();
            Assert(password.Length == PasswordGenerator.DefaultLength, "Generated VPN password length mismatch.");
            Assert(password.All(PasswordGenerator.Alphabet.Contains), "Generated VPN password contains a character outside the Flutter alphabet.");
            seen.Add(password);
        }

        Assert(seen.Count == 32, "Generated VPN passwords should not repeat.");
        return Task.CompletedTask;
    }

    private static Task TestConnectionFormAppliesToDetachedCopy()
    {
        var saved = new ServerConfig
        {
            Hostname = "saved.example.com",
            Address = "198.51.100.10",
            Port = 443,
            Username = "saved-user",
            Password = "saved-secret",
            Dns = "1.1.1.1",
            UpstreamProtocol = "http2",
            LogLevel = "info",
            HasIpv6 = true,
            SkipVerification = false,
            AntiDpi = false,
            PostQuantumGroupEnabled = true,
            CustomSni = "",
            ConnectionMode = VpnConnectionMode.FullTunnel,
            VpnMode = VpnMode.Selective,
            SplitTunnelDomains = ["saved.example.com"],
            SplitTunnelExceptions = ["keep.example.com"],
            SplitTunnelApps = [@"C:\Saved\App.exe"],
            SplitTunnelCountries = ["DE"]
        };

        var form = new ConnectionForm(
            " draft.example.com ",
            " 203.0.113.20 ",
            " 8443 ",
            " draft-user ",
            "draft-secret",
            " 9.9.9.9 ",
            "http3",
            "debug",
            " sni.example.com ",
            HasIpv6: false,
            SkipVerification: true,
            AntiDpi: true,
            PostQuantumGroupEnabled: false,
            ConnectionMode: VpnConnectionMode.SystemProxy);

        var draft = form.ApplyTo(saved);
        draft.SplitTunnelDomains.Add("mutated.example.com");
        draft.SplitTunnelExceptions.Clear();

        Assert(!ReferenceEquals(saved, draft), "Applying the form should return a detached config object.");
        Assert(saved.Hostname == "saved.example.com" && saved.Port == 443, "Applying the form should not mutate the saved config.");
        Assert(saved.SplitTunnelDomains.SequenceEqual(["saved.example.com"]), "Draft split-tunnel lists should be copies.");
        Assert(saved.SplitTunnelExceptions.SequenceEqual(["keep.example.com"]), "Draft exception lists should be copies.");
        Assert(draft.Hostname == "draft.example.com", "Draft hostname should be trimmed.");
        Assert(draft.Address == "203.0.113.20", "Draft address should be trimmed.");
        Assert(draft.Port == 8443, "Draft port should come from the parsed form value.");
        Assert(draft.Username == "draft-user", "Draft username should be trimmed.");
        Assert(draft.Password == "draft-secret", "Draft password should preserve the entered value.");
        Assert(draft.Dns == "9.9.9.9", "Draft DNS should be trimmed.");
        Assert(draft.UpstreamProtocol == "http3", "Draft protocol should match the form.");
        Assert(draft.LogLevel == "debug", "Draft log level should match the form.");
        Assert(!draft.HasIpv6 && draft.SkipVerification && draft.AntiDpi && !draft.PostQuantumGroupEnabled, "Draft booleans should match the form.");
        Assert(draft.CustomSni == "sni.example.com", "Draft custom SNI should be trimmed.");
        Assert(draft.ConnectionMode == VpnConnectionMode.SystemProxy, "Draft connection mode should match the form.");
        Assert(draft.VpnMode == VpnMode.Selective, "The Connection page must keep the routing mode.");
        Assert(draft.SplitTunnelApps.SequenceEqual([@"C:\Saved\App.exe"]) && draft.SplitTunnelCountries.SequenceEqual(["DE"]), "The Connection page must keep app and country rules.");

        var roundTrip = ConnectionForm.FromConfig(saved).ApplyTo(saved);
        Assert(roundTrip.Hostname == saved.Hostname && roundTrip.Port == saved.Port && roundTrip.Password == saved.Password, "A form built from a config should reproduce it.");
        return Task.CompletedTask;
    }

    private static Task TestDomainGroupsApplyDiscoveryChoices()
    {
        var canceledData = new DomainGroupsData();
        Assert(!canceledData.ApplyDiscoveryChoice("example.com", null), "Cancelled discovery dialog should not be treated as an applied selection.");
        Assert(canceledData.FlattenDomains().Count == 0, "Cancelled discovery dialog should not add the domain automatically.");

        var standaloneData = new DomainGroupsData();
        Assert(standaloneData.ApplyDiscoveryChoice("example.com", new DomainDiscoveryChoice(false, "", [])), "Standalone discovery selection should be applied.");
        Assert(standaloneData.Groups.Count == 0, "Standalone discovery selection should not create a group.");
        Assert(standaloneData.StandaloneDomains.SequenceEqual(["example.com"]), "Standalone discovery selection should add the primary domain.");

        var groupedData = new DomainGroupsData();
        Assert(groupedData.ApplyDiscoveryChoice("shop.example.com", new DomainDiscoveryChoice(true, "Shop", ["cdn.example.net"])), "Grouped discovery selection should be applied.");
        Assert(groupedData.Groups.Count == 1, "Grouped discovery selection should create a group.");
        Assert(groupedData.Groups[0].Name == "Shop", "Discovery group name should come from the dialog.");
        Assert(groupedData.Groups[0].Domains.SequenceEqual(["shop.example.com", "cdn.example.net"]), "Discovery group should include the primary domain and selected related domains.");

        var emptySelectionData = new DomainGroupsData();
        Assert(emptySelectionData.ApplyDiscoveryChoice("media.example.org", new DomainDiscoveryChoice(true, "Media", [])), "Empty grouped discovery selection should still be applied.");
        Assert(emptySelectionData.Groups.Count == 0, "Empty grouped discovery selection should fall back to standalone, matching Flutter.");
        Assert(emptySelectionData.StandaloneDomains.SequenceEqual(["media.example.org"]), "Empty grouped discovery selection should add the primary domain standalone.");

        Assert(DomainDiscoveryDialog.BuildDefaultGroupName("youtube.com") == "Youtube", "Default group names should come from the first label.");
        return Task.CompletedTask;
    }

    private static Task TestServerConfigEmitsDomainExceptions()
    {
        var config = new ServerConfig
        {
            Hostname = "vpn.example.com",
            Address = "203.0.113.10",
            Username = "alice",
            Password = "secret",
            VpnMode = VpnMode.General,
            SplitTunnelDomains = ["*.net", "example.org"],
            SplitTunnelExceptions = [" Example.NET ", "!*.keep.net", "203.0.113.0/24", "example.net"]
        };

        var exclusions = config.BuildTomlExclusions(["198.51.100.0/24"]);

        Assert(exclusions.SequenceEqual(["*.net", "example.org", "198.51.100.0/24", "!example.net", "!*.keep.net"]),
            $"Unexpected exclusions: {string.Join(", ", exclusions)}");

        var toml = config.ToToml();
        AssertContains(toml, "\"!example.net\"");
        AssertContains(toml, "\"!*.keep.net\"");
        Assert(!toml.Contains("!203.0.113.0/24", StringComparison.Ordinal), "IP ranges cannot be exceptions and must not reach the engine.");

        config.ConnectionMode = VpnConnectionMode.SystemProxy;
        Assert(!config.ToToml().Contains("!example.net", StringComparison.Ordinal), "System Proxy mode must not emit routing exceptions.");
        return Task.CompletedTask;
    }

    private static Task TestServerConfigSkipsMalformedRules()
    {
        var config = new ServerConfig
        {
            Hostname = "vpn.example.com",
            Address = "203.0.113.10",
            Username = "alice",
            Password = "secret",
            SplitTunnelDomains = ["good.example.com", "two words.com", "bad\"quote.com", "*.*.net", "*.fine.org"]
        };

        var exclusions = config.BuildTomlExclusions();

        Assert(exclusions.SequenceEqual(["good.example.com", "*.fine.org"]),
            $"Malformed rules must be skipped because the engine splits entries on whitespace: {string.Join(", ", exclusions)}");
        return Task.CompletedTask;
    }

    private static Task TestSplitTunnelEntryValidatesRules()
    {
        var valid = new (string Input, string Expected, SplitTunnelEntryKind Kind)[]
        {
            ("Example.COM", "example.com", SplitTunnelEntryKind.Domain),
            ("https://www.Example.com/watch?v=1", "example.com", SplitTunnelEntryKind.Domain),
            ("www.example.com", "example.com", SplitTunnelEntryKind.Domain),
            ("www.com", "www.com", SplitTunnelEntryKind.Domain),
            ("*.www.example.com", "*.www.example.com", SplitTunnelEntryKind.WildcardDomain),
            ("example.com.", "example.com", SplitTunnelEntryKind.Domain),
            ("*.net", "*.net", SplitTunnelEntryKind.WildcardDomain),
            ("*.Example.com", "*.example.com", SplitTunnelEntryKind.WildcardDomain),
            ("_srv.example.com", "_srv.example.com", SplitTunnelEntryKind.Domain),
            ("203.0.113.7", "203.0.113.7", SplitTunnelEntryKind.IpAddress),
            ("203.0.113.7:443", "203.0.113.7:443", SplitTunnelEntryKind.IpAddress),
            ("2001:db8::1", "2001:db8::1", SplitTunnelEntryKind.IpAddress),
            ("[2001:db8::1]:443", "[2001:db8::1]:443", SplitTunnelEntryKind.IpAddress),
            ("203.0.113.0/24", "203.0.113.0/24", SplitTunnelEntryKind.Cidr),
            ("2001:DB8::/32", "2001:db8::/32", SplitTunnelEntryKind.Cidr),
            ("*:8080", "*:8080", SplitTunnelEntryKind.Port)
        };

        foreach (var (input, expected, kind) in valid)
        {
            Assert(SplitTunnelEntry.TryNormalizeRule(input, out var normalized, out var actualKind), $"'{input}' should be a valid rule.");
            Assert(normalized == expected, $"'{input}' should normalize to '{expected}', got '{normalized}'.");
            Assert(actualKind == kind, $"'{input}' should be {kind}, got {actualKind}.");
        }

        foreach (var input in new[] { "", "   ", "two words.com", "*.*.net", "*.", ".net", "exa$mple.com", "*:0", "*:70000", "203.0.113.0/33", "-bad.example.com", "10", "1.2.3", "300.1.1.1", "010.0.0.1", "0x7f.0.0.1", "*.10" })
        {
            Assert(!SplitTunnelEntry.TryNormalizeRule(input, out _, out _), $"'{input}' should be rejected.");
        }

        Assert(SplitTunnelEntry.ShouldDiscoverRelatedDomains("example.com"), "Plain domains should offer related-domain discovery.");
        Assert(!SplitTunnelEntry.ShouldDiscoverRelatedDomains("*.example.com"), "Wildcards cannot be fetched for related domains.");
        Assert(!SplitTunnelEntry.ShouldDiscoverRelatedDomains("203.0.113.7"), "IP addresses should not trigger related-domain discovery.");
        return Task.CompletedTask;
    }

    private static Task TestSplitTunnelEntryConvertsInternationalDomains()
    {
        Assert(SplitTunnelEntry.TryNormalizeRule("Пример.РФ", out var domain, out var kind) && kind == SplitTunnelEntryKind.Domain,
            "Cyrillic domains should be accepted.");
        Assert(domain == "xn--e1afmkfd.xn--p1ai", $"Cyrillic domains should become punycode, got '{domain}'.");
        Assert(SplitTunnelEntry.TryNormalizeRule("*.рф", out var wildcard, out kind) && kind == SplitTunnelEntryKind.WildcardDomain,
            "Cyrillic wildcards should be accepted.");
        Assert(wildcard == "*.xn--p1ai", $"Cyrillic wildcards should become punycode, got '{wildcard}'.");
        return Task.CompletedTask;
    }

    private static Task TestSplitTunnelEntryValidatesExceptions()
    {
        Assert(SplitTunnelEntry.TryNormalizeException("!Example.net", out var exception) && exception == "example.net",
            "A leading ! should be accepted and stripped.");
        Assert(SplitTunnelEntry.TryNormalizeException("*.cdn.example.net", out var wildcard) && wildcard == "*.cdn.example.net",
            "Wildcard exceptions should be accepted.");
        foreach (var input in new[] { "203.0.113.7", "203.0.113.0/24", "*:443", "!!example.net", "two words" })
        {
            Assert(!SplitTunnelEntry.TryNormalizeException(input, out _), $"'{input}' should not be a valid exception.");
        }

        return Task.CompletedTask;
    }

    private static Task TestSplitTunnelEntryCoverage()
    {
        Assert(SplitTunnelEntry.Covers("*.net", "example.net"), "*.net covers example.net.");
        Assert(SplitTunnelEntry.Covers("*.net", "*.cdn.example.net"), "*.net covers deeper wildcards.");
        Assert(SplitTunnelEntry.Covers("example.net", "example.net"), "A rule covers itself.");
        Assert(!SplitTunnelEntry.Covers("example.net", "sub.example.net"), "A plain domain rule does not cover subdomains.");
        Assert(!SplitTunnelEntry.Covers("*.example.net", "example.net"), "A wildcard does not cover its bare domain, matching the engine.");
        Assert(!SplitTunnelEntry.Covers("*.net", "example.org"), "Unrelated zones are not covered.");
        return Task.CompletedTask;
    }

    private static Task TestDomainGroupsRulesAndExceptionsAreExclusive()
    {
        var data = new DomainGroupsData();
        Assert(data.AddStandaloneDomain("example.net"), "Rule should be added.");
        Assert(data.AddException("Example.NET"), "Adding an exception for the same pattern should succeed.");
        Assert(!data.ContainsDomain("example.net"), "An exception replaces the rule for the same pattern.");
        Assert(data.ExceptionDomains.SequenceEqual(["example.net"]), "The exception should be normalized.");
        Assert(!data.AddException("example.net"), "Duplicate exceptions should be ignored.");

        Assert(data.AddStandaloneDomain("example.net"), "Turning the exception back into a rule should succeed.");
        Assert(!data.ContainsException("example.net"), "A rule replaces the exception for the same pattern.");

        data.ExceptionDomains.Add("203.0.113.0/24");
        data.ExceptionDomains.Add(" *.Keep.net ");
        var normalized = data.NormalizeEntries();
        Assert(normalized.ExceptionDomains.SequenceEqual(["*.keep.net"]), "Normalization keeps only domain exceptions.");
        Assert(normalized.Version == DomainGroupsData.CurrentVersion, "Normalization should stamp the current version.");
        return Task.CompletedTask;
    }

    private static Task TestDomainGroupsReportsOverriddenRules()
    {
        var data = new DomainGroupsData();
        data.AddStandaloneDomain("*.net");
        data.AddGroup("Example", "example.net", ["*.example.net"]);
        data.AddException("cdn.example.net");
        data.AddException("unrelated.org");

        Assert(data.RulesOverriddenBy("cdn.example.net").SequenceEqual(["*.example.net", "*.net"]),
            $"Unexpected overridden rules: {string.Join(", ", data.RulesOverriddenBy("cdn.example.net"))}");
        Assert(data.RulesOverriddenBy("unrelated.org").Count == 0, "An exception without a broader rule overrides nothing.");
        Assert(data.FlattenExceptions().SequenceEqual(["cdn.example.net", "unrelated.org"]), "Exceptions should flatten in insertion order.");
        return Task.CompletedTask;
    }

    private static async Task TestConfigServicePersistsRoutingExceptions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            var rules = new DomainGroupsData();
            rules.AddStandaloneDomain("*.net");
            rules.AddException("example.net");

            var saved = await service.SaveSplitTunnelStateAsync(rules, VpnMode.General, ["game.exe"], ["DE"]);
            Assert(saved.SplitTunnelDomains.SequenceEqual(["*.net"]), "Saved config should contain the rule.");
            Assert(saved.SplitTunnelExceptions.SequenceEqual(["example.net"]), "Saved config should contain the exception.");

            var groupsJson = await File.ReadAllTextAsync(Path.Combine(tempDir, "domain_groups.json"));
            AssertContains(groupsJson, "\"exceptionDomains\"");

            var reloadedConfig = await service.LoadConfigAsync();
            var reloadedRules = await service.LoadDomainGroupsAsync();
            Assert(reloadedConfig.SplitTunnelExceptions.SequenceEqual(["example.net"]), "Exceptions should survive reloading config.json.");
            Assert(reloadedRules.ExceptionDomains.SequenceEqual(["example.net"]), "Exceptions should survive reloading domain_groups.json.");
            AssertContains(reloadedConfig.ToToml(), "\"!example.net\"");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceImportsRoutingExceptions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var importPath = Path.Combine(tempDir, "import.json");
            await File.WriteAllTextAsync(importPath, """
                {
                  "hostname": "vpn.example.com",
                  "address": "203.0.113.10",
                  "username": "alice",
                  "password": "secret",
                  "vpnMode": "general",
                  "splitTunnelDomains": ["*.net"],
                  "splitTunnelExceptions": ["example.net", "203.0.113.0/24"]
                }
                """);

            var service = new ConfigService(Path.Combine(tempDir, "appdata"));
            var imported = await service.ImportConfigAndPersistAsync(importPath);
            var rules = await service.LoadDomainGroupsAsync();

            Assert(imported.SplitTunnelExceptions.SequenceEqual(["example.net"]), "Only domain exceptions should be imported.");
            Assert(rules.ExceptionDomains.SequenceEqual(["example.net"]), "Imported exceptions should appear on the Routing page.");

            var exportPath = Path.Combine(tempDir, "export.json");
            await service.ExportConfigAsync(imported, exportPath);
            AssertContains(await File.ReadAllTextAsync(exportPath), "\"splitTunnelExceptions\"");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceLoadsLegacyTvGatewayConfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "config.json"), """
                {
                  "hostname": "vpn.example.com",
                  "address": "203.0.113.10",
                  "username": "alice",
                  "password": "secret",
                  "connectionMode": "fullTunnel",
                  "tvGatewayEnabled": true,
                  "vpnMode": "general",
                  "splitTunnelDomains": ["example.com"]
                }
                """);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "domain_groups.json"), """
                { "version": 1, "groups": [], "standaloneDomains": ["example.com"] }
                """);

            var service = new ConfigService(tempDir);
            var config = await service.LoadConnectionConfigAsync();
            var rules = await service.LoadDomainGroupsAsync();
            Assert(config.Hostname == "vpn.example.com" && config.SplitTunnelDomains.SequenceEqual(["example.com"]), "Configs written by the TV Gateway build should still load.");
            Assert(rules.StandaloneDomains.SequenceEqual(["example.com"]) && rules.ExceptionDomains.Count == 0, "Version 1 domain groups should load without exceptions.");

            await service.SaveConfigAsync(config);
            var json = await File.ReadAllTextAsync(Path.Combine(tempDir, "config.json"));
            Assert(!json.Contains("tvGateway", StringComparison.OrdinalIgnoreCase), "The removed TV Gateway flag should not be written back.");
            AssertContains(json, "\"splitTunnelExceptions\"");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceRoundTripsPreferences()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            Assert((await service.LoadPreferencesAsync()).CloseAction == CloseAction.Ask, "The close action should default to asking.");

            await service.SavePreferencesAsync(new AppPreferences { CloseAction = CloseAction.MinimizeToTray });
            Assert((await service.LoadPreferencesAsync()).CloseAction == CloseAction.MinimizeToTray, "The close action should round-trip.");
            AssertContains(await File.ReadAllTextAsync(Path.Combine(tempDir, "preferences.json")), "\"minimizeToTray\"");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static Task TestDomainGroupsCanonicalizeWww()
    {
        var data = new DomainGroupsData();
        Assert(data.AddStandaloneDomain("youtube.com"), "Rule should be added.");
        Assert(!data.AddStandaloneDomain("www.youtube.com"), "www.youtube.com is the same engine entry as youtube.com.");
        Assert(data.AddException("www.youtube.com"), "An exception for the www. name should be accepted.");
        Assert(data.ExceptionDomains.SequenceEqual(["youtube.com"]), "The exception should be stored in its canonical form.");
        Assert(!data.ContainsDomain("youtube.com"), "The exception replaces the rule the engine would otherwise override silently.");
        return Task.CompletedTask;
    }

    private static Task TestDomainGroupsNormalizesExceptionPrefixes()
    {
        var data = new DomainGroupsData
        {
            Groups = [new DomainGroup { Id = "g", Name = "Shop", PrimaryDomain = "shop.com", Domains = ["shop.com", "cdn.shop.com"] }],
            StandaloneDomains = ["example.net", "*.net"],
            ExceptionDomains = ["!example.net", "!cdn.shop.com", "!1.2.3.0/24"]
        };

        var normalized = data.NormalizeEntries();

        Assert(normalized.ExceptionDomains.SequenceEqual(["example.net", "cdn.shop.com"]), $"Unexpected exceptions: {string.Join(", ", normalized.ExceptionDomains)}");
        Assert(normalized.StandaloneDomains.SequenceEqual(["*.net"]), "A rule for the same pattern as an exception is dropped.");
        Assert(normalized.Groups.Single().Domains.SequenceEqual(["shop.com"]), "Group domains that are exceptions are dropped from the group.");
        return Task.CompletedTask;
    }

    private static async Task TestConfigServiceImportWritesGroupsBeforeNotifying()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var appData = Path.Combine(tempDir, "appdata");
            var service = new ConfigService(appData);
            await service.SaveDomainGroupsAsync(new DomainGroupsData { StandaloneDomains = ["old.example.com"] });

            var importPath = Path.Combine(tempDir, "import.json");
            await File.WriteAllTextAsync(importPath, """
                { "hostname": "vpn.example.com", "address": "203.0.113.10", "username": "alice", "password": "secret",
                  "splitTunnelDomains": ["new.example.com"] }
                """);

            string? groupsSeenOnChange = null;
            service.ConfigChanged += (_, _) => groupsSeenOnChange = File.ReadAllText(Path.Combine(appData, "domain_groups.json"));
            await service.ImportConfigAndPersistAsync(importPath);

            Assert(groupsSeenOnChange != null, "Importing should raise ConfigChanged.");
            Assert(groupsSeenOnChange!.Contains("new.example.com", StringComparison.Ordinal) &&
                   !groupsSeenOnChange.Contains("old.example.com", StringComparison.Ordinal),
                "Listeners reloading on ConfigChanged must already see the imported domain groups.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceToleratesOverlappingSaves()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            await service.SaveConfigAsync(new ServerConfig { Password = "secret" });

            // Pages save routing on every click and re-read the configuration on every change.
            var work = Enumerable.Range(0, 24).Select(i => Task.Run(async () =>
            {
                if (i % 2 == 0)
                {
                    var rules = new DomainGroupsData { StandaloneDomains = [$"site{i}.example.com"] };
                    await service.SaveSplitTunnelStateAsync(rules, VpnMode.General, [$"app{i}.exe"], ["DE"]);
                }
                else
                {
                    // A read racing a save must see a saved file, never fall back to defaults.
                    var loaded = await service.LoadConfigAsync();
                    Assert(loaded.Password == "secret", "A read during a save returned default settings.");
                    await service.LoadDomainGroupsAsync();
                }
            }));
            await Task.WhenAll(work);

            var final = await service.LoadConfigAsync();
            Assert(final.Password == "secret" && final.SplitTunnelApps.Count == 1, "The configuration should stay intact after overlapping saves.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static Task TestServerSetupParsesEndpointVersion()
    {
        Assert(ServerSetupService.ParseEndpointVersion("1.0.33") == "1.0.33", "A bare version should be accepted.");
        Assert(ServerSetupService.ParseEndpointVersion("trusttunnel_endpoint 1.0.33\n") == "1.0.33", "clap-style version output should be accepted.");
        Assert(ServerSetupService.ParseEndpointVersion("trusttunnel_endpoint 1.0.330") == "1.0.330", "Longer versions must not match a prefix.");
        return Task.CompletedTask;
    }

    private static async Task TestConfigServiceAtomicJsonWrites()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            await service.SaveConfigAsync(new ServerConfig
            {
                Hostname = "first.example.com",
                Address = "198.51.100.1",
                Username = "first",
                Password = "secret"
            });
            await service.SaveConfigAsync(new ServerConfig
            {
                Hostname = "second.example.com",
                Address = "198.51.100.2",
                Username = "second",
                Password = "secret"
            });
            await service.SaveDomainGroupsAsync(new DomainGroupsData
            {
                StandaloneDomains = ["example.com"]
            });
            await service.SaveServerSetupConfigAsync(new ServerSetupConfig
            {
                Host = "203.0.113.10",
                Domain = "vpn.example.com",
                Email = "admin@example.com",
                VpnUsername = "alice"
            });

            var exportPath = Path.Combine(tempDir, "export.json");
            await service.ExportConfigAsync(await service.LoadConfigAsync(), exportPath);

            var loaded = await service.LoadConfigAsync();
            Assert(loaded.Hostname == "second.example.com", "Second config save should replace the first.");
            Assert(File.Exists(Path.Combine(tempDir, "config.json")), "Config file was not written.");
            Assert(File.Exists(Path.Combine(tempDir, "domain_groups.json")), "Domain groups file was not written.");
            Assert(File.Exists(Path.Combine(tempDir, "server_setup_config.json")), "Server setup file was not written.");
            Assert(File.Exists(exportPath), "Export file was not written.");
            Assert(Directory.GetFiles(tempDir, "*.tmp", SearchOption.AllDirectories).Length == 0, "Temporary write files should be cleaned up.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceExportDoesNotCreateBackup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            var exportPath = Path.Combine(tempDir, "veil-export.json");
            await File.WriteAllTextAsync(exportPath, "{\"hostname\":\"old.example.com\"}");
            var config = new ServerConfig
            {
                Hostname = "new.example.com",
                Address = "198.51.100.10",
                Username = "alice",
                Password = "secret",
                SplitTunnelDomains = [" HTTPS://Example.COM/path ", "example.com", ""],
                SplitTunnelApps = [" Slack.exe ", "slack.EXE", "code.exe", ""]
            };
            var originalDomains = config.SplitTunnelDomains.ToList();
            var originalApps = config.SplitTunnelApps.ToList();

            await service.ExportConfigAsync(config, exportPath);

            var exported = await File.ReadAllTextAsync(exportPath);
            AssertContains(exported, "\"hostname\": \"new.example.com\"");
            using var exportedJson = JsonDocument.Parse(exported);
            var exportedRoot = exportedJson.RootElement;
            var exportedDomains = exportedRoot.GetProperty("splitTunnelDomains").EnumerateArray().Select(value => value.GetString()).ToList();
            var exportedApps = exportedRoot.GetProperty("splitTunnelApps").EnumerateArray().Select(value => value.GetString()).ToList();
            Assert(exportedDomains.SequenceEqual(["example.com"]), "Export should canonicalize split tunnel domains.");
            Assert(exportedApps.SequenceEqual(["code.exe", "Slack.exe"]), "Export should canonicalize split tunnel apps.");
            Assert(config.SplitTunnelDomains.SequenceEqual(originalDomains), "Export should not mutate the caller's domain draft.");
            Assert(config.SplitTunnelApps.SequenceEqual(originalApps), "Export should not mutate the caller's app draft.");
            Assert(!File.Exists($"{exportPath}.bak"), "Export should atomically replace the chosen file without creating a recovery backup.");
            Assert(Directory.GetFiles(tempDir, "*.tmp", SearchOption.AllDirectories).Length == 0, "Export temporary files should be cleaned up.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceRestoresFromBackup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);

            await service.SaveConfigAsync(new ServerConfig
            {
                Hostname = "backup.example.com",
                Address = "198.51.100.10",
                Username = "backup-user",
                Password = "backup-secret"
            });
            await service.SaveConfigAsync(new ServerConfig
            {
                Hostname = "primary.example.com",
                Address = "198.51.100.11",
                Username = "primary-user",
                Password = "primary-secret"
            });

            await service.SaveDomainGroupsAsync(new DomainGroupsData
            {
                StandaloneDomains = ["backup.example.com"]
            });
            await service.SaveDomainGroupsAsync(new DomainGroupsData
            {
                StandaloneDomains = ["primary.example.com"]
            });

            await service.SaveServerSetupConfigAsync(new ServerSetupConfig
            {
                Host = "198.51.100.10",
                Domain = "backup.example.com",
                Email = "backup@example.com",
                ListenPort = 8443,
                VpnUsername = "backup-user"
            });
            await service.SaveServerSetupConfigAsync(new ServerSetupConfig
            {
                Host = "198.51.100.11",
                Domain = "primary.example.com",
                Email = "primary@example.com",
                ListenPort = 9443,
                VpnUsername = "primary-user"
            });

            await File.WriteAllTextAsync(Path.Combine(tempDir, "config.json"), "{ broken");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "domain_groups.json"), "{ broken");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "server_setup_config.json"), "{ broken");

            var config = await service.LoadConfigAsync();
            var groups = await service.LoadDomainGroupsAsync();
            var setup = await service.LoadServerSetupConfigAsync();

            Assert(config.Hostname == "backup.example.com", "Config backup was not restored after primary corruption.");
            Assert(groups.StandaloneDomains.SequenceEqual(["backup.example.com"]), "Domain groups backup was not restored after primary corruption.");
            Assert(setup.Domain == "backup.example.com", "Server setup backup was not restored after primary corruption.");
            Assert(setup.ListenPort == 8443, "Server setup backup port mismatch.");
            Assert(File.Exists(Path.Combine(tempDir, "config.json.bak")), "Config backup file should exist.");
            Assert(File.Exists(Path.Combine(tempDir, "domain_groups.json.bak")), "Domain groups backup file should exist.");
            Assert(File.Exists(Path.Combine(tempDir, "server_setup_config.json.bak")), "Server setup backup file should exist.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceRestoresInvalidDomainGroupsFromBackup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            await service.SaveDomainGroupsAsync(new DomainGroupsData
            {
                Groups =
                [
                    new DomainGroup
                    {
                        Id = "backup",
                        Name = "Backup",
                        PrimaryDomain = "backup.example.com",
                        Domains = ["backup.example.com"]
                    }
                ],
                StandaloneDomains = ["standalone.backup.example.com"]
            });
            await service.SaveDomainGroupsAsync(new DomainGroupsData
            {
                StandaloneDomains = ["primary.example.com"]
            });

            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "domain_groups.json"),
                """
                {
                  "groups": [
                    {
                      "id": null,
                      "name": "Broken",
                      "primaryDomain": "broken.example.com",
                      "domains": ["broken.example.com"]
                    }
                  ],
                  "standaloneDomains": ["broken-standalone.example.com"]
                }
                """);

            var groups = await service.LoadDomainGroupsAsync();
            var flattened = groups.FlattenDomains();

            Assert(groups.Groups.Count == 1, "Invalid primary groups file should restore the backup group.");
            Assert(groups.Groups[0].Name == "Backup", "Backup group was not restored after semantic primary corruption.");
            Assert(flattened.Count == 2, "Restored backup domain count mismatch.");
            Assert(flattened.Contains("backup.example.com"), "Restored backup group domain missing.");
            Assert(flattened.Contains("standalone.backup.example.com"), "Restored backup standalone domain missing.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceClientDirectorySelection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var missingPreferred = Path.Combine(tempDir, "veil-csharp", "client");
            var flutterSourceClient = Path.Combine(tempDir, "veil", "client");
            var executableClient = Path.Combine(tempDir, "existing-client");
            Directory.CreateDirectory(flutterSourceClient);
            Directory.CreateDirectory(executableClient);
            await File.WriteAllTextAsync(Path.Combine(flutterSourceClient, "trusttunnel_client.toml.example"), "# example");
            await File.WriteAllTextAsync(Path.Combine(executableClient, "trusttunnel.exe"), "");

            var serviceWithExecutable = new ConfigService(
                tempDir,
                [missingPreferred, flutterSourceClient, executableClient]);

            var selectedExecutableDir = await serviceWithExecutable.GetClientDirectoryAsync();
            var executablePath = await serviceWithExecutable.GetTrustTunnelExecutableAsync();

            Assert(selectedExecutableDir == executableClient, "Client directory with executable should win over source example directory.");
            Assert(executablePath == Path.Combine(executableClient, "trusttunnel.exe"), "Legacy executable fallback path mismatch.");

            var serviceWithoutExecutable = new ConfigService(
                tempDir,
                [missingPreferred, flutterSourceClient]);

            var selectedMissingPreferred = await serviceWithoutExecutable.GetClientDirectoryAsync();
            Assert(selectedMissingPreferred == missingPreferred, "Missing preferred client directory should be created before using source example directory.");
            Assert(Directory.Exists(missingPreferred), "Preferred client directory was not created.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestServerSetupPersistenceNoSecrets()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            var config = new ServerSetupConfig
            {
                Host = "203.0.113.10",
                SshPort = 2222,
                SshUsername = "root",
                SshPassword = "ssh-secret",
                SshKeyPath = @"C:\Users\user\.ssh\id_ed25519",
                UseKeyAuth = true,
                Domain = "vpn.example.com",
                Email = "admin@example.com",
                ListenPort = 8443,
                VpnUsername = "alice",
                VpnPassword = "vpn-secret"
            };

            await service.SaveServerSetupConfigAsync(config);
            var loaded = await service.LoadServerSetupConfigAsync();

            Assert(loaded.Host == config.Host, "Server host did not round-trip.");
            Assert(loaded.SshPort == config.SshPort, "SSH port did not round-trip.");
            Assert(loaded.SshUsername == config.SshUsername, "SSH username did not round-trip.");
            Assert(loaded.SshKeyPath == config.SshKeyPath, "SSH key path did not round-trip.");
            Assert(loaded.UseKeyAuth == config.UseKeyAuth, "Auth mode did not round-trip.");
            Assert(loaded.Domain == config.Domain, "Domain did not round-trip.");
            Assert(loaded.Email == config.Email, "Email did not round-trip.");
            Assert(loaded.ListenPort == config.ListenPort, "Listen port did not round-trip.");
            Assert(loaded.VpnUsername == config.VpnUsername, "VPN username did not round-trip.");
            Assert(loaded.SshPassword == "", "SSH password must not be persisted.");
            Assert(loaded.VpnPassword == "", "VPN password must not be persisted.");

            var files = Directory.GetFiles(tempDir, "*.json");
            var json = await File.ReadAllTextAsync(files.Single(path => Path.GetFileName(path) == "server_setup_config.json"));
            Assert(!json.Contains("ssh-secret", StringComparison.Ordinal), "SSH password leaked to JSON.");
            Assert(!json.Contains("vpn-secret", StringComparison.Ordinal), "VPN password leaked to JSON.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestServerSetupNullFieldDefaults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "server_setup_config.json"),
                """
                {
                  "host": null,
                  "sshPort": null,
                  "sshUsername": null,
                  "sshPassword": "should-not-load",
                  "sshKeyPath": null,
                  "useKeyAuth": null,
                  "domain": null,
                  "email": null,
                  "listenPort": null,
                  "vpnUsername": null,
                  "vpnPassword": "should-not-load"
                }
                """);

            var service = new ConfigService(tempDir);
            var loaded = await service.LoadServerSetupConfigAsync();
            var defaults = ServerSetupConfig.DefaultConfig();

            Assert(loaded.Host == defaults.Host, "Null server setup host should use default.");
            Assert(loaded.SshPort == defaults.SshPort, "Null SSH port should use default.");
            Assert(loaded.SshUsername == defaults.SshUsername, "Null SSH username should use default.");
            Assert(loaded.SshKeyPath == defaults.SshKeyPath, "Null SSH key path should use WPF default.");
            Assert(!loaded.UseKeyAuth, "Null key-auth flag should use default.");
            Assert(loaded.ListenPort == defaults.ListenPort, "Null listen port should use default.");
            Assert(loaded.Domain == defaults.Domain, "Null domain should use default.");
            Assert(loaded.Email == defaults.Email, "Null email should use default.");
            Assert(loaded.VpnUsername == defaults.VpnUsername, "Null VPN username should use default.");
            Assert(loaded.SshPassword == "", "Persisted SSH password should never be loaded.");
            Assert(loaded.VpnPassword == "", "Persisted VPN password should never be loaded.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestServerSetupDraftTextNormalization()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "server_setup_config.json"),
                """
                {
                  "host": "  203.0.113.10  ",
                  "sshPort": 2222,
                  "sshUsername": "   ",
                  "sshPassword": "should-not-load",
                  "sshKeyPath": "   ",
                  "useKeyAuth": true,
                  "domain": "  vpn.example.com  ",
                  "email": "  admin@example.com  ",
                  "listenPort": 8443,
                  "vpnUsername": "  alice  ",
                  "vpnPassword": "should-not-load"
                }
                """);

            var service = new ConfigService(tempDir);
            var loaded = await service.LoadServerSetupConfigAsync();
            var defaults = ServerSetupConfig.DefaultConfig();

            Assert(loaded.Host == "203.0.113.10", "Server setup host should be trimmed on load.");
            Assert(loaded.SshPort == 2222, "Valid SSH port should be preserved.");
            Assert(loaded.SshUsername == defaults.SshUsername, "Blank SSH username should fall back to root.");
            Assert(loaded.SshKeyPath == defaults.SshKeyPath, "Blank SSH key path should fall back to default.");
            Assert(loaded.UseKeyAuth, "Auth mode should be preserved.");
            Assert(loaded.Domain == "vpn.example.com", "Server setup domain should be trimmed on load.");
            Assert(loaded.Email == "admin@example.com", "Server setup email should be trimmed on load.");
            Assert(loaded.ListenPort == 8443, "Valid listen port should be preserved.");
            Assert(loaded.VpnUsername == "alice", "VPN username should be trimmed on load.");
            Assert(loaded.SshPassword == "", "SSH password should not load from persisted draft.");
            Assert(loaded.VpnPassword == "", "VPN password should not load from persisted draft.");

            await service.SaveServerSetupConfigAsync(new ServerSetupConfig
            {
                Host = "  198.51.100.20  ",
                SshPort = 2022,
                SshUsername = "  deploy  ",
                SshPassword = "ssh-secret",
                SshKeyPath = "  C:\\Users\\user\\.ssh\\id_ed25519  ",
                UseKeyAuth = true,
                Domain = "  saved.example.com  ",
                Email = "  saved@example.com  ",
                ListenPort = 9443,
                VpnUsername = "  saved-user  ",
                VpnPassword = "vpn-secret"
            });

            var reloaded = await service.LoadServerSetupConfigAsync();
            var json = await File.ReadAllTextAsync(Path.Combine(tempDir, "server_setup_config.json"));

            Assert(reloaded.Host == "198.51.100.20", "Saved server setup host should be trimmed.");
            Assert(reloaded.SshUsername == "deploy", "Saved SSH username should be trimmed.");
            Assert(reloaded.SshKeyPath == "C:\\Users\\user\\.ssh\\id_ed25519", "Saved SSH key path should be trimmed.");
            Assert(reloaded.Domain == "saved.example.com", "Saved domain should be trimmed.");
            Assert(reloaded.Email == "saved@example.com", "Saved email should be trimmed.");
            Assert(reloaded.VpnUsername == "saved-user", "Saved VPN username should be trimmed.");
            Assert(!json.Contains("ssh-secret", StringComparison.Ordinal), "SSH password should not be persisted after normalization.");
            Assert(!json.Contains("vpn-secret", StringComparison.Ordinal), "VPN password should not be persisted after normalization.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestServerSetupInvalidDraftPortsUseDefaults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var defaults = ServerSetupConfig.DefaultConfig();
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "server_setup_config.json"),
                """
                {
                  "host": "198.51.100.10",
                  "sshPort": 0,
                  "sshUsername": "root",
                  "domain": "vpn.example.com",
                  "email": "admin@example.com",
                  "listenPort": 70000,
                  "vpnUsername": "alice"
                }
                """);

            var service = new ConfigService(tempDir);
            var loaded = await service.LoadServerSetupConfigAsync();

            Assert(loaded.Host == "198.51.100.10", "Invalid numeric ports should not discard the rest of the server setup draft.");
            Assert(loaded.SshPort == defaults.SshPort, "Invalid saved SSH port should use the default draft port.");
            Assert(loaded.ListenPort == defaults.ListenPort, "Invalid saved listen port should use the default draft port.");

            await service.SaveServerSetupConfigAsync(new ServerSetupConfig
            {
                Host = "203.0.113.10",
                SshPort = -1,
                SshUsername = "root",
                Domain = "saved.example.com",
                Email = "admin@example.com",
                ListenPort = 65536,
                VpnUsername = "alice"
            });

            loaded = await service.LoadServerSetupConfigAsync();
            var json = await File.ReadAllTextAsync(Path.Combine(tempDir, "server_setup_config.json"));

            Assert(loaded.Host == "203.0.113.10", "Sanitized server setup draft should preserve non-port fields.");
            Assert(loaded.SshPort == defaults.SshPort, "Invalid SSH port should be sanitized before persistence.");
            Assert(loaded.ListenPort == defaults.ListenPort, "Invalid listen port should be sanitized before persistence.");
            Assert(!json.Contains("\"sshPort\": -1", StringComparison.Ordinal), "Invalid SSH port should not be written to the draft file.");
            Assert(!json.Contains("\"listenPort\": 65536", StringComparison.Ordinal), "Invalid listen port should not be written to the draft file.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestConfigServiceRestoresInvalidServerSetupFromBackup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var service = new ConfigService(tempDir);
            await service.SaveServerSetupConfigAsync(new ServerSetupConfig
            {
                Host = "198.51.100.20",
                SshPort = 2222,
                SshUsername = "backup-root",
                Domain = "backup.example.com",
                Email = "backup@example.com",
                ListenPort = 8443,
                VpnUsername = "backup-user"
            });
            await service.SaveServerSetupConfigAsync(new ServerSetupConfig
            {
                Host = "198.51.100.21",
                SshPort = 22,
                SshUsername = "primary-root",
                Domain = "primary.example.com",
                Email = "primary@example.com",
                ListenPort = 9443,
                VpnUsername = "primary-user"
            });

            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "server_setup_config.json"),
                """
                {
                  "host": "broken.example.com",
                  "sshPort": "not-a-number",
                  "sshUsername": "broken-root",
                  "domain": "broken.example.com",
                  "email": "broken@example.com",
                  "listenPort": 443,
                  "vpnUsername": "broken-user"
                }
                """);

            var loaded = await service.LoadServerSetupConfigAsync();

            Assert(loaded.Host == "198.51.100.20", "Invalid server setup primary should restore backup host.");
            Assert(loaded.SshPort == 2222, "Invalid server setup primary should restore backup SSH port.");
            Assert(loaded.SshUsername == "backup-root", "Invalid server setup primary should restore backup SSH username.");
            Assert(loaded.Domain == "backup.example.com", "Invalid server setup primary should restore backup domain.");
            Assert(loaded.ListenPort == 8443, "Invalid server setup primary should restore backup listen port.");
            Assert(loaded.VpnUsername == "backup-user", "Invalid server setup primary should restore backup VPN username.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestServerSetupApplyRequiresCompletedSetup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configService = new ConfigService(tempDir);
            await configService.SaveConfigAsync(new ServerConfig
            {
                Hostname = "old.example.com",
                Address = "198.51.100.5",
                Port = 443,
                Username = "old-user",
                Password = "old-password"
            });

            var setupService = new ServerSetupService();
            var lastConfigField = typeof(ServerSetupService).GetField("_lastConfig", BindingFlags.Instance | BindingFlags.NonPublic)
                                  ?? throw new InvalidOperationException("_lastConfig field not found.");
            lastConfigField.SetValue(setupService, new ServerSetupConfig
            {
                Domain = "vpn.example.com",
                Host = "203.0.113.10",
                ListenPort = 8443,
                VpnUsername = "alice",
                VpnPassword = "secret"
            });

            await AssertThrowsAsync<InvalidOperationException>(() => setupService.ApplyToClientConfigAsync(configService));

            var loaded = await configService.LoadConfigAsync();
            Assert(loaded.Hostname == "old.example.com", "Client hostname should not change before setup is completed.");
            Assert(loaded.Address == "198.51.100.5", "Client address should not change before setup is completed.");
            Assert(loaded.Username == "old-user", "Client username should not change before setup is completed.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestServerSetupApplyPreservesSplitTunnelState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configService = new ConfigService(tempDir);
            await configService.SaveConfigAsync(new ServerConfig
            {
                Hostname = "old.example.com",
                Address = "198.51.100.5",
                Port = 443,
                Username = "old-user",
                Password = "old-password",
                Dns = "tls://1.1.1.1",
                LogLevel = "debug",
                CustomSni = "front.example.com",
                VpnMode = VpnMode.Selective,
                SplitTunnelDomains = ["example.com", "api.example.com"],
                SplitTunnelApps = ["chrome.exe", "slack.exe"],
                SplitTunnelCountries = ["US", "DE"]
            });

            var setupService = new ServerSetupService();
            var currentStepProperty = typeof(ServerSetupService).GetProperty(nameof(ServerSetupService.CurrentStep))
                                      ?? throw new InvalidOperationException("CurrentStep property not found.");
            var lastConfigField = typeof(ServerSetupService).GetField("_lastConfig", BindingFlags.Instance | BindingFlags.NonPublic)
                                  ?? throw new InvalidOperationException("_lastConfig field not found.");

            currentStepProperty.SetValue(setupService, SetupStep.Completed);
            lastConfigField.SetValue(setupService, new ServerSetupConfig
            {
                Domain = "vpn.example.com",
                Host = "203.0.113.10",
                ListenPort = 8443,
                VpnUsername = "alice",
                VpnPassword = "secret"
            });

            await setupService.ApplyToClientConfigAsync(configService);

            var loaded = await configService.LoadConfigAsync();
            Assert(loaded.Hostname == "vpn.example.com", "Completed setup should update the client hostname.");
            Assert(loaded.Address == "203.0.113.10", "Completed setup should update the client address.");
            Assert(loaded.Port == 8443, "Completed setup should update the client port.");
            Assert(loaded.Username == "alice", "Completed setup should update the client username.");
            Assert(loaded.Password == "secret", "Completed setup should update the client password.");
            Assert(loaded.Dns == "tls://1.1.1.1", "Applying server setup should preserve DNS settings.");
            Assert(loaded.LogLevel == "debug", "Applying server setup should preserve log level.");
            Assert(loaded.CustomSni == "front.example.com", "Applying server setup should preserve custom SNI.");
            Assert(loaded.VpnMode == VpnMode.Selective, "Applying server setup should preserve VPN mode.");
            Assert(loaded.SplitTunnelDomains.SequenceEqual(["example.com", "api.example.com"]), "Applying server setup should preserve split-tunnel domains.");
            Assert(loaded.SplitTunnelApps.SequenceEqual(["chrome.exe", "slack.exe"]), "Applying server setup should preserve split-tunnel apps.");
            Assert(loaded.SplitTunnelCountries.SequenceEqual(["DE", "US"]), "Applying server setup should preserve split-tunnel GeoIP countries.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestServerSetupIgnoresDuplicateInstallsWhileRunning()
    {
        var setupService = new ServerSetupService();
        var currentStepProperty = typeof(ServerSetupService).GetProperty(nameof(ServerSetupService.CurrentStep))
                                  ?? throw new InvalidOperationException("CurrentStep property not found.");
        var lastConfigField = typeof(ServerSetupService).GetField("_lastConfig", BindingFlags.Instance | BindingFlags.NonPublic)
                              ?? throw new InvalidOperationException("_lastConfig field not found.");
        var rememberedConfig = new ServerSetupConfig
        {
            Domain = "running.example.com",
            Host = "203.0.113.10"
        };

        currentStepProperty.SetValue(setupService, SetupStep.Connecting);
        lastConfigField.SetValue(setupService, rememberedConfig);

        await setupService.InstallAndRememberAsync(new ServerSetupConfig
        {
            Domain = "duplicate.example.com",
            Host = "203.0.113.11"
        });

        Assert(setupService.CurrentStep == SetupStep.Connecting, "Duplicate install should not reset the running setup step.");
        Assert(ReferenceEquals(lastConfigField.GetValue(setupService), rememberedConfig), "Duplicate install should not clear the remembered running config.");
        Assert(setupService.Logs.Any(line => line.Contains("already running", StringComparison.OrdinalIgnoreCase)), "Duplicate install should be logged.");
    }

    private static Task TestServerSetupClearLogsKeepsRunningStep()
    {
        var setupService = new ServerSetupService();
        var currentStepProperty = typeof(ServerSetupService).GetProperty(nameof(ServerSetupService.CurrentStep))
                                  ?? throw new InvalidOperationException("CurrentStep property not found.");

        currentStepProperty.SetValue(setupService, SetupStep.ConfiguringServer);

        setupService.ClearLogs();

        Assert(setupService.CurrentStep == SetupStep.ConfiguringServer, "ClearLogs should keep the running setup step.");
        Assert(setupService.IsRunning, "ClearLogs should not unlock server setup while installation is running.");

        currentStepProperty.SetValue(setupService, SetupStep.Failed);

        setupService.ClearLogs();

        Assert(setupService.CurrentStep == SetupStep.Idle, "ClearLogs should still reset a non-running setup state.");
        Assert(!setupService.IsRunning, "Failed/cleared setup should not be running.");

        return Task.CompletedTask;
    }

    private static async Task TestServerSetupDisconnectsSshSessionAfterCompletedInstall()
    {
        var fakeFactory = new FakeServerSetupSshSessionFactory();
        var setupService = new ServerSetupService(fakeFactory);

        await setupService.InstallServerAsync(new ServerSetupConfig
        {
            Host = "203.0.113.10",
            SshPort = 22,
            SshUsername = "root",
            SshPassword = "ssh-secret",
            Domain = "vpn.example.com",
            Email = "admin@example.com",
            ListenPort = 443,
            VpnUsername = "alice",
            VpnPassword = "vpn-secret"
        });

        Assert(setupService.CurrentStep == SetupStep.Completed, "Successful fake server setup should complete.");
        Assert(fakeFactory.ConnectCalls == 1, "Server setup should open one SSH session.");
        Assert(fakeFactory.Session.DisconnectCalled, "Completed server setup should disconnect the SSH session.");
        Assert(fakeFactory.Session.Disposed, "Completed server setup should dispose the SSH session.");
        Assert(!fakeFactory.Session.IsConnected, "Completed server setup should leave the fake SSH session disconnected.");
    }

    private static async Task TestServerSetupDisconnectsSshSessionAfterFailedInstall()
    {
        var fakeFactory = new FakeServerSetupSshSessionFactory
        {
            Session =
            {
                FailInstallDownload = true
            }
        };
        var setupService = new ServerSetupService(fakeFactory);

        await setupService.InstallServerAsync(new ServerSetupConfig
        {
            Host = "203.0.113.10",
            SshPort = 22,
            SshUsername = "root",
            SshPassword = "ssh-secret",
            Domain = "vpn.example.com",
            Email = "admin@example.com",
            ListenPort = 443,
            VpnUsername = "alice",
            VpnPassword = "vpn-secret"
        });

        Assert(setupService.CurrentStep == SetupStep.Failed, "Failed fake server setup should enter failed state.");
        Assert(setupService.ErrorMessage == "download failed", "Server setup failure should preserve the original install error.");
        Assert(fakeFactory.ConnectCalls == 1, "Server setup should open one SSH session before failing.");
        Assert(fakeFactory.Session.DisconnectCalled, "Failed server setup should disconnect the SSH session.");
        Assert(fakeFactory.Session.Disposed, "Failed server setup should dispose the SSH session.");
        Assert(!fakeFactory.Session.IsConnected, "Failed server setup should leave the fake SSH session disconnected.");
    }

    private static async Task TestServerSetupStopsInstalledServiceBeforeReconfiguring()
    {
        var fakeFactory = new FakeServerSetupSshSessionFactory
        {
            Session =
            {
                EndpointAlreadyInstalled = true
            }
        };
        var setupService = new ServerSetupService(fakeFactory);

        await setupService.InstallServerAsync(new ServerSetupConfig
        {
            Host = "203.0.113.10",
            SshPort = 22,
            SshUsername = "root",
            SshPassword = "ssh-secret",
            Domain = "vpn.example.com",
            Email = "admin@example.com",
            ListenPort = 443,
            VpnUsername = "alice",
            VpnPassword = "vpn-secret"
        });

        Assert(setupService.CurrentStep == SetupStep.Completed, "Already-installed fake server setup should still complete.");
        Assert(setupService.AlreadyInstalled, "Server setup should remember the endpoint was already installed.");

        var operations = fakeFactory.Session.Operations;
        var firstStopIndex = operations.FindIndex(operation => operation == "command:systemctl stop trusttunnel || true");
        var portProbeIndex = operations.FindIndex(operation => operation.StartsWith("command:ss -tuln", StringComparison.Ordinal));
        var secondStopIndex = operations.FindIndex(operation => operation == "command:systemctl stop trusttunnel 2>/dev/null || true");
        var vpnUploadIndex = operations.FindIndex(operation => operation == "upload:/opt/trusttunnel/vpn.toml");

        Assert(firstStopIndex >= 0, "Already-installed setup should stop the service before checking the listen port.");
        Assert(portProbeIndex >= 0, "Already-installed setup should still check listen port availability.");
        Assert(firstStopIndex < portProbeIndex, "Existing service should be stopped before the listen-port probe.");
        Assert(secondStopIndex >= 0, "Already-installed setup should stop the service again before uploading configuration.");
        Assert(vpnUploadIndex >= 0, "Already-installed setup should upload vpn.toml.");
        Assert(secondStopIndex < vpnUploadIndex, "Existing service should be stopped before re-uploading vpn.toml.");
    }

    private static async Task TestServerSetupToleratesPortProbeFailures()
    {
        var fakeFactory = new FakeServerSetupSshSessionFactory
        {
            Session =
            {
                FailPortProbe = true
            }
        };
        var setupService = new ServerSetupService(fakeFactory);
        var config = new ServerSetupConfig
        {
            Host = "203.0.113.10",
            SshPort = 22,
            SshUsername = "root",
            SshPassword = "ssh-secret",
            Domain = "vpn.example.com",
            Email = "admin@example.com",
            ListenPort = 443,
            VpnUsername = "alice",
            VpnPassword = "vpn-secret"
        };

        await setupService.InstallServerAsync(config);

        Assert(setupService.CurrentStep == SetupStep.Completed, "Server setup should continue when the port availability probe fails.");
        Assert(config.ListenPort == 443, "Failed port probe should keep the requested listen port.");
        Assert(fakeFactory.Session.Commands.Any(command => command.StartsWith("ss -tuln", StringComparison.Ordinal)), "Fake setup should attempt the port availability probe.");
        Assert(fakeFactory.Session.UploadedPaths.Contains("/opt/trusttunnel/vpn.toml"), "Server setup should still upload vpn.toml after a failed port probe.");
    }

    private static async Task TestServerSetupAutoSelectsAvailableListenPort()
    {
        var fakeFactory = new FakeServerSetupSshSessionFactory
        {
            Session =
            {
                BusyListenPorts = { 443 }
            }
        };
        var setupService = new ServerSetupService(fakeFactory);
        var config = new ServerSetupConfig
        {
            Host = "203.0.113.10",
            SshPort = 22,
            SshUsername = "root",
            SshPassword = "ssh-secret",
            Domain = "vpn.example.com",
            Email = "admin@example.com",
            ListenPort = 443,
            VpnUsername = "alice",
            VpnPassword = "vpn-secret"
        };

        await setupService.InstallServerAsync(config);

        Assert(setupService.CurrentStep == SetupStep.Completed, "Server setup should complete after auto-selecting a free listen port.");
        Assert(config.ListenPort == 8443, "Busy 443 should auto-select 8443 like Flutter.");
        Assert(fakeFactory.Session.Commands.Any(command => command.Contains("\":443 \"", StringComparison.Ordinal)), "Server setup should probe the requested port first.");
        Assert(fakeFactory.Session.Commands.Any(command => command.Contains("\":8443 \"", StringComparison.Ordinal)), "Server setup should probe the auto-selected fallback port.");
        Assert(fakeFactory.Session.UploadedFiles.TryGetValue("/opt/trusttunnel/vpn.toml", out var vpnToml), "Server setup should upload vpn.toml.");
        Assert(vpnToml is not null && vpnToml.Contains("listen_address = \"0.0.0.0:8443\"", StringComparison.Ordinal), "Uploaded vpn.toml should use the auto-selected listen port.");
    }

    private static async Task TestConfigServicePersistsAutoSelectedServerSetupListenPort()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var fakeFactory = new FakeServerSetupSshSessionFactory
            {
                Session =
                {
                    BusyListenPorts = { 443 }
                }
            };
            var setupService = new ServerSetupService(fakeFactory);
            var configService = new ConfigService(tempDir);
            var config = new ServerSetupConfig
            {
                Host = "203.0.113.10",
                SshPort = 22,
                SshUsername = "root",
                SshPassword = "ssh-secret",
                Domain = "vpn.example.com",
                Email = "admin@example.com",
                ListenPort = 443,
                VpnUsername = "alice",
                VpnPassword = "vpn-secret"
            };

            await configService.SaveServerSetupConfigAsync(config);
            await setupService.InstallAndRememberAsync(config);
            await configService.SaveServerSetupConfigAsync(config);

            var loaded = await configService.LoadServerSetupConfigAsync();

            Assert(setupService.CurrentStep == SetupStep.Completed, "Server setup should complete before persisting the adjusted draft.");
            Assert(config.ListenPort == 8443, "Server setup should update the in-memory config to the selected free port.");
            Assert(loaded.ListenPort == 8443, "Auto-selected listen port should be persisted for the next server setup session.");
            Assert(loaded.SshPassword == "", "Persisted adjusted server setup draft should still omit SSH secrets.");
            Assert(loaded.VpnPassword == "", "Persisted adjusted server setup draft should still omit VPN secrets.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestServerSetupShellQuotesCertificateArguments()
    {
        var fakeFactory = new FakeServerSetupSshSessionFactory
        {
            Session =
            {
                CertificateMissingInitially = true
            }
        };
        var setupService = new ServerSetupService(fakeFactory);
        var config = new ServerSetupConfig
        {
            Host = "203.0.113.10",
            SshPort = 22,
            SshUsername = "root",
            SshPassword = "ssh-secret",
            Domain = "vpn'edge.example.com",
            Email = "admin'ops@example.com",
            ListenPort = 443,
            VpnUsername = "alice",
            VpnPassword = "vpn-secret"
        };

        await setupService.InstallServerAsync(config);

        var certbotCommand = fakeFactory.Session.Commands.FirstOrDefault(command =>
            command.StartsWith("certbot certonly --non-interactive --standalone", StringComparison.Ordinal));

        Assert(setupService.CurrentStep == SetupStep.Completed, "Fake server setup should complete after certbot runs.");
        Assert(certbotCommand != null, "Missing certificate should trigger a certbot standalone command.");
        var certbot = certbotCommand ?? throw new InvalidOperationException("Missing certbot command.");
        Assert(certbot.Contains("-m 'admin'\"'\"'ops@example.com'", StringComparison.Ordinal), "Certbot email argument should be shell-quoted.");
        Assert(certbot.Contains("-d 'vpn'\"'\"'edge.example.com'", StringComparison.Ordinal), "Certbot domain argument should be shell-quoted.");
        Assert(fakeFactory.Session.Commands.Any(command =>
            command.Contains("/etc/letsencrypt/live/'vpn'\"'\"'edge.example.com'/fullchain.pem", StringComparison.Ordinal)), "Certificate path probe should shell-quote the domain path segment.");
    }

    private static async Task TestServerSetupCertbotPortBusyFallback()
    {
        var fakeFactory = new FakeServerSetupSshSessionFactory
        {
            Session =
            {
                CertificateMissingInitially = true,
                FailStandaloneCertbotPortBusy = true
            }
        };
        var setupService = new ServerSetupService(fakeFactory);
        var config = new ServerSetupConfig
        {
            Host = "203.0.113.10",
            SshPort = 22,
            SshUsername = "root",
            SshPassword = "ssh-secret",
            Domain = "vpn'edge.example.com",
            Email = "admin'ops@example.com",
            ListenPort = 443,
            VpnUsername = "alice",
            VpnPassword = "vpn-secret"
        };

        await setupService.InstallServerAsync(config);

        var fallbackCommand = fakeFactory.Session.Commands.FirstOrDefault(command =>
            command.StartsWith("certbot certonly --non-interactive --nginx", StringComparison.Ordinal));

        Assert(setupService.CurrentStep == SetupStep.Completed, "Server setup should complete through the Nginx/Apache certbot fallback.");
        Assert(fakeFactory.Session.Commands.Contains("apt-get install -y -qq python3-certbot-nginx python3-certbot-apache || true"), "Port-busy fallback should install Nginx/Apache certbot plugins.");
        Assert(fallbackCommand != null, "Port-busy standalone certbot failure should trigger the Nginx/Apache fallback command.");
        var fallback = fallbackCommand ?? throw new InvalidOperationException("Missing certbot fallback command.");
        Assert(fallback.Contains("-m 'admin'\"'\"'ops@example.com'", StringComparison.Ordinal), "Fallback certbot email argument should be shell-quoted.");
        Assert(fallback.Contains("-d 'vpn'\"'\"'edge.example.com'", StringComparison.Ordinal), "Fallback certbot domain argument should be shell-quoted.");
        Assert(fallback.Contains("certbot certonly --non-interactive --apache", StringComparison.Ordinal), "Fallback command should include the Apache fallback after Nginx.");
    }

    private static async Task TestServerSetupPinsEndpointVersion()
    {
        var fakeFactory = new FakeServerSetupSshSessionFactory();
        var setupService = new ServerSetupService(fakeFactory);

        await setupService.InstallServerAsync(CreateServerSetupConfig());

        const string expectedVersion = "1.0.33";
        const string expectedScriptUrl =
            "https://raw.githubusercontent.com/TrustTunnel/TrustTunnel/refs/tags/v1.0.33/scripts/install.sh";
        var expectedInstallCommand =
            $"curl -fsSL {expectedScriptUrl} " +
            $"| sh -s -- -a y -V {expectedVersion}";

        Assert(ServerSetupService.EndpointVersion == expectedVersion, "The endpoint release constant should pin the approved server version.");
        Assert(ServerSetupService.InstallScriptUrl == expectedScriptUrl, "The installer script URL should be pinned to the same immutable release tag.");
        Assert(setupService.CurrentStep == SetupStep.Completed, "Pinned endpoint installation should complete with the fake SSH server.");
        Assert(fakeFactory.Session.Commands.Contains(expectedInstallCommand), "Automatic installation should pass the pinned version to install.sh.");
        Assert(fakeFactory.Session.Commands.Contains("/opt/trusttunnel/trusttunnel_endpoint --version"), "Automatic installation should verify the installed binary version.");

        var installIndex = fakeFactory.Session.Commands.IndexOf(expectedInstallCommand);
        var versionIndex = fakeFactory.Session.Commands.IndexOf("/opt/trusttunnel/trusttunnel_endpoint --version");
        Assert(versionIndex > installIndex, "Binary version validation should run after the pinned installer.");
    }

    private static async Task TestServerSetupRejectsUnexpectedEndpointVersion()
    {
        var fakeFactory = new FakeServerSetupSshSessionFactory
        {
            Session =
            {
                EndpointAlreadyInstalled = true,
                InstalledEndpointVersion = "1.0.32"
            }
        };
        var setupService = new ServerSetupService(fakeFactory);

        await setupService.InstallServerAsync(CreateServerSetupConfig());

        Assert(setupService.CurrentStep == SetupStep.Failed, "An unexpected installed endpoint version must fail server setup.");
        Assert(
            setupService.ErrorMessage?.Contains(
                "Installed TrustTunnel endpoint version mismatch: expected 1.0.33, got '1.0.32'.",
                StringComparison.Ordinal) == true,
            "Version mismatch failure should include both expected and installed versions.");
        Assert(
            !fakeFactory.Session.UploadedPaths.Any(),
            "Server configuration should not be uploaded after endpoint version validation fails.");
        Assert(
            fakeFactory.Session.Commands.Contains("systemctl start trusttunnel"),
            "A failed update should restart the previously installed service.");
        Assert(
            fakeFactory.Session.Commands.Contains("systemctl is-active trusttunnel"),
            "Existing-service recovery should confirm that the restarted service is active.");
        Assert(
            setupService.Logs.Any(line => line.Contains("restarted successfully", StringComparison.Ordinal)),
            "Successful existing-service recovery should be logged.");
        Assert(
            fakeFactory.Session.Commands.Contains("systemctl show trusttunnel --property=ActiveState --value 2>/dev/null || true"),
            "Existing-service recovery should be based on the pre-update ActiveState.");

        var stopIndex = fakeFactory.Session.Commands.IndexOf("systemctl stop trusttunnel || true");
        var versionIndex = fakeFactory.Session.Commands.IndexOf("/opt/trusttunnel/trusttunnel_endpoint --version");
        var restartIndex = fakeFactory.Session.Commands.IndexOf("systemctl start trusttunnel");
        Assert(stopIndex >= 0 && versionIndex > stopIndex, "The existing service should be stopped before the update is validated.");
        Assert(restartIndex > versionIndex, "Recovery should restart the existing service only after version validation fails.");
    }

    private static async Task TestServerSetupPreservesInactiveServiceAfterUpdateFailure()
    {
        var fakeFactory = new FakeServerSetupSshSessionFactory
        {
            Session =
            {
                EndpointAlreadyInstalled = true,
                ExistingServiceActive = false,
                InstalledEndpointVersion = "1.0.32"
            }
        };
        var setupService = new ServerSetupService(fakeFactory);

        await setupService.InstallServerAsync(CreateServerSetupConfig());

        Assert(setupService.CurrentStep == SetupStep.Failed, "The endpoint version mismatch should still fail setup.");
        Assert(
            setupService.ErrorMessage?.Contains(
                "Installed TrustTunnel endpoint version mismatch: expected 1.0.33, got '1.0.32'.",
                StringComparison.Ordinal) == true,
            "Inactive-service handling must preserve the original version mismatch.");
        Assert(
            fakeFactory.Session.Commands.Contains("systemctl show trusttunnel --property=ActiveState --value 2>/dev/null || true"),
            "Setup should inspect the existing service state before stopping it.");
        Assert(
            !fakeFactory.Session.Commands.Contains("systemctl start trusttunnel"),
            "An existing service that was inactive before the update must not be started during failure recovery.");
        Assert(
            !setupService.Logs.Any(line => line.Contains("attempting to restart", StringComparison.Ordinal)),
            "Inactive existing services should not enter the restart recovery path.");
    }

    private static async Task TestServerSetupToleratesMissingUnitForInstalledBinary()
    {
        var fakeFactory = new FakeServerSetupSshSessionFactory
        {
            Session =
            {
                EndpointAlreadyInstalled = true,
                ExistingServiceUnitExists = false,
                InstalledEndpointVersion = "1.0.32"
            }
        };
        var setupService = new ServerSetupService(fakeFactory);

        await setupService.InstallServerAsync(CreateServerSetupConfig());

        Assert(setupService.CurrentStep == SetupStep.Failed, "The later endpoint version mismatch should fail setup.");
        Assert(
            fakeFactory.Session.Commands.Contains("/opt/trusttunnel/trusttunnel_endpoint --version"),
            "A missing service unit should not abort setup before the endpoint update and version validation.");
        Assert(
            !fakeFactory.Session.Commands.Contains("systemctl start trusttunnel"),
            "A service unit that did not exist before the update must not enter restart recovery.");
        Assert(
            setupService.ErrorMessage?.Contains("version mismatch", StringComparison.Ordinal) == true,
            "A non-fatal ActiveState probe must preserve the later installation error.");
    }

    private static ServerSetupConfig CreateServerSetupConfig() =>
        new()
        {
            Host = "203.0.113.10",
            SshPort = 22,
            SshUsername = "root",
            SshPassword = "ssh-secret",
            Domain = "vpn.example.com",
            Email = "admin@example.com",
            ListenPort = 443,
            VpnUsername = "alice",
            VpnPassword = "vpn-secret"
        };

    private static Task TestSplitTunnelSuggestionFilters()
    {
        var service = new SplitTunnelSuggestionService();
        var line = string.Join(' ', new[]
        {
            "api.example.com",
            "cdn.example.com",
            "existing.example.com",
            "hidden.example.com",
            "vpn.example.com",
            "printer.local",
            "service.internal",
            "trusttunnel.com",
            "api.example.com"
        });

        var suggestions = service.ExtractSuggestions(
            line,
            existingDomains: ["existing.example.com"],
            currentSuggestions: [],
            hiddenSuggestions: ["hidden.example.com"],
            endpointHostname: "vpn.example.com");

        Assert(suggestions.SequenceEqual(["api.example.com", "cdn.example.com", "vpn.example.com"]), "Suggestion filters did not match Flutter behavior.");

        var limitedLine = string.Join(' ', Enumerable.Range(0, 25).Select(i => $"host{i}.example.com"));
        var limited = service.ExtractSuggestions(
            limitedLine,
            existingDomains: [],
            currentSuggestions: Enumerable.Range(0, 18).Select(i => $"current{i}.example.com"),
            hiddenSuggestions: [],
            endpointHostname: "vpn.example.com");

        Assert(limited.Count == 2, $"Expected suggestion cap to leave 2 slots, got {limited.Count}.");

        var hiddenCurrent = Enumerable.Range(0, 20).Select(i => $"hidden{i}.example.com").ToList();
        var afterHidden = service.ExtractSuggestions(
            "fresh1.example.com fresh2.example.com",
            existingDomains: [],
            currentSuggestions: hiddenCurrent,
            hiddenSuggestions: hiddenCurrent,
            endpointHostname: "vpn.example.com");

        Assert(afterHidden.SequenceEqual(["fresh1.example.com", "fresh2.example.com"]), "Hidden suggestions should not consume visible suggestion capacity.");

        var afterDismissedBanner = service.ExtractSuggestions(
            "dismissed.example.com",
            existingDomains: [],
            currentSuggestions: [],
            hiddenSuggestions: [],
            endpointHostname: "vpn.example.com");

        Assert(afterDismissedBanner.SequenceEqual(["dismissed.example.com"]), "Dismissed suggestions should be able to reappear from later logs, matching Flutter.");
        return Task.CompletedTask;
    }

    private static Task TestSplitTunnelEntryNormalization()
    {
        Assert(SplitTunnelEntry.Normalize(" HTTPS://Example.COM/path?q=1 ") == "example.com", "URL entries should normalize to host.");
        Assert(SplitTunnelEntry.Normalize("10.0.0.0/8") == "10.0.0.0/8", "IPv4 CIDR should keep its prefix.");
        Assert(SplitTunnelEntry.Normalize("2001:db8::/32") == "2001:db8::/32", "IPv6 CIDR should keep its prefix.");
        Assert(SplitTunnelEntry.Normalize("203.0.113.10") == "203.0.113.10", "IPv4 address should be preserved.");

        Assert(SplitTunnelEntry.ShouldDiscoverRelatedDomains("example.com"), "Domains should use discovery.");
        Assert(!SplitTunnelEntry.ShouldDiscoverRelatedDomains("10.0.0.0/8"), "IPv4 CIDR should skip discovery.");
        Assert(!SplitTunnelEntry.ShouldDiscoverRelatedDomains("2001:db8::/32"), "IPv6 CIDR should skip discovery.");
        Assert(!SplitTunnelEntry.ShouldDiscoverRelatedDomains("203.0.113.10"), "IP address should skip discovery.");
        return Task.CompletedTask;
    }

    private static Task TestDomainDiscoveryHtmlExtraction()
    {
        var html = """
            <html>
              <a href="https://www.example.com/page">same root</a>
              <link href="//fonts.googleapis.com/css?family=Inter">
              <script src="https://www.google-analytics.com/analytics.js"></script>
              <script>
                const escaped = "https:\/\/api.partner-cdn.com\/v1";
                fetch("//assets.example.net/app.js");
                axios.post("https://auth.example.co.uk/login");
                const bare = "cdn.partner.com.au";
              </script>
              <img srcset="https://img.mediahub.net/thumb.png 1x, https://retina.mediahub.net/thumb.png 2x">
            </html>
            """;

        var domains = DomainDiscoveryService.ExtractRelatedDomainsFromHtml(html, "shop.example.com");

        AssertContainsDomain(domains, "partner-cdn.com");
        AssertContainsDomain(domains, "api.partner-cdn.com");
        AssertContainsDomain(domains, "example.net");
        AssertContainsDomain(domains, "assets.example.net");
        AssertContainsDomain(domains, "example.co.uk");
        AssertContainsDomain(domains, "auth.example.co.uk");
        AssertContainsDomain(domains, "partner.com.au");
        AssertContainsDomain(domains, "cdn.partner.com.au");
        AssertContainsDomain(domains, "mediahub.net");
        AssertContainsDomain(domains, "img.mediahub.net");
        AssertContainsDomain(domains, "retina.mediahub.net");
        AssertNotContainsDomain(domains, "example.com");
        AssertNotContainsDomain(domains, "www.example.com");
        AssertNotContainsDomain(domains, "google-analytics.com");
        AssertNotContainsDomain(domains, "googleapis.com");
        AssertNotContainsDomain(domains, "fonts.googleapis.com");
        Assert(domains.SequenceEqual(domains.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase), "Discovered domains should be sorted.");
        return Task.CompletedTask;
    }

    private static async Task TestDomainDiscoveryNormalizesInput()
    {
        var fetchedDomain = "";
        var service = new DomainDiscoveryService(domain =>
        {
            fetchedDomain = domain;
            return Task.FromResult<string?>("""
                <script>fetch("https://api.partner.net/v1")</script>
                """);
        });

        var result = await service.DiscoverRelatedDomainsAsync(" HTTPS://Shop.Example.COM/path ");

        Assert(result.Error == null, $"Unexpected discovery error: {result.Error}");
        Assert(fetchedDomain == "shop.example.com", $"Expected normalized host, got `{fetchedDomain}`.");
        AssertContainsDomain(result.DiscoveredDomains, "partner.net");
        AssertContainsDomain(result.DiscoveredDomains, "api.partner.net");
    }

    private static async Task TestDomainDiscoveryReportsFetchFailuresAsLoadErrors()
    {
        var service = new DomainDiscoveryService(_ =>
            throw new InvalidOperationException("DNS lookup exploded"));

        var result = await service.DiscoverRelatedDomainsAsync("example.com");

        Assert(result.DiscoveredDomains.Count == 0, "Fetch failures should not produce discovered domains.");
        Assert(result.Error == "Failed to load page.", "Fetch failures should be reported with the Flutter-style load error.");
    }

    private static Task TestInstalledAppServiceNormalizeForDisplay()
    {
        var apps = InstalledAppService.NormalizeForDisplay(
        [
            new InstalledApp
            {
                DisplayName = "Custom Browser",
                ExecutableName = "chrome.exe",
                Path = @"C:\Program Files\Custom\chrome.exe"
            },
            new InstalledApp
            {
                DisplayName = "Duplicate Chrome",
                ExecutableName = "CHROME.EXE",
                Path = @"C:\Other\chrome.exe"
            },
            new InstalledApp
            {
                DisplayName = "Setup Helper",
                ExecutableName = "setup.exe",
                Path = @"C:\Program Files\App\setup.exe"
            },
            new InstalledApp
            {
                DisplayName = "Work Tool",
                ExecutableName = "worktool.exe",
                Path = @"C:\Program Files\WorkTool\worktool.exe"
            }
        ]);

        Assert(apps.Any(app => app.ExecutableName.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase)), "Common app fallback should include Firefox.");
        Assert(apps.Any(app => app.ExecutableName.Equals("telegram.exe", StringComparison.OrdinalIgnoreCase)), "Common app fallback should include Telegram.");
        Assert(apps.Any(app => app.ExecutableName.Equals("worktool.exe", StringComparison.OrdinalIgnoreCase)), "Custom discovered app was removed.");
        Assert(!apps.Any(app => app.ExecutableName.Equals("setup.exe", StringComparison.OrdinalIgnoreCase)), "Installer executable should be filtered.");

        var chromeEntries = apps.Where(app => app.ExecutableName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert(chromeEntries.Count == 1, $"Expected one chrome.exe entry, got {chromeEntries.Count}.");
        Assert(chromeEntries[0].DisplayName == "Custom Browser", "Discovered Chrome entry should win over duplicate/common fallback.");
        Assert(chromeEntries[0].Path.EndsWith(@"Custom\chrome.exe", StringComparison.OrdinalIgnoreCase), "Discovered Chrome path should be preserved.");
        return Task.CompletedTask;
    }

    private static Task TestInstalledAppServiceDirectoryDisplayNames()
    {
        Assert(
            InstalledAppService.GetDisplayNameFromExecutablePath(@"C:\Program Files\Microsoft VS Code\Code.exe") == "Microsoft VS Code",
            "Executable discovery should display the parent app directory like Flutter.");
        Assert(
            InstalledAppService.GetDisplayNameFromExecutablePath(@"C:\Users\Alice\AppData\Local\Programs\GitHubDesktop\app-3.4.18\GitHubDesktop.exe") == "GitHubDesktop",
            "Versioned directories should fall back to the executable name.");
        Assert(
            InstalledAppService.GetDisplayNameFromExecutablePath("baretool.exe") == "baretool",
            "Bare executable names should fall back to the executable name without extension.");
        return Task.CompletedTask;
    }

    private static Task TestInstalledAppServiceParsesSteamLibraryFolders()
    {
        var vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path" "C:\\Program Files (x86)\\Steam"
                }
                "1"
                {
                    "path" "D:\\SteamLibrary"
                }
                "2" "E:\\OldSteamLibrary"
            }
            """;

        var libraries = InstalledAppService.ParseSteamLibraryFolders(vdf);

        Assert(libraries.SequenceEqual(
            [
                @"C:\Program Files (x86)\Steam",
                @"D:\SteamLibrary",
                @"E:\OldSteamLibrary"
            ]), "Steam libraryfolders.vdf paths should parse modern and legacy library formats.");
        return Task.CompletedTask;
    }

    private static Task TestSplitTunnelEntryNormalizesManualAppEntries()
    {
        Assert(SplitTunnelEntry.NormalizeAppProcessName(" game ") == "game.exe", "Bare manual app names should become Windows process names.");
        Assert(SplitTunnelEntry.NormalizeAppProcessName(@"D:\SteamLibrary\steamapps\common\Game\Game-Win64-Shipping.exe") == "Game-Win64-Shipping.exe", "Manual full paths should store only the executable process name.");
        Assert(SplitTunnelEntry.NormalizeAppProcessName("\"C:\\Games\\Cool Game\\coolgame.exe\" --launcher") == "coolgame.exe", "Quoted commands with arguments should keep the executable process name.");
        Assert(SplitTunnelEntry.NormalizeAppProcessName("   ") == "", "Blank manual app entries should be ignored.");
        return Task.CompletedTask;
    }

    private static Task TestVpnStartupErrorClassifier()
    {
        var accessDenied = VpnStartupErrorClassifier.Classify(1, ["CreateFile failed: Access is denied (code 0x5)"]);
        Assert(accessDenied.Message == "Access denied. Run the application as administrator.", "Access denied message mismatch.");
        Assert(accessDenied.LogMessages.Count == 2, "Access denied should include two user guidance log lines.");
        Assert(accessDenied.LogMessages[1].Contains("right-click", StringComparison.OrdinalIgnoreCase), "Access denied should explain how to run as administrator.");
        Assert(!accessDenied.WaitForWintunRelease, "Access denied should not wait for Wintun release.");

        var wintun = VpnStartupErrorClassifier.Classify(1, ["Wintun adapter already exists and is busy"]);
        Assert(wintun.Message == "Wintun adapter is still busy. Wait before retrying.", "Wintun message mismatch.");
        Assert(wintun.WaitForWintunRelease, "Wintun busy should wait for adapter release.");
        Assert(wintun.LogMessages.SequenceEqual(["Waiting for Wintun adapter to release...", "Wintun adapter should be free now."]), "Wintun log guidance mismatch.");

        var missingWintun = VpnStartupErrorClassifier.Classify(1, ["TRUSTTUNNEL_CLIENT make_tun_listener: Failed to load wintun: The specified module could not be found"]);
        Assert(missingWintun.Message == "Wintun driver is missing. Place wintun.dll in the client directory.", "Missing Wintun message mismatch.");
        Assert(!missingWintun.WaitForWintunRelease, "Missing Wintun should not wait for adapter release.");
        Assert(missingWintun.LogMessages.Any(line => line.Contains("wintun.dll", StringComparison.OrdinalIgnoreCase)), "Missing Wintun guidance should mention wintun.dll.");

        var generic = VpnStartupErrorClassifier.Classify(17, ["unknown startup failure"]);
        Assert(generic.Message == "Process exited with error code 17.", "Generic exit code message mismatch.");
        Assert(generic.LogMessages.Count == 0, "Generic error should not add special log guidance.");
        return Task.CompletedTask;
    }

    private static async Task TestVpnServiceMissingClientBinaryMessage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            var configService = new ConfigService(tempDir, [clientDir]);
            var vpnService = new VpnService(configService);
            var config = new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            };

            var ex = await AssertThrowsAsync<FileNotFoundException>(() => vpnService.ConnectAsync(config));

            Assert(ex.FileName == Path.Combine(clientDir, "trusttunnel_client.exe"), "Missing client FileName should point to the selected client directory.");
            Assert(ex.Message.Contains("trusttunnel_client.exe", StringComparison.Ordinal), "Missing client message should mention preferred executable.");
            Assert(ex.Message.Contains("trusttunnel.exe", StringComparison.Ordinal), "Missing client message should mention legacy executable fallback.");
            Assert(ex.Message.Contains(clientDir, StringComparison.Ordinal), "Missing client message should include selected client directory.");
            Assert(vpnService.Status == VpnStatus.Disconnected, "VPN status should reset to disconnected after missing binary.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServiceMissingWintunMessage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            Directory.CreateDirectory(clientDir);
            await File.WriteAllTextAsync(Path.Combine(clientDir, "trusttunnel_client.exe"), "fake executable placeholder");

            var configService = new ConfigService(tempDir, [clientDir]);
            var vpnService = new VpnService(configService);
            var config = new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            };

            var ex = await AssertThrowsAsync<FileNotFoundException>(() => vpnService.ConnectAsync(config));

            Assert(ex.FileName == Path.Combine(clientDir, "wintun.dll"), "Missing Wintun FileName should point to the selected client directory.");
            Assert(ex.Message.Contains("wintun.dll", StringComparison.OrdinalIgnoreCase), "Missing Wintun message should mention wintun.dll.");
            Assert(ex.Message.Contains("trusttunnel_client.exe", StringComparison.Ordinal), "Missing Wintun message should mention the selected executable.");
            Assert(ex.Message.Contains(clientDir, StringComparison.Ordinal), "Missing Wintun message should include selected client directory.");
            Assert(vpnService.Status == VpnStatus.Disconnected, "VPN status should reset to disconnected after missing Wintun.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServiceConnectWritesConfigAndLaunchesClient()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            await CreateFakeClientFilesAsync(clientDir);
            var exePath = Path.Combine(clientDir, "trusttunnel_client.exe");

            ProcessStartInfo? capturedStartInfo = null;
            var fakeProcess = new FakeVpnClientProcess();
            var configService = new ConfigService(tempDir, [clientDir]);
            var geoIpHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("203.0.113.0/24\n")
            });
            using var geoIpClient = new HttpClient(geoIpHandler);
            using var geoIpService = new GeoIpService(Path.Combine(tempDir, "geoip-cache"), geoIpClient);
            using var vpnService = new VpnService(
                configService,
                startInfo =>
                {
                    capturedStartInfo = startInfo;
                    return fakeProcess;
                },
                startupProbeDelay: TimeSpan.FromMilliseconds(1),
                wintunReleaseDelay: TimeSpan.Zero,
                geoIpService: geoIpService);

            var config = new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Port = 8443,
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8",
                LogLevel = "debug",
                SplitTunnelCountries = ["US"]
            };

            await vpnService.ConnectAsync(config);

            var configPath = await configService.GetConfigFilePathAsync();
            var writtenToml = await File.ReadAllTextAsync(configPath);
            var args = capturedStartInfo?.ArgumentList.ToArray() ?? [];

            Assert(vpnService.Status == VpnStatus.Connected, "VPN should become connected when the client process stays alive.");
            Assert(fakeProcess.StartCalled, "Client process was not started.");
            Assert(fakeProcess.BeginOutputReadLineCalled, "stdout reader was not started.");
            Assert(fakeProcess.BeginErrorReadLineCalled, "stderr reader was not started.");
            Assert(capturedStartInfo?.FileName == exePath, "Client executable path mismatch.");
            Assert(args.SequenceEqual(["--config", configPath, "--loglevel", "debug"]), "Client launch arguments mismatch.");
            Assert(writtenToml.Contains("hostname = \"vpn.example.com\"", StringComparison.Ordinal), "VPN config TOML was not written.");
            AssertContains(writtenToml, "\"203.0.113.0/24\"");
            Assert(vpnService.Logs.Any(line => line.Contains("GeoIP exclusions loaded: 1 CIDR ranges", StringComparison.Ordinal)), "GeoIP resolution log missing.");
            Assert(vpnService.Logs.Any(line => line.Contains("Connected successfully.", StringComparison.Ordinal)), "Successful connection log missing.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServiceSystemProxyWithoutWintun()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            Directory.CreateDirectory(clientDir);
            await File.WriteAllTextAsync(
                Path.Combine(clientDir, "trusttunnel_client.exe"),
                "fake executable placeholder");

            var fakeProcess = new FakeVpnClientProcess();
            var fakeProxy = new FakeSystemProxyManager();
            var configService = new ConfigService(tempDir, [clientDir]);
            using var vpnService = new VpnService(
                configService,
                _ => fakeProcess,
                startupProbeDelay: TimeSpan.Zero,
                wintunReleaseDelay: TimeSpan.FromSeconds(30),
                processExitReleaseDelay: TimeSpan.Zero,
                systemProxyManager: fakeProxy);

            await vpnService.ConnectAsync(new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8",
                ConnectionMode = VpnConnectionMode.SystemProxy,
                SplitTunnelCountries = ["US"]
            });

            var configPath = await configService.GetConfigFilePathAsync();
            var toml = await File.ReadAllTextAsync(configPath);

            Assert(vpnService.Status == VpnStatus.Connected, "System Proxy mode should connect without wintun.dll.");
            Assert(fakeProxy.RecoverCalls == 1, "System Proxy manager should recover stale settings on service construction.");
            Assert(fakeProxy.EnableCalls == 1, "System Proxy manager should be enabled after the client stays alive.");
            Assert(fakeProxy.LastPort == ServerConfig.SystemProxySocksPort, "System Proxy should use the configured local SOCKS port.");
            AssertContains(toml, "[listener.socks]");
            Assert(!toml.Contains("[listener.tun]", StringComparison.Ordinal), "System Proxy client config must not contain a TUN listener.");
            Assert(!toml.Contains("\"203.0.113.0/24\"", StringComparison.Ordinal), "System Proxy startup should skip GeoIP resolution.");

            await vpnService.DisconnectAsync();

            Assert(vpnService.Status == VpnStatus.Disconnected, "System Proxy mode should disconnect normally.");
            Assert(fakeProxy.RestoreCalls == 1, "Disconnect should restore the previous Windows proxy settings.");
            Assert(!fakeProxy.IsEnabled, "System Proxy manager should be disabled after restore.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServiceSystemProxyRestoresAfterExit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            Directory.CreateDirectory(clientDir);
            await File.WriteAllTextAsync(
                Path.Combine(clientDir, "trusttunnel_client.exe"),
                "fake executable placeholder");

            var fakeProcess = new FakeVpnClientProcess();
            var fakeProxy = new FakeSystemProxyManager();
            var configService = new ConfigService(tempDir, [clientDir]);
            using var vpnService = new VpnService(
                configService,
                _ => fakeProcess,
                startupProbeDelay: TimeSpan.Zero,
                wintunReleaseDelay: TimeSpan.Zero,
                processExitReleaseDelay: TimeSpan.FromMilliseconds(1),
                systemProxyManager: fakeProxy);

            await vpnService.ConnectAsync(new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8",
                ConnectionMode = VpnConnectionMode.SystemProxy
            });

            fakeProcess.TriggerExit(7);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            Assert(vpnService.Status == VpnStatus.Disconnected, "System Proxy mode should become disconnected after a client crash.");
            Assert(fakeProxy.RestoreCalls == 1, "A spontaneous client exit must restore Windows proxy settings.");
            Assert(!fakeProxy.IsEnabled, "A spontaneous client exit must not leave System Proxy enabled.");
            Assert(vpnService.Logs.Any(line => line.Contains("System Proxy settings restored", StringComparison.Ordinal)), "Proxy restoration should be visible in the log.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServiceSystemProxyRestoresDuringActivationExit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            Directory.CreateDirectory(clientDir);
            await File.WriteAllTextAsync(
                Path.Combine(clientDir, "trusttunnel_client.exe"),
                "fake executable placeholder");

            var fakeProcess = new FakeVpnClientProcess();
            var fakeProxy = new FakeSystemProxyManager
            {
                Enabled = () => fakeProcess.TriggerExit(9)
            };
            var configService = new ConfigService(tempDir, [clientDir]);
            using var vpnService = new VpnService(
                configService,
                _ => fakeProcess,
                startupProbeDelay: TimeSpan.Zero,
                wintunReleaseDelay: TimeSpan.Zero,
                processExitReleaseDelay: TimeSpan.Zero,
                systemProxyManager: fakeProxy);

            var ex = await AssertThrowsAsync<InvalidOperationException>(() => vpnService.ConnectAsync(new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8",
                ConnectionMode = VpnConnectionMode.SystemProxy
            }));

            Assert(
                ex.Message == "Process exited while System Proxy was being enabled.",
                "An activation-time client exit should report the precise startup failure.");
            Assert(vpnService.Status == VpnStatus.Disconnected, "Activation-time exit should leave Veil disconnected.");
            Assert(fakeProxy.RestoreCalls == 1, "Activation-time exit must restore Windows proxy settings.");
            Assert(!fakeProxy.IsEnabled, "Activation-time exit must not leave System Proxy enabled.");
            Assert(
                !vpnService.Logs.Any(line => line.Contains("Connected successfully.", StringComparison.Ordinal)),
                "Activation-time exit must never be reported as a successful connection.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServiceDisposesConnectedClientProcessAfterSpontaneousExit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            await CreateFakeClientFilesAsync(clientDir);

            var fakeProcess = new FakeVpnClientProcess();
            var configService = new ConfigService(tempDir, [clientDir]);
            using var vpnService = new VpnService(
                configService,
                _ => fakeProcess,
                startupProbeDelay: TimeSpan.FromMilliseconds(1),
                wintunReleaseDelay: TimeSpan.Zero,
                processExitReleaseDelay: TimeSpan.FromMilliseconds(1));

            await vpnService.ConnectAsync(new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            });

            fakeProcess.TriggerExit(0);
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            Assert(vpnService.Status == VpnStatus.Disconnected, "VPN should become disconnected after the connected client exits.");
            Assert(fakeProcess.Disposed, "Connected client process wrapper should be disposed after a spontaneous exit.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServiceDisposesClientProcessWhenStartThrows()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            await CreateFakeClientFilesAsync(clientDir);

            var fakeProcess = new FakeVpnClientProcess
            {
                StartException = new InvalidOperationException("start failed")
            };
            var configService = new ConfigService(tempDir, [clientDir]);
            using var vpnService = new VpnService(
                configService,
                _ => fakeProcess,
                startupProbeDelay: TimeSpan.FromMilliseconds(1),
                wintunReleaseDelay: TimeSpan.Zero);

            var config = new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            };

            var ex = await AssertThrowsAsync<InvalidOperationException>(() => vpnService.ConnectAsync(config));

            Assert(ex.Message == "start failed", "Process start exception should surface to the caller.");
            Assert(fakeProcess.Disposed, "Process wrapper should be disposed when start throws before _process is assigned.");
            Assert(vpnService.Status == VpnStatus.Disconnected, "VPN status should reset after start throws.");
            Assert(vpnService.ErrorMessage == "start failed", "VPN service should keep the start failure message.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServicePreservesConnectErrorAfterInternalCleanup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            await CreateFakeClientFilesAsync(clientDir);

            var fakeProcess = new FakeVpnClientProcess
            {
                BeginOutputReadLineException = new InvalidOperationException("stdout reader failed")
            };
            var configService = new ConfigService(tempDir, [clientDir]);
            using var vpnService = new VpnService(
                configService,
                _ => fakeProcess,
                startupProbeDelay: TimeSpan.FromMilliseconds(1),
                wintunReleaseDelay: TimeSpan.Zero);

            var config = new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            };

            var ex = await AssertThrowsAsync<InvalidOperationException>(() => vpnService.ConnectAsync(config));

            Assert(ex.Message == "stdout reader failed", "Reader startup error should surface to the caller.");
            Assert(vpnService.ErrorMessage == "stdout reader failed", "Internal cleanup after failed connect should preserve the original error message for the UI.");
            Assert(vpnService.Status == VpnStatus.Disconnected, "VPN status should reset after failed connect cleanup.");
            Assert(fakeProcess.Disposed, "Process wrapper should be disposed during failed connect cleanup.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServicePreservesStderrReaderStartupError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            await CreateFakeClientFilesAsync(clientDir);

            var fakeProcess = new FakeVpnClientProcess
            {
                BeginErrorReadLineException = new InvalidOperationException("stderr reader failed")
            };
            var configService = new ConfigService(tempDir, [clientDir]);
            using var vpnService = new VpnService(
                configService,
                _ => fakeProcess,
                startupProbeDelay: TimeSpan.FromMilliseconds(1),
                wintunReleaseDelay: TimeSpan.Zero);

            var config = new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            };

            var ex = await AssertThrowsAsync<InvalidOperationException>(() => vpnService.ConnectAsync(config));

            Assert(ex.Message == "stderr reader failed", "Stderr reader startup error should surface to the caller.");
            Assert(vpnService.ErrorMessage == "stderr reader failed", "Failed connect cleanup should preserve the stderr reader startup error for the UI.");
            Assert(vpnService.Status == VpnStatus.Disconnected, "VPN status should reset after stderr reader startup cleanup.");
            Assert(fakeProcess.BeginOutputReadLineCalled, "Stdout reader should start before the stderr reader failure.");
            Assert(fakeProcess.BeginErrorReadLineCalled, "Stderr reader failure path should be exercised.");
            Assert(fakeProcess.Disposed, "Process wrapper should be disposed during stderr reader startup cleanup.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServiceDuplicateLogCollapseNotifiesSubscribers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            await CreateFakeClientFilesAsync(clientDir);

            var fakeProcess = new FakeVpnClientProcess
            {
                OutputLines = ["request id=100 completed", "request id=101 completed"]
            };
            var configService = new ConfigService(tempDir, [clientDir]);
            using var vpnService = new VpnService(
                configService,
                _ => fakeProcess,
                startupProbeDelay: TimeSpan.FromMilliseconds(1),
                wintunReleaseDelay: TimeSpan.Zero);
            var observed = new List<string>();
            vpnService.LogAdded += observed.Add;

            await vpnService.ConnectAsync(new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            });

            Assert(vpnService.Logs.Any(line => line.Contains("request id=100 completed (x2)", StringComparison.Ordinal)), "Duplicate log line should be collapsed in the service log list.");
            Assert(observed.Any(line => line.Contains("request id=100 completed (x2)", StringComparison.Ordinal)), "Duplicate log collapse should notify log subscribers so the UI refreshes.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServiceClassifiesImmediateStartupFailure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            await CreateFakeClientFilesAsync(clientDir);

            var fakeProcess = new FakeVpnClientProcess
            {
                HasExited = true,
                ExitCode = 1,
                ErrorLines = ["CreateFile failed: Access is denied (code 0x5)"]
            };
            var configService = new ConfigService(tempDir, [clientDir]);
            using var vpnService = new VpnService(
                configService,
                _ => fakeProcess,
                startupProbeDelay: TimeSpan.FromMilliseconds(1),
                wintunReleaseDelay: TimeSpan.Zero);

            var config = new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            };

            var ex = await AssertThrowsAsync<InvalidOperationException>(() => vpnService.ConnectAsync(config));

            Assert(ex.Message == "Access denied. Run the application as administrator.", "Immediate startup error should surface classifier message.");
            Assert(vpnService.ErrorMessage == "Access denied. Run the application as administrator.", "VPN service error message mismatch.");
            Assert(vpnService.Status == VpnStatus.Disconnected, "VPN status should reset after immediate startup failure.");
            Assert(vpnService.Logs.Any(line => line.Contains("Administrator privileges are required", StringComparison.Ordinal)), "Admin guidance log missing.");
            Assert(fakeProcess.Disposed, "Failed startup process should be disposed.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServiceClassifiesBuriedWintunAccessDeniedStartupFailure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            await CreateFakeClientFilesAsync(clientDir);

            var shutdownNoise = Enumerable.Range(0, 30)
                .Select(i => $"INFO DNS proxy deinit: shutdown line {i}");
            var fakeProcess = new FakeVpnClientProcess
            {
                HasExited = true,
                ExitCode = 1,
                ErrorLines =
                [
                    "WINTUN log_wintun: Failed to create private namespace: denied (Code 0x00000005)",
                    "WINTUN create_wintun_adapter: WintunCreateAdapter: Access is denied",
                    .. shutdownNoise
                ]
            };
            var configService = new ConfigService(tempDir, [clientDir]);
            using var vpnService = new VpnService(
                configService,
                _ => fakeProcess,
                startupProbeDelay: TimeSpan.FromMilliseconds(1),
                wintunReleaseDelay: TimeSpan.Zero);

            var config = new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            };

            var ex = await AssertThrowsAsync<InvalidOperationException>(() => vpnService.ConnectAsync(config));

            Assert(ex.Message == "Access denied. Run the application as administrator.", "Buried Wintun access-denied startup error should surface classifier message.");
            Assert(vpnService.Logs.Any(line => line.Contains("Administrator privileges are required", StringComparison.Ordinal)), "Buried Wintun access-denied guidance log missing.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnServiceDoesNotReportConnectedAfterStartupTransitionExit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            await CreateFakeClientFilesAsync(clientDir);

            var fakeProcess = new FakeVpnClientProcess
            {
                ExitOnNextHasExitedCheck = true,
                ExitCode = 0
            };
            var configService = new ConfigService(tempDir, [clientDir]);
            using var vpnService = new VpnService(
                configService,
                _ => fakeProcess,
                startupProbeDelay: TimeSpan.FromMilliseconds(1),
                wintunReleaseDelay: TimeSpan.Zero,
                processExitReleaseDelay: TimeSpan.FromMilliseconds(1));

            var config = new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            };

            var ex = await AssertThrowsAsync<InvalidOperationException>(() => vpnService.ConnectAsync(config));
            await Task.Delay(TimeSpan.FromMilliseconds(20));

            Assert(ex.Message == "Process exited immediately after start.", "Startup transition exit should fail the connection attempt.");
            Assert(vpnService.Status == VpnStatus.Disconnected, "VPN status should reset when the client exits during the startup transition.");
            Assert(!vpnService.Logs.Any(line => line.Contains("Connected successfully.", StringComparison.Ordinal)), "Exited client should not be logged as connected.");
            Assert(fakeProcess.Disposed, "Exited startup process should be disposed.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestVpnProcessStopperGracefulFirst()
    {
        var process = new FakeStoppableProcess
        {
            GracefulStopAvailable = true,
            ExitOnGracefulWait = true
        };

        var result = await VpnProcessStopper.StopAsync(process, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));

        Assert(result.GracefulStopRequested, "Graceful stop should be requested.");
        Assert(result.GracefulStopSucceeded, "Graceful stop should succeed.");
        Assert(!result.ForceKillRequested, "Force kill should not be requested after graceful success.");
        Assert(process.GracefulRequests == 1, "Expected one graceful stop request.");
        Assert(process.ForceKillRequests == 0, "Expected no force kill request.");
    }

    private static async Task TestVpnProcessStopperForceAfterTimeout()
    {
        var process = new FakeStoppableProcess
        {
            GracefulStopAvailable = true,
            ExitOnGracefulWait = false,
            ExitOnForceWait = true
        };

        var result = await VpnProcessStopper.StopAsync(process, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));

        Assert(result.GracefulStopRequested, "Graceful stop should be requested.");
        Assert(!result.GracefulStopSucceeded, "Graceful stop should not succeed.");
        Assert(result.ForceKillRequested, "Force kill should be requested after graceful timeout.");
        Assert(result.ForceKillSucceeded, "Force kill should succeed.");
        Assert(process.GracefulRequests == 1, "Expected one graceful stop request.");
        Assert(process.ForceKillRequests == 1, "Expected one force kill request.");
    }

    private static async Task TestVpnProcessStopperNoGracefulChannel()
    {
        var process = new FakeStoppableProcess
        {
            GracefulStopAvailable = false,
            ExitOnForceWait = true
        };

        var result = await VpnProcessStopper.StopAsync(process, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));

        Assert(!result.GracefulStopRequested, "Graceful stop should not be reported when unavailable.");
        Assert(result.ForceKillRequested, "Force kill should be requested.");
        Assert(result.ForceKillSucceeded, "Force kill should succeed.");
        Assert(process.GracefulRequests == 1, "Expected one graceful stop attempt.");
        Assert(process.ForceKillRequests == 1, "Expected one force kill request.");
    }

    private static async Task TestStoppableProcessAdapterWindowsConsoleFallback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("Start-Sleep -Seconds 30");

            process = Process.Start(startInfo);
            var runningProcess = process ?? throw new InvalidOperationException("Test process did not start.");

            var fallbackCalled = false;
            var adapter = new StoppableProcessAdapter(runningProcess, _ =>
            {
                fallbackCalled = true;
                return true;
            });

            Assert(adapter.TryRequestGracefulStop(), "Windows console graceful fallback should report a stop request.");
            Assert(fallbackCalled, "Windows console graceful fallback was not attempted.");
        }
        finally
        {
            if (process is not null)
            {
                await KillProcessQuietlyAsync(process);
            }
        }
    }

    private static async Task TestVpnProcessStopperForceKillFailure()
    {
        var process = new FakeStoppableProcess
        {
            GracefulStopAvailable = false,
            ThrowOnForceKill = true
        };

        var result = await VpnProcessStopper.StopAsync(process, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));

        Assert(!result.GracefulStopRequested, "Graceful stop should not be reported when unavailable.");
        Assert(result.ForceKillRequested, "Force kill should be requested.");
        Assert(!result.ForceKillSucceeded, "Force kill should be reported as failed.");
        Assert(process.ForceKillRequests == 1, "Expected one force kill request.");
    }

    private static async Task TestAppProcessExitCleanupDisposesActiveServices()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Veil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clientDir = Path.Combine(tempDir, "client");
            await CreateFakeClientFilesAsync(clientDir);

            var fakeProcess = new FakeVpnClientProcess();
            var configService = new ConfigService(tempDir, [clientDir]);
            var vpnService = new VpnService(
                configService,
                _ => fakeProcess,
                startupProbeDelay: TimeSpan.Zero,
                wintunReleaseDelay: TimeSpan.Zero,
                processExitReleaseDelay: TimeSpan.Zero);

            await vpnService.ConnectAsync(new ServerConfig
            {
                Hostname = "vpn.example.com",
                Address = "203.0.113.10",
                Username = "alice",
                Password = "secret",
                Dns = "8.8.8.8"
            });

            var mutexName = $@"Local\Veil.Tests.ProcessExit.{Guid.NewGuid():N}";
            var guard = SingleInstanceGuard.Acquire(mutexName);
            Assert(guard.OwnsMutex, "Process-exit cleanup test should own a fresh mutex.");

            App.CleanupApplicationServices(vpnService, null, guard);

            Assert(fakeProcess.ForceKillCalled, "Process-exit cleanup should force-kill an active VPN client process.");
            Assert(fakeProcess.Disposed, "Process-exit cleanup should dispose the VPN process wrapper.");
            Assert(AcquireGuardOnNewThread(mutexName), "Process-exit cleanup should release the single-instance mutex.");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task TestSingleInstanceGuardAcquiresAbandonedMutex()
    {
        var mutexName = $@"Local\Veil.Tests.Abandoned.{Guid.NewGuid():N}";
        var processPath = Environment.ProcessPath
                          ?? throw new InvalidOperationException("Current test process path is unavailable.");
        using var ownerProcess = new Process
        {
            StartInfo = new ProcessStartInfo(processPath)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        ownerProcess.StartInfo.ArgumentList.Add("--hold-abandoned-mutex");
        ownerProcess.StartInfo.ArgumentList.Add(mutexName);

        Assert(ownerProcess.Start(), "Abandoned mutex owner process did not start.");

        var readyTask = ownerProcess.StandardOutput.ReadLineAsync();
        var completed = await Task.WhenAny(readyTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert(completed == readyTask && readyTask.Result == "ready", "Abandoned mutex owner process did not acquire the mutex.");

        Assert(ownerProcess.WaitForExit(5000), "Abandoned mutex owner process did not exit.");
        Assert(ownerProcess.ExitCode == 42, "Abandoned mutex owner process exited unexpectedly.");

        using var guard = SingleInstanceGuard.Acquire(mutexName);
        Assert(guard.OwnsMutex, "Single instance guard should acquire an abandoned mutex after a crashed owner.");
    }

    private static void HoldAbandonedMutexAndExit(string mutexName)
    {
        var mutex = new Mutex(initiallyOwned: false, mutexName);
        mutex.WaitOne();
        Console.WriteLine("ready");
        Console.Out.Flush();
        Environment.Exit(42);

        GC.KeepAlive(mutex);
    }

    private static Task TestSingleInstanceGuardDoesNotReleaseForeignMutex()
    {
        var mutexName = $@"Local\Veil.Tests.{Guid.NewGuid():N}";
        using var primary = SingleInstanceGuard.Acquire(mutexName);

        Assert(primary.OwnsMutex, "Primary guard should own a fresh single-instance mutex.");

        var duplicateOwnsMutex = AcquireGuardOnNewThread(mutexName);

        Assert(!duplicateOwnsMutex, "Duplicate guard should not own an already-held mutex.");

        var stillBlocked = AcquireGuardOnNewThread(mutexName);

        Assert(!stillBlocked, "Disposing a duplicate guard must not release the primary mutex.");

        primary.Dispose();

        var reacquired = AcquireGuardOnNewThread(mutexName);

        Assert(reacquired, "Mutex should be acquirable after the primary guard is disposed.");
        return Task.CompletedTask;
    }

    private static bool AcquireGuardOnNewThread(string mutexName)
    {
        bool ownsMutex = false;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var guard = SingleInstanceGuard.Acquire(mutexName);
                ownsMutex = guard.OwnsMutex;
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.Start();
        thread.Join();
        if (error != null)
        {
            throw error;
        }

        return ownsMutex;
    }

    private static void AssertContains(string text, string expected) =>
        Assert(text.Contains(expected, StringComparison.Ordinal), $"Expected to find `{expected}`.");

    private static void AssertContainsDomain(IEnumerable<string> domains, string expected) =>
        Assert(domains.Contains(expected, StringComparer.OrdinalIgnoreCase), $"Expected to find domain `{expected}`.");

    private static void AssertNotContainsDomain(IEnumerable<string> domains, string expected) =>
        Assert(!domains.Contains(expected, StringComparer.OrdinalIgnoreCase), $"Expected not to find domain `{expected}`.");

    private static async Task CreateFakeClientFilesAsync(string clientDir)
    {
        Directory.CreateDirectory(clientDir);
        await File.WriteAllTextAsync(Path.Combine(clientDir, "trusttunnel_client.exe"), "fake executable placeholder");
        await File.WriteAllTextAsync(Path.Combine(clientDir, "wintun.dll"), "fake driver placeholder");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static async Task KillProcessQuietlyAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private sealed class FakeServerSetupSshSessionFactory : IServerSetupSshSessionFactory
    {
        public FakeServerSetupSshSession Session { get; } = new();
        public int ConnectCalls { get; private set; }

        public IServerSetupSshSession Connect(ServerSetupConfig config)
        {
            ConnectCalls++;
            return Session;
        }
    }

    private sealed class FakeServerSetupSshSession : IServerSetupSshSession
    {
        private int _endpointProbeCount;
        private int _certificateProbeCount;

        public bool IsConnected { get; private set; } = true;
        public bool DisconnectCalled { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailInstallDownload { get; set; }
        public bool FailPortProbe { get; set; }
        public bool EndpointAlreadyInstalled { get; set; }
        public bool CertificateMissingInitially { get; set; }
        public bool FailStandaloneCertbotPortBusy { get; set; }
        public bool ExistingServiceActive { get; set; } = true;
        public bool ExistingServiceUnitExists { get; set; } = true;
        public string InstalledEndpointVersion { get; set; } = "1.0.33";
        public HashSet<int> BusyListenPorts { get; } = [];
        public List<string> Operations { get; } = [];
        public List<string> Commands { get; } = [];
        public List<string> UploadedPaths { get; } = [];
        public Dictionary<string, string> UploadedFiles { get; } = [];

        public ServerSetupCommandResult RunCommand(string command)
        {
            Operations.Add($"command:{command}");
            Commands.Add(command);

            if (command == "uname -m")
            {
                return Success("x86_64");
            }

            if (command == "cat /etc/os-release | head -5")
            {
                return Success("PRETTY_NAME=\"Debian GNU/Linux\"");
            }

            if (command == "test -f /opt/trusttunnel/trusttunnel_endpoint")
            {
                _endpointProbeCount++;
                if (!EndpointAlreadyInstalled && _endpointProbeCount == 1)
                {
                    throw new InvalidOperationException("not installed");
                }

                return Success();
            }

            if (command == "/opt/trusttunnel/trusttunnel_endpoint --version")
            {
                return Success(InstalledEndpointVersion);
            }

            if (command == "which curl")
            {
                return Success("/usr/bin/curl");
            }

            if (command == "which certbot")
            {
                return Success("/usr/bin/certbot");
            }

            if (command.StartsWith("ss -tuln", StringComparison.Ordinal))
            {
                if (FailPortProbe)
                {
                    throw new InvalidOperationException("ss failed");
                }

                foreach (var port in BusyListenPorts)
                {
                    if (command.Contains($":{port} ", StringComparison.Ordinal))
                    {
                        return Success($"LISTEN 0 4096 0.0.0.0:{port} 0.0.0.0:*");
                    }
                }

                return Success();
            }

            if (command.StartsWith("curl -fsSL", StringComparison.Ordinal))
            {
                if (FailInstallDownload)
                {
                    throw new InvalidOperationException("download failed");
                }

                return Success();
            }

            if (command.StartsWith("apt-get install ", StringComparison.Ordinal) ||
                command.StartsWith("apt-get update ", StringComparison.Ordinal))
            {
                return Success();
            }

            if (command.StartsWith("test -f /etc/letsencrypt/live/", StringComparison.Ordinal))
            {
                _certificateProbeCount++;
                if (CertificateMissingInitially && _certificateProbeCount == 1)
                {
                    throw new InvalidOperationException("certificate missing");
                }

                return Success();
            }

            if (command.StartsWith("certbot certonly ", StringComparison.Ordinal))
            {
                if (FailStandaloneCertbotPortBusy &&
                    command.StartsWith("certbot certonly --non-interactive --standalone", StringComparison.Ordinal))
                {
                    FailStandaloneCertbotPortBusy = false;
                    throw new InvalidOperationException("TCP port 80 is already in use");
                }

                return Success();
            }

            if (command.StartsWith("chmod ", StringComparison.Ordinal) ||
                command.StartsWith("cp /opt/trusttunnel/", StringComparison.Ordinal) ||
                command.StartsWith("systemctl stop trusttunnel", StringComparison.Ordinal) ||
                command is "systemctl daemon-reload" or "systemctl enable trusttunnel" or "systemctl start trusttunnel")
            {
                return Success();
            }

            if (command == "systemctl is-active trusttunnel")
            {
                return Success("active");
            }

            if (command == "systemctl show trusttunnel --property=ActiveState --value 2>/dev/null || true")
            {
                if (!ExistingServiceUnitExists)
                {
                    return Success();
                }

                return Success(ExistingServiceActive ? "active" : "inactive");
            }

            throw new InvalidOperationException($"Unexpected fake SSH command: {command}");
        }

        public void UploadFile(string remotePath, string content)
        {
            Operations.Add($"upload:{remotePath}");
            UploadedPaths.Add(remotePath);
            UploadedFiles[remotePath] = content;
        }

        public void Disconnect()
        {
            DisconnectCalled = true;
            IsConnected = false;
        }

        public void Dispose()
        {
            Disposed = true;
        }

        private static ServerSetupCommandResult Success(string output = "") =>
            new(output, "", 0);
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class FakeSystemProxyManager : ISystemProxyManager
    {
        public bool IsEnabled { get; private set; }
        public Action? Enabled { get; init; }
        public int RecoverCalls { get; private set; }
        public int EnableCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int LastPort { get; private set; }

        public void RecoverStaleState() => RecoverCalls++;

        public void EnableSocksProxy(int port)
        {
            EnableCalls++;
            LastPort = port;
            IsEnabled = true;
            Enabled?.Invoke();
        }

        public void Restore()
        {
            RestoreCalls++;
            IsEnabled = false;
        }

        public void Dispose()
        {
            DisposeCalls++;
            IsEnabled = false;
        }
    }

    private sealed class FakeVpnClientProcess : IVpnClientProcess
    {
        public event Action<string>? OutputLine;
        public event Action<string>? ErrorLine;
        public event EventHandler? Exited;

        private bool _hasExited;

        public bool HasExited
        {
            get
            {
                if (ExitOnNextHasExitedCheck)
                {
                    ExitOnNextHasExitedCheck = false;
                    var reportedBeforeExit = _hasExited;
                    RaiseExited();
                    return reportedBeforeExit;
                }

                return _hasExited;
            }
            set => _hasExited = value;
        }

        public int? ExitCode { get; set; }
        public string[] OutputLines { get; init; } = [];
        public string[] ErrorLines { get; init; } = [];
        public bool ExitOnNextHasExitedCheck { get; set; }
        public Exception? StartException { get; init; }
        public Exception? BeginOutputReadLineException { get; init; }
        public Exception? BeginErrorReadLineException { get; init; }
        public bool StartResult { get; init; } = true;
        public bool StartCalled { get; private set; }
        public bool BeginOutputReadLineCalled { get; private set; }
        public bool BeginErrorReadLineCalled { get; private set; }
        public bool Disposed { get; private set; }
        public bool ForceKillCalled { get; private set; }

        public bool Start()
        {
            StartCalled = true;
            if (StartException != null)
            {
                throw StartException;
            }

            return StartResult;
        }

        public void BeginOutputReadLine()
        {
            BeginOutputReadLineCalled = true;
            if (BeginOutputReadLineException != null)
            {
                throw BeginOutputReadLineException;
            }

            foreach (var line in OutputLines)
            {
                OutputLine?.Invoke(line);
            }
        }

        public void BeginErrorReadLine()
        {
            BeginErrorReadLineCalled = true;
            if (BeginErrorReadLineException != null)
            {
                throw BeginErrorReadLineException;
            }

            foreach (var line in ErrorLines)
            {
                ErrorLine?.Invoke(line);
            }
        }

        public bool TryRequestGracefulStop() => false;

        public void ForceKill()
        {
            ForceKillCalled = true;
            ExitCode ??= -1;
            RaiseExited();
        }

        public void TriggerExit(int? exitCode = 0)
        {
            ExitCode = exitCode;
            RaiseExited();
        }

        private void RaiseExited()
        {
            _hasExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }

        public Task WaitForExitAsync(TimeSpan timeout)
        {
            if (HasExited)
            {
                return Task.CompletedTask;
            }

            throw new TimeoutException();
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeStoppableProcess : IStoppableProcess
    {
        public bool HasExited { get; private set; }
        public bool GracefulStopAvailable { get; init; }
        public bool ExitOnGracefulWait { get; init; }
        public bool ExitOnForceWait { get; init; }
        public bool ThrowOnForceKill { get; init; }
        public int GracefulRequests { get; private set; }
        public int ForceKillRequests { get; private set; }

        public bool TryRequestGracefulStop()
        {
            GracefulRequests++;
            return GracefulStopAvailable;
        }

        public void ForceKill()
        {
            ForceKillRequests++;
            if (ThrowOnForceKill)
            {
                throw new InvalidOperationException("Synthetic force kill failure.");
            }
        }

        public Task WaitForExitAsync(TimeSpan timeout)
        {
            if (GracefulRequests > 0 && ForceKillRequests == 0 && ExitOnGracefulWait)
            {
                HasExited = true;
                return Task.CompletedTask;
            }

            if (ForceKillRequests > 0 && ExitOnForceWait)
            {
                HasExited = true;
                return Task.CompletedTask;
            }

            throw new TimeoutException();
        }
    }
}
