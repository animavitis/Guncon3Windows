using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GunconUSB;
using Guncon3Console.TetherScript;

namespace Guncon3Console
{
    /// <summary>
    /// Holds all per-gun objects: reader, state, calibration, feeders.
    /// </summary>
    internal class GunInstance
    {
        public int Index { get; }
        public GunconReader Reader { get; }
        public GunState State => Reader.State;
        public RectCalib Rect { get; set; }
        public AbsMouseFeeder MouseFeeder { get; }
        public KeyboardFeeder KbFeeder { get; }

        public GunInstance(int index, GunconReader reader)
        {
            Index = index;
            Reader = reader;
            MouseFeeder = new AbsMouseFeeder(reader.State);
            KbFeeder = new KeyboardFeeder(reader.State);
        }
    }

    internal static class Program
    {
        private static readonly List<GunInstance> _guns = new List<GunInstance>();
        private static volatile bool _running = true;

        [STAThread]
        private static void Main(string[] args)
        {
            Console.Title = "GUNCON3";
            PrintHeader();

            // === "keys": show keycode table and exit ===
            if (args.Length > 0 && args[0].Equals("keys", StringComparison.OrdinalIgnoreCase))
            {
                PrintKeyCodes();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // --- Detect all Guncon3 devices ---
            List<MadWizard.WinUSBNet.USBDeviceInfo> devices;
            try
            {
                devices = GunconReader.FindAllDevices();
            }
            catch (Exception ex)
            {
                FailAndExit("Could not enumerate USB devices", ex);
                return;
            }

            if (devices.Count == 0)
            {
                FailAndExit("No Guncon3 device found.");
                return;
            }

            Console.WriteLine($"Found {devices.Count} Guncon3 device(s).");

            // --- Connect each gun ---
            for (int i = 0; i < devices.Count; i++)
            {
                try
                {
                    Console.WriteLine($"Gun {i + 1} connecting...");
                    var reader = new GunconReader();
                    reader.Connect(devices[i]);
                    _guns.Add(new GunInstance(i, reader));
                    Console.WriteLine($"Gun {i + 1} connected.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Gun {i + 1}] Connect failed: {ex.Message}");
                }
            }

            if (_guns.Count == 0)
            {
                FailAndExit("No Guncon3 could be connected.");
                return;
            }

            // --- Calibration for each gun ---
            foreach (var gun in _guns)
            {
                if (!LoadRectCalib(gun))
                {
                    Console.WriteLine($"[Gun {gun.Index + 1}] No calibration file found. Opening calibration...");
                    LaunchCalibrationWindowModal(gun);
                    if (!LoadRectCalib(gun))
                    {
                        Console.WriteLine($"[Gun {gun.Index + 1}] WARNING: no calibration — gun will not be accurate.");
                    }
                }
                else
                {
                    Console.WriteLine($"[Gun {gun.Index + 1}] Calibration loaded.");
                }
            }

            // --- Feeders ---
            foreach (var gun in _guns)
            {
                TryConnectFeeders(gun);
            }

            // --- Mapping ---
            // Gun 1 uses mapping.txt, Gun 2 uses mapping_2.txt, etc.
            foreach (var gun in _guns)
            {
                string suffix = gun.Index > 0 ? $"_{gun.Index + 1}" : "";
                string mappingFile = $"mapping{suffix}.txt";
                LoadMapping(mappingFile, gun);
            }

            Console.WriteLine("Mapping OK.");
            Console.WriteLine($"Ready to use! {_guns.Count} gun(s) active.");
            Console.WriteLine("  F12 = recalibrate all guns,  R = reload mappings,  ESC = exit");

            while (_running)
            {
                foreach (var gun in _guns)
                {
                    try
                    {
                        gun.Reader.Read();
                    }
                    catch { continue; }

                    if (gun.Rect != null && gun.Rect.IsValid())
                    {
                        double rawX = gun.State.RAW_X;
                        double rawY = gun.State.RAW_Y;
                        var (px, py) = gun.Rect.Map(rawX, rawY);

                        double nx = (gun.Rect.ScreenW > 1) ? (px / (gun.Rect.ScreenW - 1)) : 0.0;
                        double ny = (gun.Rect.ScreenH > 1) ? (py / (gun.Rect.ScreenH - 1)) : 0.0;
                        if (nx < 0) nx = 0; if (nx > 1) nx = 1;
                        if (ny < 0) ny = 0; if (ny > 1) ny = 1;

                        short ax = (short)Math.Round(nx * 32767.0);
                        short ay = (short)Math.Round(ny * 32767.0);

                        gun.State.ABS_X = ax;
                        gun.State.ABS_Y = ay;
                    }

                    try
                    {
                        gun.MouseFeeder.Feed();
                        gun.KbFeeder.Feed();
                    }
                    catch { }
                }

                if (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Escape)
                        _running = false;
                    else if (k.Key == ConsoleKey.F12)
                        RecalibrateAll();
                    else if (k.Key == ConsoleKey.R)
                    {
                        Console.WriteLine("[Mapping] Reloading mappings…");
                        foreach (var gun in _guns)
                        {
                            string suffix = gun.Index > 0 ? $"_{gun.Index + 1}" : "";
                            LoadMapping($"mapping{suffix}.txt", gun);
                        }
                        Console.WriteLine("[Mapping] OK.");
                    }
                }

                Thread.Sleep(1);
            }

            foreach (var gun in _guns)
            {
                try { gun.MouseFeeder.Disconnect(); } catch { }
                try { gun.KbFeeder.Disconnect(); } catch { }
                try { gun.Reader.Disconnect(); } catch { }
            }
        }

