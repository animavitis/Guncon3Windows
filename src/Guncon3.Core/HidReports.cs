using System.Runtime.InteropServices;

namespace Guncon3.Core
{
    /// <summary>TetherScript absolute-mouse SetFeature report. 7 bytes on the wire.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SetFeatureMouseAbs
    {
        public byte ReportID;
        public byte CommandCode;
        public byte Buttons;
        public ushort X;
        public ushort Y;
    }

    /// <summary>TetherScript keyboard SetFeature report. 14 bytes on the wire.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SetFeatureKeyboard
    {
        public byte ReportID;
        public byte CommandCode;
        public uint Timeout;
        public byte Modifier;
        public byte Padding;
        public byte Key0;
        public byte Key1;
        public byte Key2;
        public byte Key3;
        public byte Key4;
        public byte Key5;
    }

    /// <summary>
    /// TetherScript joystick SetFeature report. 37 bytes on the wire. Field
    /// names, types and order are taken verbatim from the vendor HVDK SDK
    /// (CSharp/Common/Drivers.cs, struct SetFeatureJoy) — do not alter them.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SetFeatureJoy
    {
        public byte ReportID;
        public byte CommandCode;
        public ushort X;
        public ushort Y;
        public ushort Z;
        public ushort rX;
        public ushort rY;
        public ushort rZ;
        public ushort slider;
        public ushort dial;
        public ushort wheel;
        public byte hat;
        public byte btn0;
        public byte btn1;
        public byte btn2;
        public byte btn3;
        public byte btn4;
        public byte btn5;
        public byte btn6;
        public byte btn7;
        public byte btn8;
        public byte btn9;
        public byte btn10;
        public byte btn11;
        public byte btn12;
        public byte btn13;
        public byte btn14;
        public byte btn15;
    }
}
