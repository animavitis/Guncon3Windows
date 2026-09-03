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

        // One transient pipe error is not a disconnect. Require a few in a row before
        // tearing the device down, so an occasional stall no longer costs a full
        // release-and-reconnect cycle.
        private const int DisconnectsBeforeTeardown = 3;

        private int _consecutiveDisconnects;

        /// <summary>
        /// How long a gun may produce no usable frame before it is treated as
        /// disconnected. Covers both a wedged device timing out on every transfer and
        /// a sustained checksum-failure storm: in either case the gun is producing
        /// nothing, and re-enumerating it is the only recovery available. Must stay
        /// well above <c>GunconReader.PipeTimeoutMs</c> — by a factor of ten or
        /// better — or raising that per-transfer timeout silently turns this window
        /// into a one-timeout hair trigger.
        /// </summary>
        private static readonly long NoDataBeforeTeardownTicks = Stopwatch.Frequency * 2;

        private static readonly double NoDataBeforeTeardownSeconds = NoDataBeforeTeardownTicks / (double)Stopwatch.Frequency;

        private long _lastGoodReadTimestamp;

        // Reconnect must be serialized across all workers: the claimed-path set is a
        // snapshot, and WinUSB opens are shared rather than exclusive, so two workers
        // evaluating it concurrently can both pick the same free device and then
        // corrupt each other's reads on one pipe.
        private static readonly object ReconnectGate = new object();

        private readonly int _index;
        private readonly GunconReader _reader;
        private readonly AbsMouseFeeder _mouse;
        private readonly KeyboardFeeder _keyboard;
        private readonly JoystickFeeder _joystick;
        private readonly Func<GunSnapshot> _snapshot;
        private readonly Func<IReadOnlyCollection<string>> _claimedPaths;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _pauseGate = new object();
        private bool _pauseRequested;
        private bool _paused;

        private bool _connected = true;
        private int _backoffStep;

        private Thread _thread;

        public GunWorker(int index, GunconReader reader, AbsMouseFeeder mouse, KeyboardFeeder keyboard, JoystickFeeder joystick, Func<GunSnapshot> snapshot, Func<IReadOnlyCollection<string>> claimedPaths)
        {
            _index = index;
            _reader = reader;
            _mouse = mouse;
            _keyboard = keyboard;
            _joystick = joystick;
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
            _lastGoodReadTimestamp = Stopwatch.GetTimestamp();

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

                            // The pipe sat idle through the whole pause — possibly tens of
                            // seconds of a calibration modal — so the clock must restart
                            // here, before control reaches Read(). Otherwise the very next
                            // non-Ok read finds an ancient timestamp and fires the no-data
                            // teardown over a gun that was answering normally moments ago.
                            _lastGoodReadTimestamp = Stopwatch.GetTimestamp();
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
                            _consecutiveDisconnects = 0;
                            _lastGoodReadTimestamp = Stopwatch.GetTimestamp();
                            ConsoleLog.Line($"{Tag} reconnected.");

                            if (!_reader.PipeTimeoutApplied)
                                ConsoleLog.Warn($"{Tag} refused the USB transfer timeout on reconnect; a wedged device can block indefinitely.");
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
                        if (++_consecutiveDisconnects < DisconnectsBeforeTeardown)
                            continue;

                        _consecutiveDisconnects = 0;
                        _connected = false;
                        _backoffStep = 0;
                        ConsoleLog.Warn($"{Tag} disconnected (win32 {_reader.LastWin32Error}). Reconnecting...");
                        ReleaseAllInputs();
                        continue;
                    }

                    _consecutiveDisconnects = 0;

                    if (result == ReadResult.Ok)
                    {
                        _lastGoodReadTimestamp = Stopwatch.GetTimestamp();
                    }
                    else if (Stopwatch.GetTimestamp() - _lastGoodReadTimestamp >= NoDataBeforeTeardownTicks)
                    {
                        // Neither a real disconnect nor a single slow transfer: the gun
                        // has produced nothing usable for the whole window, whether from
                        // a wedge timing out on every transfer or a sustained checksum
                        // failure storm. Re-enumerating is the only recovery left.
                        ConsoleLog.Warn($"{Tag} no usable data for {NoDataBeforeTeardownSeconds:0.#} seconds. Reconnecting...");
                        _lastGoodReadTimestamp = Stopwatch.GetTimestamp();
                        _connected = false;
                        _backoffStep = 0;
                        ReleaseAllInputs();
                        continue;
                    }

                    if (result == ReadResult.BadPacket)
                        continue;

                    if (_reader.TakeMeasuredCentres(out var measured))
                    {
                        try
                        {
                            measured.Save(gunIndex: _index);
                            ConsoleLog.Line($"{Tag} stick centres measured: HatX={measured.HatX} HatY={measured.HatY} RX={measured.RX} RY={measured.RY} (saved)");
                        }
                        catch (Exception ex)
                        {
                            ConsoleLog.Warn($"{Tag} could not save stick centres: {ex.Message}");
                        }
                    }

                    var snapshot = _snapshot();

                    var calibration = snapshot.Calibration;
                    if (calibration != null && calibration.Rect.IsValid())
                    {
                        var (nx, ny) = calibration.MapNormalized(
                            _reader.State.RAW_X, _reader.State.RAW_Y, snapshot.Mode);

                        _reader.State.ABS_X = (short)Math.Round(nx * 32767.0);
                        _reader.State.ABS_Y = (short)Math.Round(ny * 32767.0);
                    }

                    try
                    {
                        _mouse.Feed(snapshot.Mapping);
                        _keyboard.Feed(snapshot.Mapping);
                        _joystick.Centres = _reader.Centres;
                        _joystick.Feed(snapshot.Mapping);
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

            try { _joystick.Release(); }
            catch (Exception ex) { ConsoleLog.Warn($"{Tag} joystick release failed: {ex.Message}"); }
        }

        public void Dispose()
        {
            _cts.Dispose();
        }
    }
}
