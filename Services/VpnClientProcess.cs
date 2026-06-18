using System.Diagnostics;

namespace Veil.Services;

internal interface IVpnClientProcess : IStoppableProcess, IDisposable
{
    event Action<string>? OutputLine;
    event Action<string>? ErrorLine;
    event EventHandler? Exited;

    int? ExitCode { get; }
    bool Start();
    void BeginOutputReadLine();
    void BeginErrorReadLine();
}

internal sealed class VpnClientProcess : IVpnClientProcess
{
    private readonly Process _process;
    private readonly StoppableProcessAdapter _stopper;

    public event Action<string>? OutputLine;
    public event Action<string>? ErrorLine;
    public event EventHandler? Exited;

    public VpnClientProcess(ProcessStartInfo startInfo)
    {
        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        _stopper = new StoppableProcessAdapter(_process);

        _process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                OutputLine?.Invoke(args.Data.Trim());
            }
        };
        _process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                ErrorLine?.Invoke(args.Data.Trim());
            }
        };
        _process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
    }

    public bool HasExited => _stopper.HasExited;

    public int? ExitCode
    {
        get
        {
            try
            {
                return _process.ExitCode;
            }
            catch
            {
                return null;
            }
        }
    }

    public bool Start() => _process.Start();

    public void BeginOutputReadLine() => _process.BeginOutputReadLine();

    public void BeginErrorReadLine() => _process.BeginErrorReadLine();

    public bool TryRequestGracefulStop() => _stopper.TryRequestGracefulStop();

    public void ForceKill() => _stopper.ForceKill();

    public Task WaitForExitAsync(TimeSpan timeout) => _stopper.WaitForExitAsync(timeout);

    public void Dispose() => _process.Dispose();
}
