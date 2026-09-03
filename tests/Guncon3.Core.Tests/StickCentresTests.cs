using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
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
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var sc = new StickCentres { HatX = 118, HatY = 120, RX = 127, RY = 122 };
                sc.Save(path);

                var loaded = StickCentres.Load(path);

                Assert.NotNull(loaded);
                Assert.Equal(118, loaded.HatX);
                Assert.Equal(120, loaded.HatY);
                Assert.Equal(127, loaded.RX);
                Assert.Equal(122, loaded.RY);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Load_ReturnsNullForMissingFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "absent-" + Path.GetRandomFileName());

            Assert.Null(StickCentres.Load(path));
        }

        [Fact]
        public void Load_ReturnsNullForMalformedFile()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllLines(path, new[] { "HatX=notanumber", "HatY=120", "RX=127", "RY=122" });

                Assert.Null(StickCentres.Load(path));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Load_ReturnsNullForImplausibleValue()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllLines(path, new[] { "HatX=200", "HatY=120", "RX=127", "RY=122" });

                Assert.Null(StickCentres.Load(path));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Load_SkipsCommentsAndBlankLines()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllLines(path, new[]
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

                var loaded = StickCentres.Load(path);

                Assert.NotNull(loaded);
                Assert.Equal(118, loaded.HatX);
                Assert.Equal(122, loaded.RY);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Parsing_IsInvariantCulture()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var sc = new StickCentres { HatX = 118, HatY = 120, RX = 127, RY = 122 };
                sc.Save(path);

                var text = File.ReadAllText(path);
                Assert.DoesNotContain(",", text);

                var loaded = StickCentres.Load(path);
                Assert.NotNull(loaded);
                Assert.Equal(118, loaded.HatX);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
                File.Delete(path);
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
