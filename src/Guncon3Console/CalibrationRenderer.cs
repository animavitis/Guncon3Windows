// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using Guncon3.Core;

namespace Guncon3Console
{
    /// <summary>Draws one calibration frame. Owns every GDI+ object the picture needs, so a paint at 60 Hz
    /// creates no GDI+ objects. <see cref="Resize"/> rebuilds what depends on the client size (fonts, arm
    /// lengths).</summary>
    internal sealed class CalibrationRenderer : IDisposable
    {
        // Layout, as fractions of the client height unless stated.
        private const float TitleTop = 0.08f;
        private const float LineSpacing = 1.15f;           // of the font height
        private const float UnitMinPx = 12f;               // big font size floor
        private const float UnitDivisor = 40f;             // big font = H / 40
        private const float BodyScale = 0.7f;
        private const float SmallScale = 0.5f;
        private const float Margin = 0.02f;
        private const float LegendBottom = 0.06f;
        private const float TargetSize = 0.05f;            // of min(W, H)
        private const float DoneDotMinPx = 10f;
        private const float DoneDotSize = 0.012f;
        private const float CrosshairArmMinPx = 20f;
        private const float CrosshairArm = 0.03f;
        private const float CrosshairGap = 0.2f;           // of the arm
        private const float PulseAmplitude = 0.15f;
        private const int PulsePeriodMs = 1000;

        private const string LegendSeparator = "      ";
        private static readonly string CapturingLegend = string.Join(LegendSeparator,
            "TRIGGER / SPACE  shoot", "A1 / BACKSPACE  restart", "ESC  cancel");
        private static readonly string CapturingLegendMultiScreen = string.Join(LegendSeparator,
            "TRIGGER / SPACE  shoot", "A1 / BACKSPACE  restart", "← →  or  B1 B2  change screen", "ESC  cancel");
        private static readonly string CheckingLegend = string.Join(LegendSeparator,
            "C2 / ENTER  save & exit", "A1 / BACKSPACE  redo", "A2 / H  switch mapping", "ESC  discard");
        private static readonly string CheckingLegendMultiScreen = string.Join(LegendSeparator,
            "C2 / ENTER  save & exit", "A1 / BACKSPACE  redo", "A2 / H  switch mapping", "← →  or  B1 B2  change screen", "ESC  discard");

        private readonly StringFormat _centred = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
        private readonly StringFormat _right = new() { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };
        private readonly StringFormat _dotLabel = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        private readonly SolidBrush _red = new(Color.Red);
        private readonly Pen _outline = new(Color.White, 3f);
        private readonly Pen _crosshair = new(Color.White, 3f);
        private readonly Pen _otherCrosshair = new(Color.Gray, 2f) { DashStyle = DashStyle.Dash };

        private Size _client;
        private Font _big;
        private Font _body;
        private Font _small;

        public CalibrationRenderer(Size client) => Resize(client);

