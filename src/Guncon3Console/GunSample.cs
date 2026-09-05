// SPDX-License-Identifier: GPL-2.0-only
using Guncon3.Core;

namespace Guncon3Console
{
    /// <summary>One gun read, copied out of <see cref="GunState"/> on the read thread and handed to the UI
    /// thread as a whole. Immutable, so the UI never sees half a frame.</summary>
    internal sealed class GunSample
    {
        public static GunSample None { get; } = new(0, 0, new bool[GunButtons.Count], false, ReadResult.BadPacket);

        public short RawX { get; }
        public short RawY { get; }
        public bool[] Buttons { get; }
        public bool InsideScreen { get; }
        public ReadResult Result { get; }

        public GunSample(short rawX, short rawY, bool[] buttons, bool insideScreen, ReadResult result)
        {
            RawX = rawX;
            RawY = rawY;
            Buttons = buttons;
            InsideScreen = insideScreen;
            Result = result;
        }

        /// <summary>Snapshot of the state after one read. Copies the button array; the reader keeps writing to
        /// its own.</summary>
        public static GunSample From(GunState state, ReadResult result)
        {
            var buttons = new bool[GunButtons.Count];
            state.Buttons.CopyTo(buttons, 0);
            return new GunSample(state.ABS_X, state.ABS_Y, buttons, state.IsInsideScreen, result);
        }
    }
}
