// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace Guncon3.Core
{
    /// <summary>Writes a file by filling <c>path + ".tmp"</c> and moving it over the target, so a failure
    /// half-way through leaves the previous file intact. Every exception reaches the caller after the temporary
    /// file has been cleaned up.</summary>
    public static class AtomicFile
    {
        /// <summary>Writes through a <see cref="TextWriter"/> over the temporary file.</summary>
        public static void Write(string path, Action<TextWriter> write)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(write);

            string tmp = path + ".tmp";
            try
            {
                using (var writer = new StreamWriter(tmp, false))
                    write(writer);

                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                DeleteQuietly(tmp);
                throw;
            }
        }

        /// <summary>Writes whole lines, one per element.</summary>
        public static void WriteLines(string path, IEnumerable<string> lines)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(lines);

            string tmp = path + ".tmp";
            try
            {
                File.WriteAllLines(tmp, lines);
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                DeleteQuietly(tmp);
                throw;
            }
        }

        private static void DeleteQuietly(string tmp)
        {
            try { File.Delete(tmp); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException or SecurityException)
            {
                // The half-written temporary file is litter, not a failure.
            }
        }
    }
}
