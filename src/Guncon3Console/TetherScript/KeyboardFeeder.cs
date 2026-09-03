using System;
using System.Diagnostics;
using GunconUSB;
using Guncon3.Core;

namespace Guncon3Console.TetherScript
{
    class KeyboardFeeder
    {
        private readonly HIDController HID = new HIDController();
        private readonly GunState _state;

        private readonly uint FTimeout = 5000;

        private static readonly int ReportSize = HidReport.SizeOf<SetFeatureKeyboard>();
        private readonly byte[] _reportBuffer = new byte[ReportSize + 1];

        private readonly KeySetBuilder _keys = new KeySetBuilder();

        private static readonly long PingIntervalTicks = Stopwatch.Frequency;   // 1000 ms
        private long _lastSendTimestamp;

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
                    ConsoleLog.Line("Keyboard feeder recovered.");
                }
                _consecutiveFailures = 0;
                return;
            }

            if (++_consecutiveFailures < FailuresBeforeUnhealthy || !Healthy)
                return;

            Healthy = false;
            ConsoleLog.Warn($"Keyboard feeder stopped accepting reports after {_consecutiveFailures} failures.");
        }

        public KeyboardFeeder(GunState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public void Connect()
        {
            HID.OnLog += Log;
            HID.VendorID = (ushort)DriversConst.TTC_VENDORID;
            HID.ProductID = (ushort)DriversConst.TTC_PRODUCTID_KEYBOARD;
            HID.Connect();
            if (!HID.Connected)
                throw new Exception("Could not connect to TetherScript Keyboard.");

            _keys.Reset();
            _lastSendTimestamp = Stopwatch.GetTimestamp();
        }

        public void Disconnect()
        {
            try
            {
                Send(0, 0, 0, 0, 0, 0, 0, 0);
            }
            catch { }
            HID.Disconnect();
            HID.OnLog -= Log;
        }

        private void Log(object s, LogArgs e) => ConsoleLog.Line("Keyboard " + e.Msg);

        public void Send(byte Modifier, byte Padding, byte Key0, byte Key1, byte Key2, byte Key3, byte Key4, byte Key5)
        {
            SetFeatureKeyboard data = new SetFeatureKeyboard
            {
                ReportID = 1,
                CommandCode = 2,
                Timeout = FTimeout / 5,
                Modifier = Modifier,
                Padding = Padding,
                Key0 = Key0,
                Key1 = Key1,
                Key2 = Key2,
                Key3 = Key3,
                Key4 = Key4,
                Key5 = Key5
            };

            HidReport.Write(in data, _reportBuffer);
            Track(HID.SendData(_reportBuffer, (uint)ReportSize));
        }

        public void Ping()
        {
            SetFeatureKeyboard data = new SetFeatureKeyboard
            {
                ReportID = 1,
                CommandCode = 3,
                Timeout = FTimeout / 5
            };

            HidReport.Write(in data, _reportBuffer);
            Track(HID.SendData(_reportBuffer, (uint)ReportSize));
        }

        internal void Feed(GunMapping mapping)
        {
            bool changed = _keys.Update(mapping.KeyboardPairs, _state.BtnState);

            if (changed)
            {
                var k = _keys.Keys;

                Send(0, 0,
                    k.Length > 0 ? k[0] : (byte)0,
                    k.Length > 1 ? k[1] : (byte)0,
                    k.Length > 2 ? k[2] : (byte)0,
                    k.Length > 3 ? k[3] : (byte)0,
                    k.Length > 4 ? k[4] : (byte)0,
                    k.Length > 5 ? k[5] : (byte)0);

                _lastSendTimestamp = Stopwatch.GetTimestamp();
                return;
            }

            if (Stopwatch.GetTimestamp() - _lastSendTimestamp >= PingIntervalTicks)
            {
                Ping();
                _lastSendTimestamp = Stopwatch.GetTimestamp();
            }
        }
    }
}
