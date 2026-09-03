using System;
using System.Collections.Generic;

namespace Guncon3Console
{
    public static class GunState
    {
        public static readonly Dictionary<GunButton, bool> BtnState;

        static GunState()
        {
            var values = Enum.GetValues(typeof(GunButton));
            BtnState = new Dictionary<GunButton, bool>(values.Length);
            foreach (GunButton item in values)
                BtnState[item] = false;
        }

        // Values already used by the project
        public static long ABS_RY { get; set; }
        public static long ABS_RX { get; set; }
        public static long ABS_HAT0Y { get; set; }
        public static long ABS_HAT0X { get; set; }
        public static short Z { get; set; }
        public static short ABS_Y { get; set; }
        public static short ABS_X { get; set; }

        // Existing indicators
        public static bool INDICATOR1 { get; set; }
        public static bool INDICATOR2 { get; set; }
        public static bool IsInsideScreen => !INDICATOR2;

        // ============================
        // NEW: compatibility aliases
        // ============================
        // For calibration we want to read the device "RAW" values. In this driver
        // the closest ones are ABS_X / ABS_Y before the transform is applied,
        // so RAW_X/RAW_Y are exposed as aliases of those fields.
        public static int RAW_X
        {
            get => ABS_X;
            set => ABS_X = (short)value;
        }

        public static int RAW_Y
        {
            get => ABS_Y;
            set => ABS_Y = (short)value;
        }

        // Map the trigger onto the button table
        public static bool BTN_TRIGGER
        {
            get => BtnState.TryGetValue(GunButton.Trigger, out var v) && v;
            set => BtnState[GunButton.Trigger] = value;
        }

        // Extra aliases in case some code expects these names
        public static bool Trigger
        {
            get => BTN_TRIGGER;
            set => BTN_TRIGGER = value;
        }

        public static int PointerX
        {
            get => ABS_X;
            set => ABS_X = (short)value;
        }

        public static int PointerY
        {
            get => ABS_Y;
            set => ABS_Y = (short)value;
        }
    }
}
