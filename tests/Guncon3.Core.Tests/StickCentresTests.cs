// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    // Both classes write real files under AppDomain.CurrentDomain.BaseDirectory through
    // the default-path API. xunit runs collections one at a time, so they cannot race.
    [Collection("default-paths")]
    public class StickCentresTests
    {
        [Fact]
        public void Defaults_AreAll128AndValid()
        {
            var sc = new StickCentres();

            Assert.Equal(128, sc.HatX);
            Assert.Equal(128, sc.HatY);
            Assert.Equal(128, sc.RX);
            Assert.Equal(128, sc.RY);
            Assert.True(sc.IsValid());
        }

        [Theory]
        [InlineData(95)]
        [InlineData(161)]
        public void IsValid_FalseWhenAnyAxisOutsideThePlausibleBand(int badValue)
        {
            Assert.False(new StickCentres { HatX = badValue }.IsValid());
            Assert.False(new StickCentres { HatY = badValue }.IsValid());
            Assert.False(new StickCentres { RX = badValue }.IsValid());
            Assert.False(new StickCentres { RY = badValue }.IsValid());
        }

        [Fact]
        public void SaveAndLoad_RoundTripsAllFourValues()
        {
            using var file = new TempFile();

            var sc = new StickCentres { HatX = 118, HatY = 120, RX = 127, RY = 122 };
            sc.Save(file.Path);

            var loaded = StickCentres.Load(file.Path);

            Assert.NotNull(loaded);
            Assert.Equal(118, loaded.HatX);
            Assert.Equal(120, loaded.HatY);
            Assert.Equal(127, loaded.RX);
            Assert.Equal(122, loaded.RY);
        }

        [Fact]
        public void Load_ReturnsNullForMissingFile()
        {
            using var file = new TempFile("absent-");

            Assert.Null(StickCentres.Load(file.Path));
        }

        [Fact]
        public void Load_ReturnsNullForMalformedFile()
        {
            using var file = new TempFile();
            File.WriteAllLines(file.Path, new[] { "HatX=notanumber", "HatY=120", "RX=127", "RY=122" });

            Assert.Null(StickCentres.Load(file.Path));
        }

        [Fact]
        public void Load_ReturnsNullForImplausibleValue()
        {
            using var file = new TempFile();
            File.WriteAllLines(file.Path, new[] { "HatX=200", "HatY=120", "RX=127", "RY=122" });

            Assert.Null(StickCentres.Load(file.Path));
        }

        [Fact]
        public void Load_SkipsCommentsAndBlankLines()
        {
            using var file = new TempFile();
            File.WriteAllLines(file.Path, new[]
            {
                "# stick centres",
                "",
                "HatX=118",
                "   ",
                "# right stick",
                "HatY=120",
                "RX=127",
                "RY=122"
            });

            var loaded = StickCentres.Load(file.Path);

            Assert.NotNull(loaded);
            Assert.Equal(118, loaded.HatX);
            Assert.Equal(122, loaded.RY);
        }

        [Fact]
        public void Parsing_IsInvariantCulture()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            using var file = new TempFile();
            try
            {
                var sc = new StickCentres { HatX = 118, HatY = 120, RX = 127, RY = 122 };
                sc.Save(file.Path);

                var text = File.ReadAllText(file.Path);
                Assert.DoesNotContain(",", text);

                var loaded = StickCentres.Load(file.Path);
                Assert.NotNull(loaded);
                Assert.Equal(118, loaded.HatX);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void SaveAndLoad_AgreeOnTheDefaultPathForAGunIndex(int gunIndex)
        {
            var sc = new StickCentres { HatX = 118, HatY = 120, RX = 127, RY = 122 };
            sc.Save(null, gunIndex);
            try
            {
                string suffix = gunIndex > 0 ? $"_{gunIndex + 1}" : "";
                var expectedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"stick_centre{suffix}.txt");

                Assert.True(File.Exists(expectedPath));

                var loaded = StickCentres.Load(null, gunIndex);
                Assert.NotNull(loaded);
                Assert.Equal(118, loaded.HatX);
            }
            finally
            {
                string suffix = gunIndex > 0 ? $"_{gunIndex + 1}" : "";
                File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"stick_centre{suffix}.txt"));
            }
        }
    }
}
