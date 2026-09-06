// SPDX-License-Identifier: GPL-2.0-only
namespace Guncon3.Core
{
    /// <summary>
    /// The MOUSEEVENTF_* flags SendInput takes, and the one function that composes a
    /// frame's flags from "did the aim move" and "which buttons changed". SendInput is
    /// a stream of events, so a held button is said once, on the frame it went down,
    /// and never again until it comes up. Kept in Core so the composition is tested;
    /// the P/Invoke that consumes it lives in the console project.
    /// </summary>
    public static class MouseEventFlags
    {
        public const uint Move = 0x0001;
        public const uint LeftDown = 0x0002;
        public const uint LeftUp = 0x0004;
        public const uint RightDown = 0x0008;
        public const uint RightUp = 0x0010;
        public const uint MiddleDown = 0x0020;
        public const uint MiddleUp = 0x0040;

        /// <summary>Do not merge this move with the previous one still in the queue: a light gun's aim is
        /// the whole point, every sample counts.</summary>
        public const uint MoveNoCoalesce = 0x2000;

        /// <summary>Absolute coordinates span the whole virtual desktop, not the primary monitor.</summary>
        public const uint VirtualDesk = 0x4000;

        public const uint Absolute = 0x8000;

        /// <summary>The flags of a move over the virtual desktop.</summary>
        public const uint AbsoluteMove = Move | Absolute | VirtualDesk | MoveNoCoalesce;

        /// <summary>Largest absolute coordinate: SendInput normalises both axes to 0..65535.</summary>
        public const int AbsoluteMax = 65535;

        /// <summary>The button bits <see cref="For"/> reads — the same bits AbsMouseFeeder packs.</summary>
        public const byte LeftBit = 1;
        public const byte RightBit = 1 << 1;
        public const byte MiddleBit = 1 << 2;

        /// <summary>The dwFlags for one frame; 0 means there is nothing to send.</summary>
        public static uint For(byte previousButtons, byte nextButtons, bool move)
        {
            uint flags = move ? AbsoluteMove : 0;
            int changed = previousButtons ^ nextButtons;

            if ((changed & LeftBit) != 0) flags |= (nextButtons & LeftBit) != 0 ? LeftDown : LeftUp;
            if ((changed & RightBit) != 0) flags |= (nextButtons & RightBit) != 0 ? RightDown : RightUp;
            if ((changed & MiddleBit) != 0) flags |= (nextButtons & MiddleBit) != 0 ? MiddleDown : MiddleUp;

            return flags;
        }
    }
}
