using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Veil.Models;

namespace Veil.Services;

public sealed class InstalledAppService
{
    public Task<List<InstalledApp>> GetInstalledAppsAsync() => Task.Run(() =>
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

        AddFromRegistry(apps, Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"));
        AddFromRegistry(apps, Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"));
        AddFromRegistry(apps, Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"));

        foreach (var dir in ProgramDirectories())
        {
            AddExecutablesFromDirectory(apps, dir, maxDepth: 2);
        }

        foreach (var library in SteamLibraryDirectories())
        {
            AddSteamGamesFromLibrary(apps, library);
        }

        return NormalizeForDisplay(apps.Values);
    });

    private static IEnumerable<string> ProgramDirectories()
    {
        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        };

        return dirs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static List<InstalledApp> NormalizeForDisplay(IEnumerable<InstalledApp> apps)
    {
        var deduped = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            var exeName = app.ExecutableName.Trim();
            if (string.IsNullOrWhiteSpace(exeName) || IsSystemExecutable(exeName))
            {
                continue;
            }

            deduped.TryAdd(exeName, new InstalledApp
            {
                DisplayName = string.IsNullOrWhiteSpace(app.DisplayName)
                    ? Path.GetFileNameWithoutExtension(exeName)
                    : app.DisplayName.Trim(),
                ExecutableName = exeName,
                Path = app.Path
            });
        }

        AddCommonApps(deduped);
        return deduped.Values
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(app => app.ExecutableName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddFromRegistry(Dictionary<string, InstalledApp> apps, RegistryKey? uninstallKey)
    {
        if (uninstallKey == null)
        {
            return;
        }

        using (uninstallKey)
        {
            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var subKey = uninstallKey.OpenSubKey(subKeyName);
                if (subKey == null)
                {
                    continue;
                }

                var displayName = subKey.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                var displayIcon = (subKey.GetValue("DisplayIcon") as string ?? "").Trim('"');
                var installLocation = (subKey.GetValue("InstallLocation") as string ?? "").Trim('"');
                var exePath = ResolveExecutablePath(displayIcon, installLocation);
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    continue;
                }

                var exeName = Path.GetFileName(exePath);
                if (string.IsNullOrWhiteSpace(exeName) || IsSystemExecutable(exeName))
                {
                    continue;
                }

                apps.TryAdd(exeName, new InstalledApp
                {
                    DisplayName = displayName,
                    ExecutableName = exeName,
                    Path = exePath
                });
            }
        }
    }

    private static string? ResolveExecutablePath(string displayIcon, string installLocation)
    {
        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            var cleaned = displayIcon.Split(',')[0].Trim('"');
            if (File.Exists(cleaned) && cleaned.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return cleaned;
            }
        }

        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            return Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }

