// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Guncon3.Core;
using Guncon3Console.TetherScript;

namespace Guncon3Console
{
    /// <summary>Owns one gun end to end on its own thread: read, decode, map, feed. The blocking USB read is
    /// the clock — there is deliberately no sleep on the success path.</summary>
    internal sealed class GunWorker : IDisposable
    {
        private static readonly int[] BackoffMs = { 250, 500, 1000, 2000 };

        // One transient pipe error is not a disconnect. Require a few in a row before tearing the device down.
        private const int DisconnectsBeforeTeardown = 3;

        private int _consecutiveDisconnects;

        /// <summary>How long a gun may produce no usable frame before it is treated as disconnected. Must stay
        /// well above <c>GunconReader.PipeTimeoutMs</c> — by a factor of ten or better — or raising that
        /// per-transfer timeout silently turns this window into a one-timeout hair trigger.</summary>
        private static readonly long NoDataBeforeTeardownTicks = Stopwatch.Frequency * 2;

        private static readonly double NoDataBeforeTeardownSeconds = NoDataBeforeTeardownTicks / (double)Stopwatch.Frequency;

        private long _lastGoodReadTimestamp;

        // Reconnect must be serialized across all workers: the claimed-path set is a snapshot, and WinUSB opens
        // are shared rather than exclusive, so two workers evaluating it concurrently can both pick the same
        // free device.
        private static readonly object ReconnectGate = new object();

        /// <summary>The virtual absolute mouse spans the whole virtual desktop, so a screen-relative aim has to
        /// be offset and scaled onto the calibrated monitor. If the TetherScript driver turns out to map its
        /// range onto the primary screen only, set this to false and the aim is screen-relative
        /// again.</summary>
        private const bool MapToVirtualDesktop = true;

        private readonly int _index;
        private readonly string _tag;
        private readonly GunconReader _reader;
        private readonly AbsMouseFeeder _mouse;
        private readonly KeyboardFeeder _keyboard;
        private readonly JoystickFeeder _joystick;
        private readonly Func<GunSnapshot> _snapshot;
        private readonly Func<IReadOnlySet<string>> _claimedPaths;
        private readonly Action _statusChanged;

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly object _pauseGate = new object();
        private bool _pauseRequested;
        private bool _paused;

        /// <summary>
        /// True while a host wants Test-tab frames. Set by <see cref="App.WatchFrames"/>
        /// on the main thread and read once per frame here, so the cost of the tab
        /// being closed is one volatile bool read per read.
        /// </summary>
        public volatile bool PublishFrames;

        private GunFrame _latestFrame;

        private bool _connected = true;
        private int _backoffStep;
        private string _lastLoggedReconnectError;

        private Thread _thread;

        public GunWorker(int index, GunconReader reader, AbsMouseFeeder mouse, KeyboardFeeder keyboard, JoystickFeeder joystick, Func<GunSnapshot> snapshot, Func<IReadOnlySet<string>> claimedPaths, Action statusChanged)
        {
            _index = index;
            _tag = $"[Gun {index + 1}]";
            _reader = reader;
            _mouse = mouse;
            _keyboard = keyboard;
            _joystick = joystick;
            _snapshot = snapshot;
            _claimedPaths = claimedPaths;
            _statusChanged = statusChanged;

            // Centres change once (after measurement) or never; assigning them per frame was the only per-frame
            // write in the feed path that did nothing.
            _joystick.Centres = _reader.Centres;
        }

        /// <summary>True while the worker believes its device is open. Read from any thread.</summary>
        public bool IsConnected => Volatile.Read(ref _connected);

        /// <summary>The last frame published, or null while nothing has been. Read from any thread; stays
        /// readable after <see cref="PublishFrames"/> goes false, so leaving the Test tab freezes the picture
        /// rather than clearing it.</summary>
        public GunFrame LatestFrame => Volatile.Read(ref _latestFrame);

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
        /// touching the device. Returns false if it did not stop within the timeout, in
        /// which case the request is withdrawn and the worker keeps running. Not safe
        /// for concurrent callers — only the main thread calls this.
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

            try
            {
                while (!token.IsCancellationRequested)
                {
                    WaitWhilePaused(token);
                    if (token.IsCancellationRequested) return;
                    if (!_connected && !TryReconnect(token)) continue;

                    var result = _reader.Read();
                    if (!HandleRead(result)) continue;   // false: not an Ok frame

                    MeasureCentresIfDue();
                    MapAndFeed();
                }
            }
            catch (Exception ex)
            {
                // A trigger mapped to a mouse button must not stay held for the rest of the process because
                // this thread died.
                Log.Error($"{_tag} worker stopped: {ex.Message}");
                ReleaseAllInputs();
            }
        }

