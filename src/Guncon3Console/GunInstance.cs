// SPDX-License-Identifier: GPL-2.0-only
using System.Threading;
using Guncon3Console.Output;

namespace Guncon3Console
{
    internal sealed class GunInstance
    {
        public int Index { get; }
        public GunconReader Reader { get; }
        public AbsMouseFeeder MouseFeeder { get; }
        public KeyboardFeeder KbFeeder { get; }
        public JoystickFeeder JoyFeeder { get; }

        private GunSnapshot _snapshot = GunSnapshot.Empty;

        /// <summary>Reads the currently published snapshot. Safe from any thread.</summary>
        public GunSnapshot Snapshot => Volatile.Read(ref _snapshot);

        /// <summary>Replaces the published snapshot. Called only from the main thread.</summary>
        public void Publish(GunSnapshot snapshot) => Volatile.Write(ref _snapshot, snapshot);

        public GunInstance(int index, GunconReader reader)
        {
            Index = index;
            Reader = reader;
            MouseFeeder = new AbsMouseFeeder(reader.State);
            KbFeeder = new KeyboardFeeder(reader.State);
            JoyFeeder = new JoystickFeeder(reader.State);
        }
    }
}
