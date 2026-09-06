// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class ScanCodeTableTests
    {
        [Theory]
        [InlineData(4, 0x1E, false)]     // a
        [InlineData(29, 0x2C, false)]    // z
        [InlineData(30, 0x02, false)]    // 1
        [InlineData(39, 0x0B, false)]    // 0
        [InlineData(40, 0x1C, false)]    // ENTER
        [InlineData(41, 0x01, false)]    // ESCAPE
        [InlineData(44, 0x39, false)]    // SPACEBAR
        [InlineData(52, 0x28, false)]    // "dummy5" is the apostrophe key
        [InlineData(57, 0x3A, false)]    // CAPSLOCK
        [InlineData(58, 0x3B, false)]    // F1
        [InlineData(67, 0x44, false)]    // F10
        [InlineData(68, 0x57, false)]    // F11
        [InlineData(69, 0x58, false)]    // F12
        [InlineData(70, 0x37, true)]     // PRINTSCREEN
        [InlineData(71, 0x46, false)]    // SCROLLLOCK
        [InlineData(73, 0x52, true)]     // INSERT
        [InlineData(76, 0x53, true)]     // DELETE
        [InlineData(79, 0x4D, true)]     // RIGHTARROW
        [InlineData(80, 0x4B, true)]     // LEFTARROW
        [InlineData(81, 0x50, true)]     // DOWNARROW
        [InlineData(82, 0x48, true)]     // UPARROW
        [InlineData(83, 0x45, false)]    // NUMLOCK
        [InlineData(84, 0x35, true)]     // K/
        [InlineData(85, 0x37, false)]    // K*
        [InlineData(88, 0x1C, true)]     // KENTER
        [InlineData(89, 0x4F, false)]    // K1
        [InlineData(97, 0x49, false)]    // K9
        [InlineData(98, 0x52, false)]    // K0
        [InlineData(99, 0x53, false)]    // K.
        [InlineData(100, 0x64, false)]   // F13 (legacy code, see KeyCodeTable)
        [InlineData(110, 0x6E, false)]   // F23
        [InlineData(111, 0x76, false)]   // F24
        public void Lookup_GivesTheSetOneMakeCode(byte usage, int code, bool extended)
        {
            var scan = ScanCodeTable.Lookup(usage);

            Assert.NotNull(scan);
            Assert.Equal((ushort)code, scan!.Value.Code);
            Assert.Equal(extended, scan.Value.Extended);
            Assert.False(scan.Value.UsesVirtualKey);
        }

        [Fact]
        public void Lookup_PauseGoesOutAsAVirtualKey()
        {
            var scan = ScanCodeTable.Lookup(72);

            Assert.NotNull(scan);
            Assert.True(scan!.Value.UsesVirtualKey);
            Assert.Equal(0x13, scan.Value.VirtualKey);
            Assert.Equal(0, scan.Value.Code);
            Assert.False(scan.Value.Extended);
        }

        [Fact]
        public void Table_IsInLockstepWithKeyCodeTable()
        {
            for (int c = 0; c < 256; c++)
            {
                byte usage = (byte)c;
                Assert.Equal(KeyCodeTable.IsValid(usage), ScanCodeTable.Lookup(usage).HasValue);
            }
        }

        [Fact]
        public void Table_EveryEntryIsEitherAScanCodeOrAVirtualKey()
        {
            foreach (var (code, _) in KeyCodeTable.Entries)
            {
                var scan = ScanCodeTable.Lookup(code)!.Value;
                bool hasScan = scan.Code != 0;
                bool hasVk = scan.VirtualKey != 0;
                Assert.True(hasScan ^ hasVk, $"usage {code} has scan {scan.Code} and vk {scan.VirtualKey}");
                if (hasVk) Assert.False(scan.Extended);
            }
        }

        [Fact]
        public void Table_NoTwoKeysShareAMakeCode()
        {
            var seen = new HashSet<(ushort, bool)>();
            foreach (var (code, name) in KeyCodeTable.Entries)
            {
                var scan = ScanCodeTable.Lookup(code)!.Value;
                if (scan.UsesVirtualKey) continue;
                Assert.True(seen.Add((scan.Code, scan.Extended)), $"{name} ({code}) repeats make code 0x{scan.Code:X2} ext={scan.Extended}");
            }
        }

        [Fact]
        public void Table_ExtendedKeysAreExactlyTheE0Set()
        {
            var expected = new HashSet<byte> { 70, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 84, 88 };
            var actual = new HashSet<byte>();
            foreach (var (code, _) in KeyCodeTable.Entries)
                if (ScanCodeTable.Lookup(code)!.Value.Extended) actual.Add(code);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TryGet_MirrorsLookup()
        {
            Assert.True(ScanCodeTable.TryGet(4, out var a));
            Assert.Equal(ScanCodeTable.Lookup(4), a);

            Assert.False(ScanCodeTable.TryGet(0, out var none));
            Assert.Equal(default, none);
        }
    }
}
