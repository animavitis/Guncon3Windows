// SPDX-License-Identifier: GPL-2.0-only
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class DepthDigitizerTests
    {
        private const int Threshold = 1000;

        [Fact]
        public void IsLow_FiresBelowTheDeadband()
        {
            Assert.True(DepthDigitizer.IsLow(Threshold - DepthDigitizer.Deadband - 1, Threshold));
            Assert.False(DepthDigitizer.IsLow(Threshold - DepthDigitizer.Deadband, Threshold));
        }

        [Fact]
        public void IsHigh_FiresAboveTheDeadband()
        {
            Assert.True(DepthDigitizer.IsHigh(Threshold + DepthDigitizer.Deadband + 1, Threshold));
            Assert.False(DepthDigitizer.IsHigh(Threshold + DepthDigitizer.Deadband, Threshold));
        }

        /// <summary>The point of the band: inside it neither direction is held, so a reading that wobbles
        /// around the threshold does not turn a bound key on and off.</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(DepthDigitizer.Deadband)]
        [InlineData(-DepthDigitizer.Deadband)]
        public void NeitherDirectionFiresInsideTheBand(int offset)
        {
            int z = Threshold + offset;

            Assert.False(DepthDigitizer.IsLow(z, Threshold));
            Assert.False(DepthDigitizer.IsHigh(z, Threshold));
        }

        [Fact]
        public void TheTwoDirectionsAreNeverBothOn()
        {
            for (int z = 0; z <= 2 * Threshold; z += 7)
                Assert.False(DepthDigitizer.IsLow(z, Threshold) && DepthDigitizer.IsHigh(z, Threshold));
        }

        [Theory]
        [InlineData(DepthDigitizer.MinThreshold)]
        [InlineData(DepthDigitizer.MaxThreshold)]
        [InlineData(DepthDigitizer.DefaultThreshold)]
        public void PlausibleThresholdsAreKept(int threshold)
        {
            Assert.True(DepthDigitizer.IsPlausibleThreshold(threshold));
            Assert.Equal(threshold, DepthDigitizer.Clamp(threshold));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        [InlineData(DepthDigitizer.MaxThreshold + 1)]
        [InlineData(int.MaxValue)]
        public void ImplausibleThresholdsFallBackToTheDefault(int threshold)
        {
            Assert.False(DepthDigitizer.IsPlausibleThreshold(threshold));
            Assert.Equal(DepthDigitizer.DefaultThreshold, DepthDigitizer.Clamp(threshold));
        }
    }
}
