namespace Guncon3.Core
{
    /// <summary>
    /// Minimal rectangular calibration: maps RAW_X/RAW_Y (the gun's raw range)
    /// onto normalized 0..1 screen space via <see cref="MapNormalized"/>.
    /// ScreenW/ScreenH are persisted metadata only and do not take part in the
    /// mapping — the caller decides what to scale the normalized result to.
    /// </summary>
    public class RectCalib
    {
        public double RawMinX { get; set; }
        public double RawMaxX { get; set; }
        public double RawMinY { get; set; }
        public double RawMaxY { get; set; }
        public int ScreenW { get; set; }
        public int ScreenH { get; set; }
        public bool InvertY { get; set; }

        /// <summary>Are the ranges valid and the screen size correct?</summary>
        public bool IsValid()
        {
            return double.IsFinite(RawMinX) && double.IsFinite(RawMaxX) &&
                   double.IsFinite(RawMinY) && double.IsFinite(RawMaxY) &&
                   RawMaxX > RawMinX &&
                   RawMaxY > RawMinY &&
                   ScreenW > 0 && ScreenH > 0;
        }

        /// <summary>
        /// Maps a RAW point to normalized 0..1 screen coordinates. ScreenW and ScreenH
        /// are persisted metadata and deliberately do not take part: the caller decides
        /// what to scale to.
        /// </summary>
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
