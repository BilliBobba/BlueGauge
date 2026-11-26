using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BlueGauge
{
    internal static class Palette
    {
        internal static readonly Color Back = Color.FromArgb(12, 19, 32);
        internal static readonly Color Surface = Color.FromArgb(19, 30, 48);
        internal static readonly Color Raised = Color.FromArgb(26, 40, 61);
        internal static readonly Color Border = Color.FromArgb(41, 59, 83);
        internal static readonly Color Main = Color.FromArgb(240, 246, 255);
        internal static readonly Color Muted = Color.FromArgb(166, 184, 210);
        internal static readonly Color Cyan = Color.FromArgb(93, 224, 226);
        internal static readonly Color Low = Color.FromArgb(255, 179, 105);
        internal static Color Level(int? percent, int threshold) => !percent.HasValue ? Muted : percent.Value <= threshold ? Low : Cyan;
    }

    internal static class TrayArtwork
    {
        internal static Icon Create(int? percent, int threshold)
        {
            using (var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var brush = new SolidBrush(Palette.Back)) g.FillRounded(brush, new RectangleF(0, 0, 32, 32), 7);
                using (var pen = new Pen(Palette.Border)) g.DrawRounded(pen, new RectangleF(0.5f, 0.5f, 31, 31), 7);
                using (var brush = new SolidBrush(Palette.Level(percent, threshold)))
                using (var font = new Font("Segoe UI", percent == 100 ? 14f : 19f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(percent?.ToString() ?? "–", font, brush, new RectangleF(0, 0, 32, 28), format);
                    if (percent.HasValue && percent.Value > 0) g.FillRectangle(brush, 4, 27, Math.Max(1f, 24f * percent.Value / 100f), 2);
                }
                IntPtr native = bitmap.GetHicon();
                try { using (var borrowed = Icon.FromHandle(native)) return (Icon)borrowed.Clone(); }
                finally { DestroyIcon(native); }
            }
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    }

    internal static class DrawingExtensions
    {
        private static GraphicsPath Round(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float d = 2 * radius;
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
        internal static void FillRounded(this Graphics g, Brush brush, RectangleF bounds, float radius)
        { using (var shape = Round(bounds, radius)) g.FillPath(brush, shape); }
        internal static void DrawRounded(this Graphics g, Pen pen, RectangleF bounds, float radius)
        { using (var shape = Round(bounds, radius)) g.DrawPath(pen, shape); }
    }
}
