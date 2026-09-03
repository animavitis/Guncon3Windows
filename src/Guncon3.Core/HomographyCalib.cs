using System;
using System.Collections.Generic;

namespace Guncon3.Core
{
    /// <summary>
    /// A projective mapping from the gun's raw coordinates to normalized screen
    /// space, fitted to the four corner points of a calibration capture. Unlike
    /// <see cref="RectCalib"/> it can represent a quadrilateral, so it stays accurate
    /// when the gun is held off the screen's axis.
    /// </summary>
    public sealed class HomographyCalib
    {
        /// <summary>
        /// A centre error above this is reported as a suspect fit. It is a guess:
        /// nobody has measured what error a good capture produces on this hardware,
        /// which is why it warns rather than rejects.
        /// </summary>
        public const double SuspectCentreError = 0.05;

        private static readonly (double X, double Y)[] UnitSquare =
        {
            (0, 0), (1, 0), (1, 1), (0, 1)
        };

        private readonly double[] _matrix;
        private readonly IReadOnlyList<double> _matrixView;

        /// <summary>The nine coefficients, with the last fixed at 1.</summary>
        public IReadOnlyList<double> Matrix => _matrixView;

        /// <summary>
        /// How far the captured centre point lands from the middle of the screen
        /// once the corner fit is applied, in normalized units. A consistent
        /// capture puts it near zero.
        /// </summary>
        public double CentreError { get; }

        /// <summary>True when the fit does not agree with the captured centre.</summary>
        public bool IsSuspect => CentreError > SuspectCentreError;

        private HomographyCalib(double[] matrix, double centreError)
        {
            _matrix = matrix;
            _matrixView = Array.AsReadOnly(_matrix);
            CentreError = centreError;
        }

        /// <summary>
        /// Fits the four corners onto the unit square. Returns null when the points
        /// cannot produce a transform: fewer than five of them, or an arrangement
        /// degenerate enough to leave the linear system singular.
        /// </summary>
        public static HomographyCalib FromPoints(IReadOnlyList<(double X, double Y)> points)
        {
            if (points == null || points.Count < 5)
                return null;

            var corners = new (double X, double Y)[4];
            for (int i = 0; i < 4; i++)
                corners[i] = points[i];

            double[] matrix;
            try
            {
                matrix = Homography.Solve(corners, UnitSquare);
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            var centre = Homography.Apply(matrix, points[4].X, points[4].Y);
            double dx = centre.X - 0.5;
            double dy = centre.Y - 0.5;

            return new HomographyCalib(matrix, Math.Sqrt(dx * dx + dy * dy));
        }

        /// <summary>
        /// Maps a raw point to normalized 0..1 screen coordinates. A projective
        /// transform can send a point outside the square, so the result is clamped —
        /// the caller's contract does not allow anything else.
        /// </summary>
        public (double X, double Y) MapNormalized(double rawX, double rawY)
        {
            var mapped = Homography.Apply(_matrix, rawX, rawY);

            double x = mapped.X;
            double y = mapped.Y;

            if (x < 0) x = 0; else if (x > 1) x = 1;
            if (y < 0) y = 0; else if (y > 1) y = 1;

            return (x, y);
        }
    }
}