        /// <summary>Rebuilds the size-dependent resources. Cheap enough to call on every resize event.</summary>
        public void Resize(Size client)
        {
            _client = client;
            float unit = Math.Max(UnitMinPx, client.Height / UnitDivisor);

            _big?.Dispose();
            _body?.Dispose();
            _small?.Dispose();
            _big = new Font(FontFamily.GenericSansSerif, unit, FontStyle.Bold, GraphicsUnit.Pixel);
            _body = new Font(FontFamily.GenericSansSerif, unit * BodyScale, FontStyle.Regular, GraphicsUnit.Pixel);
            _small = new Font(FontFamily.GenericSansSerif, unit * SmallScale, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public void Paint(Graphics g, in FrameState f)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            if (f.Phase == CalibrationPhase.Capturing)
                PaintCapturing(g, in f);
            else
                PaintChecking(g, in f);

            PaintScreenLine(g, in f);
            if (f.ShowRaw) PaintRaw(g, in f);
        }

        private void PaintCapturing(Graphics g, in FrameState f)
        {
            int H = _client.Height;
            float y = H * TitleTop;

            DrawCentred(g, $"GUN {f.GunIndex + 1}  ·  CALIBRATION", _big, Brushes.White, ref y);
            DrawCentred(g, $"Shoot the target   {f.CapturedCount + 1} / {CalibrationSession.Targets.Count}", _body, Brushes.White, ref y);

            if (f.Rejected)
                DrawCentred(g, "those five shots did not span a rectangle — capture thrown away, shoot again", _body, Brushes.Gold, ref y);

            if (f.Disconnected)
                DrawCentred(g, "gun disconnected — ESC to cancel", _body, Brushes.OrangeRed, ref y);
            else if (!f.InsideScreen)
                DrawCentred(g, "the gun does not see the screen", _body, Brushes.OrangeRed, ref y);

            for (int i = 0; i < f.CapturedCount; i++)
                DrawDone(g, ToPixel(CalibrationSession.Targets[i]), i + 1);

            DrawTarget(g, f.NextTarget, ToPixel(CalibrationSession.Targets[f.NextTarget]), Pulse());

            DrawLegend(g, f.ScreenCount > 1 ? CapturingLegendMultiScreen : CapturingLegend);
        }

        private void PaintChecking(Graphics g, in FrameState f)
        {
            int H = _client.Height;
            var file = f.Candidate;
            var active = f.Mode;
            var other = active == CalibrationMode.Rect ? CalibrationMode.Homography : CalibrationMode.Rect;
            float y = H * TitleTop;

            DrawCentred(g, $"GUN {f.GunIndex + 1}  ·  CHECK", _big, Brushes.White, ref y);
            DrawCentred(g, "Aim anywhere. White is what the game will get; grey is the other mapping.", _body, Brushes.Silver, ref y);

            string rectLabel = active == CalibrationMode.Rect ? "▶ LINEAR (rectangle)" : "   linear (rectangle)";
            string homLabel = active == CalibrationMode.Homography ? "▶ PROJECTIVE (homography)" : "   projective (homography)";
            DrawCentred(g, rectLabel + "        " + homLabel, _body, Brushes.White, ref y);

            if (file.Homography == null)
                DrawCentred(g, "projective mapping unavailable: the corners are degenerate", _body, Brushes.OrangeRed, ref y);
            else if (file.Homography.IsSuspect)
                DrawCentred(g, $"projective centre error {file.Homography.CentreError.ToString("0.000", CultureInfo.InvariantCulture)} of the screen — suspect, consider redoing", _body, Brushes.Gold, ref y);
            else
                DrawCentred(g, $"projective centre error {file.Homography.CentreError.ToString("0.000", CultureInfo.InvariantCulture)} of the screen", _body, Brushes.Silver, ref y);

            if (f.Disconnected)
            {
                DrawCentred(g, "gun disconnected — ESC to cancel", _body, Brushes.OrangeRed, ref y);
            }
            else if (!f.InsideScreen)
            {
                // The raw coordinates mean nothing off-screen; the homography can even return NaN there. Say so
                // and draw no crosshair.
                DrawCentred(g, "the gun does not see the screen", _body, Brushes.OrangeRed, ref y);
            }
            else
            {
                if (file.Homography != null)
                    DrawCrosshair(g, ToPixel(Finite(file.MapNormalized(f.RawX, f.RawY, other))), _otherCrosshair);

                DrawCrosshair(g, ToPixel(Finite(file.MapNormalized(f.RawX, f.RawY, active))), _crosshair);
            }

            DrawLegend(g, f.ScreenCount > 1 ? CheckingLegendMultiScreen : CheckingLegend);
        }

        private void PaintScreenLine(Graphics g, in FrameState f)
        {
            int W = _client.Width, H = _client.Height;
            var s = f.Screen;
            string text = f.ScreenCount > 1
                ? $"screen {f.ScreenIndex + 1} of {f.ScreenCount}  ·  {s.W}×{s.H} at ({s.X}, {s.Y})"
                : $"{s.W}×{s.H}";
            var box = new RectangleF(0, H * Margin, W - H * Margin, _small.Height * 2);
            g.DrawString(text, _small, Brushes.Silver, box, _right);
        }

        private void PaintRaw(Graphics g, in FrameState f)
        {
            int H = _client.Height;
            string line = $"RAW X={f.RawX} Y={f.RawY} TRIG={(f.Trigger ? "ON" : "off")} SEES_SCREEN={(f.InsideScreen ? "yes" : "no")}";
            g.DrawString(line, _small, Brushes.Silver, H * Margin, H - _small.Height - H * Margin);
        }

        /// <summary>Centred text in a full-width box; advances y by one line. No MeasureString.</summary>
        private void DrawCentred(Graphics g, string text, Font font, Brush brush, ref float y)
        {
            var box = new RectangleF(0, y, _client.Width, font.Height * 2);
            g.DrawString(text, font, brush, box, _centred);
            y += font.Height * LineSpacing;
        }

        private void DrawLegend(Graphics g, string text)
        {
            int W = _client.Width, H = _client.Height;
            var box = new RectangleF(0, H - _small.Height - H * LegendBottom, W, _small.Height * 2);
            g.DrawString(text, _small, Brushes.Silver, box, _centred);
        }

        private static (double X, double Y) Finite((double X, double Y) n)
            => (double.IsFinite(n.X) ? Math.Clamp(n.X, 0, 1) : 0, double.IsFinite(n.Y) ? Math.Clamp(n.Y, 0, 1) : 0);

        private PointF ToPixel((double X, double Y) n)
            => new((float)(n.X * (_client.Width - 1)), (float)(n.Y * (_client.Height - 1)));

        /// <summary>1 ± PulseAmplitude over one period, so the live target breathes.</summary>
        private static float Pulse()
        {
            double t = Environment.TickCount64 % PulsePeriodMs / (double)PulsePeriodMs;
            return 1f + PulseAmplitude * (float)Math.Sin(t * 2 * Math.PI);
        }

        private void DrawDone(Graphics g, PointF p, int number)
        {
            int W = _client.Width, H = _client.Height;
            float r = Math.Max(DoneDotMinPx, H * DoneDotSize);
            // Corner dots would be half off-screen; nudge them inward.
            float x = Math.Clamp(p.X, r + 2, W - r - 2);
            float y = Math.Clamp(p.Y, r + 2, H - r - 2);

            g.FillEllipse(Brushes.LimeGreen, x - r, y - r, r * 2, r * 2);
            g.DrawString(number.ToString(CultureInfo.InvariantCulture), _small, Brushes.Black,
                new RectangleF(x - r, y - r, r * 2, r * 2), _dotLabel);
        }

        private void DrawTarget(Graphics g, int k, PointF p, float pulse)
        {
            int W = _client.Width, H = _client.Height;
            float tri = Math.Min(W, H) * TargetSize * pulse;

            if (k <= 3)
            {
                float dx = (k == 0 || k == 3) ? tri : -tri;
                float dy = (k == 0 || k == 1) ? tri : -tri;
                // Three points on the stack; the array itself is a per-paint allocation, though not of GDI+
                // objects — those are all prebuilt — so use the overload that takes one and keep it small.
                var pts = new[] { p, new PointF(p.X + dx, p.Y), new PointF(p.X, p.Y + dy) };
                g.FillPolygon(_red, pts);
                g.DrawPolygon(_outline, pts);
            }
            else
            {
                float r = tri * 0.9f;
                g.DrawEllipse(_outline, p.X - r, p.Y - r, r * 2, r * 2);
                g.DrawLine(_outline, p.X - r * 1.7f, p.Y, p.X + r * 1.7f, p.Y);
                g.DrawLine(_outline, p.X, p.Y - r * 1.7f, p.X, p.Y + r * 1.7f);
            }
        }

        private void DrawCrosshair(Graphics g, PointF p, Pen pen)
        {
            int H = _client.Height;
            float arm = Math.Max(CrosshairArmMinPx, H * CrosshairArm);
            float gap = arm * CrosshairGap;
            g.DrawLine(pen, p.X - (arm + gap), p.Y, p.X - gap, p.Y);
            g.DrawLine(pen, p.X + gap, p.Y, p.X + (arm + gap), p.Y);
            g.DrawLine(pen, p.X, p.Y - (arm + gap), p.X, p.Y - gap);
            g.DrawLine(pen, p.X, p.Y + gap, p.X, p.Y + (arm + gap));
            g.DrawEllipse(pen, p.X - gap, p.Y - gap, gap * 2, gap * 2);
        }

        public void Dispose()
        {
            _big?.Dispose();
            _body?.Dispose();
            _small?.Dispose();
            _red.Dispose();
            _outline.Dispose();
            _crosshair.Dispose();
            _otherCrosshair.Dispose();
            _centred.Dispose();
            _right.Dispose();
            _dotLabel.Dispose();
        }
    }
}
