using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;
using Guncon3.Core;

namespace Guncon3Console
{
    public class CalibrationWindow : Form
    {
        private readonly WinFormsTimer _poll;
        private readonly List<(double X, double Y)> _rawPoints = new();
        private PointF[] _targets = Array.Empty<PointF>();
        private int _idx = 0;
        private bool _prevTrig = false;
        private bool _prevA1 = false, _prevC2 = false;
        private bool _checking = false;
        private CalibrationFile _fileForCheck = null;

        private readonly GunconReader _reader;
        private readonly int _gunIndex;
        private readonly CalibrationMode _mode;

        public CalibrationWindow(GunconReader reader, int gunIndex = 0, CalibrationMode mode = CalibrationMode.Rect)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _gunIndex = gunIndex;
            _mode = mode;

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            Bounds = Screen.PrimaryScreen.Bounds;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Color.DimGray;
            ForeColor = Color.White;

            Shown += (_, __) => { try { Cursor.Hide(); } catch { } };
            FormClosed += (_, __) => { try { Cursor.Show(); } catch { } };

            KeyDown += CalibrationWindow_KeyDown;
            MouseDown += CalibrationWindow_MouseDown;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            UpdateStyles();

            RebuildTargets();
            Resize += (_, __) => { RebuildTargets(); Invalidate(); };

            _poll = new WinFormsTimer { Interval = 16 };
            _poll.Tick += PollTick;
            _poll.Start();
        }

