using System;
using System.Collections.Generic;

namespace GunconUSB
{
    public class GunState
    {
        public readonly Dictionary<GunButton, bool> BtnState;

        public GunState()
        {
            BtnState = new Dictionary<GunButton, bool>();
            foreach (GunButton b in Enum.GetValues(typeof(GunButton)))
                BtnState[b] = false;
        }

        // Valores RAW de la gun
        public long ABS_RY { get; set; }
        public long ABS_RX { get; set; }
        public long ABS_HAT0Y { get; set; }
        public long ABS_HAT0X { get; set; }
        public short Z { get; set; }
        public short ABS_Y { get; set; }
        public short ABS_X { get; set; }

        public bool INDICATOR1 { get; set; }
        public bool INDICATOR2 { get; set; }

        // Compatibilidad con el calibrador rectangular
        public double RAW_X { get; set; }
        public double RAW_Y { get; set; }
        public bool BTN_TRIGGER { get; set; }

        public bool IsInsideScreen => !INDICATOR2;
    }
}
