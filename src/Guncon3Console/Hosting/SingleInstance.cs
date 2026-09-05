// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.IO;
using System.Threading;

namespace Guncon3Console.Hosting
{
    /// <summary>
    /// One running copy per user session. Two copies would fight over each gun's USB
    /// device and over the three TetherScript virtual devices, so the first start
    /// takes a named mutex and holds it until the process exits; a second start finds
    /// it taken, sets a named event asking the first copy to show its window, and
    /// exits.
    ///
    /// Both names are <c>Local\</c>, i.e. per user session, because the TetherScript
    /// devices are per session too: a machine-wide lock would stop a second user's
    /// own copy for no reason.
    ///
    /// The mutex is owned by the thread that took it, so <see cref="TryAcquire"/> and
    /// <see cref="Dispose"/> must run on the same thread — in practice both run on
    /// Main's STA thread.
    /// </summary>
    internal sealed class SingleInstance : IDisposable
    {
        private const string MutexName = @"Local\Guncon3Console.Instance";
        private const string ShowEventName = @"Local\Guncon3Console.Show";

        /// <summary>How long <see cref="Dispose"/> waits for the listener thread to leave its wait.</summary>
        private static readonly TimeSpan ListenerStopTimeout = TimeSpan.FromSeconds(1);

        /// <summary>Null only when the lock object itself could not be created; see <see cref="TryAcquire"/>.</summary>
        private readonly Mutex _mutex;

        /// <summary>Null when the event could not be created; the handover is then off and nothing else
        /// changes.</summary>
        private readonly EventWaitHandle _show;

        /// <summary>Set by <see cref="Dispose"/> to end the listener's wait.</summary>
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);

        private Thread _listener;
        private bool _disposed;

        private SingleInstance(Mutex mutex, EventWaitHandle show)
        {
            _mutex = mutex;
            _show = show;
        }

        /// <summary>
        /// Takes the lock for this user session. Null means another copy already holds
        /// it and the caller must not touch a device. A lock that cannot be created at
        /// all is a warning and counts as taken: a broken lock must never lock the
        /// user out of their own program.
        /// </summary>
        public static SingleInstance TryAcquire()
        {
            Mutex mutex = null;
            try
            {
                mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

                if (!createdNew && !TryTakeOver(mutex))
                {
                    mutex.Dispose();
                    return null;
                }

                return new SingleInstance(mutex, CreateShowEvent());
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
            {
                Log.Warn($"The single-instance lock could not be taken ({ex.Message}); a second start will not be stopped.");
                mutex?.Dispose();
                return new SingleInstance(null, null);
            }
        }

        /// <summary>
        /// Another process created the mutex. It may still own it (a real second
        /// start), it may have released it while exiting, or it may have died holding
        /// it — which Windows reports as an abandoned mutex on the next wait and which
        /// still hands ownership over. A zero timeout, so a start never blocks here.
        /// </summary>
        private static bool TryTakeOver(Mutex mutex)
        {
            try
            {
                return mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                // The previous copy crashed or was killed. This one now owns the mutex.
                Log.Warn("The previous instance did not shut down cleanly; taking its lock over.");
                return true;
            }
        }

        /// <summary>
        /// The event a second start sets. Created by the first instance so the name
        /// exists for as long as this copy runs.
        /// </summary>
        private static EventWaitHandle CreateShowEvent()
        {
            try
            {
                return new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
            {
                Log.Warn($"The show-window event could not be created ({ex.Message}); a second start will exit without bringing this window to the front.");
                return null;
            }
        }

        /// <summary>
        /// Asks the copy that holds the lock to show its window. Safe to call before
        /// that copy has a listener: the event is auto-reset, so it stays set until
        /// something waits on it and the window then appears once.
        /// </summary>
        public static void SignalShow()
        {
            try
            {
                using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                show.Set();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
            {
                Log.Warn($"GUNCON3 is already running, but its window could not be asked to show itself: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts a background thread that calls <paramref name="onShow"/> every time
        /// another start asks for the window. The callback runs on that thread and must
        /// marshal to the UI thread itself.
        /// </summary>
        public void ListenForShow(Action onShow)
        {
            ArgumentNullException.ThrowIfNull(onShow);

            if (_show == null || _disposed || _listener != null) return;

            _listener = new Thread(() => Listen(onShow))
            {
                IsBackground = true,
                Name = "Guncon3 show listener"
            };
            _listener.Start();
        }

        private void Listen(Action onShow)
        {
            var handles = new WaitHandle[] { _show, _stop };

            while (true)
            {
                int signalled;
                try
                {
                    signalled = WaitHandle.WaitAny(handles);
                }
                catch (ObjectDisposedException)
                {
                    // Dispose closed the handles while this thread was in the wait;
                    // there is nothing left to listen for.
                    return;
                }

                if (signalled != 0) return; // _stop

                try
                {
                    onShow();
                }
                catch (Exception ex)
                {
                    // One failed callback must not silence every later request: this
                    // thread exists only to raise a window, so it keeps listening
                    // instead of taking the process down with it.
                    Log.Error("The single-instance listener's callback failed: " + ex);
                }
            }
        }

        /// <summary>
        /// Releases the lock and stops the listener. Must not run until after
        /// <see cref="App.Shutdown"/>: a restart taken the moment the window closes
        /// must not find the lock free while this process still holds a gun's USB
        /// device open.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _stop.Set();
            _listener?.Join(ListenerStopTimeout);

            if (_mutex != null)
            {
                // Releasing is tidiness, not correctness: a handle closed while owned
                // leaves an abandoned mutex, which the next start takes anyway. Caught
                // broadly (ApplicationException when not owned; SynchronizationLockException
                // is also documented for ReleaseMutex) because this runs on Main's way
                // out — nothing here may throw past Dispose.
                try { _mutex.ReleaseMutex(); }
                catch (Exception)
                {
                }

                _mutex.Dispose();
            }

            _show?.Dispose();
            _stop.Dispose();
        }
    }
}
