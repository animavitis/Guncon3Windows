using System.Collections.Generic;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class StickDigitizerTests
    {
        [Fact]
        public void IsLow_AtBoundary_IsNotLow()
        {
            Assert.False(StickDigitizer.IsLow(128 - StickDigitizer.Deadzone, 128));
        }

        [Fact]
        public void IsLow_OneBelowBoundary_IsLow()
        {
            Assert.True(StickDigitizer.IsLow(128 - StickDigitizer.Deadzone - 1, 128));
        }

        [Fact]
        public void IsHigh_AtBoundary_IsNotHigh()
        {
            Assert.False(StickDigitizer.IsHigh(128 + StickDigitizer.Deadzone, 128));
        }

        [Fact]
        public void IsHigh_OneAboveBoundary_IsHigh()
        {
            Assert.True(StickDigitizer.IsHigh(128 + StickDigitizer.Deadzone + 1, 128));
        }

        [Fact]
        public void IsLowAndIsHigh_AtCentre_AreBothFalse()
        {
            Assert.False(StickDigitizer.IsLow(128, 128));
            Assert.False(StickDigitizer.IsHigh(128, 128));
        }

        [Fact]
        public void IsLow_WithOffCentreCentre_UsesThatCentre()
        {
            // centre 120: low boundary is 100 (not low), 99 is low
            Assert.False(StickDigitizer.IsLow(120 - StickDigitizer.Deadzone, 120));
            Assert.True(StickDigitizer.IsLow(120 - StickDigitizer.Deadzone - 1, 120));
        }

        [Fact]
        public void IsHigh_WithOffCentreCentre_UsesThatCentre()
        {
            // centre 120: high boundary is 140 (not high), 141 is high
            Assert.False(StickDigitizer.IsHigh(120 + StickDigitizer.Deadzone, 120));
            Assert.True(StickDigitizer.IsHigh(120 + StickDigitizer.Deadzone + 1, 120));
        }

        [Fact]
        public void IsPlausibleCentre_AtLowerBandEdge_IsTrue()
        {
            Assert.True(StickDigitizer.IsPlausibleCentre(StickDigitizer.MinPlausibleCentre));
        }

        [Fact]
        public void IsPlausibleCentre_OneBelowLowerBandEdge_IsFalse()
        {
            Assert.False(StickDigitizer.IsPlausibleCentre(StickDigitizer.MinPlausibleCentre - 1));
        }

        [Fact]
        public void IsPlausibleCentre_AtUpperBandEdge_IsTrue()
        {
            Assert.True(StickDigitizer.IsPlausibleCentre(StickDigitizer.MaxPlausibleCentre));
        }

        [Fact]
        public void IsPlausibleCentre_OneAboveUpperBandEdge_IsFalse()
        {
            Assert.False(StickDigitizer.IsPlausibleCentre(StickDigitizer.MaxPlausibleCentre + 1));
        }

        [Fact]
        public void EstimateCentre_EmptyList_ReturnsDefault()
        {
            Assert.Equal(StickDigitizer.DefaultCentre, StickDigitizer.EstimateCentre(new List<int>()));
        }

        [Fact]
        public void EstimateCentre_NullList_ReturnsDefault()
        {
            Assert.Equal(StickDigitizer.DefaultCentre, StickDigitizer.EstimateCentre(null));
        }

        [Fact]
        public void EstimateCentre_OddCount_ReturnsMedian()
        {
            var samples = new List<int> { 130, 120, 125 };
            Assert.Equal(125, StickDigitizer.EstimateCentre(samples));
        }

        [Fact]
        public void EstimateCentre_EvenCount_ReturnsUpperOfTheTwoMiddleValues()
        {
            // sorted: 120, 122, 124, 126 -> middle index Count/2 = 2 -> 124
            var samples = new List<int> { 126, 120, 124, 122 };
            Assert.Equal(124, StickDigitizer.EstimateCentre(samples));
        }

        [Fact]
        public void EstimateCentre_ImplausibleMedianAllHigh_FallsBackToDefault()
        {
            var samples = new List<int> { 255, 255, 255 };
            Assert.Equal(StickDigitizer.DefaultCentre, StickDigitizer.EstimateCentre(samples));
        }

        [Fact]
        public void EstimateCentre_ImplausibleMedianAllLow_FallsBackToDefault()
        {
            var samples = new List<int> { 0, 0, 0 };
            Assert.Equal(StickDigitizer.DefaultCentre, StickDigitizer.EstimateCentre(samples));
        }

        [Fact]
        public void EstimateCentre_DoesNotReorderTheCallersList()
        {
            var samples = new List<int> { 130, 120, 125 };
            StickDigitizer.EstimateCentre(samples);
            Assert.Equal(new[] { 130, 120, 125 }, samples);
        }

        [Fact]
        public void ToCentredAxis_AtCentre_ReturnsExactHalf()
        {
            Assert.Equal(StickDigitizer.AxisMax / 2, StickDigitizer.ToCentredAxis(128, 128));
        }

        [Fact]
        public void ToCentredAxis_AtLowExtreme_ReturnsZero()
        {
            Assert.Equal(0, StickDigitizer.ToCentredAxis(0, 128));
        }

        [Fact]
        public void ToCentredAxis_AtHighExtreme_ReturnsAxisMax()
        {
            Assert.Equal(StickDigitizer.AxisMax, StickDigitizer.ToCentredAxis(255, 128));
        }

        [Fact]
        public void ToCentredAxis_BelowLowExtreme_ClampsToZero()
        {
            Assert.Equal(0, StickDigitizer.ToCentredAxis(-50, 128));
        }

        [Fact]
        public void ToCentredAxis_AboveHighExtreme_ClampsToAxisMax()
        {
            Assert.Equal(StickDigitizer.AxisMax, StickDigitizer.ToCentredAxis(999, 128));
        }

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

        [Fact]
        public void ToCentredAxis_CentreOfZero_DoesNotThrowAndStaysInRange()
        {
            var ex = Record.Exception(() =>
            {
                for (int v = 0; v <= 255; v++)
                    Assert.InRange(StickDigitizer.ToCentredAxis(v, 0), 0, StickDigitizer.AxisMax);
            });
            Assert.Null(ex);
        }

        [Fact]
        public void ToCentredAxis_CentreOf255_DoesNotThrowAndStaysInRange()
        {
            var ex = Record.Exception(() =>
            {
                for (int v = 0; v <= 255; v++)
                    Assert.InRange(StickDigitizer.ToCentredAxis(v, 255), 0, StickDigitizer.AxisMax);
            });
            Assert.Null(ex);
        }

        [Fact]
        public void ToAxis_AtZero_ReturnsZero()
        {
            Assert.Equal(0, StickDigitizer.ToAxis(0));
        }

        [Fact]
        public void ToAxis_At255_ReturnsAxisMax()
        {
            Assert.Equal(StickDigitizer.AxisMax, StickDigitizer.ToAxis(255));
        }

        [Fact]
        public void ToAxis_BelowZero_ClampsToZero()
        {
            Assert.Equal(0, StickDigitizer.ToAxis(-100));
        }

        [Fact]
        public void ToAxis_Above255_ClampsToAxisMax()
        {
            Assert.Equal(StickDigitizer.AxisMax, StickDigitizer.ToAxis(400));
        }

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
