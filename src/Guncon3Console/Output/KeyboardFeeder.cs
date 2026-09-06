// SPDX-License-Identifier: GPL-2.0-only
using System;
using Guncon3.Core;

namespace Guncon3Console.Output
{
    /// <summary>The keyboard-mapped buttons as key-down and key-up events through SendInput. KeySetBuilder
    /// still decides which keys should be down; KeyTransitions turns the difference from what is down into
    /// events, releases first. Windows has no timeout on an injected key, so what this feeder holds is only
    /// ever released by <see cref="ReleaseHeldInputs"/> or a physical press.</summary>
    sealed class KeyboardFeeder : Feeder
    {
        private readonly KeySetBuilder _keys = new KeySetBuilder();
        private readonly KeyTransitions _transitions = new KeyTransitions();

        /// <summary>The keys Windows is holding down for us, ascending; advanced only when a send was accepted.</summary>
        private readonly byte[] _held = new byte[KeySetBuilder.MaxKeys];
        private int _heldCount;

        /// <summary>Room for every held key to go up and every wanted key to go down in one batch.</summary>
        private readonly NativeInput.INPUT[] _frame = new NativeInput.INPUT[2 * KeySetBuilder.MaxKeys];

        /// <summary>Its own array, for the same reason as AbsMouseFeeder's.</summary>
        private readonly NativeInput.INPUT[] _release = new NativeInput.INPUT[KeySetBuilder.MaxKeys];

        /// <summary>The key set the mapping wants after the last <see cref="Feed"/>, ascending and 0..6 long.
        /// Valid on the worker thread only: the span points at the builder's live buffer, which the next Feed
        /// overwrites.</summary>
        internal ReadOnlySpan<byte> LastKeys => _keys.Keys;

        public KeyboardFeeder(GunState state) : base(state, "Keyboard")
        {
        }

        /// <summary>SendInput needs nothing opened; this only forgets what an earlier run held.</summary>
        public override void Connect()
        {
            _keys.Reset();
            _heldCount = 0;
        }

        protected override void ReleaseHeldInputs()
        {
            int n = 0;
            for (int i = 0; i < _heldCount; i++)
                if (ScanCodeTable.TryGet(_held[i], out var scan))
                    _release[n++] = NativeInput.Key(scan, up: true);

            // An empty batch is not a send: it must not touch the health record.
            if (n == 0)
            {
                _heldCount = 0;
                return;
            }

            if (Track(NativeInput.Send(_release, n)))
                _heldCount = 0;
        }

        internal void Feed(GunMapping mapping)
        {
            if (!_keys.Update(mapping.KeyboardPairs, State.Buttons))
                return;

            var wanted = _keys.Keys;
            _transitions.Compute(_held.AsSpan(0, _heldCount), wanted);

            int n = 0;
            foreach (byte code in _transitions.Released)
                if (ScanCodeTable.TryGet(code, out var scan))
                    _frame[n++] = NativeInput.Key(scan, up: true);
            foreach (byte code in _transitions.Pressed)
                if (ScanCodeTable.TryGet(code, out var scan))
                    _frame[n++] = NativeInput.Key(scan, up: false);

            // An empty batch (nothing changed that has a scan code) is not a send: record the wanted set and
            // leave the health record alone.
            if (n > 0 && !Track(NativeInput.Send(_frame, n)))
            {
                // The builder has already recorded the wanted set as "previous" and would report no change
                // next frame; forgetting it makes the next Update recompute and retry the transition.
                _keys.Reset();
                return;
            }

            wanted.CopyTo(_held);
            _heldCount = wanted.Length;
        }
    }
}
