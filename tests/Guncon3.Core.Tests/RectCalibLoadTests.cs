using System;
using System.IO;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class RectCalibLoadTests
    {
        [Fact]
        public void TryLoad_ReportsMissing()
        {
            var path = Path.Combine(Path.GetTempPath(), "absent-" + Path.GetRandomFileName());

            Assert.Equal(CalibrationLoad.Missing, RectCalib.TryLoad(path, 0, out var calib));
            Assert.Null(calib);
        }

        [Fact]
        public void TryLoad_ReportsMalformed()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllLines(path, new[] { "RawMinX=100", "RawMaxX=50", "ScreenW=0" });

                Assert.Equal(CalibrationLoad.Malformed, RectCalib.TryLoad(path, 0, out var calib));
                Assert.Null(calib);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void TryLoad_ReportsOk()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                new RectCalib
                {
                    RawMinX = 1, RawMaxX = 2, RawMinY = 3, RawMaxY = 4,
                    ScreenW = 1920, ScreenH = 1080, InvertY = true
                }.Save(path);

                Assert.Equal(CalibrationLoad.Ok, RectCalib.TryLoad(path, 0, out var calib));
                Assert.NotNull(calib);
                Assert.True(calib.IsValid());
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void SaveAndTryLoad_AgreeOnTheDefaultPathForAGunIndex(int gunIndex)
        {
            var rc = new RectCalib
            {
                RawMinX = 1, RawMaxX = 2, RawMinY = 3, RawMaxY = 4,
                ScreenW = 1920, ScreenH = 1080, InvertY = true
            };

            rc.Save(null, gunIndex);
            try
            {
                Assert.Equal(CalibrationLoad.Ok, RectCalib.TryLoad(null, gunIndex, out var loaded));
                Assert.NotNull(loaded);
                Assert.Equal(rc.RawMaxX, loaded.RawMaxX);
            }
            finally
            {
                string suffix = gunIndex > 0 ? $"_{gunIndex + 1}" : "";
                File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"calibration_rect{suffix}.txt"));
            }
        }
    }
}
