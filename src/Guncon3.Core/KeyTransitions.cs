// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace Guncon3.Core
{
    /// <summary>
    /// What changed between the key set a virtual keyboard holds and the set it
    /// should hold: the keys to release and the keys to press. SendInput is a stream
    /// of key-down and key-up events rather than a device state, so a feeder needs
    /// exactly this. Both inputs are ascending, duplicate-free and at most
    /// <see cref="KeySetBuilder.MaxKeys"/> long — the shape <see cref="KeySetBuilder.Keys"/>
    /// has — which makes the diff one merge walk and no allocation.
    /// </summary>
    public sealed class KeyTransitions
    {
        private readonly byte[] _released = new byte[KeySetBuilder.MaxKeys];
        private readonly byte[] _pressed = new byte[KeySetBuilder.MaxKeys];
        private int _releasedCount;
        private int _pressedCount;

        /// <summary>Keys in the previous set and not the current one, ascending. Valid until the next Compute.</summary>
        public ReadOnlySpan<byte> Released => _released.AsSpan(0, _releasedCount);

        /// <summary>Keys in the current set and not the previous one, ascending. Valid until the next Compute.</summary>
        public ReadOnlySpan<byte> Pressed => _pressed.AsSpan(0, _pressedCount);

        public void Compute(ReadOnlySpan<byte> previous, ReadOnlySpan<byte> current)
        {
            if (previous.Length > KeySetBuilder.MaxKeys)
                throw new ArgumentException($"previous has {previous.Length} keys; at most {KeySetBuilder.MaxKeys} can be held.", nameof(previous));
            if (current.Length > KeySetBuilder.MaxKeys)
                throw new ArgumentException($"current has {current.Length} keys; at most {KeySetBuilder.MaxKeys} can be held.", nameof(current));

            _releasedCount = 0;
            _pressedCount = 0;

            int i = 0, j = 0;
            while (i < previous.Length && j < current.Length)
            {
                if (previous[i] == current[j]) { i++; j++; }
                else if (previous[i] < current[j]) _released[_releasedCount++] = previous[i++];
                else _pressed[_pressedCount++] = current[j++];
            }

            while (i < previous.Length) _released[_releasedCount++] = previous[i++];
            while (j < current.Length) _pressed[_pressedCount++] = current[j++];
        }
    }
}
