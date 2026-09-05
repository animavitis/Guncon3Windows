// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;
using Guncon3.Core;

namespace Guncon3Console
{
    /// <summary>
    /// Full-screen calibration for one gun. The state lives in
    /// <see cref="CalibrationSession"/>, the picture in <see cref="CalibrationRenderer"/>,
    /// the key and button meanings in <see cref="CalibrationInput"/>. This form reads the
    /// gun on its own thread, turns edges into actions, and writes the file when the user
    /// accepts. Nothing is saved until the check phase is accepted, so cancelling at any
    /// point leaves the previous calibration exactly as it was.
    /// </summary>
    public class CalibrationWindow : Form
    {
        // Dictionary enumeration order is incidental in .NET; nothing here relies on the
        // order these buttons are checked in, only on each being checked once.
        private static readonly GunButton[] WatchedButtons = CalibrationInput.ButtonActions.Keys.ToArray();

        private readonly WinFormsTimer _poll;
        private readonly GunconReader _reader;
        private readonly int _gunIndex;
        private readonly CalibrationSession _session;
        private readonly Screen[] _screens;
        private readonly CalibrationRenderer _renderer;

        // The read thread publishes here; the UI thread consumes the latest.
        private readonly CancellationTokenSource _readCts = new();
        private readonly Thread _readThread;
        private GunSample _latest = GunSample.None;

        private GunSample _previous = GunSample.None;
        private bool _primed;
        private bool _showRaw;
        private FrameState _lastPainted;
        private bool _disposed;

        /// <summary>True once a calibration was written. False on cancel.</summary>
        public bool Saved { get; private set; }

        /// <summary>
        /// True once the read thread has actually stopped (joined within Dispose's
        /// timeout). False means it may still be reading the gun's USB pipe, which the
        /// caller must then not hand to anyone else.
        /// </summary>
        public bool ReadThreadStopped { get; private set; } = true;

        /// <summary>The mapping the user was looking at when they accepted.</summary>
        public CalibrationMode ChosenMode => _session.Mode;

        public CalibrationWindow(GunconReader reader, int gunIndex = 0,
                                 CalibrationMode mode = CalibrationMode.Rect, int initialScreen = 0)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _gunIndex = gunIndex;

            _screens = Screen.AllScreens;
            if (_screens.Length == 0) _screens = new[] { Screen.PrimaryScreen };

            var placements = new ScreenPlacement[_screens.Length];
            for (int i = 0; i < _screens.Length; i++)
                placements[i] = Screens.ToPlacement(_screens[i].Bounds);

            _session = new CalibrationSession(placements, initialScreen, mode);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = System.Drawing.Color.FromArgb(40, 40, 40);
            ForeColor = System.Drawing.Color.White;
            ApplyScreenBounds();

            _renderer = new CalibrationRenderer(ClientSize);

            Shown += (_, __) => { try { Cursor.Hide(); } catch (InvalidOperationException) { } Activate(); };
            FormClosed += (_, __) => { try { Cursor.Show(); } catch (InvalidOperationException) { } };

            KeyDown += OnKeyDown;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            UpdateStyles();

            _readThread = new Thread(ReadLoop) { IsBackground = true, Name = $"calibration-read-{gunIndex + 1}" };
            _readThread.Start();

            _poll = new WinFormsTimer { Interval = 16 };
            _poll.Tick += PollTick;
            _poll.Start();
        }

        // ----------------------------------------------------------- lifecycle

        /// <summary>
        /// Stops the read thread and the poll timer on every exit path. The timer is
        /// rooted by its own native window, so it would keep ticking after Close()
        /// otherwise.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _poll.Stop();
            _readCts.Cancel();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            // WinForms disposes a closed form itself; the caller's own `using` then
            // disposes it again. Without this guard the second pass calls
            // _readCts.Cancel() on a CancellationTokenSource already disposed.
            if (disposing && !_disposed)
            {
                _disposed = true;

                _poll.Stop();
                _readCts.Cancel();
                // Bounded by GunconReader's transfer timeout (100 ms) when the driver
                // honoured it; a refused timeout policy can hold the thread until the
                // gun answers, which is why this join has a ceiling too.
                bool joined = _readThread.Join(TimeSpan.FromMilliseconds(500));
                _poll.Dispose();
                ReadThreadStopped = joined;
                // An abandoned thread (join timed out) may still be sitting in
                // token.WaitHandle.WaitOne; disposing under it would throw. Leak the CTS
                // in that case instead — ReadLoop's own catch-all keeps that from ever
                // escaping.
                if (joined)
                {
                    _readCts.Dispose();
                }
                else
                {
                    Log.Warn($"[Gun {_gunIndex + 1} Calibration] the gun read thread did not stop within 500 ms; "
                                   + "the gun is left idle — restart the program to use it again.");
                }
                _renderer.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            _renderer?.Resize(ClientSize);
        }

