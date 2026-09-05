// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Diagnostics;
using Guncon3.Core;

namespace Guncon3Console.TetherScript
{
    sealed class KeyboardFeeder : FeederBase<SetFeatureKeyboard>
    {
        private readonly uint FTimeout = 5000;

        private readonly KeySetBuilder _keys = new KeySetBuilder();

        private static readonly long PingIntervalTicks = Stopwatch.Frequency;   // 1000 ms
        private long _lastSendTimestamp;

        /// <summary>The key set the device holds after the last <see cref="Feed"/>, ascending and 0..6 long.
        /// Valid on the worker thread only: the span points at the builder's live buffer, which the next Feed
        /// overwrites.</summary>
        internal ReadOnlySpan<byte> LastKeys => _keys.Keys;

        public KeyboardFeeder(GunState state)
            : base(state, "Keyboard", DriversConst.TTC_PRODUCTID_KEYBOARD)
        {
        }

        protected override void OnConnected()
        {
            _keys.Reset();
            _lastSendTimestamp = Stopwatch.GetTimestamp();
        }

        protected override void ReleaseHeldInputs() => Send(0, 0, 0, 0, 0, 0, 0, 0);

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

            Send(in data);
        }

        public void Ping()
        {
            SetFeatureKeyboard data = new SetFeatureKeyboard
            {
                ReportID = 1,
                CommandCode = 3,
                Timeout = FTimeout / 5
            };

            Send(in data);
        }

        internal void Feed(GunMapping mapping)
        {
            bool changed = _keys.Update(mapping.KeyboardPairs, State.Buttons);

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
