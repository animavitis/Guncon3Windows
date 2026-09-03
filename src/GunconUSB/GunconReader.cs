using MadWizard.WinUSBNet;
using System;
using System.Collections.Generic;
using System.Linq;
using Guncon3.Core;

namespace GunconUSB
{
    /// <summary>Outcome of a single report read.</summary>
    public enum ReadResult
    {
        /// <summary>A valid report was decoded and the state updated.</summary>
        Ok,

        /// <summary>The report was the wrong length or failed its checksum. Routine; retry.</summary>
        BadPacket,

        /// <summary>The device is gone. Reconnect before reading again.</summary>
        Disconnected
    }

    public class GunconReader
    {
        private const int pid = 2048;   // 0x0800
        private const int vid = 2970;   // 0x0B9A (Namco)
        private USBDevice device = null;
        private USBInterface iface;
        private readonly byte[] readBuffer = new byte[15];
        private readonly byte[] decodedBuffer = new byte[13];
        private static readonly Guid deviceguid = new Guid("{A5DCBF10-6530-11D2-901F-00C04FB951ED}");

        // Each reader has its own state
        public GunState State { get; } = new GunState();

        /// <summary>
        /// The last raw 15-byte USB report, before decoding. Diagnostics only.
        /// </summary>
        public byte[] LastRawFrame { get; } = new byte[15];

        /// <summary>The device path this reader last connected to, or null.</summary>
        public string DevicePath { get; private set; }

        /// <summary>
        /// Returns all Guncon3 USB device infos found on the system.
        /// </summary>
        public static List<USBDeviceInfo> FindAllDevices()
        {
            return USBDevice.GetDevices(deviceguid)
                            .Where(x => x.PID == pid && x.VID == vid)
                            .ToList();
        }

        /// <summary>
        /// Connect to a specific USB device info.
        /// </summary>
        public void Connect(USBDeviceInfo devInfo)
        {
            if (devInfo == null)
                throw new ArgumentNullException(nameof(devInfo));

            try
            {
                device = new USBDevice(devInfo);
                iface = device.Interfaces[0];
                DevicePath = devInfo.DevicePath;
            }
            catch
            {
                try { device?.Dispose(); } catch { }
                device = null;
                iface = null;
                throw;
            }
        }

        /// <summary>
        /// Connect to the first (or only) Guncon3 found.
        /// </summary>
        public void Connect()
        {
            var devInfo = FindAllDevices().FirstOrDefault();
            if (devInfo == null)
                throw new Exception("Guncon3 device not found");
            Connect(devInfo);
        }

        public void Disconnect()
        {
            try { device?.Dispose(); }
            finally { device = null; iface = null; }
        }

        /// <summary>
        /// Attempts to reattach, preferring the same physical port. Paths in
        /// <paramref name="claimedPaths"/> belong to other guns and are skipped.
        /// </summary>
        public bool Reconnect(IReadOnlyCollection<string> claimedPaths)
        {
            Disconnect();

            List<USBDeviceInfo> candidates;
            try { candidates = FindAllDevices(); }
            catch { return false; }

            var preferred = candidates.Find(d => string.Equals(d.DevicePath, DevicePath, StringComparison.OrdinalIgnoreCase));
            if (preferred != null)
            {
                try { Connect(preferred); return true; }
                catch { /* fall through to any free device */ }
            }

            foreach (var candidate in candidates)
            {
                if (claimedPaths.Contains(candidate.DevicePath, StringComparer.OrdinalIgnoreCase)) continue;

                try { Connect(candidate); return true; }
                catch { }
            }

            return false;
        }

        public ReadResult Read()
        {
            if (device == null) return ReadResult.Disconnected;

            int n;
            try
            {
                iface.OutPipe.Write(GunconDecoder.Key);
                n = iface.InPipe.Read(readBuffer);
            }
            catch (USBException)
            {
                return ReadResult.Disconnected;
            }
            catch (ObjectDisposedException)
            {
                return ReadResult.Disconnected;
            }

            if (n != 15) return ReadResult.BadPacket;

            Buffer.BlockCopy(readBuffer, 0, LastRawFrame, 0, 15);

            if (!GunconDecoder.TryDecode(readBuffer, decodedBuffer))
                return ReadResult.BadPacket;

            // Main buttons -> dictionary
            State.BtnState[GunButton.Trigger] = (decodedBuffer[11] & 0x20) != 0;
            State.BtnState[GunButton.A1] = (decodedBuffer[12] & 0x04) != 0;
            State.BtnState[GunButton.A2] = (decodedBuffer[12] & 0x02) != 0;
            State.BtnState[GunButton.B1] = (decodedBuffer[11] & 0x04) != 0;
            State.BtnState[GunButton.B2] = (decodedBuffer[11] & 0x02) != 0;
            State.BtnState[GunButton.C1] = (decodedBuffer[11] & 0x80) != 0;
            State.BtnState[GunButton.C2] = (decodedBuffer[12] & 0x08) != 0;
            State.BtnState[GunButton.AClick] = (decodedBuffer[10] & 0x80) != 0;
            State.BtnState[GunButton.BClick] = (decodedBuffer[10] & 0x40) != 0;

            // Axes/indicators
            State.ABS_RY = decodedBuffer[0];
            State.ABS_RX = decodedBuffer[1];
            State.ABS_HAT0Y = decodedBuffer[2];
            State.ABS_HAT0X = decodedBuffer[3];
            State.Z = (short)(decodedBuffer[4] * 256 + decodedBuffer[5]);
            State.ABS_Y = (short)(decodedBuffer[6] * 256 + decodedBuffer[7]);
            State.ABS_X = (short)(decodedBuffer[8] * 256 + decodedBuffer[9]);
            State.INDICATOR1 = (decodedBuffer[11] & 0x10) != 0;
            State.INDICATOR2 = (decodedBuffer[11] & 0x08) != 0;
            // Digitalize left analog (LUp/LDown/LLeft/LRight)
            // ABS_HAT0X / ABS_HAT0Y are 0..255 with center ~128.
            const int DEAD = 20; // deadzone in raw units (~8%)
            int lx = (int)State.ABS_HAT0X;
            int ly = (int)State.ABS_HAT0Y;

            State.BtnState[GunButton.LLeft]  = lx < (128 - DEAD);
            State.BtnState[GunButton.LRight] = lx > (128 + DEAD);
            State.BtnState[GunButton.LUp]    = ly < (128 - DEAD);
            State.BtnState[GunButton.LDown]  = ly > (128 + DEAD);


            // Compatibility with the EXE calibrator
            State.RAW_X = State.ABS_X;
            State.RAW_Y = State.ABS_Y;
            State.BTN_TRIGGER = State.BtnState[GunButton.Trigger];

            return ReadResult.Ok;
        }
    }
}
