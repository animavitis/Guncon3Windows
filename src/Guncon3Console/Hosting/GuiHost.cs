// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Windows.Forms;
using Guncon3Console.Logging;
using Guncon3Console.Ui;

namespace Guncon3Console.Hosting
{
    /// <summary>
    /// The windowed host: sinks, the fatal and unhandled-exception policy, the engine,
    /// the window, the message loop. It attaches no console at all — a GUI start that
    /// wrote to one would flash a black window on every double-click.
    /// </summary>
    internal static class GuiHost
    {
        /// <param name="instance">The single-instance lock; Program disposes it after this returns.</param>
        public static int Run(SingleInstance instance)
        {
            ArgumentNullException.ThrowIfNull(instance);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // The calibration window's DPI handling is written against PerMonitorV2 and
            // it is the same window in both hosts.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            var uiSink = new UiSink();
            Log.AddSink(uiSink);

            // Read after the sink exists, so a complaint about a broken settings.txt has
            // somewhere to appear.
            var settings = SettingsStore.Load();

            FileSink fileSink = null;
            if (settings.LogToFile)
            {
                fileSink = FileSink.TryOpen(out string error);
                if (fileSink != null)
                    Log.AddSink(fileSink);
                else
                    Log.Warn($"[Log] {FileSink.FilePath} could not be opened: {error}. Writing the log to a file is off for this session.");
            }

            Log.FatalHandler = (msg, ex) =>
            {
                // Before Application.Run there is no message loop and no window: the
                // Start() == false branch below opens the window on the log instead,
                // which is more use than a modal box standing in front of it.
                if (!Application.MessageLoop) return;

                MessageBox.Show(
                    msg + (ex == null ? string.Empty : Environment.NewLine + Environment.NewLine + ex.Message),
                    "GUNCON3", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            };

            // An exception on the window thread is logged and shown, and the process
            // stays up. A worker thread keeps its own catch.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                Log.Error("Unhandled error on the window thread: " + e.Exception);
                MessageBox.Show(e.Exception.Message, "GUNCON3", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };

            var app = new App();

            // Start before the window exists, so a gun with no calibration file opens
            // its calibration window on its own, with no owner.
            bool started = app.Start();

            using var form = new MainForm(
                app, uiSink, fileSink,
                // What the session actually got: a log file that would not open leaves
                // the setting off rather than lying to the settings dialog.
                settings with { LogToFile = fileSink != null });

            // The handle must exist before the listener starts: app.Start() above can
            // sit on a calibration dialog for minutes, and a second start signalling
            // during that window must not find no handle to BeginInvoke on. A
            // BeginInvoke against an already-created handle is simply pumped once
            // Application.Run begins.
            form.EnsureHandleCreated();

            // A second start sets the show event instead of opening a second window
            // that would fight this one for the device. The callback arrives on the
            // listener thread, so it is marshalled the way OnStatusChanged is.
            instance.ListenForShow(() =>
            {
                if (form.IsDisposed || !form.IsHandleCreated) return;

                try
                {
                    form.BeginInvoke(form.ShowWindow);
                }
                catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
                {
                    // The window went away between the check and the call; there is
                    // nothing left to bring to the front.
                }
            });

            try
            {
                // A degraded start has something to say; it must be seen even when the
                // user asked to start minimized.
                if (settings.StartMinimized && started)
                {
                    Application.Run();
                }
                else
                {
                    Application.Run(form);
                }

                // Any Environment.ExitCode = 1 that Log.Fatal set inside App.Start has already been shown to the user.
                return 0;
            }
            finally
            {
                Log.FatalHandler = null;
                Log.RemoveSink(uiSink);
                // The file sink now belongs to the form, which closes it in ShutDownAndExit.
            }
        }
    }
}
