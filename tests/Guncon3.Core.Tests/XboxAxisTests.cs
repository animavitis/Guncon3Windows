// SPDX-License-Identifier: GPL-2.0-only
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class XboxAxisTests
    {
        [Theory]
        [InlineData(0, -32767)]
        [InlineData(16383, -1)]        // the resting centre ToCentredAxis produces
        [InlineData(16384, 1)]
        [InlineData(32767, 32767)]
        public void ToThumb_SpansTheSignedRange(int axis, int expected)
            => Assert.Equal((short)expected, XboxAxis.ToThumb((ushort)axis));

        [Theory]
        [InlineData(0, 32767)]         // the gun says "up" as a low value; XInput says it as a high one
        [InlineData(16383, 1)]
        [InlineData(32767, -32767)]
        public void ToThumbInverted_FlipsTheSign(int axis, int expected)
            => Assert.Equal((short)expected, XboxAxis.ToThumbInverted((ushort)axis));

        [Theory]
        [InlineData(0, 0)]
        [InlineData(16383, 127)]
        [InlineData(32767, 255)]
        public void ToTrigger_SpansTheByte(int axis, int expected)
            => Assert.Equal((byte)expected, XboxAxis.ToTrigger((ushort)axis));

        [Fact]
        public void ValuesAboveAxisMax_AreClamped()
        {
            Assert.Equal((short)32767, XboxAxis.ToThumb(65535));
            Assert.Equal((short)-32767, XboxAxis.ToThumbInverted(65535));
            Assert.Equal((byte)255, XboxAxis.ToTrigger(65535));
        }

        [Fact]
        public void ThumbMapping_IsMonotonic()
        {
            short previous = short.MinValue;
            for (int v = 0; v <= StickDigitizer.AxisMax; v += 97)
            {
                short current = XboxAxis.ToThumb((ushort)v);
                Assert.True(current > previous, $"not increasing at {v}");
                previous = current;
            }
        }
    }
}
