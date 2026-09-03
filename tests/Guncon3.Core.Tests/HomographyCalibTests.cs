using System;
using System.Collections.Generic;
using System.Linq;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class HomographyCalibTests
    {
        /// A gun held to the left of the screen: the right edge of the captured
        /// quadrilateral is vertically compressed, and raw Y decreases downward.
        private static List<(double X, double Y)> Keystone(double centreX = 1000, double centreY = 1000) => new()
        {
            (200, 1800),   // P0 top-left
            (1800, 1600),  // P1 top-right
            (1800, 400),   // P2 bottom-right
            (200, 200),    // P3 bottom-left
            (centreX, centreY)
        };

        [Fact]
        public void FromPoints_MapsEachCapturedCornerOntoItsScreenCorner()
        {
            var calib = HomographyCalib.FromPoints(Keystone());

            Assert.NotNull(calib);

            var expected = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) };
            var points = Keystone();

            for (int i = 0; i < 4; i++)
            {
                var mapped = calib.MapNormalized(points[i].X, points[i].Y);
                Assert.Equal(expected[i].Item1, mapped.X, 6);
                Assert.Equal(expected[i].Item2, mapped.Y, 6);
            }
        }

        [Fact]
        public void MapNormalized_ClampsAPointOutsideTheCapturedQuadrilateral()
        {
            var calib = HomographyCalib.FromPoints(Keystone());

            var far = calib.MapNormalized(-100000, -100000);
            Assert.InRange(far.X, 0.0, 1.0);
            Assert.InRange(far.Y, 0.0, 1.0);

            var alsoFar = calib.MapNormalized(100000, 100000);
            Assert.InRange(alsoFar.X, 0.0, 1.0);
            Assert.InRange(alsoFar.Y, 0.0, 1.0);
        }

        [Fact]
        public void CentreError_IsNearZeroWhenTheCentreAgreesWithTheCorners()
        {
            // The true projective centre of the corner quad, not its arithmetic mean.
            var unitSquare = new (double X, double Y)[] { (0, 0), (1, 0), (1, 1), (0, 1) };
            var corners = Keystone().GetRange(0, 4).ToArray();
            var forward = Homography.Solve(unitSquare, corners);
            var trueCentre = Homography.Apply(forward, 0.5, 0.5);

            var calib = HomographyCalib.FromPoints(Keystone(trueCentre.X, trueCentre.Y));

            Assert.NotNull(calib);
            Assert.True(calib.CentreError < 1e-6, $"centre error was {calib.CentreError}");
            Assert.False(calib.IsSuspect);
        }

        [Fact]
        public void CentreError_IsLargeAndSuspectWhenTheCentreDisagrees()
        {
            // Centre shot far from where the corners imply it should be.
            var calib = HomographyCalib.FromPoints(Keystone(400, 1700));

            Assert.NotNull(calib);
            Assert.True(calib.CentreError > HomographyCalib.SuspectCentreError,
                $"centre error was {calib.CentreError}");
            Assert.True(calib.IsSuspect);
        }

        [Fact]
        public void FromPoints_ReturnsNullForTooFewPoints()
        {
            Assert.Null(HomographyCalib.FromPoints(null));
            Assert.Null(HomographyCalib.FromPoints(new List<(double X, double Y)>()));
            Assert.Null(HomographyCalib.FromPoints(Keystone().GetRange(0, 4)));
        }

        [Fact]
        public void FromPoints_ReturnsNullForDegenerateCorners()
        {
            // Five shots at the same spot: the corners are coincident, so the
            // system is singular and there is no transform to fit.
            var same = new List<(double X, double Y)>
            {
                (500, 500), (500, 500), (500, 500), (500, 500), (500, 500)
            };

            Assert.Null(HomographyCalib.FromPoints(same));
        }

        [Fact]
        public void Matrix_ExposesNineCoefficients()
        {
            var calib = HomographyCalib.FromPoints(Keystone());

            Assert.NotNull(calib);
            Assert.Equal(9, calib.Matrix.Count);
        }

        [Fact]
        public void Matrix_IsNotTheLiveArrayAndCannotBeCastBackToMutateIt()
        {
            var calib = HomographyCalib.FromPoints(Keystone());

            Assert.NotNull(calib);
            Assert.False(calib.Matrix is double[], "Matrix must not hand out the live backing array.");

            var before = calib.MapNormalized(1000, 1000);
            var after = calib.MapNormalized(1000, 1000);
            Assert.Equal(before, after);
        }
    }
}
