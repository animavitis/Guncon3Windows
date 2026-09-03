using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class CalibrationFileTests
    {
        private static List<(double X, double Y)> Keystone() => new()
        {
            (200, 1800), (1800, 1600), (1800, 400), (200, 200), (1000, 1000)
        };

        [Fact]
        public void FromCapture_DerivesTheRectangleFromThePointsBoundingBox()
        {
            var file = CalibrationFile.FromCapture(Keystone(), 1920, 1080);

            Assert.Equal(200, file.Rect.RawMinX);
            Assert.Equal(1800, file.Rect.RawMaxX);
            Assert.Equal(200, file.Rect.RawMinY);
            Assert.Equal(1800, file.Rect.RawMaxY);
            Assert.Equal(1920, file.Rect.ScreenW);
            Assert.Equal(1080, file.Rect.ScreenH);
            Assert.True(file.Rect.InvertY);
            Assert.True(file.Rect.IsValid());
        }

        [Fact]
        public void FromCapture_KeepsThePointsAndDerivesAHomography()
        {
            var file = CalibrationFile.FromCapture(Keystone(), 1920, 1080);

            Assert.Equal(5, file.Points.Count);
            Assert.Equal((200.0, 1800.0), file.Points[0]);
            Assert.NotNull(file.Homography);
        }

        [Fact]
        public void MapNormalized_RectModeAgreesWithTheRectangleAlone()
        {
            var file = CalibrationFile.FromCapture(Keystone(), 1920, 1080);

            var viaFile = file.MapNormalized(1000, 1000, CalibrationMode.Rect);
            var viaRect = file.Rect.MapNormalized(1000, 1000);

            Assert.Equal(viaRect.X, viaFile.X, 9);
            Assert.Equal(viaRect.Y, viaFile.Y, 9);
        }

        [Fact]
        public void MapNormalized_HomographyModeAgreesWithTheHomographyAlone()
        {
            var file = CalibrationFile.FromCapture(Keystone(), 1920, 1080);

            var viaFile = file.MapNormalized(1000, 1000, CalibrationMode.Homography);
            var viaHomography = file.Homography.MapNormalized(1000, 1000);

            Assert.Equal(viaHomography.X, viaFile.X, 9);
            Assert.Equal(viaHomography.Y, viaFile.Y, 9);
        }

        [Fact]
        public void MapNormalized_FallsBackToTheRectangleWhenNoHomographyExists()
        {
            // A rectangle with no captured points: what an existing calibration file
            // loads as. Asking for Homography must not throw or return nonsense.
            var rect = new RectCalib
            {
                RawMinX = 200, RawMaxX = 1800,
                RawMinY = 200, RawMaxY = 1800,
                ScreenW = 1920, ScreenH = 1080, InvertY = true
            };
            var file = CalibrationFile.FromRect(rect);

            Assert.Null(file.Homography);
            Assert.Empty(file.Points);

            var asked = file.MapNormalized(1000, 1000, CalibrationMode.Homography);
            var rectResult = rect.MapNormalized(1000, 1000);

            Assert.Equal(rectResult.X, asked.X, 9);
            Assert.Equal(rectResult.Y, asked.Y, 9);
        }

        [Fact]
        public void FromRect_RejectsNullRatherThanBuildingAnInstanceThatThrowsLater()
        {
            Assert.Throws<ArgumentNullException>(() => CalibrationFile.FromRect(null));
        }

        [Fact]
        public void FromCapture_LeavesTheHomographyNullWhenTheCornersAreDegenerate()
        {
            // Collinear corners: the four "corners" fall on one line, so no
            // homography can be fitted, but the bounding box is a real,
            // non-degenerate rectangle. This is exactly what Load returns for a
            // 5-point file whose corners will not fit, so it exercises a real
            // state rather than a contrived all-identical one where the rectangle
            // would also be invalid and both paths would trivially return (0,0).
            var collinear = new List<(double X, double Y)>
            {
                (200, 200), (600, 600), (1000, 1000), (1400, 1400), (800, 800)
            };

            var file = CalibrationFile.FromCapture(collinear, 1920, 1080);

            Assert.Null(file.Homography);
            Assert.True(file.Rect.IsValid());

            // Null Homography must still fall back to the rectangle rather than
            // throwing or returning nonsense — this is the upgrade path every
            // existing user takes until they recalibrate. The rectangle here is
            // valid, so this comparison is discriminating: a broken fallback
            // could not produce the same non-trivial value by accident.
            var asked = file.MapNormalized(1000, 1000, CalibrationMode.Homography);
            var viaRect = file.MapNormalized(1000, 1000, CalibrationMode.Rect);

            Assert.Equal(viaRect.X, asked.X, 9);
            Assert.Equal(viaRect.Y, asked.Y, 9);
            Assert.Equal(0.6666666666666666, asked.X, 9);
            Assert.Equal(0.33333333333333337, asked.Y, 9);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsThePointsAndTheRectangle()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var original = CalibrationFile.FromCapture(Keystone(), 1921, 1081);
                original.Save(path);

                var loaded = CalibrationFile.Load(path);

                Assert.NotNull(loaded);
                Assert.Equal(5, loaded.Points.Count);
                for (int i = 0; i < 5; i++)
                {
                    Assert.Equal(original.Points[i].X, loaded.Points[i].X, 6);
                    Assert.Equal(original.Points[i].Y, loaded.Points[i].Y, 6);
                }

                Assert.Equal(original.Rect.RawMinX, loaded.Rect.RawMinX);
                Assert.Equal(original.Rect.RawMaxX, loaded.Rect.RawMaxX);
                Assert.Equal(original.Rect.RawMinY, loaded.Rect.RawMinY);
                Assert.Equal(original.Rect.RawMaxY, loaded.Rect.RawMaxY);
                Assert.Equal(1921, loaded.Rect.ScreenW);
                Assert.Equal(1081, loaded.Rect.ScreenH);
                Assert.True(loaded.Rect.InvertY);
                Assert.NotNull(loaded.Homography);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Load_AFileWithoutPointsGivesARectangleAndNoHomography()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                // Exactly what every calibration written before this version looks like.
                File.WriteAllLines(path, new[]
                {
                    "RawMinX=200", "RawMaxX=1800",
                    "RawMinY=200", "RawMaxY=1800",
                    "ScreenW=1920", "ScreenH=1080", "InvertY=1"
                });

                var loaded = CalibrationFile.Load(path);

                Assert.NotNull(loaded);
                Assert.Empty(loaded.Points);
                Assert.Null(loaded.Homography);
                Assert.True(loaded.Rect.IsValid());
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Load_ReturnsNullForMissingFile()
        {
            // TryLoad short-circuits on its own File.Exists before ever calling Load,
            // so this exercises Load's own missing-file branch directly — deleting that
            // check would raise FileNotFoundException from File.ReadAllLines, which
            // nothing else asserts against.
            Assert.Null(CalibrationFile.Load(Path.Combine(Path.GetTempPath(), "no-such-" + Path.GetRandomFileName())));
        }

        /// <summary>
        /// Transcribed verbatim (behaviourally) from <c>20649ab:src/Guncon3.Core/RectCalib.cs</c>'s
        /// <c>Load</c>: a line-by-line loop skipping blanks and '#' comments, splitting on the
        /// first '=', a switch over exactly the seven rectangle keys with no default arm (so any
        /// key it does not recognise — including the P0..P4 point lines this build now writes —
        /// is silently ignored), then the same validity check. This is what a build older than
        /// this branch does when it reads a file this branch wrote.
        /// </summary>
        private static RectCalib LoadWithPreHomographyParser(string path)
        {
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

        [Fact]
        public void Save_WritesTheDerivedFieldsSoOlderBuildsCanStillReadIt()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var file = CalibrationFile.FromCapture(Keystone(), 1920, 1080);
                file.Save(path);

                // What an older build — one that never heard of P0..P4 or the homography —
                // recovers from a file this build wrote.
                var oldRect = LoadWithPreHomographyParser(path);

                Assert.NotNull(oldRect);
                Assert.Equal(file.Rect.RawMinX, oldRect.RawMinX);
                Assert.Equal(file.Rect.RawMaxX, oldRect.RawMaxX);
                Assert.Equal(file.Rect.RawMinY, oldRect.RawMinY);
                Assert.Equal(file.Rect.RawMaxY, oldRect.RawMaxY);
                Assert.Equal(file.Rect.ScreenW, oldRect.ScreenW);
                Assert.Equal(file.Rect.ScreenH, oldRect.ScreenH);
                Assert.Equal(file.Rect.InvertY, oldRect.InvertY);

                // Same mapping as the current Rect path would give, not merely the same fields.
                Assert.Equal(file.Rect.MapNormalized(0, 0), oldRect.MapNormalized(0, 0));
                Assert.Equal(file.Rect.MapNormalized(1000, 1000), oldRect.MapNormalized(1000, 1000));
                Assert.Equal(file.Rect.MapNormalized(1800, 400), oldRect.MapNormalized(1800, 400));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Load_RejectsTheWholeFileWhenAPointLineIsMalformed()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "P0=200,1800", "P1=1800,1600", "P2=notanumber,400",
                    "P3=200,200", "P4=1000,1000",
                    "RawMinX=200", "RawMaxX=1800",
                    "RawMinY=200", "RawMaxY=1800",
                    "ScreenW=1920", "ScreenH=1080", "InvertY=1"
                });

                Assert.Null(CalibrationFile.Load(path));
                Assert.Equal(CalibrationLoad.Malformed, CalibrationFile.TryLoad(path, 0, out var c));
                Assert.Null(c);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Load_RejectsNonFiniteCoordinates()
        {
            // NumberStyles.Float under the invariant culture accepts NaN/Infinity, and
            // HomographyCalib.MapNormalized's clamp (x < 0 / x > 1) is false for NaN, so a
            // non-finite value would otherwise escape to the caller as (NaN, NaN) rather
            // than being rejected the way the old format rejected RawMinX=NaN via IsValid().
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "P0=NaN,1800", "P1=1800,1600", "P2=1800,400",
                    "P3=200,200", "P4=1000,1000",
                    "RawMinX=200", "RawMaxX=1800",
                    "RawMinY=200", "RawMaxY=1800",
                    "ScreenW=1920", "ScreenH=1080", "InvertY=1"
                });

                Assert.Null(CalibrationFile.Load(path));
                Assert.Equal(CalibrationLoad.Malformed, CalibrationFile.TryLoad(path, 0, out var c));
                Assert.Null(c);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Load_RejectsAPartialSetOfPoints()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                // Four of five: a truncated write. Better rejected than half-fitted.
                File.WriteAllLines(path, new[]
                {
                    "P0=200,1800", "P1=1800,1600", "P2=1800,400", "P3=200,200",
                    "RawMinX=200", "RawMaxX=1800",
                    "RawMinY=200", "RawMaxY=1800",
                    "ScreenW=1920", "ScreenH=1080", "InvertY=1"
                });

                Assert.Equal(CalibrationLoad.Malformed, CalibrationFile.TryLoad(path, 0, out var c));
                Assert.Null(c);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Load_IgnoresCommentsAndBlankLines()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "# written by the calibration window",
                    "",
                    "  P0 = 200,1800  ",
                    "P1=1800,1600", "P2=1800,400", "P3=200,200", "P4=1000,1000",
                    "RawMinX=200", "  RawMaxX = 1800  ",
                    "RawMinY=200", "RawMaxY=1800",
                    "ScreenW=1920", "ScreenH=1080", "InvertY=1"
                });

                var loaded = CalibrationFile.Load(path);

                Assert.NotNull(loaded);
                Assert.Equal(200.0, loaded.Points[0].X, 6);
                Assert.Equal(1800.0, loaded.Rect.RawMaxX);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Load_UsesInvariantDecimalSeparators()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "P0=200.5,1800.25", "P1=1800,1600", "P2=1800,400",
                    "P3=200,200", "P4=1000,1000",
                    "RawMinX=200.5", "RawMaxX=1800",
                    "RawMinY=200", "RawMaxY=1800",
                    "ScreenW=1920", "ScreenH=1080", "InvertY=1"
                });

                var loaded = CalibrationFile.Load(path);

                Assert.NotNull(loaded);
                Assert.Equal(200.5, loaded.Points[0].X, 6);
                Assert.Equal(1800.25, loaded.Points[0].Y, 6);
                Assert.Equal(200.5, loaded.Rect.RawMinX);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Save_UsesInvariantDecimalSeparatorsRegardlessOfCurrentCulture()
        {
            // Load_UsesInvariantDecimalSeparators only exercises the read side, and every
            // Save fixture elsewhere in this file uses integral coordinates, so a Save-side
            // regression to CurrentCulture would change nothing those tests can see. This
            // pins the write side directly, the way StickCentresTests.Parsing_IsInvariantCulture
            // pins it for stick centres.
            var original = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var fractional = new List<(double X, double Y)>
                {
                    (200.5, 1800.25), (1800, 1600), (1800, 400), (200, 200), (1000, 1000)
                };

                CalibrationFile.FromCapture(fractional, 1920, 1080).Save(path);

                var text = File.ReadAllText(path);
                Assert.Contains("200.5,1800.25", text);
                Assert.DoesNotContain("200,5", text);

                var loaded = CalibrationFile.Load(path);
                Assert.NotNull(loaded);
                Assert.Equal(200.5, loaded.Points[0].X, 6);
                Assert.Equal(1800.25, loaded.Points[0].Y, 6);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
                File.Delete(path);
            }
        }

        [Fact]
        public void Save_RejectsAnInstanceWithAPartialPointCount()
        {
            // Save writes Points.Count P-lines; Load demands exactly 0 or PointCount (5).
            // FromCapture accepts any non-empty count, so without this guard a 1-, 3- or
            // 4-point capture would save a file its own Load then rejects as Malformed —
            // the calibration silently gone on the next restart. A file Load cannot read
            // back must never be written in the first place.
            var partial = CalibrationFile.FromCapture(
                new List<(double X, double Y)> { (200, 1800), (1800, 1600), (1800, 400), (200, 200) },
                1920, 1080);

            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                Assert.Throws<InvalidOperationException>(() => partial.Save(path));
                Assert.False(File.Exists(path), "a file that cannot be loaded must not be written at all");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void TryLoad_ReportsMissing()
        {
            var path = Path.Combine(Path.GetTempPath(), "absent-" + Path.GetRandomFileName());

            Assert.Equal(CalibrationLoad.Missing, CalibrationFile.TryLoad(path, 0, out var c));
            Assert.Null(c);
        }

        [Fact]
        public void TryLoad_ReportsMalformedForAnUnusableRectangle()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllLines(path, new[] { "RawMinX=100", "RawMaxX=50", "ScreenW=0" });

                Assert.Equal(CalibrationLoad.Malformed, CalibrationFile.TryLoad(path, 0, out var c));
                Assert.Null(c);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void TryLoad_ReportsOk()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                CalibrationFile.FromCapture(Keystone(), 1920, 1080).Save(path);

                Assert.Equal(CalibrationLoad.Ok, CalibrationFile.TryLoad(path, 0, out var c));
                Assert.NotNull(c);
                Assert.True(c.Rect.IsValid());
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void SaveAndTryLoad_AgreeOnTheDefaultPathForAGunIndex(int gunIndex)
        {
            CalibrationFile.FromCapture(Keystone(), 1920, 1080).Save(null, gunIndex);
            try
            {
                Assert.Equal(CalibrationLoad.Ok, CalibrationFile.TryLoad(null, gunIndex, out var loaded));
                Assert.NotNull(loaded);
                Assert.Equal(1800.0, loaded.Rect.RawMaxX);
            }
            finally
            {
                string suffix = gunIndex > 0 ? $"_{gunIndex + 1}" : "";
                File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"calibration_rect{suffix}.txt"));
            }
        }
    }
}
