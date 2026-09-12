// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace Guncon3Console.Ui
{
    /// <summary>
    /// What the two Test tabs have in common: one gun selector, one refresh timer, the
    /// newest <see cref="GunFrame"/> for the selected gun, and the small drawing pieces
    /// both halves use. Passive by construction — a panel holds no engine reference,
    /// only two delegates the host supplies — and it asks for nothing while
    /// <see cref="SetActive"/> was last called with false.
    /// </summary>
    /// <remarks>
    /// A subclass builds its own controls and ends its constructor with
    /// <see cref="Compose"/>, which docks them under the selector and paints the first
    /// frame. It implements <see cref="Render"/> and, if it owns GDI+ handles, overrides
    /// <see cref="DisposeResources"/>.
    /// </remarks>
    internal abstract class TestPanelBase : UserControl
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

        // ---------------------------------------------------------------- layout

        protected const int BoxMarginPx = 8;
        protected const float BorderWidth = 1f;
        protected const int BoxTextPadPx = 6;

        /// <summary>Both tabs draw one dark picture in the same place: the calibrated screen on the input side,
        /// the virtual desktop on the output one. Both are 4:3.</summary>
        protected const int AspectW = 4;
        protected const int AspectH = 3;

        // ---------------------------------------------------------------- sticks

        /// <summary>A pad takes half the panel it is drawn on, but never shrinks past this: below it the point
        /// and the arrow stop saying anything.</summary>
        private const int MinPadSizePx = 70;

        private const int PadGapPx = 14;
        private const int PadTopPx = 16;
        private const int PointRadiusPx = 4;
        private const int ArrowLengthPx = 38;
        private const float ArrowWidth = 2f;
        private const int PadValuePadPx = 4;

        // ------------------------------------------------------------------ bars

        private const int BarHeightPx = 16;
        private const int BarGapPx = 5;
        private const int BarLabelPx = 30;
        private const int BarValuePx = 56;
        private const int BarTextGapPx = 4;

        // ---------------------------------------------------------------- tiles

        protected const int TileWidthPx = 62;
        protected const int TileHeightPx = 22;
        protected const int TileMarginPx = 2;

        // ----------------------------------------------------------------- text

        protected const string NoFrames = "no frames yet";
        protected const string Missing = "—";
        protected const string LeftStickCaption = "Left stick";
        protected const string RightStickCaption = "Right stick";
        protected const string DepthCaption = "Z";

        // -------------------------------------------------------------- colours

        private static readonly Color TileLitColour = Color.FromArgb(120, 205, 130);
        private static readonly Color TileIdleColour = Color.FromArgb(238, 238, 238);
        private static readonly Color BarBackColour = Color.FromArgb(228, 228, 232);
        private static readonly Color BarFillColour = Color.FromArgb(90, 140, 200);
        private static readonly Color PadBorderColour = Color.FromArgb(150, 150, 158);
        private static readonly Color PointColour = Color.White;
        private static readonly Color ScreenBackColour = Color.FromArgb(24, 24, 28);

        // ------------------------------------------------------------- controls

        private readonly ToolStrip _tools;
        private readonly ToolStripComboBox _guns;
        private readonly WinFormsTimer _timer;

        // ------------------------------------------------------ GDI+ resources

        /// <summary>Also the outline of a bar's track and of a stick's pad.</summary>
        protected readonly Pen PadBorderPen = new Pen(PadBorderColour, BorderWidth);
        private readonly SolidBrush _barBackBrush = new SolidBrush(BarBackColour);
        private readonly SolidBrush _barFillBrush = new SolidBrush(BarFillColour);
        private readonly Pen _arrowPen = new Pen(BarFillColour, ArrowWidth) { EndCap = LineCap.ArrowAnchor };
        private readonly SolidBrush _pointBrush = new SolidBrush(PointColour);

        /// <summary>The ground of the 4:3 picture — the aim box on one tab, the cursor box on the other.</summary>
        protected readonly SolidBrush ScreenBackBrush = new SolidBrush(ScreenBackColour);
        protected readonly Font Mono = new Font(FontFamily.GenericMonospace, 9f);
        protected readonly Font Small = new Font(FontFamily.GenericSansSerif, 8f);

        // ----------------------------------------------------------------- state

        private readonly List<int> _indices = new List<int>();
        private bool _connectedNow;
        private int _statusCountdown;
        private int _selected;

        /// <summary>The newest frame for the selected gun, or null when there is none.</summary>
        protected GunFrame Frame { get; private set; }

        /// <summary>True when <see cref="Frame"/> is older than <see cref="StaleAfterMs"/>.</summary>
        protected bool Stale { get; private set; }

        /// <summary>The selected gun's connected flag, re-read twice a second.</summary>
        protected bool Connected { get; private set; }

        /// <summary>Names of the drawing panels that have already logged their one paint failure.</summary>
        private readonly HashSet<string> _paintWarned = new HashSet<string>();

        /// <summary>The newest frame for a slot position, or null when there is none. The host passes
        /// <c>App.LatestFrame</c>; called on the UI thread only.</summary>
        private readonly Func<int, GunFrame> _frameSource;

        /// <summary>Every gun's status. The host passes <c>App.Status</c>; read twice a second rather than
        /// every tick, and only for the connected flag.</summary>
        private readonly Func<IReadOnlyList<GunStatus>> _statusSource;

        protected TestPanelBase(Func<int, GunFrame> frameSource, Func<IReadOnlyList<GunStatus>> statusSource)
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

            _timer = new WinFormsTimer { Interval = RefreshMs };
            _timer.Tick += (_, __) => Tick();
        }

        /// <summary>Docks the subclass's filling control under the gun selector and draws the first, empty
        /// frame. The last statement of a subclass constructor: <see cref="Render"/> runs from it, so every
        /// field it touches must already be set.</summary>
        protected void Compose(Control fill)
        {
            ArgumentNullException.ThrowIfNull(fill);

            // Docking is applied from the last added control backwards, so the filling control goes in first
            // and the tool strip last.
            Controls.Add(fill);
            Controls.Add(_tools);

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

            Frame = null;
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
            Frame = null;
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

            if (ReferenceEquals(frame, Frame) && stale == Stale && _connectedNow == Connected)
                return;

            Frame = frame;
            Stale = stale;
            Connected = _connectedNow;
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

        /// <summary>Pushes <see cref="Frame"/> into this tab's controls and invalidates its drawing panels.
        /// Called on the UI thread only, and only when something changed.</summary>
        protected abstract void Render();

        // ----------------------------------------------------------------- painting

        /// <summary>Runs one paint body. A drawing bug at 30 Hz must not take the window down or fill the log —
        /// each panel's first failure is reported and the rest are silent.</summary>
        protected void SafePaint(Graphics g, Action<Graphics> paint, string what)
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

        /// <summary>
        /// One stick as a pad drawing. <paramref name="X"/> and <paramref name="Y"/> are already on 0..1 —
        /// whatever the axis's own range was — or null when there is nothing to draw. <paramref name="ArrowX"/>
        /// and <paramref name="ArrowY"/> are a sign per axis, both zero for no arrow. <paramref name="Values"/>
        /// is drawn inside the pad, or null for none.
        /// </summary>
        protected readonly record struct StickPicture(
            string Caption, double? X, double? Y, int ArrowX, int ArrowY, string Values);

        /// <summary>The picture both Test tabs draw for a pair of sticks: two square pads side by side, with the
        /// depth axis on a bar under them. The caller has already put every axis on 0..1, so the gun's 0..255
        /// and the virtual pad's 0..32767 arrive here the same shape.</summary>
        protected void PaintSticks(Graphics g, Panel host, in StickPicture left, in StickPicture right,
                                   double depth, string depthText)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Square, half the width each, but never so tall that the depth bar falls off the bottom.
            int size = (host.ClientSize.Width - 2 * BoxMarginPx - PadGapPx) / 2;
            int room = host.ClientSize.Height - PadTopPx - PadGapPx - BarHeightPx - BarGapPx;
            if (size > room) size = room;
            if (size < MinPadSizePx) size = MinPadSizePx;

            var leftBox = new Rectangle(BoxMarginPx, PadTopPx, size, size);
            var rightBox = new Rectangle(leftBox.Right + PadGapPx, PadTopPx, size, size);

            Pad(g, leftBox, left);
            Pad(g, rightBox, right);

            var bar = new Rectangle(BoxMarginPx, leftBox.Bottom + PadGapPx,
                Math.Max(0, host.ClientSize.Width - 2 * BoxMarginPx), BarHeightPx);

            Bar(g, bar, DepthCaption, depth, depthText);
        }

        /// <summary>
        /// One stick: the point where the two axes put it and, when the caller passed a
        /// direction, an arrow from the centre. On the input side that direction comes
        /// from the frame's button flags — the gun's own digitisation, deadzone and
        /// measured centres included — so nothing here re-derives it; the virtual pad
        /// reports no directions and draws none.
        /// </summary>
        private void Pad(Graphics g, Rectangle box, in StickPicture stick)
        {
            g.DrawString(stick.Caption, Small, SystemBrushes.ControlText, box.X, box.Y - Small.Height);
            g.DrawRectangle(PadBorderPen, box);
            g.DrawLine(PadBorderPen, box.X, box.Y + box.Height / 2, box.Right, box.Y + box.Height / 2);
            g.DrawLine(PadBorderPen, box.X + box.Width / 2, box.Y, box.X + box.Width / 2, box.Bottom);

            if (stick.X == null || stick.Y == null) return;

            Point(g, box, stick.X.Value, stick.Y.Value);

            if (stick.Values != null)
            {
                float width = g.MeasureString(stick.Values, Small).Width;
                g.DrawString(stick.Values, Small, SystemBrushes.ControlText,
                    box.X + (box.Width - width) / 2f, box.Bottom - Small.Height - PadValuePadPx);
            }

            if (stick.ArrowX == 0 && stick.ArrowY == 0) return;

            float cx = box.X + box.Width / 2f;
            float cy = box.Y + box.Height / 2f;
            double length = Math.Sqrt(stick.ArrowX * stick.ArrowX + stick.ArrowY * stick.ArrowY);
            g.DrawLine(_arrowPen, cx, cy,
                cx + (float)(stick.ArrowX / length * ArrowLengthPx),
                cy + (float)(stick.ArrowY / length * ArrowLengthPx));
        }

        /// <summary>One labelled bar: the gun's Z on the input side, a virtual pad axis on the output one.</summary>
        protected void Bar(Graphics g, Rectangle area, string label, double fraction, string value)
        {
            g.DrawString(label, Small, SystemBrushes.ControlText, area.X, area.Y + 1);

            var track = new Rectangle(area.X + BarLabelPx, area.Y,
                area.Width - BarLabelPx - BarValuePx, area.Height);
            if (track.Width <= 0) return;

            g.FillRectangle(_barBackBrush, track);

            int filled = (int)Math.Round(track.Width * Clamp01(fraction));
            if (filled > 0) g.FillRectangle(_barFillBrush, track.X, track.Y, filled, track.Height);

            g.DrawRectangle(PadBorderPen, track);
            g.DrawString(value, Small, SystemBrushes.ControlText, track.Right + BarTextGapPx, area.Y + 1);
        }

        protected static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>The largest 4:3 box that fits, centred: the picture is a screen, not the panel.</summary>
        protected static Rectangle BoxIn(Rectangle client)
        {
            int w = client.Width - 2 * BoxMarginPx;
            int h = client.Height - 2 * BoxMarginPx;
            if (w <= 0 || h <= 0) return Rectangle.Empty;

            if (w * AspectH > h * AspectW) w = h * AspectW / AspectH;
            else h = w * AspectH / AspectW;

            return new Rectangle(client.X + (client.Width - w) / 2, client.Y + (client.Height - h) / 2, w, h);
        }

        /// <summary>One line in the middle of a box: what a picture says when it has nothing to draw. Each tab
        /// says it once, in its own picture, rather than in every label at once.</summary>
        protected void CentreText(Graphics g, Rectangle box, string text, Brush brush)
        {
            var size = g.MeasureString(text, Small);
            g.DrawString(text, Small, brush,
                box.X + (box.Width - size.Width) / 2f, box.Y + (box.Height - size.Height) / 2f);
        }

        /// <summary>The white dot both pictures use for a point on 0..1 in each axis.</summary>
        protected void Point(Graphics g, Rectangle box, double x, double y)
        {
            float px = box.X + (float)(Clamp01(x) * box.Width);
            float py = box.Y + (float)(Clamp01(y) * box.Height);

            g.FillEllipse(_pointBrush, px - PointRadiusPx, py - PointRadiusPx, PointRadiusPx * 2, PointRadiusPx * 2);
            g.DrawEllipse(PadBorderPen, px - PointRadiusPx, py - PointRadiusPx, PointRadiusPx * 2, PointRadiusPx * 2);
        }

        // -------------------------------------------------------------------- pieces

        protected static Color Lit(bool on) => on ? TileLitColour : TileIdleColour;

        protected static Label Tile(string text) => new Label
        {
            Text = text,
            AutoSize = false,
            // Without this a tile too narrow for its caption cuts it mid-word — "Trigg", "AClic" — with nothing
            // to say it did.
            AutoEllipsis = true,
            Width = TileWidthPx,
            Height = TileHeightPx,
            TextAlign = ContentAlignment.MiddleCenter,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = TileIdleColour,
            Margin = new Padding(TileMarginPx)
        };

        /// <summary>A panel that repaints without flicker and redraws on resize.</summary>
        protected sealed class DrawPanel : Panel
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

            if (disposing) DisposeResources();
        }

        /// <summary>Releases this panel's GDI+ handles, after the controls that painted with them are gone. An
        /// override disposes its own and then calls this one.</summary>
        protected virtual void DisposeResources()
        {
            PadBorderPen.Dispose();
            _barBackBrush.Dispose();
            _barFillBrush.Dispose();
            _arrowPen.Dispose();
            _pointBrush.Dispose();
            ScreenBackBrush.Dispose();
            Mono.Dispose();
            Small.Dispose();
        }
    }
}
