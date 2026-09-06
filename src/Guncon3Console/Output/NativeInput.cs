// SPDX-License-Identifier: GPL-2.0-only
using System.Runtime.InteropServices;
using Guncon3.Core;

namespace Guncon3Console.Output
{
    /// <summary>The user32 SendInput call and the structures it takes. INPUT is 40 bytes on x64 (a 4-byte type,
    /// 4 bytes of padding, then the 32-byte union); Marshal.SizeOf works that out from the layout below, so
    /// nothing here hard-codes it.</summary>
    internal static class NativeInput
    {
        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;

        private const uint KeyEventExtendedKey = 0x0001;
        private const uint KeyEventKeyUp = 0x0002;
        private const uint KeyEventScanCode = 0x0008;

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public nuint dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public nuint dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        private static readonly int InputSize = Marshal.SizeOf<INPUT>();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        /// <summary>An absolute move and/or button change. <paramref name="flags"/> comes from
        /// <see cref="MouseEventFlags.For"/>; x and y are 0..65535 over the virtual desktop and are ignored by
        /// Windows when the flags carry no move.</summary>
        public static INPUT Mouse(int x, int y, uint flags) => new INPUT
        {
            type = InputMouse,
            u = new InputUnion { mi = new MOUSEINPUT { dx = x, dy = y, dwFlags = flags } }
        };

        /// <summary>One key going down or up, by scan code — or by virtual key for the one key
        /// <see cref="ScanCodeTable"/> cannot express as a scan code.</summary>
        public static INPUT Key(ScanCode key, bool up)
        {
            uint flags = up ? KeyEventKeyUp : 0;
            ushort vk = 0, scan = 0;

            if (key.UsesVirtualKey)
            {
                vk = key.VirtualKey;
            }
            else
            {
                scan = key.Code;
                flags |= KeyEventScanCode;
                if (key.Extended) flags |= KeyEventExtendedKey;
            }

            return new INPUT
            {
                type = InputKeyboard,
                u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } }
            };
        }

        /// <summary>Sends the first <paramref name="count"/> entries as one batch. Returns null when every one
        /// was inserted, otherwise why not — fewer inserted with no Win32 error is what a blocked input queue
        /// looks like; win32 error 5 is UIPI, the foreground window being more elevated than this process.</summary>
        public static string Send(INPUT[] inputs, int count)
        {
            if (count == 0) return null;

            uint sent = SendInput((uint)count, inputs, InputSize);
            if (sent == count) return null;

            int error = Marshal.GetLastWin32Error();
            return error == 0
                ? $"SendInput inserted {sent} of {count} events"
                : $"SendInput inserted {sent} of {count} events (win32 error {error})";
        }
    }
}
