// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace Guncon3Console
{
    /// <summary>The three facts about this build that both the console header and the GUI's About box print.
    /// One place, so they can never disagree.</summary>
    internal static class AppInfo
    {
        /// <summary>Three-part assembly version, or "?" if the assembly carries none.</summary>
        public static string Version { get; } =
            typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "?";

        /// <summary>The running exe, or "?" for a host that does not expose one.</summary>
        public static string ExePath { get; } = Environment.ProcessPath ?? "?";

        /// <summary>
        /// Where every configuration file is read from and written to — never a relative
        /// path, because a logon start runs with the working directory set to system32,
        /// and a relative path would read the wrong file there.
        /// </summary>
        public static string BaseDirectory { get; } = AppDomain.CurrentDomain.BaseDirectory;
    }
}
