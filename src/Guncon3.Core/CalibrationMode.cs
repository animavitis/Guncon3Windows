// SPDX-License-Identifier: GPL-2.0-only
namespace Guncon3.Core
{
    /// <summary>Which of a calibration's two mappings to aim through.</summary>
    public enum CalibrationMode
    {
        /// <summary>Per-axis linear mapping over the captured bounding box.</summary>
        Rect,

        /// <summary>Projective mapping fitted to the captured corners.</summary>
        Homography
    }
}
