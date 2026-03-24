using System.Threading;

namespace Stt.App.Services;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _hasHandle;

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(initiallyOwned: false, name);
    }

    public bool TryAcquire()
    {
        if (_hasHandle)
        {
            return true;
        }

        try
        {
            _hasHandle = _mutex.WaitOne(0, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            _hasHandle = true;
        }

        return _hasHandle;
    }

    public void Dispose()
    {
        if (_hasHandle)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        _mutex.Dispose();
    }
}
