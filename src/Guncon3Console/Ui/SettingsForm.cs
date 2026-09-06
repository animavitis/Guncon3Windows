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
    /// <summary>Three checkboxes and one number. All but one live in settings.txt; "Run at Windows logon" is
    /// the registry entry, read when the dialog opens and applied on OK — the registry is the source of truth
    /// for it, so a value removed by some startup manager shows here as off.</summary>
    /// <remarks>
    /// Laid out rather than positioned: the log path and the registry's own error message are both as long as
    /// the machine makes them, and at a fixed 460 px with fixed control positions either one was cut to an
    /// ellipsis — which hid exactly the part worth reading. Everything sizes to its text now, and the two long
    /// strings sit on their own wrapped lines under the checkbox they belong to.
    /// </remarks>
    internal sealed class SettingsForm : Form
    {
        private const int PadPx = 14;
        private const int GapPx = 8;
        private const int IndentPx = 22;

        /// <summary>Past this a path wraps instead of widening the dialog across the screen.</summary>
        private const int NoteMaxWidthPx = 520;

        private const int MinWidthPx = 420;
        private const int ThresholdBoxWidthPx = 84;

        /// <summary>Lifts the caption onto the same baseline as the box beside it.</summary>
        private const int ThresholdLabelTopPx = 4;

        private readonly CheckBox _startMinimized;
        private readonly CheckBox _autostart;
        private readonly CheckBox _logToFile;
        private readonly NumericUpDown _zThreshold;

        /// <summary>Why "Run at Windows logon" is greyed out, when it is. Hidden otherwise.</summary>
        private readonly Label _autostartNote;

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
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _startMinimized = Check("Start minimised to the tray", settings.StartMinimized);
            _autostart = Check("Run at Windows logon", false);
            _logToFile = Check("Write the log to a file", settings.LogToFile);

            _autostartNote = Note(string.Empty);
            _autostartNote.Visible = false;

            // A number rather than a checkbox, and one nothing here can pick for the user: where "near" ends
            // depends on where they stand. The note under it says where to read the value off.
            _zThreshold = new NumericUpDown
            {
                Minimum = DepthDigitizer.MinThreshold,
                Maximum = DepthDigitizer.MaxThreshold,
                Value = Math.Clamp(settings.ZThreshold, DepthDigitizer.MinThreshold, DepthDigitizer.MaxThreshold),
                Width = ThresholdBoxWidthPx,
                Margin = new Padding(IndentPx, 0, 0, 0)
            };

            var zRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, GapPx, 0, 0)
            };
            zRow.Controls.Add(new Label
            {
                Text = "Depth threshold for the ZLow / ZHigh mappings",
                AutoSize = true,
                Margin = new Padding(0, ThresholdLabelTopPx, 0, 0)
            });
            zRow.Controls.Add(_zThreshold);

            var ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(84, 28)
            };
            ok.Click += OnOk;

            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(84, 28)
            };

            // Right to left, so the first one added is the rightmost: Cancel on the edge, OK beside it.
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, GapPx, 0, 0)
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(PadPx),
                MinimumSize = new Size(MinWidthPx, 0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.Controls.Add(_startMinimized);
            layout.Controls.Add(_autostart);
            layout.Controls.Add(_autostartNote);
            layout.Controls.Add(_logToFile);
            layout.Controls.Add(Note(FileSink.FilePath));
            layout.Controls.Add(zRow);
            layout.Controls.Add(Note("Read the live Z beside the bar on the Test Input tab and set this from it; "
                                   + "bind ZLow and ZHigh in mapping.txt."));
            layout.Controls.Add(buttons);

            Controls.Add(layout);

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
                LogToFile = _logToFile.Checked,
                ZThreshold = (int)_zThreshold.Value
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
                // unused. The reason goes on its own line: it is a Windows message, and no checkbox caption is
                // wide enough for one.
                _autostart.Enabled = false;
                _autostartNote.Text = "unavailable: " + ex.Message;
                _autostartNote.Visible = true;
            }
        }

        private static CheckBox Check(string text, bool isChecked)
            => new CheckBox
            {
                Text = text,
                Checked = isChecked,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, GapPx)
            };

        /// <summary>A second line under a checkbox: a path, or why the box above is greyed out. Wraps rather
        /// than widening the dialog, and dimmed so it reads as detail rather than as another setting.</summary>
        private static Label Note(string text)
            => new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(NoteMaxWidthPx, 0),
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(IndentPx, 0, 0, GapPx)
            };
    }
}
