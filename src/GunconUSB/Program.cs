using System;
using System.IO;
using System.IO.Pipes;
using System.Windows.Forms;
using GunconUSB; // for GunconReader / GunState

namespace Guncon3Calibration
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Create the calibration window
            var form = new Form1();

            if (args != null && args.Length > 0)
            {
                // === PIPE MODE (as before) ===
                form.SetPipeHandle(args[0]); // enables TargetNext() over the pipe
                Application.Run(form);
                return;
            }

            // === STANDALONE MODE (no arguments) ===
            // 1) Start the gun reader
            GunconReader.ProgressChanged += (s, e) =>
            {
                // Every time a packet arrives, notify the form
                form.OnGunReport(GunState.PointerX, GunState.PointerY, GunState.Trigger);
            };

            // 2) Start the reader and open the window
            try
            {
                GunconReader.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo iniciar la lectura de la GunCon3.\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.Run(form);

            // 3) When the window closes, stop
            try { GunconReader.Stop(); } catch { }
        }
    }
}

