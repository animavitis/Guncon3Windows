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
}
