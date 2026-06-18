using System.Threading;

namespace Veil.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        OwnsMutex = ownsMutex;
    }

    public bool OwnsMutex { get; }

    public static SingleInstanceGuard Acquire(string mutexName)
    {
        var mutex = new Mutex(initiallyOwned: false, mutexName, out _);
        var ownsMutex = false;
        try
        {
            ownsMutex = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        return new SingleInstanceGuard(mutex, ownsMutex);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (OwnsMutex)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
