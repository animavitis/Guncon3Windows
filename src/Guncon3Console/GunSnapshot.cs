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
        public static GunSnapshot Empty { get; } = new GunSnapshot(GunMapping.Empty, null);

        public GunMapping Mapping { get; }

        /// <summary>Null when the gun has no usable calibration.</summary>
        public RectCalib Calibration { get; }

        public GunSnapshot(GunMapping mapping, RectCalib calibration)
        {
            Mapping = mapping;
            Calibration = calibration;
        }
    }
}
