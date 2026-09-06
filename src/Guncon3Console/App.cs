// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Guncon3.Core;
using Guncon3Console.Output;
using Nefarius.Drivers.WinUSB;

namespace Guncon3Console
{
    /// <summary>
    /// The engine: every gun, its worker, the calibration mode and the mappings. It
    /// has no console, no message loop and no key handling — a host (ConsoleHost or
    /// GuiHost) owns those and calls the methods here.
    ///
    /// Every public method must be called on the main STA thread and none of them
    /// takes a lock, so a host must not call one while another is running — which
    /// includes <see cref="Rescan"/>, since it mutates the slot list and can open a
    /// calibration window. The exceptions are <see cref="Status"/>,
    /// <see cref="Mode"/>, <see cref="HasGuns"/>, <see cref="WatchFrames"/> and
    /// <see cref="LatestFrame"/>, which change no engine state and are safe to call
    /// from the window's timers even while a calibration dialog is up.
    /// Workers only read the snapshots each <see cref="GunInstance"/>
    /// publishes, plus the slot list and other guns' <see cref="GunconReader.DevicePath"/>
    /// through the claimed-paths closure passed at construction — safe because the
    /// list is only ever mutated while it is empty (<see cref="Start"/>, and
    /// <see cref="Rescan"/>, which refuses to run once <see cref="HasGuns"/>), so no
    /// worker exists yet when it grows, and a worker's own reconnect attempts are
    /// serialised against every other worker's by GunWorker's ReconnectGate.
    /// <see cref="Status"/> reads DevicePath the same way but with no gate at all,
    /// since it runs on the UI thread rather than a worker; a torn or stale value
    /// during a reconnect is acceptable there, being display-only. The one exception
    /// to the main-thread rule is <see cref="Guncon3Console.Ui.MainForm.OnSessionEnding"/>'s
    /// catch fallback, which calls <see cref="Shutdown"/> from the SystemEvents thread — but
    /// only once <c>Invoke</c> itself has failed, i.e. the form is already gone.
    /// </summary>
    internal sealed class App
    {
        private readonly List<GunSlot> _slots = new();
        private CalibrationMode _mode = CalibrationMode.Rect;
        private int _zThreshold = DepthDigitizer.DefaultThreshold;
        private volatile bool _shutdown;

        /// <summary>Which of a calibration's two mappings every gun is aiming through.</summary>
        public CalibrationMode Mode => _mode;

        /// <summary>
        /// Where every gun's depth reading splits into <see cref="GunButton.ZLow"/> and
        /// <see cref="GunButton.ZHigh"/>. Set from settings.txt at startup and again whenever the settings
        /// dialog is accepted; kept here as well as on the readers so that a gun connecting later gets it too.
        /// Changes no engine state and takes no locks, so it needs no busy guard.
        /// </summary>
        public int ZThreshold
        {
            get => _zThreshold;
            set
            {
                _zThreshold = DepthDigitizer.Clamp(value);

                foreach (var slot in _slots)
                    slot.Gun.Reader.ZThreshold = _zThreshold;
            }
        }

        /// <summary>
        /// The window the calibration dialog is modal to; the console host leaves it
        /// null and ShowDialog runs the same modal loop Application.Run did.
        /// </summary>
        public IWin32Window CalibrationOwner { get; set; }

        /// <summary>
        /// The window whose monitor the calibration opens on when there is no owner;
        /// <see cref="IntPtr.Zero"/> means the primary monitor.
        /// </summary>
        public IntPtr CalibrationScreenWindow { get; set; }

        /// <summary>
        /// Raised after every action here, and by a worker on a connect/disconnect or
        /// feeder-health transition — a GUI handler must marshal to its own thread.
        /// </summary>
        public event Action StatusChanged;

        // ------------------------------------------------------------- startup

        /// <summary>
        /// True once at least one gun is connected. The GUI's degraded state is exactly
        /// this being false.
        /// </summary>
        public bool HasGuns => _slots.Count > 0;

        /// <summary>
        /// Enumerate, connect, calibrate, feeders, mappings, workers. False when nothing
        /// can run, in which case <see cref="Log.Fatal"/> has already said why and set
        /// the exit code.
        /// </summary>
        public bool Start() => StartCore((msg, ex) => Log.Fatal(msg, ex));

        /// <summary>
        /// Runs the same sequence again once the user has plugged a gun in. Reports
        /// through <see cref="Log.Warn"/> rather than <see cref="Log.Fatal"/>: by now
        /// the GUI's message loop is running, where Fatal shows a modal box and exits.
        /// </summary>
        public bool Rescan()
        {
            if (_shutdown || HasGuns) return false;

            return StartCore((msg, ex) => Log.Warn(msg + (ex == null ? string.Empty : ": " + ex.Message)));
        }

