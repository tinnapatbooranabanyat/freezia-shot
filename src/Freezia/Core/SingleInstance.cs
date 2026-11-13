using System;
using System.Threading;

namespace Freezia.Core;

/// <summary>
/// Named-mutex single instance guard. The first instance listens on named events so a
/// second launch can ask it to show itself or to exit (used by the installer / uninstaller).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _owned;
    private EventWaitHandle? _activateEvent;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _activateWait;
    private RegisteredWaitHandle? _exitWait;

    public bool IsFirstInstance => _owned;

    public SingleInstance()
    {
        // No "Global\" prefix: one instance per user session (matches installer AppMutex).
        _mutex = new Mutex(initiallyOwned: true, AppInfo.MutexName, out bool createdNew);
        _owned = createdNew;
        if (!createdNew)
        {
            // The previous owner may have crashed (abandoned mutex): try to take it.
            try
            {
                _owned = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                _owned = true;
            }
        }
    }

    /// <summary>Start listening for requests from later instances (first instance only).</summary>
    public void Listen(Action onActivate, Action onExit)
    {
        if (!_owned) return;
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.ActivateEventName);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, AppInfo.ExitEventName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activateEvent, (_, _) => onActivate(), null, Timeout.Infinite, false);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) => onExit(), null, Timeout.Infinite, true);
    }

    /// <summary>Signal the running instance (from a second instance).</summary>
    public static bool Signal(string eventName)
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(eventName, out var ev))
            {
                using (ev) ev.Set();
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to signal running instance", ex);
        }
        return false;
    }

    /// <summary>Wait until the running instance releases its mutex (it has exited).</summary>
    public bool WaitForOtherToExit(TimeSpan timeout)
    {
        try
        {
            return _mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        _activateWait?.Unregister(null);
        _exitWait?.Unregister(null);
        _activateEvent?.Dispose();
        _exitEvent?.Dispose();
        if (_owned)
        {
            try { _mutex.ReleaseMutex(); } catch { /* not owned by this thread */ }
        }
        _mutex.Dispose();
    }
}
