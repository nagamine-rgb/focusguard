using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FocusGuard
{
    /// <summary>
    /// 霧の朝デザインの配色・フォント・部品。Lv.3 ロック中は赤みのある配色に切り替わる。
    /// </summary>
    public static class Theme
    {
        public static bool Locked { get; private set; }

        private static readonly Color NGround = Color.FromArgb(228, 232, 225);
        private static readonly Color NPaper = Color.FromArgb(243, 245, 241);
        private static readonly Color NWell = Color.FromArgb(221, 227, 218);
        private static readonly Color NSub = Color.FromArgb(77, 90, 82);
        private static readonly Color NLine = Color.FromArgb(185, 194, 182);
        private static readonly Color NTrack = Color.FromArgb(205, 212, 202);
        private static readonly Color NTick = Color.FromArgb(154, 165, 151);
        private static readonly Color NSched = Color.FromArgb(127, 140, 124);

        private static readonly Color LGround = Color.FromArgb(236, 226, 223);
        private static readonly Color LPaper = Color.FromArgb(247, 240, 238);
        private static readonly Color LWell = Color.FromArgb(230, 217, 213);
        private static readonly Color LSub = Color.FromArgb(90, 71, 68);
        private static readonly Color LLine = Color.FromArgb(201, 180, 176);
        private static readonly Color LTrack = Color.FromArgb(220, 203, 199);
        private static readonly Color LTick = Color.FromArgb(184, 162, 158);
        private static readonly Color LSched = Color.FromArgb(168, 146, 142);

        public static readonly Color Ink = Color.FromArgb(28, 38, 32);
        public static readonly Color White = Color.FromArgb(255, 255, 255);
        public static readonly Color Ok = Color.FromArgb(46, 107, 74);

        public static Color Ground { get { return Locked ? LGround : NGround; } }
        public static Color Paper { get { return Locked ? LPaper : NPaper; } }
        public static Color Well { get { return Locked ? LWell : NWell; } }
        public static Color Sub { get { return Locked ? LSub : NSub; } }
        public static Color Line { get { return Locked ? LLine : NLine; } }
        public static Color Track { get { return Locked ? LTrack : NTrack; } }
        public static Color Tick { get { return Locked ? LTick : NTick; } }
        public static Color Sched { get { return Locked ? LSched : NSched; } }

        public static Color Lv(int level)
        {
            if (level == 1) return Color.FromArgb(217, 162, 27);
            if (level == 2) return Color.FromArgb(180, 80, 30);
            return Color.FromArgb(142, 36, 36);
        }

        public static Color LvText(int level)
        {
            if (level == 1) return Color.FromArgb(110, 82, 0);
            if (level == 2) return Color.FromArgb(138, 58, 20);
            return Color.FromArgb(142, 36, 36);
        }

        public static string LvName(int level)
        {
            if (level == 1) return "気づき";
            if (level == 2) return "ひと手間";
            return "強制";
        }

        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();

        public static Font UI(float size, bool bold = false)
        {
            return GetFont("Yu Gothic UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        }

        public static Font Serif(float size)
        {
            return GetFont("Yu Mincho", size, FontStyle.Bold);
        }

        private static Font GetFont(string family, float size, FontStyle style)
        {
            string key = family + "|" + size + "|" + style;
            Font f;
            if (!FontCache.TryGetValue(key, out f))
            {
                f = new Font(family, size, style);
                FontCache[key] = f;
            }
            return f;
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>配色を切り替え、画面上のすべての部品の色を置き換える</summary>
        public static void Apply(Control root, bool locked)
        {
            if (Locked == locked) return;
            var from = new[] { Ground, Paper, Well, Sub, Line };
            Locked = locked;
            var to = new[] { Ground, Paper, Well, Sub, Line };
            Swap(root, from, to);
            root.Invalidate(true);
        }

        private static Color Map(Color c, Color[] from, Color[] to)
        {
            for (int i = 0; i < from.Length; i++)
                if (c.ToArgb() == from[i].ToArgb()) return to[i];
            return c;
        }

        private static void Swap(Control c, Color[] from, Color[] to)
        {
            c.BackColor = Map(c.BackColor, from, to);
            c.ForeColor = Map(c.ForeColor, from, to);
            var rp = c as RoundPanel;
            if (rp != null) rp.Fill = Map(rp.Fill, from, to);
            foreach (Control child in c.Controls) Swap(child, from, to);
        }
    }

    /// <summary>角丸の面。子の背景色は Fill に合わせる。</summary>
    public class RoundPanel : Panel
    {
        private Color _fill = Color.White;
        public float Radius = 18f;

        public Color Fill
        {
            get { return _fill; }
            set { _fill = value; Invalidate(); }
        }

        public RoundPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), Radius))
            using (var b = new SolidBrush(_fill))
                e.Graphics.FillPath(b, path);
        }
    }

    public enum PillKind { Primary, Line, Strong, Chip, Segment }

    /// <summary>角丸のボタン。種類ごとに描き分け、キーボードでも押せる。</summary>
    public class PillButton : Control
    {
        public PillKind Kind = PillKind.Line;
        private bool _selected;
        private Color _accent = Color.Empty;
        private bool _hover;

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; AccessibleDescription = value ? "選択中" : null; Invalidate(); }
        }

        public Color Accent
        {
            get { return _accent; }
            set { _accent = value; Invalidate(); }
        }

        public PillButton(string text, PillKind kind)
        {
            Text = text;
            Kind = kind;
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = text;
            Font = Theme.UI(kind == PillKind.Primary ? 11f : 10f, kind == PillKind.Primary || kind == PillKind.Strong);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AccessibleName = Text; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                e.Handled = true;
                OnClick(EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            Focus();
            base.OnClick(e);
        }

        protected virtual float CornerRadius
        {
            get { return Kind == PillKind.Chip ? Height / 2f : (Kind == PillKind.Segment ? 9f : 14f); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var rect = new RectangleF(1, 1, Width - 3, Height - 3);
            Color text = Theme.Ink;
            FontStyle style = Font.Style;

            using (var path = Theme.Rounded(rect, CornerRadius))
            {
                switch (Kind)
                {
                    case PillKind.Primary:
                        if (Enabled)
                        {
                            Color accent = _accent.IsEmpty ? Theme.Sched : _accent;
                            using (var b = new SolidBrush(accent)) g.FillPath(b, path);
                            g.SetClip(path);
                            using (var b = new SolidBrush(_hover ? Color.FromArgb(44, 58, 50) : Theme.Ink))
                                g.FillRectangle(b, rect.X, rect.Y, rect.Width, rect.Height - 4);
                            g.ResetClip();
                            text = Theme.Paper;
                        }
                        else
                        {
                            using (var b = new SolidBrush(Theme.Line)) g.FillPath(b, path);
                            text = Theme.Sub;
                        }
                        break;
                    case PillKind.Strong:
                        using (var b = new SolidBrush(Theme.White)) g.FillPath(b, path);
                        using (var p = new Pen(Theme.Ink, 2f)) g.DrawPath(p, path);
                        break;
                    case PillKind.Chip:
                        if (_selected)
                        {
                            using (var b = new SolidBrush(Theme.Ink)) g.FillPath(b, path);
                            text = Theme.Paper;
                            style = FontStyle.Bold;
                        }
                        else
                        {
                            if (_hover) using (var b = new SolidBrush(Theme.White)) g.FillPath(b, path);
                            using (var p = new Pen(Theme.Line, 1f)) g.DrawPath(p, path);
                        }
                        break;
                    case PillKind.Segment:
                        if (_selected)
                        {
                            using (var b = new SolidBrush(Theme.White)) g.FillPath(b, path);
                            style = FontStyle.Bold;
                        }
                        break;
                    default:
                        if (_hover && Enabled) using (var b = new SolidBrush(Theme.White)) g.FillPath(b, path);
                        using (var p = new Pen(Theme.Line, 1f))
                        {
                            if (!Enabled) p.DashStyle = DashStyle.Dash;
                            g.DrawPath(p, path);
                        }
                        if (!Enabled) text = Theme.Sub;
                        else if (ForeColor.ToArgb() != Theme.Ink.ToArgb() && ForeColor != Control.DefaultForeColor) text = ForeColor;
                        break;
                }
                if (Focused && ShowFocusCues)
                {
                    using (var p = new Pen(Theme.Ink, 2f) { DashStyle = DashStyle.Dot })
                    using (var fp = Theme.Rounded(new RectangleF(3, 3, Width - 7, Height - 7), Math.Max(2f, CornerRadius - 2)))
                        g.DrawPath(p, fp);
                }
            }
            PaintContent(g, text, style);
        }

        protected virtual void PaintContent(Graphics g, Color text, FontStyle style)
        {
            Font f = Theme.UI(Font.Size, style == FontStyle.Bold);
            TextRenderer.DrawText(g, Text, f, new Rectangle(4, 0, Width - 8, Height - (Kind == PillKind.Primary ? 3 : 0)), text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>強さの選択ボタン（棒グラフの高さで強さを表す）</summary>
    public class LevelButton : PillButton
    {
        public readonly int Level;

        public LevelButton(int level) : base(Theme.LvName(level), PillKind.Line)
        {
            Level = level;
            Font = Theme.UI(10.5f, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var rect = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Rounded(rect, 14f))
            {
                if (Selected) using (var b = new SolidBrush(Theme.White)) g.FillPath(b, path);
                using (var p = new Pen(Selected ? Theme.Ink : Theme.Line, 2f)) g.DrawPath(p, path);
            }
            int[] heights = { 10, 18, 26 };
            for (int i = 0; i < 3; i++)
            {
                bool on = Selected && i < Level;
                using (var b = new SolidBrush(on ? Theme.Lv(Level) : Color.FromArgb(195, 203, 192)))
                    g.FillRectangle(b, 14 + i * 11, 14 + 26 - heights[i], 8, heights[i]);
            }
            TextRenderer.DrawText(g, Text, Font, new Point(12, Height - 30), Theme.Ink);
            if (Focused && ShowFocusCues)
            {
                using (var p = new Pen(Theme.Ink, 2f) { DashStyle = DashStyle.Dot })
                using (var fp = Theme.Rounded(new RectangleF(4, 4, Width - 9, Height - 9), 11f))
                    g.DrawPath(p, fp);
            }
        }
    }

    /// <summary>オン・オフのスイッチ</summary>
    public class SwitchToggle : PillButton
    {
        public SwitchToggle() : base("", PillKind.Line)
        {
            Size = new Size(52, 32);
            AccessibleRole = AccessibleRole.CheckButton;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f))
            using (var b = new SolidBrush(Selected ? Theme.Ink : Theme.Line))
                g.FillPath(b, path);
            float x = Selected ? Width - 28 : 4;
            using (var b = new SolidBrush(Theme.Paper)) g.FillEllipse(b, x, 4, 23, 23);
            if (Focused && ShowFocusCues)
                using (var p = new Pen(Theme.Ink, 1.5f) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>24時間の文字盤</summary>
    public class DialControl : Control
    {
        public struct Arc
        {
            public double StartMin;
            public double LenMin;
            public Color Color;
            public Arc(double start, double len, Color color) { StartMin = start; LenMin = len; Color = color; }
        }

        public readonly List<Arc> Arcs = new List<Arc>();
        public double NowMin;
        public string CenterLabel = "";
        public string CenterTime = "";
        public bool CenterTimeSmall;
        public string CenterState = "";
        public Color CenterStateColor = Theme.Ink;
        public bool ShowLock;

        public DialControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Size = new Size(300, 300);
            AccessibleRole = AccessibleRole.Graphic;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            float s = Math.Min(Width, Height) / 300f;
            g.ScaleTransform(s, s);

            using (var p = new Pen(Theme.Tick, 6f))
                for (int h = 0; h < 24; h++)
                    g.DrawArc(p, 14, 14, 272, 272, -90 + h * 15 - 0.6f, 1.2f);

            var ring = new RectangleF(30, 30, 240, 240);
            using (var p = new Pen(Theme.Track, 18f)) g.DrawEllipse(p, ring);
            foreach (var a in Arcs)
            {
                if (a.LenMin <= 0) continue;
                float start = (float)(-90 + (a.StartMin % 1440) / 1440.0 * 360);
                float sweep = (float)(Math.Min(a.LenMin, 1440) / 1440.0 * 360);
                using (var p = new Pen(a.Color, 18f)) g.DrawArc(p, ring, start, Math.Max(0.5f, sweep));
            }

            double th = NowMin / 1440.0 * 2 * Math.PI;
            float mx = (float)(150 + 120 * Math.Sin(th));
            float my = (float)(150 - 120 * Math.Cos(th));
            using (var b = new SolidBrush(Theme.Paper)) g.FillEllipse(b, mx - 7, my - 7, 14, 14);
            using (var p = new Pen(Theme.Ink, 2.5f)) g.DrawEllipse(p, mx - 7, my - 7, 14, 14);

            var hourFont = Theme.UI(8.5f);
            using (var b = new SolidBrush(Theme.Sub))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("0", hourFont, b, 150, 54, sf);
                g.DrawString("6", hourFont, b, 246, 150, sf);
                g.DrawString("12", hourFont, b, 150, 246, sf);
                g.DrawString("18", hourFont, b, 54, 150, sf);

                float y = 150;
                var timeFont = CenterTimeSmall ? Theme.Serif(20f) : Theme.Serif(36f);
                float timeH = g.MeasureString(CenterTime.Length > 0 ? CenterTime : "0", timeFont).Height;
                float total = (ShowLock ? 28 : 0) + 20 + timeH + 20;
                y = 150 - total / 2;
                if (ShowLock)
                {
                    using (var p = new Pen(Theme.Lv(3), 2.2f))
                    {
                        g.DrawRectangle(p, 141, y + 10, 18, 13);
                        g.DrawArc(p, 144, y + 1, 12, 16, 180, 180);
                    }
                    y += 28;
                }
                g.DrawString(CenterLabel, Theme.UI(9f), b, 150, y + 10, sf);
                y += 20;
                using (var ib = new SolidBrush(Theme.Ink))
                    g.DrawString(CenterTime, timeFont, ib, 150, y + timeH / 2, sf);
                y += timeH;
                using (var cb = new SolidBrush(CenterStateColor))
                    g.DrawString(CenterState, Theme.UI(9f, true), cb, new RectangleF(60, y, 180, 22), sf);
            }
        }
    }

    /// <summary>スケジュール1件の行（名前・内容・24時間バー・スイッチ・削除）</summary>
    public class ScheduleRow : Control
    {
        public readonly ScheduleRule Rule;
        public readonly SwitchToggle Switch;
        public readonly PillButton Delete;

        public ScheduleRow(ScheduleRule rule, int width)
        {
            Rule = rule;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Size = new Size(width, 112);
            Margin = new Padding(0, 0, 0, 10);

            Switch = new SwitchToggle { Location = new Point(width - 116, 16), Selected = rule.enabled };
            Switch.AccessibleName = (rule.name ?? "スケジュール") + "を有効にする";
            Delete = new PillButton("削除", PillKind.Line) { Location = new Point(width - 56, 12), Size = new Size(44, 40), Font = Theme.UI(9f) };
            Delete.AccessibleName = (rule.name ?? "スケジュール") + "を削除";
            Controls.Add(Switch);
            Controls.Add(Delete);
        }

        private static int ParseMin(string s)
        {
            TimeSpan ts;
            return TimeSpan.TryParse(s ?? "", out ts) ? (int)ts.TotalMinutes : -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 14f))
            using (var b = new SolidBrush(Theme.Paper))
                g.FillPath(b, path);

            Color main = Rule.enabled ? Theme.Ink : Theme.Sub;
            TextRenderer.DrawText(g, Rule.name ?? "無題", Theme.UI(11f, true), new Rectangle(14, 12, Width - 140, 24), main, TextFormatFlags.EndEllipsis);

            string[] dn = { "日", "月", "火", "水", "木", "金", "土" };
            var days = Rule.days ?? new List<int>();
            string daysStr = days.Count == 7 ? "毎日" : string.Join("・", days.FindAll(d => d >= 0 && d <= 6).ConvertAll(d => dn[d]).ToArray());
            string tgt = Rule.target == "pc" ? "PCだけ" : (Rule.target == "android" ? "スマホだけ" : "両方");
            string meta = string.Format("{0} {1}〜{2}、{3}を Lv.{4} で", daysStr, Rule.startTime, Rule.endTime, tgt, Rule.level);
            TextRenderer.DrawText(g, meta, Theme.UI(9f), new Rectangle(14, 38, Width - 140, 20), Theme.Sub, TextFormatFlags.EndEllipsis);

            var bar = new RectangleF(14, 68, Width - 28, 8);
            using (var path = Theme.Rounded(bar, 4f))
            {
                using (var b = new SolidBrush(Theme.Well)) g.FillPath(b, path);
                int sm = ParseMin(Rule.startTime), em = ParseMin(Rule.endTime);
                if (sm >= 0 && em >= 0)
                {
                    g.SetClip(path);
                    Color c = Rule.enabled ? Theme.Lv(Rule.level) : Theme.Line;
                    using (var b = new SolidBrush(c))
                    {
                        Func<int, float> x = m => bar.X + bar.Width * m / 1440f;
                        if (sm == em) g.FillRectangle(b, bar);
                        else if (sm < em) g.FillRectangle(b, x(sm), bar.Y, x(em) - x(sm), bar.Height);
                        else
                        {
                            g.FillRectangle(b, bar.X, bar.Y, x(em) - bar.X, bar.Height);
                            g.FillRectangle(b, x(sm), bar.Y, bar.Right - x(sm), bar.Height);
                        }
                    }
                    g.ResetClip();
                }
            }
            string[] scale = { "0時", "6時", "12時", "18時", "24時" };
            for (int i = 0; i < 5; i++)
            {
                float cx = bar.X + bar.Width * i / 4f;
                var flags = i == 0 ? TextFormatFlags.Left : (i == 4 ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter);
                var r = i == 0 ? new Rectangle((int)bar.X, 82, 60, 18) : (i == 4 ? new Rectangle((int)bar.Right - 60, 82, 60, 18) : new Rectangle((int)cx - 30, 82, 60, 18));
                TextRenderer.DrawText(g, scale[i], Theme.UI(8f), r, Theme.Sub, flags);
            }
        }
    }
}
