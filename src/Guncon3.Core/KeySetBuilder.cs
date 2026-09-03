using System;
using System.Collections.Generic;

namespace Guncon3.Core
{
    /// <summary>
    /// Builds the six-slot HID keyboard key set from a mapping and a pressed-button
    /// state, and reports whether it changed since the previous call. Allocation-free
    /// after construction.
    /// </summary>
    public sealed class KeySetBuilder
    {
        public const int MaxKeys = 6;

        private readonly byte[] _current = new byte[MaxKeys];
        private readonly byte[] _previous = new byte[MaxKeys];
        private int _previousCount = 0;

        public int Count { get; private set; }

        /// <summary>The active keycodes, ascending. Length equals <see cref="Count"/>.</summary>
        public ReadOnlySpan<byte> Keys => _current.AsSpan(0, Count);

        /// <summary>Forces the next Update to report a change.</summary>
        public void Reset()
        {
            Array.Clear(_previous, 0, MaxKeys);
            Array.Clear(_current, 0, MaxKeys);
            Count = 0;
            _previousCount = -1;
        }

        /// <summary>Returns true when the resulting key set differs from the previous call.</summary>
        public bool Update(
            ReadOnlySpan<KeyValuePair<GunButton, byte>> mapping,
            Dictionary<GunButton, bool> pressed)
        {
            int count = 0;

            foreach (var entry in mapping)
            {
                if (count >= MaxKeys) break;
                if (!pressed.TryGetValue(entry.Key, out bool down) || !down) continue;

                byte code = entry.Value;

                bool already = false;
                for (int i = 0; i < count; i++)
                    if (_current[i] == code) { already = true; break; }

                if (!already) _current[count++] = code;
            }

            for (int i = 1; i < count; i++)
            {
                byte key = _current[i];
                int j = i - 1;
                while (j >= 0 && _current[j] > key) { _current[j + 1] = _current[j]; j--; }
                _current[j + 1] = key;
            }

            for (int i = count; i < MaxKeys; i++)
                _current[i] = 0;

            bool changed = count != _previousCount;
            if (!changed)
            {
                for (int i = 0; i < MaxKeys; i++)
                    if (_current[i] != _previous[i]) { changed = true; break; }
            }

            Count = count;

            if (changed)
                Buffer.BlockCopy(_current, 0, _previous, 0, MaxKeys);

            _previousCount = count;
            return changed;
        }
    }
}
