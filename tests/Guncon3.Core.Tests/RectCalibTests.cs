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
        public void IsValid_RejectsNonFiniteRawFields()
        {
            Assert.False(new RectCalib { RawMinX = double.NegativeInfinity, RawMaxX = double.PositiveInfinity, RawMinY = 0, RawMaxY = 1, ScreenW = 1, ScreenH = 1 }.IsValid());
            Assert.False(new RectCalib { RawMinX = 0, RawMaxX = 1, RawMinY = double.NegativeInfinity, RawMaxY = double.PositiveInfinity, ScreenW = 1, ScreenH = 1 }.IsValid());
            Assert.False(new RectCalib { RawMinX = double.NaN, RawMaxX = 1, RawMinY = 0, RawMaxY = 1, ScreenW = 1, ScreenH = 1 }.IsValid());
        }

        [Fact]
        public void MapNormalized_ReturnsOriginForAnInfiniteRectRatherThanNaN()
        {
            var rc = new RectCalib { RawMinX = double.NegativeInfinity, RawMaxX = double.PositiveInfinity, RawMinY = 0, RawMaxY = 1, ScreenW = 1920, ScreenH = 1080 };

            var mapped = rc.MapNormalized(0, 0);

            Assert.Equal((0.0, 0.0), mapped);
        }

        [Fact]
        public void IsValid_AcceptsHardwareScaleRawRanges()
        {
            // From tests/Guncon3.Core.Tests/data/packets.txt: X roughly [-10142, 4652],
            // Y [-5728, 4860], signed. The finiteness clause must not reject this.
            var rc = new RectCalib { RawMinX = -10142, RawMaxX = 4652, RawMinY = -5728, RawMaxY = 4860, ScreenW = 1920, ScreenH = 1080, InvertY = true };

            Assert.True(rc.IsValid());
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

    }
}
