// SPDX-License-Identifier: GPL-2.0-only
using System;
using Microsoft.Win32;

namespace Guncon3Console
{
    /// <summary>Run-at-logon for the current user. The registry value is the source of truth, not a setting in
    /// settings.txt: the user can remove it with any startup manager and the checkbox must then show it as off.
    /// Every method can throw (UnauthorizedAccessException, SecurityException, IOException); the settings
    /// dialog catches, shows the message and reverts its checkbox.</summary>
    internal static class Autostart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Guncon3";

        /// <summary>What Windows would run at logon: this exe, quoted against spaces in the path.</summary>
        public static string Command => "\"" + Environment.ProcessPath + "\"";

        public static bool IsEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) != null;
        }

        public static void Enable()
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(ValueName, Command, RegistryValueKind.String);
        }

        public static void Disable()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