        /// <summary>
        /// The one start sequence, with the two hosts' two failure styles passed in as
        /// <paramref name="fail"/>. Every failure path leaves <see cref="_slots"/> empty,
        /// because ConnectGun adds a slot only after a successful connect; a run where
        /// some guns connected and others did not is a success and continues.
        /// </summary>
        private bool StartCore(Action<string, Exception> fail)
        {
            List<USBDeviceInfo> devices;
            try
            {
                devices = GunconReader.FindAllDevices();
            }
            catch (Exception ex)
            {
                fail("Could not enumerate USB devices", ex);
                return false;
            }

            if (devices.Count == 0)
            {
                fail("No Guncon3 device found.", null);
                return false;
            }

            Log.Line($"Found {devices.Count} Guncon3 device(s).");

            for (int i = 0; i < devices.Count; i++)
                ConnectGun(i, devices[i]);

            if (_slots.Count == 0)
            {
                fail("No Guncon3 could be connected.", null);
                return false;
            }

            CalibrationMode? chosen = null;
            foreach (var slot in _slots)
            {
                if (LoadCalibration(slot))
                {
                    Log.Line($"[Gun {slot.Index + 1}] Calibration loaded.");
                    continue;
                }

                Log.Line($"[Gun {slot.Index + 1}] No usable calibration. Opening calibration...");
                // No worker exists yet at startup, so there is nothing to resume onto
                // the read thread's pipe; GunSlot.PipeAbandoned still matters for
                // Shutdown if this calibration's read thread does not stop.
                chosen = Calibrate(slot, "Calibration cancelled — gun will not be accurate until F12.") ?? chosen;
            }
            if (chosen is { } mode && mode != _mode) SetMode(mode);

            foreach (var slot in _slots)
            {
                var gun = slot.Gun;
                ConnectFeeder(slot, "Mouse", "SendInput", gun.MouseFeeder.Connect);
                ConnectFeeder(slot, "Keyboard", "SendInput", gun.KbFeeder.Connect);
                ConnectFeeder(slot, "Joystick", "ViGEmBus, Xbox 360", gun.JoyFeeder.Connect);

                // A feeder that stops accepting reports, or recovers, is a status
                // change a host may want to show. Wired after Connect so a connect
                // failure cannot fire it before the slot is finished.
                gun.MouseFeeder.HealthChanged = RaiseStatusChanged;
                gun.KbFeeder.HealthChanged = RaiseStatusChanged;
                gun.JoyFeeder.HealthChanged = RaiseStatusChanged;
            }

            ReloadMappings(announce: false);

            Log.Line("Mapping OK.");
            Log.Line($"Ready to use! {_slots.Count} gun(s) active.");
            Log.Line($"[Calibration] {_mode} mode.");

            foreach (var slot in _slots)
            {
                var gun = slot.Gun;
                slot.Worker = new GunWorker(
                    gun.Index, gun.Reader, gun.MouseFeeder, gun.KbFeeder, gun.JoyFeeder,
                    () => gun.Snapshot,
                    () => _slots.Where(s => s != slot && s.Gun.Reader.DevicePath != null)
                                .Select(s => s.Gun.Reader.DevicePath)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase),
                    RaiseStatusChanged);
                slot.Worker.Start();
            }

            RaiseStatusChanged();
            return true;
        }

        private void ConnectGun(int index, USBDeviceInfo device)
        {
            try
            {
                Log.Line($"Gun {index + 1} connecting...");
                var reader = new GunconReader { ZThreshold = _zThreshold };
                reader.Connect(device);
                _slots.Add(new GunSlot(new GunInstance(index, reader)));
                Log.Line($"Gun {index + 1} connected.");

                var centres = StickCentres.Load(gunIndex: index);
                if (centres != null)
                {
                    reader.Centres = centres;
                    Log.Line($"Gun {index + 1} stick centres loaded.");
                }

                if (!reader.PipeTimeoutApplied)
                    Log.Warn($"Gun {index + 1} refused the USB transfer timeout; a wedged device can block indefinitely.");
            }
            catch (Exception ex)
            {
                Log.Line($"[Gun {index + 1}] Connect failed: {ex.Message}");
            }
        }

