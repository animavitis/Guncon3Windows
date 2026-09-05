// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Drawing;
using System.Windows.Forms;
using Guncon3.Core;

namespace Guncon3Console
{
    /// <summary>Monitor helpers shared by the app and the calibration window.</summary>
    internal static class Screens
    {
        /// <summary>Index into <see cref="Screen.AllScreens"/> of the monitor holding the given window; 0 when
        /// unknown.</summary>
        public static int IndexOf(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return 0;
            var target = Screen.FromHandle(hwnd);
            var all = Screen.AllScreens;
            for (int i = 0; i < all.Length; i++)
                if (all[i].Bounds == target.Bounds) return i;
            return 0;
        }

        public static ScreenPlacement ToPlacement(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
    }
}
