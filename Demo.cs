using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TabletSwitch
{
    public static class DemoEnvironment
    {
        public const string TabletName = "演示数位板";

        public static List<DisplayTarget> GetDisplays()
        {
            return new List<DisplayTarget>
            {
                new DisplayTarget { Id = "demo-left", DeviceName = "DEMO1", Label = "左侧横屏", Bounds = new Rectangle(-1920, 0, 1920, 1080) },
                new DisplayTarget { Id = "demo-main", DeviceName = "DEMO2", Label = "主屏幕", Bounds = new Rectangle(0, 0, 2560, 1440), Primary = true },
                new DisplayTarget { Id = "demo-portrait", DeviceName = "DEMO3", Label = "右侧竖屏", Bounds = new Rectangle(2560, -240, 1080, 1920) }
            };
        }
    }

    // Entirely in-memory adapter. It does not launch processes, inspect devices, or send input.
    public sealed class DemoClient : IOtdClient
    {
        private DisplayArea area;

        public DemoClient()
        {
            List<DisplayTarget> displays = DemoEnvironment.GetDisplays();
            area = SwitchService.AreaFor(displays[1], displays);
        }

        public Task CheckAsync(string executable)
        {
            RequireDemo(executable);
            return Task.FromResult(0);
        }

        public Task<OtdSnapshot> SnapshotAsync(string executable)
        {
            RequireDemo(executable);
            var payload = new
            {
                Profiles = new[]
                {
                    new
                    {
                        Tablet = DemoEnvironment.TabletName,
                        OutputMode = new { Path = "VoiDPlugins.OutputMode.WinInkAbsoluteMode" },
                        AbsoluteModeSettings = new { Display = new { Width = area.Width, Height = area.Height, X = area.X, Y = area.Y } }
                    }
                }
            };
            return Task.FromResult(OtdSnapshot.Parse(SettingsStore.Json().Serialize(payload)));
        }

        public Task SetAreaAsync(string executable, string tablet, DisplayArea value)
        {
            RequireDemo(executable);
            if (tablet != DemoEnvironment.TabletName) throw new InvalidOperationException("演示只支持模拟数位板。");
            if (value == null || value.Width <= 0 || value.Height <= 0) throw new ArgumentException("模拟显示区域无效。");
            area = new DisplayArea { Width = value.Width, Height = value.Height, X = value.X, Y = value.Y };
            return Task.FromResult(0);
        }

        private static void RequireDemo(string executable)
        {
            if (executable != "DEMO") throw new InvalidOperationException("模拟适配器只能用于演示。");
        }
    }

    internal sealed class DemoTabletView : Control
    {
        private PointF penPosition = new PointF(0.5F, 0.5F);
        public event Action<float, float> PenMoved;

        public DemoTabletView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Cross;
            AccessibleName = "模拟数位板感应区";
            AccessibleDescription = "在此处移动鼠标，橙色笔点会映射到选中的模拟屏幕。";
        }

        public PointF PenPosition
        {
            get { return penPosition; }
            set { penPosition = value; Invalidate(); }
        }

        private RectangleF ActiveArea
        {
            get
            {
                float dpi;
                using (Graphics graphics = CreateGraphics()) dpi = graphics.DpiX / 96F;
                return new RectangleF(18 * dpi, 14 * dpi, Math.Max(1, Width - 36 * dpi), Math.Max(1, Height - 48 * dpi));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            RectangleF active = ActiveArea;
            if (!active.Contains(e.Location)) return;
            float x = Math.Max(0, Math.Min(1, (e.X - active.Left) / active.Width));
            float y = Math.Max(0, Math.Min(1, (e.Y - active.Top) / active.Height));
            if (PenMoved != null) PenMoved(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            float dpi = g.DpiX / 96F;
            RectangleF body = new RectangleF(0.5F, 0.5F, Width - 1, Height - 1);
            using (LinearGradientBrush surface = new LinearGradientBrush(body, Color.FromArgb(49, 53, 61), Color.FromArgb(28, 31, 38), 80F))
                Palette.FillRound(g, surface, body, 13 * dpi);
            RectangleF area = ActiveArea;
            using (SolidBrush surface = new SolidBrush(Color.FromArgb(43, 47, 55))) Palette.FillRound(g, surface, area, 5 * dpi);
            using (Pen edge = new Pen(Color.FromArgb(72, 77, 88)))
            using (GraphicsPath path = Palette.Rounded(area, 5 * dpi)) g.DrawPath(edge, path);
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(86, 92, 103)))
                for (float x = area.Left + 13 * dpi; x < area.Right - 6 * dpi; x += 18 * dpi)
                    for (float y = area.Top + 12 * dpi; y < area.Bottom - 5 * dpi; y += 18 * dpi)
                        g.FillEllipse(dot, x, y, dpi, dpi);
            float penX = area.X + area.Width * penPosition.X;
            float penY = area.Y + area.Height * penPosition.Y;
            using (Pen cross = new Pen(Color.FromArgb(99, 110, 128), dpi))
            {
                g.DrawLine(cross, penX - 12 * dpi, penY, penX + 12 * dpi, penY);
                g.DrawLine(cross, penX, penY - 12 * dpi, penX, penY + 12 * dpi);
            }
            using (SolidBrush halo = new SolidBrush(Color.FromArgb(35, 255, 159, 10))) g.FillEllipse(halo, penX - 13 * dpi, penY - 13 * dpi, 26 * dpi, 26 * dpi);
            using (SolidBrush marker = new SolidBrush(Color.FromArgb(255, 159, 10))) g.FillEllipse(marker, penX - 4 * dpi, penY - 4 * dpi, 8 * dpi, 8 * dpi);
            using (Pen ring = new Pen(Color.White, 1.2F * dpi)) g.DrawEllipse(ring, penX - 6 * dpi, penY - 6 * dpi, 12 * dpi, 12 * dpi);
            string caption = "PEN  /  X " + Math.Round(penPosition.X * 100) + "%    Y " + Math.Round(penPosition.Y * 100) + "%";
            using (Font captionFont = new Font("Segoe UI", 8F))
                TextRenderer.DrawText(g, caption, captionFont, new Rectangle((int)(4 * dpi), Height - (int)(29 * dpi), Width - (int)(8 * dpi), (int)(23 * dpi)), Color.FromArgb(170, 178, 193), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
