// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class MappingWriterTests
    {
        private static readonly IReadOnlyList<string> NoHeader = Array.Empty<string>();

        /// <summary>The invariant the editor depends on: what Write writes, Parse reads back.</summary>
        private static GunMapping RoundTrip(
            IReadOnlyList<string> header,
            IReadOnlyDictionary<GunButton, byte> keyboard,
            IReadOnlyDictionary<GunButton, MouseButton> mouse)
            => MappingFile.Parse(MappingWriter.Write(header, keyboard, mouse));

        [Fact]
        public void Write_EmptyMappingsGiveJustTheHeader()
        {
            var lines = MappingWriter.Write(
                new[] { "# mine" },
                new Dictionary<GunButton, byte>(),
                new Dictionary<GunButton, MouseButton>());

            Assert.Equal(new[] { "# mine", "" }, lines);
        }

        [Fact]
        public void Write_OneEntryPerDeviceRoundTripsThroughParse()
        {
            var m = RoundTrip(NoHeader,
                new Dictionary<GunButton, byte> { [GunButton.C1] = 30 },
                new Dictionary<GunButton, MouseButton> { [GunButton.Trigger] = MouseButton.Left });

            Assert.Equal((byte)30, m.Keyboard[GunButton.C1]);
            Assert.Equal(MouseButton.Left, m.Mouse[GunButton.Trigger]);
            Assert.Single(m.Keyboard);
            Assert.Single(m.Mouse);
            Assert.Empty(m.Diagnostics);
        }

        [Fact]
        public void Write_AllSeventeenButtonsOnBothDevicesRoundTrip()
        {
            var buttons = Enum.GetValues<GunButton>();
            Assert.Equal(GunButtons.Count, buttons.Length);

            var keyboard = new Dictionary<GunButton, byte>();
            var mouse = new Dictionary<GunButton, MouseButton>();
            for (int i = 0; i < buttons.Length; i++)
            {
                keyboard[buttons[i]] = KeyCodeTable.Entries[i].Code;
                mouse[buttons[i]] = (MouseButton)(i % 3);
            }

            var m = RoundTrip(NoHeader, keyboard, mouse);

            Assert.Equal(GunButtons.Count, m.Keyboard.Count);
            Assert.Equal(GunButtons.Count, m.Mouse.Count);
            Assert.All(buttons, b => Assert.Equal(keyboard[b], m.Keyboard[b]));
            Assert.All(buttons, b => Assert.Equal(mouse[b], m.Mouse[b]));
            Assert.Empty(m.Diagnostics);
        }

        [Fact]
        public void Write_KeepsTheHeaderVerbatimIncludingBlankLines()
        {
            var header = new[] { "# GUNCON3 mapping", "#", "", "#   DEVICE.COMMAND = GUNCOMMAND" };

            var lines = MappingWriter.Write(
                header,
                new Dictionary<GunButton, byte> { [GunButton.A1] = 4 },
                new Dictionary<GunButton, MouseButton>());

            Assert.Equal(header, lines.Take(header.Length));
        }

        [Fact]
        public void Write_PutsOneBlankLineBetweenTheHeaderAndTheMappings()
        {
            var lines = MappingWriter.Write(
                new[] { "# mine" },
                new Dictionary<GunButton, byte> { [GunButton.A1] = 4 },
                new Dictionary<GunButton, MouseButton>());

            Assert.Equal(new[] { "# mine", "", "KEYBOARD.4=A1" }, lines);
        }

        [Fact]
        public void Write_DoesNotAddASecondBlankLineWhenTheHeaderEndsWithOne()
        {
            var lines = MappingWriter.Write(
                new[] { "# mine", "" },
                new Dictionary<GunButton, byte> { [GunButton.A1] = 4 },
                new Dictionary<GunButton, MouseButton>());

            Assert.Equal(new[] { "# mine", "", "KEYBOARD.4=A1" }, lines);
        }

        [Fact]
        public void Write_WithNoHeaderStartsWithTheFirstMapping()
        {
            var lines = MappingWriter.Write(
                NoHeader,
                new Dictionary<GunButton, byte> { [GunButton.A1] = 4 },
                new Dictionary<GunButton, MouseButton>());

            Assert.Equal(new[] { "KEYBOARD.4=A1" }, lines);
        }

        [Fact]
        public void Write_EmitsButtonsInEnumOrderWithKeyboardBeforeMouse()
        {
            var lines = MappingWriter.Write(
                NoHeader,
                new Dictionary<GunButton, byte> { [GunButton.RRight] = 30, [GunButton.Trigger] = 31 },
                new Dictionary<GunButton, MouseButton> { [GunButton.Trigger] = MouseButton.Left, [GunButton.A1] = MouseButton.Right });

            Assert.Equal(new[]
            {
                "KEYBOARD.31=Trigger",
                "MOUSE.Left=Trigger",
                "MOUSE.Right=A1",
                "KEYBOARD.30=RRight"
            }, lines);
        }

        [Fact]
        public void Write_TheSameKeycodeOnTwoButtonsSurvivesTheRoundTrip()
        {
            var m = RoundTrip(NoHeader,
                new Dictionary<GunButton, byte> { [GunButton.A1] = 30, [GunButton.B1] = 30 },
                new Dictionary<GunButton, MouseButton>());

            Assert.Equal((byte)30, m.Keyboard[GunButton.A1]);
            Assert.Equal((byte)30, m.Keyboard[GunButton.B1]);
            Assert.Empty(m.Diagnostics);
        }

        [Fact]
        public void Write_EveryKeycodeInTheTableRoundTripsWithNoDiagnostics()
        {
            foreach (var (code, _) in KeyCodeTable.Entries)
            {
                var m = RoundTrip(NoHeader,
                    new Dictionary<GunButton, byte> { [GunButton.Trigger] = code },
                    new Dictionary<GunButton, MouseButton>());

                Assert.Equal(code, m.Keyboard[GunButton.Trigger]);
                Assert.Empty(m.Diagnostics);
            }
        }

        [Fact]
        public void Write_NullHeaderIsTreatedAsNoHeader()
        {
            var lines = MappingWriter.Write(
                null,
                new Dictionary<GunButton, byte> { [GunButton.A1] = 4 },
                new Dictionary<GunButton, MouseButton>());

            Assert.Equal(new[] { "KEYBOARD.4=A1" }, lines);
        }

        [Fact]
        public void Write_NullDictionariesThrow()
        {
            Assert.Throws<ArgumentNullException>(
                () => MappingWriter.Write(NoHeader, null!, new Dictionary<GunButton, MouseButton>()));
            Assert.Throws<ArgumentNullException>(
                () => MappingWriter.Write(NoHeader, new Dictionary<GunButton, byte>(), null!));
        }

        [Fact]
        public void HeaderOf_StopsAtTheFirstMappingLine()
        {
            var header = MappingWriter.HeaderOf(new[] { "# one", "", "MOUSE.Left = Trigger", "# not part of the header" });

            Assert.Equal(new[] { "# one", "" }, header);
        }

        [Fact]
        public void HeaderOf_KeepsCommentsAndBlankLinesVerbatim()
        {
            var lines = new[] { "#  spaced comment  ", "", "   ", "#last" };

            Assert.Equal(lines, MappingWriter.HeaderOf(lines));
        }

        [Fact]
        public void HeaderOf_OfAFileThatStartsWithAMappingIsEmpty()
            => Assert.Empty(MappingWriter.HeaderOf(new[] { "MOUSE.Left = Trigger", "# after" }));

        [Fact]
        public void HeaderOf_ThenWrite_RoundTripsAWholeFileAndDropsWhatCouldNotBeParsed()
        {
            var file = new[]
            {
                "# GUNCON3 mapping",
                "",
                "MOUSE.Left = Trigger",
                "KEYBOARD.30 = C1",
                "nonsense"
            };

            var parsed = MappingFile.Parse(file);
            Assert.Single(parsed.Diagnostics);      // "nonsense" is reported, and deliberately not carried over

            var written = MappingWriter.Write(MappingWriter.HeaderOf(file), parsed.Keyboard, parsed.Mouse);
            var reparsed = MappingFile.Parse(written);

            Assert.Equal("# GUNCON3 mapping", written[0]);
            Assert.Equal(MouseButton.Left, reparsed.Mouse[GunButton.Trigger]);
            Assert.Equal((byte)30, reparsed.Keyboard[GunButton.C1]);
            Assert.Empty(reparsed.Diagnostics);
            Assert.DoesNotContain("nonsense", written);
        }

        [Fact]
        public void HeaderOf_NullThrows()
            => Assert.Throws<ArgumentNullException>(() => MappingWriter.HeaderOf(null!));
    }
}
