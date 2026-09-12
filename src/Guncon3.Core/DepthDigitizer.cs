// SPDX-License-Identifier: GPL-2.0-only
namespace Guncon3.Core
{
    /// <summary>
    /// Turns the gun's depth reading into two digital directions either side of a
    /// threshold the user sets, so that Z can be bound in mapping.txt like any button.
    /// The same shape as <see cref="StickDigitizer.IsLow"/> and
    /// <see cref="StickDigitizer.IsHigh"/>, with one difference: a stick has a resting
    /// centre that can be measured, and Z has none — where the split belongs depends on
    /// how far the player stands from the screen, so it is a setting rather than a
    /// measurement.
    /// </summary>
    public static class DepthDigitizer
    {
        /// <summary>
        /// The middle of the 0..255 range the rest of the pipeline assumes for Z — see
        /// <see cref="StickDigitizer.ToAxis"/>, which clamps it there before the pad
        /// sees it. It is a starting point, not a good value for any particular room:
        /// the Test Input tab prints the live reading beside the Z bar, and this is
        /// meant to be set from that.
        /// </summary>
        public const int DefaultThreshold = 128;

        /// <summary>Half-width of the band around the threshold in which neither direction fires. Wider than
        /// the sticks' own: a hand-held gun's distance to the screen never sits as still as a thumb off a
        /// stick, and a direction that chatters is worse than one that answers late.</summary>
        public const int Deadband = 24;

        /// <summary>Anything outside this cannot split the reading in two: one of the directions would be
        /// permanently on, which as a bound key means a key held down for the whole session.</summary>
        public const int MinThreshold = 1;
        public const int MaxThreshold = 65534;

        public static bool IsPlausibleThreshold(int threshold)
            => threshold >= MinThreshold && threshold <= MaxThreshold;

        /// <summary>True when the gun is nearer than the threshold — the reading is below it.</summary>
        public static bool IsLow(int z, int threshold) => z < threshold - Deadband;

        /// <summary>True when the gun is further than the threshold — the reading is above it.</summary>
        public static bool IsHigh(int z, int threshold) => z > threshold + Deadband;

        /// <summary>The threshold to actually digitise with: the setting when it can work, the default when
        /// it cannot. Never throws — a settings file is user input.</summary>
        public static int Clamp(int threshold)
            => IsPlausibleThreshold(threshold) ? threshold : DefaultThreshold;
    }
}