        private static bool LoadRectCalib(GunInstance gun)
        {
            gun.Rect = RectCalib.Load(gunIndex: gun.Index);
            return gun.Rect != null && gun.Rect.IsValid();
        }

        private static void LaunchCalibrationWindowModal(GunInstance gun)
        {
            try
            {
                using (var w = new CalibrationWindow(gun.Reader, gun.Index))
                    Application.Run(w);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gun {gun.Index + 1} Calibration] Error: " + ex.Message);
            }
        }

        private static void RecalibrateAll()
        {
            foreach (var gun in _guns)
            {
                Console.WriteLine($"[Gun {gun.Index + 1}] Opening calibration (F12)...");
                LaunchCalibrationWindowModal(gun);
                if (LoadRectCalib(gun))
                    Console.WriteLine($"[Gun {gun.Index + 1}] Calibration loaded.");
                else
                    Console.WriteLine($"[Gun {gun.Index + 1}] WARNING: calibration not saved.");
            }
        }

        private static void TryConnectFeeders(GunInstance gun)
        {
            try
            {
                Console.WriteLine($"[Gun {gun.Index + 1}] Mouse Connecting...");
                gun.MouseFeeder.Connect();
                Console.WriteLine($"[Gun {gun.Index + 1}] Mouse Connected (TetherScript).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gun {gun.Index + 1} MouseFeeder] Connect fail:\n" + ex);
            }

            try
            {
                Console.WriteLine($"[Gun {gun.Index + 1}] Keyboard Connecting...");
                gun.KbFeeder.Connect();
                Console.WriteLine($"[Gun {gun.Index + 1}] Keyboard Connected (TetherScript).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gun {gun.Index + 1} KeyboardFeeder] Connect fail:\n" + ex);
            }
        }

        private static void LoadMapping(string path, GunInstance gun)
        {
            gun.MouseFeeder.Mapping.Clear();
            gun.KbFeeder.Mapping.Clear();

            if (!File.Exists(path))
            {
                Console.WriteLine($"[Gun {gun.Index + 1} Mapping] {path} not found (empty mapping).");
                return;
            }

            byte lineNo = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                lineNo++;
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#")) continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var left = line.Substring(0, eq).Trim();
                var right = line.Substring(eq + 1).Trim();

                var dot = left.IndexOf('.');
                if (dot <= 0) continue;

                var device = left.Substring(0, dot).ToUpperInvariant();
                var cmd = left.Substring(dot + 1);

                if (!Enum.TryParse<GunButton>(right, ignoreCase: false, out var gunBtn))
                {
                    Console.WriteLine($"[Gun {gun.Index + 1} Mapping] Line {lineNo}: unknown guncommand: {right}");
                    continue;
                }

                if (device == "MOUSE")
                {
                    if (cmd.Equals("Left", StringComparison.OrdinalIgnoreCase))
                        gun.MouseFeeder.Mapping[gunBtn] = MouseButton.Left;
                    else if (cmd.Equals("Right", StringComparison.OrdinalIgnoreCase))
                        gun.MouseFeeder.Mapping[gunBtn] = MouseButton.Right;
                    else if (cmd.Equals("Middle", StringComparison.OrdinalIgnoreCase))
                        gun.MouseFeeder.Mapping[gunBtn] = MouseButton.Middle;
                }
                else if (device == "KEYBOARD")
                {
                    if (byte.TryParse(cmd, out var keyCode))
                        gun.KbFeeder.Mapping[gunBtn] = keyCode;
                }
            }

