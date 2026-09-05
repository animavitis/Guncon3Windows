// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class UsbErrorsTests
    {
        [Theory]
        [InlineData(121, 0)]     // a bare Win32Exception
        [InlineData(1167, 0)]    // ERROR_BAD_DRIVER or similar — any code but ERROR_SEM_TIMEOUT
        [InlineData(121, 2)]
        [InlineData(121, 3)]
        public void IsTransferTimeout_FindsErrorSemTimeoutAtAnyDepthAndNothingElse(int code, int depth)
            => Assert.Equal(code == UsbErrors.SemTimeout,
                            UsbErrors.IsTransferTimeout(Nest(new Win32Exception(code), depth)));

        [Fact]
        public void IsTransferTimeout_NoWin32ExceptionAnywhere_IsNotTimeout()
            => Assert.False(UsbErrors.IsTransferTimeout(Nest(new InvalidOperationException("some other failure"), 1)));

        [Fact]
        public void IsTransferTimeout_Null_DoesNotThrow()
            => Assert.False(UsbErrors.IsTransferTimeout(null));

        [Theory]
        [InlineData(121, 0)]
        [InlineData(995, 1)]
        public void Win32CodeOf_ReturnsTheCodeAtAnyDepth(int code, int depth)
            => Assert.Equal(code, UsbErrors.Win32CodeOf(Nest(new Win32Exception(code), depth)));

        [Fact]
        public void Win32CodeOf_NoWin32ExceptionAnywhere_ReturnsZero()
            => Assert.Equal(0, UsbErrors.Win32CodeOf(Nest(new InvalidOperationException("some other failure"), 1)));

        [Fact]
        public void Win32CodeOf_Null_ReturnsZero()
            => Assert.Equal(0, UsbErrors.Win32CodeOf(null));

        [Fact]
        public void Win32CodeOf_TwoAtDifferentDepths_FirstFoundWins()
        {
            // Win32Exception has no public constructor that takes both an explicit error
            // code and an inner exception, so the deterministic way to nest two of them is
            // to point the thread's last-P/Invoke-error at each code right before the
            // constructor that reads it runs.
            Marshal.SetLastPInvokeError(995);
            var deepest = new Win32Exception();

            Marshal.SetLastPInvokeError(121);
            var shallow = new Win32Exception("shallower", deepest);

            Assert.Equal(121, UsbErrors.Win32CodeOf(Nest(shallow, 1)));
        }

        /// <summary>
        /// Wraps <paramref name="inner"/> in <paramref name="depth"/> layers, alternating
        /// wrapper types: nothing in UsbErrors looks at the wrapper type, only at
        /// InnerException.
        /// </summary>
        private static Exception Nest(Exception inner, int depth)
        {
            var ex = inner;
            for (int i = 0; i < depth; i++)
                ex = i % 2 == 0
                    ? new Exception("Failed to read from pipe.", ex)
                    : new InvalidOperationException("wrapped", ex);

            return ex;
        }
    }
}
