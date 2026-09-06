// SPDX-License-Identifier: GPL-2.0-only
using Nefarius.Drivers.WinUSB;
using System;
using System.Collections.Generic;
using System.Linq;
using Guncon3.Core;

namespace Guncon3Console
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

    public class GunconReader : IDisposable
    {
        private const int pid = 2048;   // 0x0800
        private const int vid = 2970;   // 0x0B9A (Namco)
        private USBDevice device;
        private USBInterface iface;

        /// <summary>
        /// How long a single USB transfer may take before WinUSB abandons it. The gun
        /// answers in single-digit milliseconds, so this is a wide margin that exists
        /// only to bound a wedged device. GunWorker's no-data teardown window must stay
        /// well above this value — by a factor of ten or better — or a single timeout
        /// becomes a hair trigger for tearing the device down.
        /// </summary>
        private const int PipeTimeoutMs = 100;

        /// <summary>False when the driver refused the transfer-timeout policy, in which case a wedged device
        /// can still block indefinitely.</summary>
        public bool PipeTimeoutApplied { get; private set; }

        /// <summary>
        /// The Win32 error code behind the most recent <see cref="USBException"/>
        /// classified by <see cref="Read"/>, or 0 if none has occurred yet. Exists so a
        /// log line can say what the driver actually reported: the timeout/disconnect
        /// split rests on ERROR_SEM_TIMEOUT being 121 for this transfer path, and a
        /// driver that disagrees would otherwise be indistinguishable from a real
        /// unplug.
        /// </summary>
        public int LastWin32Error { get; private set; }
        private readonly byte[] readBuffer = new byte[15];
        private bool _keySent;
        private readonly byte[] decodedBuffer = new byte[13];
        private readonly byte[] key = GunconDecoder.Key.ToArray();
        private static readonly Guid deviceguid = new Guid("{A5DCBF10-6530-11D2-901F-00C04FB951ED}");

        // Number of frames to sample before settling on a measured centre for each axis.
        private const int CentreSamples = 60;

        private readonly List<int> _hatXSamples = new List<int>(CentreSamples);
        private readonly List<int> _hatYSamples = new List<int>(CentreSamples);
        private readonly List<int> _rxSamples = new List<int>(CentreSamples);
        private readonly List<int> _rySamples = new List<int>(CentreSamples);

        private StickCentres _centres = new StickCentres();
        private bool _measuringCentres = true;

        /// <summary>Where <see cref="GunButton.ZLow"/> and <see cref="GunButton.ZHigh"/> split. Written from
        /// the UI thread when settings change and read on this reader's own thread, so it is volatile; an int
        /// cannot tear, and a digitisation that uses the old value for one packet is not worth a lock.</summary>
        private volatile int _zThreshold = DepthDigitizer.DefaultThreshold;

        /// <summary>The depth threshold in raw reading units. Anything implausible is refused in favour of the
        /// default rather than left to make one of the two directions permanent.</summary>
        public int ZThreshold
        {
            get => _zThreshold;
            set => _zThreshold = DepthDigitizer.Clamp(value);
        }
        private bool _centresJustMeasured;

        public GunState State { get; } = new GunState();

        /// <summary>The last raw 15-byte USB report, before decoding. Valid after a Read() that returned Ok;
        /// diagnostics only.</summary>
        public ReadOnlySpan<byte> LastRawFrame => readBuffer;

        /// <summary>The device path this reader last connected to, or null.</summary>
        public string DevicePath { get; private set; }

        /// <summary>The per-axis stick centres in use. Assign a loaded set before the first Read() to skip
        /// measurement entirely.</summary>
        public StickCentres Centres
        {
            get => _centres;
            set
            {
                if (value == null || !value.IsValid()) return;
                _centres = value;
                _measuringCentres = false;
            }
        }

        /// <summary>Returns the centres exactly once, if this reader measured them itself, so the caller can
        /// persist them; false otherwise.</summary>
        public bool TakeMeasuredCentres(out StickCentres centres)
        {
            if (_centresJustMeasured)
            {
                _centresJustMeasured = false;
                centres = _centres;
                return true;
            }

            centres = null;
            return false;
        }

        public static List<USBDeviceInfo> FindAllDevices()
        {
            return USBDevice.GetDevices(deviceguid)
                            .Where(x => x.PID == pid && x.VID == vid)
                            .ToList();
        }

        public void Connect(USBDeviceInfo devInfo)
        {
            ArgumentNullException.ThrowIfNull(devInfo);

            PipeTimeoutApplied = false;

            try
            {
                device = new USBDevice(devInfo);
                iface = device.Interfaces[0];
                DevicePath = devInfo.DevicePath;

                try
                {
                    iface.OutPipe.Policy.PipeTransferTimeout = PipeTimeoutMs;
                    iface.InPipe.Policy.PipeTransferTimeout = PipeTimeoutMs;
                    PipeTimeoutApplied = true;
                }
                catch
                {
                    // The driver refused the policy. A reader without a timeout is what we have today, which
                    // works, so the connect still succeeds.
                }

                // The Linux guncon3 driver sends the key once at open and then only
                // reads. Do the same; Read() resends it after any read that was not Ok, so a device that does
                // want it per frame costs one timed-out read and then behaves.
                _keySent = false;
                try
                {
                    iface.OutPipe.Write(key);
                    _keySent = true;
                }
                catch (USBException)
                {
                    // Read() will retry the write; a failure here is not a failed connect.
                }
            }
            catch
            {
                try { device?.Dispose(); } catch { }
                device = null;
                iface = null;
                throw;
            }
        }

        public void Connect()
        {
            var devInfo = FindAllDevices().FirstOrDefault();
            if (devInfo == null)
                throw new InvalidOperationException("Guncon3 device not found");
            Connect(devInfo);
        }

        public void Disconnect()
        {
            try { device?.Dispose(); }
            finally { device = null; iface = null; _keySent = false; }
        }

        public void Dispose()
        {
            Disconnect();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Why the last <see cref="Reconnect"/> failed, or null when it succeeded or has
        /// not run. The worker logs it when it changes, so a gun that cannot be reopened
        /// for a persistent reason says so once instead of never.
        /// </summary>
        public string LastReconnectError { get; private set; }

        /// <summary>Attempts to reattach, preferring the same physical port. Paths in <paramref
        /// name="claimedPaths"/> belong to other guns and are skipped.</summary>
        public bool Reconnect(IReadOnlySet<string> claimedPaths)
        {
            Disconnect();

            List<USBDeviceInfo> candidates;
            try { candidates = FindAllDevices(); }
            catch (Exception ex) { LastReconnectError = "enumeration failed: " + ex.Message; return false; }

            // Safe to try the remembered path without checking claimedPaths: no two readers can ever hold the
            // same DevicePath, since the fallback loop below skips claimed paths and Connect only records a
            // path once it succeeds.
            var preferred = candidates.Find(d => string.Equals(d.DevicePath, DevicePath, StringComparison.OrdinalIgnoreCase));
            string lastError = null;
            if (preferred != null)
            {
                try { Connect(preferred); LastReconnectError = null; return true; }
                catch (Exception ex) { lastError = ex.Message; }
            }

            int free = 0;
            foreach (var candidate in candidates)
            {
                if (claimedPaths.Contains(candidate.DevicePath)) continue;
                free++;

                try { Connect(candidate); LastReconnectError = null; return true; }
                catch (Exception ex) { lastError = ex.Message; }
            }

            LastReconnectError = free == 0
                ? (candidates.Count == 0 ? "no Guncon3 device present" : "every Guncon3 device is claimed by another gun")
                : "open failed: " + lastError;
            return false;
        }

        public ReadResult Read()
        {
            if (device == null) return ReadResult.Disconnected;

            int n;
            try
            {
                if (!_keySent)
                {
                    iface.OutPipe.Write(key);
                    _keySent = true;
                }
                n = iface.InPipe.Read(readBuffer);
            }
            catch (USBException ex)
            {
                _keySent = false;
                LastWin32Error = UsbErrors.Win32CodeOf(ex);

                // A timed-out transfer is a dropped frame, not a disconnection: the device is still enumerated
                // and simply did not answer. Treating it as a disconnect would release the gun's inputs and
                // re-enumerate over a single slow transfer.
                return UsbErrors.IsTransferTimeout(ex) ? ReadResult.BadPacket : ReadResult.Disconnected;
            }
            catch (ObjectDisposedException)
            {
                _keySent = false;
                return ReadResult.Disconnected;
            }

            if (n != 15) { _keySent = false; return ReadResult.BadPacket; }

            if (!GunconDecoder.TryDecode(readBuffer, decodedBuffer)) { _keySent = false; return ReadResult.BadPacket; }

            State.Buttons[(int)GunButton.Trigger] = (decodedBuffer[11] & 0x20) != 0;
            State.Buttons[(int)GunButton.A1] = (decodedBuffer[12] & 0x04) != 0;
            State.Buttons[(int)GunButton.A2] = (decodedBuffer[12] & 0x02) != 0;
            State.Buttons[(int)GunButton.B1] = (decodedBuffer[11] & 0x04) != 0;
            State.Buttons[(int)GunButton.B2] = (decodedBuffer[11] & 0x02) != 0;
            State.Buttons[(int)GunButton.C1] = (decodedBuffer[11] & 0x80) != 0;
            State.Buttons[(int)GunButton.C2] = (decodedBuffer[12] & 0x08) != 0;
            State.Buttons[(int)GunButton.AClick] = (decodedBuffer[10] & 0x80) != 0;
            State.Buttons[(int)GunButton.BClick] = (decodedBuffer[10] & 0x40) != 0;

            State.ABS_RY = decodedBuffer[0];
            State.ABS_RX = decodedBuffer[1];
            State.ABS_HAT0Y = decodedBuffer[2];
            State.ABS_HAT0X = decodedBuffer[3];
            State.Z = (short)(decodedBuffer[4] * 256 + decodedBuffer[5]);
            State.ABS_Y = (short)(decodedBuffer[6] * 256 + decodedBuffer[7]);
            State.ABS_X = (short)(decodedBuffer[8] * 256 + decodedBuffer[9]);
            State.INDICATOR1 = (decodedBuffer[11] & 0x10) != 0;
            State.INDICATOR2 = (decodedBuffer[11] & 0x08) != 0;

            int lx = State.ABS_HAT0X;
            int ly = State.ABS_HAT0Y;
            int rx = State.ABS_RX;
            int ry = State.ABS_RY;

            if (_measuringCentres)
            {
                _hatXSamples.Add(lx);
                _hatYSamples.Add(ly);
                _rxSamples.Add(rx);
                _rySamples.Add(ry);

                if (_hatXSamples.Count >= CentreSamples)
                {
                    _centres = new StickCentres
                    {
                        HatX = StickDigitizer.EstimateCentre(_hatXSamples),
                        HatY = StickDigitizer.EstimateCentre(_hatYSamples),
                        RX = StickDigitizer.EstimateCentre(_rxSamples),
                        RY = StickDigitizer.EstimateCentre(_rySamples)
                    };
                    _measuringCentres = false;
                    _centresJustMeasured = true;

                    _hatXSamples.Clear();
                    _hatYSamples.Clear();
                    _rxSamples.Clear();
                    _rySamples.Clear();
                }
            }

            // Digitalize both analog sticks (ABS_HAT0X/Y and ABS_RX/Y are 0..255, centered around a per-axis
            // measured or default centre). Both sticks share one implementation.
            State.Buttons[(int)GunButton.LLeft]  = StickDigitizer.IsLow(lx, _centres.HatX);
            State.Buttons[(int)GunButton.LRight] = StickDigitizer.IsHigh(lx, _centres.HatX);
            State.Buttons[(int)GunButton.LUp]    = StickDigitizer.IsLow(ly, _centres.HatY);
            State.Buttons[(int)GunButton.LDown]  = StickDigitizer.IsHigh(ly, _centres.HatY);

            State.Buttons[(int)GunButton.RLeft]  = StickDigitizer.IsLow(rx, _centres.RX);
            State.Buttons[(int)GunButton.RRight] = StickDigitizer.IsHigh(rx, _centres.RX);
            State.Buttons[(int)GunButton.RUp]    = StickDigitizer.IsLow(ry, _centres.RY);
            State.Buttons[(int)GunButton.RDown]  = StickDigitizer.IsHigh(ry, _centres.RY);

            // The depth axis the same way, around a threshold the user set rather than a measured centre:
            // where "near" ends depends on where the player stands, and nothing here can know that.
            int z = State.Z;
            int zThreshold = _zThreshold;
            State.Buttons[(int)GunButton.ZLow]  = DepthDigitizer.IsLow(z, zThreshold);
            State.Buttons[(int)GunButton.ZHigh] = DepthDigitizer.IsHigh(z, zThreshold);

            return ReadResult.Ok;
        }
    }
}