            Console.WriteLine($"[Gun {gun.Index + 1} Mapping] Mouse: {gun.MouseFeeder.Mapping.Count} entries, Keyboard: {gun.KbFeeder.Mapping.Count} entries.");
        }

        private static void FailAndExit(string msg, Exception ex = null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg);
            if (ex != null) Console.WriteLine(ex);
            Console.ResetColor();
            Console.WriteLine("Press any key to exit.");
            try { Console.ReadKey(true); } catch { }
        }

        private static void PrintHeader()
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("GUNCON3 V0.50 - MULTI-GUN SUPPORT (BASED ON SONIK PROJECT)");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("BUILD TAG: MULTI-GUN v1 + feeders-guard (STRICT TS)");
            Console.WriteLine("EXE:  " + System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            Console.WriteLine("BASE: " + AppDomain.CurrentDomain.BaseDirectory);
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Supports up to 2 lightguns (or more).");
        }

        // === Full keycode table (4..111) ===
        private static void PrintKeyCodes()
        {
            string[] name = new string[112];

            name[4] = "a"; name[5] = "b"; name[6] = "c"; name[7] = "d"; name[8] = "e"; name[9] = "f";
            name[10] = "g"; name[11] = "h"; name[12] = "i"; name[13] = "j"; name[14] = "k"; name[15] = "l";
            name[16] = "m"; name[17] = "n"; name[18] = "o"; name[19] = "p"; name[20] = "q"; name[21] = "r";
            name[22] = "s"; name[23] = "t"; name[24] = "u"; name[25] = "v"; name[26] = "w"; name[27] = "x";
            name[28] = "y"; name[29] = "z";

            name[30] = "1"; name[31] = "2"; name[32] = "3"; name[33] = "4"; name[34] = "5";
            name[35] = "6"; name[36] = "7"; name[37] = "8"; name[38] = "9"; name[39] = "0";

            name[40] = "ENTER";
            name[41] = "ESCAPE";
            name[42] = "BACKSPACE";
            name[43] = "TAB";
            name[44] = "SPACEBAR";
            name[45] = "-";
            name[46] = "=";
            name[47] = "[";
            name[48] = "]";
            name[49] = "\\";
            name[50] = "";
            name[51] = ";";
            name[52] = "dummy5";
            name[53] = "`";
            name[54] = ",";
            name[55] = ".";
            name[56] = "/";

            name[57] = "CAPSLOCK";
            name[58] = "F1"; name[59] = "F2"; name[60] = "F3"; name[61] = "F4"; name[62] = "F5";
            name[63] = "F6"; name[64] = "F7"; name[65] = "F8"; name[66] = "F9"; name[67] = "F10";
            name[68] = "F11"; name[69] = "F12";

            name[70] = "PRINTSCREEN";
            name[71] = "SCROLLLOCK";
            name[72] = "PAUSE";
            name[73] = "INSERT";
            name[74] = "HOME";
            name[75] = "PAGEUP";
            name[76] = "DELETE";
            name[77] = "END";
            name[78] = "PAGEDOWN";
            name[79] = "RIGHTARROW";
            name[80] = "LEFTARROW";
            name[81] = "DOWNARROW";
            name[82] = "UPARROW";

            name[83] = "NUMLOCK";
            name[84] = "K/";
            name[85] = "K*";
            name[86] = "K-";
            name[87] = "K+";
            name[88] = "KENTER";
            name[89] = "K1";
            name[90] = "K2";
            name[91] = "K3";
            name[92] = "K4";
            name[93] = "K5";
            name[94] = "K6";
            name[95] = "K7";
            name[96] = "K8";
            name[97] = "K9";
            name[98] = "K0";
            name[99] = "K.";

            name[100] = "F13"; name[101] = "F14"; name[102] = "F15"; name[103] = "F16";
            name[104] = "F17"; name[105] = "F18"; name[106] = "F19"; name[107] = "F20";
            name[108] = "F21"; name[109] = "F22"; name[110] = "F23"; name[111] = "F24";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("KEYCODE\tKEY");
            Console.ResetColor();

            for (int i = 4; i <= 111; i++)
            {
                var n = name[i];
                if (!string.IsNullOrEmpty(n))
                    Console.WriteLine($"{i}\t{n}");
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit…");
            try { Console.ReadKey(true); } catch { }
        }
    }
}
