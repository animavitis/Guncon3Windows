using System.Collections.Generic;

namespace Guncon3.Core
{
    /// <summary>
    /// USB HID usage codes accepted in mapping.txt, with the names printed by
    /// the "keys" command.
    /// </summary>
    public static class KeyCodeTable
    {
        private static readonly (byte Code, string Name)[] _entries =
        {
            (4, "a"), (5, "b"), (6, "c"), (7, "d"), (8, "e"), (9, "f"),
            (10, "g"), (11, "h"), (12, "i"), (13, "j"), (14, "k"), (15, "l"),
            (16, "m"), (17, "n"), (18, "o"), (19, "p"), (20, "q"), (21, "r"),
            (22, "s"), (23, "t"), (24, "u"), (25, "v"), (26, "w"), (27, "x"),
            (28, "y"), (29, "z"),

            (30, "1"), (31, "2"), (32, "3"), (33, "4"), (34, "5"),
            (35, "6"), (36, "7"), (37, "8"), (38, "9"), (39, "0"),

            (40, "ENTER"), (41, "ESCAPE"), (42, "BACKSPACE"), (43, "TAB"),
            (44, "SPACEBAR"), (45, "-"), (46, "="), (47, "["), (48, "]"),
            (49, "\\"), (51, ";"), (52, "dummy5"), (53, "`"), (54, ","),
            (55, "."), (56, "/"),

            (57, "CAPSLOCK"),
            (58, "F1"), (59, "F2"), (60, "F3"), (61, "F4"), (62, "F5"),
            (63, "F6"), (64, "F7"), (65, "F8"), (66, "F9"), (67, "F10"),
            (68, "F11"), (69, "F12"),

            (70, "PRINTSCREEN"), (71, "SCROLLLOCK"), (72, "PAUSE"), (73, "INSERT"),
            (74, "HOME"), (75, "PAGEUP"), (76, "DELETE"), (77, "END"), (78, "PAGEDOWN"),
            (79, "RIGHTARROW"), (80, "LEFTARROW"), (81, "DOWNARROW"), (82, "UPARROW"),

            (83, "NUMLOCK"), (84, "K/"), (85, "K*"), (86, "K-"), (87, "K+"),
            (88, "KENTER"), (89, "K1"), (90, "K2"), (91, "K3"), (92, "K4"),
            (93, "K5"), (94, "K6"), (95, "K7"), (96, "K8"), (97, "K9"),
            (98, "K0"), (99, "K."),

            (100, "F13"), (101, "F14"), (102, "F15"), (103, "F16"),
            (104, "F17"), (105, "F18"), (106, "F19"), (107, "F20"),
            (108, "F21"), (109, "F22"), (110, "F23"), (111, "F24")
        };

        private static readonly string[] _byCode = BuildIndex();

        public static IReadOnlyList<(byte Code, string Name)> Entries => _entries;

        /// <summary>Name for a keycode, or null when the code has no name.</summary>
        public static string NameOf(byte code) => _byCode[code];

        private static string[] BuildIndex()
        {
            var index = new string[256];
            foreach (var (code, name) in _entries)
                index[code] = name;
            return index;
        }
    }
}
