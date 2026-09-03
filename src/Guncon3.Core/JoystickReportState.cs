using System;

namespace Guncon3.Core
{
    /// <summary>
    /// Everything one joystick report actually carries: the two sticks, the depth
    /// axis and the two button bytes. Held from one send to the next so a feeder
    /// can tell whether anything changed, and skip the send when nothing did.
    /// </summary>
    /// <remarks>
    /// This exists as its own type, in Core, so the comparison can be tested. A
    /// field left out of it is invisible at runtime — the device simply stops
    /// following that axis — and that is precisely the bug the tests here guard
    /// against.
    /// </remarks>
    public readonly struct JoystickReportState : IEquatable<JoystickReportState>
    {
        public ushort X { get; }
        public ushort Y { get; }
        public ushort RX { get; }
        public ushort RY { get; }
        public ushort Z { get; }
        public byte Buttons0 { get; }
        public byte Buttons1 { get; }

        public JoystickReportState(ushort x, ushort y, ushort rx, ushort ry, ushort z, byte buttons0, byte buttons1)
        {
            X = x;
            Y = y;
            RX = rx;
            RY = ry;
            Z = z;
            Buttons0 = buttons0;
            Buttons1 = buttons1;
        }

        public bool Equals(JoystickReportState other)
            => X == other.X
            && Y == other.Y
            && RX == other.RX
            && RY == other.RY
            && Z == other.Z
            && Buttons0 == other.Buttons0
            && Buttons1 == other.Buttons1;

        public override bool Equals(object obj) => obj is JoystickReportState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, RX, RY, Z, Buttons0, Buttons1);

        public static bool operator ==(JoystickReportState a, JoystickReportState b) => a.Equals(b);

        public static bool operator !=(JoystickReportState a, JoystickReportState b) => !a.Equals(b);
    }
}
