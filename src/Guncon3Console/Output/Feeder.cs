// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace Guncon3Console.Output
{
    /// <summary>What every virtual output shares: the gun state it reads, a name for the log, and the
    /// health tracking. A subclass supplies how it connects, how it sends, and what "release everything this
    /// output is holding" means for it. Feed methods are not here — their signatures differ per device and
    /// GunWorker calls them by concrete type.</summary>
    abstract class Feeder
    {
        protected readonly GunState State;
        private readonly FeederHealth _health;

        /// <summary>Prefix for this feeder's log lines, e.g. "Mouse".</summary>
        protected string Name { get; }

        /// <summary>False once the output has rejected several sends in a row.</summary>
        public bool Healthy => _health.Healthy;

        /// <summary>Called on the healthy↔unhealthy transition, from the worker thread. <see cref="Healthy"/>
        /// is a plain bool written there and read from the UI thread: a stale read is one repaint behind,
        /// which the status timer corrects.</summary>
        public Action HealthChanged
        {
            get => _health.Changed;
            set => _health.Changed = value;
        }

        protected Feeder(GunState state, string name)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Name = name;
            _health = new FeederHealth(name);
        }

        /// <summary>Opens whatever the backend needs and resets the record of what is held. Throws when the
        /// backend is unavailable; the caller logs and carries on without this output.</summary>
        public abstract void Connect();

        /// <summary>Releases everything held, then closes the backend. A release that fails must not stop the
        /// rest of the shutdown, so it is caught and logged here.</summary>
        public void Disconnect()
        {
            try { ReleaseHeldInputs(); }
            catch (Exception ex) { Log.Warn($"{Name} release failed: {ex.Message}"); }

            OnDisconnect();
        }

        /// <summary>Clears everything this output is holding. Called on disconnect and on a worker's teardown.</summary>
        protected abstract void ReleaseHeldInputs();

        /// <summary>Closes the backend, after the release. Nothing to do for SendInput.</summary>
        protected virtual void OnDisconnect() { }

        /// <summary>Records one send's outcome; null means accepted. Returns true when accepted.</summary>
        protected bool Track(string failure) => _health.Track(failure);
    }
}
