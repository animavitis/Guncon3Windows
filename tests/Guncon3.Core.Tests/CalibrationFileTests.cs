// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    // Both classes write real files under AppDomain.CurrentDomain.BaseDirectory through
    // the default-path API. xunit runs collections one at a time, so they cannot race.
    [Collection("default-paths")]
    public class CalibrationFileTests
    {
        [Fact]
        public void FromCapture_DerivesTheRectangleFromThePointsBoundingBox()
        {
            var file = CalibrationFile.FromCapture(Fixtures.Keystone(), 1920, 1080);

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
            var file = CalibrationFile.FromCapture(Fixtures.Keystone(), 1920, 1080);

            Assert.Equal(5, file.Points.Count);
            Assert.Equal((200.0, 1800.0), file.Points[0]);
            Assert.NotNull(file.Homography);
        }

        [Fact]
        public void MapNormalized_RectModeAgreesWithTheRectangleAlone()
        {
            var file = CalibrationFile.FromCapture(Fixtures.Keystone(), 1920, 1080);

            var viaFile = file.MapNormalized(1000, 1000, CalibrationMode.Rect);
            var viaRect = file.Rect.MapNormalized(1000, 1000);

            Assert.Equal(viaRect.X, viaFile.X, 9);
            Assert.Equal(viaRect.Y, viaFile.Y, 9);
        }

        [Fact]
        public void MapNormalized_HomographyModeAgreesWithTheHomographyAlone()
        {
            var file = CalibrationFile.FromCapture(Fixtures.Keystone(), 1920, 1080);

            var viaFile = file.MapNormalized(1000, 1000, CalibrationMode.Homography);
            Assert.NotNull(file.Homography);
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
            Assert.Throws<ArgumentNullException>(() => CalibrationFile.FromRect(null!));
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
            using var file = new TempFile();

            var original = CalibrationFile.FromCapture(Fixtures.Keystone(), 1921, 1081);
            original.Save(file.Path);

            var loaded = CalibrationFile.Load(file.Path);

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

        [Fact]
        public void Load_AFileWithoutPointsGivesARectangleAndNoHomography()
        {
            using var file = new TempFile();
            // Exactly what every calibration written before this version looks like.
            File.WriteAllLines(file.Path, new[]
            {
                "RawMinX=200", "RawMaxX=1800",
                "RawMinY=200", "RawMaxY=1800",
                "ScreenW=1920", "ScreenH=1080", "InvertY=1"
            });

            var loaded = CalibrationFile.Load(file.Path);

            Assert.NotNull(loaded);
            Assert.Empty(loaded.Points);
            Assert.Null(loaded.Homography);
            Assert.True(loaded.Rect.IsValid());
        }

        [Fact]
        public void Load_ReturnsNullForMissingFile()
        {
            // TryLoad short-circuits on its own File.Exists before ever calling Load,
            // so this exercises Load's own missing-file branch directly — deleting that
            // check would raise FileNotFoundException from File.ReadAllLines, which
            // nothing else asserts against.
            using var file = new TempFile("no-such-");
            Assert.Null(CalibrationFile.Load(file.Path));
        }

        /// <summary>
        /// Transcribed verbatim (behaviourally) from <c>20649ab:src/Guncon3.Core/RectCalib.cs</c>'s
        /// <c>Load</c>: a line-by-line loop skipping blanks and '#' comments, splitting on the
        /// first '=', a switch over exactly the seven rectangle keys with no default arm (so any
        /// key it does not recognise — including the P0..P4 point lines this build now writes —
        /// is silently ignored), then the same validity check. This is what a build older than
        /// this branch does when it reads a file this branch wrote.
        /// </summary>
        private static RectCalib? LoadWithPreHomographyParser(string path)
        {
            double rawMinX = 0, rawMaxX = 0, rawMinY = 0, rawMaxY = 0;
            int screenW = 0, screenH = 0;
            bool invertY = false;
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

                switch (key)
                {
                    case "RawMinX": if (double.TryParse(val, NumberStyles.Float, ci, out var rminx)) rawMinX = rminx; break;
                    case "RawMaxX": if (double.TryParse(val, NumberStyles.Float, ci, out var rmaxx)) rawMaxX = rmaxx; break;
                    case "RawMinY": if (double.TryParse(val, NumberStyles.Float, ci, out var rminy)) rawMinY = rminy; break;
                    case "RawMaxY": if (double.TryParse(val, NumberStyles.Float, ci, out var rmaxy)) rawMaxY = rmaxy; break;
                    case "ScreenW": if (int.TryParse(val, NumberStyles.Integer, ci, out var sw)) screenW = sw; break;
                    case "ScreenH": if (int.TryParse(val, NumberStyles.Integer, ci, out var sh)) screenH = sh; break;
                    case "InvertY": invertY = (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)); break;
                }
            }

            var rc = new RectCalib { RawMinX = rawMinX, RawMaxX = rawMaxX, RawMinY = rawMinY, RawMaxY = rawMaxY, ScreenW = screenW, ScreenH = screenH, InvertY = invertY };
            return rc.IsValid() ? rc : null;
        }

        [Fact]
        public void Save_WritesTheDerivedFieldsSoOlderBuildsCanStillReadIt()
        {
            using var file = new TempFile();

            var calFile = CalibrationFile.FromCapture(Fixtures.Keystone(), 1920, 1080);
            calFile.Save(file.Path);

            // What an older build — one that never heard of P0..P4 or the homography —
            // recovers from a file this build wrote.
            var oldRect = LoadWithPreHomographyParser(file.Path);

            Assert.NotNull(oldRect);
            Assert.Equal(calFile.Rect.RawMinX, oldRect.RawMinX);
            Assert.Equal(calFile.Rect.RawMaxX, oldRect.RawMaxX);
            Assert.Equal(calFile.Rect.RawMinY, oldRect.RawMinY);
            Assert.Equal(calFile.Rect.RawMaxY, oldRect.RawMaxY);
            Assert.Equal(calFile.Rect.ScreenW, oldRect.ScreenW);
            Assert.Equal(calFile.Rect.ScreenH, oldRect.ScreenH);
            Assert.Equal(calFile.Rect.InvertY, oldRect.InvertY);

            // Same mapping as the current Rect path would give, not merely the same fields.
            Assert.Equal(calFile.Rect.MapNormalized(0, 0), oldRect.MapNormalized(0, 0));
            Assert.Equal(calFile.Rect.MapNormalized(1000, 1000), oldRect.MapNormalized(1000, 1000));
            Assert.Equal(calFile.Rect.MapNormalized(1800, 400), oldRect.MapNormalized(1800, 400));
        }

        [Fact]
        public void Load_RejectsTheWholeFileWhenAPointLineIsMalformed()
        {
            using var file = new TempFile();
            File.WriteAllLines(file.Path, new[]
            {
                "P0=200,1800", "P1=1800,1600", "P2=notanumber,400",
                "P3=200,200", "P4=1000,1000",
                "RawMinX=200", "RawMaxX=1800",
                "RawMinY=200", "RawMaxY=1800",
                "ScreenW=1920", "ScreenH=1080", "InvertY=1"
            });

            Assert.Null(CalibrationFile.Load(file.Path));
            Assert.Equal(CalibrationLoad.Malformed, CalibrationFile.TryLoad(file.Path, 0, out var c));
            Assert.Null(c);
        }

        [Fact]
        public void Load_RejectsNonFiniteCoordinates()
        {
            // NumberStyles.Float under the invariant culture accepts NaN/Infinity, and
            // HomographyCalib.MapNormalized's clamp (x < 0 / x > 1) is false for NaN, so a
            // non-finite value would otherwise escape to the caller as (NaN, NaN) rather
            // than being rejected the way the old format rejected RawMinX=NaN via IsValid().
            using var file = new TempFile();
            File.WriteAllLines(file.Path, new[]
            {
                "P0=NaN,1800", "P1=1800,1600", "P2=1800,400",
                "P3=200,200", "P4=1000,1000",
                "RawMinX=200", "RawMaxX=1800",
                "RawMinY=200", "RawMaxY=1800",
                "ScreenW=1920", "ScreenH=1080", "InvertY=1"
            });

            Assert.Null(CalibrationFile.Load(file.Path));
            Assert.Equal(CalibrationLoad.Malformed, CalibrationFile.TryLoad(file.Path, 0, out var c));
            Assert.Null(c);
        }

        [Fact]
        public void Load_RejectsAPartialSetOfPoints()
        {
            using var file = new TempFile();
            // Four of five: a truncated write. Better rejected than half-fitted.
            File.WriteAllLines(file.Path, new[]
            {
                "P0=200,1800", "P1=1800,1600", "P2=1800,400", "P3=200,200",
                "RawMinX=200", "RawMaxX=1800",
                "RawMinY=200", "RawMaxY=1800",
                "ScreenW=1920", "ScreenH=1080", "InvertY=1"
            });

            Assert.Equal(CalibrationLoad.Malformed, CalibrationFile.TryLoad(file.Path, 0, out var c));
            Assert.Null(c);
        }

        [Fact]
        public void Load_IgnoresCommentsAndBlankLines()
        {
            using var file = new TempFile();
            File.WriteAllLines(file.Path, new[]
            {
                "# written by the calibration window",
                "",
                "  P0 = 200,1800  ",
                "P1=1800,1600", "P2=1800,400", "P3=200,200", "P4=1000,1000",
                "RawMinX=200", "  RawMaxX = 1800  ",
                "RawMinY=200", "RawMaxY=1800",
                "ScreenW=1920", "ScreenH=1080", "InvertY=1"
            });

            var loaded = CalibrationFile.Load(file.Path);

            Assert.NotNull(loaded);
            Assert.Equal(200.0, loaded.Points[0].X, 6);
            Assert.Equal(1800.0, loaded.Rect.RawMaxX);
        }

        [Fact]
        public void Load_UsesInvariantDecimalSeparators()
        {
            using var file = new TempFile();
            File.WriteAllLines(file.Path, new[]
            {
                "P0=200.5,1800.25", "P1=1800,1600", "P2=1800,400",
                "P3=200,200", "P4=1000,1000",
                "RawMinX=200.5", "RawMaxX=1800",
                "RawMinY=200", "RawMaxY=1800",
                "ScreenW=1920", "ScreenH=1080", "InvertY=1"
            });

            var loaded = CalibrationFile.Load(file.Path);

            Assert.NotNull(loaded);
            Assert.Equal(200.5, loaded.Points[0].X, 6);
            Assert.Equal(1800.25, loaded.Points[0].Y, 6);
            Assert.Equal(200.5, loaded.Rect.RawMinX);
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
            using var file = new TempFile();
            try
            {
                var fractional = new List<(double X, double Y)>
                {
                    (200.5, 1800.25), (1800, 1600), (1800, 400), (200, 200), (1000, 1000)
                };

                CalibrationFile.FromCapture(fractional, 1920, 1080).Save(file.Path);

                var text = File.ReadAllText(file.Path);
                Assert.Contains("200.5,1800.25", text);
                Assert.DoesNotContain("200,5", text);

                var loaded = CalibrationFile.Load(file.Path);
                Assert.NotNull(loaded);
                Assert.Equal(200.5, loaded.Points[0].X, 6);
                Assert.Equal(1800.25, loaded.Points[0].Y, 6);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
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

            using var file = new TempFile();

            Assert.Throws<InvalidOperationException>(() => partial.Save(file.Path));
            Assert.False(File.Exists(file.Path), "a file that cannot be loaded must not be written at all");
        }

        [Fact]
        public void TryLoad_ReportsMissing()
        {
            using var file = new TempFile("absent-");

            Assert.Equal(CalibrationLoad.Missing, CalibrationFile.TryLoad(file.Path, 0, out var c));
            Assert.Null(c);
        }

        [Fact]
        public void TryLoad_ReportsMalformedForAnUnusableRectangle()
        {
            using var file = new TempFile();
            File.WriteAllLines(file.Path, new[] { "RawMinX=100", "RawMaxX=50", "ScreenW=0" });

            Assert.Equal(CalibrationLoad.Malformed, CalibrationFile.TryLoad(file.Path, 0, out var c));
            Assert.Null(c);
        }

        [Fact]
        public void TryLoad_ReportsOk()
        {
            using var file = new TempFile();
            CalibrationFile.FromCapture(Fixtures.Keystone(), 1920, 1080).Save(file.Path);

            Assert.Equal(CalibrationLoad.Ok, CalibrationFile.TryLoad(file.Path, 0, out var c));
            Assert.NotNull(c);
            Assert.True(c.Rect.IsValid());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void SaveAndTryLoad_AgreeOnTheDefaultPathForAGunIndex(int gunIndex)
        {
            CalibrationFile.FromCapture(Fixtures.Keystone(), 1920, 1080).Save(null, gunIndex);
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
