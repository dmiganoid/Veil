using Veil.Models;

namespace Veil.Services;

/// <summary>
/// Builds the engine <c>exclusions</c> list from the saved split-tunnel state.
/// </summary>
internal static class SplitTunnelExclusions
{
    public static List<string> Build(ServerConfig config, IEnumerable<string>? runtimeExclusions = null)
    {
        var builder = new Builder();

        foreach (var entry in config.SplitTunnelDomains)
        {
            if (SplitTunnelEntry.TryNormalizeRule(entry, out var rule, out _))
            {
                builder.Add(rule);
            }
        }

        foreach (var entry in runtimeExclusions ?? [])
        {
            if (SplitTunnelEntry.TryNormalizeRule(entry, out var rule, out _))
            {
                builder.Add(rule);
            }
        }

        // The engine matches the executable basename case-insensitively, so one entry per app is enough.
        // Names may contain spaces: VeilEngine reads one exclusion per line.
        foreach (var app in config.SplitTunnelApps.Select(SplitTunnelEntry.NormalizeAppProcessName))
        {
            if (app.Length == 0)
            {
                continue;
            }

            builder.Add(app);
            foreach (var hint in AppRoutingHints(app))
            {
                builder.Add(hint);
            }
        }

        foreach (var entry in config.SplitTunnelExceptions)
        {
            if (SplitTunnelEntry.TryNormalizeException(entry, out var exception))
            {
                builder.Add(SplitTunnelEntry.ExceptionPrefix + exception);
            }
        }

        return builder.Items;
    }

    private static IEnumerable<string> AppRoutingHints(string executableName)
    {
        var lower = executableName.ToLowerInvariant();

        // Steam games authenticate and download through the Steam client and Valve CDNs,
        // so routing a game alone is not enough.
        if (SteamProcesses.Contains(lower) || SteamGames.Contains(lower))
        {
            foreach (var process in SteamProcesses)
            {
                yield return process;
            }

            foreach (var domain in SteamDomains)
            {
                yield return domain;
                yield return SplitTunnelEntry.WildcardPrefix + domain;
            }
        }

        // VMware Workstation's launcher does not own a NAT guest's connections. The VM and VMware NAT
        // service do, so treating Workstation as one app must include its networking processes.
        if (VmwareProcesses.Contains(lower))
        {
            foreach (var process in VmwareProcesses)
            {
                yield return process;
            }
        }
    }

    private static readonly string[] SteamGames = ["deadlock.exe", "dota2.exe"];

    private static readonly string[] SteamProcesses =
    [
        "steam.exe",
        "steamwebhelper.exe",
        "gameoverlayui.exe",
        "gameoverlayui64.exe",
        "steamservice.exe"
    ];

    private static readonly string[] SteamDomains =
    [
        "valve.net",
        "steamserver.net",
        "steampowered.com",
        "steamcommunity.com",
        "steamcontent.com",
        "steamstatic.com",
        "steamusercontent.com",
        "steam-chat.com",
        "steamgames.com"
    ];

    private static readonly string[] VmwareProcesses =
    [
        "vmware.exe",
        "vmplayer.exe",
        "vmware-vmx.exe",
        "vmnat.exe",
        "vmnetdhcp.exe"
    ];

    private sealed class Builder
    {
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Items { get; } = [];

        public void Add(string value)
        {
            if (value.Length > 0 && _seen.Add(value))
            {
                Items.Add(value);
            }
        }
    }
}
