using System;
using System.ComponentModel;

namespace Guncon3.Core
{
    /// <summary>
    /// Classifies the Win32 failures that surface from a USB transfer.
    /// </summary>
    public static class UsbErrors
    {
        /// <summary>ERROR_SEM_TIMEOUT — a transfer that did not complete in time.</summary>
        public const int SemTimeout = 121;

        /// <summary>
        /// True when this exception, or anything it wraps, is a Win32 timeout.
        /// A timed-out transfer means the device is still there and simply did
        /// not answer, which is a dropped frame rather than a disconnection.
        /// </summary>
        public static bool IsTransferTimeout(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
                if (e is Win32Exception w && w.NativeErrorCode == SemTimeout)
                    return true;

            return false;
        }

        /// <summary>
        /// The Win32 error code carried by this exception or anything it wraps, or 0
        /// when there is none. Diagnostic only — it exists so a log line can say what
        /// the driver actually reported.
        /// </summary>
        public static int Win32CodeOf(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
                if (e is Win32Exception w)
                    return w.NativeErrorCode;

            return 0;
        }
    }
}
