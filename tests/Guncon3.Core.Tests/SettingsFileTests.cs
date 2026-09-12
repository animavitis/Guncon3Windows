// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class SettingsFileTests
    {
        [Fact]
        public void Parse_EmptyInputGivesTheDefaults()
        {
            var parse = SettingsFile.Parse(Array.Empty<string>());

            Assert.Equal(Settings.Default, parse.Settings);
            Assert.False(parse.Settings.StartMinimized);
            Assert.False(parse.Settings.LogToFile);
            Assert.Empty(parse.Diagnostics);
            Assert.Empty(parse.UnknownLines);
        }

        [Fact]
        public void Parse_ReadsTheDepthThreshold()
        {
            var parse = SettingsFile.Parse(new[] { "ZThreshold=1234" });

            Assert.Equal(1234, parse.Settings.ZThreshold);
            Assert.Empty(parse.Diagnostics);
            Assert.Empty(parse.UnknownLines);
        }

        [Theory]
        [InlineData("ZThreshold=nope")]
        [InlineData("ZThreshold=")]
        [InlineData("ZThreshold=0")]
        [InlineData("ZThreshold=-5")]
        [InlineData("ZThreshold=99999999")]
        public void Parse_KeepsTheDefaultDepthThresholdAndReportsAnUnusableOne(string line)
        {
            var parse = SettingsFile.Parse(new[] { line });

            Assert.Equal(Settings.Default.ZThreshold, parse.Settings.ZThreshold);
            Assert.Single(parse.Diagnostics);

            // Reported, not kept as an unknown line: the key is known, only its value was not usable.
            Assert.Empty(parse.UnknownLines);
        }

        [Fact]
        public void FormatThenParse_RoundTripsTheDepthThreshold()
        {
            var settings = new Settings { StartMinimized = true, LogToFile = true, ZThreshold = 4321 };

            var parse = SettingsFile.Parse(SettingsFile.Format(settings));

            Assert.Equal(settings, parse.Settings);
            Assert.Empty(parse.Diagnostics);
        }

        [Fact]
        public void Parse_ReadsBothKeys()
        {
            var parse = SettingsFile.Parse(new[] { "StartMinimized=true", "LogToFile=true" });

            Assert.True(parse.Settings.StartMinimized);
            Assert.True(parse.Settings.LogToFile);
            Assert.Empty(parse.Diagnostics);
        }

        [Theory]
        [InlineData("startminimized=TRUE")]
        [InlineData("STARTMINIMIZED=True")]
        [InlineData("  StartMinimized  =  true  ")]
        public void Parse_KeysAndValuesAreCaseAndWhitespaceInsensitive(string line)
        {
            var parse = SettingsFile.Parse(new[] { line });

            Assert.True(parse.Settings.StartMinimized);
            Assert.Empty(parse.Diagnostics);
        }

        [Fact]
        public void Parse_SkipsCommentsAndBlankLines()
        {
            var parse = SettingsFile.Parse(new[] { "# a comment", "", "   ", "LogToFile=true" });

            Assert.True(parse.Settings.LogToFile);
            Assert.Empty(parse.Diagnostics);
            Assert.Empty(parse.UnknownLines);
        }

        [Fact]
        public void Parse_ReportsALineWithNoSeparator()
        {
            var parse = SettingsFile.Parse(new[] { "StartMinimized true" });

            Assert.Equal(Settings.Default, parse.Settings);
            Assert.Contains("Line 1", parse.Diagnostics.Single());
            Assert.Contains("key=value", parse.Diagnostics.Single());
        }

        [Fact]
        public void Parse_ReportsAValueThatIsNotABooleanAndKeepsTheDefault()
        {
            var parse = SettingsFile.Parse(new[] { "LogToFile=perhaps" });

            Assert.False(parse.Settings.LogToFile);
            Assert.Contains("Line 1", parse.Diagnostics.Single());
            Assert.Contains("perhaps", parse.Diagnostics.Single());
        }

        [Fact]
        public void Parse_NumbersTheReportedLineFromOneIncludingComments()
        {
            var parse = SettingsFile.Parse(new[] { "# one", "# two", "LogToFile=maybe" });

            Assert.Contains("Line 3", parse.Diagnostics.Single());
        }

        [Fact]
        public void Parse_KeepsAnUnknownKeyAndDoesNotReportIt()
        {
            var parse = SettingsFile.Parse(new[] { "Volume=11", "LogToFile=true" });

            Assert.Equal("Volume=11", parse.UnknownLines.Single());
            Assert.Empty(parse.Diagnostics);
        }

        [Fact]
        public void Format_WritesBothKeysWithLowercaseBooleans()
        {
            var lines = SettingsFile.Format(new Settings { StartMinimized = true, LogToFile = false });

            Assert.Contains("StartMinimized=true", lines);
            Assert.Contains("LogToFile=false", lines);
        }

        [Fact]
        public void Format_PutsUnknownLinesBackAtTheEnd()
        {
            var lines = SettingsFile.Format(Settings.Default, new[] { "Volume=11" });

            Assert.Equal("Volume=11", lines[^1]);
        }

        [Fact]
        public void Format_ThenParse_RoundTripsTheSettings()
        {
            var original = new Settings { StartMinimized = true, LogToFile = true };

            var reparsed = SettingsFile.Parse(SettingsFile.Format(original));

            Assert.Equal(original, reparsed.Settings);
            Assert.Empty(reparsed.Diagnostics);
            Assert.Empty(reparsed.UnknownLines);
        }

        [Fact]
        public void Format_IsStableAcrossASecondRoundTripWithAnUnknownKey()
        {
            var first = SettingsFile.Parse(new[] { "# mine", "LogToFile=true", "Volume=11" });
            var once = SettingsFile.Format(first.Settings, first.UnknownLines);

            var second = SettingsFile.Parse(once);
            var twice = SettingsFile.Format(second.Settings, second.UnknownLines);

            Assert.Equal(once, twice);
            Assert.True(second.Settings.LogToFile);
            Assert.Equal("Volume=11", second.UnknownLines.Single());
        }

        [Fact]
        public void Parse_NullInputThrows()
            => Assert.Throws<ArgumentNullException>(() => SettingsFile.Parse(null!));
    }
}
