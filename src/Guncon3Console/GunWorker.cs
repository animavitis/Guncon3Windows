using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Guncon3.Core;
using GunconUSB;
using Guncon3Console.TetherScript;

namespace Guncon3Console
{
    /// <summary>
    /// Owns one gun end to end on its own thread: read, decode, map, feed. The
    /// blocking USB read is the clock — there is deliberately no sleep on the
    /// success path.
    /// </summary>
    internal sealed class GunWorker : IDisposable
    {
        private static readonly int[] BackoffMs = { 250, 500, 1000, 2000 };

        // Reconnect must be serialized across all workers: the claimed-path set is a
        // snapshot, and WinUSB opens are shared rather than exclusive, so two workers
        // evaluating it concurrently can both pick the same free device and then
        // corrupt each other's reads on one pipe.
        private static readonly object ReconnectGate = new object();

        private readonly int _index;
        private readonly GunconReader _reader;
        private readonly AbsMouseFeeder _mouse;
        private readonly KeyboardFeeder _keyboard;
        private readonly Func<GunSnapshot> _snapshot;
        private readonly Func<IReadOnlyCollection<string>> _claimedPaths;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _pauseGate = new object();
        private bool _pauseRequested;
        private bool _paused;

        private bool _connected = true;
        private int _backoffStep;

        private Thread _thread;

        public GunWorker(int index, GunconReader reader, AbsMouseFeeder mouse, KeyboardFeeder keyboard, Func<GunSnapshot> snapshot, Func<IReadOnlyCollection<string>> claimedPaths)
        {
            _index = index;
            _reader = reader;
            _mouse = mouse;
            _keyboard = keyboard;
            _snapshot = snapshot;
            _claimedPaths = claimedPaths;
        }

        private string Tag => $"[Gun {_index + 1}]";

        public void Start()
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = $"gun-{_index + 1}",
                Priority = ThreadPriority.AboveNormal
            };
            _thread.Start();
        }

        public void RequestStop()
        {
            _cts.Cancel();
            lock (_pauseGate) { Monitor.PulseAll(_pauseGate); }
        }

        public bool Join(TimeSpan timeout) => _thread == null || _thread.Join(timeout);

        /// <summary>
        /// Blocks until the worker has finished its current iteration and stopped
        /// touching the device. Returns false if it did not stop within the timeout,
        /// in which case the request is withdrawn and the worker keeps running.
        /// Not safe for concurrent callers — only the main thread calls this.
        /// </summary>
        public bool Pause(TimeSpan timeout)
        {
            // A thread that has exited cannot touch the device, which is exactly the
            // state Pause is trying to reach.
            if (_thread == null || !_thread.IsAlive)
                return true;

            lock (_pauseGate)
            {
                _pauseRequested = true;
                Monitor.PulseAll(_pauseGate);

                var sw = Stopwatch.StartNew();
                while (!_paused)
                {
                    var remaining = timeout - sw.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                    {
                        _pauseRequested = false;
                        Monitor.PulseAll(_pauseGate);
                        return false;
                    }

                    Monitor.Wait(_pauseGate, remaining);
                }

                return true;
            }
        }

        /// <summary>Releases a paused worker. Safe to call even if Pause returned false.</summary>
        public void Resume()
        {
            lock (_pauseGate)
            {
                _pauseRequested = false;
                Monitor.PulseAll(_pauseGate);
            }
        }

        private void Run()
        {
            var token = _cts.Token;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    lock (_pauseGate)
                    {
                        if (_pauseRequested)
                        {
                            _paused = true;
                            Monitor.PulseAll(_pauseGate);

                            while (_pauseRequested && !token.IsCancellationRequested)
                                Monitor.Wait(_pauseGate, 100);

                            _paused = false;
                            Monitor.PulseAll(_pauseGate);
                        }
                    }

                    if (token.IsCancellationRequested)
                        return;

                    if (!_connected)
                    {
                        bool reconnected;
                        lock (ReconnectGate)
                        {
                            reconnected = _reader.Reconnect(_claimedPaths());
                        }

                        if (reconnected)
                        {
                            _connected = true;
                            _backoffStep = 0;
                            ConsoleLog.Line($"{Tag} reconnected.");
                        }
                        else
                        {
                            int step = Math.Min(_backoffStep, BackoffMs.Length - 1);
                            if (_backoffStep < BackoffMs.Length) _backoffStep++;
                            _cts.Token.WaitHandle.WaitOne(BackoffMs[step]);
                        }

                        continue;
                    }

                    var result = _reader.Read();

                    if (result == ReadResult.Disconnected)
                    {
                        _connected = false;
                        _backoffStep = 0;
                        ConsoleLog.Warn($"{Tag} disconnected. Reconnecting...");
                        ReleaseAllInputs();
                        continue;
                    }

                    if (result == ReadResult.BadPacket)
                        continue;

                    var snapshot = _snapshot();

                    var calibration = snapshot.Calibration;
                    if (calibration != null && calibration.IsValid())
                    {
                        var (nx, ny) = calibration.MapNormalized(_reader.State.RAW_X, _reader.State.RAW_Y);

                        _reader.State.ABS_X = (short)Math.Round(nx * 32767.0);
                        _reader.State.ABS_Y = (short)Math.Round(ny * 32767.0);
                    }

                    try
                    {
                        _mouse.Feed(snapshot.Mapping);
                        _keyboard.Feed(snapshot.Mapping);
                    }
                    catch (Exception ex)
                    {
                        ConsoleLog.Warn($"{Tag} feed failed: {ex.Message}");
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ConsoleLog.Error($"{Tag} worker stopped: {ex.Message}");
                    return;
                }
            }
        }

        /// <summary>
        /// Clears every held button on the virtual devices. Without this, a gun
        /// unplugged with the trigger down leaves the key held until the driver's
        /// own timeout expires.
        /// </summary>
        private void ReleaseAllInputs()
        {
            foreach (GunButton b in Enum.GetValues<GunButton>())
                _reader.State.BtnState[b] = false;

            try { _keyboard.Feed(GunMapping.Empty); }
            catch (Exception ex) { ConsoleLog.Warn($"{Tag} keyboard release failed: {ex.Message}"); }

            try { _mouse.Feed(GunMapping.Empty); }
            catch (Exception ex) { ConsoleLog.Warn($"{Tag} mouse release failed: {ex.Message}"); }
        }

        public void Dispose()
        {
            _cts.Dispose();
        }
    }
}
