// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using Guncon3.Core;

namespace Guncon3Console.Ui
{
    /// <summary>
    /// The Test Output tab: what the three virtual outputs — cursor, keys, pad — were
    /// last told for one gun, each with the feeder's health in its caption. Laid out like
    /// <see cref="InputTestPanel"/>: two columns, the cursor and the keys on the left,
    /// the pad on the right drawn exactly as the gun's own sticks and buttons are.
    /// </summary>
    internal sealed class OutputTestPanel : TestPanelBase
    {
        /// <summary>The nine physical buttons the joystick feeder reports, in <see cref="GunButton"/> order —
        /// exactly JoystickFeeder's own PhysicalButtons order, i.e. JoystickReportState's bit order.</summary>
        private const int JoystickButtonCount = 9;

        /// <summary>The tile grid is the same five columns as the input tab's, so a button sits at the same
        /// width on both; nine buttons need two rows of it.</summary>
        private const int TileColumns = 5;
        private const int TileRows = 2;

        /// <summary>Bit 8 and up live in <see cref="JoystickReportState.Buttons1"/>.</summary>
        private const int ButtonsPerByte = 8;

        /// <summary>SendInput's own range for an absolute move, over the whole virtual desktop.</summary>
        private const int MouseAxisMax = 65535;

        /// <summary>The cursor picture takes most of the left column; six keys need far less room than a
        /// picture does.</summary>
        private const float MouseGroupPercent = 62f;
        private const float KeyboardGroupPercent = 38f;

        // ----------------------------------------------------------------- text

        private const string StaleText = "stale";
        private const string DisconnectedText = "disconnected";
        private const string HealthyText = "OK";
        private const string FailedText = "failed";
        private const string NoKeys = "none";
        private const string NotMoved = "cursor left where it was";
        private const string MouseGroupName = "Mouse";
        private const string KeyboardGroupName = "Keyboard";
        private const string JoystickGroupName = "Joystick";

        // -------------------------------------------------------------- colours

        private static readonly Color HealthyColour = Color.FromArgb(20, 110, 40);
        private static readonly Color FailedColour = Color.Firebrick;

        // ------------------------------------------------------------- controls

        private readonly GroupBox _mouseGroup;
        private readonly Label _mouseValues;
        private readonly Label[] _mouseTiles = new Label[3];
        private readonly GroupBox _keyboardGroup;
        private readonly Label _keyboardKeys;
        private readonly GroupBox _joystickGroup;
        private readonly DrawPanel _cursor;
        private readonly DrawPanel _joySticks;
        private readonly Label[] _joyTiles = new Label[JoystickButtonCount];

        /// <summary>Says "stale" or "disconnected" when the numbers above are not being refreshed. The input
        /// tab writes the same warning into its aim picture; this tab needs its own place for it.</summary>
        private readonly Label _state;

