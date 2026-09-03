using System;
using System.IO;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class RectCalibTests
    {
        private static RectCalib Sample() => new RectCalib
        {
            RawMinX = 100, RawMaxX = 1100,
            RawMinY = 200, RawMaxY = 1200,
            ScreenW = 1921, ScreenH = 1081,
            InvertY = false
        };

        [Fact]
        public void IsValid_RequiresPositiveRangesAndScreen()
        {
            Assert.True(Sample().IsValid());
            Assert.False(new RectCalib { RawMinX = 5, RawMaxX = 5, RawMinY = 0, RawMaxY = 1, ScreenW = 1, ScreenH = 1 }.IsValid());
            Assert.False(new RectCalib { RawMinX = 0, RawMaxX = 1, RawMinY = 0, RawMaxY = 1, ScreenW = 0, ScreenH = 1 }.IsValid());
        }

        [Fact]
        public void MapNormalized_PlacesCornersAtZeroAndOne()
        {
            var rc = Sample();

            var topLeft = rc.MapNormalized(100, 200);
            Assert.Equal(0.0, topLeft.X, 6);
            Assert.Equal(0.0, topLeft.Y, 6);

            var bottomRight = rc.MapNormalized(1100, 1200);
            Assert.Equal(1.0, bottomRight.X, 6);
            Assert.Equal(1.0, bottomRight.Y, 6);
        }

        [Fact]
        public void MapNormalized_PlacesCentreAtHalf()
        {
            var rc = Sample();

            var centre = rc.MapNormalized(600, 700);
            Assert.Equal(0.5, centre.X, 6);
            Assert.Equal(0.5, centre.Y, 6);
        }

        [Fact]
        public void MapNormalized_ClampsOutOfRangeInput()
        {
            var rc = Sample();

            Assert.Equal(0.0, rc.MapNormalized(-5000, -5000).X, 6);
            Assert.Equal(1.0, rc.MapNormalized(99999, 99999).X, 6);
            Assert.Equal(1.0, rc.MapNormalized(99999, 99999).Y, 6);
        }

        [Fact]
        public void MapNormalized_InvertYFlipsVerticalAxis()
        {
            var rc = Sample();
            rc.InvertY = true;

            Assert.Equal(1.0, rc.MapNormalized(100, 200).Y, 6);
            Assert.Equal(0.0, rc.MapNormalized(100, 1200).Y, 6);
        }

        [Fact]
        public void MapNormalized_IsIndependentOfStoredScreenSize()
        {
            var a = Sample();
            var b = Sample();
            b.ScreenW = 640;
            b.ScreenH = 480;

            Assert.Equal(a.MapNormalized(600, 700), b.MapNormalized(600, 700));
        }

        [Fact]
        public void MapNormalized_ReturnsOriginWhenInvalid()
        {
            var rc = new RectCalib();
            Assert.Equal((0.0, 0.0), rc.MapNormalized(500, 500));
        }

        [Fact]
        public void SaveThenLoad_RoundTripsEveryField()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var original = Sample();
                original.InvertY = true;
                original.Save(path);

                var loaded = RectCalib.Load(path);

                Assert.NotNull(loaded);
                Assert.Equal(original.RawMinX, loaded.RawMinX);
                Assert.Equal(original.RawMaxX, loaded.RawMaxX);
                Assert.Equal(original.RawMinY, loaded.RawMinY);
                Assert.Equal(original.RawMaxY, loaded.RawMaxY);
                Assert.Equal(original.ScreenW, loaded.ScreenW);
                Assert.Equal(original.ScreenH, loaded.ScreenH);
                Assert.True(loaded.InvertY);
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
                    "RawMinX=100",
                    "  RawMaxX = 1100  ",
                    "RawMinY=200",
                    "RawMaxY=1200",
                    "ScreenW=1921",
                    "ScreenH=1081",
                    "InvertY=1"
                });

                var loaded = RectCalib.Load(path);

                Assert.NotNull(loaded);
                Assert.Equal(1100, loaded.RawMaxX);
                Assert.True(loaded.InvertY);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Load_ReturnsNullForMissingFile()
        {
            Assert.Null(RectCalib.Load(Path.Combine(Path.GetTempPath(), "no-such-" + Path.GetRandomFileName())));
        }

        [Fact]
        public void Load_UsesInvariantDecimalSeparator()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "RawMinX=100.5", "RawMaxX=1100.25",
                    "RawMinY=200", "RawMaxY=1200",
                    "ScreenW=1921", "ScreenH=1081", "InvertY=0"
                });

                var loaded = RectCalib.Load(path);

                Assert.NotNull(loaded);
                Assert.Equal(100.5, loaded.RawMinX);
                Assert.Equal(1100.25, loaded.RawMaxX);
            }
            finally { File.Delete(path); }
        }
    }
}
