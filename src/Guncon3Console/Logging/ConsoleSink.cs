// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.IO;

namespace Guncon3Console.Logging
{
    /// <summary>Coloured console output. Runs inside <see cref="Log"/>'s lock, so the colour change and the
    /// write that follows are already one critical section.</summary>
    internal sealed class ConsoleSink : ILogSink
    {
        public void Write(in LogEntry entry)
        {
            try
            {
                if (entry.Colour.HasValue) Console.ForegroundColor = entry.Colour.Value;
                Console.WriteLine(entry.Text);
                if (entry.Colour.HasValue) Console.ResetColor();
            }
            catch (IOException)
            {
                // The console went away (the window was closed under us). Losing the line is the only sensible
                // outcome; the other sinks still have it.
            }
        }
    }
}
