// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace Guncon3Console.TetherScript
{
    /// <summary>Tracks whether a TetherScript virtual device is still accepting reports. Logs once when it
    /// stops and once when it recovers, never once per frame.</summary>
    internal sealed class FeederHealth
    {
        private const int FailuresBeforeUnhealthy = 10;

        private readonly string _name;
        private int _consecutiveFailures;

        /// <summary>False once the device has rejected several reports in a row.</summary>
        public bool Healthy { get; private set; } = true;

        /// <summary>Called on the healthy↔unhealthy transition, on the worker thread that sent the report. Null
        /// until a host wires it up.</summary>
        public Action Changed { get; set; }

        /// <param name="name">The device's name as it appears in log lines, e.g. "Mouse".</param>
        public FeederHealth(string name) => _name = name;

        /// <summary>Records the outcome of one send (0 = accepted). Returns true when it was accepted.</summary>
        public bool Track(int error)
        {
            if (error == 0)
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
            string why = error == HIDController.NotConnected ? "the device is not open" : $"win32 error {error}";
            Log.Warn($"{_name} feeder stopped accepting reports after {_consecutiveFailures} failures ({why}).");
            Changed?.Invoke();
            return false;
        }
    }
}
