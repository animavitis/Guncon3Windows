using System;
using Guncon3.Core;

namespace Guncon3Console.TetherScript
{
    /// <summary>
    /// The scaffolding every TetherScript feeder shares: the HID connection, the
    /// reusable report buffer, and the health tracking. A subclass supplies the
    /// report struct it packs, the virtual device it talks to, and what "release
    /// everything this device is holding" means for that device.
    /// </summary>
    /// <typeparam name="TReport">The feature-report struct this feeder sends.</typeparam>
    abstract class FeederBase<TReport> where TReport : struct
    {
        /// <summary>Marshalled report size, computed once per closed generic type.</summary>
        protected static readonly int ReportSize = HidReport.SizeOf<TReport>();

        protected readonly HIDController HID = new HIDController();
        protected readonly GunState State;

        /// <summary>Reused for every send, so feeding never allocates.</summary>
        protected readonly byte[] ReportBuffer = new byte[ReportSize + 1];

        private readonly FeederHealth _health;
        private readonly string _name;
        private readonly string _deviceName;
        private readonly DriversConst _productId;

        /// <summary>False once the virtual device has rejected several reports in a row.</summary>
        public bool Healthy => _health.Healthy;

        /// <param name="state">The gun state this feeder reads from.</param>
        /// <param name="name">Prefix for this feeder's log lines, e.g. "Mouse".</param>
        /// <param name="productId">The TetherScript virtual device to connect to.</param>
        /// <param name="deviceName">
        /// The device's name as the driver kit calls it, used only in the
        /// connect-failure message. Defaults to <paramref name="name"/>.
        /// </param>
        protected FeederBase(GunState state, string name, DriversConst productId, string deviceName = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            _name = name;
            _deviceName = deviceName ?? name;
            _productId = productId;
            _health = new FeederHealth(name);
        }

        public void Connect()
        {
            HID.OnLog += Log;
            HID.VendorID = (ushort)DriversConst.TTC_VENDORID;
            HID.ProductID = (ushort)_productId;
            HID.Connect();

            if (!HID.Connected)
                throw new Exception($"Could not connect to the TetherScript {_deviceName} device.");

            OnConnected();
        }

        public void Disconnect()
        {
            try { ReleaseHeldInputs(); }
            catch { }

            HID.Disconnect();
            HID.OnLog -= Log;
        }

        /// <summary>
        /// Runs after a successful connect. Override to reset whatever state decides
        /// when the next send happens, so a reconnected device is refreshed rather
        /// than left matching a stale "last sent" value.
        /// </summary>
        protected virtual void OnConnected() { }

        /// <summary>
        /// Clears everything this device is holding. Called on disconnect, where it
        /// is wrapped in a catch: a device that has already gone away must not stop
        /// the rest of the shutdown.
        /// </summary>
        protected abstract void ReleaseHeldInputs();

        /// <summary>Packs one report into the shared buffer and sends it.</summary>
        protected void Send(in TReport data)
        {
            HidReport.Write(in data, ReportBuffer);
            _health.Track(HID.SendData(ReportBuffer, (uint)ReportSize));
        }

        private void Log(object sender, LogArgs e) => ConsoleLog.Line($"{_name} {e.Msg}");
    }
}
