using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GunconUSB;

namespace Guncon3Console.TetherScript
{
    class AbsMouseFeeder
    {
        private readonly HIDController HID = new HIDController();
        private readonly GunState _state;

        public readonly Dictionary<GunButton, MouseButton> Mapping = new Dictionary<GunButton, MouseButton>();

        public bool Force4by3 = false;
        private byte btns = 0;

        public AbsMouseFeeder(GunState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public void Connect()
        {
            HID.OnLog += Log;
            HID.VendorID = (ushort)DriversConst.TTC_VENDORID;
            HID.ProductID = (ushort)DriversConst.TTC_PRODUCTID_MOUSEABS;
            HID.Connect();

            if (!HID.Connected)
                throw new Exception("Could not connect to TetherScript's AbsMouse");
        }

        public void Disconnect()
        {
            HID.Disconnect();
            HID.OnLog -= Log;
        }

        private void Log(object s, LogArgs e) => Console.WriteLine("Mouse " + e.Msg);

        public void Send_Data_To_MouseAbs(ushort x, ushort y)
        {
            var data = new SetFeatureMouseAbs
            {
                ReportID = 1,
                CommandCode = 2,
                Buttons = btns,
                X = x,
                Y = y
            };

            byte[] buf = StructToBytes(data, Marshal.SizeOf(data));
            HID.SendData(buf, (uint)Marshal.SizeOf(data));
        }

        private static byte[] StructToBytes<T>(T value, int size) where T : struct
        {
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, ptr, false);
                Marshal.Copy(ptr, arr, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return arr;
        }

        internal void Feed()
        {
            short absX = 0;
            short absY = 0;

            if (_state.IsInsideScreen)
            {
                absX = _state.ABS_X;
                absY = _state.ABS_Y;

                if (Force4by3)
                    absX = (short)Helper.ConvertRange(4096, 28671, 0, 32767, absX);
            }

            btns = 0;
            foreach (var map in Mapping)
            {
                if (!_state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                if (map.Value == MouseButton.Left) btns = (byte)(btns | 1);
                if (map.Value == MouseButton.Right) btns = (byte)(btns | (1 << 1));
                if (map.Value == MouseButton.Middle) btns = (byte)(btns | (1 << 2));
            }

            Send_Data_To_MouseAbs((ushort)absX, (ushort)absY);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SetFeatureMouseAbs
    {
        public byte ReportID;
        public byte CommandCode;
        public byte Buttons;
        public ushort X;
        public ushort Y;
    }
}
