using System;
using System.Threading;

namespace Watt.Services
{
    /// <summary>
    /// Single-instance guard for the tray app.
    ///
    /// The first process owns <c>Local\Watt.SingleInstance</c> (per user session, so
    /// multiple Windows users each get their own Watt) and listens in the background on
    /// <c>Local\Watt.SingleInstance.Activate</c>. A second launch calls
    /// <see cref="TryAcquire"/>, gets false, calls <see cref="NotifyExisting"/> to ask the
    /// running instance to show its panel, and then exits immediately - no window, no tray
    /// icon, no visible flash.
    ///
    /// The listener runs on an isolated background thread with a bounded wait so it can
    /// never keep the process alive and never block WPF startup. Events are raised on that
    /// thread: subscribers (the WPF shell) must marshal to the dispatcher themselves.
    ///
    /// The guard is deliberately permissive: if the named objects cannot be created
    /// (unusual ACL/sandbox situation) the app is still allowed to run rather than being
    /// prevented from starting.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        /// <summary>Per-session mutex name. Local\ keeps it per Windows logon session.</summary>
        public const string DefaultMutexName = @"Local\Watt.SingleInstance";

        /// <summary>Per-session auto-reset event the running instance listens on.</summary>
        public const string DefaultActivateEventName = @"Local\Watt.SingleInstance.Activate";

        private const int ListenerWaitMilliseconds = 500;
        private const int NotifyRetryCount = 20;
        private const int NotifyRetryDelayMilliseconds = 25;

        private readonly object _gate = new object();
        private readonly string _mutexName;
        private readonly string _eventName;

        private Mutex _mutex;
        private EventWaitHandle _activateEvent;
        private Thread _listener;
        private int _owningThreadId;
        private bool _acquired;
        private bool _disposed;

        public SingleInstance()
            : this(DefaultMutexName, DefaultActivateEventName)
        {
        }

        /// <summary>Creates a guard with custom kernel object names (used by tests).</summary>
        public SingleInstance(string mutexName, string eventName)
        {
            _mutexName = string.IsNullOrEmpty(mutexName) ? DefaultMutexName : mutexName;
            _eventName = string.IsNullOrEmpty(eventName) ? DefaultActivateEventName : eventName;
        }

        /// <summary>Raised on the listener thread when another launch asked to be shown.</summary>
        public event EventHandler ActivationRequested;

        public string MutexName
        {
            get { return _mutexName; }
        }

        public string ActivateEventName
        {
            get { return _eventName; }
        }

        /// <summary>True while this process owns the single-instance guard.</summary>
        public bool IsAcquired
        {
            get { lock (_gate) { return _acquired && !_disposed; } }
        }

        /// <summary>
        /// Tries to become the one running instance. Returns true when this process owns
        /// the guard (and the activation listener is running), false when another instance
        /// is already running. Never blocks for more than a WaitOne(0) probe.
        /// </summary>
        public bool TryAcquire()
        {
            lock (_gate)
            {
                if (_disposed) return false;
                if (_acquired) return true;

                try
                {
                    _mutex = new Mutex(false, _mutexName);
                    bool owns;
                    try
                    {
                        owns = _mutex.WaitOne(0);
                    }
                    catch (AbandonedMutexException)
                    {
                        // The previous owner died without releasing the mutex. We now own it.
                        owns = true;
                    }

                    if (!owns)
                    {
                        _mutex.Dispose();
                        _mutex = null;
                        return false;
                    }

                    _owningThreadId = Thread.CurrentThread.ManagedThreadId;
                    _acquired = true;

                    // The event is created only by the primary instance. If it cannot be
                    // created the app still runs, it just cannot be activated by a second launch.
                    try
                    {
                        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName);
                    }
                    catch (Exception ex)
                    {
                        ServiceLog.Error("SingleInstance create activation event", ex);
                    }

                    StartListener();
                    ServiceLog.Write("single instance acquired: " + _mutexName);
                    return true;
                }
                catch (Exception ex)
                {
                    // Permissive fallback: never prevent the app from starting because of
                    // the single-instance machinery.
                    ServiceLog.Error("SingleInstance.TryAcquire", ex);
                    if (_mutex != null)
                    {
                        try { _mutex.Dispose(); } catch { }
                        _mutex = null;
                    }

                    return true;
                }
            }
        }

        /// <summary>
        /// Signals the already-running instance to show itself. Safe to call when the
        /// running instance has not created its event yet (a bounded number of short
        /// retries) and safe to call when no instance exists at all (no exception).
        /// </summary>
        public void NotifyExisting()
        {
            for (var attempt = 0; attempt < NotifyRetryCount; attempt++)
            {
                try
                {
                    using (var existing = EventWaitHandle.OpenExisting(_eventName))
                    {
                        existing.Set();
                        return;
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // The running instance may still be creating its event; retry briefly.
                    Thread.Sleep(NotifyRetryDelayMilliseconds);
                }
                catch (Exception ex)
                {
                    ServiceLog.Error("SingleInstance.NotifyExisting", ex);
                    return;
                }
            }

            ServiceLog.Write("activation event not found: " + _eventName);
        }

        /// <summary>
        /// Stops the listener and releases the guard. Call on the thread that called
        /// TryAcquire (usually the WPF UI thread). Safe to call more than once.
        /// </summary>
        public void Dispose()
        {
            Thread listener;
            EventWaitHandle activateEvent;
            Mutex mutex;
            var ownsMutex = false;

            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _acquired = false;

                listener = _listener;
                _listener = null;
                activateEvent = _activateEvent;
                _activateEvent = null;
                mutex = _mutex;
                _mutex = null;
                ownsMutex = mutex != null && _owningThreadId == Thread.CurrentThread.ManagedThreadId;
            }

            // Wake the listener immediately, then give it a moment to exit.
            if (activateEvent != null)
            {
                try { activateEvent.Set(); } catch { }
            }

            if (listener != null)
            {
                try { listener.Join(ListenerWaitMilliseconds); } catch { }
            }

            if (activateEvent != null)
            {
                try { activateEvent.Dispose(); } catch { }
            }

            if (mutex != null)
            {
                if (ownsMutex)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }

                try { mutex.Dispose(); } catch { }
            }
        }

        private void StartListener()
        {
            var activateEvent = _activateEvent;
            if (activateEvent == null) return;

            _listener = new Thread((ThreadStart)delegate { ListenLoop(activateEvent); });
            _listener.IsBackground = true;
            _listener.Name = "Watt.SingleInstance";
            _listener.Start();
        }

        private void ListenLoop(EventWaitHandle activateEvent)
        {
            while (true)
            {
                bool signaled;
                try
                {
                    signaled = activateEvent.WaitOne(ListenerWaitMilliseconds);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (_disposed) return;
                    ServiceLog.Error("SingleInstance listener wait", ex);
                    try { Thread.Sleep(100); } catch { return; }
                    continue;
                }

                if (_disposed) return;
                if (!signaled) continue;

                var handler = ActivationRequested;
                if (handler == null) continue;

                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    ServiceLog.Error("SingleInstance activation handler", ex);
                }
            }
        }
    }
}
