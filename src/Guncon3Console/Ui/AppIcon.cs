// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Drawing;
using System.IO;

namespace Guncon3Console.Ui
{
    /// <summary>The exe's own icon, loaded once. The window, the taskbar and the tray all use it.</summary>
    internal static class AppIcon
    {
        private static Icon _icon;

        public static Icon Value => _icon ??= Load();

        private static Icon Load()
        {
            try
            {
                if (Environment.ProcessPath is string path)
                    return Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                // A host that reports no exe path: the generic icon is a worse icon, not a broken window.
            }

            return SystemIcons.Application;
        }
    }
}
