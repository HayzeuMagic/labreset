// Lab Reset - one PC in the progress grid: a monitor icon, the PC number and a progress bar.
// C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LabReset
{
    public enum TileState { Idle, Waiting, Working, Done, Error, Skipped }

    public class PcTile : Control
    {
        // one shared animation clock for every tile
        static readonly Timer anim = new Timer { Interval = 40 };
        static int phase;
        static event EventHandler AnimTick;
        static PcTile()
        {
            anim.Tick += (s, e) =>
            {
                phase++;
                EventHandler h = AnimTick;
                if (h != null) h(null, EventArgs.Empty);
            };
            anim.Start();
        }

        static readonly Color CIdle = Color.FromArgb(214, 221, 230);
        static readonly Color CWait = Color.FromArgb(170, 200, 240);
        static readonly Color CWork = Color.FromArgb(37, 117, 230);
        static readonly Color CDone = Color.FromArgb(34, 150, 80);
        static readonly Color CError = Color.FromArgb(214, 64, 64);
        static readonly Color CSkip = Color.FromArgb(150, 158, 168);
        static readonly Color CFrame = Color.FromArgb(55, 62, 72);
        static readonly Color CTrack = Color.FromArgb(226, 230, 236);

        public readonly string Pc;
        readonly string number;
        bool ticked = true, hover;
        TileState state = TileState.Idle;
        int percent;
        string detail = "";
        readonly ToolTip tip = new ToolTip();
        readonly string tooltip;

        public event EventHandler TickToggled;
        /// <summary>The ⏻ button was clicked, or the tile was right-clicked. Location = where to show the menu.</summary>
        public event MouseEventHandler PowerMenuRequested;
        bool powerHover;

        // the small ⏻ button in the top-right corner
        Rectangle PowerRect { get { return new Rectangle(Width - 23, 3, 19, 19); } }

        public PcTile(string pc, string number, string tooltip)
        {
            Pc = pc;
            this.number = number;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(124, 150);
            Margin = new Padding(6);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            this.tooltip = tooltip;
            tip.SetToolTip(this, tooltip);
            AnimTick += OnAnim;
        }

        protected override void Dispose(bool disposing)
        {
            AnimTick -= OnAnim;
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        void OnAnim(object s, EventArgs e)
        {
            if (state == TileState.Working || state == TileState.Waiting) Invalidate();
        }

        public bool Ticked { get { return ticked; } set { if (ticked != value) { ticked = value; Invalidate(); } } }
        public TileState State { get { return state; } }
        public int Percent { get { return percent; } }

        public void SetState(TileState s, int pct, string text)
        {
            state = s;
            if (pct >= 0) percent = Math.Max(0, Math.Min(100, pct));
            if (text != null) detail = text;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; powerHover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool over = PowerRect.Contains(e.Location);
            if (over != powerHover)
            {
                powerHover = over;
                tip.SetToolTip(this, over ? "Shut down / restart this PC" : tooltip);
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && PowerRect.Contains(e.Location)))
            {
                MouseEventHandler p = PowerMenuRequested;
                if (p != null) p(this, new MouseEventArgs(e.Button, 1, e.Button == MouseButtons.Right ? e.X : PowerRect.Left, e.Button == MouseButtons.Right ? e.Y : PowerRect.Bottom, 0));
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            EventHandler h = TickToggled;
            if (h != null) h(this, EventArgs.Empty);
        }

        void DrawPowerButton(Graphics g)
        {
            Rectangle r = PowerRect;
            if (powerHover)
                using (GraphicsPath bp = Round(r, 5))
                using (var b = new SolidBrush(Color.FromArgb(253, 228, 228))) g.FillPath(b, bp);
            Color c = powerHover ? CError : Color.FromArgb(120, 128, 140);
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f + 0.5f, rad = 5.5f;
            using (var p = new Pen(c, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(p, cx - rad, cy - rad, rad * 2, rad * 2, -55, 290);   // ring with a gap at the top
                g.DrawLine(p, cx, cy - rad - 1.5f, cx, cy - 1);                 // the stroke through the gap
            }
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        Color StateColor()
        {
            switch (state)
            {
                case TileState.Waiting: return CWait;
                case TileState.Working: return CWork;
                case TileState.Done: return CDone;
                case TileState.Error: return CError;
                case TileState.Skipped: return CSkip;
                default: return CIdle;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = Width;
            Color col = StateColor();

            // card
            var card = new RectangleF(1, 1, w - 3, Height - 3);
            using (GraphicsPath cp = Round(card, 8))
            {
                using (var b = new SolidBrush(hover ? Color.FromArgb(240, 245, 252) : Color.White)) g.FillPath(b, cp);
                using (var pen = new Pen(hover ? Color.FromArgb(150, 180, 220) : Color.FromArgb(218, 222, 228))) g.DrawPath(pen, cp);
            }

            // monitor
            var screenOuter = new RectangleF((w - 84) / 2f, 14, 84, 58);
            using (GraphicsPath sp = Round(screenOuter, 5))
            using (var b = new SolidBrush(CFrame)) g.FillPath(b, sp);
            var screen = RectangleF.Inflate(screenOuter, -5, -5);
            using (var b = new SolidBrush(col)) g.FillRectangle(b, screen);
            DrawScreen(g, screen);
            // stand
            float cx = w / 2f;
            using (var b = new SolidBrush(CFrame))
            {
                g.FillPolygon(b, new[] { new PointF(cx - 7, screenOuter.Bottom), new PointF(cx + 7, screenOuter.Bottom),
                                         new PointF(cx + 10, screenOuter.Bottom + 10), new PointF(cx - 10, screenOuter.Bottom + 10) });
                using (GraphicsPath bp = Round(new RectangleF(cx - 20, screenOuter.Bottom + 9, 40, 5), 2)) g.FillPath(b, bp);
            }

            // PC number
            using (var f = new Font(Font.FontFamily, 10.5f, FontStyle.Bold))
            using (var b = new SolidBrush(Color.FromArgb(30, 34, 40)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString(number, f, b, new RectangleF(0, 96, w, 20), sf);

            // progress bar
            var track = new RectangleF(12, 119, w - 24, 9);
            using (GraphicsPath tp = Round(track, 4.5f))
            using (var b = new SolidBrush(CTrack)) g.FillPath(b, tp);
            DrawBar(g, track, col);

            // status text
            using (var f = new Font(Font.FontFamily, 7.5f))
            using (var b = new SolidBrush(Color.FromArgb(95, 102, 112)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(detail, f, b, new RectangleF(4, 131, w - 8, 15), sf);

            // tick box
            CheckBoxRenderer.DrawCheckBox(g, new Point(7, 6),
                ticked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal);

            // unticked PCs are faded
            if (!ticked)
                using (GraphicsPath cp = Round(card, 8))
                using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.FillPath(b, cp);

            // ⏻ stays clear even on unticked PCs - it works on that one PC whether it's ticked or not
            DrawPowerButton(g);
        }

        void DrawScreen(Graphics g, RectangleF s)
        {
            GraphicsState saved = g.Save();
            g.SetClip(s);
            if (state == TileState.Working)
            {
                // moving diagonal stripes + percent
                float off = (phase * 1.5f) % 16f;
                using (var b = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))
                    for (float x = s.Left - s.Height - 16 + off; x < s.Right; x += 16)
                        g.FillPolygon(b, new[] { new PointF(x, s.Bottom), new PointF(x + 8, s.Bottom), new PointF(x + 8 + s.Height, s.Top), new PointF(x + s.Height, s.Top) });
                using (var f = new Font(Font.FontFamily, 11f, FontStyle.Bold))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(percent + "%", f, Brushes.White, s, sf);
            }
            else if (state == TileState.Waiting)
            {
                // three pulsing dots
                for (int i = 0; i < 3; i++)
                {
                    double t = Math.Sin((phase * 0.15) - i * 0.9);
                    int a = (int)(140 + 115 * t);
                    using (var b = new SolidBrush(Color.FromArgb(Math.Max(40, Math.Min(255, a)), 255, 255, 255)))
                        g.FillEllipse(b, s.Left + s.Width / 2f - 16 + i * 13, s.Top + s.Height / 2f - 4, 8, 8);
                }
            }
            else if (state == TileState.Done)
            {
                using (var p = new Pen(Color.White, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    float x = s.Left + s.Width / 2f, y = s.Top + s.Height / 2f;
                    g.DrawLines(p, new[] { new PointF(x - 12, y), new PointF(x - 3, y + 9), new PointF(x + 13, y - 9) });
                }
            }
            else if (state == TileState.Error)
            {
                using (var p = new Pen(Color.White, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float x = s.Left + s.Width / 2f, y = s.Top + s.Height / 2f;
                    g.DrawLine(p, x - 9, y - 9, x + 9, y + 9);
                    g.DrawLine(p, x + 9, y - 9, x - 9, y + 9);
                }
            }
            else if (state == TileState.Skipped)
            {
                using (var p = new Pen(Color.White, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float x = s.Left + s.Width / 2f, y = s.Top + s.Height / 2f;
                    g.DrawLine(p, x - 10, y, x + 10, y);
                }
            }
            g.Restore(saved);
        }

        void DrawBar(Graphics g, RectangleF track, Color col)
        {
            RectangleF fill;
            if (state == TileState.Working && percent == 0)
            {
                // not started yet: a block sliding back and forth
                float bw = track.Width * 0.3f;
                float t = (float)((Math.Sin(phase * 0.08) + 1) / 2);
                fill = new RectangleF(track.X + t * (track.Width - bw), track.Y, bw, track.Height);
            }
            else
            {
                float p = state == TileState.Done ? 100 : state == TileState.Idle || state == TileState.Waiting ? 0 : percent;
                if (state == TileState.Error || state == TileState.Skipped) p = Math.Max(p, 100);
                if (p <= 0) return;
                fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * p / 100f), track.Height);
            }
            using (GraphicsPath fp = Round(fill, 4.5f))
            using (var b = new SolidBrush(col)) g.FillPath(b, fp);
        }
    }
}
