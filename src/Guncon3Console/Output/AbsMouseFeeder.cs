// SPDX-License-Identifier: GPL-2.0-only
using Guncon3.Core;

namespace Guncon3Console.Output
{
    /// <summary>The aim and the mouse-mapped buttons, as absolute mouse events through SendInput. There is
    /// no device to hold a state, so a frame is sent only when the position or a button changed, and a held
    /// button is said once. Every gun drives the one system cursor. At start the cursor is left where it is
    /// until the gun first aims on-screen: with nothing sent yet, an off-screen (0, 0) frame is not a
    /// move.</summary>
    sealed class AbsMouseFeeder : Feeder
    {
        private readonly NativeInput.INPUT[] _frame = new NativeInput.INPUT[1];

        /// <summary>Its own array, because a release may run on the main thread while the worker is still
        /// alive (App.Shutdown's wedged-thread path) and must not share a buffer with Feed.</summary>
        private readonly NativeInput.INPUT[] _release = new NativeInput.INPUT[1];

        private ushort _lastX;
        private ushort _lastY;

        /// <summary>The button bits Windows is holding down for us, advanced only when a send was accepted so
        /// a rejected transition is retried by the next frame.</summary>
        private byte _heldButtons;

        /// <summary>What this feeder decided the cursor should hold after the last <see cref="Feed"/> — whether
        /// or not that frame was actually sent, because an unchanged state is still what the cursor shows.
        /// Written and read on the worker thread only, so it needs no locking.</summary>
        internal MouseOutput LastOutput { get; private set; }

        public AbsMouseFeeder(GunState state) : base(state, "Mouse")
        {
        }

        /// <summary>SendInput needs nothing opened; this only forgets what an earlier run held.</summary>
        public override void Connect()
        {
            _heldButtons = 0;
            _lastX = 0;
            _lastY = 0;
        }

        /// <summary>Lets go of every button still down. The position is left where it is.</summary>
        protected override void ReleaseHeldInputs()
        {
            if (_heldButtons == 0) return;

            _release[0] = NativeInput.Mouse(0, 0, MouseEventFlags.For(_heldButtons, 0, move: false));
            if (Track(NativeInput.Send(_release, 1)))
                _heldButtons = 0;
        }

        /// <summary>Feeds one frame. When <paramref name="hasPosition"/> is false the cursor is left where it
        /// was and only the button state is sent. x and y are 0..65535 over the virtual desktop.</summary>
        internal void Feed(GunMapping mapping, ushort x, ushort y, bool hasPosition)
        {
            byte buttons = 0;
            foreach (var map in mapping.MousePairs)
            {
                if (!State.Buttons[(int)map.Key]) continue;

                if (map.Value == MouseButton.Left) buttons |= MouseEventFlags.LeftBit;
                else if (map.Value == MouseButton.Right) buttons |= MouseEventFlags.RightBit;
                else if (map.Value == MouseButton.Middle) buttons |= MouseEventFlags.MiddleBit;
            }

            if (!hasPosition)
            {
                x = _lastX;
                y = _lastY;
            }

            // Recorded before the "is there anything to send" test, so it says what the cursor is holding on
            // the frames where nothing goes out too.
            LastOutput = new MouseOutput(x, y, buttons, hasPosition);

            bool moved = hasPosition && (x != _lastX || y != _lastY);
            uint flags = MouseEventFlags.For(_heldButtons, buttons, moved);
            if (flags == 0) return;

            _frame[0] = NativeInput.Mouse(x, y, flags);
            if (!Track(NativeInput.Send(_frame, 1))) return;

            _heldButtons = buttons;
            _lastX = x;
            _lastY = y;
        }
    }
}
