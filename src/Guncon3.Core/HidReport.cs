// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Runtime.InteropServices;

namespace Guncon3.Core
{
    /// <summary>Packs a Pack = 1 report struct into a caller-owned buffer with no allocation and no unmanaged
    /// round trip.</summary>
    public static class HidReport
    {
        private static class CachedSize<T> where T : struct
        {
            public static readonly int Value = Checked();

            private static int Checked()
            {
                int marshalled = Marshal.SizeOf<T>();
                int managed = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
                if (marshalled != managed)
                    throw new InvalidOperationException(
                        $"{typeof(T).Name}: Marshal.SizeOf is {marshalled} but the managed layout Write copies is {managed}; the struct is not Pack = 1 blittable.");
                return marshalled;
            }
        }

        /// <summary>Marshalled size of the report, computed once per type.</summary>
        public static int SizeOf<T>() where T : struct => CachedSize<T>.Value;

        /// <summary>Writes the report to the start of <paramref name="buffer"/>. The buffer may be longer than
        /// the report; trailing bytes are left untouched.</summary>
        public static void Write<T>(in T value, byte[] buffer) where T : struct
        {
            ArgumentNullException.ThrowIfNull(buffer);

            int size = CachedSize<T>.Value;
            if (buffer.Length < size)
                throw new ArgumentException($"buffer is {buffer.Length} bytes, report needs {size}", nameof(buffer));

            MemoryMarshal.Write(buffer.AsSpan(0, size), in value);
        }
    }
}
