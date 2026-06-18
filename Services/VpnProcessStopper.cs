using System.Diagnostics;

namespace Veil.Services;

public interface IStoppableProcess
{
    bool HasExited { get; }
    bool TryRequestGracefulStop();
    void ForceKill();
    Task WaitForExitAsync(TimeSpan timeout);
}

public sealed class ProcessStopResult
{
    public bool WasAlreadyExited { get; init; }
    public bool GracefulStopRequested { get; init; }
    public bool GracefulStopSucceeded { get; init; }
    public bool ForceKillRequested { get; init; }
    public bool ForceKillSucceeded { get; init; }
}

public static class VpnProcessStopper
{
    public static async Task<ProcessStopResult> StopAsync(
        IStoppableProcess process,
        TimeSpan gracefulTimeout,
        TimeSpan forceTimeout)
    {
        if (process.HasExited)
        {
            return new ProcessStopResult { WasAlreadyExited = true };
        }

        var gracefulRequested = process.TryRequestGracefulStop();
        if (gracefulRequested)
        {
            try
            {
                await process.WaitForExitAsync(gracefulTimeout);
                if (process.HasExited)
                {
                    return new ProcessStopResult
                    {
                        GracefulStopRequested = true,
                        GracefulStopSucceeded = true
                    };
                }
            }
            catch (TimeoutException)
            {
                // Fall through to force kill below.
            }
        }

        var forceKillRequested = false;
        if (!process.HasExited)
        {
            forceKillRequested = true;
            try
            {
                process.ForceKill();
            }
            catch
            {
                return new ProcessStopResult
                {
                    GracefulStopRequested = gracefulRequested,
                    ForceKillRequested = true,
                    ForceKillSucceeded = process.HasExited
                };
            }

            try
            {
                await process.WaitForExitAsync(forceTimeout);
            }
            catch (TimeoutException)
            {
                // Report below whether the force kill actually completed.
            }
        }

        return new ProcessStopResult
        {
            GracefulStopRequested = gracefulRequested,
            ForceKillRequested = forceKillRequested,
            ForceKillSucceeded = process.HasExited
        };
    }
}

public sealed class StoppableProcessAdapter : IStoppableProcess
{
    private readonly Process _process;
    private readonly Func<Process, bool>? _windowsConsoleStop;

    public StoppableProcessAdapter(Process process, Func<Process, bool>? windowsConsoleStop = null)
    {
        _process = process;
        _windowsConsoleStop = windowsConsoleStop;
    }

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch
            {
                return true;
            }
        }
    }

    public bool TryRequestGracefulStop()
    {
        try
        {
            if (_process.CloseMainWindow())
            {
                return true;
            }
        }
        catch
        {
        }

        return OperatingSystem.IsWindows() && TryRequestWindowsConsoleStop();
    }

    public void ForceKill()
    {
        if (!HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
    }

    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        try
        {
            await _process.WaitForExitAsync().WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            throw;
        }
    }

    private bool TryRequestWindowsConsoleStop()
    {
        if (_windowsConsoleStop is not null)
        {
            return _windowsConsoleStop(_process);
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/PID");
            startInfo.ArgumentList.Add(_process.Id.ToString());
            startInfo.ArgumentList.Add("/T");

            using var taskkill = Process.Start(startInfo);
            if (taskkill is null)
            {
                return false;
            }

            if (!taskkill.WaitForExit(2000))
            {
                TryKillProcess(taskkill);
                return false;
            }

            return taskkill.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch
        {
        }
    }
}
