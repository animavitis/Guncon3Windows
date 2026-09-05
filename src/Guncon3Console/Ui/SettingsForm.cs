// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Drawing;
using System.IO;
using System.Security;
using System.Windows.Forms;
using Guncon3.Core;
using Guncon3Console.Logging;

namespace Guncon3Console.Ui
{
    /// <summary>Three checkboxes. Two of them live in settings.txt; "Run at Windows logon" is the registry
    /// entry, read when the dialog opens and applied on OK — the registry is the source of truth for it, so a
    /// value removed by some startup manager shows here as off.</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly CheckBox _startMinimized;
        private readonly CheckBox _autostart;
        private readonly CheckBox _logToFile;

        /// <summary>What ReadAutostart found the registry to hold, so OnOk can tell "unchanged" from "toggled"
        /// and revert to the truth on failure.</summary>
        private bool _autostartAtOpen;

        /// <summary>What the user accepted. Only meaningful once ShowDialog returned OK.</summary>
        public Settings Result { get; private set; }

        public SettingsForm(Settings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Result = settings;

            Text = "GUNCON3 settings";
            Icon = AppIcon.Value;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(460, 190);

            _startMinimized = Check("Start minimised to the tray", 16, settings.StartMinimized);
            _autostart = Check("Run at Windows logon", 46, false);
            _logToFile = Check("Write the log to " + FileSink.FilePath, 76, settings.LogToFile);

            var ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(268, 140),
                Size = new Size(84, 28)
            };
            ok.Click += OnOk;

            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(360, 140),
                Size = new Size(84, 28)
            };

            Controls.Add(_startMinimized);
            Controls.Add(_autostart);
            Controls.Add(_logToFile);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            ReadAutostart();
        }

        /// <summary>Applied here rather than in the caller: a registry failure has to revert the checkbox and
        /// keep the dialog open.</summary>
        private void OnOk(object sender, EventArgs e)
        {
            if (_autostart.Enabled && _autostart.Checked != _autostartAtOpen)
            {
                bool wanted = _autostart.Checked;
                try
                {
                    if (wanted) Autostart.Enable();
                    else Autostart.Disable();
                    _autostartAtOpen = wanted;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
                {
                    // Revert to what the registry actually holds, not to the opposite of what was wanted:
                    // nothing changed there, so the checkbox must not either.
                    _autostart.Checked = _autostartAtOpen;
                    MessageBox.Show(this,
                        "Run at Windows logon could not be changed: " + ex.Message,
                        "GUNCON3", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    // Keeps the dialog open, so the reverted checkbox is visible.
                    DialogResult = DialogResult.None;
                    return;
                }
            }

            Result = new Settings
            {
                StartMinimized = _startMinimized.Checked,
                LogToFile = _logToFile.Checked
            };
        }

        private void ReadAutostart()
        {
            try
            {
                _autostartAtOpen = Autostart.IsEnabled();
                _autostart.Checked = _autostartAtOpen;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
            {
                // Showing a guess would be worse than showing that we cannot tell. The checkbox stays disabled,
                // so OnOk's Enabled check never touches the registry — _autostartAtOpen is left false and
                // unused.
                _autostart.Enabled = false;
                _autostart.Text += " (unavailable: " + ex.Message + ")";
            }
        }

        private static CheckBox Check(string text, int top, bool isChecked)
            => new CheckBox
            {
                Text = text,
                Checked = isChecked,
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(16, top),
                Size = new Size(428, 24)
            };
    }
}
