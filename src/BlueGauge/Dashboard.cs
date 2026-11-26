using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BlueGauge
{
    internal sealed class Dashboard : Form
    {
        private static readonly Font Title = new Font("Segoe UI Semibold", 11.5f);
        private static readonly Font Body = new Font("Segoe UI Semibold", 9.5f);
        private static readonly Font Detail = new Font("Segoe UI", 8.5f);
        private static readonly Font SmallBold = new Font("Segoe UI Semibold", 8.25f);
        private static readonly Color PanelColor = Color.FromArgb(21, 32, 50);
        private static readonly Color Outline = Color.FromArgb(36, 53, 75);
        private static readonly Color Tile = Color.FromArgb(22, 48, 70);
        private static readonly Color Aqua = Color.FromArgb(112, 207, 244);
        private static readonly Color Green = Color.FromArgb(84, 220, 170);
        private static readonly Color Red = Color.FromArgb(255, 124, 128);
        private static readonly Color Amber = Color.FromArgb(255, 194, 107);
        private readonly Label caption, footer;
        private readonly FlowLayoutPanel rows;
        private readonly HeaderButton refresh;
        private readonly ToolTip tips = new ToolTip();
        private bool sizing;
        private string signature;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool AllowClose { get; set; }
        internal event EventHandler RefreshRequested;
        internal event EventHandler SettingsRequested;

        internal Dashboard(bool demo)
        {
            SuspendLayout();
            Text = "BlueGauge";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(386, 410);
            BackColor = Palette.Back;
            ForeColor = Palette.Main;
            DoubleBuffered = true;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(13, 10, 13, 10), RowCount = 5, ColumnCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            Controls.Add(layout);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 39));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var logo = new BrandMark { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 7, 10) };
            logo.MouseDown += DragWindow;
            header.Controls.Add(logo, 0, 0);
            var brand = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 1, RowCount = 2 };
            brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            var product = Caption("BlueGauge", Title, Palette.Main);
            product.TextAlign = ContentAlignment.BottomLeft;
            product.MouseDown += DragWindow;
            brand.Controls.Add(product, 0, 0);
            caption = Caption(L.T("Battery monitor", "Монитор заряда"), Detail, Palette.Muted);
            caption.MouseDown += DragWindow;
            brand.Controls.Add(caption, 0, 1);
            header.Controls.Add(brand, 1, 0);
            refresh = new HeaderButton('r', L.T("Refresh", "Обновить")) { Margin = new Padding(2, 2, 2, 13) };
            refresh.Click += (s, e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
            tips.SetToolTip(refresh, refresh.AccessibleName);
            header.Controls.Add(refresh, 2, 0);
            var close = new HeaderButton('x', L.T("Hide in tray", "Свернуть в трей")) { Margin = new Padding(2, 2, 1, 13) };
            close.Click += (s, e) => Close();
            tips.SetToolTip(close, close.AccessibleName);
            header.Controls.Add(close, 3, 0);
            header.MouseDown += DragWindow;
            layout.Controls.Add(header, 0, 0);

            var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 1, 0, 4), BackColor = PanelColor };
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var activeTab = new NavButton(L.T("Devices", "Устройства"), true) { Margin = new Padding(3, 3, 1, 3), TabStop = false };
            nav.Controls.Add(activeTab, 0, 0);
            var settings = new NavButton(L.T("Settings", "Настройки"), false) { Margin = new Padding(1, 3, 3, 3), Cursor = Cursors.Hand };
            settings.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
            nav.Controls.Add(settings, 1, 0);
            layout.Controls.Add(nav, 0, 1);

            var section = Caption(L.T("CONNECTED", "ПОДКЛЮЧЕНЫ"), SmallBold, Palette.Muted);
            section.Padding = new Padding(2, 0, 0, 0);
            section.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(section, 0, 2);
            rows = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown };
            rows.ClientSizeChanged += (s, e) => ResizeRows();
            layout.Controls.Add(rows, 0, 3);
            footer = Caption(demo ? L.T("Demo mode", "Деморежим") : L.T("Waiting for data…", "Ожидание данных…"), Detail, Palette.Muted);
            footer.TextAlign = ContentAlignment.BottomLeft;
            layout.Controls.Add(footer, 0, 4);
            Demo = demo;
            ResumeLayout(true);
        }

        private bool Demo { get; }
        internal void SetBusy(bool busy) => refresh.Enabled = !busy;

        internal void UpdateDevices(IReadOnlyList<BatteryDevice> devices, DateTime? lastUpdate, int threshold, string error)
        {
            var visible = BatteryMetrics.VisibleDevices(devices);
            caption.Text = L.T("Active devices: ", "Активных устройств: ") + visible.Length;
            string current = threshold + ":" + string.Join("\n", visible.Select(d => $"{d.Id}|{d.Name}|{d.Kind}|{d.Transport}|{d.LevelText}|{d.Charging}"));
            if (!string.Equals(current, signature, StringComparison.Ordinal))
            {
                signature = current;
                int scroll = -rows.AutoScrollPosition.Y;
                rows.SuspendLayout();
                try
                {
                    foreach (var control in rows.Controls.Cast<Control>().ToArray()) control.Dispose();
                    if (visible.Length == 0)
                    {
                        var empty = Caption(L.T("No connected devices.\nConnect a supported device.", "Нет подключённых устройств.\nПодключи поддерживаемое устройство."), Detail, Palette.Muted);
                        empty.Height = Pixels(89);
                        empty.Dock = DockStyle.None;
                        empty.TextAlign = ContentAlignment.MiddleCenter;
                        empty.BackColor = PanelColor;
                        rows.Controls.Add(empty);
                    }
                    foreach (var device in visible) rows.Controls.Add(new DeviceCard(device, threshold, DeviceDpi));
                    ResizeRows();
                }
                finally { rows.ResumeLayout(true); }
                ResizeRows();
                rows.AutoScrollPosition = new Point(0, scroll);
            }

            string stamp = lastUpdate.HasValue ? lastUpdate.Value.ToString(visible.Any(d => d.Transport != DeviceTransport.Bluetooth) ? "HH:mm:ss" : "HH:mm") : null;
            footer.Text = error ?? (Demo ? L.T("Demo mode", "Деморежим") : visible.Any(d => d.Transport != DeviceTransport.Bluetooth)
                ? L.T("USB · refreshed every 2 s", "USB · опрос каждые 2 с")
                : L.T("Last refresh", "Последний опрос")) + (stamp != null ? "  ·  " + stamp : "");
            footer.ForeColor = error == null ? Palette.Muted : Amber;
        }

        private int Pixels(int logical) => (int)Math.Round(logical * DeviceDpi / 96f);

        private void ResizeRows()
        {
            if (sizing) return;
            sizing = true;
            try
            {
                int contentHeight = rows.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical);
                int width = Math.Max(Pixels(150), rows.Width - (contentHeight > rows.ClientSize.Height ? SystemInformation.VerticalScrollBarWidth + 1 : 0));
                foreach (Control c in rows.Controls) if (c.Width != width) c.Width = width;
            }
            finally { sizing = false; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int radiusPreference = 2; // DWMWCP_ROUND, ignored by older Windows.
            DwmSetWindowAttribute(Handle, 33, ref radiusPreference, 4);
            int border = 0x4b3524;
            DwmSetWindowAttribute(Handle, 34, ref border, 4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Outline)) e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed) { tips.Dispose(); Icon?.Dispose(); }
            base.Dispose(disposing);
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero); // WM_NCLBUTTONDOWN / HTCAPTION
        }

        private static Label Caption(string text, Font font, Color color) => new Label { Text = text, Font = font,
            ForeColor = color, AutoEllipsis = true, Dock = DockStyle.Fill, Margin = Padding.Empty, UseMnemonic = false };

        private static void DrawBattery(Graphics g, RectangleF area, int? level, Color color, float scale)
        {
            float body = area.Width - 3 * scale;
            using (var pen = new Pen(level == null ? Palette.Muted : color, Math.Max(1, scale)))
                g.DrawRounded(pen, new RectangleF(area.X, area.Y, body, area.Height), 2 * scale);
            using (var brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, area.X + body + 1 * scale, area.Y + 3 * scale, 2 * scale, area.Height - 6 * scale);
                if (level > 0) g.FillRectangle(brush, area.X + 2 * scale, area.Y + 2 * scale,
                    Math.Max(scale, (body - 4 * scale) * level.Value / 100f), area.Height - 4 * scale);
            }
        }

        private sealed class BrandMark : Control
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float k = DeviceDpi / 96f;
                var tile = new RectangleF(1, 1, Width - 2, Height - 2);
                using (var brush = new SolidBrush(Tile)) g.FillRounded(brush, tile, 7 * k);
                DrawBattery(g, new RectangleF((Width - 18 * k) / 2, (Height - 9 * k) / 2, 18 * k, 9 * k), 69, Aqua, k);
            }
        }

        private sealed class DeviceCard : Control
        {
            private readonly BatteryDevice device;
            private readonly int threshold;
            private readonly string detail;
            internal DeviceCard(BatteryDevice d, int threshold, int dpi)
            {
                device = d;
                this.threshold = threshold;
                float k = dpi / 96f;
                Height = (int)Math.Round(51 * k);
                Margin = new Padding(0, 0, 0, (int)Math.Round(5 * k));
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                string connection = d.Transport == DeviceTransport.UsbReceiver ? L.T("2.4 GHz receiver", "Донгл 2,4 ГГц")
                    : d.Transport == DeviceTransport.UsbCable ? L.T("USB cable", "USB-кабель") : "Bluetooth";
                detail = connection + (d.UsbPowered ? L.T("  ·  USB power", "  ·  Питание USB") : d.IsStale
                    ? L.T("  ·  Last reading", "  ·  Последнее показание")
                    : d.Charging ? L.T("  ·  Charging", "  ·  Заряжается") : "");
                AccessibleRole = AccessibleRole.StaticText;
                AccessibleName = d.Name + ", " + detail + ", " + d.LevelText;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float k = DeviceDpi / 96f;
                int P(float n) => (int)Math.Round(n * k);
                using (var fill = new SolidBrush(PanelColor)) g.FillRounded(fill, new RectangleF(0, 0, Width - 1, Height - 1), P(8));
                using (var edge = new Pen(Outline)) g.DrawRounded(edge, new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), P(8));
                int mid = Height / 2;
                using (var fill = new SolidBrush(Tile)) g.FillRounded(fill, new RectangleF(P(9), mid - P(15), P(30), P(30)), P(7));
                DrawDevice(g, device.Kind, P(24), mid, k, Aqua);
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                var textWidth = Math.Max(1, Width - P(132));
                TextRenderer.DrawText(g, device.Name, Body, new Rectangle(P(48), mid - P(17), textWidth, P(19)), Palette.Main, flags);
                TextRenderer.DrawText(g, detail, Detail, new Rectangle(P(48), mid + P(1), textWidth, P(16)), Palette.Muted, flags);
                Color color = device.Charging ? Aqua : !device.Percent.HasValue ? Palette.Muted
                    : device.Percent <= Math.Min(10, threshold) ? Red : device.Percent <= threshold || device.Percent < 60 ? Amber : Green;
                DrawBattery(g, new RectangleF(Width - P(76), mid - P(5), P(23), P(10)), device.Percent, color, k);
                TextRenderer.DrawText(g, device.LevelText, Body,
                    new Rectangle(Width - P(49), 0, P(38), Height), color, flags | TextFormatFlags.Right);
            }
        }

        internal static void DrawDevice(Graphics g, DeviceKind kind, int cx, int cy, float k, Color color)
        {
            var state = g.Save();
            g.TranslateTransform(cx, cy);
            g.ScaleTransform(k, k);
            using (var p = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                switch (kind)
                {
                    case DeviceKind.Mouse:
                        g.DrawRounded(p, new RectangleF(-4.5f, -7, 9, 14), 4);
                        g.DrawLine(p, 0, -5, 0, -2);
                        break;
                    case DeviceKind.Keyboard:
                        g.DrawRounded(p, new RectangleF(-8, -4.5f, 16, 9), 2);
                        for (int i = -5; i <= 5; i += 3) g.DrawLine(p, i, -1, i + 0.3f, -1);
                        g.DrawLine(p, -4, 2, 4, 2);
                        break;
                    case DeviceKind.Headphones:
                        g.DrawArc(p, -6, -7, 12, 12, 180, 180);
                        g.DrawRounded(p, new RectangleF(-7, -1, 4, 6), 1.5f);
                        g.DrawRounded(p, new RectangleF(3, -1, 4, 6), 1.5f);
                        break;
                    case DeviceKind.Controller:
                        g.DrawRounded(p, new RectangleF(-8, -4, 16, 9), 4);
                        g.DrawLine(p, -5, 0, -1, 0);
                        g.DrawLine(p, -3, -2, -3, 2);
                        g.DrawLine(p, 3, 0, 3.5f, 0);
                        g.DrawLine(p, 5, 2, 5.5f, 2);
                        break;
                    default:
                        g.DrawRounded(p, new RectangleF(-4, -7, 8, 14), 2);
                        g.DrawLine(p, -1, 4, 1, 4);
                        break;
                }
            }
            g.Restore(state);
        }

        private sealed class NavButton : Button
        {
            private readonly bool selected;
            internal NavButton(string text, bool selected)
            {
                Text = text;
                this.selected = selected;
                Dock = DockStyle.Fill;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(PanelColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool hover = !selected && ClientRectangle.Contains(PointToClient(Cursor.Position));
                if (selected || hover || Focused)
                {
                    using (var tile = new SolidBrush(Tile)) g.FillRounded(tile, new RectangleF(0, 0, Width - 1, Height - 1), 5);
                    if (selected) using (var edge = new Pen(Color.FromArgb(46, 95, 123)))
                        g.DrawRounded(edge, new RectangleF(.5f, .5f, Width - 2, Height - 2), 5);
                }
                TextRenderer.DrawText(g, Text, Body, ClientRectangle, selected ? Aqua : Palette.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2));
            }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
        }

        private sealed class HeaderButton : Button
        {
            private readonly char glyph;
            internal HeaderButton(char glyph, string name)
            {
                this.glyph = glyph;
                AccessibleName = name;
                Dock = DockStyle.Fill;
                Cursor = Cursors.Hand;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Palette.Back);
                bool hover = ClientRectangle.Contains(PointToClient(Cursor.Position));
                using (var bg = new SolidBrush(hover && glyph == 'x' ? Color.FromArgb(81, 37, 47) : hover || Focused ? Tile : PanelColor))
                    g.FillRounded(bg, new RectangleF(0, 0, Width - 1, Height - 1), 6);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2));
                float k = DeviceDpi / 96f;
                var state = g.Save();
                g.TranslateTransform(Width / 2f, Height / 2f);
                g.ScaleTransform(k, k);
                using (var pen = new Pen(Enabled ? Palette.Main : Palette.Muted, 1.35f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    if (glyph == 'x') { g.DrawLine(pen, -4, -4, 4, 4); g.DrawLine(pen, 4, -4, -4, 4); }
                    else { g.DrawArc(pen, -4.5f, -4.5f, 9, 9, 15, 300); g.DrawLines(pen, new[] { new PointF(3, -5), new PointF(4.2f, -2.6f), new PointF(1.5f, -2.6f) }); }
                }
                g.Restore(state);
            }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
        }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
