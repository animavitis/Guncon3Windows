// SPDX-License-Identifier: GPL-2.0-only
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class MouseEventFlagsTests
    {
        [Fact]
        public void Constants_AreTheWin32Values()
        {
            Assert.Equal(0x0001u, MouseEventFlags.Move);
            Assert.Equal(0x0002u, MouseEventFlags.LeftDown);
            Assert.Equal(0x0004u, MouseEventFlags.LeftUp);
            Assert.Equal(0x0008u, MouseEventFlags.RightDown);
            Assert.Equal(0x0010u, MouseEventFlags.RightUp);
            Assert.Equal(0x0020u, MouseEventFlags.MiddleDown);
            Assert.Equal(0x0040u, MouseEventFlags.MiddleUp);
            Assert.Equal(0x2000u, MouseEventFlags.MoveNoCoalesce);
            Assert.Equal(0x4000u, MouseEventFlags.VirtualDesk);
            Assert.Equal(0x8000u, MouseEventFlags.Absolute);
            Assert.Equal(0xE001u, MouseEventFlags.AbsoluteMove);
            Assert.Equal(65535, MouseEventFlags.AbsoluteMax);
        }

        [Fact]
        public void NothingChanged_NoMove_IsZero()
            => Assert.Equal(0u, MouseEventFlags.For(0, 0, move: false));

        [Fact]
        public void HeldButtons_DoNotRepeat()
            => Assert.Equal(0u, MouseEventFlags.For(5, 5, move: false));

        [Fact]
        public void MoveAlone_IsTheAbsoluteMove()
            => Assert.Equal(MouseEventFlags.AbsoluteMove, MouseEventFlags.For(0, 0, move: true));

        [Theory]
        [InlineData(0, 1, 0x0002u)]   // left down
        [InlineData(1, 0, 0x0004u)]   // left up
        [InlineData(0, 2, 0x0008u)]   // right down
        [InlineData(2, 0, 0x0010u)]   // right up
        [InlineData(0, 4, 0x0020u)]   // middle down
        [InlineData(4, 0, 0x0040u)]   // middle up
        public void OneButtonTransition_IsThatFlag(byte previous, byte next, uint expected)
            => Assert.Equal(expected, MouseEventFlags.For(previous, next, move: false));

        [Fact]
        public void SwappingButtons_ReleasesOneAndPressesTheOther()
            => Assert.Equal(MouseEventFlags.LeftUp | MouseEventFlags.RightDown, MouseEventFlags.For(1, 2, move: false));

        [Fact]
        public void MoveAndThreePresses_Combine()
            => Assert.Equal(
                MouseEventFlags.AbsoluteMove | MouseEventFlags.LeftDown | MouseEventFlags.RightDown | MouseEventFlags.MiddleDown,
                MouseEventFlags.For(0, 7, move: true));

        [Fact]
        public void BitsAboveMiddle_AreIgnored()
            => Assert.Equal(0u, MouseEventFlags.For(0, 0xF8, move: false));
    }
}
