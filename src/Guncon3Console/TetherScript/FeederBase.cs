// SPDX-License-Identifier: GPL-2.0-only
using System;
using Guncon3.Core;

namespace Guncon3Console.TetherScript
{
    /// <summary>The scaffolding every TetherScript feeder shares: the HID connection, the reusable report
    /// buffer, and the health tracking. A subclass supplies the report struct it packs, the virtual device it
    /// talks to, and what "release everything this device is holding" means for that device.</summary>
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

        /// <summary>Called on the healthy↔unhealthy transition, from the worker thread. <see cref="Healthy"/>
        /// is a plain bool written there and read from the UI thread: a stale read is one repaint behind, which
        /// the status timer corrects.</summary>
        public Action HealthChanged
        {
            get => _health.Changed;
            set => _health.Changed = value;
        }

        /// <param name="name">Prefix for this feeder's log lines, e.g. "Mouse".</param>
        /// <param name="deviceName">The driver kit's name for the device, used only in the connect-failure
        /// message. Defaults to <paramref name="name"/>.</param>
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
            HID.OnLog += OnHidLog;
            try
            {
                HID.Connect((ushort)DriversConst.TTC_VENDORID, (ushort)_productId);

                if (!HID.Connected)
                    throw new InvalidOperationException($"Could not connect to the TetherScript {_deviceName} device.");
            }
            catch
            {
                // R5: a failed connect must not leave this feeder subscribed to a controller nothing will ever
                // disconnect.
                HID.OnLog -= OnHidLog;
                throw;
            }

            OnConnected();
        }

        public void Disconnect()
        {
            try { ReleaseHeldInputs(); }
            catch { }

            HID.Disconnect();
            HID.OnLog -= OnHidLog;
        }

        /// <summary>Runs after a successful connect. Override to reset whatever decides when the next send
        /// happens, so a reconnected device is refreshed rather than left matching a stale "last sent"
        /// value.</summary>
        protected virtual void OnConnected() { }

        /// <summary>Clears everything this device is holding. Called on disconnect, where it is wrapped in a
        /// catch: a device that has already gone away must not stop the rest of the shutdown.</summary>
        protected abstract void ReleaseHeldInputs();

        protected void Send(in TReport data)
        {
            HidReport.Write(in data, ReportBuffer);
            _health.Track(HID.SendData(ReportBuffer, (uint)ReportSize));
        }

        private void OnHidLog(object sender, LogEventArgs e) => Log.Line($"{_name} {e.Msg}");
    }
}
