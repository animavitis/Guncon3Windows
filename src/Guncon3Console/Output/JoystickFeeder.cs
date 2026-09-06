// SPDX-License-Identifier: GPL-2.0-only
using System;
using Guncon3.Core;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Guncon3Console.Output
{
    /// <summary>Feeds the gun's two analog sticks, its depth axis and its nine physical buttons to a virtual
    /// Xbox 360 controller on the ViGEmBus driver: one pad per gun, so a second gun is a second player.
    /// The layout is fixed and documented in the README.</summary>
    sealed class JoystickFeeder : Feeder
    {
        // The nine physical GunCon 3 buttons, in GunButton declaration order. Indices 0..8 here are exactly
        // JoystickReportState's Buttons0 bits 0..7 and Buttons1 bit 0, which is what the Test tab shows. The
        // eight "digitalized" stick directions further down the enum (LUp..RRight) are deliberately excluded:
        // they are already reported as the analog stick axes, and sending them as buttons too would make a
        // game see both a stick deflection and a button press for the same physical motion.
        private static readonly GunButton[] PhysicalButtons =
        {
            GunButton.Trigger, GunButton.A1, GunButton.A2, GunButton.B1, GunButton.B2,
            GunButton.C1, GunButton.C2, GunButton.AClick, GunButton.BClick
        };

        // The Xbox 360 button each PhysicalButtons entry becomes, same order: Trigger→A, A1→B, A2→X, B1→Y,
        // B2→LB, C1→RB, C2→Start, AClick→LS click, BClick→RS click.
        private static readonly Xbox360Button[] XboxButtons =
        {
            Xbox360Button.A, Xbox360Button.B, Xbox360Button.X, Xbox360Button.Y, Xbox360Button.LeftShoulder,
            Xbox360Button.RightShoulder, Xbox360Button.Start, Xbox360Button.LeftThumb, Xbox360Button.RightThumb
        };

        private const string NoBusMessage =
            "the ViGEmBus driver is not installed, so there is no virtual Xbox 360 controller. " +
            "Install ViGEmBus (1.22.0) to get one; the mouse and the keyboard work without it.";

        /// <summary>Used when no centres have been supplied, so feeding never allocates.</summary>
        private static readonly StickCentres DefaultCentres = new StickCentres();

        private IXbox360Controller _pad;

        /// <summary>The last report the pad accepted; null after a connect, so the first feed always sends.</summary>
        private JoystickReportState? _lastSent;

        /// <summary>The stick centres to scale the analog axes against, so scaling follows the auto-measured or
        /// file-loaded values rather than assuming the default centre.</summary>
        public StickCentres Centres { get; set; } = new StickCentres();

        /// <summary>The report state this feeder computed on the last <see cref="Feed"/> — whether or not it
        /// was sent, because an unchanged report is still what the pad is showing. Written and read on the
        /// worker thread only, so it needs no locking.</summary>
        internal JoystickReportState LastState { get; private set; }

        public JoystickFeeder(GunState state) : base(state, "Joystick")
        {
        }

        /// <summary>Plugs one pad into the bus. Throws an InvalidOperationException that says what to install
        /// when the driver is missing, and lets any other ViGEm failure through with its own message.</summary>
        public override void Connect()
        {
            if (_pad != null) return;

            IXbox360Controller pad = null;
            try
            {
                pad = XboxBus.Open().CreateXbox360Controller();
                pad.AutoSubmitReport = false;
                pad.Connect();
            }
            catch (VigemBusNotFoundException ex)
            {
                (pad as IDisposable)?.Dispose();
                throw new InvalidOperationException(NoBusMessage, ex);
            }
            catch
            {
                (pad as IDisposable)?.Dispose();
                throw;
            }

            _pad = pad;
            _lastSent = null;
        }

        /// <summary>Unplugs the pad. The release has already run.</summary>
        protected override void OnDisconnect()
        {
            if (_pad == null) return;

            try { _pad.Disconnect(); }
            catch (Exception ex) { Log.Warn($"{Name} pad could not be unplugged: {ex.Message}"); }

            (_pad as IDisposable)?.Dispose();
            _pad = null;
        }

        /// <summary>Sends one report with every button clear, the trigger at zero and the four stick axes
        /// centred. The pad holds whatever it was last told, so letting go has to be said explicitly.</summary>
        public void Release()
        {
            ushort centre = (ushort)(StickDigitizer.AxisMax / 2);
            SendState(new JoystickReportState(centre, centre, centre, centre, 0, 0, 0));
        }

        protected override void ReleaseHeldInputs() => Release();

        /// <summary>Loads the report into the pad and submits it once, recording what went out so the next
        /// feed can tell whether anything changed.</summary>
        private void SendState(in JoystickReportState s)
        {
            if (_pad == null)
            {
                Track("no virtual controller");
                return;
            }

            // With AutoSubmitReport off these are plain writes into the client's report struct and cannot
            // throw; only SubmitReport below talks to the driver, so only it is guarded.
            _pad.SetAxisValue(Xbox360Axis.LeftThumbX, XboxAxis.ToThumb(s.X));
            _pad.SetAxisValue(Xbox360Axis.LeftThumbY, XboxAxis.ToThumbInverted(s.Y));
            _pad.SetAxisValue(Xbox360Axis.RightThumbX, XboxAxis.ToThumb(s.RX));
            _pad.SetAxisValue(Xbox360Axis.RightThumbY, XboxAxis.ToThumbInverted(s.RY));
            _pad.SetSliderValue(Xbox360Slider.LeftTrigger, XboxAxis.ToTrigger(s.Z));
            _pad.SetButtonsFull(XboxMask(s.Buttons0, s.Buttons1));

            try
            {
                _pad.SubmitReport();
            }
            catch (Exception ex)
            {
                Track(ex.Message);
                return;
            }

            Track(null);
            _lastSent = s;
        }

        /// <summary>Folds the report's two button bytes into the XInput button mask.</summary>
        private static ushort XboxMask(byte buttons0, byte buttons1)
        {
            ushort mask = 0;
            for (int i = 0; i < XboxButtons.Length; i++)
            {
                bool down = i < 8
                    ? (buttons0 & (1 << i)) != 0
                    : (buttons1 & (1 << (i - 8))) != 0;
                if (down) mask |= XboxButtons[i].Value;
            }
            return mask;
        }

        /// <summary>
        /// <paramref name="mapping"/> is accepted only for signature parity with the
        /// mouse and keyboard feeders. The pad's assignment is fixed (left stick → left
        /// thumb, right stick → right thumb, depth → left trigger, the nine physical
        /// buttons → the table above), so it never consults the mapping.
        /// </summary>
        internal void Feed(GunMapping mapping)
        {
            var centres = Centres ?? DefaultCentres;

            ushort x = StickDigitizer.ToCentredAxis(State.ABS_HAT0X, centres.HatX);
            ushort y = StickDigitizer.ToCentredAxis(State.ABS_HAT0Y, centres.HatY);
            ushort rx = StickDigitizer.ToCentredAxis(State.ABS_RX, centres.RX);
            ushort ry = StickDigitizer.ToCentredAxis(State.ABS_RY, centres.RY);

            // The depth axis is genuinely 8-bit (see GunconReader): ToAxis clamps it onto 0..AxisMax instead
            // of leaving it pinned near zero, which is what feeding the raw 16-bit-shaped value would do.
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

            // The pad holds its state, so an unchanged report needs no resend and no periodic refresh.
            if (_lastSent is { } last && next == last)
                return;

            SendState(next);
        }
    }
}
