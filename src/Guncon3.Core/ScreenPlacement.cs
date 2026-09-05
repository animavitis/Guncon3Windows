// SPDX-License-Identifier: GPL-2.0-only
namespace Guncon3.Core
{
    /// <summary>
    /// Where a monitor sits on the virtual desktop, in pixels. The calibration window
    /// records the screen it was calibrated on, so the aim can be placed on that screen
    /// even when the virtual mouse spans several.
    /// </summary>
    public readonly record struct ScreenPlacement(int X, int Y, int W, int H)
    {
        public bool IsValid => W > 0 && H > 0;

        /// <summary>
        /// Converts a point normalized to this screen (0..1 on each axis) into a point
        /// normalized to the whole desktop, clamped to 0..1. A placement that no longer
        /// fits the desktop clamps to the edge rather than extrapolating; a non-finite
        /// input (a homography evaluated on its horizon) becomes an edge too.
        /// </summary>
        public (double X, double Y) ToDesktop(double nx, double ny, ScreenPlacement desktop)
        {
            if (!desktop.IsValid)
                return (Clamp01(nx), Clamp01(ny));

            double px = X + nx * W;
            double py = Y + ny * H;

            return (Clamp01((px - desktop.X) / desktop.W), Clamp01((py - desktop.Y) / desktop.H));
        }

        private static double Clamp01(double v)
        {
            if (double.IsNaN(v)) return 0;
            return v < 0 ? 0 : v > 1 ? 1 : v;
        }
    }
}