        private static void ConnectFeeder(GunSlot slot, string name, string backend, Action connect)
        {
            try
            {
                connect();
                Log.Line($"[Gun {slot.Index + 1}] {name} ready ({backend}).");
            }
            catch (Exception ex)
            {
                Log.Warn($"[Gun {slot.Index + 1}] {name} unavailable: {ex.Message}");
            }
        }

        // ------------------------------------------------------------ shutdown

        /// <summary>
        /// Releases every held virtual input, stops the workers and closes the devices.
        /// Idempotent: the second call must do nothing.
        /// </summary>
        public void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;

            foreach (var slot in _slots) slot.Worker?.RequestStop();

            bool anyWedged = false;

            foreach (var slot in _slots)
            {
                var gun = slot.Gun;

                if (slot.Worker != null && !slot.Worker.Join(TimeSpan.FromSeconds(2)))
                {
                    // A blocking WinUSB read has no transfer timeout, so a wedged device
                    // can leave its thread inside Read() indefinitely. Disposing the
                    // device underneath it would be an access violation; leaking the
                    // handle until process exit is harmless by comparison. The mouse and
                    // the keyboard are still released: Windows keeps an injected key or
                    // button down until it is pressed for real, nothing else would let go,
                    // and a thread stuck in the USB read is not inside a feed — the
                    // SendInput feeders keep separate buffers for exactly this call. The
                    // pad is left alone: the ViGEm target is not thread-safe, and the bus
                    // removes it when this process exits.
                    Log.Warn($"[Gun {gun.Index + 1}] thread did not stop in time; leaving its device open.");
                    try { gun.MouseFeeder.Disconnect(); } catch (Exception ex) { Log.Warn($"[Gun {gun.Index + 1}] mouse disconnect: {ex.Message}"); }
                    try { gun.KbFeeder.Disconnect(); } catch (Exception ex) { Log.Warn($"[Gun {gun.Index + 1}] keyboard disconnect: {ex.Message}"); }
                    anyWedged = true;
                    continue;
                }

                if (slot.PipeAbandoned)
                {
                    // A calibration read thread that did not stop may still be inside
                    // Reader.Read(); Disconnect() disposes the USBDevice, which would be
                    // a native use-after-free under that thread. The feeders are safe to
                    // close: the worker has joined, so nothing else touches them.
                    Log.Warn($"[Gun {gun.Index + 1}] left idle after an abandoned calibration read thread; leaving its device open.");
                    try { gun.MouseFeeder.Disconnect(); } catch (Exception ex) { Log.Warn($"[Gun {gun.Index + 1}] mouse disconnect: {ex.Message}"); }
                    try { gun.KbFeeder.Disconnect(); } catch (Exception ex) { Log.Warn($"[Gun {gun.Index + 1}] keyboard disconnect: {ex.Message}"); }
                    try { gun.JoyFeeder.Disconnect(); } catch (Exception ex) { Log.Warn($"[Gun {gun.Index + 1}] joystick disconnect: {ex.Message}"); }
                    slot.Worker?.Dispose();
                    continue;
                }

                try { gun.MouseFeeder.Disconnect(); } catch (Exception ex) { Log.Warn($"[Gun {gun.Index + 1}] mouse disconnect: {ex.Message}"); }
                try { gun.KbFeeder.Disconnect(); } catch (Exception ex) { Log.Warn($"[Gun {gun.Index + 1}] keyboard disconnect: {ex.Message}"); }
                try { gun.JoyFeeder.Disconnect(); } catch (Exception ex) { Log.Warn($"[Gun {gun.Index + 1}] joystick disconnect: {ex.Message}"); }
                try { gun.Reader.Disconnect(); } catch (Exception ex) { Log.Warn($"[Gun {gun.Index + 1}] reader disconnect: {ex.Message}"); }

                slot.Worker?.Dispose();
            }

