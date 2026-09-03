using System;
using System.Globalization;
using System.IO;

namespace Guncon3.Core
{
    /// <summary>Why a calibration file could not be used.</summary>
    public enum CalibrationLoad
    {
        Ok,
        Missing,
        Malformed
    }

    /// <summary>
    /// Minimal rectangular calibration: maps RAW_X/RAW_Y (the gun's raw range)
    /// onto normalized 0..1 screen space via <see cref="MapNormalized"/>.
    /// ScreenW/ScreenH are persisted metadata only and do not take part in the
    /// mapping — the caller decides what to scale the normalized result to.
    /// </summary>
    public class RectCalib
    {
        public double RawMinX { get; set; }
        public double RawMaxX { get; set; }
        public double RawMinY { get; set; }
        public double RawMaxY { get; set; }
        public int ScreenW { get; set; }
        public int ScreenH { get; set; }
        public bool InvertY { get; set; }

        /// <summary>Are the ranges valid and the screen size correct?</summary>
        public bool IsValid()
        {
            return RawMaxX > RawMinX &&
                   RawMaxY > RawMinY &&
                   ScreenW > 0 && ScreenH > 0;
        }

        /// <summary>
        /// Maps a RAW point to normalized 0..1 screen coordinates. ScreenW and ScreenH
        /// are persisted metadata and deliberately do not take part: the caller decides
        /// what to scale to.
        /// </summary>
        public (double X, double Y) MapNormalized(double rawX, double rawY)
        {
            if (!IsValid())
                return (0, 0);

            double nx = (rawX - RawMinX) / (RawMaxX - RawMinX);
            double ny = (rawY - RawMinY) / (RawMaxY - RawMinY);

            if (nx < 0) nx = 0; else if (nx > 1) nx = 1;
            if (ny < 0) ny = 0; else if (ny > 1) ny = 1;

            if (InvertY) ny = 1.0 - ny;

            return (nx, ny);
        }

        /// <summary>The file this gun's calibration lives in, next to the executable.</summary>
        private static string DefaultPath(int gunIndex)
        {
            string suffix = gunIndex > 0 ? $"_{gunIndex + 1}" : "";
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"calibration_rect{suffix}.txt");
        }

        /// <summary>Saves to calibration_rect.txt (next to the EXE by default).</summary>
        public void Save(string path = null, int gunIndex = 0)
        {
            path ??= DefaultPath(gunIndex);

            using var sw = new StreamWriter(path, false);
            var ci = CultureInfo.InvariantCulture;

            sw.WriteLine("RawMinX=" + RawMinX.ToString("R", ci));
            sw.WriteLine("RawMaxX=" + RawMaxX.ToString("R", ci));
            sw.WriteLine("RawMinY=" + RawMinY.ToString("R", ci));
            sw.WriteLine("RawMaxY=" + RawMaxY.ToString("R", ci));
            sw.WriteLine("ScreenW=" + ScreenW.ToString(ci));
            sw.WriteLine("ScreenH=" + ScreenH.ToString(ci));
            sw.WriteLine("InvertY=" + (InvertY ? "1" : "0"));
        }

        /// <summary>Loads from calibration_rect.txt. Returns null if missing or malformed.</summary>
        public static RectCalib Load(string path = null, int gunIndex = 0)
        {
            path ??= DefaultPath(gunIndex);

            if (!File.Exists(path))
                return null;

            var rc = new RectCalib();
            var ci = CultureInfo.InvariantCulture;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "RawMinX": if (double.TryParse(val, NumberStyles.Float, ci, out var rminx)) rc.RawMinX = rminx; break;
                    case "RawMaxX": if (double.TryParse(val, NumberStyles.Float, ci, out var rmaxx)) rc.RawMaxX = rmaxx; break;
                    case "RawMinY": if (double.TryParse(val, NumberStyles.Float, ci, out var rminy)) rc.RawMinY = rminy; break;
                    case "RawMaxY": if (double.TryParse(val, NumberStyles.Float, ci, out var rmaxy)) rc.RawMaxY = rmaxy; break;
                    case "ScreenW": if (int.TryParse(val, NumberStyles.Integer, ci, out var sw)) rc.ScreenW = sw; break;
                    case "ScreenH": if (int.TryParse(val, NumberStyles.Integer, ci, out var sh)) rc.ScreenH = sh; break;
                    case "InvertY": rc.InvertY = (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)); break;
                }
            }

            return rc.IsValid() ? rc : null;
        }

        /// <summary>
        /// Loads a calibration and says why it failed. A missing file is a normal
        /// first run; a malformed one is worth telling the user about.
        /// </summary>
        public static CalibrationLoad TryLoad(string path, int gunIndex, out RectCalib calibration)
        {
            calibration = null;

            path ??= DefaultPath(gunIndex);

            if (!File.Exists(path))
                return CalibrationLoad.Missing;

            RectCalib loaded;
            try { loaded = Load(path); }
            catch (IOException) { return CalibrationLoad.Malformed; }
            catch (UnauthorizedAccessException) { return CalibrationLoad.Malformed; }

            if (loaded == null)
                return CalibrationLoad.Malformed;

            calibration = loaded;
            return CalibrationLoad.Ok;
        }
    }
}
