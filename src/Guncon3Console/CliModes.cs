// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;
using Guncon3.Core;
using Nefarius.Drivers.WinUSB;

namespace Guncon3Console
{
    /// <summary>The two command-line modes that run instead of the app: `dump` captures raw USB frames to a
    /// file, `keys` prints the keycode table.</summary>
    internal static class CliModes
    {
        internal static void DumpPackets(int count, string outPath)
        {
            List<USBDeviceInfo> devices;
            try
            {
                devices = GunconReader.FindAllDevices();
            }
            catch (Exception ex)
            {
                Log.Fatal("Could not enumerate USB devices", ex);
                return;
            }

            if (devices.Count == 0)
            {
                Log.Fatal("No Guncon3 device found.");
                return;
            }

            var reader = new GunconReader();
            try
            {
                reader.Connect(devices[0]);
            }
            catch (Exception ex)
            {
                Log.Fatal("Could not connect to the Guncon3", ex);
                return;
            }

            // Give up rather than spin forever when nothing ever decodes: a wrong device or an unpaired gun
            // would otherwise hang with no output at all.
            int maxFailures = Math.Max(1000, count * 20);

            var lines = new List<string>(count);
            int bad = 0;

            Log.Line($"Capturing {count} frames. Move and shoot the gun to vary the data.");

            try
            {
                while (lines.Count < count && bad < maxFailures)
                {
                    if (reader.Read() != ReadResult.Ok)
                    {
                        bad++;
                        if (bad % 500 == 0)
                            Log.Line($"  {bad} reads rejected so far, {lines.Count}/{count} captured.");
                        continue;
                    }

                    lines.Add(Convert.ToHexString(reader.LastRawFrame));

                    if (lines.Count % 50 == 0)
                        Log.Line($"  {lines.Count}/{count}");
                }
            }
            finally
            {
                try { reader.Disconnect(); } catch { }
            }

            File.WriteAllLines(outPath, lines);

            if (lines.Count < count)
                Log.Line($"Gave up after {bad} rejected reads. Wrote {lines.Count} frames to {outPath}.");
            else
                Log.Line($"Wrote {lines.Count} frames to {outPath} ({bad} reads rejected).");
        }

        // === Full keycode table (4..111) ===
        internal static void PrintKeyCodes()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("KEYCODE\tKEY");
            Console.ResetColor();

            foreach (var (code, name) in KeyCodeTable.Entries)
                Console.WriteLine($"{code}\t{name}");

            Console.WriteLine();
            Console.WriteLine("Press any key to exit…");
            try { Console.ReadKey(true); } catch { }
        }
    }
}
