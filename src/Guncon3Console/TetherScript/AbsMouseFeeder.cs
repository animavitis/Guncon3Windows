using System.Diagnostics;
using Guncon3.Core;

namespace Guncon3Console.TetherScript
{
    class AbsMouseFeeder : FeederBase<SetFeatureMouseAbs>
    {
        public bool Force4by3 = false;
        private byte btns = 0;

        private static readonly long RefreshIntervalTicks = Stopwatch.Frequency;   // 1000 ms
        private long _lastSendTimestamp;
        private ushort _lastX;
        private ushort _lastY;
        private byte _lastButtons = 0xFF;   // impossible value, forces the first send

        public AbsMouseFeeder(GunState state)
            : base(state, "Mouse", DriversConst.TTC_PRODUCTID_MOUSEABS, "AbsMouse")
        {
        }

        protected override void OnConnected()
        {
            _lastButtons = 0xFF;
            _lastSendTimestamp = 0;
        }

        /// <summary>
        /// The mouse report has no Timeout field, so nothing clears a held button
        /// if we close without releasing it.
        /// </summary>
        protected override void ReleaseHeldInputs()
        {
            btns = 0;
            Send_Data_To_MouseAbs(_lastX, _lastY);
        }

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

            Send(in data);
        }

        internal void Feed(GunMapping mapping)
        {
            short absX = 0;
            short absY = 0;

            if (State.IsInsideScreen)
            {
                absX = State.ABS_X;
                absY = State.ABS_Y;

                if (Force4by3)
                    absX = (short)Helper.ConvertRange(4096, 28671, 0, 32767, absX);
            }

            btns = 0;
            foreach (var map in mapping.MousePairs)
            {
                if (!State.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
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
