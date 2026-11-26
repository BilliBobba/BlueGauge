using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BlueGauge;

// Windows 11 does not expose a taskbar-widget extension point for this area.
// This is our own non-activating window over unused taskbar space, not an Explorer child.
internal sealed class TaskbarPanel : Form
{
    private static readonly Color Surface = Color.FromArgb(18, 30, 46);
    private static readonly Color Good = Color.FromArgb(108, 224, 174);
    private static readonly Color Critical = Color.FromArgb(255, 137, 139);
    private const int ItemWidth = 62, MoreWidth = 36, PaddingWidth = 14;
    private readonly ToolTip tooltip = new();
    private readonly Action open;
    private BatteryDevice[] devices = [];
    private int threshold, dpi = 96, visibleItems;
    private Rectangle lastBounds;

    internal TaskbarPanel(Action open, bool demo)
    {
        this.open = open;
        Text = demo ? "BlueGauge · Demo taskbar panel" : "BlueGauge · Taskbar panel";
        AccessibleName = L.T("BlueGauge battery panel", "Панель заряда BlueGauge");
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Surface;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return parameters;
        }
    }

    internal void UpdateDevices(BatteryDevice[] readings, int lowThreshold)
    {
        if (threshold == lowThreshold && devices.Length == readings.Length && devices.Zip(readings).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name && pair.First.Percent == pair.Second.Percent
            && pair.First.Kind == pair.Second.Kind && pair.First.Charging == pair.Second.Charging
            && pair.First.IsStale == pair.Second.IsStale && pair.First.UsbPowered == pair.Second.UsbPowered)) return;
        devices = readings;
        threshold = lowThreshold;
        string hint = string.Join("\n", devices.Select(d => d.Name + " · " + d.LevelText + (d.UsbPowered
            ? L.T(" (USB power)", " (питание от USB)") : d.IsStale ? L.T(" (last reading)", " (последнее показание)")
            : d.Charging ? L.T(" (charging)", " (заряжается)") : "")));
        tooltip.SetToolTip(this, hint);
        AccessibleDescription = hint;
        Invalidate();
    }

    internal static (int Width, int VisibleItems) Fit(int available, int dpi, int count)
    {
        int pad = TaskbarPlacement.Scale(PaddingWidth, dpi);
        int item = TaskbarPlacement.Scale(ItemWidth, dpi);
        int more = TaskbarPlacement.Scale(MoreWidth, dpi);
        if (count <= 0) return (0, 0);
        int all = pad + item * count;
        if (available >= all) return (all, count);
        if (available < pad + more) return (0, 0);
        int fit = Math.Max(0, (available - pad - more) / item);
        return (pad + fit * item + more, fit);
    }

    internal void Place(TaskbarSlot slot)
    {
        var area = slot.Free;
        var (width, fit) = Fit(area.Width, slot.Dpi, devices.Length);
        int height = Math.Min(TaskbarPlacement.Scale(30, slot.Dpi), area.Height - 2);
        if (width == 0 || height <= 0) { Hide(); return; }
        bool repaint = dpi != slot.Dpi || visibleItems != fit;
        dpi = slot.Dpi;
        visibleItems = fit;
        var bounds = new Rectangle(area.Right - width, area.Top + (area.Height - height) / 2, width, height);
        if (!Visible || bounds != lastBounds)
        {
            lastBounds = bounds;
            SetWindowPos(Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010); // TOPMOST, NOACTIVATE
            using (var capsule = new GraphicsPath())
            {
                float diameter = Math.Min(Width, Height);
                capsule.AddArc(0, 0, diameter, diameter, 90, 180);
                capsule.AddArc(Width - diameter, 0, diameter, diameter, 270, 180);
                capsule.CloseFigure();
                Region = new Region(capsule); // Clip the actual HWND, including hit-testing, not just its paint.
            }
            if (!Visible) Show();
        }
        if (repaint) Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Surface);
        using var edge = new Pen(Palette.Border);
        g.DrawRounded(edge, new RectangleF(.5f, .5f, Width - 2, Height - 2), (Height - 2) / 2f);
        float scale = dpi / 96f;
        int P(int unit) => TaskbarPlacement.Scale(unit, dpi);
        int x = P(PaddingWidth) / 2;
        using var font = new Font("Segoe UI Semibold", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        foreach (var d in devices.Take(visibleItems))
        {
            Color color = d.Charging || d.UsbPowered ? Palette.Cyan : d.IsStale || d.Percent is not int level ? Palette.Muted
                : level <= Math.Min(10, threshold) ? Critical : level <= threshold ? Palette.Low : Good;
            Dashboard.DrawDevice(g, d.Kind, x + P(9), Height / 2, scale, color);
            TextRenderer.DrawText(g, d.LevelText, font, new Rectangle(x + P(21), 0, P(ItemWidth - 21), Height), color, flags);
            x += P(ItemWidth);
        }
        if (visibleItems < devices.Length)
            TextRenderer.DrawText(g, "+" + (devices.Length - visibleItems), font, new Rectangle(x, 0, P(MoreWidth), Height),
                Palette.Main, flags | TextFormatFlags.HorizontalCenter);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left) open();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tooltip.Dispose();
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
