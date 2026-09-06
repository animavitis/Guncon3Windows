// SPDX-License-Identifier: GPL-2.0-only
using Nefarius.ViGEm.Client;

namespace Guncon3Console.Output
{
    /// <summary>The one connection to the ViGEmBus driver this process holds. Every gun's virtual pad hangs
    /// off it. Opened on the first joystick connect — so a machine without the driver pays nothing until
    /// then — and closed by App.Shutdown once the feeders have unplugged their pads.</summary>
    internal static class XboxBus
    {
        private static readonly object Gate = new object();
        private static ViGEmClient _client;

        /// <summary>The shared client, created on demand. Throws the ViGEm exception when the bus is missing
        /// (VigemBusNotFoundException) or cannot be opened; the caller turns that into a log line.</summary>
        public static ViGEmClient Open()
        {
            lock (Gate)
            {
                _client ??= new ViGEmClient();
                return _client;
            }
        }

        /// <summary>Disconnects from the bus. Any pad still plugged in is removed by the bus with it.</summary>
        public static void Close()
        {
            lock (Gate)
            {
                _client?.Dispose();
                _client = null;
            }
        }
    }
}
