// SPDX-License-Identifier: GPL-2.0-only
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class RectCalibTests
    {
        private static RectCalib Sample(bool invertY = false, int screenW = 1921, int screenH = 1081) => new RectCalib
        {
            RawMinX = 100, RawMaxX = 1100,
            RawMinY = 200, RawMaxY = 1200,
            ScreenW = screenW, ScreenH = screenH,
            InvertY = invertY
        };

        [Theory]
        // both ranges and both screen dimensions must be positive
        [InlineData(100, 1100, 200, 1200, 1921, 1081, true)]
        [InlineData(5, 5, 0, 1, 1, 1, false)]
        [InlineData(0, 1, 0, 1, 0, 1, false)]
        // ...and every raw field finite
        [InlineData(double.NegativeInfinity, double.PositiveInfinity, 0, 1, 1, 1, false)]
        [InlineData(0, 1, double.NegativeInfinity, double.PositiveInfinity, 1, 1, false)]
        [InlineData(double.NaN, 1, 0, 1, 1, 1, false)]
        // ...without rejecting real hardware ranges: from data/packets.txt, X is roughly
        // [-10142, 4652] and Y [-5728, 4860], signed.
        [InlineData(-10142, 4652, -5728, 4860, 1920, 1080, true)]
        public void IsValid_RequiresFinitePositiveRangesAndAPositiveScreen(
            double rawMinX, double rawMaxX, double rawMinY, double rawMaxY, int screenW, int screenH, bool expected)
        {
            var rc = new RectCalib
            {
                RawMinX = rawMinX, RawMaxX = rawMaxX, RawMinY = rawMinY, RawMaxY = rawMaxY,
                ScreenW = screenW, ScreenH = screenH
            };

            Assert.Equal(expected, rc.IsValid());
        }

        [Theory]
        [InlineData(false, 100, 200, 0.0, 0.0)]
        [InlineData(false, 1100, 1200, 1.0, 1.0)]
        [InlineData(false, 600, 700, 0.5, 0.5)]
        [InlineData(false, -5000, -5000, 0.0, 0.0)]
        [InlineData(false, 99999, 99999, 1.0, 1.0)]
        [InlineData(true, 100, 200, 0.0, 1.0)]
        [InlineData(true, 100, 1200, 0.0, 0.0)]
        public void MapNormalized_PlacesTheRectangleOnTheUnitSquareAndClampsOutside(
            bool invertY, double rawX, double rawY, double expectedX, double expectedY)
        {
            var mapped = Sample(invertY: invertY).MapNormalized(rawX, rawY);

            Assert.Equal(expectedX, mapped.X, 6);
            Assert.Equal(expectedY, mapped.Y, 6);
        }

        [Fact]
        public void MapNormalized_ReturnsOriginForAnInfiniteRectRatherThanNaN()
        {
            var rc = new RectCalib { RawMinX = double.NegativeInfinity, RawMaxX = double.PositiveInfinity, RawMinY = 0, RawMaxY = 1, ScreenW = 1920, ScreenH = 1080 };

            Assert.Equal((0.0, 0.0), rc.MapNormalized(0, 0));
        }

        [Fact]
        public void MapNormalized_ReturnsOriginWhenInvalid()
            => Assert.Equal((0.0, 0.0), new RectCalib().MapNormalized(500, 500));

        [Fact]
        public void MapNormalized_IsIndependentOfStoredScreenSize()
            => Assert.Equal(Sample().MapNormalized(600, 700), Sample(screenW: 640, screenH: 480).MapNormalized(600, 700));

        [Fact]
        public void Properties_AreInitOnly()
        {
            // The worker reads a RectCalib every frame from another thread; a setter would
            // let the main thread change it mid-map. Reflection is the only way to assert
            // "no setter" without writing code that must not compile.
            foreach (var p in typeof(RectCalib).GetProperties())
            {
                var setter = p.GetSetMethod();
                Assert.NotNull(setter);
                Assert.Contains(setter.ReturnParameter.GetRequiredCustomModifiers(),
                    m => m == typeof(System.Runtime.CompilerServices.IsExternalInit));
            }
            Assert.True(typeof(RectCalib).IsSealed);
        }
    }
}
