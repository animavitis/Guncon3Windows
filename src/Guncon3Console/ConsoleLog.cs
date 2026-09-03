using System;

namespace Guncon3Console
{
    /// <summary>
    /// The only thing permitted to touch Console once worker threads exist.
    /// Colour changes and the write that follows them must be one critical
    /// section, otherwise two threads interleave and the colours smear.
    /// </summary>
    internal static class ConsoleLog
    {
        private static readonly object _gate = new object();

        public static void Line(string msg) => Write(msg, null);

        public static void Warn(string msg) => Write(msg, ConsoleColor.Yellow);

        public static void Error(string msg) => Write(msg, ConsoleColor.Red);

        /// <summary>A banner line. White by default; the build-info lines use green.</summary>
        public static void Header(string msg, ConsoleColor colour = ConsoleColor.White) => Write(msg, colour);

        public static void Blank()
        {
            lock (_gate) Console.WriteLine();
        }

        private static void Write(string msg, ConsoleColor? colour)
        {
            lock (_gate)
            {
                if (colour.HasValue) Console.ForegroundColor = colour.Value;
                Console.WriteLine(msg);
                if (colour.HasValue) Console.ResetColor();
            }
        }

        /// <summary>
        /// Reports a fatal problem and waits for a key, so a double-clicked exe does
        /// not vanish before the message can be read. The caller decides what to do
        /// next; this only reports.
        /// </summary>
        public static void FailAndExit(string msg, Exception ex = null)
        {
            Error(msg);
            if (ex != null) Error(ex.ToString());
            Line("Press any key to exit.");
            try { Console.ReadKey(true); } catch { }
        }

    }
}
