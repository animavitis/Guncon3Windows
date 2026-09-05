// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;

namespace Guncon3.Core.Tests
{
    /// <summary>Captures shared by several test classes.</summary>
    internal static class Fixtures
    {
        /// <summary>
        /// A gun held to the left of the screen: the right edge of the captured
        /// quadrilateral is vertically compressed, and raw Y decreases downward.
        /// Corners P0..P3 then the centre.
        /// </summary>
        public static List<(double X, double Y)> Keystone(double centreX = 1000, double centreY = 1000) => new()
        {
            (200, 1800),   // P0 top-left
            (1800, 1600),  // P1 top-right
            (1800, 400),   // P2 bottom-right
            (200, 200),    // P3 bottom-left
            (centreX, centreY)
        };
    }

    /// <summary>
    /// A path under the system temporary directory that no file occupies yet, deleted
    /// on dispose. Creating the file is the test's job — several of these tests assert
    /// that the code under test creates it.
    /// </summary>
    internal sealed class TempFile : IDisposable
    {
        public TempFile(string prefix = "")
            => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + System.IO.Path.GetRandomFileName());

        /// <summary>The path. Nothing has been written to it by this class.</summary>
        public string Path { get; }

        /// <summary>
        /// Deletes the file if it is there, and any ".tmp" an atomic saver left behind
        /// from a write that failed partway through. A file something else still holds
        /// open leaves litter in the temporary directory rather than failing a test that
        /// has already made its assertions.
        /// </summary>
        public void Dispose()
        {
            try { File.Delete(Path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            try { File.Delete(Path + ".tmp"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
