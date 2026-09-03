using System;
using GunconUSB;
using Guncon3.Core;

namespace Guncon3Console.TetherScript
{
    /// <summary>
    /// Feeds the gun's two analog sticks, its depth axis and its nine physical
    /// buttons to a TetherScript virtual joystick, alongside the existing
    /// absolute mouse and keyboard feeders.
    /// </summary>
    class JoystickFeeder
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

        private readonly HIDController HID = new HIDController();
        private readonly GunState _state;

        private static readonly int ReportSize = HidReport.SizeOf<SetFeatureJoy>();
        private readonly byte[] _reportBuffer = new byte[ReportSize + 1];

        private readonly FeederHealth _health = new FeederHealth("Joystick");

        /// <summary>False once the virtual device has rejected several reports in a row.</summary>
        public bool Healthy => _health.Healthy;

        /// <summary>
        /// The stick centres to scale the analog axes against. Set this from
        /// <see cref="GunconReader.Centres"/> so scaling follows the auto-measured
        /// or file-loaded values rather than assuming the default centre.
        /// </summary>
        public StickCentres Centres { get; set; } = new StickCentres();

        public JoystickFeeder(GunState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public void Connect()
        {
            HID.OnLog += Log;
            HID.VendorID = (ushort)DriversConst.TTC_VENDORID;
            HID.ProductID = (ushort)DriversConst.TTC_PRODUCTID_JOYSTICK;
            HID.Connect();

            if (!HID.Connected)
                throw new Exception("Could not connect to TetherScript's Joystick");
        }

        public void Disconnect()
        {
            // The joystick report has no Timeout field, so nothing clears a held
            // button or a deflected axis if we close without releasing it first.
            try { Release(); }
            catch { }

            HID.Disconnect();
            HID.OnLog -= Log;
        }

        /// <summary>
        /// Sends one report with every button clear and the four stick axes centred.
        /// This device has no Timeout field, so nothing clears a held button or a
        /// deflected axis on its own — it has to be said explicitly. Used both when
        /// this feeder is shutting down and when a gun's inputs must be released
        /// mid-session (e.g. on disconnect while the app keeps running).
        /// </summary>
        public void Release()
        {
            ushort centre = (ushort)(StickDigitizer.AxisMax / 2);
            Send(centre, centre, centre, centre, 0, 0, 0);
        }

        private void Log(object s, LogArgs e) => ConsoleLog.Line("Joystick " + e.Msg);

        private void Send(ushort x, ushort y, ushort rx, ushort ry, ushort z, byte btn0, byte btn1)
        {
            var data = new SetFeatureJoy
            {
                ReportID = 1,
                CommandCode = 2,
                X = x,
                Y = y,
                Z = z,
                rX = rx,
                rY = ry,
                rZ = 0,
                slider = 0,
                dial = 0,
                wheel = 0,
                hat = 0,
                btn0 = btn0,
                btn1 = btn1,
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

            HidReport.Write(in data, _reportBuffer);
            _health.Track(HID.SendData(_reportBuffer, (uint)ReportSize));
        }

        /// <summary>
        /// <paramref name="mapping"/> is accepted only for signature parity with the
        /// mouse and keyboard feeders. The joystick's field assignment is fixed
        /// (left stick -> X/Y, right stick -> rX/rY, depth -> Z, the nine physical
        /// buttons -> btn0/btn1), so it never consults the mapping.
        /// </summary>
        internal void Feed(GunMapping mapping)
        {
            var centres = Centres ?? new StickCentres();

            ushort x = StickDigitizer.ToCentredAxis((int)_state.ABS_HAT0X, centres.HatX);
            ushort y = StickDigitizer.ToCentredAxis((int)_state.ABS_HAT0Y, centres.HatY);
            ushort rx = StickDigitizer.ToCentredAxis((int)_state.ABS_RX, centres.RX);
            ushort ry = StickDigitizer.ToCentredAxis((int)_state.ABS_RY, centres.RY);

            // The depth axis is genuinely 8-bit (see GunconReader): ToAxis clamps
            // it onto the joystick's 0..AxisMax range instead of leaving it pinned
            // near zero, which is what feeding the raw 16-bit-shaped value would do.
            ushort z = StickDigitizer.ToAxis(_state.Z);

            byte btn0 = 0, btn1 = 0;
            for (int i = 0; i < PhysicalButtons.Length; i++)
            {
                if (!_state.BtnState.TryGetValue(PhysicalButtons[i], out bool pressed) || !pressed)
                    continue;

                int bit = i % 8;
                if (i / 8 == 0) btn0 |= (byte)(1 << bit);
                else btn1 |= (byte)(1 << bit);
            }

            // No change-detection dedup: unlike AbsMouseFeeder this feeder sends on
            // every call. That dedup is unverified against hardware; copying it into
            // a brand-new, equally unverified feeder would compound the risk. Easy
            // follow-up if joystick traffic turns out to matter.
            Send(x, y, rx, ry, z, btn0, btn1);
        }
    }
}
