// SPDX-License-Identifier: GPL-2.0-only
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Guncon3Console.TetherScript
{
    public class LogEventArgs : EventArgs { public string Msg { get; set; } }

    /// <summary>Opens a TetherScript virtual HID device by vendor and product id and sends feature reports to
    /// it. One instance per virtual device.</summary>
    sealed class HIDController
    {
        public event EventHandler<LogEventArgs> OnLog;

        private SafeFileHandle _handle;

        public bool Connected { get; private set; }

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid interfaceClassGuid;
            public int flags;
            private IntPtr reserved;
        }

        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        private static extern void HidD_GetHidGuid(out Guid ClassGuid);

        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        private static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] Buffer, uint BufferLength);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo, IntPtr devInfo, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string fileName, FileAccess fileAccess, FileShare fileShare, IntPtr securityAttributes, FileMode creationDisposition, uint flagsAndAttributes, IntPtr template);

        private void DoLog(string msg)
        {
            try { OnLog?.Invoke(this, new LogEventArgs { Msg = msg }); }
            catch (Exception) { /* a logging failure must not take the feeder down */ }
        }

        /// <summary>Opens the first present HID interface whose attributes match. Sets <see
        /// cref="Connected"/>.</summary>
        public void Connect(ushort vendorId, ushort productId)
        {
            DoLog("Connecting...");
            if (Connected) { DoLog("Already connected."); return; }

            foreach (var path in EnumerateHidPaths())
            {
                var h = CreateFile(path, FileAccess.ReadWrite, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
                if (h.IsInvalid)
                    h = CreateFile(path, 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
                if (h.IsInvalid) { h.Dispose(); continue; }

                var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (HidD_GetAttributes(h, ref a) && a.VendorID == vendorId && a.ProductID == productId)
                {
                    _handle = h;
                    Connected = true;
                    DoLog("Connected.");
                    return;
                }

                h.Dispose();
            }

            DoLog($"No HID device with VID=0x{vendorId:X4} PID=0x{productId:X4} is present.");
        }

        /// <summary>Every present HID interface path. The device-info list is destroyed when the enumeration
        /// ends or is abandoned.</summary>
        private IEnumerable<string> EnumerateHidPaths()
        {
            HidD_GetHidGuid(out var hidGuid);

            IntPtr info = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (info == INVALID_HANDLE_VALUE)
            {
                DoLog("SetupDiGetClassDevs failed.");
                yield break;
            }

            try
            {
                for (uint i = 0; ; i++)
                {
                    var ifData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                    if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref hidGuid, i, ref ifData))
                        yield break;

                    SetupDiGetDeviceInterfaceDetail(info, ref ifData, IntPtr.Zero, 0, out uint needed, IntPtr.Zero);

                    IntPtr detail = Marshal.AllocHGlobal((int)needed);
                    string path;
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize: 8 on x64, 6 on x86.
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(info, ref ifData, detail, needed, out needed, IntPtr.Zero))
                            continue;

                        path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                    }
                    finally { Marshal.FreeHGlobal(detail); }

                    if (path != null) yield return path;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(info);
            }
        }

        public void Disconnect()
        {
            Connected = false;
            try { _handle?.Dispose(); } catch (ObjectDisposedException) { }
            _handle = null;
        }

        /// <summary>Returned by <see cref="SendData"/> when there is no open device.</summary>
        public const int NotConnected = -1;

        /// <summary>Sends one feature report. Returns 0 on success, <see cref="NotConnected"/> when there is no
        /// device, otherwise the Win32 error the driver reported. ERROR_INVALID_USER_BUFFER (1784) would mean
        /// the report length is wrong.</summary>
        public int SendData(byte[] buffer, uint bufferLength)
        {
            if (!Connected) return NotConnected;
            return HidD_SetFeature(_handle, buffer, bufferLength + 1) ? 0 : Marshal.GetLastWin32Error();
        }
    }
}
