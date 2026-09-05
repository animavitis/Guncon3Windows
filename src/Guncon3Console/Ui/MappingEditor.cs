// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security;
using System.Windows.Forms;
using Guncon3.Core;

namespace Guncon3Console.Ui
{
    /// <summary>
    /// The Mapping tab: one mapping file at a time, one row per gun button, a keyboard
    /// code and a mouse button per row. Saving writes the file in the format
    /// <see cref="MappingFile"/> parses — through <see cref="MappingWriter"/>, so the two
    /// can never drift. The editor knows nothing about the engine on purpose: the host
    /// owns the rule that only one engine call runs at a time.
    /// </summary>
    internal sealed class MappingEditor : UserControl
    {
        private const string NotMapped = "—";
        private const int NoValue = -1;

        /// <summary>One entry of a drop-down: what it shows and what it means.</summary>
        private sealed class Choice
        {
            public string Label { get; init; }

            public int Value { get; init; }
        }

        private readonly ToolStripComboBox _files;
        private readonly DataGridView _grid;
        private readonly Label _banner;
        private readonly List<string> _paths = new List<string>();
        private readonly List<int> _indices = new List<int>();

        private IReadOnlyList<string> _header = Array.Empty<string>();
        private IReadOnlyList<string> _diagnostics = Array.Empty<string>();
        private string _loadError;
        private bool _loadFailed;
        private bool _dropWarned;
        private bool _loading;

        /// <summary>Raised after a file was written, so the host can reload the mappings.</summary>
        public event Action Saved;

        /// <summary>Runs a modal dialog under the host's busy state.</summary>
        private readonly Action<Action> _modal;

        private void RunModal(Action show) => _modal(show);

        internal MappingEditor(Action<Action> modal)
        {
            _modal = modal ?? throw new ArgumentNullException(nameof(modal));

            _files = new ToolStripComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                AutoSize = false,
                Width = 240
            };
            _files.SelectedIndexChanged += (_, __) => LoadSelected();

            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            tools.Items.Add(new ToolStripLabel("File:"));
            tools.Items.Add(_files);
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(UiFactory.Button("Save", Save));
            tools.Items.Add(UiFactory.Button("Revert", Revert));
            tools.Items.Add(UiFactory.Button("Open in editor", OpenInEditor));

