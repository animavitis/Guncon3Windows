using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class UsbErrorsTests
    {
        [Fact]
        public void IsTransferTimeout_BareWin32Exception121_IsTimeout()
        {
            var ex = new Win32Exception(121);

            Assert.True(UsbErrors.IsTransferTimeout(ex));
        }

        [Fact]
        public void IsTransferTimeout_OtherWin32ErrorCode_IsNotTimeout()
        {
            // ERROR_BAD_DRIVER or similar — any code other than ERROR_SEM_TIMEOUT.
            var ex = new Win32Exception(1167);

            Assert.False(UsbErrors.IsTransferTimeout(ex));
        }

        [Fact]
        public void IsTransferTimeout_NestedTwoLevelsDeep_IsFound()
        {
            var inner = new Win32Exception(121);
            var middle = new InvalidOperationException("wrapped", inner);
            var outer = new Exception("Failed to read from pipe.", middle);

            Assert.True(UsbErrors.IsTransferTimeout(outer));
        }

        [Fact]
        public void IsTransferTimeout_NestedThreeLevelsDeep_IsFound()
        {
            var inner = new Win32Exception(121);
            var level2 = new InvalidOperationException("wrapped", inner);
            var level3 = new ApplicationException("wrapped again", level2);
            var outer = new Exception("Failed to read from pipe.", level3);

            Assert.True(UsbErrors.IsTransferTimeout(outer));
        }

        [Fact]
        public void IsTransferTimeout_NoWin32ExceptionAnywhere_IsNotTimeout()
        {
            var inner = new InvalidOperationException("some other failure");
            var outer = new Exception("Failed to read from pipe.", inner);

            Assert.False(UsbErrors.IsTransferTimeout(outer));
        }

        [Fact]
        public void IsTransferTimeout_Null_DoesNotThrow()
        {
            Assert.False(UsbErrors.IsTransferTimeout(null));
        }

        [Fact]
        public void Win32CodeOf_BareWin32Exception_ReturnsItsCode()
        {
            var ex = new Win32Exception(121);

            Assert.Equal(121, UsbErrors.Win32CodeOf(ex));
        }

        [Fact]
        public void Win32CodeOf_NestedWin32Exception_ReturnsItsCode()
        {
            var inner = new Win32Exception(995);
            var outer = new Exception("Failed to read from pipe.", inner);

            Assert.Equal(995, UsbErrors.Win32CodeOf(outer));
        }

        [Fact]
        public void Win32CodeOf_NoWin32ExceptionAnywhere_ReturnsZero()
        {
            var inner = new InvalidOperationException("some other failure");
            var outer = new Exception("Failed to read from pipe.", inner);

            Assert.Equal(0, UsbErrors.Win32CodeOf(outer));
        }

        [Fact]
        public void Win32CodeOf_Null_ReturnsZero()
        {
            Assert.Equal(0, UsbErrors.Win32CodeOf(null));
        }

        [Fact]
        public void Win32CodeOf_TwoAtDifferentDepths_FirstFoundWins()
        {
            // Win32Exception has no public constructor that takes both an explicit
            // error code and an inner exception, so the deterministic way to nest two
            // of them is to point the thread's last-P/Invoke-error at each code right
            // before the constructor that reads it runs.
            Marshal.SetLastPInvokeError(995);
            var deepest = new Win32Exception();

            Marshal.SetLastPInvokeError(121);
            var shallow = new Win32Exception("shallower", deepest);

            var outer = new Exception("Failed to read from pipe.", shallow);

            Assert.Equal(121, UsbErrors.Win32CodeOf(outer));
        }
    }
}