        /// <summary>Blocks while the main thread has asked for a pause (calibration window open).</summary>
        private void WaitWhilePaused(CancellationToken token)
        {
            lock (_pauseGate)
            {
                if (!_pauseRequested) return;

                _paused = true;
                Monitor.PulseAll(_pauseGate);

                while (_pauseRequested && !token.IsCancellationRequested)
                    Monitor.Wait(_pauseGate, 100);

                _paused = false;
                Monitor.PulseAll(_pauseGate);

                // The pipe sat idle through the whole pause, so the clock must restart here — otherwise the
                // very next non-Ok read finds an ancient timestamp and fires the no-data teardown over a gun
                // that was answering normally.
                _lastGoodReadTimestamp = Stopwatch.GetTimestamp();
            }
        }

        /// <summary>One reconnect attempt, then the backoff. The backoff waits on the pause gate, so Pause()
        /// and RequestStop() — which pulse it — wake this immediately instead of after up to two
        /// seconds.</summary>
        private bool TryReconnect(CancellationToken token)
        {
            bool reconnected;
            lock (ReconnectGate)
            {
                reconnected = _reader.Reconnect(_claimedPaths());
            }

            if (reconnected)
            {
                Volatile.Write(ref _connected, true);
                _backoffStep = 0;
                _consecutiveDisconnects = 0;
                _lastLoggedReconnectError = null;
                _lastGoodReadTimestamp = Stopwatch.GetTimestamp();
                Log.Line($"{_tag} reconnected.");

                if (!_reader.PipeTimeoutApplied)
                    Log.Warn($"{_tag} refused the USB transfer timeout on reconnect; a wedged device can block indefinitely.");

                _statusChanged?.Invoke();
                return true;
            }

            var error = _reader.LastReconnectError;
            if (error != null && error != _lastLoggedReconnectError)
            {
                Log.Warn($"{_tag} reconnect failed: {error}. Retrying...");
                _lastLoggedReconnectError = error;
            }

            int step = Math.Min(_backoffStep, BackoffMs.Length - 1);
            if (_backoffStep < BackoffMs.Length) _backoffStep++;

            lock (_pauseGate)
            {
                if (!_pauseRequested && !token.IsCancellationRequested)
                    Monitor.Wait(_pauseGate, BackoffMs[step]);
            }

            return false;
        }

        /// <summary>Classifies one read. Returns true only for an Ok frame that should be mapped and fed.</summary>
        private bool HandleRead(ReadResult result)
        {
            if (result == ReadResult.Disconnected)
            {
                if (++_consecutiveDisconnects < DisconnectsBeforeTeardown)
                    return false;

                EnterDisconnected($"disconnected (win32 {_reader.LastWin32Error})");
                return false;
            }

            _consecutiveDisconnects = 0;

            if (result == ReadResult.Ok)
            {
                _lastGoodReadTimestamp = Stopwatch.GetTimestamp();
                return true;
            }

            if (Stopwatch.GetTimestamp() - _lastGoodReadTimestamp >= NoDataBeforeTeardownTicks)
            {
                // Neither a real disconnect nor a single slow transfer: the gun has produced nothing usable for
                // the whole window. Re-enumerating is the only recovery left.
                EnterDisconnected($"no usable data for {NoDataBeforeTeardownSeconds:0.#} seconds");
            }

            return false;   // BadPacket
        }

        /// <summary>The one teardown: mark disconnected, say why, release what the gun was holding.</summary>
        private void EnterDisconnected(string reason)
        {
            _consecutiveDisconnects = 0;
            Volatile.Write(ref _connected, false);
            _backoffStep = 0;
            _lastGoodReadTimestamp = Stopwatch.GetTimestamp();
            Log.Warn($"{_tag} {reason}. Reconnecting...");
            ReleaseAllInputs();
            _statusChanged?.Invoke();
        }

        private void MeasureCentresIfDue()
        {
            if (!_reader.TakeMeasuredCentres(out var measured)) return;

            _joystick.Centres = measured;
            try
            {
                measured.Save(gunIndex: _index);
                Log.Line($"{_tag} stick centres measured: HatX={measured.HatX} HatY={measured.HatY} RX={measured.RX} RY={measured.RY} (saved)");
            }
            catch (Exception ex)
            {
                Log.Warn($"{_tag} could not save stick centres: {ex.Message}");
            }
        }

