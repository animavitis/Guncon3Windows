// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace Guncon3.Core
{
    /// <summary>
    /// The joystick feeder's 0..<see cref="StickDigitizer.AxisMax"/> axes as an Xbox 360
    /// controller reports them: thumbs are signed 16-bit with 0 at rest, triggers are a
    /// byte. The gun reports "up" as a value below the centre and XInput reports it as a
    /// positive thumb, so the Y axes go through <see cref="ToThumbInverted"/>.
    /// </summary>
    public static class XboxAxis
    {
        /// <summary>0 → -32767, AxisMax → 32767; the resting centre (AxisMax / 2) lands on -1, well inside
        /// XInput's own deadzone. Anything above AxisMax is clamped.</summary>
        public static short ToThumb(ushort axis)
        {
            int v = Math.Min((int)axis, StickDigitizer.AxisMax);   // the cast: Min(ushort, ushort) would be ambiguous
            return (short)(v * 2 - StickDigitizer.AxisMax);
        }

        /// <summary>0 → 32767, AxisMax → -32767: <see cref="ToThumb"/> with the sign flipped.</summary>
        public static short ToThumbInverted(ushort axis) => (short)-ToThumb(axis);

        /// <summary>0 → 0, AxisMax → 255. Anything above AxisMax is clamped.</summary>
        public static byte ToTrigger(ushort axis)
        {
            int v = Math.Min((int)axis, StickDigitizer.AxisMax);   // the cast: Min(ushort, ushort) would be ambiguous
            return (byte)(v >> 7);
        }
    }
}
