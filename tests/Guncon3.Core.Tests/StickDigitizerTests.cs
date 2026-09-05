// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class StickDigitizerTests
    {
        [Theory]
        [InlineData(128, 128 - StickDigitizer.Deadzone, false)]
        [InlineData(128, 128 - StickDigitizer.Deadzone - 1, true)]
        [InlineData(120, 120 - StickDigitizer.Deadzone, false)]
        [InlineData(120, 120 - StickDigitizer.Deadzone - 1, true)]
        [InlineData(128, 128, false)]
        public void IsLow_IsTrueOnlyMoreThanTheDeadzoneBelowTheCentre(int centre, int value, bool expected)
            => Assert.Equal(expected, StickDigitizer.IsLow(value, centre));

        [Theory]
        [InlineData(128, 128 + StickDigitizer.Deadzone, false)]
        [InlineData(128, 128 + StickDigitizer.Deadzone + 1, true)]
        [InlineData(120, 120 + StickDigitizer.Deadzone, false)]
        [InlineData(120, 120 + StickDigitizer.Deadzone + 1, true)]
        [InlineData(128, 128, false)]
        public void IsHigh_IsTrueOnlyMoreThanTheDeadzoneAboveTheCentre(int centre, int value, bool expected)
            => Assert.Equal(expected, StickDigitizer.IsHigh(value, centre));

        [Theory]
        [InlineData(StickDigitizer.MinPlausibleCentre, true)]
        [InlineData(StickDigitizer.MinPlausibleCentre - 1, false)]
        [InlineData(StickDigitizer.MaxPlausibleCentre, true)]
        [InlineData(StickDigitizer.MaxPlausibleCentre + 1, false)]
        public void IsPlausibleCentre_AcceptsTheBandIncludingBothEdges(int centre, bool expected)
            => Assert.Equal(expected, StickDigitizer.IsPlausibleCentre(centre));

        [Theory]
        [InlineData(null, StickDigitizer.DefaultCentre)]
        [InlineData(new int[] { }, StickDigitizer.DefaultCentre)]
        [InlineData(new[] { 130, 120, 125 }, 125)]
        [InlineData(new[] { 126, 120, 124, 122 }, 124)]
        [InlineData(new[] { 255, 255, 255 }, StickDigitizer.DefaultCentre)]
        [InlineData(new[] { 0, 0, 0 }, StickDigitizer.DefaultCentre)]
        public void EstimateCentre_IsTheMedianWhenPlausibleAndTheDefaultOtherwise(int[]? samples, int expected)
            => Assert.Equal(expected, StickDigitizer.EstimateCentre(samples));

        [Fact]
        public void EstimateCentre_DoesNotReorderTheCallersList()
        {
            var samples = new List<int> { 130, 120, 125 };
            StickDigitizer.EstimateCentre(samples);
            Assert.Equal(new[] { 130, 120, 125 }, samples);
        }

        [Theory]
        [InlineData(128, 128, StickDigitizer.AxisMax / 2)]
        [InlineData(0, 128, 0)]
        [InlineData(255, 128, StickDigitizer.AxisMax)]
        [InlineData(-50, 128, 0)]
        [InlineData(999, 128, StickDigitizer.AxisMax)]
        public void ToCentredAxis_PlacesTheEndsAtTheEndsAndClampsBeyondThem(int value, int centre, int expected)
            => Assert.Equal(expected, StickDigitizer.ToCentredAxis(value, centre));

        [Theory]
        [InlineData(110)]
        [InlineData(150)]
        public void ToCentredAxis_OffCentreCentre_BothHalvesStillReachTheExtremes(int centre)
        {
            Assert.Equal(0, StickDigitizer.ToCentredAxis(0, centre));
            Assert.Equal(StickDigitizer.AxisMax, StickDigitizer.ToCentredAxis(255, centre));
            Assert.Equal(StickDigitizer.AxisMax / 2, StickDigitizer.ToCentredAxis(centre, centre));
        }

        [Theory]
        [InlineData(96)]
        [InlineData(110)]
        [InlineData(128)]
        [InlineData(150)]
        [InlineData(160)]
        public void ToCentredAxis_IsMonotonicNonDecreasing_AcrossFullRange(int centre)
        {
            ushort previous = StickDigitizer.ToCentredAxis(0, centre);
            for (int v = 1; v <= 255; v++)
            {
                ushort current = StickDigitizer.ToCentredAxis(v, centre);
                Assert.True(current >= previous, $"value {v}: {current} < {previous} (centre {centre})");
                Assert.InRange(current, 0, StickDigitizer.AxisMax);
                previous = current;
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(255)]
        public void ToCentredAxis_AnImplausibleCentre_StaysInRangeAcrossTheWholeInput(int centre)
        {
            for (int v = 0; v <= 255; v++)
                Assert.InRange(StickDigitizer.ToCentredAxis(v, centre), 0, StickDigitizer.AxisMax);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(255, StickDigitizer.AxisMax)]
        [InlineData(-100, 0)]
        [InlineData(400, StickDigitizer.AxisMax)]
        public void ToAxis_MapsTheEndsAndClampsBeyondThem(int value, int expected)
            => Assert.Equal(expected, StickDigitizer.ToAxis(value));

        [Fact]
        public void ToAxis_IsMonotonicNonDecreasing_AcrossFullRange()
        {
            ushort previous = StickDigitizer.ToAxis(0);
            for (int v = 1; v <= 255; v++)
            {
                ushort current = StickDigitizer.ToAxis(v);
                Assert.True(current >= previous, $"value {v}: {current} < {previous}");
                previous = current;
            }
        }
    }
}
