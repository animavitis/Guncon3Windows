// SPDX-License-Identifier: GPL-2.0-only
using Guncon3.Core;

namespace Guncon3Console
{
    /// <summary>
    /// Everything one calibration frame's picture depends on. A value type with
    /// structural equality, so the window repaints only when something in it changed
    /// — Candidate is compared by reference. NextTarget indexes
    /// <see cref="CalibrationSession.Targets"/>; ScreenIndex and GunIndex are
    /// zero-based; Rejected means the last five shots did not span a rectangle.
    /// </summary>
    internal readonly record struct FrameState(
        CalibrationPhase Phase,
        int NextTarget,
        int CapturedCount,
        CalibrationFile Candidate,
        CalibrationMode Mode,
        int ScreenIndex,
        int ScreenCount,
        ScreenPlacement Screen,
        short RawX,
        short RawY,
        bool InsideScreen,
        bool Trigger,
        bool Rejected,
        bool ShowRaw,
        int GunIndex,
        bool Disconnected);
}
