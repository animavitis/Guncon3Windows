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
    /// The Test Input tab: what one gun is sending right now. Left, the aim as three
    /// crosshairs on a picture of the calibrated screen with the same three points as
    /// numbers; right, every button as a tile and both sticks as pads, with Z on a bar.
    /// Nothing here is a virtual output — that is <see cref="OutputTestPanel"/>.
    /// </summary>
    internal sealed class InputTestPanel : TestPanelBase
    {
        // ----------------------------------------------------------- aim picture

        private const float OffScreenBorderWidth = 3f;
        private const int CrosshairArmPx = 8;
        private const int ActiveCrosshairArmPx = 13;
        private const float CrosshairWidth = 1f;
        private const float ActiveCrosshairWidth = 2.5f;
        private const int AimTextLines = 4;

        // -------------------------------------------------------------- sticks

        /// <summary>The gun's own stick and depth axes, before any digitisation.</summary>
        private const int RawAxisMax = 255;

        // ---------------------------------------------------------------- tiles

        // Five columns, not six: at the window's minimum width six of them are too narrow for "Trigger".
        private const int TileColumns = 5;
        private const int TileRows = 4;

        // ----------------------------------------------------------------- text

        private const string NoCalibration = "no calibration";
        private const string NoHomography = "no homography";
        private const string StaleText = "stale";
        private const string DisconnectedText = "disconnected";
        private const string PairFormat = "0.000";
        private const string PercentFormat = "0.0";

        // -------------------------------------------------------------- colours

        private static readonly Color BorderColour = Color.FromArgb(120, 120, 130);
        private static readonly Color OffScreenColour = Color.FromArgb(205, 45, 45);
        private static readonly Color RawColour = Color.FromArgb(130, 130, 130);
        private static readonly Color RectColour = Color.White;
        private static readonly Color HomographyColour = Color.FromArgb(0, 205, 225);
        private static readonly Color DimColour = Color.FromArgb(80, 80, 86);

        // ------------------------------------------------------------- controls

        private readonly DrawPanel _aim;
        private readonly Label _aimNumbers;
        private readonly Label[] _tiles = new Label[GunButtons.Count];
        private readonly DrawPanel _sticks;

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

        internal InputTestPanel(Func<int, GunFrame> frameSource, Func<IReadOnlyList<GunStatus>> statusSource)
            : base(frameSource, statusSource)
        {
            _aim = new DrawPanel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
            _aim.Paint += (_, e) => SafePaint(e.Graphics, PaintAim, "aim picture");

            _aimNumbers = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Font = Mono,
                Padding = new Padding(BoxTextPadPx, 2, BoxTextPadPx, 2)
            };
            _aimNumbers.Height = AimTextLines * Mono.Height + 2 * BoxTextPadPx;

            var aimHost = new Panel { Dock = DockStyle.Fill };
            aimHost.Controls.Add(_aim);
            aimHost.Controls.Add(_aimNumbers);

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

            int cell = 0;
            foreach (GunButton button in Enum.GetValues<GunButton>())
            {
                var tile = Tile(button.ToString());
                tile.Dock = DockStyle.Fill;
                _tiles[cell] = tile;
                tileGrid.Controls.Add(tile, cell % TileColumns, cell / TileColumns);
                cell++;
            }

            _sticks = new DrawPanel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
            _sticks.Paint += (_, e) => SafePaint(e.Graphics, PaintGunSticks, "stick pads");

            var gunHost = new Panel { Dock = DockStyle.Fill };
            gunHost.Controls.Add(_sticks);
            gunHost.Controls.Add(tileGrid);

            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            columns.Controls.Add(aimHost, 0, 0);
            columns.Controls.Add(gunHost, 1, 0);

            Compose(columns);
        }

        // --------------------------------------------------------------- rendering

        protected override void Render()
        {
            var frame = Frame;

            _aimNumbers.Text = AimText(frame);

            for (int i = 0; i < _tiles.Length; i++)
                _tiles[i].BackColor = Lit(frame != null && i < frame.Buttons.Length && frame.Buttons[i]);

            _aim.Invalidate();
            _sticks.Invalidate();
        }

        private static string AimText(GunFrame frame)
        {
            if (frame == null) return string.Empty;

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

        // ----------------------------------------------------------------- painting

        private void PaintAim(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var box = BoxIn(_aim.ClientRectangle);
            if (box.Width <= 0 || box.Height <= 0) return;

            g.FillRectangle(ScreenBackBrush, box);

            var frame = Frame;
            bool offScreen = frame != null && !frame.InsideScreen;
            g.DrawRectangle(offScreen ? _offScreenPen : _borderPen, box);

            if (frame == null)
            {
                // This tab's one empty-state message. The numbers under the box stay blank rather than repeat it.
                CentreText(g, box, NoFrames, Brushes.Silver);
                return;
            }

            if (!frame.HasCalibration)
                g.DrawString(NoCalibration, Small, Brushes.Gold, box.X + BoxTextPadPx, box.Y + BoxTextPadPx);
            else if (frame.Homography == null)
                g.DrawString(NoHomography, Small, Brushes.Gold, box.X + BoxTextPadPx, box.Y + BoxTextPadPx);

            if (!offScreen)
            {
                bool homographyActive = frame.Mode == CalibrationMode.Homography && frame.Homography != null;

                // The active mode last and thicker, so it is the one the eye follows.
                Crosshair(g, box, frame.RawNormalized, Connected ? _rawPen : _dimPen, CrosshairArmPx);

                if (homographyActive)
                {
                    Crosshair(g, box, frame.Rect, Connected ? _rectPen : _dimPen, CrosshairArmPx);
                    Crosshair(g, box, frame.Homography, Connected ? _homographyActivePen : _dimActivePen, ActiveCrosshairArmPx);
                }
                else
                {
                    Crosshair(g, box, frame.Homography, Connected ? _homographyPen : _dimPen, CrosshairArmPx);
                    Crosshair(g, box, frame.Rect, Connected ? _rectActivePen : _dimActivePen, ActiveCrosshairArmPx);
                }
            }

            if (Stale)
                g.DrawString(StaleText, Small, Brushes.Gold,
                    box.Right - BoxTextPadPx - g.MeasureString(StaleText, Small).Width, box.Y + BoxTextPadPx);

            if (!Connected)
                g.DrawString(DisconnectedText, Small, Brushes.OrangeRed,
                    box.X + BoxTextPadPx, box.Bottom - BoxTextPadPx - Small.Height);
        }

        private static void Crosshair(Graphics g, Rectangle box, (double X, double Y)? point, Pen pen, int arm)
        {
            if (point == null) return;

            float x = box.X + (float)(point.Value.X * box.Width);
            float y = box.Y + (float)(point.Value.Y * box.Height);

            g.DrawLine(pen, x - arm, y, x + arm, y);
            g.DrawLine(pen, x, y - arm, x, y + arm);
        }

        private void PaintGunSticks(Graphics g)
        {
            var frame = Frame;

            PaintSticks(g, _sticks,
                Stick(LeftStickCaption, frame?.HatX, frame?.HatY,
                      Pressed(frame, GunButton.LUp), Pressed(frame, GunButton.LDown),
                      Pressed(frame, GunButton.LLeft), Pressed(frame, GunButton.LRight)),
                Stick(RightStickCaption, frame?.RX, frame?.RY,
                      Pressed(frame, GunButton.RUp), Pressed(frame, GunButton.RDown),
                      Pressed(frame, GunButton.RLeft), Pressed(frame, GunButton.RRight)),
                frame == null ? 0 : frame.Z / (double)RawAxisMax,
                frame == null ? Missing : frame.Z.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>One stick as the base class wants it: the two raw axes on 0..1 and the four digitised
        /// direction flags reduced to a sign per axis.</summary>
        private static StickPicture Stick(string caption, int? axisX, int? axisY,
                                          bool up, bool down, bool leftward, bool rightward)
            => new StickPicture(
                caption,
                axisX == null ? null : axisX.Value / (double)RawAxisMax,
                axisY == null ? null : axisY.Value / (double)RawAxisMax,
                (rightward ? 1 : 0) - (leftward ? 1 : 0),
                (down ? 1 : 0) - (up ? 1 : 0),
                null);

        private static bool Pressed(GunFrame frame, GunButton button)
            => frame != null && (int)button < frame.Buttons.Length && frame.Buttons[(int)button];

        protected override void DisposeResources()
        {
            _borderPen.Dispose();
            _offScreenPen.Dispose();
            _rawPen.Dispose();
            _rectPen.Dispose();
            _rectActivePen.Dispose();
            _homographyPen.Dispose();
            _homographyActivePen.Dispose();
            _dimPen.Dispose();
            _dimActivePen.Dispose();

            base.DisposeResources();
        }
    }
}