        private void MapAndFeed()
        {
            var snapshot = _snapshot();
            var state = _reader.State;

            // No calibration: no position. A cursor that jumps over the raw 16-bit gun
            // value is worse than one that stays put; the buttons still work.
            var calibration = snapshot.Calibration;
            bool hasPosition = calibration != null && calibration.Rect.IsValid();
            ushort x = 0, y = 0;

            if (hasPosition && state.IsInsideScreen)
            {
                var (nx, ny) = calibration.MapNormalized(state.ABS_X, state.ABS_Y, snapshot.Mode);

                if (MapToVirtualDesktop)
                    (nx, ny) = calibration.Screen.ToDesktop(nx, ny, snapshot.Desktop);

                x = (ushort)(short)Math.Round(nx * 32767.0);
                y = (ushort)(short)Math.Round(ny * 32767.0);
            }
            // Off-screen with a calibration: (0, 0). Whether a game wants that is a
            // separate, unsettled question.

            try
            {
                _mouse.Feed(snapshot.Mapping, x, y, hasPosition);
                _keyboard.Feed(snapshot.Mapping);
                _joystick.Feed(snapshot.Mapping);
            }
            catch (Exception ex)
            {
                Log.Warn($"{_tag} feed failed: {ex.Message}");
            }

            // After the feeds, whether they all went out or one of them threw: the frame says what the feeders
            // decided the devices should hold. One reference write, and only while somebody is looking.
            if (PublishFrames)
                Volatile.Write(ref _latestFrame, BuildFrame(snapshot, state));
        }

        /// <summary>Copies one frame out for the Test tab. Called on the worker thread, which is the only
        /// thread allowed to read the feeders' <c>Last…</c> members.</summary>
        private GunFrame BuildFrame(GunSnapshot snapshot, GunState state)
        {
            var buttons = new bool[GunButtons.Count];
            state.Buttons.CopyTo(buttons, 0);

            var calibration = snapshot.Calibration;
            (double X, double Y)? rect = calibration != null
                ? calibration.Rect.MapNormalized(state.ABS_X, state.ABS_Y)
                : null;
            (double X, double Y)? homography = calibration?.Homography?.MapNormalized(state.ABS_X, state.ABS_Y);

            var keys = _keyboard.LastKeys;
            var keyCopy = keys.IsEmpty ? Array.Empty<byte>() : keys.ToArray();

            return new GunFrame(
                Stopwatch.GetTimestamp(),
                state.ABS_X, state.ABS_Y, state.IsInsideScreen,
                buttons,
                state.ABS_HAT0X, state.ABS_HAT0Y, state.ABS_RX, state.ABS_RY, state.Z,
                snapshot.Mode,
                calibration != null,
                Normalize(state.ABS_X, state.ABS_Y),
                rect,
                homography,
                _mouse.LastOutput,
                new KeyboardOutput(0, keyCopy),
                new JoystickOutput(_joystick.LastState),
                _mouse.Healthy, _keyboard.Healthy, _joystick.Healthy);
        }

        /// <summary>Places the signed 16-bit raw axes on 0..1. Not a calibration: it exists so the Test tab has
        /// somewhere to draw the grey crosshair on a gun with none at all.</summary>
        private static (double X, double Y) Normalize(short rawX, short rawY)
            => (Clamp01(rawX / 65535.0 + 0.5), Clamp01(rawY / 65535.0 + 0.5));

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>Clears every held button on the virtual devices. Without this, a gun unplugged with the
        /// trigger down leaves the key held until the driver's own timeout expires.</summary>
        private void ReleaseAllInputs()
        {
            Array.Clear(_reader.State.Buttons);

            try { _keyboard.Feed(GunMapping.Empty); }
            catch (Exception ex) { Log.Warn($"{_tag} keyboard release failed: {ex.Message}"); }

            try { _mouse.Feed(GunMapping.Empty, 0, 0, hasPosition: false); }
            catch (Exception ex) { Log.Warn($"{_tag} mouse release failed: {ex.Message}"); }

            try { _joystick.Release(); }
            catch (Exception ex) { Log.Warn($"{_tag} joystick release failed: {ex.Message}"); }
        }

        /// <summary>Stops and joins the thread (bounded) before disposing the token source it waits on.</summary>
        public void Dispose()
        {
            RequestStop();
            Join(TimeSpan.FromSeconds(1));
            _cts.Dispose();
        }
    }
}