        /// <summary>
        /// Under PerMonitorV2, moving a borderless form to a monitor with another DPI
        /// makes WinForms apply a suggested rectangle scaled by the DPI ratio, so
        /// targets land off-screen. Refuse the suggestion and re-assert the monitor's
        /// physical bounds once the message has been processed.
        /// </summary>
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            e.Cancel = true;
            BeginInvoke(ApplyScreenBounds);
        }

        private void ApplyScreenBounds()
        {
            Bounds = _screens[_session.ScreenIndex].Bounds;
        }

        // ----------------------------------------------------------- read thread

        /// <summary>
        /// Loops on the blocking USB read and publishes each outcome; never touches the
        /// form. A Disconnected result is published too, so the UI can say so.
        /// </summary>
        private void ReadLoop()
        {
            var token = _readCts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    ReadResult result;
                    try
                    {
                        result = _reader.Read();
                    }
                    catch (Exception ex)
                    {
                        DumpDiagnostics("cal_read_error.txt", ex, includePoints: false);
                        result = ReadResult.Disconnected;
                    }

                    if (result == ReadResult.BadPacket) continue;   // keep the last good sample

                    Volatile.Write(ref _latest, GunSample.From(_reader.State, result));

                    if (result == ReadResult.Disconnected)
                        token.WaitHandle.WaitOne(250);   // no point hammering a gone device
                }
            }
            catch (Exception ex)
            {
                // Nothing this thread does may ever reach the default handler — that
                // takes the whole process down. This also covers an abandoned thread
                // (Dispose's join timed out) finding the CTS disposed out from under it:
                // log and exit instead of crashing.
                Log.Warn($"[Gun {_gunIndex + 1} Calibration] read thread stopped: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- input

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.D)
            {
                _showRaw = !_showRaw;
                Invalidate();
                return;
            }

            if (CalibrationInput.KeyActions.TryGetValue(e.KeyCode, out var action))
                Perform(action);
        }

        private void PollTick(object sender, EventArgs e)
        {
            var sample = Volatile.Read(ref _latest);

            // The read thread has not published anything yet; priming edges from
            // GunSample.None (all buttons false) would make a trigger already held at
            // F12 look like a fresh press once the real sample arrives.
            if (sample == GunSample.None) return;

            // The first tick only records what is held, so a trigger still down from
            // the F12 press or a game does not shoot the first target by itself.
            if (_primed && sample != _previous)
            {
                foreach (var b in WatchedButtons)
                {
                    bool pressed = sample.Buttons[(int)b] && !_previous.Buttons[(int)b];
                    if (!pressed) continue;

                    Perform(CalibrationInput.ButtonActions[b]);
                    // Accept or Cancel may have just closed the form; a later button in
                    // this same tick's loop must not go on to act on a closed form.
                    if (IsDisposed || !Visible) return;
                }
            }
            _previous = sample;
            _primed = true;

            var frame = CurrentFrame();
            if (frame.Phase == CalibrationPhase.Capturing || frame != _lastPainted)
                Invalidate();
        }

        /// <summary>
        /// Applies one action to the session and does the two things the session
        /// cannot: save and close.
        /// </summary>
        private void Perform(CalibrationAction action)
        {
            var sample = Volatile.Read(ref _latest);

            // A gun that is gone, or has not been read yet at all, cannot shoot;
            // everything else (restart, cancel, screen change, accept of an
            // already-captured candidate) still works regardless of the sample.
            if (action == CalibrationAction.Shoot && (sample == GunSample.None || sample.Result == ReadResult.Disconnected))
                return;

            bool valid;
            try
            {
                valid = _session.Apply(action, sample.RawX, sample.RawY);
            }
            catch (Exception ex)
            {
                DumpDiagnostics("cal_capture_error.txt", ex);
                return;
            }

            if (!valid) return;

            switch (action)
            {
                case CalibrationAction.Cancel:
                    Close();
                    return;
                case CalibrationAction.Accept:
                    AcceptAndSave();
                    return;
                case CalibrationAction.PreviousScreen:
                case CalibrationAction.NextScreen:
                    ApplyScreenBounds();
                    break;
            }

            Invalidate();
        }

        private void AcceptAndSave()
        {
            var file = _session.Accept();
            if (file == null) return;

            _poll.Stop();
            try
            {
                file.Save(gunIndex: _gunIndex);
                Saved = true;
                Close();
            }
            catch (Exception ex)
            {
                DumpDiagnostics("cal_save_error.txt", ex);
                try { Cursor.Show(); } catch (InvalidOperationException) { }
                MessageBox.Show(this, "Calibration could not be saved: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                try { Cursor.Hide(); } catch (InvalidOperationException) { }
                _poll.Start();
            }
        }

        // ------------------------------------------------------------- drawing

        private FrameState CurrentFrame()
        {
            var s = Volatile.Read(ref _latest);
            return new FrameState(
                _session.Phase, _session.NextTarget, _session.CapturedPoints.Count, _session.Candidate,
                _session.Mode, _session.ScreenIndex, _session.ScreenCount, _session.Screen,
                s.RawX, s.RawY, s.InsideScreen, s.Buttons[(int)GunButton.Trigger],
                _session.LastCaptureRejected, _showRaw, _gunIndex,
                Disconnected: s.Result == ReadResult.Disconnected);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _lastPainted = CurrentFrame();
            _renderer.Paint(e.Graphics, in _lastPainted);
        }

        // ------------------------------------------------------------ diagnostics

        /// <summary>
        /// Writes the exception, and (except from the read thread) the points captured
        /// so far, next to the exe. <see cref="CalibrationSession.CapturedPoints"/> is a
        /// live view over a list the UI thread mutates on every shot, so the read
        /// thread's call site passes <paramref name="includePoints"/>: false to avoid
        /// racing it.
        /// </summary>
        private void DumpDiagnostics(string file, Exception ex, bool includePoints = true)
        {
            string path = Path.Combine(AppInfo.BaseDirectory, file);
            try
            {
                using var sw = new StreamWriter(path, false);
                sw.WriteLine("# Calibration error: " + ex);
                if (includePoints)
                {
                    sw.WriteLine("# captured raw points:");
                    foreach (var p in _session.CapturedPoints)
                        sw.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{p.X},{p.Y}"));
                }
                Log.Warn($"[Gun {_gunIndex + 1} Calibration] diagnostics written to {path}");
            }
            catch (Exception dumpEx) when (dumpEx is IOException or UnauthorizedAccessException or SecurityException)
            {
                Log.Warn($"[Gun {_gunIndex + 1} Calibration] could not write {path}: {dumpEx.Message}");
            }
        }
    }
}
