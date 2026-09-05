// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using Guncon3.Core;

namespace Guncon3Console
{
    /// <summary>
    /// settings.txt next to the exe. Everything about the format lives in
    /// <see cref="SettingsFile"/>; this is the file handling around it.
    /// </summary>
    internal static class SettingsStore
    {
        private const string FileName = "settings.txt";

        /// <summary>Unknown keys from the last <see cref="Load"/>, so <see cref="Save"/> can put them back.</summary>
        private static IReadOnlyList<string> _unknownLines = Array.Empty<string>();

        /// <summary>
        /// Set when <see cref="Load"/> found the file but could not read it.
        /// <see cref="Save"/> then refuses: writing defaults over a file we could not
        /// even read would silently drop whatever it held.
        /// </summary>
        private static bool _loadFailed;

        public static string FilePath => Path.Combine(AppInfo.BaseDirectory, FileName);

        /// <summary>
        /// Reads settings.txt. A missing file is the defaults, silently — that is the
        /// normal first run. A malformed line is a warning and the default for that key.
        /// </summary>
        public static Settings Load()
        {
            if (!File.Exists(FilePath)) return Settings.Default;

            try
            {
                var parse = SettingsFile.Parse(File.ReadAllLines(FilePath));
                _unknownLines = parse.UnknownLines;

                foreach (var diagnostic in parse.Diagnostics)
                    Log.Warn($"[Settings] {diagnostic}");

                return parse.Settings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Log.Warn($"[Settings] {FileName} could not be read: {ex.Message}. Using the defaults.");
                _loadFailed = true;
                return Settings.Default;
            }
        }

        /// <summary>
        /// Writes settings.txt atomically. False when it could not be written, or when
        /// <see cref="Load"/> found the file but could not read it — overwriting it from
        /// defaults would drop whatever it actually held.
        /// </summary>
        public static bool Save(Settings settings)
        {
            if (_loadFailed)
            {
                Log.Warn("settings.txt could not be read at startup; not overwriting it.");
                return false;
            }

            try
            {
                AtomicFile.WriteLines(FilePath, SettingsFile.Format(settings, _unknownLines));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Log.Warn($"[Settings] {FileName} could not be written: {ex.Message}");
                return false;
            }
        }
    }
}
