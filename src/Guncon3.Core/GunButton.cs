// SPDX-License-Identifier: GPL-2.0-only
namespace Guncon3.Core
{
    /// <summary>Logical buttons exposed by the Guncon for mapping to keyboard/mouse. Public so the reader, the
    /// feeders and the mapping parser can all name the same button.</summary>
    public enum GunButton
    {
        // Physical buttons
        Trigger,
        A1,
        A2,
        B1,
        B2,
        C1,
        C2,
        AClick,
        BClick,

        // "Digitalized" left stick axes
        LUp,
        LDown,
        LLeft,
        LRight,

        // "Digitalized" right stick axes
        RUp,
        RDown,
        RLeft,
        RRight,

        // "Digitalized" depth axis. Last on purpose: the joystick feeder's report bits and every saved
        // mapping are indexed by this enum, so a new member may only ever be appended.
        ZLow,
        ZHigh
    }

    public static class GunButtons
    {
        /// <summary>Number of <see cref="GunButton"/> members. The enum is contiguous from 0, so a
        /// <c>bool[Count]</c> indexed by <c>(int)button</c> holds one flag per button without a dictionary
        /// lookup.</summary>
        public const int Count = 19;
    }
}
