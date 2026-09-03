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

        /// <summary>
        /// How long a single USB transfer may take before WinUSB abandons it. The gun
        /// answers in single-digit milliseconds, so this is a wide margin that exists
        /// only to bound a wedged device — it turns an indefinite block into one
        /// dropped frame. GunWorker's no-data teardown window (in the console
        /// project) must stay well above this value — by a factor of ten or better —
        /// or a single timeout becomes a hair trigger for tearing the device down.
        /// </summary>
        private const int PipeTimeoutMs = 100;

        /// <summary>
        /// False when the driver refused the transfer-timeout policy, in which case a
        /// wedged device can still block indefinitely.
        /// </summary>
        public bool PipeTimeoutApplied { get; private set; }

        /// <summary>
        /// The Win32 error code behind the most recent <see cref="USBException"/>
        /// classified by <see cref="Read"/>, or 0 if none has occurred yet.
        /// Diagnostic only: it lets the disconnect log line say what the driver
        /// actually reported, since the whole timeout/disconnect split rests on
        /// ERROR_SEM_TIMEOUT being 121 for this transfer path, and a driver that
        /// disagrees would otherwise be indistinguishable from a real unplug.
        /// </summary>
        public int LastWin32Error { get; private set; }
        private readonly byte[] readBuffer = new byte[15];
        private readonly byte[] decodedBuffer = new byte[13];
        private static readonly Guid deviceguid = new Guid("{A5DCBF10-6530-11D2-901F-00C04FB951ED}");

        // Number of frames to sample before settling on a measured centre for each axis.
        private const int CentreSamples = 60;

        private readonly List<int> _hatXSamples = new List<int>(CentreSamples);
        private readonly List<int> _hatYSamples = new List<int>(CentreSamples);
        private readonly List<int> _rxSamples = new List<int>(CentreSamples);
        private readonly List<int> _rySamples = new List<int>(CentreSamples);

        private StickCentres _centres = new StickCentres();
        private bool _measuringCentres = true;
        private bool _centresJustMeasured;

        // Each reader has its own state
        public GunState State { get; } = new GunState();

        /// <summary>
        /// The last raw 15-byte USB report, before decoding. Diagnostics only.
        /// </summary>
        public byte[] LastRawFrame { get; } = new byte[15];

        /// <summary>The device path this reader last connected to, or null.</summary>
        public string DevicePath { get; private set; }

        /// <summary>
        /// The per-axis stick centres in use. Assign a loaded set before the first
        /// Read() to skip measurement entirely.
        /// </summary>
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

        /// <summary>
        /// Returns the centres exactly once, if this reader measured them itself, so
        /// the caller can persist them. Returns false otherwise.
        /// </summary>
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
                    // The driver refused the policy. A reader without a timeout is
                    // what we have today, which works, so the connect still succeeds.
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

            // Safe to try the remembered path without checking claimedPaths: no two
            // readers can ever hold the same DevicePath, since the fallback loop below
            // skips claimed paths and Connect only records a path once it succeeds.
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
            catch (USBException ex)
            {
                LastWin32Error = UsbErrors.Win32CodeOf(ex);

                // A timed-out transfer is a dropped frame, not a disconnection: the device
                // is still enumerated and simply did not answer. Treating it as a
                // disconnect would release the gun's inputs and re-enumerate over a single
                // slow transfer.
                return UsbErrors.IsTransferTimeout(ex) ? ReadResult.BadPacket : ReadResult.Disconnected;
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

            int lx = (int)State.ABS_HAT0X;
            int ly = (int)State.ABS_HAT0Y;
            int rx = (int)State.ABS_RX;
            int ry = (int)State.ABS_RY;

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

            // Digitalize both analog sticks (ABS_HAT0X/Y and ABS_RX/Y are 0..255,
            // centered around a per-axis measured or default centre). Both sticks
            // share one implementation.
            State.BtnState[GunButton.LLeft]  = StickDigitizer.IsLow(lx, _centres.HatX);
            State.BtnState[GunButton.LRight] = StickDigitizer.IsHigh(lx, _centres.HatX);
            State.BtnState[GunButton.LUp]    = StickDigitizer.IsLow(ly, _centres.HatY);
            State.BtnState[GunButton.LDown]  = StickDigitizer.IsHigh(ly, _centres.HatY);

            State.BtnState[GunButton.RLeft]  = StickDigitizer.IsLow(rx, _centres.RX);
            State.BtnState[GunButton.RRight] = StickDigitizer.IsHigh(rx, _centres.RX);
            State.BtnState[GunButton.RUp]    = StickDigitizer.IsLow(ry, _centres.RY);
            State.BtnState[GunButton.RDown]  = StickDigitizer.IsHigh(ry, _centres.RY);


            // Compatibility with the EXE calibrator
            State.RAW_X = State.ABS_X;
            State.RAW_Y = State.ABS_Y;
            State.BTN_TRIGGER = State.BtnState[GunButton.Trigger];

            return ReadResult.Ok;
        }
    }
}
