using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BlueGauge;

// An independent non-activating window over the unused taskbar area.
internal sealed class TaskbarPanel : Form
{
    private static readonly Color Surface = Color.FromArgb(18, 30, 46);
    private static readonly Color Good = Color.FromArgb(108, 224, 174);
    private static readonly Color Critical = Color.FromArgb(255, 137, 139);
    private const int EdgePadding = 8, Gap = 10, LabelOffset = 19;
    private readonly ToolTip tooltip = new();
    private readonly Action open;
    private BatteryDevice[] devices = [];
    private int threshold;
    private bool dirty = true;
    private Rectangle lastBounds;
    private int lastDpi;

    internal TaskbarPanel(Action open, bool demo)
    {
        this.open = open;
        Text = demo ? "BlueGauge · Demo taskbar panel" : "BlueGauge · Taskbar panel";
        AccessibleName = L.T("BlueGauge battery panel", "Панель заряда BlueGauge");
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Cursor = Cursors.Hand;
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08080080; // WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TOOLWINDOW
            return parameters;
        }
    }

    internal void UpdateDevices(BatteryDevice[] readings, int lowThreshold)
    {
        if (threshold == lowThreshold && devices.Length == readings.Length && devices.Zip(readings).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name && pair.First.LevelText == pair.Second.LevelText
            && pair.First.Kind == pair.Second.Kind && pair.First.Charging == pair.Second.Charging)) return;
        devices = readings;
        threshold = lowThreshold;
        string hint = string.Join("\n", devices.Select(d => d.Name + " · " + d.LevelText + (d.UsbPowered
            ? L.T(" (USB power)", " (питание от USB)") : d.IsStale ? L.T(" (last reading)", " (последнее показание)")
            : d.Charging ? L.T(" (charging)", " (заряжается)") : "")));
        tooltip.SetToolTip(this, hint);
        AccessibleDescription = hint;
        dirty = true;
    }

    internal static (int Width, int VisibleItems) Fit(int available, int padding, int gap, IReadOnlyList<int> widths, int overflowWidth)
    {
        if (widths.Count == 0) return (0, 0);
        int all = padding + widths.Sum() + gap * (widths.Count - 1);
        if (available >= all) return (all, widths.Count);
        if (available < padding + overflowWidth) return (0, 0);
        int width = padding + overflowWidth, fit = 0;
        while (fit < widths.Count && width + gap + widths[fit] <= available)
            width += gap + widths[fit++];
        return (width, fit);
    }

    // A premultiplied alpha surface lets DWM blend the curved edge into the actual taskbar,
    // including transparent corners. SetWindowRgn would reduce each pixel to in/out.
    internal Bitmap RenderSurface(TaskbarSlot slot, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        int P(int unit) => TaskbarPlacement.Scale(unit, slot.Dpi);
        var area = slot.Free;
        int height = Math.Min(P(30), area.Height - 2);
        if (height <= 6 || devices.Length == 0) return null;
        float scale = slot.Dpi / 96f;
        using var font = new Font("Segoe UI Semibold", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var measureImage = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
        using var measuring = Graphics.FromImage(measureImage);
        using var format = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center };
        int Measure(string text) => (int)Math.Ceiling(measuring.MeasureString(text, font, PointF.Empty, format).Width) + 1;
        var widths = devices.Select(d => P(LabelOffset) + Measure(d.LevelText)).ToArray();
        var (width, fit) = Fit(area.Width, 2 * P(EdgePadding), P(Gap), widths, Measure("+" + new string('9', devices.Length.ToString().Length)));
        if (width == 0) return null;
        bounds = new Rectangle(area.Right - width, area.Top + (area.Height - height) / 2, width, height);
        var image = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        int radius = Math.Min(width - 2, height - 2) / 2;
        using (var fill = new SolidBrush(Surface))
            g.FillRounded(fill, new RectangleF(1, 1, width - 2, height - 2), radius);
        using (var edge = new Pen(Palette.Border))
            g.DrawRounded(edge, new RectangleF(1, 1, width - 2, height - 2), radius);
        int x = P(EdgePadding);
        for (int index = 0; index < fit; index++)
        {
            var d = devices[index];
            Color color = d.Charging || d.UsbPowered ? Palette.Cyan : d.IsStale || d.Percent is not int level ? Palette.Muted
                : level <= Math.Min(10, threshold) ? Critical : level <= threshold ? Palette.Low : Good;
            Dashboard.DrawDevice(g, d.Kind, x + P(8), height / 2, scale, color);
            using var ink = new SolidBrush(color);
            g.DrawString(d.LevelText, font, ink, new RectangleF(x + P(LabelOffset), 0, widths[index] - P(LabelOffset) + 1, height), format);
            x += widths[index] + P(Gap);
        }
        if (fit < devices.Length)
        {
            using var ink = new SolidBrush(Palette.Main);
            g.DrawString("+" + (devices.Length - fit), font, ink, new RectangleF(x, 0, width - x, height), format);
        }
        return image;
    }

    internal void Place(TaskbarSlot slot)
    {
        if (Visible && !dirty && lastDpi == slot.Dpi && lastBounds.Right == slot.Free.Right
            && slot.Free.Contains(lastBounds) && lastBounds.Top == slot.Free.Top + (slot.Free.Height - lastBounds.Height) / 2
            && lastFreeWidth == slot.Free.Width) return;
        using var image = RenderSurface(slot, out var bounds);
        if (image == null) { Hide(); return; }
        if (!Visible)
        {
            Bounds = bounds;
            Show();
        }
        LayeredSurface.Present(Handle, bounds, image);
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // TOPMOST, NOACTIVATE, NOMOVE, NOSIZE
        lastBounds = bounds;
        lastDpi = slot.Dpi;
        lastFreeWidth = slot.Free.Width;
        dirty = false;
    }

    private int lastFreeWidth;

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
