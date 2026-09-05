// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Globalization;
using System.IO;
using System.Security;

namespace Guncon3Console.Logging
{
    /// <summary>Appends every line to guncon3.log next to the exe, flushed per line and never rotated: a crash
    /// must not be the reason the lines before it are missing, and a log the user has to find is a log they
    /// will never find twice.</summary>
    internal sealed class FileSink : ILogSink, IDisposable
    {
        private const string FileName = "guncon3.log";

        private readonly StreamWriter _writer;

        private FileSink(StreamWriter writer) => _writer = writer;

        /// <summary>Where the log goes. Shown in the settings dialog.</summary>
        public static string FilePath => Path.Combine(AppInfo.BaseDirectory, FileName);

        /// <summary>Opens the log for appending. Returns null and the reason when it cannot be opened — a log
        /// that cannot be written is a warning, never a reason to fail a start.</summary>
        public static FileSink TryOpen(out string error)
        {
            try
            {
                var writer = new StreamWriter(FilePath, append: true) { AutoFlush = true };
                error = null;
                return new FileSink(writer);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
                                          or ArgumentException or NotSupportedException)
            {
                error = ex.Message;
                return null;
            }
        }

        public void Write(in LogEntry entry)
        {
            _writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{entry.Time:yyyy-MM-dd HH:mm:ss.fff} [{Tag(entry.Level)}] {entry.Text}"));
        }

        /// <summary>One character per level: I(nfo), W(arn), E(rror), H(eader).</summary>
        private static char Tag(LogLevel level) => level switch
        {
            LogLevel.Warn => 'W',
            LogLevel.Error => 'E',
            LogLevel.Header => 'H',
            _ => 'I'
        };

        /// <summary>Remove the sink from <see cref="Log"/> before disposing it, not after.</summary>
        public void Dispose() => _writer.Dispose();
    }
}
