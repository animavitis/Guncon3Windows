// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;

namespace Guncon3.Core
{
    /// <summary>Turns an analog stick axis into two digital directions, with a deadzone around a per-axis
    /// centre. The GunCon 3 reports each axis as one byte.</summary>
    public static class StickDigitizer
    {
        /// <summary>Centre assumed when nothing better is known.</summary>
        public const int DefaultCentre = 128;

        /// <summary>Maximum value a TetherScript joystick axis accepts.</summary>
        public const int AxisMax = 32767;

        private const int AxisMin = 0;
        private const int RawMin = 0;
        private const int RawMax = 255;

        /// <summary>Half-width of the dead band, in raw axis units.</summary>
        public const int Deadzone = 20;

        /// <summary>A measured centre outside this band is not believable — it means the stick was held away
        /// from rest while it was being measured.</summary>
        public const int MinPlausibleCentre = 96;
        public const int MaxPlausibleCentre = 160;

        public static bool IsPlausibleCentre(int centre)
            => centre >= MinPlausibleCentre && centre <= MaxPlausibleCentre;

        /// <summary>True when the axis is deflected towards 0 (left, or up).</summary>
        public static bool IsLow(int value, int centre) => value < centre - Deadzone;

        /// <summary>True when the axis is deflected towards 255 (right, or down).</summary>
        public static bool IsHigh(int value, int centre) => value > centre + Deadzone;

        /// <summary>
        /// The median of the samples, or <see cref="DefaultCentre"/> when there
        /// are none or the median is not plausible. Sorting a copy keeps the
        /// caller's list untouched; for an even count this takes the upper of
        /// the two middle values, which is arbitrary but deterministic.
        /// </summary>
        public static int EstimateCentre(IReadOnlyList<int>? samples)
        {
            if (samples == null || samples.Count == 0)
                return DefaultCentre;

            var sorted = new int[samples.Count];
            for (int i = 0; i < samples.Count; i++) sorted[i] = samples[i];
            Array.Sort(sorted);

            int median = sorted[sorted.Length / 2];
            return IsPlausibleCentre(median) ? median : DefaultCentre;
        }

        /// <summary>
        /// Maps a 0..255 stick axis onto 0..AxisMax so that the stick's measured
        /// resting value lands exactly at the centre of the output range. Each half
        /// is scaled independently, so a stick whose rest point is off-centre still
        /// reaches both extremes.
        /// </summary>
        public static ushort ToCentredAxis(int value, int centre)
        {
            if (value < RawMin) value = RawMin;
            if (value > RawMax) value = RawMax;

            // Defensive: IsPlausibleCentre keeps a real centre well away from 0 and 255, but a clamp here is
            // cheaper than proving it can never arrive.
            if (centre < RawMin + 1) centre = RawMin + 1;
            if (centre > RawMax - 1) centre = RawMax - 1;

            const int half = AxisMax / 2;

            double t;
            double outMin, outSpan;

            if (value <= centre)
            {
                t = (double)(value - RawMin) / (centre - RawMin);
                outMin = AxisMin;
                outSpan = half - AxisMin;
            }
            else
            {
                t = (double)(value - centre) / (RawMax - centre);
                outMin = half;
                outSpan = AxisMax - half;
            }

            int result = (int)Math.Round(outMin + t * outSpan);
            if (result < AxisMin) result = AxisMin;
            if (result > AxisMax) result = AxisMax;
            return (ushort)result;
        }

        /// <summary>Maps a 0..255 axis onto 0..AxisMax linearly, for an axis with no meaningful centre. Values
        /// outside 0..255 are clamped.</summary>
        public static ushort ToAxis(int value)
        {
            if (value < RawMin) value = RawMin;
            if (value > RawMax) value = RawMax;

            int result = (int)Math.Round((double)value / RawMax * AxisMax);
            if (result < AxisMin) result = AxisMin;
            if (result > AxisMax) result = AxisMax;
            return (ushort)result;
        }
    }
}
