// SPDX-License-Identifier: GPL-2.0-only
using Guncon3.Core;

namespace Guncon3Console
{
    /// <summary>
    /// Everything the main thread can change underneath a running gun: its mapping and
    /// its calibration. Immutable, so a worker reads one consistent pair and never sees
    /// a mapping reload half-applied.
    /// </summary>
    internal sealed class GunSnapshot
    {
        public static GunSnapshot Empty { get; } = new GunSnapshot(GunMapping.Empty, null, CalibrationMode.Rect, default);

        public GunMapping Mapping { get; }

        /// <summary>Null when the gun has no usable calibration.</summary>
        public CalibrationFile Calibration { get; }

        /// <summary>Which of the calibration's two mappings to aim through.</summary>
        public CalibrationMode Mode { get; }

        /// <summary>
        /// The virtual desktop at the time of publishing, so the worker can place the
        /// aim on the calibrated screen without a system call per frame. An invalid
        /// (default) value leaves the aim in screen-relative coordinates.
        /// </summary>
        public ScreenPlacement Desktop { get; }

        public GunSnapshot(GunMapping mapping, CalibrationFile calibration, CalibrationMode mode, ScreenPlacement desktop)
        {
            Mapping = mapping;
            Calibration = calibration;
            Mode = mode;
            Desktop = desktop;
        }
    }
}
