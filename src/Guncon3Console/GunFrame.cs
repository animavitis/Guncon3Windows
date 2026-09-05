// SPDX-License-Identifier: GPL-2.0-only
using Guncon3.Core;

namespace Guncon3Console
{
    /// <summary>
    /// One frame as the Test tab sees it: the raw read, both calibrated aims, and
    /// what the three feeders decided to hold. Built on the worker thread at the end
    /// of <see cref="GunWorker.MapAndFeed"/> and published by one reference write, so
    /// the UI thread either sees the whole frame or the previous one — never half.
    /// Timestamp is <see cref="System.Diagnostics.Stopwatch"/> ticks; the four stick
    /// axes are 0..255; Buttons is a copy, not a live view; RawNormalized is the raw
    /// axes placed on 0..1 with no calibration applied, only somewhere to draw a grey
    /// crosshair.
    /// </summary>
    internal sealed record GunFrame(
        long Timestamp,
        short RawX,
        short RawY,
        bool InsideScreen,
        bool[] Buttons,
        int HatX,
        int HatY,
        int RX,
        int RY,
        short Z,
        CalibrationMode Mode,
        bool HasCalibration,
        (double X, double Y) RawNormalized,
        (double X, double Y)? Rect,
        (double X, double Y)? Homography,
        MouseOutput Mouse,
        KeyboardOutput Keyboard,
        JoystickOutput Joystick,
        bool MouseHealthy,
        bool KeyboardHealthy,
        bool JoystickHealthy);

    /// <summary>
    /// What the absolute mouse holds after one frame, whether or not that frame was
    /// actually sent: an unchanged report is still what the device is showing. X and Y
    /// are 0..32767 over the virtual desktop; Buttons is bit 0 left, bit 1 right, bit 2
    /// middle; HasPosition is false when the gun has no usable calibration, so the
    /// cursor was left where it was.
    /// </summary>
    internal readonly record struct MouseOutput(ushort X, ushort Y, byte Buttons, bool HasPosition);

    /// <summary>
    /// What the virtual keyboard holds after one frame. Modifier is always 0 today: no
    /// mapping produces one. Keys is a copy of <see cref="KeySetBuilder.Keys"/>, 0..6 long.
    /// </summary>
    internal readonly record struct KeyboardOutput(byte Modifier, byte[] Keys);

    /// <summary>What the virtual joystick holds after one frame, sent or not.</summary>
    internal readonly record struct JoystickOutput(JoystickReportState Report);
}
