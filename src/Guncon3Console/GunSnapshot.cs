using Guncon3.Core;

namespace Guncon3Console
{
    /// <summary>
    /// Everything the main thread can change underneath a running gun: its mapping
    /// and its calibration. Immutable, so a worker reads one consistent pair and
    /// never sees a mapping reload half-applied.
    /// </summary>
    internal sealed class GunSnapshot
    {
        public static GunSnapshot Empty { get; } = new GunSnapshot(GunMapping.Empty, null, CalibrationMode.Rect);

        public GunMapping Mapping { get; }

        /// <summary>Null when the gun has no usable calibration.</summary>
        public CalibrationFile Calibration { get; }

        /// <summary>Which of the calibration's two mappings to aim through.</summary>
        public CalibrationMode Mode { get; }

        public GunSnapshot(GunMapping mapping, CalibrationFile calibration, CalibrationMode mode)
        {
            Mapping = mapping;
            Calibration = calibration;
            Mode = mode;
        }
    }
}
