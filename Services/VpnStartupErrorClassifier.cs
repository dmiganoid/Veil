namespace Veil.Services;

public sealed class VpnStartupError
{
    public string Message { get; init; } = "";
    public List<string> LogMessages { get; init; } = [];
    public bool WaitForWintunRelease { get; init; }
}

public static class VpnStartupErrorClassifier
{
    public static VpnStartupError Classify(int? exitCode, IEnumerable<string> recentLogs)
    {
        var recent = string.Join('\n', recentLogs).ToLowerInvariant();

        if (IsAccessDenied(recent))
        {
            return new VpnStartupError
            {
                Message = "Access denied. Run the application as administrator.",
                LogMessages =
                [
                    "Administrator privileges are required to create a VPN tunnel.",
                    "Close the application and run it as administrator (right-click -> Run as administrator)."
                ]
            };
        }

        if (IsWintunBusy(recent))
        {
            return new VpnStartupError
            {
                Message = "Wintun adapter is still busy. Wait before retrying.",
                LogMessages =
                [
                    "Waiting for Wintun adapter to release...",
                    "Wintun adapter should be free now."
                ],
                WaitForWintunRelease = true
            };
        }

        if (IsWintunMissing(recent))
        {
            return new VpnStartupError
            {
                Message = "Wintun driver is missing. Place wintun.dll in the client directory.",
                LogMessages =
                [
                    "The Veil engine could not load wintun.dll.",
                    "Download Wintun and place wintun.dll next to trusttunnel_client.exe, then try again."
                ]
            };
        }

        return new VpnStartupError
        {
            Message = exitCode.HasValue
                ? $"Process exited with error code {exitCode}."
                : "Process exited with error."
        };
    }

    private static bool IsAccessDenied(string recentLogs) =>
        recentLogs.Contains("access is denied", StringComparison.Ordinal) ||
        recentLogs.Contains("access denied", StringComparison.Ordinal) ||
        recentLogs.Contains("code 0x5", StringComparison.Ordinal) ||
        recentLogs.Contains("code 0x00000005", StringComparison.Ordinal);

    private static bool IsWintunBusy(string recentLogs) =>
        recentLogs.Contains("wintun", StringComparison.Ordinal) &&
        (recentLogs.Contains("already", StringComparison.Ordinal) ||
         recentLogs.Contains("in use", StringComparison.Ordinal) ||
         recentLogs.Contains("busy", StringComparison.Ordinal));

    private static bool IsWintunMissing(string recentLogs) =>
        recentLogs.Contains("wintun", StringComparison.Ordinal) &&
        (recentLogs.Contains("failed to load", StringComparison.Ordinal) ||
         recentLogs.Contains("specified module could not be found", StringComparison.Ordinal) ||
         recentLogs.Contains("wintun.dll", StringComparison.Ordinal) &&
         (recentLogs.Contains("not found", StringComparison.Ordinal) ||
          recentLogs.Contains("missing", StringComparison.Ordinal)));
}
