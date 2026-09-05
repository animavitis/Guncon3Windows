// SPDX-License-Identifier: GPL-2.0-only
namespace Guncon3.Core
{
    /// <summary>Minimal rectangular calibration: maps the gun's raw coordinates (0-65535) onto normalized 0..1
    /// screen space via <see cref="MapNormalized"/>. ScreenW/ScreenH are persisted metadata only and do not
    /// take part in the mapping.</summary>
    public sealed class RectCalib
    {
        public double RawMinX { get; init; }
        public double RawMaxX { get; init; }
        public double RawMinY { get; init; }
        public double RawMaxY { get; init; }
        public int ScreenW { get; init; }
        public int ScreenH { get; init; }
        public bool InvertY { get; init; }

        /// <summary>Are the ranges valid and the screen size correct?</summary>
        public bool IsValid()
        {
            return double.IsFinite(RawMinX) && double.IsFinite(RawMaxX) &&
                   double.IsFinite(RawMinY) && double.IsFinite(RawMaxY) &&
                   RawMaxX > RawMinX &&
                   RawMaxY > RawMinY &&
                   ScreenW > 0 && ScreenH > 0;
        }

        /// <summary>Maps a raw point to normalized 0..1 screen coordinates.</summary>
        public (double X, double Y) MapNormalized(double rawX, double rawY)
        {
            if (!IsValid())
                return (0, 0);

            double nx = (rawX - RawMinX) / (RawMaxX - RawMinX);
            double ny = (rawY - RawMinY) / (RawMaxY - RawMinY);

            if (nx < 0) nx = 0; else if (nx > 1) nx = 1;
            if (ny < 0) ny = 0; else if (ny > 1) ny = 1;

            if (InvertY) ny = 1.0 - ny;

            return (nx, ny);
        }
    }
}