        return null;
    }

    private static void AddExecutablesFromDirectory(Dictionary<string, InstalledApp> apps, string root, int maxDepth)
    {
        try
        {
            foreach (var exe in EnumerateExecutables(root, maxDepth))
            {
                var exeName = Path.GetFileName(exe);
                if (string.IsNullOrWhiteSpace(exeName) || IsSystemExecutable(exeName))
                {
                    continue;
                }

                apps.TryAdd(exeName, new InstalledApp
                {
                    DisplayName = GetDisplayNameFromExecutablePath(exe),
                    ExecutableName = exeName,
                    Path = exe
                });
            }
        }
        catch
        {
            // Some program folders are protected; skip inaccessible paths.
        }
    }

    private static IEnumerable<string> SteamLibraryDirectories()
    {
        var roots = SteamRootCandidates()
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var libraries = new List<string>();
        foreach (var root in roots)
        {
            libraries.Add(root);

            var libraryFoldersPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFoldersPath))
            {
                continue;
            }

            try
            {
                libraries.AddRange(ParseSteamLibraryFolders(File.ReadAllText(libraryFoldersPath)));
            }
            catch
            {
                // Steam library discovery is best effort.
            }
        }

        return libraries
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SteamRootCandidates()
    {
        foreach (var value in ReadRegistryString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"))
        {
            yield return value;
        }

        foreach (var value in ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"))
        {
            yield return value;
        }

        foreach (var value in ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"))
        {
            yield return value;
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Steam");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Steam");
        }
    }

    private static IEnumerable<string> ReadRegistryString(RegistryKey root, string subKeyName, string valueName)
    {
        using var subKey = root.OpenSubKey(subKeyName);
        var value = subKey?.GetValue(valueName) as string;
        if (!string.IsNullOrWhiteSpace(value))
        {
            yield return value.Replace('/', Path.DirectorySeparatorChar).Trim();
        }
    }

    internal static List<string> ParseSteamLibraryFolders(string libraryFoldersVdf)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(
                     libraryFoldersVdf,
                     "\"path\"\\s+\"(?<path>(?:\\\\.|[^\"])*)\"",
                     RegexOptions.IgnoreCase))
        {
            AddSteamPath(result, seen, match.Groups["path"].Value);
        }

        foreach (Match match in Regex.Matches(
                     libraryFoldersVdf,
                     "\"\\d+\"\\s+\"(?<path>(?:\\\\.|[^\"])*)\"",
                     RegexOptions.IgnoreCase))
        {
            AddSteamPath(result, seen, match.Groups["path"].Value);
        }

        return result;
    }

    private static void AddSteamPath(List<string> result, HashSet<string> seen, string value)
    {
        var path = value
            .Replace(@"\\", "\\", StringComparison.Ordinal)
            .Replace(@"\/", "/", StringComparison.Ordinal)
            .Trim();

        if (path.Length > 0 && seen.Add(path))
        {
            result.Add(path);
        }
    }

    private static void AddSteamGamesFromLibrary(Dictionary<string, InstalledApp> apps, string libraryPath)
    {
        var commonPath = Path.Combine(libraryPath, "steamapps", "common");
        if (!Directory.Exists(commonPath))
        {
            return;
        }

        IEnumerable<string> gameDirectories;
        try
        {
            gameDirectories = Directory.EnumerateDirectories(commonPath).ToList();
        }
        catch
        {
            return;
        }

        foreach (var gameDirectory in gameDirectories)
        {
            var gameName = Path.GetFileName(gameDirectory);
            foreach (var exe in EnumerateExecutables(gameDirectory, maxDepth: 3))
            {
                var exeName = Path.GetFileName(exe);
                if (string.IsNullOrWhiteSpace(exeName) || IsSystemExecutable(exeName))
                {
                    continue;
                }

                apps.TryAdd(exeName, new InstalledApp
                {
                    DisplayName = SteamGameDisplayName(gameName, exeName),
                    ExecutableName = exeName,
                    Path = exe
                });
            }
        }
    }

    private static string SteamGameDisplayName(string gameName, string exeName)
    {
        var exeDisplayName = Path.GetFileNameWithoutExtension(exeName);
        return gameName.Equals(exeDisplayName, StringComparison.OrdinalIgnoreCase)
            ? gameName
            : $"{gameName} - {exeDisplayName}";
    }

    private static IEnumerable<string> EnumerateExecutables(string root, int maxDepth)
    {
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            var (path, depth) = pending.Dequeue();
            IEnumerable<string> files = [];
            IEnumerable<string> dirs = [];

            try
            {
                files = Directory.EnumerateFiles(path, "*.exe");
                if (depth < maxDepth)
                {
                    dirs = Directory.EnumerateDirectories(path);
                }
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var dir in dirs)
            {
                pending.Enqueue((dir, depth + 1));
            }
        }
    }

    internal static string GetDisplayNameFromExecutablePath(string exePath)
    {
        var exeName = Path.GetFileNameWithoutExtension(exePath);
        var directoryName = Path.GetFileName(Path.GetDirectoryName(exePath));
        return !string.IsNullOrWhiteSpace(directoryName) &&
               !directoryName.Contains('.', StringComparison.Ordinal)
            ? directoryName
            : exeName;
    }

    private static void AddCommonApps(Dictionary<string, InstalledApp> apps)
    {
        foreach (var app in CommonApps())
        {
            apps.TryAdd(app.ExecutableName, app);
        }
    }

    private static IEnumerable<InstalledApp> CommonApps()
    {
        yield return new InstalledApp { ExecutableName = "chrome.exe", DisplayName = "Google Chrome" };
        yield return new InstalledApp { ExecutableName = "firefox.exe", DisplayName = "Mozilla Firefox" };
        yield return new InstalledApp { ExecutableName = "msedge.exe", DisplayName = "Microsoft Edge" };
        yield return new InstalledApp { ExecutableName = "opera.exe", DisplayName = "Opera" };
        yield return new InstalledApp { ExecutableName = "brave.exe", DisplayName = "Brave Browser" };
        yield return new InstalledApp { ExecutableName = "telegram.exe", DisplayName = "Telegram" };
        yield return new InstalledApp { ExecutableName = "discord.exe", DisplayName = "Discord" };
        yield return new InstalledApp { ExecutableName = "slack.exe", DisplayName = "Slack" };
        yield return new InstalledApp { ExecutableName = "spotify.exe", DisplayName = "Spotify" };
        yield return new InstalledApp { ExecutableName = "steam.exe", DisplayName = "Steam" };
        yield return new InstalledApp { ExecutableName = "epicgameslauncher.exe", DisplayName = "Epic Games" };
        yield return new InstalledApp { ExecutableName = "code.exe", DisplayName = "VS Code" };
        yield return new InstalledApp { ExecutableName = "idea64.exe", DisplayName = "IntelliJ IDEA" };
        yield return new InstalledApp { ExecutableName = "torrent.exe", DisplayName = "Torrent Client" };
        yield return new InstalledApp { ExecutableName = "qbittorrent.exe", DisplayName = "qBittorrent" };
    }

    private static bool IsSystemExecutable(string name)
    {
        var lowerName = name.ToLowerInvariant();
        var systemMarkers = new[]
        {
            "uninstall",
            "uninst",
            "setup",
            "install",
            "update",
            "updater",
            "helper",
            "crash",
            "reporter",
            "service"
        };

        return systemMarkers.Any(lowerName.Contains);
    }
}
