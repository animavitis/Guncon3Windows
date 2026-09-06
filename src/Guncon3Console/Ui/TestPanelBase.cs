// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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

        // ------------------------------------------------------------------ bars

        protected const int BarHeightPx = 16;
        protected const int BarGapPx = 5;
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

        // -------------------------------------------------------------- colours

        private static readonly Color TileLitColour = Color.FromArgb(120, 205, 130);
        private static readonly Color TileIdleColour = Color.FromArgb(238, 238, 238);
        private static readonly Color BarBackColour = Color.FromArgb(228, 228, 232);
        protected static readonly Color BarFillColour = Color.FromArgb(90, 140, 200);
        private static readonly Color PadBorderColour = Color.FromArgb(150, 150, 158);

        // ------------------------------------------------------------- controls

        private readonly ToolStrip _tools;
        private readonly ToolStripComboBox _guns;
        private readonly WinFormsTimer _timer;

        // ------------------------------------------------------ GDI+ resources

        /// <summary>Also the outline of a bar's track and of a stick's pad.</summary>
        protected readonly Pen PadBorderPen = new Pen(PadBorderColour, BorderWidth);
        private readonly SolidBrush _barBackBrush = new SolidBrush(BarBackColour);
        private readonly SolidBrush _barFillBrush = new SolidBrush(BarFillColour);
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

        // -------------------------------------------------------------------- pieces

        protected static Color Lit(bool on) => on ? TileLitColour : TileIdleColour;

        protected static Label Tile(string text) => new Label
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
            Mono.Dispose();
            Small.Dispose();
        }
    }
}
