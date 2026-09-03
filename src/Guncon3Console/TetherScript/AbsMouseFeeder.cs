using System;
using System.Diagnostics;
using GunconUSB;
using Guncon3.Core;

namespace Guncon3Console.TetherScript
{
    class AbsMouseFeeder
    {
        private readonly HIDController HID = new HIDController();
        private readonly GunState _state;

        public bool Force4by3 = false;
        private byte btns = 0;

        private static readonly int ReportSize = HidReport.SizeOf<SetFeatureMouseAbs>();
        private readonly byte[] _reportBuffer = new byte[ReportSize + 1];

        private const int FailuresBeforeUnhealthy = 10;
        private int _consecutiveFailures;

        /// <summary>False once the virtual device has rejected several reports in a row.</summary>
        public bool Healthy { get; private set; } = true;

        private void Track(bool sent)
        {
            if (sent)
            {
                if (!Healthy)
                {
                    Healthy = true;
                    ConsoleLog.Line("Mouse feeder recovered.");
                }
                _consecutiveFailures = 0;
                return;
            }

            if (++_consecutiveFailures < FailuresBeforeUnhealthy || !Healthy)
                return;

            Healthy = false;
            ConsoleLog.Warn($"Mouse feeder stopped accepting reports after {_consecutiveFailures} failures.");
        }

        private static readonly long RefreshIntervalTicks = Stopwatch.Frequency;   // 1000 ms
        private long _lastSendTimestamp;
        private ushort _lastX;
        private ushort _lastY;
        private byte _lastButtons = 0xFF;   // impossible value, forces the first send

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

            _lastButtons = 0xFF;
            _lastSendTimestamp = 0;
        }

        public void Disconnect()
        {
            HID.Disconnect();
            HID.OnLog -= Log;
        }

        private void Log(object s, LogArgs e) => ConsoleLog.Line("Mouse " + e.Msg);

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

            HidReport.Write(in data, _reportBuffer);
            Track(HID.SendData(_reportBuffer, (uint)ReportSize));
        }

        internal void Feed(GunMapping mapping)
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
            foreach (var map in mapping.MousePairs)
            {
                if (!_state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                if (map.Value == MouseButton.Left) btns |= 1;
                else if (map.Value == MouseButton.Right) btns |= 1 << 1;
                else if (map.Value == MouseButton.Middle) btns |= 1 << 2;
            }

            ushort x = (ushort)absX;
            ushort y = (ushort)absY;

            bool changed = x != _lastX || y != _lastY || btns != _lastButtons;
            bool stale = Stopwatch.GetTimestamp() - _lastSendTimestamp >= RefreshIntervalTicks;

            if (!changed && !stale) return;

            Send_Data_To_MouseAbs(x, y);

            _lastX = x;
            _lastY = y;
            _lastButtons = btns;
            _lastSendTimestamp = Stopwatch.GetTimestamp();
        }
    }
}