            _banner = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                BackColor = Color.FromArgb(255, 250, 205),
                Padding = new Padding(8, 4, 8, 4),
                Visible = false
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EditMode = DataGridViewEditMode.EditOnEnter
            };

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Button",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                FillWeight = 30
            });
            _grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                HeaderText = "Keyboard",
                DataSource = KeyChoices(),
                DisplayMember = nameof(Choice.Label),
                ValueMember = nameof(Choice.Value),
                SortMode = DataGridViewColumnSortMode.NotSortable,
                FlatStyle = FlatStyle.Flat,
                FillWeight = 45
            });
            _grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                HeaderText = "Mouse",
                DataSource = MouseChoices(),
                DisplayMember = nameof(Choice.Label),
                ValueMember = nameof(Choice.Value),
                SortMode = DataGridViewColumnSortMode.NotSortable,
                FlatStyle = FlatStyle.Flat,
                FillWeight = 25
            });

            foreach (GunButton button in Enum.GetValues<GunButton>())
            {
                int index = _grid.Rows.Add();
                var row = _grid.Rows[index];
                row.Cells[0].Value = button.ToString();
                row.Cells[1].Value = NoValue;
                row.Cells[2].Value = NoValue;
                row.Tag = button;
            }

            // A combo-box cell otherwise commits only when it loses focus, so Save could
            // miss the change the user just made.
            _grid.CurrentCellDirtyStateChanged += (_, __) =>
            {
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += (_, __) =>
            {
                if (!_loading) UpdateBanner();
            };
            // A cell value outside its list would otherwise raise a dialog the user can
            // do nothing about; the load path only ever writes values from the list.
            _grid.DataError += (_, e) => e.ThrowException = false;

            Controls.Add(_grid);
            Controls.Add(_banner);
            Controls.Add(tools);
        }

        /// <summary>
        /// Offers one file per gun, named the way <see cref="App.ReloadMappings"/> names
        /// them, built from each <see cref="GunStatus.Index"/> rather than the list's
        /// position, because a gun that failed to connect leaves a gap: a single gun at
        /// index 1 must offer mapping_2.txt, not mapping.txt. Always offers mapping.txt,
        /// even with no gun connected.
        /// </summary>
        public void SetFiles(IReadOnlyList<GunStatus> statuses)
        {
            ArgumentNullException.ThrowIfNull(statuses);

            var indices = new List<int>();
            if (statuses.Count == 0)
                indices.Add(0);
            else
                foreach (var status in statuses)
                    indices.Add(status.Index);

            // Rebuilding on every call would drop an in-progress edit every time the 1 s
            // status timer ticks; only the actual set of guns changing warrants it.
            if (SameIndices(indices)) return;

            _indices.Clear();
            _indices.AddRange(indices);

            _paths.Clear();
            _files.Items.Clear();

            foreach (int index in indices)
            {
                string suffix = index > 0 ? $"_{index + 1}" : string.Empty;
                _paths.Add(Path.Combine(AppInfo.BaseDirectory, $"mapping{suffix}.txt"));
                _files.Items.Add(index == 0
                    ? "mapping.txt (default)"
                    : string.Create(CultureInfo.InvariantCulture, $"mapping{suffix}.txt (Gun {index + 1})"));
            }

            // Assigning 0 when it is already 0 raises no event, so the load is explicit.
            _files.SelectedIndex = 0;
            LoadSelected();
        }

        private bool SameIndices(List<int> indices)
        {
            if (_indices.Count != indices.Count) return false;

            for (int i = 0; i < indices.Count; i++)
                if (_indices[i] != indices[i]) return false;

            return true;
        }

        // ------------------------------------------------------------ load, save

        private void LoadSelected()
        {
            if (_files.SelectedIndex < 0 || _files.SelectedIndex >= _paths.Count) return;

            string path = _paths[_files.SelectedIndex];

            string[] lines;
            try
            {
                lines = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Left half-applied, this would let Save write the previous file's grid,
                // under the previous file's header, to the newly-selected path. Instead
                // the selection starts from a known-empty, known-unsaveable state, and
                // the error stays visible in the banner until a Revert succeeds.
                _loadError = $"{Path.GetFileName(path)} could not be read: {ex.Message}";
                _loadFailed = true;
                _header = Array.Empty<string>();
                _diagnostics = Array.Empty<string>();
                _dropWarned = false;

                _loading = true;
                try
                {
                    foreach (DataGridViewRow row in _grid.Rows)
                    {
                        row.Cells[1].Value = NoValue;
                        row.Cells[2].Value = NoValue;
                    }
                }
                finally
                {
                    _loading = false;
                }

                UpdateBanner();
                return;
            }

            var mapping = MappingFile.Parse(lines);
            _header = MappingWriter.HeaderOf(lines);
            _diagnostics = mapping.Diagnostics;
            _loadError = null;
            _loadFailed = false;
            _dropWarned = false;

            _loading = true;
            try
            {
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    var button = (GunButton)row.Tag;
                    row.Cells[1].Value = mapping.Keyboard.TryGetValue(button, out byte code) ? (int)code : NoValue;
                    row.Cells[2].Value = mapping.Mouse.TryGetValue(button, out var mouseButton) ? (int)mouseButton : NoValue;
                }
            }
            finally
            {
                _loading = false;
            }

            UpdateBanner();
        }

        private void Revert() => LoadSelected();

        private void Save()
        {
            if (_files.SelectedIndex < 0 || _files.SelectedIndex >= _paths.Count) return;

            if (_loadFailed)
            {
                RunModal(() => MessageBox.Show(this, "The file could not be read; fix that and press Revert before saving.",
                                "GUNCON3", MessageBoxButtons.OK, MessageBoxIcon.Error));
                return;
            }

            if (_diagnostics.Count > 0 && !_dropWarned)
            {
                DialogResult answer = DialogResult.None;
                RunModal(() => answer = MessageBox.Show(this,
                    string.Create(CultureInfo.InvariantCulture,
                        $"{_diagnostics.Count} line(s) in this file could not be understood and will be dropped. Save anyway?"),
                    "GUNCON3", MessageBoxButtons.YesNo, MessageBoxIcon.Warning));

                if (answer != DialogResult.Yes) return;
                if (IsDisposed) return;
                _dropWarned = true;
            }

            string path = _paths[_files.SelectedIndex];
            var keyboard = new Dictionary<GunButton, byte>();
            var mouse = new Dictionary<GunButton, MouseButton>();

            foreach (DataGridViewRow row in _grid.Rows)
            {
                var button = (GunButton)row.Tag;

                int code = ValueOf(row.Cells[1]);
                if (code != NoValue) keyboard[button] = (byte)code;

                int mouseButton = ValueOf(row.Cells[2]);
                if (mouseButton != NoValue) mouse[button] = (MouseButton)mouseButton;
            }

            try
            {
                AtomicFile.WriteLines(path, MappingWriter.Write(_header, keyboard, mouse));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                RunModal(() => MessageBox.Show(this, $"{Path.GetFileName(path)} could not be saved: {ex.Message}",
                                "GUNCON3", MessageBoxButtons.OK, MessageBoxIcon.Error));
                return;
            }

            // What was just written parses cleanly by construction, so there is nothing
            // left to drop and nothing left to warn about.
            _diagnostics = Array.Empty<string>();
            _dropWarned = false;
            UpdateBanner();

            Log.Line($"[Mapping] {Path.GetFileName(path)} saved.");
            Saved?.Invoke();
        }

        private void OpenInEditor()
        {
            if (_files.SelectedIndex < 0 || _files.SelectedIndex >= _paths.Count) return;

            string path = _paths[_files.SelectedIndex];
            try
            {
                using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or SecurityException or InvalidOperationException)
            {
                RunModal(() => MessageBox.Show(this, $"{Path.GetFileName(path)} could not be opened: {ex.Message}",
                                "GUNCON3", MessageBoxButtons.OK, MessageBoxIcon.Warning));
            }
        }

        // --------------------------------------------------------------- banner

        /// <summary>
        /// The yellow strip above the grid: lines this file's parse could not
        /// understand, and any keycode used by more than one button.
        /// </summary>
        private void UpdateBanner()
        {
            var notes = new List<string>();

            // Kept in a field rather than painted straight onto the label: the very next
            // CellValueChanged (this method again, from the grid reset above) would
            // otherwise overwrite it before the user ever saw it.
            if (_loadError != null)
                notes.Add(_loadError);

            if (_diagnostics.Count > 0)
            {
                notes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{_diagnostics.Count} line(s) could not be understood and will be dropped when you save:"));
                foreach (var diagnostic in _diagnostics)
                    notes.Add("    " + diagnostic);
            }

            notes.AddRange(SharedKeycodeNotes());

            if (notes.Count == 0)
            {
                _banner.Visible = false;
                _banner.Text = string.Empty;
                return;
            }

            ShowBanner(string.Join(Environment.NewLine, notes));
        }

        /// <summary>One note per keycode that more than one gun button is mapped to.</summary>
        private List<string> SharedKeycodeNotes()
        {
            var buttonsByCode = new Dictionary<int, List<string>>();

            foreach (DataGridViewRow row in _grid.Rows)
            {
                int code = ValueOf(row.Cells[1]);
                if (code == NoValue) continue;

                if (!buttonsByCode.TryGetValue(code, out var buttons))
                    buttonsByCode[code] = buttons = new List<string>();

                buttons.Add(row.Cells[0].Value as string);
            }

            var notes = new List<string>();
            foreach (var pair in buttonsByCode)
            {
                if (pair.Value.Count < 2) continue;

                notes.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Keycode {pair.Key} ({KeyCodeTable.NameOf((byte)pair.Key)}) is used by {string.Join(", ", pair.Value)}. That is allowed."));
            }

            return notes;
        }

        private void ShowBanner(string text)
        {
            _banner.Text = text;
            // The label does not wrap, so its height has to follow the line count.
            _banner.Height = 10 + text.Split('\n').Length * _banner.Font.Height;
            _banner.Visible = true;
        }

        // ---------------------------------------------------------------- pieces

        private static int ValueOf(DataGridViewCell cell) => cell.Value is int value ? value : NoValue;

        private static List<Choice> KeyChoices()
        {
            var choices = new List<Choice> { new Choice { Label = NotMapped, Value = NoValue } };

            foreach (var (code, name) in KeyCodeTable.Entries)
                choices.Add(new Choice
                {
                    Label = string.Create(CultureInfo.InvariantCulture, $"{code,3}  {name}"),
                    Value = code
                });

            return choices;
        }

        private static List<Choice> MouseChoices()
        {
            var choices = new List<Choice> { new Choice { Label = NotMapped, Value = NoValue } };

            foreach (MouseButton button in Enum.GetValues<MouseButton>())
                choices.Add(new Choice { Label = button.ToString(), Value = (int)button });

            return choices;
        }
    }
}
