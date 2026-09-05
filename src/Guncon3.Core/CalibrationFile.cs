// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
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
    /// One gun's calibration. Holds the five points captured at the screen corners and
    /// centre, and both mappings derived from them, so the two can never describe
    /// different capture sessions.
    /// </summary>
    public sealed class CalibrationFile
    {
        private static readonly (double X, double Y)[] NoPoints = Array.Empty<(double X, double Y)>();

        private const int PointCount = 5;

        /// <summary>
        /// The captured points: top-left, top-right, bottom-right, bottom-left, centre.
        /// Empty for a calibration written before points were stored.
        /// </summary>
        public IReadOnlyList<(double X, double Y)> Points { get; }

        public RectCalib Rect { get; }

        /// <summary>Null when there are no captured points, or when they were too degenerate to fit.</summary>
        public HomographyCalib? Homography { get; }

        /// <summary>
        /// The monitor this calibration was captured on. Its size is the rectangle's
        /// ScreenW/ScreenH; its origin is (0,0) for files written before the origin was
        /// stored.
        /// </summary>
        public ScreenPlacement Screen { get; }

        private CalibrationFile(IReadOnlyList<(double X, double Y)> points, RectCalib rect, HomographyCalib? homography, int screenX, int screenY)
        {
            Points = points;
            Rect = rect;
            Homography = homography;
            Screen = new ScreenPlacement(screenX, screenY, rect.ScreenW, rect.ScreenH);
        }

        /// <summary>Builds a calibration from a fresh capture. The rectangle is derived from the points'
        /// bounding box.</summary>
        public static CalibrationFile FromCapture(IReadOnlyList<(double X, double Y)> points, int screenW, int screenH)
            => FromCapture(points, new ScreenPlacement(0, 0, screenW, screenH));

        /// <summary>Builds a calibration from a fresh capture on a particular monitor. The rectangle is derived
        /// from the points' bounding box.</summary>
        public static CalibrationFile FromCapture(IReadOnlyList<(double X, double Y)> points, ScreenPlacement screen)
        {
            if (points == null || points.Count == 0)
                throw new ArgumentException("A capture needs points.", nameof(points));

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            foreach (var p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            var rect = new RectCalib
            {
                RawMinX = Math.Round(minX),
                RawMaxX = Math.Round(maxX),
                RawMinY = Math.Round(minY),
                RawMaxY = Math.Round(maxY),
                ScreenW = screen.W,
                ScreenH = screen.H,
                InvertY = true
            };

            var copy = new (double X, double Y)[points.Count];
            for (int i = 0; i < points.Count; i++) copy[i] = points[i];

            return new CalibrationFile(copy, rect, HomographyCalib.FromPoints(copy), screen.X, screen.Y);
        }

        /// <summary>Wraps a rectangle with no captured points — what a calibration written before this version
        /// loads as.</summary>
        public static CalibrationFile FromRect(RectCalib rect)
        {
            ArgumentNullException.ThrowIfNull(rect);
            return new CalibrationFile(NoPoints, rect, null, 0, 0);
        }

        /// <summary>Maps a raw point to normalized 0..1 screen coordinates. Falls back to the rectangle when
        /// the homography was asked for and is not available.</summary>
        public (double X, double Y) MapNormalized(double rawX, double rawY, CalibrationMode mode)
        {
            if (mode == CalibrationMode.Homography && Homography != null)
                return Homography.MapNormalized(rawX, rawY);

            return Rect.MapNormalized(rawX, rawY);
        }

        /// <summary>The file this gun's calibration lives in, next to the executable.</summary>
        private static string DefaultPath(int gunIndex)
        {
            string suffix = gunIndex > 0 ? $"_{gunIndex + 1}" : "";
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"calibration_rect{suffix}.txt");
        }

        /// <summary>
        /// Writes the captured points and the rectangle derived from them, even though
        /// this version does not need the rectangle, so a build that predates the points
        /// can still read the file.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The instance holds a non-empty point count other than <see cref="PointCount"/>.
        /// <see cref="Load"/> can only make sense of zero or exactly five points, so a file
        /// this method itself could not load back must never be written.
        /// </exception>
        public void Save(string? path = null, int gunIndex = 0)
        {
            if (Points.Count != 0 && Points.Count != PointCount)
                throw new InvalidOperationException(
                    $"Cannot save a calibration with {Points.Count} captured point(s); " +
                    $"Load only accepts 0 or {PointCount}.");

            path ??= DefaultPath(gunIndex);

            AtomicFile.Write(path, WriteTo);
        }

        private void WriteTo(TextWriter sw)
        {
            var ci = CultureInfo.InvariantCulture;

            sw.WriteLine("# raw gun coordinates captured at each target");
            sw.WriteLine("# P0 top-left, P1 top-right, P2 bottom-right, P3 bottom-left, P4 centre");
            for (int i = 0; i < Points.Count; i++)
                sw.WriteLine($"P{i}=" + Points[i].X.ToString("R", ci) + "," + Points[i].Y.ToString("R", ci));

            sw.WriteLine("RawMinX=" + Rect.RawMinX.ToString("R", ci));
            sw.WriteLine("RawMaxX=" + Rect.RawMaxX.ToString("R", ci));
            sw.WriteLine("RawMinY=" + Rect.RawMinY.ToString("R", ci));
            sw.WriteLine("RawMaxY=" + Rect.RawMaxY.ToString("R", ci));
            sw.WriteLine("ScreenX=" + Screen.X.ToString(ci));
            sw.WriteLine("ScreenY=" + Screen.Y.ToString(ci));
            sw.WriteLine("ScreenW=" + Rect.ScreenW.ToString(ci));
            sw.WriteLine("ScreenH=" + Rect.ScreenH.ToString(ci));
            sw.WriteLine("InvertY=" + (Rect.InvertY ? "1" : "0"));
        }

        /// <summary>Reads a calibration. Returns null when missing or unusable.</summary>
        public static CalibrationFile? Load(string? path = null, int gunIndex = 0)
        {
            path ??= DefaultPath(gunIndex);

            if (!File.Exists(path))
                return null;

            double rawMinX = 0, rawMaxX = 0, rawMinY = 0, rawMaxY = 0;
            int screenW = 0, screenH = 0;
            bool invertY = false;
            int screenX = 0, screenY = 0;
            var points = new (double X, double Y)?[PointCount];
            var ci = CultureInfo.InvariantCulture;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                    continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();

                if (key.Length == 2 && key[0] == 'P' && key[1] >= '0' && key[1] <= '4')
                {
                    // A point line that will not parse means the file is damaged. Half a
                    // capture is worse than none, so reject the whole thing.
                    if (!TryParsePoint(val, ci, out var point))
                        return null;

                    points[key[1] - '0'] = point;
                    continue;
                }

                switch (key)
                {
                    case "RawMinX": if (double.TryParse(val, NumberStyles.Float, ci, out var rminx)) rawMinX = rminx; break;
                    case "RawMaxX": if (double.TryParse(val, NumberStyles.Float, ci, out var rmaxx)) rawMaxX = rmaxx; break;
                    case "RawMinY": if (double.TryParse(val, NumberStyles.Float, ci, out var rminy)) rawMinY = rminy; break;
                    case "RawMaxY": if (double.TryParse(val, NumberStyles.Float, ci, out var rmaxy)) rawMaxY = rmaxy; break;
                    // The origin is placement metadata, not part of the mapping, so a
                    // damaged value falls back to the primary screen instead of
                    // rejecting the file.
                    case "ScreenX": if (int.TryParse(val, NumberStyles.Integer, ci, out var sx)) screenX = sx; break;
                    case "ScreenY": if (int.TryParse(val, NumberStyles.Integer, ci, out var sy)) screenY = sy; break;
                    case "ScreenW": if (int.TryParse(val, NumberStyles.Integer, ci, out var sw2)) screenW = sw2; break;
                    case "ScreenH": if (int.TryParse(val, NumberStyles.Integer, ci, out var sh)) screenH = sh; break;
                    case "InvertY": invertY = (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)); break;
                }
            }

            var rect = new RectCalib
            {
                RawMinX = rawMinX, RawMaxX = rawMaxX, RawMinY = rawMinY, RawMaxY = rawMaxY,
                ScreenW = screenW, ScreenH = screenH, InvertY = invertY
            };

            if (!rect.IsValid())
                return null;

            int found = 0;
            foreach (var p in points) if (p.HasValue) found++;

            if (found == 0)
                return new CalibrationFile(NoPoints, rect, null, screenX, screenY);

            // Some but not all: a truncated write. Reject rather than guess.
            if (found != PointCount)
                return null;

            var captured = new (double X, double Y)[PointCount];
            for (int i = 0; i < PointCount; i++)
            {
                if (points[i] is { } p) captured[i] = p;
                else return null;
            }

            return new CalibrationFile(captured, rect, HomographyCalib.FromPoints(captured), screenX, screenY);
        }

        /// <summary>Reads a calibration and says why it could not be used.</summary>
        public static CalibrationLoad TryLoad(string? path, int gunIndex, out CalibrationFile? calibration)
        {
            calibration = null;

            if (path == null)
                path = DefaultPath(gunIndex);

            if (!File.Exists(path))
                return CalibrationLoad.Missing;

            CalibrationFile? loaded;
            try { loaded = Load(path); }
            catch (IOException) { return CalibrationLoad.Malformed; }
            catch (UnauthorizedAccessException) { return CalibrationLoad.Malformed; }

            if (loaded == null)
                return CalibrationLoad.Malformed;

            calibration = loaded;
            return CalibrationLoad.Ok;
        }

        private static bool TryParsePoint(string value, IFormatProvider ci, out (double X, double Y) point)
        {
            point = default;

            var comma = value.IndexOf(',');
            if (comma <= 0) return false;

            if (!double.TryParse(value.Substring(0, comma).Trim(), NumberStyles.Float, ci, out var x)) return false;
            if (!double.TryParse(value.Substring(comma + 1).Trim(), NumberStyles.Float, ci, out var y)) return false;

            // NumberStyles.Float accepts NaN/Infinity under the invariant culture. Those
            // are never produced by real hardware (ABS_X/ABS_Y are short) and would
            // otherwise sail through the mapping unclamped, since a NaN comparison is
            // always false.
            if (!double.IsFinite(x) || !double.IsFinite(y)) return false;

            point = (x, y);
            return true;
        }
    }
}