            // After every pad has been unplugged. Harmless when no joystick ever connected. Skipped when a
            // worker is still alive: freeing the client underneath it would be a native use-after-free, and
            // process exit tears the bus down anyway.
            if (!anyWedged) XboxBus.Close();
        }

        // --------------------------------------------------------------- status

        /// <summary>
        /// Every gun's state right now. A fresh list per call: a form asks once a
        /// second at most, so the allocation is beneath notice. IsConnected is false
        /// before the workers exist, which only the startup path can observe.
        /// </summary>
        public IReadOnlyList<GunStatus> Status()
        {
            var statuses = new List<GunStatus>(_slots.Count);

            foreach (var slot in _slots)
            {
                var gun = slot.Gun;
                var calibration = gun.Snapshot.Calibration;

                statuses.Add(new GunStatus(
                    gun.Index,
                    gun.Reader.DevicePath,
                    slot.Worker?.IsConnected ?? false,
                    slot.PipeAbandoned,
                    calibration != null,
                    calibration?.Homography != null,
                    gun.MouseFeeder.Healthy,
                    gun.KbFeeder.Healthy,
                    gun.JoyFeeder.Healthy));
            }

            return statuses;
        }

        private void RaiseStatusChanged() => StatusChanged?.Invoke();

        // ---------------------------------------------------------- test frames

        /// <summary>
        /// Turns the per-frame Test-tab publication on or off; safe off the main thread
        /// since it only sets a volatile publish flag on each worker.
        /// </summary>
        public void WatchFrames(bool on)
        {
            foreach (var slot in _slots)
                if (slot.Worker != null)
                    slot.Worker.PublishFrames = on;
        }

        /// <summary>
        /// The newest frame one slot published, or null when the slot does not exist,
        /// its worker was never created (a degraded start), or nothing has been
        /// published yet. <paramref name="slotIndex"/> is a position in the slot list,
        /// not a <see cref="GunStatus.Index"/>.
        /// </summary>
        public GunFrame LatestFrame(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count)
                return null;

            return _slots[slotIndex].Worker?.LatestFrame;
        }

        // --------------------------------------------------------- calibration

        /// <summary>
        /// Opens the calibration window for every gun in turn, one at a time — while one
        /// gun's window is open the others keep aiming.
        /// </summary>
        public void RecalibrateAll()
        {
            CalibrationMode? chosen = null;

            foreach (var slot in _slots)
            {
                if (slot.PipeAbandoned)
                {
                    Log.Warn($"[Gun {slot.Index + 1}] is idle after a calibration read thread did not stop; restart the program to calibrate it again.");
                    continue;
                }

                Log.Line($"[Gun {slot.Index + 1}] Opening calibration (F12)...");

                if (!slot.Worker.Pause(TimeSpan.FromSeconds(2)))
                {
                    Log.Error($"[Gun {slot.Index + 1}] Worker did not pause; skipping calibration.");
                    slot.Worker.Resume();
                    continue;
                }

                try
                {
                    chosen = Calibrate(slot, "Calibration discarded; keeping the previous one.") ?? chosen;
                }
                finally
                {
                    // A read thread that did not stop within its join timeout may still
                    // be on the gun's USB pipe; resuming the worker onto the same pipe
                    // would tear frames between the two. Leave the gun idle instead —
                    // CalibrationWindow.Dispose already warned about it.
                    if (!slot.PipeAbandoned)
                        slot.Worker.Resume();
                }
            }

            if (chosen is { } mode && mode != _mode) SetMode(mode);

            RaiseStatusChanged();
        }

        /// <summary>
        /// Runs the calibration window for one gun, reloads its file, and prints one of
        /// three outcomes. Returns the mode the user accepted with, or null on cancel;
        /// the caller applies it once, after its own loop, so SetMode never inspects a
        /// gun whose calibration has not been loaded yet. Sets
        /// <see cref="GunSlot.PipeAbandoned"/> from the window's join result, read after
        /// the window's own Dispose() on every path including an exception.
        /// </summary>
        private CalibrationMode? Calibrate(GunSlot slot, string cancelledMessage)
        {
            if (slot.Worker != null && !slot.Worker.IsConnected)
            {
                Log.Warn($"[Gun {slot.Index + 1}] is disconnected; not opening its calibration window. Reconnect it and press F12.");
                return null;
            }

            CalibrationMode? chosen;
            CalibrationWindow w = null;
            try
            {
                // With an owner (the GUI) the dialog is modal to the main window and
                // opens on its monitor; without one (the console) ShowDialog runs its
                // own modal loop, which is what Application.Run used to do, and the
                // monitor comes from the console window.
                int screen = Screens.IndexOf(CalibrationOwner?.Handle ?? CalibrationScreenWindow);
                w = new CalibrationWindow(slot.Gun.Reader, slot.Index, _mode, screen);
                w.ShowDialog(CalibrationOwner);
                chosen = w.Saved ? w.ChosenMode : null;
            }
            catch (Exception ex)
            {
                Log.Error($"[Gun {slot.Index + 1} Calibration] Error: {ex.Message}");
                chosen = null;
            }
            finally
            {
                if (w != null)
                {
                    // ShowDialog, unlike Application.Run, does not dispose the form, so
                    // this is the first and only Dispose: its join is what sets
                    // ReadThreadStopped, and the form's _disposed guard makes a second
                    // pass (WinForms disposing a closed form) a no-op.
                    w.Dispose();
                    slot.PipeAbandoned |= !w.ReadThreadStopped;
                }
            }

            if (chosen == null)
                Log.Warn($"[Gun {slot.Index + 1}] {cancelledMessage}");
            else if (LoadCalibration(slot))
                Log.Line($"[Gun {slot.Index + 1}] Calibration saved and loaded.");
            else
                Log.Warn($"[Gun {slot.Index + 1}] Calibration was saved but could not be read back.");

            return chosen;
        }

        private bool LoadCalibration(GunSlot slot)
        {
            var gun = slot.Gun;
            var outcome = CalibrationFile.TryLoad(null, gun.Index, out var calib);

            if (outcome == CalibrationLoad.Malformed)
                Log.Warn($"[Gun {gun.Index + 1}] calibration_rect file is malformed and will be ignored.");

            if (calib?.Homography != null && calib.Homography.IsSuspect)
                Log.Warn($"[Gun {gun.Index + 1}] the projective calibration disagrees with the captured centre by "
                       + $"{calib.Homography.CentreError:0.###} of the screen. Recalibrate if aiming is off in H mode.");

            gun.Publish(new GunSnapshot(gun.Snapshot.Mapping, calib, _mode, DesktopFor(gun, calib)));
            return calib != null;
        }

        private static ScreenPlacement CurrentDesktop()
        {
            var r = SystemInformation.VirtualScreen;
            return new ScreenPlacement(r.X, r.Y, r.Width, r.Height);
        }

        /// <summary>
        /// The desktop to place this calibration's aim on, or screen-relative with a
        /// recalibrate warning when the file's monitor is not among the current ones.
        /// </summary>
        private static ScreenPlacement DesktopFor(GunInstance gun, CalibrationFile calib)
        {
            if (calib == null)
                return default;

            foreach (var screen in Screen.AllScreens)
                if (Screens.ToPlacement(screen.Bounds) == calib.Screen)
                    return CurrentDesktop();

            var s = calib.Screen;
            Log.Warn($"[Gun {gun.Index + 1}] calibrated on a {s.W}×{s.H} monitor at ({s.X}, {s.Y}); no such monitor now. "
                   + "Aiming as if on the primary screen — recalibrate with F12.");
            return default;
        }

        // ------------------------------------------------------- mode, mapping

        /// <summary>Applies a mapping mode to every gun and says so. H, the toolbar and the tray item.</summary>
        public void SetMode(CalibrationMode mode)
        {
            _mode = mode;
            Log.Line($"[Calibration] {_mode} mode.");

            foreach (var slot in _slots)
            {
                var gun = slot.Gun;
                var snapshot = gun.Snapshot;
                gun.Publish(new GunSnapshot(snapshot.Mapping, snapshot.Calibration, _mode, snapshot.Desktop));

                if (_mode == CalibrationMode.Homography && snapshot.Calibration?.Homography == null)
                {
                    if (snapshot.Calibration == null)
                        Log.Warn($"[Gun {gun.Index + 1}] has no calibration at all. Recalibrate with F12.");
                    else
                        Log.Warn($"[Gun {gun.Index + 1}] has no projective calibration; still using the linear one. "
                               + "Recalibrate with F12 to get one.");
                }
            }

            RaiseStatusChanged();
        }

        /// <summary>Re-reads every gun's mapping file and announces it. R, the toolbar and the tray item.</summary>
        public void ReloadMappings() => ReloadMappings(announce: true);

        private void ReloadMappings(bool announce)
        {
            if (announce) Log.Line("[Mapping] Reloading mappings…");

            foreach (var slot in _slots)
            {
                var gun = slot.Gun;
                string suffix = gun.Index > 0 ? $"_{gun.Index + 1}" : "";
                var mapping = MappingFile.Load(Path.Combine(AppInfo.BaseDirectory, $"mapping{suffix}.txt"));

                gun.Publish(new GunSnapshot(mapping, gun.Snapshot.Calibration, _mode, gun.Snapshot.Desktop));

                foreach (var d in mapping.Diagnostics)
                    Log.Line($"[Gun {gun.Index + 1} Mapping] {d}");

                Log.Line($"[Gun {gun.Index + 1} Mapping] Mouse: {mapping.Mouse.Count} entries, Keyboard: {mapping.Keyboard.Count} entries.");
            }

            if (announce)
            {
                Log.Line("[Mapping] OK.");
                RaiseStatusChanged();
            }
        }
    }
}
