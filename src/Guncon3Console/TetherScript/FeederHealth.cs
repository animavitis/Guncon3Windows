namespace Guncon3Console.TetherScript
{
    /// <summary>
    /// Tracks whether a TetherScript virtual device is still accepting reports.
    /// Logs once when it stops and once when it recovers, never once per frame.
    /// </summary>
    internal sealed class FeederHealth
    {
        private const int FailuresBeforeUnhealthy = 10;

        private readonly string _name;
        private int _consecutiveFailures;

        /// <summary>False once the device has rejected several reports in a row.</summary>
        public bool Healthy { get; private set; } = true;

        /// <param name="name">The device's name as it appears in log lines, e.g. "Mouse".</param>
        public FeederHealth(string name) => _name = name;

        /// <summary>Records the outcome of one send. Returns what it was given.</summary>
        public bool Track(bool sent)
        {
            if (sent)
            {
                if (!Healthy)
                {
                    Healthy = true;
                    ConsoleLog.Line($"{_name} feeder recovered.");
                }
                _consecutiveFailures = 0;
                return true;
            }

            if (++_consecutiveFailures < FailuresBeforeUnhealthy || !Healthy)
                return false;

            Healthy = false;
            ConsoleLog.Warn($"{_name} feeder stopped accepting reports after {_consecutiveFailures} failures.");
            return false;
        }
    }
}