        internal OutputTestPanel(Func<int, GunFrame> frameSource, Func<IReadOnlyList<GunStatus>> statusSource)
            : base(frameSource, statusSource)
        {
            _mouseValues = new Label { Dock = DockStyle.Top, AutoSize = false, Height = TileHeightPx, Font = Mono };
            var mouseTiles = new FlowLayoutPanel { Dock = DockStyle.Top, Height = TileHeightPx + 2 * TileMarginPx };
            string[] mouseNames = { "L", "R", "M" };
            for (int i = 0; i < _mouseTiles.Length; i++)
            {
                _mouseTiles[i] = Tile(mouseNames[i]);
                _mouseTiles[i].Width = TileWidthPx / 2;
                mouseTiles.Controls.Add(_mouseTiles[i]);
            }

            // The same dark 4:3 picture the input tab draws the aim on, in the same corner of the tab — here it
            // is the virtual desktop, and the dot is where the cursor was actually put.
            _cursor = new DrawPanel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
            _cursor.Paint += (_, e) => SafePaint(e.Graphics, PaintCursor, "cursor picture");

            _mouseGroup = new GroupBox { Dock = DockStyle.Fill, Text = MouseGroupName };
            _mouseGroup.Controls.Add(_cursor);
            _mouseGroup.Controls.Add(mouseTiles);
            _mouseGroup.Controls.Add(_mouseValues);

            _keyboardKeys = new Label { Dock = DockStyle.Fill, AutoSize = false, Font = Mono };
            _keyboardGroup = new GroupBox { Dock = DockStyle.Fill, Text = KeyboardGroupName };
            _keyboardGroup.Controls.Add(_keyboardKeys);

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, MouseGroupPercent));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, KeyboardGroupPercent));
            left.Controls.Add(_mouseGroup, 0, 0);
            left.Controls.Add(_keyboardGroup, 0, 1);

            var tileGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = TileColumns,
                RowCount = TileRows,
                Height = TileRows * (TileHeightPx + 2 * TileMarginPx) + 2
            };
            for (int i = 0; i < TileColumns; i++)
                tileGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / TileColumns));
            for (int i = 0; i < TileRows; i++)
                tileGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, TileHeightPx + 2 * TileMarginPx));

            for (int i = 0; i < _joyTiles.Length; i++)
            {
                // The pad's bit order is the physical buttons in GunButton order, so the first nine names of
                // the input tab's grid are these nine, in these positions.
                _joyTiles[i] = Tile(((GunButton)i).ToString());
                _joyTiles[i].Dock = DockStyle.Fill;
                tileGrid.Controls.Add(_joyTiles[i], i % TileColumns, i / TileColumns);
            }

            _joySticks = new DrawPanel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
            _joySticks.Paint += (_, e) => SafePaint(e.Graphics, PaintPadSticks, "pad sticks");

            _joystickGroup = new GroupBox { Dock = DockStyle.Fill, Text = JoystickGroupName };
            _joystickGroup.Controls.Add(_joySticks);
            _joystickGroup.Controls.Add(tileGrid);

            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            columns.Controls.Add(left, 0, 0);
            columns.Controls.Add(_joystickGroup, 1, 0);

            _state = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = TileHeightPx,
                Font = Small,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(BoxMarginPx, 0, BoxMarginPx, 0)
            };

            var host = new Panel { Dock = DockStyle.Fill };
            host.Controls.Add(columns);
            host.Controls.Add(_state);

            Compose(host);
        }

        // --------------------------------------------------------------- rendering

        protected override void Render()
        {
            var frame = Frame;

            SetGroupHealth(_mouseGroup, MouseGroupName, frame?.MouseHealthy);
            SetGroupHealth(_keyboardGroup, KeyboardGroupName, frame?.KeyboardHealthy);
            SetGroupHealth(_joystickGroup, JoystickGroupName, frame?.JoystickHealthy);

            // Blank with no frame: the cursor picture says it once for the whole tab.
            _mouseValues.Text = frame == null
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture,
                    $"X {frame.Mouse.X,5}  Y {frame.Mouse.Y,5}  position {(frame.Mouse.HasPosition ? "yes" : "no")}");

            for (int i = 0; i < _mouseTiles.Length; i++)
                _mouseTiles[i].BackColor = Lit(frame != null && (frame.Mouse.Buttons & (1 << i)) != 0);

            _keyboardKeys.Text = KeyText(frame);

            for (int i = 0; i < _joyTiles.Length; i++)
            {
                bool lit = frame != null && (i < ButtonsPerByte
                    ? (frame.Joystick.Report.Buttons0 & (1 << i)) != 0
                    : (frame.Joystick.Report.Buttons1 & (1 << (i - ButtonsPerByte))) != 0);
                _joyTiles[i].BackColor = Lit(lit);
            }

            if (!Connected)
            {
                _state.Text = DisconnectedText;
                _state.ForeColor = Color.OrangeRed;
            }
            else if (Stale)
            {
                _state.Text = StaleText;
                _state.ForeColor = Color.DarkGoldenrod;
            }
            else
            {
                _state.Text = string.Empty;
                _state.ForeColor = SystemColors.ControlText;
            }

            _cursor.Invalidate();
            _joySticks.Invalidate();
        }

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

        private static string KeyText(GunFrame frame)
        {
            if (frame == null) return string.Empty;

            var keys = frame.Keyboard.Keys;
            if (keys == null || keys.Length == 0) return NoKeys;

            var lines = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                lines[i] = string.Create(CultureInfo.InvariantCulture,
                    $"{keys[i],3}  {KeyCodeTable.NameOf(keys[i]) ?? Missing}");

            return string.Join(Environment.NewLine, lines);
        }

        // ----------------------------------------------------------------- painting

        /// <summary>Where the cursor was put on the virtual desktop, or why it was not. The dot is the same one
        /// the sticks use, on the same dark 4:3 ground as the input tab's aim.</summary>
        private void PaintCursor(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var box = BoxIn(_cursor.ClientRectangle);
            if (box.Width <= 0 || box.Height <= 0) return;

            g.FillRectangle(ScreenBackBrush, box);
            g.DrawRectangle(PadBorderPen, box);

            var frame = Frame;
            if (frame == null)
            {
                // This tab's one empty-state message; every label above stays blank rather than repeat it.
                CentreText(g, box, NoFrames, Brushes.Silver);
                return;
            }

            if (!frame.Mouse.HasPosition)
            {
                CentreText(g, box, NotMoved, Brushes.Gold);
                return;
            }

            Point(g, box, frame.Mouse.X / (double)MouseAxisMax, frame.Mouse.Y / (double)MouseAxisMax);
        }

        /// <summary>The pad's two sticks and its depth axis, drawn as the gun's own are. No arrows: the report
        /// carries axes, not directions — what digitising there was happened before it.</summary>
        private void PaintPadSticks(Graphics g)
        {
            var frame = Frame;
            var report = frame?.Joystick.Report ?? default;

            PaintSticks(g, _joySticks,
                Stick(LeftStickCaption, frame == null ? null : report.X, frame == null ? null : report.Y),
                Stick(RightStickCaption, frame == null ? null : report.RX, frame == null ? null : report.RY),
                frame == null ? 0 : report.Z / (double)StickDigitizer.AxisMax,
                frame == null ? Missing : report.Z.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>One stick as the base class wants it: the two report axes on 0..1, with the numbers the
        /// bars used to carry drawn inside the pad.</summary>
        private static StickPicture Stick(string caption, ushort? axisX, ushort? axisY)
            => new StickPicture(
                caption,
                axisX == null ? null : axisX.Value / (double)StickDigitizer.AxisMax,
                axisY == null ? null : axisY.Value / (double)StickDigitizer.AxisMax,
                0,
                0,
                axisX == null || axisY == null
                    ? Missing
                    : string.Create(CultureInfo.InvariantCulture, $"{axisX.Value}, {axisY.Value}"));
    }
}
