// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class MappingFileTests
    {
        [Fact]
        public void Parse_ReadsMouseAndKeyboardEntries()
        {
            var m = MappingFile.Parse(new[]
            {
                "MOUSE.Left = Trigger",
                "KEYBOARD.30 = C1"
            });

            Assert.Equal(MouseButton.Left, m.Mouse[GunButton.Trigger]);
            Assert.Equal((byte)30, m.Keyboard[GunButton.C1]);
            Assert.Empty(m.Diagnostics);
        }

        [Fact]
        public void Parse_SkipsCommentsAndBlankLines()
        {
            var m = MappingFile.Parse(new[] { "# comment", "", "   ", "MOUSE.Right = A1" });

            Assert.Single(m.Mouse);
            Assert.Empty(m.Diagnostics);
        }

        [Fact]
        public void Parse_AcceptsAnyCasingOnBothSides()
        {
            var m = MappingFile.Parse(new[]
            {
                "mouse.left = trigger",
                "keyboard.31 = c2",
                "MOUSE.MIDDLE = b1"
            });

            Assert.Equal(MouseButton.Left, m.Mouse[GunButton.Trigger]);
            Assert.Equal(MouseButton.Middle, m.Mouse[GunButton.B1]);
            Assert.Equal((byte)31, m.Keyboard[GunButton.C2]);
            Assert.Empty(m.Diagnostics);
        }

        [Fact]
        public void Parse_ReportsLineWithNoSeparator()
        {
            var m = MappingFile.Parse(new[] { "MOUSE.Left Trigger" });

            Assert.Empty(m.Mouse);
            Assert.Contains("Line 1", m.Diagnostics.Single());
            Assert.Contains("no '='", m.Diagnostics.Single());
        }

        [Fact]
        public void Parse_ReportsLineWithNoDeviceSeparator()
        {
            var m = MappingFile.Parse(new[] { "MOUSE = Trigger" });

            Assert.Contains("no '.'", m.Diagnostics.Single());
        }

        [Fact]
        public void Parse_ReportsUnknownDevice()
        {
            var m = MappingFile.Parse(new[] { "KEYBORD.30 = Trigger" });

            Assert.Contains("unknown device", m.Diagnostics.Single());
            Assert.Contains("KEYBORD", m.Diagnostics.Single());
        }

        [Fact]
        public void Parse_ReportsUnknownGunCommand()
        {
            var m = MappingFile.Parse(new[] { "MOUSE.Left = Trigger2" });

            Assert.Contains("unknown gun command", m.Diagnostics.Single());
            Assert.Contains("Trigger2", m.Diagnostics.Single());
        }

        [Fact]
        public void Parse_ReportsUnknownMouseButton()
        {
            var m = MappingFile.Parse(new[] { "MOUSE.Side = Trigger" });

            Assert.Contains("unknown mouse button", m.Diagnostics.Single());
        }

        [Fact]
        public void Parse_ReportsNonNumericKeyCode()
        {
            var m = MappingFile.Parse(new[] { "KEYBOARD.F = Trigger" });

            Assert.Contains("not a keycode", m.Diagnostics.Single());
        }

        [Fact]
        public void Parse_NumbersLinesBeyondTwoHundredFiftyFive()
        {
            var lines = new List<string>();
            for (int i = 0; i < 300; i++) lines.Add("# filler");
            lines.Add("MOUSE.Left Trigger");

            var m = MappingFile.Parse(lines);

            Assert.Contains("Line 301", m.Diagnostics.Single());
        }

        [Fact]
        public void Parse_LastEntryWinsForDuplicateGunButton()
        {
            var m = MappingFile.Parse(new[] { "KEYBOARD.30 = Trigger", "KEYBOARD.31 = Trigger" });

            Assert.Equal((byte)31, m.Keyboard[GunButton.Trigger]);
        }

        [Fact]
        public void Load_ReportsMissingFileAndReturnsEmptyMapping()
        {
            var m = MappingFile.Load("no-such-mapping-file.txt");

            Assert.Empty(m.Mouse);
            Assert.Empty(m.Keyboard);
            Assert.Contains("not found", m.Diagnostics.Single());
        }

        [Fact]
        public void Empty_HasNoEntriesAndNoDiagnostics()
        {
            Assert.Empty(GunMapping.Empty.Mouse);
            Assert.Empty(GunMapping.Empty.Keyboard);
            Assert.Empty(GunMapping.Empty.Diagnostics);
        }

        [Fact]
        public void Parse_ReportsNumericGunCommand()
        {
            var m = MappingFile.Parse(new[] { "MOUSE.Left = 99" });

            Assert.Empty(m.Mouse);
            Assert.Contains("unknown gun command", m.Diagnostics.Single());
        }

        [Fact]
        public void Parse_ReportsNumericMouseButton()
        {
            var m = MappingFile.Parse(new[] { "MOUSE.7 = Trigger" });

            Assert.Empty(m.Mouse);
            Assert.Contains("unknown mouse button", m.Diagnostics.Single());
        }

        [Fact]
        public void Parse_AcceptsTheRightStickGunCommandsWithNoParserChange()
        {
            var m = MappingFile.Parse(new[]
            {
                "KEYBOARD.30 = RUp",
                "MOUSE.Right = RLeft"
            });

            Assert.Equal((byte)30, m.Keyboard[GunButton.RUp]);
            Assert.Equal(MouseButton.Right, m.Mouse[GunButton.RLeft]);
            Assert.Empty(m.Diagnostics);
        }

        [Fact]
        public void Parse_ExposesEntriesAsSpans()
        {
            var m = MappingFile.Parse(new[] { "MOUSE.Left = Trigger", "KEYBOARD.30 = C1", "KEYBOARD.31 = C2" });

            Assert.Equal(1, m.MousePairs.Length);
            Assert.Equal(2, m.KeyboardPairs.Length);
            Assert.Equal(m.Mouse.Count, m.MousePairs.Length);
            Assert.Equal(m.Keyboard.Count, m.KeyboardPairs.Length);

            foreach (var pair in m.MousePairs)
            {
                Assert.True(m.Mouse.TryGetValue(pair.Key, out var value));
                Assert.Equal(value, pair.Value);
            }

            foreach (var pair in m.KeyboardPairs)
            {
                Assert.True(m.Keyboard.TryGetValue(pair.Key, out var value));
                Assert.Equal(value, pair.Value);
            }
        }

        [Theory]
        [InlineData("KEYBOARD.0 = Trigger", "0")]
        [InlineData("KEYBOARD.200 = Trigger", "200")]
        [InlineData("KEYBOARD.3 = Trigger", "3")]
        public void Parse_RejectsAKeycodeOutsideTheTable(string line, string code)
        {
            var m = MappingFile.Parse(new[] { line });

            Assert.Empty(m.Keyboard);
            var d = m.Diagnostics.Single();
            Assert.Contains("Line 1", d);
            Assert.Contains($"unknown keycode: {code}", d);
            Assert.Contains("keys", d);   // names the command that lists valid codes
        }

        [Fact]
        public void Parse_AcceptsEveryKeycodeInTheTable()
        {
            var lines = KeyCodeTable.Entries.Select(e => $"KEYBOARD.{e.Code} = Trigger").ToArray();

            var m = MappingFile.Parse(lines);

            Assert.Empty(m.Diagnostics);
        }

        [Theory]
        [InlineData("KEYBOARD.+30 = Trigger")]
        [InlineData("KEYBOARD.٣٠ = Trigger")]
        [InlineData("KEYBOARD.1e1 = Trigger")]
        public void Parse_RejectsKeycodesThatAreNotPlainAsciiDigits(string line)
        {
            var m = MappingFile.Parse(new[] { line });

            Assert.Empty(m.Keyboard);
            Assert.Contains("not a keycode", m.Diagnostics.Single());
        }
    }
}
