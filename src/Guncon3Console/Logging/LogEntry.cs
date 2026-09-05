// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace Guncon3Console.Logging
{
    /// <summary>What kind of line this is. Sinks other than the console use this rather than a colour.</summary>
    internal enum LogLevel
    {
        Line,
        Warn,
        Error,
        Header
    }

    /// <summary>
    /// One line of log, as every sink sees it. Colour is yellow for a warning, red for
    /// an error, white or green for a header line, null for an ordinary line.
    /// </summary>
    internal readonly record struct LogEntry(DateTime Time, LogLevel Level, string Text, ConsoleColor? Colour);

    /// <summary>
    /// One destination for log lines. <see cref="Write"/> is called under the Log lock,
    /// in registration order, so an implementation neither needs its own lock against
    /// other sinks nor may call back into <see cref="Log"/> — that would deadlock on the
    /// lock it is already holding.
    /// </summary>
    internal interface ILogSink
    {
        void Write(in LogEntry entry);
    }
}
