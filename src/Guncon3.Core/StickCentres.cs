// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Globalization;
using System.IO;

namespace Guncon3.Core
{
    /// <summary>The measured (or default) rest position of each of the four stick axes, used by <see
    /// cref="StickDigitizer"/> as the centre of its deadzone.</summary>
    public sealed class StickCentres
    {
        public int HatX { get; set; } = StickDigitizer.DefaultCentre;
        public int HatY { get; set; } = StickDigitizer.DefaultCentre;
        public int RX { get; set; } = StickDigitizer.DefaultCentre;
        public int RY { get; set; } = StickDigitizer.DefaultCentre;

        /// <summary>True when every axis centre is believable.</summary>
        public bool IsValid()
        {
            return StickDigitizer.IsPlausibleCentre(HatX) &&
                   StickDigitizer.IsPlausibleCentre(HatY) &&
                   StickDigitizer.IsPlausibleCentre(RX) &&
                   StickDigitizer.IsPlausibleCentre(RY);
        }

        /// <summary>The file this gun's stick centres live in, next to the executable.</summary>
        private static string DefaultPath(int gunIndex)
        {
            string suffix = gunIndex > 0 ? $"_{gunIndex + 1}" : "";
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"stick_centre{suffix}.txt");
        }

        /// <summary>Writes stick_centre.txt (next to the executable by default).</summary>
        public void Save(string? path = null, int gunIndex = 0)
        {
            path ??= DefaultPath(gunIndex);

            AtomicFile.Write(path, WriteTo);
        }

        private void WriteTo(TextWriter sw)
        {
            var ci = CultureInfo.InvariantCulture;

            sw.WriteLine("HatX=" + HatX.ToString(ci));
            sw.WriteLine("HatY=" + HatY.ToString(ci));
            sw.WriteLine("RX=" + RX.ToString(ci));
            sw.WriteLine("RY=" + RY.ToString(ci));
        }

        /// <summary>Reads stick_centre.txt. Returns null if the file is missing, cannot be parsed, or the
        /// result is not <see cref="IsValid"/>.</summary>
        public static StickCentres? Load(string? path = null, int gunIndex = 0)
        {
            path ??= DefaultPath(gunIndex);

            if (!File.Exists(path))
                return null;

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }

            var sc = new StickCentres();
            var ci = CultureInfo.InvariantCulture;

            foreach (var rawLine in lines)
            {
                var line = rawLine?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                    continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "HatX":
                        if (!int.TryParse(val, NumberStyles.Integer, ci, out var hx)) return null;
                        sc.HatX = hx;
                        break;
                    case "HatY":
                        if (!int.TryParse(val, NumberStyles.Integer, ci, out var hy)) return null;
                        sc.HatY = hy;
                        break;
                    case "RX":
                        if (!int.TryParse(val, NumberStyles.Integer, ci, out var rx)) return null;
                        sc.RX = rx;
                        break;
                    case "RY":
                        if (!int.TryParse(val, NumberStyles.Integer, ci, out var ry)) return null;
                        sc.RY = ry;
                        break;
                }
            }

            return sc.IsValid() ? sc : null;
        }
    }
}
