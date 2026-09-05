// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using Guncon3.Core;

namespace Guncon3Console.TetherScript
{
    /// <summary>Feeds the gun's two analog sticks, its depth axis and its nine physical buttons to a
    /// TetherScript virtual joystick.</summary>
    sealed class JoystickFeeder : FeederBase<SetFeatureJoy>
    {
        // The nine physical GunCon 3 buttons, in GunButton declaration order.
        // Indices 0..8 here are exactly the joystick report's btn0 bits 0..7 and
        // btn1 bit 0. The eight "digitalized" stick directions further down the
        // enum (LUp..RRight) are deliberately excluded: they are already reported
        // as the analog stick axes below, and sending them as buttons too would
        // make a game see both a stick deflection and a button press for the same
        // physical motion.
        private static readonly GunButton[] PhysicalButtons =
        {
            GunButton.Trigger, GunButton.A1, GunButton.A2, GunButton.B1, GunButton.B2,
            GunButton.C1, GunButton.C2, GunButton.AClick, GunButton.BClick
        };

        /// <summary>Used when no centres have been supplied, so feeding never allocates.</summary>
        private static readonly StickCentres DefaultCentres = new StickCentres();

        private static readonly long RefreshIntervalTicks = Stopwatch.Frequency;   // 1000 ms
        private long _lastSendTimestamp;
        private JoystickReportState _lastSent;

        /// <summary>The stick centres to scale the analog axes against, so scaling follows the auto-measured or
        /// file-loaded values rather than assuming the default centre.</summary>
        public StickCentres Centres { get; set; } = new StickCentres();

        /// <summary>The report state this feeder computed on the last <see cref="Feed"/> — whether or not it
        /// was sent, because an unchanged report is still what the device is showing. Written and read on the
        /// worker thread only, so it needs no locking.</summary>
        internal JoystickReportState LastState { get; private set; }

        public JoystickFeeder(GunState state)
            : base(state, "Joystick", DriversConst.TTC_PRODUCTID_JOYSTICK)
        {
        }

        /// <summary>A reconnected device holds nothing, so the record of what it was last sent is cleared and
        /// the refresh clock zeroed. Both make the next feed send unconditionally rather than match a stale
        /// value.</summary>
        protected override void OnConnected()
        {
            _lastSent = default;
            _lastSendTimestamp = 0;
        }

        /// <summary>Sends one report with every button clear and the four stick axes centred. This device has
        /// no Timeout field, so nothing clears a held button or a deflected axis on its own — it has to be said
        /// explicitly.</summary>
        public void Release()
        {
            ushort centre = (ushort)(StickDigitizer.AxisMax / 2);
            SendState(new JoystickReportState(centre, centre, centre, centre, 0, 0, 0));
        }

        protected override void ReleaseHeldInputs() => Release();

        /// <summary>Sends one report and records what went out, so the next feed can tell whether anything
        /// actually changed.</summary>
        private void SendState(in JoystickReportState s)
        {
            var data = new SetFeatureJoy
            {
                ReportID = 1,
                CommandCode = 2,
                X = s.X,
                Y = s.Y,
                Z = s.Z,
                rX = s.RX,
                rY = s.RY,
                rZ = 0,
                slider = 0,
                dial = 0,
                wheel = 0,
                hat = 0,
                btn0 = s.Buttons0,
                btn1 = s.Buttons1,
                btn2 = 0,
                btn3 = 0,
                btn4 = 0,
                btn5 = 0,
                btn6 = 0,
                btn7 = 0,
                btn8 = 0,
                btn9 = 0,
                btn10 = 0,
                btn11 = 0,
                btn12 = 0,
                btn13 = 0,
                btn14 = 0,
                btn15 = 0
            };

            Send(in data);

            _lastSent = s;
            _lastSendTimestamp = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// <paramref name="mapping"/> is accepted only for signature parity with the
        /// mouse and keyboard feeders. The joystick's field assignment is fixed
        /// (left stick -> X/Y, right stick -> rX/rY, depth -> Z, the nine physical
        /// buttons -> btn0/btn1), so it never consults the mapping.
        /// </summary>
        internal void Feed(GunMapping mapping)
        {
            var centres = Centres ?? DefaultCentres;

            ushort x = StickDigitizer.ToCentredAxis(State.ABS_HAT0X, centres.HatX);
            ushort y = StickDigitizer.ToCentredAxis(State.ABS_HAT0Y, centres.HatY);
            ushort rx = StickDigitizer.ToCentredAxis(State.ABS_RX, centres.RX);
            ushort ry = StickDigitizer.ToCentredAxis(State.ABS_RY, centres.RY);

            // The depth axis is genuinely 8-bit (see GunconReader): ToAxis clamps
            // it onto the joystick's 0..AxisMax range instead of leaving it pinned
            // near zero, which is what feeding the raw 16-bit-shaped value would do.
            ushort z = StickDigitizer.ToAxis(State.Z);

            byte btn0 = 0, btn1 = 0;
            for (int i = 0; i < PhysicalButtons.Length; i++)
            {
                if (!State.Buttons[(int)PhysicalButtons[i]]) continue;

                int bit = i % 8;
                if (i / 8 == 0) btn0 |= (byte)(1 << bit);
                else btn1 |= (byte)(1 << bit);
            }

            var next = new JoystickReportState(x, y, rx, ry, z, btn0, btn1);
            LastState = next;

            // Send only when something moved, with a periodic refresh otherwise: the
            // joystick report has no Timeout field, exactly like the mouse's, so the
            // device is never told to hold its state and a silent feeder would leave
            // it running on a report that keeps ageing.
            bool stale = Stopwatch.GetTimestamp() - _lastSendTimestamp >= RefreshIntervalTicks;
            if (next == _lastSent && !stale)
                return;

            SendState(next);
        }
    }
}
