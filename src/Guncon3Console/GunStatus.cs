// SPDX-License-Identifier: GPL-2.0-only
namespace Guncon3Console
{
    /// <summary>One gun as a host sees it, taken on the main thread by <see cref="App.Status"/>. Nothing here
    /// is live — ask again for a newer one.</summary>
    internal sealed record GunStatus(
        int Index,
        string DevicePath,
        bool IsConnected,
        bool PipeAbandoned,
        bool HasCalibration,
        bool HasHomography,
        bool MouseHealthy,
        bool KeyboardHealthy,
        bool JoystickHealthy);
}
