// SPDX-License-Identifier: GPL-2.0-only
namespace Guncon3.Core
{
    /// <summary>
    /// What settings.txt can say. Immutable; reading and writing the file live in
    /// <see cref="SettingsFile"/>, so the format has no UI in it and can be tested off
    /// Windows.
    /// </summary>
    public sealed record Settings
    {
        /// <summary>Start with the window hidden — the tray icon is the only visible thing.</summary>
        public bool StartMinimized { get; init; }

        /// <summary>Also append every log line to guncon3.log next to the exe.</summary>
        public bool LogToFile { get; init; }

        public static Settings Default { get; } = new Settings();
    }
}