        private void CalibrationWindow_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !_checking)
                CapturePointSafe();
        }

        private void CalibrationWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
                Close();
            else if (e.KeyCode == Keys.Space && !_checking)
                CapturePointSafe();
        }

        private void CapturePointSafe()
        {
            try { _reader.Read(); } catch { }

            try
            {
                double rawX = _reader.State.ABS_X;
                double rawY = _reader.State.ABS_Y;
                CapturePoint(rawX, rawY);
            }
            catch (Exception ex) { DumpError("cal_capture_error.txt", ex); }
        }

        private void PollTick(object sender, EventArgs e)
        {
            try { _reader.Read(); } catch { }

            if (_checking)
            {
                bool a1 = _reader.State.BtnState.TryGetValue(GunButton.A1, out var v1) && v1;
                bool c2 = _reader.State.BtnState.TryGetValue(GunButton.C2, out var v2) && v2;

                if (!_prevA1 && a1)
                {
                    _checking = false;
                    _fileForCheck = null;
                    _rawPoints.Clear();
                    _idx = 0;
                    RebuildTargets();
                }
                else if (!_prevC2 && c2)
                {
                    Close();
                    return;
                }

                _prevA1 = a1;
                _prevC2 = c2;
            }
            else
            {
                bool t = _reader.State.BtnState.TryGetValue(GunButton.Trigger, out var trig) && trig;
                if (!_prevTrig && t)
                {
                    CapturePoint(_reader.State.ABS_X, _reader.State.ABS_Y);
                }
                _prevTrig = t;
            }

            Invalidate();
        }

        private void RebuildTargets()
        {
            int W = Math.Max(1, ClientSize.Width);
            int H = Math.Max(1, ClientSize.Height);
            _targets = new[]
            {
                new PointF(0,0),
                new PointF(W-1,0),
                new PointF(W-1,H-1),
                new PointF(0,H-1),
                new PointF(W/2f,H/2f)
            };
            _idx = 0;
        }

        private void CapturePoint(double rawX, double rawY)
        {
            if (_rawPoints.Count >= 5) return;
            _rawPoints.Add((rawX, rawY));
            _idx++;

            if (_idx < 5) { Invalidate(); return; }

            FinishAndSave();
        }

        private void FinishAndSave()
        {
            try
            {
                _poll.Stop();

                int W = Screen.PrimaryScreen.Bounds.Width;
                int H = Screen.PrimaryScreen.Bounds.Height;

                var file = CalibrationFile.FromCapture(_rawPoints, W, H);
                file.Save(gunIndex: _gunIndex);

                _fileForCheck = file;
                _checking = true;
                _poll.Start();
            }
            catch (Exception ex)
            {
                DumpRaw("cal_raw_dump.txt", ex);
                MessageBox.Show("Calibration error: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var f = new Font(FontFamily.GenericSansSerif, 18f, FontStyle.Bold);
            using var b = new SolidBrush(Color.White);

            string gunLabel = _gunIndex > 0 ? $" [Gun {_gunIndex + 1}]" : " [Gun 1]";

            if (!_checking)
            {
                g.DrawString($"SHOOT THE MARK (5 POINTS){gunLabel}. ESC = cancel / Space = capture", f, b, new PointF(20, 20));
                g.DrawString("Progress: " + Math.Min(_idx + 1, 5) + "/5", f, b, new PointF(20, 46));

                string rawLine = "RAW: X=0 Y=0 TRIG=off";
                try
                {
                    double px = _reader.State.ABS_X;
                    double py = _reader.State.ABS_Y;
                    string trig = (_reader.State.BtnState.TryGetValue(GunButton.Trigger, out var t) && t) ? "ON" : "off";
                    rawLine = $"RAW: X={px} Y={py} TRIG={trig}";
                }
                catch { }

                g.DrawString(rawLine, f, b, new PointF(20, 72));

                int k = Math.Min(_idx, _targets.Length - 1);
                DrawTarget(g, k, _targets[k], ClientSize);
            }
            else
            {
                g.DrawString($"CHECK CALIBRATION{gunLabel} — A1 = recalibrate | C2 = save & exit", f, b, new PointF(20, 20));

                double rx = _reader.State.ABS_X;
                double ry = _reader.State.ABS_Y;
                var (nx, ny) = _fileForCheck.MapNormalized(rx, ry, _mode);

                DrawCrosshair(g,
                    (float)(nx * (ClientSize.Width - 1)),
                    (float)(ny * (ClientSize.Height - 1)));
            }
        }

        private static void DrawTarget(Graphics g, int k, PointF p, Size client)
        {
            using var red = new SolidBrush(Color.Red);
            using var penW = new Pen(Color.White, 3f);

            float tri = Math.Min(client.Width, client.Height) * 0.05f;
            if (k <= 3)
            {
                PointF a, b2, c;
                if (k == 0) { a = new(p.X, p.Y); b2 = new(p.X + tri, p.Y); c = new(p.X, p.Y + tri); }
                else if (k == 1) { a = new(p.X, p.Y); b2 = new(p.X - tri, p.Y); c = new(p.X, p.Y + tri); }
                else if (k == 2) { a = new(p.X, p.Y); b2 = new(p.X - tri, p.Y); c = new(p.X, p.Y - tri); }
                else { a = new(p.X, p.Y); b2 = new(p.X + tri, p.Y); c = new(p.X, p.Y - tri); }
                g.FillPolygon(red, new[] { a, b2, c });
                g.DrawPolygon(penW, new[] { a, b2, c });
            }
            else
            {
                float r = tri * 0.9f;
                g.DrawEllipse(penW, p.X - r, p.Y - r, r * 2, r * 2);
                g.DrawLine(penW, p.X - r * 1.7f, p.Y, p.X + r * 1.7f, p.Y);
                g.DrawLine(penW, p.X, p.Y - r * 1.7f, p.X, p.Y + r * 1.7f);
            }
        }

        private static void DrawCrosshair(Graphics g, float x, float y)
        {
            using var pen = new Pen(Color.White, 2f);
            const int arm = 20;
            const int gap = 4;
            g.DrawLine(pen, x - (arm + gap), y, x - gap, y);
            g.DrawLine(pen, x + gap, y, x + (arm + gap), y);
            g.DrawLine(pen, x, y - (arm + gap), x, y - gap);
            g.DrawLine(pen, x, y + gap, x, y + (arm + gap));
        }

        private void DumpError(string file, Exception ex)
        {
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file), ex.ToString()); } catch { }
        }

        private void DumpRaw(string file, Exception ex)
        {
            try
            {
                string dump = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file);
                using var sw = new StreamWriter(dump, false);
                sw.WriteLine("# Calibration error: " + ex.Message);
                sw.WriteLine("# RAW:");
                foreach (var p in _rawPoints)
                    sw.WriteLine($"{p.X},{p.Y}");
            }
            catch { }
        }
    }
}
