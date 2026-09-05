// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Guncon3.Core
{
    /// <summary>Everything one read of settings.txt produces: the settings, with a default in place of anything
    /// unreadable; unknown lines kept verbatim so a save can put them back; and one diagnostic per line that
    /// could not be understood, never a throw.</summary>
    public sealed record SettingsParse(
        Settings Settings,
        IReadOnlyList<string> UnknownLines,
        IReadOnlyList<string> Diagnostics);

    /// <summary>Reads and writes settings.txt: <c>key=value</c>, one per line, '#' starts a comment, keys are
    /// case-insensitive. A line that cannot be understood is reported and dropped — a broken settings file
    /// never stops the application.</summary>
    public static class SettingsFile
    {
        public const string StartMinimizedKey = "StartMinimized";
        public const string LogToFileKey = "LogToFile";

        private const string HeaderComment = "# GUNCON3 settings. Delete a line to get its default back.";

        public static SettingsParse Parse(IEnumerable<string> lines)
        {
            ArgumentNullException.ThrowIfNull(lines);

            bool startMinimized = Settings.Default.StartMinimized;
            bool logToFile = Settings.Default.LogToFile;
            var unknown = new List<string>();
            var diagnostics = new List<string>();

            int lineNo = 0;
            foreach (var raw in lines)
            {
                lineNo++;

                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                    continue;

                int eq = line.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0)
                {
                    diagnostics.Add(string.Create(CultureInfo.InvariantCulture, $"Line {lineNo}: not a key=value line: {line}"));
                    continue;
                }

                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();

                if (key.Equals(StartMinimizedKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(value, out bool parsed)) startMinimized = parsed;
                    else diagnostics.Add(BadBoolean(lineNo, key, value));
                }
                else if (key.Equals(LogToFileKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (bool.TryParse(value, out bool parsed)) logToFile = parsed;
                    else diagnostics.Add(BadBoolean(lineNo, key, value));
                }
                else
                {
                    // A key from a newer version, or one the user added: kept so that saving from an older
                    // build does not silently delete it.
                    unknown.Add(line);
                }
            }

            return new SettingsParse(
                new Settings { StartMinimized = startMinimized, LogToFile = logToFile },
                unknown,
                diagnostics);
        }

        /// <summary>The lines to write: a one-line comment, both keys, then any unknown lines a previous <see
        /// cref="Parse"/> handed back.</summary>
        public static IReadOnlyList<string> Format(Settings settings, IReadOnlyList<string>? unknownLines = null)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var lines = new List<string>
            {
                HeaderComment,
                $"{StartMinimizedKey}={Text(settings.StartMinimized)}",
                $"{LogToFileKey}={Text(settings.LogToFile)}"
            };

            if (unknownLines != null) lines.AddRange(unknownLines);
            return lines;
        }

        private static string BadBoolean(int lineNo, string key, string value)
            => string.Create(CultureInfo.InvariantCulture, $"Line {lineNo}: {key} wants true or false, not '{value}'.");

        private static string Text(bool value) => value ? "true" : "false";
    }
}
