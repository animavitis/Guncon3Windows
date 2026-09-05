// SPDX-License-Identifier: GPL-2.0-only
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class ScreenPlacementTests
    {
        [Fact]
        public void ToDesktop_IsIdentityWhenTheScreenIsTheWholeDesktop()
        {
            var screen = new ScreenPlacement(0, 0, 1920, 1080);

            var (x, y) = screen.ToDesktop(0.25, 0.75, screen);

            Assert.Equal(0.25, x, 9);
            Assert.Equal(0.75, y, 9);
        }

        [Fact]
        public void ToDesktop_OffsetsAndScalesASecondaryScreenIntoTheDesktop()
        {
            // Primary 1920x1080 at the origin, secondary 1280x720 to its right,
            // top-aligned. Virtual desktop is the union: 3200 x 1080.
            var secondary = new ScreenPlacement(1920, 0, 1280, 720);
            var desktop = new ScreenPlacement(0, 0, 3200, 1080);

            var (x, y) = secondary.ToDesktop(0.5, 1.0, desktop);

            Assert.Equal((1920 + 640) / 3200.0, x, 9);
            Assert.Equal(720 / 1080.0, y, 9);
        }

        [Fact]
        public void ToDesktop_HandlesAScreenPlacedAtNegativeCoordinates()
        {
            // Secondary to the LEFT of primary: the desktop origin is negative.
            var secondary = new ScreenPlacement(-1280, 0, 1280, 720);
            var desktop = new ScreenPlacement(-1280, 0, 3200, 1080);

            var (x, y) = secondary.ToDesktop(0.0, 0.0, desktop);

            Assert.Equal(0.0, x, 9);
            Assert.Equal(0.0, y, 9);
        }

        [Fact]
        public void ToDesktop_ReturnsInputUnchangedWhenTheDesktopIsDegenerate()
        {
            var screen = new ScreenPlacement(0, 0, 1920, 1080);
            var desktop = new ScreenPlacement(0, 0, 0, 0);

            var (x, y) = screen.ToDesktop(0.3, 0.6, desktop);

            Assert.Equal(0.3, x, 9);
            Assert.Equal(0.6, y, 9);
        }

        [Fact]
        public void ToDesktop_ClampsAScreenThatSticksOutOfTheDesktop()
        {
            // A stale placement after the monitors were rearranged: the calibrated
            // screen now lies partly outside the desktop. The virtual mouse takes
            // 0..1 only, so the result must be clamped, not extrapolated.
            var stale = new ScreenPlacement(2000, 0, 1920, 1080);
            var desktop = new ScreenPlacement(0, 0, 1920, 1080);

            var (x, y) = stale.ToDesktop(0.5, 0.5, desktop);

            Assert.Equal(1.0, x, 9);
            Assert.Equal(0.5, y, 9);
        }

        [Fact]
        public void ToDesktop_TurnsANonFiniteInputIntoTheEdgeInsteadOfPassingItOn()
        {
            var screen = new ScreenPlacement(0, 0, 1920, 1080);

            var (x, y) = screen.ToDesktop(double.NaN, double.PositiveInfinity, screen);

            Assert.True(double.IsFinite(x));
            Assert.True(double.IsFinite(y));
            Assert.InRange(x, 0.0, 1.0);
            Assert.InRange(y, 0.0, 1.0);
        }

        [Fact]
        public void IsValid_RequiresPositiveSize()
        {
            Assert.True(new ScreenPlacement(0, 0, 1, 1).IsValid);
            Assert.True(new ScreenPlacement(-100, -50, 1, 1).IsValid);
            Assert.False(new ScreenPlacement(0, 0, 0, 1).IsValid);
            Assert.False(new ScreenPlacement(0, 0, 1, 0).IsValid);
        }
    }
}
