// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Guncon3.Core;
using Guncon3Console.Logging;

namespace Guncon3Console.Hosting
{
    /// <summary>Today's console mode: a console, the header, the console sink, and a key loop driving the
    /// engine. Everything here needs a console; <see cref="App"/> does not.</summary>
    internal static class ConsoleHost
    {
        private static bool _prepared;

        /// <summary>Gets a console, registers the console sink, names the window and prints the header. The two
        /// CLI modes call this too. Safe to call more than once.</summary>
        public static void PrepareConsole()
        {
            if (_prepared) return;
            _prepared = true;

            ConsoleWindow.Ensure();
            Log.AddSink(new ConsoleSink());

            try { Console.Title = "GUNCON3"; }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
            {
                // No console at all: the title is cosmetic and the rest still works.
            }

            // Attaching to the launching shell leaves the cursor just after its prompt; one blank line
            // separates our header from the command the user typed.
            Log.Blank();
            Log.Header($"GUNCON3 {AppInfo.Version} - MULTI-GUN SUPPORT (BASED ON SONIK PROJECT)");
            Log.Header("EXE:  " + AppInfo.ExePath, ConsoleColor.Green);
            Log.Header("BASE: " + AppInfo.BaseDirectory, ConsoleColor.Green);
            Log.Blank();
            Log.Line("Supports up to 2 lightguns (or more).");
        }

        /// <summary>Runs the console mode to completion. Returns the process exit code.</summary>
        public static int Run()
        {
            PrepareConsole();

            // PerMonitorV2 is what the calibration window's DPI handling is written against; both hosts use it.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var stop = new ManualResetEventSlim();

            // Ctrl+C (when not treated as input), Ctrl+Break and closing the console window arrive here rather
            // than as a key. Cancelling the default handler gives Shutdown the chance to release every held
            // virtual input first.
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                e.Cancel = true;
                try { stop.Set(); } catch (ObjectDisposedException) { }
            };
            Console.CancelKeyPress += onCancel;

            try
            {
                var app = new App { CalibrationScreenWindow = ConsoleWindow.Handle };

                if (!app.Start())
                    return Environment.ExitCode;

                Log.Line("  F12 = recalibrate all guns,  R = reload mappings,  H = toggle calibration mode,  ESC = exit  (console window must have focus)");

                KeyLoop(app, stop);
                app.Shutdown();
                return Environment.ExitCode;
            }
            finally
            {
                // Unsubscribe before `stop` is disposed, so a Ctrl+C arriving during shutdown cannot reach a
                // disposed event.
                Console.CancelKeyPress -= onCancel;

                try { Console.TreatControlCAsInput = false; }
                catch (Exception ex) when (ex is IOException or InvalidOperationException)
                {
                    // No console: nothing to restore.
                }
            }
        }

        /// <summary>Polls for console keys until ESC, Ctrl+C or Ctrl+Break; polling means a stop requested by
        /// CancelKeyPress is seen without waiting for another key. When stdin is redirected the loop waits for
        /// Ctrl+C / Ctrl+Break via CancelKeyPress.</summary>
        private static void KeyLoop(App app, ManualResetEventSlim stop)
        {
            try { Console.TreatControlCAsInput = true; }
            catch (IOException) { /* no console: Ctrl+C arrives through CancelKeyPress instead */ }

            while (!stop.IsSet)
            {
                bool available;
                try
                {
                    available = Console.KeyAvailable;
                }
                catch (InvalidOperationException)
                {
                    Log.Warn("Console input is redirected; F12 / R / H are unavailable. Ctrl+C or Ctrl+Break exits.");
                    stop.Wait();
                    return;
                }

                if (!available)
                {
                    stop.Wait(50);
                    continue;
                }

                var k = Console.ReadKey(true);

                bool ctrlC = k.Key == ConsoleKey.C && (k.Modifiers & ConsoleModifiers.Control) != 0;

                if (k.Key == ConsoleKey.Escape || ctrlC)
                    return;
                if (k.Key == ConsoleKey.F12)
                    app.RecalibrateAll();
                else if (k.Key == ConsoleKey.R)
                    app.ReloadMappings();
                else if (k.Key == ConsoleKey.H)
                    app.SetMode(app.Mode == CalibrationMode.Rect ? CalibrationMode.Homography : CalibrationMode.Rect);
            }
        }
    }
}
