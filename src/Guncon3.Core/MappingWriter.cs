// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Guncon3.Core
{
    /// <summary>
    /// Writes the lines of a mapping file from a pair of dictionaries: the inverse of
    /// <see cref="MappingFile.Parse"/>, and pinned to it by test — Parse(Write(h, k, m))
    /// gives back exactly k and m with no diagnostics. Lines the original file could
    /// not parse are not carried over; the editor warns before saving instead.
    /// </summary>
    public static class MappingWriter
    {
        /// <summary>
        /// The comment and blank lines at the top of an existing mapping file, kept
        /// verbatim so a hand-written header survives an edit in the UI.
        /// </summary>
        public static IReadOnlyList<string> HeaderOf(IEnumerable<string> lines)
        {
            ArgumentNullException.ThrowIfNull(lines);

            var header = new List<string>();
            foreach (var raw in lines)
            {
                var line = raw?.Trim();
                if (!string.IsNullOrEmpty(line) && !line.StartsWith('#'))
                    break;

                header.Add(raw ?? string.Empty);
            }

            return header;
        }

        /// <summary>
        /// The header, a blank line, then every mapped button in <see cref="GunButton"/>
        /// order — its KEYBOARD line first, then its MOUSE line. Codes and names are
        /// written with the invariant culture, because the parser reads them that way.
        /// </summary>
        public static IReadOnlyList<string> Write(
            IReadOnlyList<string>? header,
            IReadOnlyDictionary<GunButton, byte> keyboard,
            IReadOnlyDictionary<GunButton, MouseButton> mouse)
        {
            ArgumentNullException.ThrowIfNull(keyboard);
            ArgumentNullException.ThrowIfNull(mouse);

            var lines = new List<string>();
            if (header != null) lines.AddRange(header);

            // One blank line between a kept header and the mappings — and not a second one, so a file saved
            // twice does not grow.
            if (lines.Count != 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add(string.Empty);

            foreach (GunButton button in Enum.GetValues<GunButton>())
            {
                if (keyboard.TryGetValue(button, out byte code))
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"KEYBOARD.{code}={button}"));

                if (mouse.TryGetValue(button, out var mouseButton))
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"MOUSE.{mouseButton}={button}"));
            }

            return lines;
        }
    }
}
