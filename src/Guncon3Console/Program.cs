// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Globalization;
using Guncon3Console.Hosting;

namespace Guncon3Console
{
    /// <summary>Chooses what to run: the two CLI modes, the console host, or the GUI host. Nothing else lives
    /// here — each host owns its own set-up.</summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (IsMode(args, "keys"))
            {
                ConsoleHost.PrepareConsole();
                CliModes.PrintKeyCodes();
                return 0;
            }

            // Everything below drives a gun or the virtual devices, so it needs the lock. `keys` above only
            // prints a table and is never blocked.
            using var instance = SingleInstance.TryAcquire();
            if (instance == null)
            {
                if (IsMode(args, "dump") || IsMode(args, "--console"))
                {
                    ConsoleHost.PrepareConsole();
                    Log.Error("GUNCON3 is already running.");
                    return 1;
                }

                // A second double-click brings the running window to the front.
                SingleInstance.SignalShow();
                return 0;
            }

            if (IsMode(args, "dump"))
            {
                ConsoleHost.PrepareConsole();
                int count = args.Length > 1
                            && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 500;
                string outPath = args.Length > 2 ? args[2] : "packets.txt";
                CliModes.DumpPackets(count, outPath);
                return Environment.ExitCode;
            }

            if (IsMode(args, "--console"))
                return ConsoleHost.Run();

            return GuiHost.Run(instance);
        }

        private static bool IsMode(string[] args, string name)
            => args.Length > 0 && args[0].Equals(name, StringComparison.OrdinalIgnoreCase);
    }
}
