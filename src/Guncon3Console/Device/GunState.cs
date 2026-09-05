// SPDX-License-Identifier: GPL-2.0-only
// CA1707: ABS_X/ABS_Y and friends are the Linux evdev names for these axes; renaming them is a spec non-goal.
#pragma warning disable CA1707
using Guncon3.Core;

namespace Guncon3Console
{
    public class GunState
    {
        /// <summary>One flag per <see cref="GunButton"/>, indexed by <c>(int)button</c>.</summary>
        public bool[] Buttons { get; } = new bool[GunButtons.Count];

        public int ABS_RY { get; set; }
        public int ABS_RX { get; set; }
        public int ABS_HAT0Y { get; set; }
        public int ABS_HAT0X { get; set; }
        public short Z { get; set; }
        public short ABS_Y { get; set; }
        public short ABS_X { get; set; }

        public bool INDICATOR1 { get; set; }
        public bool INDICATOR2 { get; set; }

        public bool IsInsideScreen => !INDICATOR2;
    }
}
