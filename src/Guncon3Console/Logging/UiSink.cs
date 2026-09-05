// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;

namespace Guncon3Console.Logging
{
    /// <summary>
    /// Keeps the last lines for the window's log tab. Two buffers under one lock: a ring
    /// of everything still worth showing and a pending queue the form drains on a timer,
    /// so a burst of worker-thread lines costs one UI update, not one each.
    /// </summary>
    internal sealed class UiSink : ILogSink
    {
        /// <summary>
        /// Lines kept for the log tab. Two thousand is several seconds of a worst-case
        /// burst and a trivial amount of memory; older lines are still in guncon3.log
        /// when that is on. Internal so MainForm can bound its RichTextBox to the same
        /// figure instead of guessing a second one.
        /// </summary>
        internal const int Capacity = 2000;

        private readonly object _gate = new object();
        private readonly Queue<LogEntry> _buffer = new Queue<LogEntry>(Capacity);
        private readonly Queue<LogEntry> _pending = new Queue<LogEntry>();

        /// <summary>
        /// Raised for every line, on whichever thread logged it, while the Log lock is
        /// held. A handler may only set a flag — anything else runs a worker thread's
        /// work under two locks.
        /// </summary>
        public event Action<LogEntry> Appended;

        public void Write(in LogEntry entry)
        {
            lock (_gate)
            {
                if (_buffer.Count == Capacity) _buffer.Dequeue();
                _buffer.Enqueue(entry);
                _pending.Enqueue(entry);
            }

            Appended?.Invoke(entry);
        }

        /// <summary>Everything buffered so far, oldest first. Clears the pending queue with it, so nothing is
        /// handed out twice.</summary>
        public LogEntry[] Snapshot()
        {
            lock (_gate)
            {
                _pending.Clear();
                return _buffer.ToArray();
            }
        }

        /// <summary>Lines logged since the last <see cref="Snapshot"/> or <see cref="Drain"/>.</summary>
        public LogEntry[] Drain()
        {
            lock (_gate)
            {
                if (_pending.Count == 0) return Array.Empty<LogEntry>();

                var lines = _pending.ToArray();
                _pending.Clear();
                return lines;
            }
        }
    }
}
