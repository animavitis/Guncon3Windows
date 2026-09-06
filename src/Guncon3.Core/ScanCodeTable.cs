// SPDX-License-Identifier: GPL-2.0-only
namespace Guncon3.Core
{
    /// <summary>
    /// How SendInput has to say one key: a PS/2 set 1 make code and whether it carries
    /// the E0 prefix (KEYEVENTF_EXTENDEDKEY) — or, for the one key that has no
    /// single-byte make code, a virtual-key code instead. Exactly one of
    /// <see cref="Code"/> and <see cref="VirtualKey"/> is non-zero.
    /// </summary>
    public readonly record struct ScanCode(ushort Code, bool Extended, ushort VirtualKey)
    {
        public bool UsesVirtualKey => VirtualKey != 0;
    }

    /// <summary>
    /// USB HID keyboard usage (the codes in mapping.txt and <see cref="KeyCodeTable"/>)
    /// to the scan code SendInput wants. This is the standard HID-to-PS/2 translation
    /// every USB keyboard driver applies, with two legacy quirks kept on purpose
    /// because users pick keys by the label the "keys" command prints, not by the
    /// number: code 52 is labelled "dummy5" and is the apostrophe key (HID 0x34); codes
    /// 100–111 are labelled F13–F24 (standard HID puts those at 104–115) and translate
    /// as F13–F24. The table has an entry for every code KeyCodeTable accepts and for
    /// nothing else; a test keeps the two in lockstep.
    /// </summary>
    public static class ScanCodeTable
    {
        /// <summary>VK_PAUSE. Pause is E1 1D 45 on the wire, which KEYBDINPUT cannot express as a scan
        /// code, so it is the one key sent by virtual key.</summary>
        private const ushort VkPause = 0x13;

        private static readonly ScanCode?[] _byUsage = Build();

        /// <summary>The scan code for a usage, or null when mapping.txt may not use that code.</summary>
        public static ScanCode? Lookup(byte usage) => _byUsage[usage];

        public static bool TryGet(byte usage, out ScanCode scan)
        {
            var found = _byUsage[usage];
            scan = found.GetValueOrDefault();
            return found.HasValue;
        }

        private static ScanCode?[] Build()
        {
            var t = new ScanCode?[256];

            void Plain(int usage, int code) => t[usage] = new ScanCode((ushort)code, false, 0);
            void Ext(int usage, int code) => t[usage] = new ScanCode((ushort)code, true, 0);

            // a..z are usages 4..29; the make codes follow the QWERTY rows, not the alphabet.
            int[] letters =
            {
                0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32,
                0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C
            };
            for (int i = 0; i < letters.Length; i++) Plain(4 + i, letters[i]);

            // 1..9 then 0 are usages 30..39 and make codes 0x02..0x0B, in the same order.
            for (int i = 0; i < 10; i++) Plain(30 + i, 0x02 + i);

            Plain(40, 0x1C);   // ENTER
            Plain(41, 0x01);   // ESCAPE
            Plain(42, 0x0E);   // BACKSPACE
            Plain(43, 0x0F);   // TAB
            Plain(44, 0x39);   // SPACEBAR
            Plain(45, 0x0C);   // -
            Plain(46, 0x0D);   // =
            Plain(47, 0x1A);   // [
            Plain(48, 0x1B);   // ]
            Plain(49, 0x2B);   // \
            Plain(51, 0x27);   // ;
            Plain(52, 0x28);   // ' ("dummy5" in KeyCodeTable)
            Plain(53, 0x29);   // `
            Plain(54, 0x33);   // ,
            Plain(55, 0x34);   // .
            Plain(56, 0x35);   // /
            Plain(57, 0x3A);   // CAPSLOCK

            // F1..F10 are usages 58..67 and make codes 0x3B..0x44.
            for (int i = 0; i < 10; i++) Plain(58 + i, 0x3B + i);
            Plain(68, 0x57);   // F11
            Plain(69, 0x58);   // F12

            Ext(70, 0x37);     // PRINTSCREEN
            Plain(71, 0x46);   // SCROLLLOCK
            t[72] = new ScanCode(0, false, VkPause);   // PAUSE
            Ext(73, 0x52);     // INSERT
            Ext(74, 0x47);     // HOME
            Ext(75, 0x49);     // PAGEUP
            Ext(76, 0x53);     // DELETE
            Ext(77, 0x4F);     // END
            Ext(78, 0x51);     // PAGEDOWN
            Ext(79, 0x4D);     // RIGHTARROW
            Ext(80, 0x4B);     // LEFTARROW
            Ext(81, 0x50);     // DOWNARROW
            Ext(82, 0x48);     // UPARROW

            Plain(83, 0x45);   // NUMLOCK
            Ext(84, 0x35);     // K/
            Plain(85, 0x37);   // K*
            Plain(86, 0x4A);   // K-
            Plain(87, 0x4E);   // K+
            Ext(88, 0x1C);     // KENTER
            Plain(89, 0x4F);   // K1
            Plain(90, 0x50);   // K2
            Plain(91, 0x51);   // K3
            Plain(92, 0x4B);   // K4
            Plain(93, 0x4C);   // K5
            Plain(94, 0x4D);   // K6
            Plain(95, 0x47);   // K7
            Plain(96, 0x48);   // K8
            Plain(97, 0x49);   // K9
            Plain(98, 0x52);   // K0
            Plain(99, 0x53);   // K.

            // F13..F23 are KeyCodeTable's 100..110 and make codes 0x64..0x6E; F24 is 0x76.
            for (int i = 0; i < 11; i++) Plain(100 + i, 0x64 + i);
            Plain(111, 0x76);  // F24

            return t;
        }
    }
}
