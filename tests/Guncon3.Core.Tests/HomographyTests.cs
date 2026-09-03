using System;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class HomographyTests
    {
        private static readonly (double X, double Y)[] UnitSquare =
        {
            (0, 0), (1, 0), (1, 1), (0, 1)
        };

        [Fact]
        public void Solve_MappingTheUnitSquareToItself_GivesTheIdentity()
        {
            var h = Homography.Solve(UnitSquare, UnitSquare);

            Assert.Equal(1.0, h[0], 9);
            Assert.Equal(0.0, h[1], 9);
            Assert.Equal(0.0, h[2], 9);
            Assert.Equal(0.0, h[3], 9);
            Assert.Equal(1.0, h[4], 9);
            Assert.Equal(0.0, h[5], 9);
            Assert.Equal(0.0, h[6], 9);
            Assert.Equal(0.0, h[7], 9);
            Assert.Equal(1.0, h[8], 9);
        }

        [Fact]
        public void Solve_AlwaysFixesTheLastCoefficientAtOne()
        {
            var quad = new (double X, double Y)[] { (10, 20), (300, 40), (280, 200), (30, 190) };

            var h = Homography.Solve(quad, UnitSquare);

            Assert.Equal(1.0, h[8], 9);
        }

        [Fact]
        public void SolveAndApply_MapEachCornerOntoItsCounterpart()
        {
            var quad = new (double X, double Y)[] { (200, 1800), (1800, 1600), (1800, 400), (200, 200) };

            var h = Homography.Solve(quad, UnitSquare);

            for (int i = 0; i < 4; i++)
            {
                var mapped = Homography.Apply(h, quad[i].X, quad[i].Y);
                Assert.Equal(UnitSquare[i].X, mapped.X, 6);
                Assert.Equal(UnitSquare[i].Y, mapped.Y, 6);
            }
        }

        [Fact]
        public void SolveAndApply_RoundTripThroughTheInverseFit()
        {
            var quad = new (double X, double Y)[] { (200, 1800), (1800, 1600), (1800, 400), (200, 200) };

            var forward = Homography.Solve(UnitSquare, quad);
            var back = Homography.Solve(quad, UnitSquare);

            foreach (var (sx, sy) in new[] { (0.25, 0.25), (0.5, 0.5), (0.75, 0.1), (0.9, 0.8) })
            {
                var raw = Homography.Apply(forward, sx, sy);
                var recovered = Homography.Apply(back, raw.X, raw.Y);

                Assert.Equal(sx, recovered.X, 6);
                Assert.Equal(sy, recovered.Y, 6);
            }
        }

        [Fact]
        public void Solve_RejectsAnythingOtherThanFourCorrespondences()
        {
            var three = new (double X, double Y)[] { (0, 0), (1, 0), (1, 1) };

            Assert.Throws<ArgumentException>(() => Homography.Solve(three, UnitSquare));
            Assert.Throws<ArgumentException>(() => Homography.Solve(UnitSquare, three));
        }

        [Fact]
        public void Solve_ThrowsInvalidOperationForCollinearSourcePoints()
        {
            var collinear = new (double X, double Y)[] { (0, 0), (1, 1), (2, 2), (3, 3) };

            Assert.Throws<InvalidOperationException>(() => Homography.Solve(collinear, UnitSquare));
        }

        [Fact]
        public void Apply_SurvivesADegenerateThirdRow()
        {
            // H[6..8] chosen so the projective divisor lands on zero. Apply clamps it
            // to 1e-9 rather than dividing by zero, so this must return a finite point.
            var h = new double[] { 1, 0, 0, 0, 1, 0, 1, 0, -1 };

            var mapped = Homography.Apply(h, 1, 0);

            Assert.True(double.IsFinite(mapped.X));
            Assert.True(double.IsFinite(mapped.Y));
        }
    }
}
