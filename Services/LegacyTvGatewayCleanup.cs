using System.Diagnostics;
using System.Threading;

namespace Veil.Services;

/// <summary>
/// Veil 0.1 test builds could route a TV through a Windows NAT named "VeilTvGateway" with the gateway
/// address 192.168.250.1. The feature is gone; if such a build exited without cleaning up, remove both
/// once. The address is only removed together with Veil's own NAT, so a LAN that really uses it is left alone.
/// </summary>
internal static class LegacyTvGatewayCleanup
{
    private const string Script =
        "$nat = Get-NetNat -Name 'VeilTvGateway' -ErrorAction SilentlyContinue; " +
        "if ($nat) { " +
        "$nat | Remove-NetNat -Confirm:$false -ErrorAction Stop; " +
        "Get-NetIPAddress -IPAddress '192.168.250.1' -PrefixLength 24 -ErrorAction SilentlyContinue | Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue " +
        "}";

    public static async Task RunOnceAsync(ConfigService configService)
    {
        try
        {
            var preferences = await configService.LoadPreferencesAsync();
            if (preferences.LegacyTvGatewayChecked)
            {
                return;
            }

            if (!await RunPowerShellAsync(Script, TimeSpan.FromSeconds(60)))
            {
                return; // Try again on the next launch.
            }

            preferences = await configService.LoadPreferencesAsync();
            preferences.LegacyTvGatewayChecked = true;
            await configService.SavePreferencesAsync(preferences);
        }
        catch
        {
            // Best effort cleanup of an old feature; never disturb startup.
        }
    }

    private static async Task<bool> RunPowerShellAsync(string script, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return false;
        }

        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return false;
        }

        return process.ExitCode == 0;
    }
}
