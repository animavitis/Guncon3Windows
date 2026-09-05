// SPDX-License-Identifier: GPL-2.0-only
namespace Guncon3Console
{
    /// <summary>One gun and the worker that drives it.</summary>
    internal sealed class GunSlot
    {
        public GunInstance Gun { get; }

        /// <summary>Null until <see cref="App"/> has created the workers.</summary>
        public GunWorker Worker { get; set; }

        /// <summary>Set when a calibration window's read thread did not stop within its join timeout: the gun
        /// is left idle and its device never disconnected or re-opened.</summary>
        public bool PipeAbandoned { get; set; }

        public GunSlot(GunInstance gun) => Gun = gun;

        public int Index => Gun.Index;
    }
}
