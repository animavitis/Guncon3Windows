// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Guncon3.Core;
using Guncon3Console.Logging;
using Microsoft.Win32;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace Guncon3Console.Ui
{
    /// <summary>
    /// The one window. Owns the tray icon, shows every gun and the log, and turns
    /// menu items, keys and tray items into <see cref="App"/> calls.
    ///
    /// Every engine call happens on this thread and only one at a time: the engine
    /// takes no locks and a calibration runs a modal dialog that keeps pumping
    /// messages, so <see cref="Run"/> disables the Gun menu and the tray menu for the
    /// duration. The two things that arrive from elsewhere — App.StatusChanged and
    /// UiSink.Appended — are marshalled here.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private const int StatusRefreshMs = 1000;
        private const int LogDrainMs = 100;
        private const int StatusColumns = 8;

        private static readonly Color WarnColour = Color.FromArgb(150, 110, 0);
        private static readonly Color ErrorColour = Color.FromArgb(190, 30, 30);

        private readonly App _app;
        private readonly UiSink _log;
        private readonly TrayIcon _tray;

        /// <summary>Null when the log is not being written to a file. Owned here, because the settings dialog
        /// can turn it on and off.</summary>
        private FileSink _fileSink;

        /// <summary>What settings.txt currently says, as this session actually applied it.</summary>
        private Settings _settings;

        private readonly ToolStripMenuItem _recalibrateItem;
        private readonly ToolStripMenuItem _reloadItem;
        private readonly ToolStripMenuItem _modeItem;
        private readonly ToolStripMenuItem _searchAgainItem;
        private readonly TabControl _tabs;
        private readonly TabPage _statusPage;
        private readonly TabPage _logPage;
        private readonly ListView _statusList;
        private readonly RichTextBox _logBox;
        private readonly MappingEditor _mapping;
        private readonly InputTestPanel _testInput;
        private readonly OutputTestPanel _testOutput;
        private readonly TabPage _testInputPage;
        private readonly TabPage _testOutputPage;
        private readonly ToolStripStatusLabel _modeLabel;
        private readonly ToolStripStatusLabel _gunsLabel;
        private readonly WinFormsTimer _statusTimer;
        private readonly WinFormsTimer _logTimer;

        private readonly Action _refreshStatus;
        private readonly Action<LogEntry> _onLogAppended;
        private readonly SessionEndingEventHandler _onSessionEnding;

        private bool _busy;
        private bool _exiting;
        private bool _exitRequested;
        private volatile bool _shutDown;
        private bool _sessionEndingHooked;
        private volatile bool _logDirty;
        private bool _balloonShown;
        private bool _watching;

        /// <summary>True while the status strip is showing the degraded message, so <see
        /// cref="RefreshDegraded"/> can tell a rescan that has just succeeded from a session that never was
        /// degraded.</summary>
        private bool _wasDegraded;

        /// <summary>What the status strip says while <see cref="Degraded"/>. A failed rescan replaces it.</summary>
        private string _degradedMessage = "No gun found — plug one in and press Search again.";

        /// <param name="fileSink">The open log file, or null when the option is off (or its file would not
        /// open).</param>
        /// <param name="settings">What settings.txt says, with LogToFile already corrected to what actually
        /// happened.</param>
        public MainForm(App app, UiSink log, FileSink fileSink, Settings settings)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _fileSink = fileSink;

            Icon = AppIcon.Value;
            ClientSize = new Size(880, 520);
            MinimumSize = new Size(560, 320);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;

            // In the menu bar rather than on a strip of their own: three text buttons are not worth a whole row
            // of the window, and the keys beside their names are what most of this gets driven by anyway. The
            // current mode is not repeated here — the title bar and the status strip both carry it.
            _recalibrateItem = UiFactory.MenuItem("&Recalibrate (F12)", Recalibrate);
            _reloadItem = UiFactory.MenuItem("Re&load mappings (R)", ReloadMappings);
            _modeItem = UiFactory.MenuItem("Toggle calibration &mode (H)", ToggleMode);

            _statusList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                LabelEdit = false,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _statusList.Columns.Add("#", 34);
            _statusList.Columns.Add("Device", 250);
            _statusList.Columns.Add("Connected", 80);
            _statusList.Columns.Add("Calibration", 130);
            _statusList.Columns.Add("Mouse", 70);
            _statusList.Columns.Add("Keyboard", 80);
            _statusList.Columns.Add("Joystick", 70);
            _statusList.Columns.Add("Note", 240);

            _statusPage = new TabPage("Status");
            _statusPage.Controls.Add(_statusList);

            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Font = new Font(FontFamily.GenericMonospace, 9f),
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                // RichTextBox defaults to a 32767-character limit, which would silently drop appends long
                // before UiSink.Capacity lines had accumulated.
                MaxLength = int.MaxValue,
                // A read-only RichTextBox paints itself grey otherwise, which reads as disabled rather than as a log.
                BackColor = SystemColors.Window,
                HideSelection = false
            };

            var logTools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            logTools.Items.Add(UiFactory.Button("Clear", ClearLog));
            logTools.Items.Add(UiFactory.Button("Copy all", CopyLog));

            _logPage = new TabPage("Log");
            _logPage.Controls.Add(_logBox);
            _logPage.Controls.Add(logTools);

            _mapping = new MappingEditor(RunModal) { Dock = DockStyle.Fill };
            _mapping.Saved += ReloadMappings;

            var mappingPage = new TabPage("Mapping");
            mappingPage.Controls.Add(_mapping);

            // The panel is given two read-only delegates rather than the engine, so it cannot reach anything
            // that would need the busy guard.
            _testInput = new InputTestPanel(_app.LatestFrame, _app.Status) { Dock = DockStyle.Fill };
            _testOutput = new OutputTestPanel(_app.LatestFrame, _app.Status) { Dock = DockStyle.Fill };

            _testInputPage = new TabPage("Test Input");
            _testInputPage.Controls.Add(_testInput);

            _testOutputPage = new TabPage("Test Output");
            _testOutputPage.Controls.Add(_testOutput);

            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.Add(_statusPage);
            _tabs.TabPages.Add(_logPage);
            _tabs.TabPages.Add(mappingPage);
            _tabs.TabPages.Add(_testInputPage);
            _tabs.TabPages.Add(_testOutputPage);
            _tabs.SelectedIndexChanged += (_, __) => UpdateWatch();

            _modeLabel = new ToolStripStatusLabel(ModeLabelText());
            _gunsLabel = new ToolStripStatusLabel(string.Empty)
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var statusStrip = new StatusStrip();
            statusStrip.Items.Add(_modeLabel);
            statusStrip.Items.Add(_gunsLabel);

            // Never disabled by SetActionsEnabled: it is the one action a session with no gun still has, and
            // Run's own _busy guard stops a second click.
            _searchAgainItem = UiFactory.MenuItem("Search &again", SearchAgain);

            var fileMenu = new ToolStripMenuItem("&File");
            fileMenu.DropDownItems.Add(_searchAgainItem);
            fileMenu.DropDownItems.Add(UiFactory.MenuItem("&Settings…", OpenSettings));
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(UiFactory.MenuItem("E&xit", RequestExit));
            var gunMenu = new ToolStripMenuItem("&Gun");
            gunMenu.DropDownItems.Add(_recalibrateItem);
            gunMenu.DropDownItems.Add(_reloadItem);
            gunMenu.DropDownItems.Add(_modeItem);
            var helpMenu = new ToolStripMenuItem("&Help");
            helpMenu.DropDownItems.Add(UiFactory.MenuItem("&About", ShowAbout));
            var menu = new MenuStrip();
            menu.Items.Add(fileMenu);
            menu.Items.Add(gunMenu);
            menu.Items.Add(helpMenu);
            MainMenuStrip = menu;

            // Docking is applied from the last added control backwards, so the filling control goes in first
            // and the menu last.
            Controls.Add(_tabs);
            Controls.Add(statusStrip);
            Controls.Add(menu);

            _tray = new TrayIcon(AppIcon.Value);
            _tray.ShowHideClicked += ToggleWindow;
            // Like the tray's Recalibrate, this shows the window first: a rescan can open a calibration dialog
            // for a gun with no file.
            _tray.SearchAgainClicked += () => { ShowWindow(); SearchAgain(); };
            _tray.DoubleClicked += ShowWindow;
            // Tray-initiated recalibration shows the window first, so a hidden main window does not leave the
            // calibration dialog looking ownerless.
            _tray.RecalibrateClicked += () => { ShowWindow(); Recalibrate(); };
            _tray.ReloadMappingsClicked += ReloadMappings;
            _tray.ToggleModeClicked += ToggleMode;
            _tray.ExitClicked += RequestExit;

            _refreshStatus = RefreshStatus;
            _onLogAppended = _ => _logDirty = true;
            _onSessionEnding = (_, __) => OnSessionEnding();

            _app.CalibrationOwner = this;
            _app.StatusChanged += OnStatusChanged;
            _log.Appended += _onLogAppended;
            // SystemEvents.SessionEnding is hooked once the handle exists (OnHandleCreated).

            _statusTimer = new WinFormsTimer { Interval = StatusRefreshMs };
            _statusTimer.Tick += (_, __) => RefreshStatus();
            _statusTimer.Start();

            _logTimer = new WinFormsTimer { Interval = LogDrainMs };
            _logTimer.Tick += (_, __) => DrainLogIfDirty();
            _logTimer.Start();

            // Everything logged during Start() is already in the sink.
            Append(_log.Snapshot());
            RefreshStatus();

            if (Degraded)
            {
                // No engine to drive: open on the log, which says why.
                _tabs.SelectedTab = _logPage;
                SetActionsEnabled(false);
            }

            RefreshDegraded();
        }

        /// <summary>No gun is connected: the three engine actions are refused and the File menu offers Search
        /// again.</summary>
        private bool Degraded => !_app.HasGuns;

        // ------------------------------------------------------------- actions

        private void Recalibrate() => RunEngineAction(_app.RecalibrateAll);

        private void ReloadMappings() => RunEngineAction(_app.ReloadMappings);

        private void ToggleMode() => RunEngineAction(() => _app.SetMode(
            _app.Mode == CalibrationMode.Rect ? CalibrationMode.Homography : CalibrationMode.Rect));

        /// <summary>The Search again button and its tray item. Goes through <see cref="Run"/> because <see
        /// cref="App.Rescan"/> mutates the slot list and can open a calibration window.</summary>
        private void SearchAgain() => Run(SearchAgainCore);

        private void SearchAgainCore()
        {
            Log.Line("Looking for a gun again…");

            if (!_app.Rescan())
                _degradedMessage = "Still no gun found — check the cable and press Search again.";
        }

        /// <summary>One of the three actions that need at least one gun; refused while <see cref="Degraded"/>,
        /// though F12, R and H are live keys whatever the state.</summary>
        private void RunEngineAction(Action action)
        {
            if (Degraded) return;

            Run(action);
        }

        /// <summary>Runs one engine action with the UI locked, so a tray-menu click during a calibration's
        /// modal dialog cannot re-enter it.</summary>
        private void Run(Action action)
        {
            if (_busy || _shutDown) return;

            _busy = true;
            SetActionsEnabled(false);
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // The engine reports its own failures; this is the last resort for one that escapes, and it
                // must not take the window down with it.
                Log.Error("Action failed: " + ex);
            }
            finally
            {
                _busy = false;
                SetActionsEnabled(!Degraded);
                RefreshStatus();
                RefreshDegraded();

                // An Exit taken while this ran was queued rather than run underneath the modal calibration
                // loop; replay it now that it is safe.
                if (_exitRequested) RequestExit();
            }
        }

        /// <summary>
        /// Runs one modal dialog with the same busy state <see cref="Run"/> holds, so a
        /// tray Exit is queued rather than running underneath its modal loop. Runs even
        /// while <see cref="Degraded"/>: Settings and About touch no engine state, and a
        /// start with no gun must not lock the user out of either.
        /// </summary>
        private void RunModal(Action action)
        {
            if (_busy || _shutDown) return;

            _busy = true;
            SetActionsEnabled(false);
            try
            {
                action();
            }
            finally
            {
                _busy = false;
                SetActionsEnabled(!Degraded);

                // An Exit taken while this ran was queued rather than run underneath the dialog's own modal
                // loop; replay it now that it is safe.
                if (_exitRequested) RequestExit();
            }
        }

        private void SetActionsEnabled(bool enabled)
        {
            _recalibrateItem.Enabled = enabled;
            _reloadItem.Enabled = enabled;
            _modeItem.Enabled = enabled;
            _tray.ActionsEnabled = enabled;
        }

        /// <summary>Shows or clears the degraded state. The strip's normal text and colour belong to <see
        /// cref="RefreshStatus"/>, which runs first.</summary>
        private void RefreshDegraded()
        {
            bool degraded = Degraded;

            // Available, not Visible: a hidden item's Visible getter reads false while its menu is closed,
            // which would make the next assignment a no-op.
            _searchAgainItem.Available = degraded;
            _tray.SearchAgainVisible = degraded;

            if (degraded)
            {
                _gunsLabel.ForeColor = Color.Firebrick;
                _gunsLabel.Text = _degradedMessage;
            }
            else if (_wasDegraded)
            {
                // A rescan has just found a gun. The log has done its job; the rows the user was waiting for
                // are on the Status tab.
                _tabs.SelectedTab = _statusPage;

                // The workers only just came into existence with publication off, while _watching may still say
                // "on" from a degraded session where WatchFrames had no worker to set. Re-apply it from
                // scratch.
                _watching = false;
                UpdateWatch();
            }

            _wasDegraded = degraded;
        }

        // -------------------------------------------------------------- status

        /// <summary>Rebuilds the rows, the title, the status strip and the tray tooltip from one <see
        /// cref="App.Status"/> snapshot. Rows are updated in place while the gun count holds, so a refresh
        /// neither flickers nor loses the selection.</summary>
        private void RefreshStatus()
        {
            var statuses = _app.Status();

            _statusList.BeginUpdate();
            try
            {
                if (_statusList.Items.Count != statuses.Count)
                {
                    _statusList.Items.Clear();
                    foreach (var status in statuses)
                        _statusList.Items.Add(NewRow(status));
                }
                else
                {
                    for (int i = 0; i < statuses.Count; i++)
                        FillRow(_statusList.Items[i], statuses[i]);
                }
            }
            finally
            {
                _statusList.EndUpdate();
            }

            int connected = 0;
            foreach (var status in statuses)
                if (status.IsConnected) connected++;

            Text = TitleText();
            _modeLabel.Text = ModeLabelText();
            _tray.Tooltip = string.Create(CultureInfo.InvariantCulture, $"GUNCON3 — {connected} gun(s) connected");

            if (!Degraded)
            {
                _gunsLabel.ForeColor = SystemColors.ControlText;
                _gunsLabel.Text = string.Create(CultureInfo.InvariantCulture, $"{statuses.Count} gun(s), {connected} connected");
            }

            // Fixed after Start in practice; both ignore a call that changes nothing.
            _mapping.SetFiles(statuses);
            _testInput.SetGuns(statuses);
            _testOutput.SetGuns(statuses);
        }

        private static ListViewItem NewRow(GunStatus status)
        {
            var row = new ListViewItem();
            for (int i = 1; i < StatusColumns; i++) row.SubItems.Add(string.Empty);
            FillRow(row, status);
            return row;
        }

        private static void FillRow(ListViewItem row, GunStatus status)
        {
            row.SubItems[0].Text = (status.Index + 1).ToString(CultureInfo.InvariantCulture);
            row.SubItems[1].Text = DeviceTail(status.DevicePath);
            row.SubItems[2].Text = status.IsConnected ? "yes" : "no";
            row.SubItems[3].Text = CalibrationText(status);
            row.SubItems[4].Text = status.MouseHealthy ? "OK" : "failed";
            row.SubItems[5].Text = status.KeyboardHealthy ? "OK" : "failed";
            row.SubItems[6].Text = status.JoystickHealthy ? "OK" : "failed";
            row.SubItems[7].Text = status.PipeAbandoned ? "idle after read-thread timeout" : string.Empty;
        }

        private static string CalibrationText(GunStatus status)
            => !status.HasCalibration ? "none"
             : status.HasHomography ? "rect + homography"
             : "rect";

        /// <summary>The readable part of a WinUSB device path: the `\\?\` prefix and the class GUID at the end
        /// say nothing a user needs, the VID/PID and the instance id do.</summary>
        private static string DeviceTail(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return "—";

            string text = devicePath.StartsWith(@"\\?\", StringComparison.Ordinal) ? devicePath[4..] : devicePath;
            int guid = text.IndexOf('{', StringComparison.Ordinal);
            if (guid > 0) text = text[..guid].TrimEnd('#');
            return text;
        }

        private string TitleText()
            => string.Create(CultureInfo.InvariantCulture, $"GUNCON3 {AppInfo.Version} — mode: {_app.Mode}");

        private string ModeLabelText() => "Mode: " + _app.Mode;

        /// <summary><see cref="App.StatusChanged"/> can arrive on a worker thread; the rows may only be touched
        /// here.</summary>
        private void OnStatusChanged()
        {
            if (_shutDown || IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke(_refreshStatus);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                // The window went away between the check and the call; the status timer's next tick would have
                // redrawn it anyway.
            }
        }

        // ----------------------------------------------------------------- log

        private void DrainLogIfDirty()
        {
            if (!_logDirty) return;

            _logDirty = false;
            Append(_log.Drain());
        }

        /// <summary>Appends a batch of lines, coloured by level, so a burst from three worker threads costs one
        /// update per 100 ms rather than one per line.</summary>
        private void Append(LogEntry[] entries)
        {
            if (entries.Length == 0) return;

            // Follow the tail only while the caret is already at the end, so a user who scrolled up to read
            // something keeps their place.
            bool atEnd = _logBox.SelectionStart + _logBox.SelectionLength >= _logBox.TextLength;

            foreach (var entry in entries)
            {
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.SelectionLength = 0;
                _logBox.SelectionColor = ColourOf(entry.Level);
                _logBox.AppendText(entry.Text + Environment.NewLine);
            }

            // Bound the control to the sink's own capacity: the sink already drops lines past this count.
            if (_logBox.Lines.Length > UiSink.Capacity)
            {
                _logBox.SelectionStart = 0;
                _logBox.SelectionLength = _logBox.GetFirstCharIndexFromLine(_logBox.Lines.Length - UiSink.Capacity);
                _logBox.SelectedText = string.Empty;
            }

            if (atEnd)
            {
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.SelectionLength = 0;
                _logBox.ScrollToCaret();
            }
        }

        private static Color ColourOf(LogLevel level) => level switch
        {
            LogLevel.Warn => WarnColour,
            LogLevel.Error => ErrorColour,
            _ => SystemColors.WindowText
        };

        private void ClearLog() => _logBox.Clear();

        private void CopyLog()
        {
            if (_logBox.TextLength == 0) return;

            try
            {
                Clipboard.SetText(_logBox.Text);
            }
            catch (ExternalException ex)
            {
                // Another process can hold the clipboard open; that is not our problem to solve, only to report.
                Log.Warn("The log could not be copied to the clipboard: " + ex.Message);
            }
        }

        // ----------------------------------------------------------- window, keys

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled) return;

            // The mapping grid takes typing: a bare R or H in it is data, not a command.
            bool typing = FocusedControl() is DataGridView or TextBoxBase { ReadOnly: false };

            switch (e.KeyCode)
            {
                case Keys.F12:
                    Recalibrate();
                    e.Handled = true;
                    break;
                case Keys.R when !typing:
                    ReloadMappings();
                    e.Handled = true;
                    break;
                case Keys.H when !typing:
                    ToggleMode();
                    e.Handled = true;
                    break;
                case Keys.Escape when !typing:
                    HideToTray();
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }

        /// <summary>The control that really has focus. With a TabControl in the way, the form's own
        /// ActiveControl is the tab page, never the grid inside it.</summary>
        private Control FocusedControl()
        {
            Control control = ActiveControl;
            while (control is IContainerControl container && container.ActiveControl != null)
                control = container.ActiveControl;
            return control;
        }

        /// <summary>
        /// The two Test tabs are the only thing in the window that costs the workers
        /// anything, so frames are built exactly while one of them is the selected tab on
        /// a visible window, and only that one ticks. Both halves are flipped together:
        /// the engine stops building frames and the panels stop asking for them. Called
        /// from the tab selection changing and from
        /// <see cref="OnResize"/> — hiding to the tray is covered too, since
        /// <see cref="Hide"/> raises <see cref="OnVisibleChanged"/>. Idempotent, so
        /// calling it from several places costs nothing.
        /// </summary>
        private void UpdateWatch()
        {
            // OnResize fires from the constructor's ClientSize assignment before the tabs exist.
            if (_tabs == null || _testInput == null || _testOutput == null) return;

            // Visible stays true while minimised, so WindowState is checked too.
            var tab = _tabs.SelectedTab;
            bool input = tab == _testInputPage;
            bool output = tab == _testOutputPage;
            bool on = !_shutDown && Visible && WindowState != FormWindowState.Minimized && (input || output);

            // WatchFrames and LatestFrame change no engine state, so this needs no busy guard and is safe
            // while a calibration dialog is up. Moving between the two Test tabs leaves it alone: the engine
            // is already publishing.
            bool changed = on != _watching;
            if (changed)
            {
                _watching = on;
                if (on) _app.WatchFrames(true);
            }

            // Only the selected panel ticks, and both are told either way: the one being left has to stop.
            // Both calls ignore an argument that changes nothing.
            _testInput.SetActive(on && input);
            _testOutput.SetActive(on && output);

            if (changed && !on) _app.WatchFrames(false);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            UpdateWatch();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateWatch();
        }

        /// <summary>Brings the window up from the tray or from minimised. Internal because a second start's
        /// handover (<see cref="Hosting.SingleInstance"/>) lands here.</summary>
        internal void ShowWindow()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ToggleWindow()
        {
            if (Visible) Hide();
            else ShowWindow();
        }

        private void HideToTray()
        {
            Hide();

            if (_balloonShown) return;
            _balloonShown = true;
            _tray.ShowBalloon("GUNCON3", "GUNCON3 is still running in the tray.");
        }

        private void ShowAbout() => RunModal(ShowAboutCore);

        private void ShowAboutCore()
            => MessageBox.Show(this,
                $"GUNCON3 {AppInfo.Version}{Environment.NewLine}{Environment.NewLine}"
                + $"EXE:  {AppInfo.ExePath}{Environment.NewLine}"
                + $"BASE: {AppInfo.BaseDirectory}",
                "About GUNCON3", MessageBoxButtons.OK, MessageBoxIcon.Information);

        /// <summary>Opens the settings dialog and applies what comes back. The dialog has already dealt with
        /// the registry; what is left is the file and the log sink.</summary>
        private void OpenSettings() => RunModal(OpenSettingsCore);

        private void OpenSettingsCore()
        {
            using var dialog = new SettingsForm(_settings);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            var wanted = dialog.Result;
            if (wanted == _settings) return;    // record equality: nothing was changed

            ApplyLogToFile(wanted.LogToFile);

            // What is stored is what actually happened: a log file that would not open leaves the option off.
            _settings = wanted with { LogToFile = _fileSink != null };
            SettingsStore.Save(_settings);
        }

        /// <summary>Turns guncon3.log on or off without a restart. A file that will not open is a warning and
        /// the option stays off; it is never a reason to fail anything.</summary>
        private void ApplyLogToFile(bool enabled)
        {
            if (enabled == (_fileSink != null)) return;

            if (!enabled)
            {
                Log.RemoveSink(_fileSink);
                _fileSink.Dispose();
                _fileSink = null;
                return;
            }

            var sink = FileSink.TryOpen(out string error);
            if (sink == null)
            {
                Log.Warn($"[Log] {FileSink.FilePath} could not be opened: {error}. Writing the log to a file stays off.");
                return;
            }

            Log.AddSink(sink);
            _fileSink = sink;
            Log.Line($"[Log] also writing to {FileSink.FilePath}.");
        }

        /// <summary>Forces the window handle into existence without showing anything, so the marshalling in
        /// <see cref="OnStatusChanged"/> works in a start-minimised session.</summary>
        public void EnsureHandleCreated()
        {
            if (!IsHandleCreated) _ = Handle;
        }

        // ------------------------------------------------------------ lifecycle

        /// <summary>Hooks SystemEvents.SessionEnding only once the window handle exists. Hooking it in the
        /// constructor instead could let a logoff land before any handle exists and fall through to RequestExit
        /// on the SystemEvents thread, which App's main-thread-only shutdown contract forbids.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (_sessionEndingHooked) return;
            _sessionEndingHooked = true;
            SystemEvents.SessionEnding += _onSessionEnding;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // The × hides to the tray. An exit the user asked for, and Windows going down, close for real.
            if (e.CloseReason == CloseReason.UserClosing && !_exiting)
            {
                e.Cancel = true;
                base.OnFormClosing(e);
                HideToTray();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            ShutDownAndExit();
        }

        /// <summary>File → Exit, the tray's Exit, and Windows logging off.</summary>
        private void RequestExit()
        {
            // A calibration runs a modal dialog inside Run(); shutting the engine down from underneath it would
            // dispose a device its read thread is still using.
            if (_busy)
            {
                _exitRequested = true;
                return;
            }

            _exiting = true;
            Close();

            // A window that was never shown does not necessarily raise FormClosed.
            ShutDownAndExit();
        }

        /// <summary>Idempotent: stop listening, stop the engine, take the tray icon down before disposing it,
        /// end the message loop.</summary>
        private void ShutDownAndExit()
        {
            if (_shutDown) return;
            _shutDown = true;

            _statusTimer.Stop();
            _logTimer.Stop();
            _testInput.SetActive(false);
            _testOutput.SetActive(false);
            _watching = false;
            _app.WatchFrames(false);
            _app.StatusChanged -= OnStatusChanged;
            _log.Appended -= _onLogAppended;
            SystemEvents.SessionEnding -= _onSessionEnding;

            // Shutdown's own warnings must still reach the file sink, so it runs before the sink comes down.
            // RemoveSink takes the same gate Write holds, so once it returns no worker can be inside the sink's
            // Write.
            _app.Shutdown();

            if (_fileSink != null)
            {
                Log.RemoveSink(_fileSink);
                _fileSink.Dispose();
                _fileSink = null;
            }

            _tray.Dispose();

            Application.Exit();
        }

        /// <summary>Windows is logging off or shutting down. SystemEvents raises this on its own thread, so the
        /// work is marshalled — synchronously, because the process may not outlive the return.</summary>
        private void OnSessionEnding()
        {
            if (_shutDown) return;

            // Subscribed only after OnHandleCreated, so this should always be true; the guard stays because a
            // handler already queued cannot be un-invoked.
            if (!IsHandleCreated) return;

            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(RequestExit));
                }
                catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
                {
                    // The window went away underneath us; the engine still has to stop —
                    // a held virtual button must not survive the logoff.
                    _app.Shutdown();
                }
                return;
            }

            RequestExit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Normally ShutDownAndExit has already run; a form disposed without ever being closed (an
                // exception out of GuiHost) has not.
                ShutDownAndExit();
                _statusTimer.Dispose();
                _logTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
