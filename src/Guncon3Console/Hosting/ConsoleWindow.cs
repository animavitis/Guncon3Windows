// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Runtime.InteropServices;

namespace Guncon3Console.Hosting
{
    /// <summary>
    /// The exe is a WinExe, so the process starts with no console at all. Every mode
    /// that writes console lines calls <see cref="Ensure"/> first — .NET caches the
    /// console handles the first time anything touches Console, so it has to run
    /// before the first line.
    /// </summary>
    internal static class ConsoleWindow
    {
        /// <summary>AttachConsole's "the parent process" pseudo-id (ATTACH_PARENT_PROCESS).</summary>
        private const uint AttachParentProcess = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        private static bool _ensured;

        /// <summary>
        /// True once this process has a console to write to. False only when both
        /// attaching and allocating failed, in which case console writes go nowhere
        /// and the key loop takes its redirected-input path.
        /// </summary>
        public static bool Available { get; private set; }

        /// <summary>The console window, or <see cref="IntPtr.Zero"/> when there is none.</summary>
        public static IntPtr Handle => Available ? GetConsoleWindow() : IntPtr.Zero;

        /// <summary>Attaches or allocates a console, once per process. Returns <see cref="Available"/>.</summary>
        public static bool Ensure()
        {
            if (_ensured) return Available;
            _ensured = true;

            // Attaching to the shell that launched us keeps `Guncon3Console.exe --console` in that window; a
            // double-clicked exe has no parent console, so it gets its own.
            Available = AttachConsole(AttachParentProcess) || AllocConsole();
            return Available;
        }
    }
}
