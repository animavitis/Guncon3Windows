// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using Guncon3.Core;
using Xunit;
using Xunit.Abstractions;

namespace Guncon3.Core.Tests
{
    public class CalibrationAccuracyTests
    {
        private readonly ITestOutputHelper _output;

        public CalibrationAccuracyTests(ITestOutputHelper output) => _output = output;

        private static readonly (double X, double Y)[] UnitSquare =
        {
            (0, 0), (1, 0), (1, 1), (0, 1)
        };

        /// A gun held left of the screen: the right edge of the raw quadrilateral is
        /// vertically compressed relative to the left, and raw Y decreases downward.
        private static readonly (double X, double Y)[] KeystonedCorners =
        {
            (200, 1800), (1800, 1600), (1800, 400), (200, 200)
        };

        [Fact]
        public void Homography_IsSubstantiallyMoreAccurateThanTheRectangleOnAKeystonedCapture()
        {
            var forward = Homography.Solve(UnitSquare, KeystonedCorners);

            var captured = new List<(double X, double Y)>();
            foreach (var corner in UnitSquare)
                captured.Add(Homography.Apply(forward, corner.X, corner.Y));
            captured.Add(Homography.Apply(forward, 0.5, 0.5));

            var calib = CalibrationFile.FromCapture(captured, 1920, 1080);
            Assert.NotNull(calib.Homography);

            double rectWorst = 0, homographyWorst = 0;
            double rectTotal = 0, homographyTotal = 0;
            int samples = 0;

            for (int i = 0; i <= 10; i++)
            {
                for (int j = 0; j <= 10; j++)
                {
                    double sx = i / 10.0;
                    double sy = j / 10.0;

                    var raw = Homography.Apply(forward, sx, sy);

                    var viaRect = calib.MapNormalized(raw.X, raw.Y, CalibrationMode.Rect);
                    var viaHomography = calib.MapNormalized(raw.X, raw.Y, CalibrationMode.Homography);

                    double rectError = Distance(viaRect, sx, sy);
                    double homographyError = Distance(viaHomography, sx, sy);

                    rectTotal += rectError;
                    homographyTotal += homographyError;
                    if (rectError > rectWorst) rectWorst = rectError;
                    if (homographyError > homographyWorst) homographyWorst = homographyError;
                    samples++;
                }
            }

            double rectMean = rectTotal / samples;
            double homographyMean = homographyTotal / samples;

            _output.WriteLine(string.Format(CultureInfo.InvariantCulture, "rect      mean={0:F4} worst={1:F4}", rectMean, rectWorst));
            _output.WriteLine(string.Format(CultureInfo.InvariantCulture, "homography mean={0:F4} worst={1:F4}", homographyMean, homographyWorst));

            // The homography is fitted to this exact transform, so it should recover
            // the screen position essentially exactly.
            Assert.True(homographyWorst < 1e-6,
                $"homography should be near-exact on its own fit, worst was {homographyWorst}");

            // The rectangle cannot represent a quadrilateral. It must be visibly worse.
            Assert.True(rectMean > 0.02,
                $"the keystone fixture is too mild to prove anything, rect mean was {rectMean}");
            Assert.True(rectWorst > 0.05,
                $"the keystone fixture is too mild to prove anything, rect worst was {rectWorst}");
        }

        [Fact]
        public void BothMappingsAgreeWhenTheCaptureIsAlreadyARectangle()
        {
            // No keystone: the two mappings have nothing to disagree about, so a
            // straight-on setup loses nothing by switching.
            var rectangular = new List<(double X, double Y)>
            {
                (200, 1800), (1800, 1800), (1800, 200), (200, 200), (1000, 1000)
            };

            var calib = CalibrationFile.FromCapture(rectangular, 1920, 1080);
            Assert.NotNull(calib.Homography);

            for (int i = 0; i <= 4; i++)
            {
                for (int j = 0; j <= 4; j++)
                {
                    double rawX = 200 + (1600.0 * i / 4);
                    double rawY = 200 + (1600.0 * j / 4);

                    var viaRect = calib.MapNormalized(rawX, rawY, CalibrationMode.Rect);
                    var viaHomography = calib.MapNormalized(rawX, rawY, CalibrationMode.Homography);

                    Assert.Equal(viaRect.X, viaHomography.X, 6);
                    Assert.Equal(viaRect.Y, viaHomography.Y, 6);
                }
            }
        }

        private static double Distance((double X, double Y) mapped, double x, double y)
        {
            double dx = mapped.X - x;
            double dy = mapped.Y - y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
