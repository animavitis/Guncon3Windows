// SPDX-License-Identifier: GPL-2.0-only
// CA1707: these are the TetherScript SDK's own constant names (Drivers.cs); kept verbatim so they can be
// grepped against the vendor kit.
#pragma warning disable CA1707
namespace Guncon3Console.TetherScript
{
    public enum DriversConst : ushort
    {
        TTC_VENDORID = 0xF00F,
        TTC_PRODUCTID_JOYSTICK = 0x00000001,
        TTC_PRODUCTID_MOUSEABS = 0x00000002,
        TTC_PRODUCTID_KEYBOARD = 0x00000003,
        TTC_PRODUCTID_GAMEPAD = 0x00000004,
        TTC_PRODUCTID_MOUSEREL = 0x00000005,
    }
}
