﻿using MadWizard.WinUSBNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using vJoyInterfaceWrap;
using System.IO;

namespace GunconUSB
{
    //https://tewarid.github.io/2012/03/23/custom-usb-driver-and-app-using-winusb-and-c.html
...
    //https://tetherscript.com/hid-driver-kit-download/

    public partial class MainForm : Form
    {
        private bool _prevTrigger = false;

        //private readonly GunconReader gconreader = new GunconReader();

        public MainForm()
        {
            InitializeComponent();
            bindEvents();

            // Keyboard shortcut to launch calibration from anywhere
            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F12)
                {
                    StartCalibration();
                    e.Handled = true;
                }
            };
        }

        private void StartCalibration()
        {
            // Current screen size
            var bounds = Screen.PrimaryScreen.Bounds;
            int screenW = bounds.Width;
            int screenH = bounds.Height;

            // Path to the calibrator EXE (must sit next to the main EXE at run time)
            string exePath = Path.Combine(Application.StartupPath, "Guncon3Calibration.exe");

            // Close any previous calibration
            CalibBridge.Instance?.Dispose();

            // Launch calibration (opens the gray window with the red corner)
            CalibBridge.Instance = new CalibrationHost(screenW, screenH);
            CalibBridge.Instance.Start(exePath);

            // Visual notice (optional)
            try { this.BeginInvoke((MethodInvoker)(() => this.Text = "Calibrando: dispara 5 puntos (4 esquinas + centro)…")); } catch {}
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            //GammaManager.SetBrightness(255);
            //Thread
...
        }

        private void bindEvents()
        {
            this.Load += MainForm_Load;
            this.FormClosing += MainForm_FormClosing;
            GunconReader.ProgressChanged += Gconreader_ProgressChanged;
        }

        private void unBindEvents()
        {
            this.Load -= MainForm_Load;
            this.FormClosing -= MainForm_FormClosing;
            GunconReader.ProgressChanged -= Gconreader_ProgressChanged;
        }

        private void Gconreader_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            //object userObject = e.UserState;
            //int percentage = e.ProgressPercentage;
            //System.Diagnostics.Debug.WriteLine(userObject);
            //System.Diagnostics.Debug.WriteLine(DateTime.Now);

            if (rbMoveJoy.Checked || rbMoveMouse.Checked)
            {
                Helper.MoveMouse(GunState.PointerX, GunState.PointerY);
                //if (GunState.Trigger)
                //{
                //    MouseOperations.MouseEvent(MouseOperations.MouseEventFlags.LeftDown);
                //    MouseOperations.MouseEvent(MouseOperations.MouseEventFlags.LeftUp);
                //}
            }

            UpdateForm();

            // --- Send the shot to the calibrator if it is active (first click) ---
            bool trigger = GunState.Trigger;
            if (CalibBridge.Instance != null && trigger && !_prevTrigger)
            {
                // In this project PointerX/Y are the RAW values we care about
                CalibBridge.Instance.OnTriggerRaw(GunState.PointerX, GunState.PointerY);
            }
            _prevTrigger = trigger;
            // --- end of block ---
        }

        private void Start()
        {
            GunconReader.Start();
        }

        private void Stop()
        {
            GunconReader.Stop();
        }

        private void UpdateForm()
        {
            //if (InvokeRequired)
            //{
...
        }

        private void btnCalibrate_Click(object sender, EventArgs e)
        {
            StartCalibration();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                Stop();
            }
            catch { }
        }

        // Remaining handlers and original logic...
        // ...
        //            //var cursor = new Cursor(Cursor.Current.Handle);
        //            Cursor.Position = new Point(x, y);
        //            //Cursor.Clip = new Rectangle(this.Location, this.Size);
        //        }
        //    }

        //    if (GunState.BtnB)
        //    {
        //        Stop();
        //        //shouldContinue = false;
        //    }
        //}
    }

}
