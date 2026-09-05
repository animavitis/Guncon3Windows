// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class CalibrationFileScreenTests
    {
        [Fact]
        public void FromCapture_WithAPlacementRecordsItAndFillsTheRectangleSize()
        {
            var screen = new ScreenPlacement(1920, 0, 1280, 720);

            var file = CalibrationFile.FromCapture(Fixtures.Keystone(), screen);

            Assert.Equal(screen, file.Screen);
            Assert.Equal(1280, file.Rect.ScreenW);
            Assert.Equal(720, file.Rect.ScreenH);
        }

        [Fact]
        public void FromCapture_WithOnlyASizePlacesTheScreenAtTheOrigin()
        {
            var file = CalibrationFile.FromCapture(Fixtures.Keystone(), 1920, 1080);

            Assert.Equal(new ScreenPlacement(0, 0, 1920, 1080), file.Screen);
        }

        [Fact]
        public void FromRect_PlacesTheScreenAtTheOrigin()
        {
            var rect = new RectCalib
            {
                RawMinX = 200, RawMaxX = 1800, RawMinY = 200, RawMaxY = 1800,
                ScreenW = 1920, ScreenH = 1080, InvertY = true
            };

            var file = CalibrationFile.FromRect(rect);

            Assert.Equal(new ScreenPlacement(0, 0, 1920, 1080), file.Screen);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsTheScreenPlacement()
        {
            using var file = new TempFile();

            var screen = new ScreenPlacement(-1280, 40, 1280, 720);
            CalibrationFile.FromCapture(Fixtures.Keystone(), screen).Save(file.Path);

            var loaded = CalibrationFile.Load(file.Path);

            Assert.NotNull(loaded);
            Assert.Equal(screen, loaded.Screen);
        }

        [Fact]
        public void Load_AFileWithoutScreenOriginPlacesTheScreenAtTheOrigin()
        {
            using var file = new TempFile();
            File.WriteAllLines(file.Path, new[]
            {
                "RawMinX=200", "RawMaxX=1800",
                "RawMinY=200", "RawMaxY=1800",
                "ScreenW=1920", "ScreenH=1080", "InvertY=1"
            });

            var loaded = CalibrationFile.Load(file.Path);

            Assert.NotNull(loaded);
            Assert.Equal(new ScreenPlacement(0, 0, 1920, 1080), loaded.Screen);
        }

        [Fact]
        public void Load_IgnoresAnUnparsableScreenOriginAndFallsBackToZero()
        {
            // The origin is placement metadata, not part of the mapping. A damaged
            // origin should not cost the user their whole calibration.
            using var file = new TempFile();
            File.WriteAllLines(file.Path, new[]
            {
                "RawMinX=200", "RawMaxX=1800",
                "RawMinY=200", "RawMaxY=1800",
                "ScreenX=abc", "ScreenY=",
                "ScreenW=1920", "ScreenH=1080", "InvertY=1"
            });

            var loaded = CalibrationFile.Load(file.Path);

            Assert.NotNull(loaded);
            Assert.Equal(new ScreenPlacement(0, 0, 1920, 1080), loaded.Screen);
        }

        [Fact]
        public void Save_ReplacesAnExistingFileAndLeavesNoTemporaryBehind()
        {
            using var file = new TempFile();
            File.WriteAllText(file.Path, "RawMinX=1\nRawMaxX=2\nRawMinY=1\nRawMaxY=2\nScreenW=1\nScreenH=1\nInvertY=1\n");

            CalibrationFile.FromCapture(Fixtures.Keystone(), new ScreenPlacement(0, 0, 1920, 1080)).Save(file.Path);

            var loaded = CalibrationFile.Load(file.Path);
            Assert.NotNull(loaded);
            Assert.Equal(1920, loaded.Rect.ScreenW);
            Assert.Equal(5, loaded.Points.Count);

            var dir = Path.GetDirectoryName(file.Path)!;
            var name = Path.GetFileName(file.Path);
            Assert.All(Directory.GetFiles(dir, name + "*"), f => Assert.Equal(file.Path, f));
        }
    }
}
