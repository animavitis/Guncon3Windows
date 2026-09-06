// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Guncon3.Core;

namespace Guncon3Console.Ui
{
    /// <summary>
    /// The Test Output tab: what the three virtual outputs — cursor, keys, pad — were
    /// last told for one gun, each with the feeder's health in its caption. The gun's
    /// own aim and buttons are the other tab, <see cref="InputTestPanel"/>.
    /// </summary>
    internal sealed class OutputTestPanel : TestPanelBase
    {
        /// <summary>The nine physical buttons the joystick feeder reports, in <see cref="GunButton"/> order —
        /// exactly JoystickFeeder's own PhysicalButtons order, i.e. JoystickReportState's bit order.</summary>
        private const int JoystickButtonCount = 9;

        // ----------------------------------------------------------------- text

        private const string StaleText = "stale";
        private const string DisconnectedText = "disconnected";
        private const string HealthyText = "OK";
        private const string FailedText = "failed";
        private const string NoKeys = "none";
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
        private readonly DrawPanel _joyBars;
        private readonly Label[] _joyTiles = new Label[JoystickButtonCount];

        /// <summary>Says "stale" or "disconnected" when the numbers above are not being refreshed. The aim
        /// picture carries the same warning on the input tab; this tab needs its own.</summary>
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

            _mouseGroup = new GroupBox { Dock = DockStyle.Fill, Text = MouseGroupName };
            _mouseGroup.Controls.Add(mouseTiles);
            _mouseGroup.Controls.Add(_mouseValues);

            _keyboardKeys = new Label { Dock = DockStyle.Fill, AutoSize = false, Font = Mono };
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
            host.Controls.Add(outputs);
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

            _joyBars.Invalidate();
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

        private void PaintJoystickBars(Graphics g)
        {
            var frame = Frame;
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
    }
}
