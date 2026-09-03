using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Guncon3.Core;
using Nefarius.Drivers.WinUSB;
using Guncon3Console.TetherScript;

namespace Guncon3Console
{
    internal static class Program
    {
        // Built once at startup and never mutated afterwards: the workers list is
        // indexed in parallel with this one, so removing or reordering an entry would
        // silently mispair guns and workers. A disconnected gun stays here; its worker
        // tracks the connection state.
        private static readonly List<GunInstance> _guns = new List<GunInstance>();
        private static volatile bool _running = true;
        private static CalibrationMode _calibrationMode = CalibrationMode.Rect;

        [STAThread]
        private static void Main(string[] args)
        {
            Console.Title = "GUNCON3";
            PrintHeader();

            // === "keys": show keycode table and exit ===
            if (args.Length > 0 && args[0].Equals("keys", StringComparison.OrdinalIgnoreCase))
            {
                CliModes.PrintKeyCodes();
                return;
            }

            // === "dump": capture raw USB frames for test vectors and exit ===
            if (args.Length > 0 && args[0].Equals("dump", StringComparison.OrdinalIgnoreCase))
            {
                int count = (args.Length > 1 && int.TryParse(args[1], out var c)) ? c : 500;
                string outPath = (args.Length > 2) ? args[2] : "packets.txt";
                CliModes.DumpPackets(count, outPath);
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // --- Detect all Guncon3 devices ---
            List<USBDeviceInfo> devices;
            try
            {
                devices = GunconReader.FindAllDevices();
            }
            catch (Exception ex)
            {
                ConsoleLog.FailAndExit("Could not enumerate USB devices", ex);
                return;
            }

            if (devices.Count == 0)
            {
                ConsoleLog.FailAndExit("No Guncon3 device found.");
                return;
            }

            ConsoleLog.Line($"Found {devices.Count} Guncon3 device(s).");

            // --- Connect each gun ---
            for (int i = 0; i < devices.Count; i++)
            {
                try
                {
                    ConsoleLog.Line($"Gun {i + 1} connecting...");
                    var reader = new GunconReader();
                    reader.Connect(devices[i]);
                    _guns.Add(new GunInstance(i, reader));
                    ConsoleLog.Line($"Gun {i + 1} connected.");

                    var centres = StickCentres.Load(gunIndex: i);
                    if (centres != null)
                    {
                        reader.Centres = centres;
                        ConsoleLog.Line($"Gun {i + 1} stick centres loaded.");
                    }

                    if (!reader.PipeTimeoutApplied)
                        ConsoleLog.Warn($"Gun {i + 1} refused the USB transfer timeout; a wedged device can block indefinitely.");
                }
                catch (Exception ex)
                {
                    ConsoleLog.Line($"[Gun {i + 1}] Connect failed: {ex.Message}");
                }
            }

            if (_guns.Count == 0)
            {
                ConsoleLog.FailAndExit("No Guncon3 could be connected.");
                return;
            }

            // --- Calibration for each gun ---
            foreach (var gun in _guns)
            {
                if (!LoadCalibration(gun))
                {
                    ConsoleLog.Line($"[Gun {gun.Index + 1}] No usable calibration. Opening calibration...");
                    LaunchCalibrationWindowModal(gun);
                    if (!LoadCalibration(gun))
                    {
                        ConsoleLog.Line($"[Gun {gun.Index + 1}] WARNING: no calibration — gun will not be accurate.");
                    }
                }
                else
                {
                    ConsoleLog.Line($"[Gun {gun.Index + 1}] Calibration loaded.");
                }
            }

            // --- Feeders ---
            foreach (var gun in _guns)
            {
                TryConnectFeeders(gun);
            }

            // --- Mapping ---
            // Gun 1 uses mapping.txt, Gun 2 uses mapping_2.txt, etc.
            ReloadMappings(announce: false);

            ConsoleLog.Line("Mapping OK.");
            ConsoleLog.Line($"Ready to use! {_guns.Count} gun(s) active.");
            ConsoleLog.Line($"[Calibration] {_calibrationMode} mode.");
            ConsoleLog.Line("  F12 = recalibrate all guns,  R = reload mappings,  H = toggle calibration mode,  ESC = exit");

            var workers = new List<GunWorker>();
            foreach (var gun in _guns)
            {
                var worker = new GunWorker(
                    gun.Index, gun.Reader, gun.MouseFeeder, gun.KbFeeder, gun.JoyFeeder,
                    () => gun.Snapshot,
                    () => _guns.Where(g => g != gun && g.Reader.DevicePath != null)
                               .Select(g => g.Reader.DevicePath)
                               .ToHashSet(StringComparer.OrdinalIgnoreCase));
                workers.Add(worker);
                worker.Start();
            }

            while (_running)
            {
                var k = Console.ReadKey(true);

                if (k.Key == ConsoleKey.Escape)
                    _running = false;
                else if (k.Key == ConsoleKey.F12)
                    RecalibrateAll(workers);
                else if (k.Key == ConsoleKey.R)
                    ReloadMappings();
                else if (k.Key == ConsoleKey.H)
                    ToggleCalibrationMode();
            }

            foreach (var w in workers) w.RequestStop();

            for (int i = 0; i < workers.Count; i++)
            {
                var gun = _guns[i];

                if (!workers[i].Join(TimeSpan.FromSeconds(2)))
                {
                    // A blocking WinUSB read has no transfer timeout, so a wedged device
                    // can leave its thread inside Read() indefinitely. Disposing the
                    // device underneath it would be an access violation; leaking the
                    // handle until process exit is harmless by comparison.
                    ConsoleLog.Warn($"[Gun {gun.Index + 1}] thread did not stop in time; leaving its device open.");
                    continue;
                }

                try { gun.MouseFeeder.Disconnect(); } catch { }
                try { gun.KbFeeder.Disconnect(); } catch { }
                try { gun.JoyFeeder.Disconnect(); } catch { }
                try { gun.Reader.Disconnect(); } catch { }

                workers[i].Dispose();
            }
        }

        private static void ReloadMappings(bool announce = true)
        {
            if (announce) ConsoleLog.Line("[Mapping] Reloading mappings…");
            foreach (var gun in _guns)
            {
                string suffix = gun.Index > 0 ? $"_{gun.Index + 1}" : "";
                LoadMapping($"mapping{suffix}.txt", gun);
            }
            if (announce) ConsoleLog.Line("[Mapping] OK.");
        }

        /// <summary>
        /// Flips every gun between the linear and the projective mapping. Both come
        /// from the same capture, so this is a live A/B: point at the same spot and
        /// press H. The choice is not remembered across runs.
        /// </summary>
        private static void ToggleCalibrationMode()
        {
            _calibrationMode = _calibrationMode == CalibrationMode.Rect
                ? CalibrationMode.Homography
                : CalibrationMode.Rect;

            ConsoleLog.Line($"[Calibration] {_calibrationMode} mode.");

            foreach (var gun in _guns)
            {
                var snapshot = gun.Snapshot;
                gun.Publish(new GunSnapshot(snapshot.Mapping, snapshot.Calibration, _calibrationMode));

                if (_calibrationMode == CalibrationMode.Homography && snapshot.Calibration?.Homography == null)
                {
                    if (snapshot.Calibration == null)
                        ConsoleLog.Warn($"[Gun {gun.Index + 1}] has no calibration at all. Recalibrate with F12.");
                    else
                        ConsoleLog.Warn($"[Gun {gun.Index + 1}] has no projective calibration; still using the linear one. "
                                      + "Recalibrate with F12 to get one.");
                }
            }
        }

        private static bool LoadCalibration(GunInstance gun)
        {
            var outcome = CalibrationFile.TryLoad(null, gun.Index, out var calib);

            if (outcome == CalibrationLoad.Malformed)
                ConsoleLog.Warn($"[Gun {gun.Index + 1}] calibration_rect file is malformed and will be ignored.");

            if (calib?.Homography != null && calib.Homography.IsSuspect)
                ConsoleLog.Warn($"[Gun {gun.Index + 1}] the projective calibration disagrees with the captured centre by "
                              + $"{calib.Homography.CentreError:0.###} of the screen. Recalibrate if aiming is off in H mode.");

            gun.Publish(new GunSnapshot(gun.Snapshot.Mapping, calib, _calibrationMode));
            return calib != null;
        }

        private static void LaunchCalibrationWindowModal(GunInstance gun)
        {
            try
            {
                using (var w = new CalibrationWindow(gun.Reader, gun.Index, _calibrationMode))
                    Application.Run(w);
            }
            catch (Exception ex)
            {
                ConsoleLog.Line($"[Gun {gun.Index + 1} Calibration] Error: " + ex.Message);
            }
        }

        private static void RecalibrateAll(List<GunWorker> workers)
        {
            for (int i = 0; i < _guns.Count; i++)
            {
                var gun = _guns[i];
                var worker = workers[i];

                ConsoleLog.Line($"[Gun {gun.Index + 1}] Opening calibration (F12)...");

                if (!worker.Pause(TimeSpan.FromSeconds(2)))
                {
                    ConsoleLog.Error($"[Gun {gun.Index + 1}] Worker did not pause; skipping calibration.");
                    worker.Resume();
                    continue;
                }

                try
                {
                    LaunchCalibrationWindowModal(gun);

                    if (LoadCalibration(gun))
                        ConsoleLog.Line($"[Gun {gun.Index + 1}] Calibration loaded.");
                    else
                        ConsoleLog.Warn($"[Gun {gun.Index + 1}] WARNING: calibration not saved.");
                }
                finally
                {
                    worker.Resume();
                }
            }
        }

        private static void TryConnectFeeders(GunInstance gun)
        {
            try
            {
                ConsoleLog.Line($"[Gun {gun.Index + 1}] Mouse Connecting...");
                gun.MouseFeeder.Connect();
                ConsoleLog.Line($"[Gun {gun.Index + 1}] Mouse Connected (TetherScript).");
            }
            catch (Exception ex)
            {
                ConsoleLog.Line($"[Gun {gun.Index + 1} MouseFeeder] Connect fail:\n" + ex);
            }

            try
            {
                ConsoleLog.Line($"[Gun {gun.Index + 1}] Keyboard Connecting...");
                gun.KbFeeder.Connect();
                ConsoleLog.Line($"[Gun {gun.Index + 1}] Keyboard Connected (TetherScript).");
            }
            catch (Exception ex)
            {
                ConsoleLog.Line($"[Gun {gun.Index + 1} KeyboardFeeder] Connect fail:\n" + ex);
            }

            try
            {
                ConsoleLog.Line($"[Gun {gun.Index + 1}] Joystick Connecting...");
                gun.JoyFeeder.Connect();
                ConsoleLog.Line($"[Gun {gun.Index + 1}] Joystick Connected (TetherScript).");
            }
            catch (Exception ex)
            {
                ConsoleLog.Line($"[Gun {gun.Index + 1} JoystickFeeder] Connect fail:\n" + ex);
            }
        }

        private static void LoadMapping(string path, GunInstance gun)
        {
            var mapping = MappingFile.Load(path);

            gun.Publish(new GunSnapshot(mapping, gun.Snapshot.Calibration, _calibrationMode));

            foreach (var d in mapping.Diagnostics)
                ConsoleLog.Line($"[Gun {gun.Index + 1} Mapping] {d}");

            ConsoleLog.Line($"[Gun {gun.Index + 1} Mapping] Mouse: {mapping.Mouse.Count} entries, Keyboard: {mapping.Keyboard.Count} entries.");
        }

        private static void PrintHeader()
        {
            ConsoleLog.Header("GUNCON3 V0.50 - MULTI-GUN SUPPORT (BASED ON SONIK PROJECT)");
            ConsoleLog.Header("BUILD TAG: MULTI-GUN v1 + feeders-guard (STRICT TS)", ConsoleColor.Green);
            ConsoleLog.Header("EXE:  " + System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName, ConsoleColor.Green);
            ConsoleLog.Header("BASE: " + AppDomain.CurrentDomain.BaseDirectory, ConsoleColor.Green);
            ConsoleLog.Blank();
            ConsoleLog.Line("Supports up to 2 lightguns (or more).");
        }

    }
}
