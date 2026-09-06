// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace Guncon3Console.Output
{
    /// <summary>Tracks whether a virtual output is still accepting what it is sent. Logs once when it stops
    /// and once when it recovers, never once per frame.</summary>
    internal sealed class FeederHealth
    {
        private const int FailuresBeforeUnhealthy = 10;

        private readonly string _name;
        private int _consecutiveFailures;

        /// <summary>False once the output has rejected several sends in a row.</summary>
        public bool Healthy { get; private set; } = true;

        /// <summary>Called on the healthy↔unhealthy transition, on the worker thread that sent. Null until a
        /// host wires it up.</summary>
        public Action Changed { get; set; }

        /// <param name="name">The output's name as it appears in log lines, e.g. "Mouse".</param>
        public FeederHealth(string name) => _name = name;

        /// <summary>Records the outcome of one send. <paramref name="failure"/> is null when it was accepted,
        /// otherwise a short reason. Returns true when it was accepted.</summary>
        public bool Track(string failure)
        {
            if (failure == null)
            {
                if (!Healthy)
                {
                    Healthy = true;
                    Log.Line($"{_name} feeder recovered.");
                    Changed?.Invoke();
                }
                _consecutiveFailures = 0;
                return true;
            }

            if (++_consecutiveFailures < FailuresBeforeUnhealthy || !Healthy)
                return false;

            Healthy = false;
            Log.Warn($"{_name} feeder stopped accepting input after {_consecutiveFailures} failures ({failure}).");
            Changed?.Invoke();
            return false;
        }
    }
}
