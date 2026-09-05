// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using Guncon3.Core;

namespace Guncon3Console.TetherScript
{
    sealed class AbsMouseFeeder : FeederBase<SetFeatureMouseAbs>
    {
        private static readonly long RefreshIntervalTicks = Stopwatch.Frequency;   // 1000 ms
        private long _lastSendTimestamp;
        private ushort _lastX;
        private ushort _lastY;
        private byte _lastButtons = 0xFF;   // impossible value, forces the first send

        /// <summary>What this feeder decided the device should hold after the last <see cref="Feed"/> — whether
        /// or not that frame was actually sent, because an unchanged report is still what the device is
        /// showing. Written and read on the worker thread only, so it needs no locking.</summary>
        internal MouseOutput LastOutput { get; private set; }

        public AbsMouseFeeder(GunState state)
            : base(state, "Mouse", DriversConst.TTC_PRODUCTID_MOUSEABS, "AbsMouse")
        {
        }

        protected override void OnConnected()
        {
            _lastButtons = 0xFF;
            _lastSendTimestamp = 0;
        }

        /// <summary>The mouse report has no Timeout field, so nothing clears a held button if we close without
        /// releasing it. The position is left where it was.</summary>
        protected override void ReleaseHeldInputs() => Send(_lastX, _lastY, 0);

        public void Send(ushort x, ushort y, byte buttons)
        {
            var data = new SetFeatureMouseAbs
            {
                ReportID = 1,
                CommandCode = 2,
                Buttons = buttons,
                X = x,
                Y = y
            };

            Send(in data);
        }

        /// <summary>Feeds one frame. When <paramref name="hasPosition"/> is false the cursor is left where it
        /// was and only the button state is sent.</summary>
        internal void Feed(GunMapping mapping, ushort x, ushort y, bool hasPosition)
        {
            byte buttons = 0;
            foreach (var map in mapping.MousePairs)
            {
                if (!State.Buttons[(int)map.Key]) continue;

                if (map.Value == MouseButton.Left) buttons |= 1;
                else if (map.Value == MouseButton.Right) buttons |= 1 << 1;
                else if (map.Value == MouseButton.Middle) buttons |= 1 << 2;
            }

            if (!hasPosition)
            {
                x = _lastX;
                y = _lastY;
            }

            // Recorded before the "did anything change" test below, so it says what the device is holding on
            // the frames where nothing is sent too.
            LastOutput = new MouseOutput(x, y, buttons, hasPosition);

            bool changed = x != _lastX || y != _lastY || buttons != _lastButtons;
            bool stale = Stopwatch.GetTimestamp() - _lastSendTimestamp >= RefreshIntervalTicks;

            if (!changed && !stale) return;

            Send(x, y, buttons);

            _lastX = x;
            _lastY = y;
            _lastButtons = buttons;
            _lastSendTimestamp = Stopwatch.GetTimestamp();
        }
    }
}
