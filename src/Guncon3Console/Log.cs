// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using Guncon3Console.Logging;

namespace Guncon3Console
{
    /// <summary>
    /// The only thing in the application permitted to say something. Every call is
    /// fanned out to the registered sinks under one lock, in registration order, so two
    /// threads never interleave inside a sink and a colour change can never smear across
    /// another thread's line. A sink must not call back in here.
    /// </summary>
    internal static class Log
    {
        private static readonly object _gate = new object();

        /// <summary>
        /// Replaced wholesale rather than mutated, so <see cref="Write"/> iterates an
        /// array nothing else is touching even if a sink is added while it runs.
        /// </summary>
        private static ILogSink[] _sinks = Array.Empty<ILogSink>();

        /// <summary>
        /// What <see cref="Fatal"/> does instead of waiting for a console key. Null
        /// keeps the console behaviour; the GUI host sets it to a message box.
        /// </summary>
        public static Action<string, Exception> FatalHandler { get; set; }

        public static void AddSink(ILogSink sink)
        {
            ArgumentNullException.ThrowIfNull(sink);

            lock (_gate)
            {
                var next = new List<ILogSink>(_sinks) { sink };
                _sinks = next.ToArray();
            }
        }

        public static void RemoveSink(ILogSink sink)
        {
            lock (_gate) RemoveLocked(sink);
        }

        public static void Line(string msg) => Write(msg, LogLevel.Line, null);

        public static void Warn(string msg) => Write(msg, LogLevel.Warn, ConsoleColor.Yellow);

        public static void Error(string msg) => Write(msg, LogLevel.Error, ConsoleColor.Red);

        /// <summary>A banner line. White by default; the build-info lines use green.</summary>
        public static void Header(string msg, ConsoleColor colour = ConsoleColor.White) => Write(msg, LogLevel.Header, colour);

        public static void Blank() => Write(string.Empty, LogLevel.Line, null);

        /// <summary>
        /// Reports a fatal problem and sets the process exit code to 1. Without a
        /// <see cref="FatalHandler"/> it then waits for a key, so a double-clicked exe
        /// does not vanish before the message can be read. The caller still has to
        /// return; this only reports and marks the outcome.
        /// </summary>
        public static void Fatal(string msg, Exception ex = null)
        {
            Error(msg);
            if (ex != null) Error(ex.ToString());
            Environment.ExitCode = 1;

            var handler = FatalHandler;
            if (handler != null)
            {
                handler(msg, ex);
                return;
            }

            Line("Press any key to exit.");
            // Any failure to wait for a key must not mask the fatal message.
            try { Console.ReadKey(true); } catch (Exception) { }
        }

        private static void Write(string msg, LogLevel level, ConsoleColor? colour)
        {
            var entry = new LogEntry(DateTime.Now, level, msg ?? string.Empty, colour);

            lock (_gate)
            {
                foreach (var sink in _sinks)
                {
                    try
                    {
                        sink.Write(in entry);
                    }
                    catch (Exception)
                    {
                        // A sink that throws - a full disk under FileSink - must not
                        // take down the thread that was only trying to say something.
                        // Drop it and keep the others; the console still gets the line.
                        RemoveLocked(sink);
                    }
                }
            }
        }

        /// <summary>Caller already holds <c>_gate</c>.</summary>
        private static void RemoveLocked(ILogSink sink)
        {
            var next = new List<ILogSink>(_sinks);
            next.Remove(sink);
            _sinks = next.ToArray();
        }
    }
}
