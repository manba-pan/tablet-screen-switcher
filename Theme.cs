using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TabletSwitch
{
    internal static class Palette
    {
        public static readonly Color Background = Color.FromArgb(245, 245, 247);
        public static readonly Color Ink = Color.FromArgb(29, 29, 31);
        public static readonly Color Muted = Color.FromArgb(116, 116, 123);
        public static readonly Color Blue = Color.FromArgb(0, 122, 255);
        public static readonly Color Line = Color.FromArgb(229, 229, 234);
        public static readonly Color BlueWash = Color.FromArgb(232, 243, 255);

        public static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics graphics, Brush brush, RectangleF rect, float radius)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (GraphicsPath path = Rounded(rect, radius)) graphics.FillPath(brush, path);
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        public int CornerRadius { get; set; }
        public bool DrawBorder { get; set; }
        public RoundedPanel()
        {
            CornerRadius = 18;
            DrawBorder = true;
            BackColor = Color.White;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Palette.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = e.Graphics.DpiX / 96F;
            using (SolidBrush fill = new SolidBrush(BackColor)) Palette.FillRound(e.Graphics, fill, new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), CornerRadius * scale);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!DrawBorder || Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Palette.Rounded(new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), CornerRadius * e.Graphics.DpiX / 96F))
            using (Pen line = new Pen(Palette.Line)) e.Graphics.DrawPath(line, path);
        }
    }

    // A Button subclass retains standard keyboard navigation, accessibility and click semantics.
    internal sealed class RoundedButton : Button
    {
        private bool hovered;
        private bool pressed;
        public bool Primary { get; set; }
        public bool Selected { get; set; }
        public bool Quiet { get; set; }
        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 3 || Height < 3) return;
            Graphics g = e.Graphics;
            g.Clear(Parent == null ? Palette.Background : Parent.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float dpi = g.DpiX / 96F;
            Color fill = Primary ? Palette.Blue : Selected ? Palette.BlueWash : Quiet ? (Parent == null ? Palette.Background : Parent.BackColor) : Color.FromArgb(245, 245, 247);
            Color ink = Primary ? Color.White : Selected ? Palette.Blue : Palette.Ink;
            if (!Enabled) { fill = Color.FromArgb(241, 241, 243); ink = Color.FromArgb(165, 165, 172); }
            else if (pressed) fill = Primary ? Color.FromArgb(0, 99, 223) : Color.FromArgb(217, 228, 240);
            else if (hovered) fill = Primary ? Color.FromArgb(30, 138, 255) : Color.FromArgb(234, 237, 243);
            RectangleF rect = new RectangleF(1, 1, Width - 2, Height - 2);
            using (SolidBrush brush = new SolidBrush(fill)) Palette.FillRound(g, brush, rect, 9 * dpi);
            if (Selected && Enabled)
                using (GraphicsPath path = Palette.Rounded(rect, 9 * dpi))
                using (Pen pen = new Pen(Color.FromArgb(194, 220, 255))) g.DrawPath(pen, path);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            flags |= TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            Rectangle textArea = new Rectangle((int)(10 * dpi), 0, Math.Max(1, Width - (int)(20 * dpi)), Height);
            TextRenderer.DrawText(g, Text, Font, textArea, ink, flags);
            if (Focused && ShowFocusCues)
            {
                using (GraphicsPath path = Palette.Rounded(new RectangleF(3 * dpi, 3 * dpi, Width - 6 * dpi, Height - 6 * dpi), 7 * dpi))
                using (Pen pen = new Pen(Primary ? Color.White : Palette.Blue, dpi)) { pen.DashStyle = DashStyle.Dot; g.DrawPath(pen, path); }
            }
        }
    }

    internal sealed class BrandMark : Control
    {
        public BrandMark()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AccessibleName = "PenBridge";
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(Width, Height) / 256F;
            g.ScaleTransform(s, s);
            using (LinearGradientBrush fill = new LinearGradientBrush(new Rectangle(0, 0, 256, 256), Color.FromArgb(41, 151, 255), Color.FromArgb(0, 100, 230), 90F))
                Palette.FillRound(g, fill, new RectangleF(8, 8, 240, 240), 56);
            using (GraphicsPath rear = Palette.Rounded(new RectangleF(40, 80, 112, 84), 12))
            using (Pen pen = new Pen(Color.FromArgb(128, 255, 255, 255), 10)) g.DrawPath(pen, rear);
            using (GraphicsPath front = Palette.Rounded(new RectangleF(102, 56, 112, 84), 12))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(18, 130, 244)))
            using (Pen pen = new Pen(Color.White, 10))
            {
                g.FillPath(fill, front);
                g.DrawPath(pen, front);
            }
            using (GraphicsPath stylus = new GraphicsPath())
            using (SolidBrush fill = new SolidBrush(Color.White))
            using (Pen edge = new Pen(Color.FromArgb(18, 130, 244), 5))
            {
                stylus.AddPolygon(new PointF[] { new PointF(174, 114), new PointF(191, 131), new PointF(117, 205), new PointF(91, 214), new PointF(100, 188) });
                edge.LineJoin = LineJoin.Round;
                g.FillPath(fill, stylus);
                g.DrawPath(edge, stylus);
            }
            using (Pen pen = new Pen(Color.White, 9))
            using (GraphicsPath cap = new GraphicsPath())
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLine(pen, 100, 188, 117, 205);
                cap.AddLine(173, 114, 183, 104);
                cap.AddBezier(183, 104, 187, 100, 189, 100, 193, 104);
                cap.AddLine(193, 104, 201, 112);
                cap.AddBezier(201, 112, 205, 116, 205, 118, 201, 122);
                cap.AddLine(201, 122, 191, 132);
                g.DrawPath(pen, cap);
            }
        }
    }
}
