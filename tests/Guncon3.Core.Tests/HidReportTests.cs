// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Runtime.InteropServices;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class HidReportTests
    {
        /// <summary>The pre-existing packing path, kept here purely as the oracle.</summary>
        private static byte[] LegacyPack<T>(T value) where T : struct
        {
            int size = Marshal.SizeOf(value);
            var arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, ptr, false);
                Marshal.Copy(ptr, arr, 0, size);
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return arr;
        }

        [Fact]
        public void SizeOf_MatchesMarshalSizeOf()
        {
            Assert.Equal(Marshal.SizeOf<SetFeatureMouseAbs>(), HidReport.SizeOf<SetFeatureMouseAbs>());
            Assert.Equal(Marshal.SizeOf<SetFeatureKeyboard>(), HidReport.SizeOf<SetFeatureKeyboard>());
            Assert.Equal(Marshal.SizeOf<SetFeatureJoy>(), HidReport.SizeOf<SetFeatureJoy>());
        }

        [Fact]
        public void SizeOf_AgreesWithTheManagedLayoutWriteUses()
        {
            // Write copies Unsafe.SizeOf<T>() bytes through MemoryMarshal; SizeOf reports
            // Marshal.SizeOf<T>(). For Pack = 1 blittable structs they agree, and the
            // static initialiser now throws if a future struct makes them differ.
            Assert.Equal(System.Runtime.CompilerServices.Unsafe.SizeOf<SetFeatureMouseAbs>(), HidReport.SizeOf<SetFeatureMouseAbs>());
            Assert.Equal(System.Runtime.CompilerServices.Unsafe.SizeOf<SetFeatureKeyboard>(), HidReport.SizeOf<SetFeatureKeyboard>());
            Assert.Equal(System.Runtime.CompilerServices.Unsafe.SizeOf<SetFeatureJoy>(), HidReport.SizeOf<SetFeatureJoy>());
        }

        [Fact]
        public void Sizes_AreTheWireSizes()
        {
            Assert.Equal(7, HidReport.SizeOf<SetFeatureMouseAbs>());
            Assert.Equal(14, HidReport.SizeOf<SetFeatureKeyboard>());
            Assert.Equal(37, HidReport.SizeOf<SetFeatureJoy>());
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(1, 0, 32767)]
        [InlineData(7, 32767, 0)]
        [InlineData(255, 65535, 65535)]
        [InlineData(2, 12345, 54321)]
        public void MouseReport_PacksIdenticallyToLegacyPath(byte buttons, ushort x, ushort y)
        {
            var value = new SetFeatureMouseAbs
            {
                ReportID = 1, CommandCode = 2, Buttons = buttons, X = x, Y = y
            };

            var buffer = new byte[HidReport.SizeOf<SetFeatureMouseAbs>() + 1];
            HidReport.Write(in value, buffer);

            Assert.Equal(LegacyPack(value), buffer[..HidReport.SizeOf<SetFeatureMouseAbs>()]);
            Assert.Equal(0, buffer[^1]);
        }

        [Theory]
        [InlineData(0u, 0, 0, 0, 0, 0, 0)]
        [InlineData(1000u, 4, 0, 0, 0, 0, 0)]
        [InlineData(1000u, 4, 5, 6, 7, 8, 9)]
        [InlineData(uint.MaxValue, 255, 255, 255, 255, 255, 255)]
        public void KeyboardReport_PacksIdenticallyToLegacyPath(
            uint timeout, byte k0, byte k1, byte k2, byte k3, byte k4, byte k5)
        {
            var value = new SetFeatureKeyboard
            {
                ReportID = 1, CommandCode = 2, Timeout = timeout,
                Modifier = 0, Padding = 0,
                Key0 = k0, Key1 = k1, Key2 = k2, Key3 = k3, Key4 = k4, Key5 = k5
            };

            var buffer = new byte[HidReport.SizeOf<SetFeatureKeyboard>() + 1];
            HidReport.Write(in value, buffer);

            Assert.Equal(LegacyPack(value), buffer[..HidReport.SizeOf<SetFeatureKeyboard>()]);
            Assert.Equal(0, buffer[^1]);
        }

        [Fact]
        public void JoyReport_PacksIdenticallyToLegacyPath()
        {
            var value = new SetFeatureJoy
            {
                ReportID = 1,
                CommandCode = 2,
                X = 100,
                Y = 200,
                Z = 300,
                rX = 400,
                rY = 500,
                rZ = 0,
                slider = 0,
                dial = 0,
                wheel = 0,
                hat = 0,
                btn0 = 0b0000_0001,
                btn1 = 0b0000_0001,
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

            var buffer = new byte[HidReport.SizeOf<SetFeatureJoy>() + 1];
            HidReport.Write(in value, buffer);

            Assert.Equal(LegacyPack(value), buffer[..HidReport.SizeOf<SetFeatureJoy>()]);
            Assert.Equal(0, buffer[^1]);
        }

        [Fact]
        public void Write_RejectsUndersizedBuffer()
        {
            var value = new SetFeatureMouseAbs { ReportID = 1 };
            Assert.Throws<ArgumentException>(() => HidReport.Write(in value, new byte[3]));
        }

        [Fact]
        public void Write_ClearsStaleBytesFromAReusedBuffer()
        {
            var buffer = new byte[HidReport.SizeOf<SetFeatureKeyboard>() + 1];
            var held = new SetFeatureKeyboard { ReportID = 1, CommandCode = 2, Key0 = 40, Key1 = 41 };
            HidReport.Write(in held, buffer);

            var released = new SetFeatureKeyboard { ReportID = 1, CommandCode = 2 };
            HidReport.Write(in released, buffer);

            Assert.Equal(LegacyPack(released), buffer[..HidReport.SizeOf<SetFeatureKeyboard>()]);
        }
    }
}
