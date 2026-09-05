// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Guncon3.Core
{
    /// <summary>
    /// An immutable mapping from gun buttons to host mouse buttons and HID keycodes.
    /// </summary>
    public sealed class GunMapping
    {
        public static GunMapping Empty { get; } = new GunMapping(
            new Dictionary<GunButton, MouseButton>(),
            new Dictionary<GunButton, byte>(),
            Array.Empty<string>());

        public IReadOnlyDictionary<GunButton, MouseButton> Mouse { get; }
        public IReadOnlyDictionary<GunButton, byte> Keyboard { get; }
        public IReadOnlyList<string> Diagnostics { get; }

        private readonly KeyValuePair<GunButton, MouseButton>[] _mousePairs;
        private readonly KeyValuePair<GunButton, byte>[] _keyboardPairs;

        /// <summary>Mouse entries as a span, for allocation-free iteration on the hot path.</summary>
        public ReadOnlySpan<KeyValuePair<GunButton, MouseButton>> MousePairs => _mousePairs;

        /// <summary>Keyboard entries as a span, for allocation-free iteration on the hot path.</summary>
        public ReadOnlySpan<KeyValuePair<GunButton, byte>> KeyboardPairs => _keyboardPairs;

        public GunMapping(
            IReadOnlyDictionary<GunButton, MouseButton> mouse,
            IReadOnlyDictionary<GunButton, byte> keyboard,
            IReadOnlyList<string> diagnostics)
        {
            Mouse = mouse;
            Keyboard = keyboard;
            Diagnostics = diagnostics;

            _mousePairs = new KeyValuePair<GunButton, MouseButton>[mouse.Count];
            int mi = 0;
            foreach (var kv in mouse) _mousePairs[mi++] = kv;

            _keyboardPairs = new KeyValuePair<GunButton, byte>[keyboard.Count];
            int ki = 0;
            foreach (var kv in keyboard) _keyboardPairs[ki++] = kv;
        }
    }

    /// <summary>Parses mapping.txt. Every line is DEVICE.COMMAND = GUNCOMMAND, for example "KEYBOARD.30 = C1"
    /// or "MOUSE.Left = Trigger". Blank lines and lines starting with '#' are ignored. Rejected lines never
    /// throw; they produce a diagnostic.</summary>
    public static class MappingFile
    {
        public static GunMapping Load(string path)
        {
            if (!File.Exists(path))
                return new GunMapping(
                    new Dictionary<GunButton, MouseButton>(),
                    new Dictionary<GunButton, byte>(),
                    new[] { $"{path} not found (empty mapping)." });

            return Parse(File.ReadAllLines(path));
        }

        public static GunMapping Parse(IEnumerable<string> lines)
        {
            var mouse = new Dictionary<GunButton, MouseButton>();
            var keyboard = new Dictionary<GunButton, byte>();
            var diagnostics = new List<string>();

            int lineNo = 0;
            foreach (var raw in lines)
            {
                lineNo++;

                var line = raw?.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    diagnostics.Add($"Line {lineNo}: no '=' separator: {line}");
                    continue;
                }

                var left = line.Substring(0, eq).Trim();
                var right = line.Substring(eq + 1).Trim();

                var dot = left.IndexOf('.');
                if (dot <= 0)
                {
                    diagnostics.Add($"Line {lineNo}: no '.' between device and command: {left}");
                    continue;
                }

                var device = left.Substring(0, dot).ToUpperInvariant();
                var cmd = left.Substring(dot + 1).Trim();

                if (!Enum.TryParse<GunButton>(right, ignoreCase: true, out var gunBtn)
                    || !Enum.IsDefined(gunBtn))
                {
                    diagnostics.Add($"Line {lineNo}: unknown gun command: {right}");
                    continue;
                }

                switch (device)
                {
                    case "MOUSE":
                        if (Enum.TryParse<MouseButton>(cmd, ignoreCase: true, out var mb)
                            && Enum.IsDefined(mb))
                            mouse[gunBtn] = mb;
                        else
                            diagnostics.Add($"Line {lineNo}: unknown mouse button: {cmd}");
                        break;

                    case "KEYBOARD":
                        if (!byte.TryParse(cmd, NumberStyles.None, CultureInfo.InvariantCulture, out var code))
                            diagnostics.Add($"Line {lineNo}: not a keycode: {cmd}");
                        else if (!KeyCodeTable.IsValid(code))
                            diagnostics.Add($"Line {lineNo}: unknown keycode: {cmd} (run with the 'keys' argument for the list)");
                        else
                            keyboard[gunBtn] = code;
                        break;

                    default:
                        diagnostics.Add($"Line {lineNo}: unknown device: {device}");
                        break;
                }
            }

            return new GunMapping(mouse, keyboard, diagnostics);
        }
    }
}
