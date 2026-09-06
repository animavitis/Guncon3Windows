// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using Guncon3.Core;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace Guncon3Console.Ui
{
    /// <summary>
    /// The Test tab: what one gun is doing right now. Left, the aim as three crosshairs
    /// on a picture of the calibrated screen; middle, every button and both sticks;
    /// right, what the three virtual outputs — cursor, keys, pad — were last told.
    /// Passive by construction — the panel holds no engine reference, only one
    /// <see cref="GunFrame"/> and one status list through two delegates the host
    /// supplies — and it asks for nothing while <see cref="SetActive"/> was last called with false.
    /// </summary>
    internal sealed class TestPanel : UserControl
    {
        // ------------------------------------------------------------- refresh

        /// <summary>About 30 Hz: fast enough to follow the gun, slow enough to be free.</summary>
        private const int RefreshMs = 33;

        /// <summary>A frame older than this is labelled "stale" (a paused worker, or a gun that stopped
        /// answering).</summary>
        private const int StaleAfterMs = 500;

        /// <summary>Ticks between two <see cref="_statusSource"/> reads: the connected flag does not need 30
        /// Hz.</summary>
        private const int StatusEveryTicks = 15;

        // ----------------------------------------------------------- aim picture

        private const int AspectW = 4;
        private const int AspectH = 3;
        private const int BoxMarginPx = 8;
        private const float BorderWidth = 1f;
        private const float OffScreenBorderWidth = 3f;
        private const int CrosshairArmPx = 8;
        private const int ActiveCrosshairArmPx = 13;
        private const float CrosshairWidth = 1f;
        private const float ActiveCrosshairWidth = 2.5f;
        private const int BoxTextPadPx = 6;
        private const int AimTextLines = 4;

        // -------------------------------------------------------- sticks, bars

        private const int PadSizePx = 116;
        private const int PadGapPx = 14;
        private const int PadTopPx = 16;
        private const int PointRadiusPx = 4;
        private const int ArrowLengthPx = 38;
        private const float ArrowWidth = 2f;
        private const int BarHeightPx = 16;
        private const int BarGapPx = 5;
        private const int BarLabelPx = 30;
        private const int BarValuePx = 56;
        private const int BarTextGapPx = 4;
        private const int RawAxisMax = 255;

        // ---------------------------------------------------------------- tiles

        private const int TileColumns = 6;
        private const int TileRows = 3;
        private const int TileWidthPx = 62;
        private const int TileHeightPx = 22;
        private const int TileMarginPx = 2;

        /// <summary>The nine physical buttons the joystick feeder reports, in <see cref="GunButton"/> order —
        /// exactly JoystickFeeder's own PhysicalButtons order, i.e. JoystickReportState's bit order.</summary>
        private const int JoystickButtonCount = 9;

        // ----------------------------------------------------------------- text

        private const string NoFrames = "no frames yet";
        private const string NoCalibration = "no calibration";
        private const string NoHomography = "no homography";
        private const string StaleText = "stale";
        private const string DisconnectedText = "disconnected";
        private const string HealthyText = "OK";
        private const string FailedText = "failed";
        private const string NoKeys = "none";
        private const string Missing = "—";
        private const string MouseGroupName = "Mouse";
        private const string KeyboardGroupName = "Keyboard";
        private const string JoystickGroupName = "Joystick";
        private const string LeftStickCaption = "Left stick";
        private const string RightStickCaption = "Right stick";
        private const string DepthCaption = "Z";
        private const string PairFormat = "0.000";
        private const string PercentFormat = "0.0";

        // -------------------------------------------------------------- colours

        private static readonly Color AimBackColour = Color.FromArgb(24, 24, 28);
        private static readonly Color BorderColour = Color.FromArgb(120, 120, 130);
        private static readonly Color OffScreenColour = Color.FromArgb(205, 45, 45);
        private static readonly Color RawColour = Color.FromArgb(130, 130, 130);
        private static readonly Color RectColour = Color.White;
        private static readonly Color HomographyColour = Color.FromArgb(0, 205, 225);
        private static readonly Color DimColour = Color.FromArgb(80, 80, 86);
        private static readonly Color TileLitColour = Color.FromArgb(120, 205, 130);
        private static readonly Color TileIdleColour = Color.FromArgb(238, 238, 238);
        private static readonly Color BarBackColour = Color.FromArgb(228, 228, 232);
        private static readonly Color BarFillColour = Color.FromArgb(90, 140, 200);
        private static readonly Color PadBorderColour = Color.FromArgb(150, 150, 158);
        private static readonly Color HealthyColour = Color.FromArgb(20, 110, 40);
        private static readonly Color FailedColour = Color.Firebrick;

        // ------------------------------------------------------------- controls

        private readonly ToolStrip _tools;
        private readonly ToolStripComboBox _guns;
        private readonly DrawPanel _aim;
        private readonly Label _aimNumbers;
        private readonly TableLayoutPanel _tileGrid;
        private readonly Label[] _tiles = new Label[GunButtons.Count];
        private readonly DrawPanel _sticks;
        private readonly GroupBox _mouseGroup;
        private readonly Label _mouseValues;
        private readonly Label[] _mouseTiles = new Label[3];
        private readonly GroupBox _keyboardGroup;
        private readonly Label _keyboardKeys;
        private readonly GroupBox _joystickGroup;
        private readonly DrawPanel _joyBars;
        private readonly Label[] _joyTiles = new Label[JoystickButtonCount];
        private readonly WinFormsTimer _timer;

        // ------------------------------------------------------ GDI+ resources

        private readonly Pen _borderPen = new Pen(BorderColour, BorderWidth);
        private readonly Pen _offScreenPen = new Pen(OffScreenColour, OffScreenBorderWidth);
        private readonly Pen _rawPen = new Pen(RawColour, CrosshairWidth);
        private readonly Pen _rectPen = new Pen(RectColour, CrosshairWidth);
        private readonly Pen _rectActivePen = new Pen(RectColour, ActiveCrosshairWidth);
        private readonly Pen _homographyPen = new Pen(HomographyColour, CrosshairWidth);
        private readonly Pen _homographyActivePen = new Pen(HomographyColour, ActiveCrosshairWidth);
        private readonly Pen _dimPen = new Pen(DimColour, CrosshairWidth);
        private readonly Pen _dimActivePen = new Pen(DimColour, ActiveCrosshairWidth);
        private readonly Pen _padBorderPen = new Pen(PadBorderColour, BorderWidth);
        private readonly Pen _arrowPen = new Pen(BarFillColour, ArrowWidth) { EndCap = LineCap.ArrowAnchor };
        private readonly SolidBrush _aimBackBrush = new SolidBrush(AimBackColour);
        private readonly SolidBrush _barBackBrush = new SolidBrush(BarBackColour);
        private readonly SolidBrush _barFillBrush = new SolidBrush(BarFillColour);
        private readonly SolidBrush _pointBrush = new SolidBrush(RectColour);
        private readonly Font _mono = new Font(FontFamily.GenericMonospace, 9f);
        private readonly Font _small = new Font(FontFamily.GenericSansSerif, 8f);

        // ----------------------------------------------------------------- state

        private readonly List<int> _indices = new List<int>();
        private GunFrame _frame;
        private bool _stale;
        private bool _connected;
        private bool _connectedNow;
        private int _statusCountdown;
        private int _selected;

        /// <summary>Names of the drawing panels that have already logged their one paint failure.</summary>
        private readonly HashSet<string> _paintWarned = new HashSet<string>();

        /// <summary>The newest frame for a slot position, or null when there is none. The host passes
        /// <c>App.LatestFrame</c>; called on the UI thread only.</summary>
        private readonly Func<int, GunFrame> _frameSource;

        /// <summary>Every gun's status. The host passes <c>App.Status</c>; read twice a second rather than
        /// every tick, and only for the connected flag.</summary>
        private readonly Func<IReadOnlyList<GunStatus>> _statusSource;

        internal TestPanel(Func<int, GunFrame> frameSource, Func<IReadOnlyList<GunStatus>> statusSource)
        {
            _frameSource = frameSource ?? throw new ArgumentNullException(nameof(frameSource));
            _statusSource = statusSource ?? throw new ArgumentNullException(nameof(statusSource));

            _guns = new ToolStripComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                AutoSize = false,
                Width = 120
            };
            _guns.SelectedIndexChanged += (_, __) => SelectGun();

            _tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Visible = false };
            _tools.Items.Add(new ToolStripLabel("Gun:"));
            _tools.Items.Add(_guns);

            _aim = new DrawPanel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
            _aim.Paint += (_, e) => SafePaint(e.Graphics, PaintAim, "aim picture");

            _aimNumbers = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Font = _mono,
                Padding = new Padding(BoxTextPadPx, 2, BoxTextPadPx, 2)
            };
            _aimNumbers.Height = AimTextLines * _mono.Height + 2 * BoxTextPadPx;

            var aimHost = new Panel { Dock = DockStyle.Fill };
            aimHost.Controls.Add(_aim);
            aimHost.Controls.Add(_aimNumbers);

            _tileGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = TileColumns,
                RowCount = TileRows,
                Height = TileRows * (TileHeightPx + 2 * TileMarginPx) + 2
            };
            for (int i = 0; i < TileColumns; i++)
                _tileGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / TileColumns));
            for (int i = 0; i < TileRows; i++)
                _tileGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, TileHeightPx + 2 * TileMarginPx));

            int cell = 0;
            foreach (GunButton button in Enum.GetValues<GunButton>())
            {
                var tile = Tile(button.ToString());
                tile.Dock = DockStyle.Fill;
                _tiles[cell] = tile;
                _tileGrid.Controls.Add(tile, cell % TileColumns, cell / TileColumns);
                cell++;
            }

            _sticks = new DrawPanel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
            _sticks.Paint += (_, e) => SafePaint(e.Graphics, PaintSticks, "stick pads");

            var gunHost = new Panel { Dock = DockStyle.Fill };
            gunHost.Controls.Add(_sticks);
            gunHost.Controls.Add(_tileGrid);

            _mouseValues = new Label { Dock = DockStyle.Top, AutoSize = false, Height = TileHeightPx, Font = _mono };
            var mouseTiles = new FlowLayoutPanel { Dock = DockStyle.Top, Height = TileHeightPx + 2 * TileMarginPx };
            string[] mouseNames = { "L", "R", "M" };
            for (int i = 0; i < _mouseTiles.Length; i++)
            {
                _mouseTiles[i] = Tile(mouseNames[i]);
                _mouseTiles[i].Width = TileWidthPx / 2;
                mouseTiles.Controls.Add(_mouseTiles[i]);
            }

            _mouseGroup = new GroupBox { Dock = DockStyle.Fill, Text = MouseGroupName };
            _mouseGroup.Controls.Add(mouseTiles);
            _mouseGroup.Controls.Add(_mouseValues);

            _keyboardKeys = new Label { Dock = DockStyle.Fill, AutoSize = false, Font = _mono };
            _keyboardGroup = new GroupBox { Dock = DockStyle.Fill, Text = KeyboardGroupName };
            _keyboardGroup.Controls.Add(_keyboardKeys);

            _joyBars = new DrawPanel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
            _joyBars.Paint += (_, e) => SafePaint(e.Graphics, PaintJoystickBars, "joystick bars");

            var joyTiles = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 2 * (TileHeightPx + 2 * TileMarginPx) + 2
            };
            for (int i = 0; i < _joyTiles.Length; i++)
            {
                _joyTiles[i] = Tile(((GunButton)i).ToString());
                joyTiles.Controls.Add(_joyTiles[i]);
            }

            _joystickGroup = new GroupBox { Dock = DockStyle.Fill, Text = JoystickGroupName };
            _joystickGroup.Controls.Add(_joyBars);
            _joystickGroup.Controls.Add(joyTiles);

            var outputs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            outputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            outputs.RowStyles.Add(new RowStyle(SizeType.Percent, 26f));
            outputs.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
            outputs.RowStyles.Add(new RowStyle(SizeType.Percent, 44f));
            outputs.Controls.Add(_mouseGroup, 0, 0);
            outputs.Controls.Add(_keyboardGroup, 0, 1);
            outputs.Controls.Add(_joystickGroup, 0, 2);

            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            columns.Controls.Add(aimHost, 0, 0);
            columns.Controls.Add(gunHost, 1, 0);
            columns.Controls.Add(outputs, 2, 0);

            // Docking is applied from the last added control backwards, so the filling control goes in first
            // and the tool strip last.
            Controls.Add(columns);
            Controls.Add(_tools);

            _timer = new WinFormsTimer { Interval = RefreshMs };
            _timer.Tick += (_, __) => Tick();

            Render();
        }

        /// <summary>Starts or stops the refresh timer. The host calls this with true while this tab is the
        /// selected one on a visible window; it separately tells the engine whether to publish frames at all.</summary>
        internal void SetActive(bool active)
        {
            if (active == _timer.Enabled) return;

            if (active)
            {
                _statusCountdown = 0;
                _timer.Start();
                Tick();
            }
            else
            {
                _timer.Stop();
            }
        }

        /// <summary>Fills the gun selector, which is hidden with one gun. Must ignore a call that changes
        /// nothing: rebuilding the list would reset the selection every second.</summary>
        public void SetGuns(IReadOnlyList<GunStatus> statuses)
        {
            ArgumentNullException.ThrowIfNull(statuses);

            var indices = new List<int>(statuses.Count);
            foreach (var status in statuses)
                indices.Add(status.Index);

            if (SameIndices(indices)) return;

            _indices.Clear();
            _indices.AddRange(indices);

            _guns.Items.Clear();
            foreach (int index in indices)
                _guns.Items.Add(string.Create(CultureInfo.InvariantCulture, $"Gun {index + 1}"));

            _tools.Visible = indices.Count > 1;

            _selected = 0;
            if (indices.Count > 0) _guns.SelectedIndex = 0;

            _frame = null;
            Render();
        }

        private bool SameIndices(List<int> indices)
        {
            if (_indices.Count != indices.Count) return false;

            for (int i = 0; i < indices.Count; i++)
                if (_indices[i] != indices[i]) return false;

            return true;
        }

        private void SelectGun()
        {
            _selected = _guns.SelectedIndex < 0 ? 0 : _guns.SelectedIndex;
            _frame = null;
            _statusCountdown = 0;
            Render();
        }

        // ---------------------------------------------------------------- ticking

        /// <summary>One refresh. Nothing is redrawn unless the frame reference, the stale flag or the connected
        /// flag actually changed.</summary>
        private void Tick()
        {
            if (IsDisposed) return;

            var frame = _frameSource(_selected);
            bool stale = frame != null && IsStale(frame.Timestamp);

            if (--_statusCountdown <= 0)
            {
                _statusCountdown = StatusEveryTicks;
                _connectedNow = ConnectedNow();
            }

            if (ReferenceEquals(frame, _frame) && stale == _stale && _connectedNow == _connected)
                return;

            _frame = frame;
            _stale = stale;
            _connected = _connectedNow;
            Render();
        }

        private static bool IsStale(long timestamp)
            => Stopwatch.GetTimestamp() - timestamp > Stopwatch.Frequency * StaleAfterMs / 1000;

        private bool ConnectedNow()
        {
            var statuses = _statusSource();
            if (statuses == null || _selected < 0 || _selected >= statuses.Count)
                return false;

            return statuses[_selected].IsConnected;
        }

        // --------------------------------------------------------------- rendering

        private void Render()
        {
            var frame = _frame;

            _aimNumbers.Text = AimText(frame);

            for (int i = 0; i < _tiles.Length; i++)
                _tiles[i].BackColor = Lit(frame != null && i < frame.Buttons.Length && frame.Buttons[i]);

            SetGroupHealth(_mouseGroup, MouseGroupName, frame?.MouseHealthy);
            SetGroupHealth(_keyboardGroup, KeyboardGroupName, frame?.KeyboardHealthy);
            SetGroupHealth(_joystickGroup, JoystickGroupName, frame?.JoystickHealthy);

            _mouseValues.Text = frame == null
                ? NoFrames
                : string.Create(CultureInfo.InvariantCulture,
                    $"X {frame.Mouse.X,5}  Y {frame.Mouse.Y,5}  position {(frame.Mouse.HasPosition ? "yes" : "no")}");

            for (int i = 0; i < _mouseTiles.Length; i++)
                _mouseTiles[i].BackColor = Lit(frame != null && (frame.Mouse.Buttons & (1 << i)) != 0);

            _keyboardKeys.Text = KeyText(frame);

            for (int i = 0; i < _joyTiles.Length; i++)
            {
                bool lit = frame != null && (i < 8
                    ? (frame.Joystick.Report.Buttons0 & (1 << i)) != 0
                    : (frame.Joystick.Report.Buttons1 & (1 << (i - 8))) != 0);
                _joyTiles[i].BackColor = Lit(lit);
            }

            _aim.Invalidate();
            _sticks.Invalidate();
            _joyBars.Invalidate();
        }

        private static Color Lit(bool on) => on ? TileLitColour : TileIdleColour;

        private static void SetGroupHealth(GroupBox group, string name, bool? healthy)
        {
            if (healthy == null)
            {
                group.Text = name;
                group.ForeColor = SystemColors.ControlText;
                return;
            }

            group.Text = name + " — " + (healthy.Value ? HealthyText : FailedText);
            group.ForeColor = healthy.Value ? HealthyColour : FailedColour;
        }

        private static string AimText(GunFrame frame)
        {
            if (frame == null) return NoFrames;

            bool homographyActive = frame.Mode == CalibrationMode.Homography && frame.Homography != null;

            var lines = new string[AimTextLines];
            lines[0] = "Raw   " + Pair(frame.RawNormalized);
            lines[1] = "Rect  " + (frame.Rect == null ? Missing : Pair(frame.Rect.Value))
                     + (frame.Rect != null && !homographyActive ? "   (active)" : string.Empty);
            lines[2] = "Hom   " + (frame.Homography == null ? Missing : Pair(frame.Homography.Value))
                     + (frame.Homography != null && homographyActive ? "   (active)" : string.Empty);
            lines[3] = Delta(frame);

            return string.Join(Environment.NewLine, lines);
        }

        private static string Pair((double X, double Y) p)
            => string.Create(CultureInfo.InvariantCulture,
                $"{p.X.ToString(PairFormat, CultureInfo.InvariantCulture)}, {p.Y.ToString(PairFormat, CultureInfo.InvariantCulture)}");

        /// <summary>How far apart the two mappings put the aim, as a percentage of the screen diagonal.</summary>
        private static string Delta(GunFrame frame)
        {
            if (frame.Rect == null || frame.Homography == null) return string.Empty;

            double dx = frame.Rect.Value.X - frame.Homography.Value.X;
            double dy = frame.Rect.Value.Y - frame.Homography.Value.Y;
            // Percent of the 4:3 screen diagonal, not the unit-square one: the two normalized axes do not cover
            // equal physical distances.
            double percent = Math.Sqrt(dx * dx * AspectW * AspectW + dy * dy * AspectH * AspectH)
                / Math.Sqrt((double)(AspectW * AspectW + AspectH * AspectH)) * 100.0;

            return string.Create(CultureInfo.InvariantCulture,
                $"apart {percent.ToString(PercentFormat, CultureInfo.InvariantCulture)}% of the diagonal");
        }

        private static string KeyText(GunFrame frame)
        {
            if (frame == null) return NoFrames;

            var keys = frame.Keyboard.Keys;
            if (keys == null || keys.Length == 0) return NoKeys;

            var lines = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                lines[i] = string.Create(CultureInfo.InvariantCulture,
                    $"{keys[i],3}  {KeyCodeTable.NameOf(keys[i]) ?? Missing}");

            return string.Join(Environment.NewLine, lines);
        }

        // ----------------------------------------------------------------- painting

        /// <summary>Runs one paint body. A drawing bug at 30 Hz must not take the window down or fill the log —
        /// each panel's first failure is reported and the rest are silent.</summary>
        private void SafePaint(Graphics g, Action<Graphics> paint, string what)
        {
            try
            {
                paint(g);
            }
            catch (Exception ex)
            {
                if (!_paintWarned.Add(what)) return;

                Log.Warn($"[Test] drawing the {what} failed: {ex.Message}. The tab keeps running.");
            }
        }

        private void PaintAim(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var box = BoxIn(_aim.ClientRectangle);
            if (box.Width <= 0 || box.Height <= 0) return;

            g.FillRectangle(_aimBackBrush, box);

            var frame = _frame;
            bool offScreen = frame != null && !frame.InsideScreen;
            g.DrawRectangle(offScreen ? _offScreenPen : _borderPen, box);

            if (frame == null)
            {
                g.DrawString(NoFrames, _small, Brushes.Silver, box.X + BoxTextPadPx, box.Y + BoxTextPadPx);
                return;
            }

            if (!frame.HasCalibration)
                g.DrawString(NoCalibration, _small, Brushes.Gold, box.X + BoxTextPadPx, box.Y + BoxTextPadPx);
            else if (frame.Homography == null)
                g.DrawString(NoHomography, _small, Brushes.Gold, box.X + BoxTextPadPx, box.Y + BoxTextPadPx);

            if (!offScreen)
            {
                bool homographyActive = frame.Mode == CalibrationMode.Homography && frame.Homography != null;

                // The active mode last and thicker, so it is the one the eye follows.
                Crosshair(g, box, frame.RawNormalized, _connected ? _rawPen : _dimPen, CrosshairArmPx);

                if (homographyActive)
                {
                    Crosshair(g, box, frame.Rect, _connected ? _rectPen : _dimPen, CrosshairArmPx);
                    Crosshair(g, box, frame.Homography, _connected ? _homographyActivePen : _dimActivePen, ActiveCrosshairArmPx);
                }
                else
                {
                    Crosshair(g, box, frame.Homography, _connected ? _homographyPen : _dimPen, CrosshairArmPx);
                    Crosshair(g, box, frame.Rect, _connected ? _rectActivePen : _dimActivePen, ActiveCrosshairArmPx);
                }
            }

            if (_stale)
                g.DrawString(StaleText, _small, Brushes.Gold,
                    box.Right - BoxTextPadPx - g.MeasureString(StaleText, _small).Width, box.Y + BoxTextPadPx);

            if (!_connected)
                g.DrawString(DisconnectedText, _small, Brushes.OrangeRed,
                    box.X + BoxTextPadPx, box.Bottom - BoxTextPadPx - _small.Height);
        }

        /// <summary>The largest 4:3 box that fits, centred: the picture is the calibrated screen, not the
        /// panel.</summary>
        private static Rectangle BoxIn(Rectangle client)
        {
            int w = client.Width - 2 * BoxMarginPx;
            int h = client.Height - 2 * BoxMarginPx;
            if (w <= 0 || h <= 0) return Rectangle.Empty;

            if (w * AspectH > h * AspectW) w = h * AspectW / AspectH;
            else h = w * AspectH / AspectW;

            return new Rectangle(client.X + (client.Width - w) / 2, client.Y + (client.Height - h) / 2, w, h);
        }

        private static void Crosshair(Graphics g, Rectangle box, (double X, double Y)? point, Pen pen, int arm)
        {
            if (point == null) return;

            float x = box.X + (float)(point.Value.X * box.Width);
            float y = box.Y + (float)(point.Value.Y * box.Height);

            g.DrawLine(pen, x - arm, y, x + arm, y);
            g.DrawLine(pen, x, y - arm, x, y + arm);
        }

        private void PaintSticks(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var frame = _frame;
            var left = new Rectangle(BoxMarginPx, PadTopPx, PadSizePx, PadSizePx);
            var right = new Rectangle(left.Right + PadGapPx, PadTopPx, PadSizePx, PadSizePx);

            Pad(g, left, LeftStickCaption, frame?.HatX, frame?.HatY,
                Pressed(frame, GunButton.LUp), Pressed(frame, GunButton.LDown),
                Pressed(frame, GunButton.LLeft), Pressed(frame, GunButton.LRight));

            Pad(g, right, RightStickCaption, frame?.RX, frame?.RY,
                Pressed(frame, GunButton.RUp), Pressed(frame, GunButton.RDown),
                Pressed(frame, GunButton.RLeft), Pressed(frame, GunButton.RRight));

            var bar = new Rectangle(BoxMarginPx, left.Bottom + PadGapPx,
                Math.Max(0, _sticks.ClientSize.Width - 2 * BoxMarginPx), BarHeightPx);

            Bar(g, bar, DepthCaption,
                frame == null ? 0 : frame.Z / (double)RawAxisMax,
                frame == null ? Missing : frame.Z.ToString(CultureInfo.InvariantCulture));
        }

        private static bool Pressed(GunFrame frame, GunButton button)
            => frame != null && (int)button < frame.Buttons.Length && frame.Buttons[(int)button];

        /// <summary>
        /// One stick: the raw point at axis/255 and, when the reader digitised a
        /// direction, an arrow from the centre. The direction comes from the frame's
        /// button flags — the gun's own digitisation, deadzone and measured centres
        /// included — so nothing here re-derives them.
        /// </summary>
        private void Pad(Graphics g, Rectangle box, string caption, int? axisX, int? axisY,
                         bool up, bool down, bool leftward, bool rightward)
        {
            g.DrawString(caption, _small, SystemBrushes.ControlText, box.X, box.Y - _small.Height);
            g.DrawRectangle(_padBorderPen, box);
            g.DrawLine(_padBorderPen, box.X, box.Y + box.Height / 2, box.Right, box.Y + box.Height / 2);
            g.DrawLine(_padBorderPen, box.X + box.Width / 2, box.Y, box.X + box.Width / 2, box.Bottom);

            if (axisX == null || axisY == null) return;

            float px = box.X + (float)(Clamp01(axisX.Value / (double)RawAxisMax) * box.Width);
            float py = box.Y + (float)(Clamp01(axisY.Value / (double)RawAxisMax) * box.Height);
            g.FillEllipse(_pointBrush, px - PointRadiusPx, py - PointRadiusPx, PointRadiusPx * 2, PointRadiusPx * 2);
            g.DrawEllipse(_padBorderPen, px - PointRadiusPx, py - PointRadiusPx, PointRadiusPx * 2, PointRadiusPx * 2);

            int dx = (rightward ? 1 : 0) - (leftward ? 1 : 0);
            int dy = (down ? 1 : 0) - (up ? 1 : 0);
            if (dx == 0 && dy == 0) return;

            float cx = box.X + box.Width / 2f;
            float cy = box.Y + box.Height / 2f;
            double length = Math.Sqrt(dx * dx + dy * dy);
            g.DrawLine(_arrowPen, cx, cy,
                cx + (float)(dx / length * ArrowLengthPx),
                cy + (float)(dy / length * ArrowLengthPx));
        }

        private void PaintJoystickBars(Graphics g)
        {
            var frame = _frame;
            var report = frame?.Joystick.Report ?? default;

            int width = Math.Max(0, _joyBars.ClientSize.Width - 2 * BoxMarginPx);
            int y = BarGapPx;

            AxisBar(g, width, ref y, "X", frame == null ? null : report.X);
            AxisBar(g, width, ref y, "Y", frame == null ? null : report.Y);
            AxisBar(g, width, ref y, "RX", frame == null ? null : report.RX);
            AxisBar(g, width, ref y, "RY", frame == null ? null : report.RY);
            AxisBar(g, width, ref y, "Z", frame == null ? null : report.Z);
        }

        private void AxisBar(Graphics g, int width, ref int y, string label, ushort? value)
        {
            Bar(g, new Rectangle(BoxMarginPx, y, width, BarHeightPx), label,
                value == null ? 0 : value.Value / (double)StickDigitizer.AxisMax,
                value == null ? Missing : value.Value.ToString(CultureInfo.InvariantCulture));

            y += BarHeightPx + BarGapPx;
        }

        private void Bar(Graphics g, Rectangle area, string label, double fraction, string value)
        {
            g.DrawString(label, _small, SystemBrushes.ControlText, area.X, area.Y + 1);

            var track = new Rectangle(area.X + BarLabelPx, area.Y,
                area.Width - BarLabelPx - BarValuePx, area.Height);
            if (track.Width <= 0) return;

            g.FillRectangle(_barBackBrush, track);

            int filled = (int)Math.Round(track.Width * Clamp01(fraction));
            if (filled > 0) g.FillRectangle(_barFillBrush, track.X, track.Y, filled, track.Height);

            g.DrawRectangle(_padBorderPen, track);
            g.DrawString(value, _small, SystemBrushes.ControlText, track.Right + BarTextGapPx, area.Y + 1);
        }

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        // -------------------------------------------------------------------- pieces

        private static Label Tile(string text) => new Label
        {
            Text = text,
            AutoSize = false,
            Width = TileWidthPx,
            Height = TileHeightPx,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = TileIdleColour,
            Margin = new Padding(TileMarginPx)
        };

        /// <summary>A panel that repaints without flicker and redraws on resize.</summary>
        private sealed class DrawPanel : Panel
        {
            public DrawPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
            }

            // After the base call: a control must not paint with a font or a pen that has already gone.
            base.Dispose(disposing);

            if (!disposing) return;

            _borderPen.Dispose();
            _offScreenPen.Dispose();
            _rawPen.Dispose();
            _rectPen.Dispose();
            _rectActivePen.Dispose();
            _homographyPen.Dispose();
            _homographyActivePen.Dispose();
            _dimPen.Dispose();
            _dimActivePen.Dispose();
            _padBorderPen.Dispose();
            _arrowPen.Dispose();
            _aimBackBrush.Dispose();
            _barBackBrush.Dispose();
            _barFillBrush.Dispose();
            _pointBrush.Dispose();
            _mono.Dispose();
            _small.Dispose();
        }
    }
}
