using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace BlueGauge
{
    internal sealed class Dashboard : Form
    {
        private readonly Label status;
        private readonly Label metric;
        private readonly Label metricDetail;
        private readonly Label footer;
        private readonly Button refresh;
        private readonly FlowLayoutPanel list;
        private static readonly Font TitleFont = new Font("Segoe UI", 23, FontStyle.Bold);
        private static readonly Font SectionFont = new Font("Segoe UI", 11, FontStyle.Bold);
        private static readonly Font MetricFont = new Font("Segoe UI", 25, FontStyle.Bold);
        private static readonly Font BodyFont = new Font("Segoe UI", 10);
        private static readonly Font SmallFont = new Font("Segoe UI", 9);
        internal bool AllowClose { get; set; }
        internal event EventHandler RefreshRequested;
        internal event EventHandler SettingsRequested;

        internal Dashboard(bool demo)
        {
            Text = "BlueGauge";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(510, 620);
            MinimumSize = new Size(455, 480);
            BackColor = Palette.Back;
            ForeColor = Palette.Main;
            Font = BodyFont;
            MaximizeBox = false;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(24, 16, 24, 16), BackColor = Palette.Back };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (float h in new[] { 63f, 38f, 101f, 34f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
            Controls.Add(layout);

            var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var brand = Caption("BlueGauge", TitleFont, Palette.Main);
            brand.Location = new Point(-3, 1);
            header.Controls.Add(brand);
            var subtitle = Caption(L.T("BLUETOOTH BATTERY · WINDOWS", "ЗАРЯД BLUETOOTH · WINDOWS"), SmallFont, Palette.Cyan);
            subtitle.Location = new Point(0, 42);
            header.Controls.Add(subtitle);
            layout.Controls.Add(header, 0, 0);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            refresh = ActionButton(L.T("Refresh", "Обновить"));
            refresh.Click += (s, e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
            var settings = ActionButton(L.T("Settings", "Настройки"));
            settings.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
            actions.Controls.Add(refresh);
            actions.Controls.Add(settings);
            if (demo)
            {
                var badge = Caption(L.T("  DEMO", "  ДЕМО"), SectionFont, Palette.Low);
                badge.Margin = new Padding(3, 5, 0, 0);
                actions.Controls.Add(badge);
            }
            layout.Controls.Add(actions, 0, 1);

            var summary = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Surface, Margin = new Padding(0, 4, 0, 5), Padding = new Padding(14, 5, 14, 5) };
            var summaryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2, Margin = Padding.Empty };
            summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            metric = Caption("—", MetricFont, Palette.Cyan);
            metric.Dock = DockStyle.Fill; metric.TextAlign = ContentAlignment.MiddleLeft;
            summaryLayout.Controls.Add(metric, 0, 0); summaryLayout.SetRowSpan(metric, 2);
            var metricTitle = Caption(L.T("LOWEST CONNECTED BATTERY", "МИНИМАЛЬНЫЙ ЗАРЯД"), SmallFont, Palette.Muted);
            metricTitle.Dock = DockStyle.Fill; metricTitle.TextAlign = ContentAlignment.BottomLeft;
            summaryLayout.Controls.Add(metricTitle, 1, 0);
            metricDetail = Caption(L.T("Waiting for readings", "Ожидание данных"), SectionFont, Palette.Main);
            metricDetail.AutoSize = false; metricDetail.AutoEllipsis = true; metricDetail.Dock = DockStyle.Fill;
            summaryLayout.Controls.Add(metricDetail, 1, 1);
            summary.Controls.Add(summaryLayout);
            layout.Controls.Add(summary, 0, 2);

            status = Caption(L.T("Paired devices", "Сопряжённые устройства"), SectionFont, Palette.Main);
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(status, 0, 3);
            list = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false,
                FlowDirection = FlowDirection.TopDown, Margin = Padding.Empty, BackColor = Palette.Back };
            list.ClientSizeChanged += (s, e) => SizeCards();
            layout.Controls.Add(list, 0, 4);

            footer = Caption(L.T("Reading Bluetooth devices…", "Поиск Bluetooth-устройств…"), SmallFont, Palette.Muted);
            footer.AutoSize = false; footer.Dock = DockStyle.Fill; footer.TextAlign = ContentAlignment.BottomLeft;
            layout.Controls.Add(footer, 0, 5);
        }

        internal void SetBusy(bool busy)
        {
            refresh.Enabled = !busy;
            refresh.Text = busy ? L.T("Reading…", "Чтение…") : L.T("Refresh", "Обновить");
        }

        internal void UpdateDevices(IReadOnlyList<BatteryDevice> devices, DateTime? lastUpdate, int threshold, string error)
        {
            var lowest = BatteryMetrics.Lowest(devices);
            metric.Text = lowest == null ? "—" : lowest.Percent + "%";
            metric.ForeColor = Palette.Level(lowest?.Percent, threshold);
            metricDetail.Text = lowest?.Name ?? L.T("No battery reading", "Данных о заряде нет");
            status.Text = L.T("Paired devices", "Сопряжённые устройства") + "  ·  " + devices.Count;
            int scroll = -list.AutoScrollPosition.Y;
            list.SuspendLayout();
            try
            {
                foreach (var control in list.Controls.Cast<Control>().ToArray()) control.Dispose();
                if (devices.Count == 0)
                {
                    var help = Caption(L.T("No paired Bluetooth devices.\nConnect an accessory in Windows Bluetooth settings.",
                        "Нет сопряжённых устройств.\nДобавьте устройство в настройках Bluetooth Windows."), BodyFont, Palette.Muted);
                    help.AutoSize = false; help.Height = 72; help.TextAlign = ContentAlignment.MiddleLeft;
                    list.Controls.Add(help);
                }
                foreach (var d in devices) list.Controls.Add(new DeviceCard(d, threshold));
                SizeCards();
            }
            finally { list.ResumeLayout(true); }
            list.AutoScrollPosition = new Point(0, scroll);
            footer.Text = error ?? (lastUpdate.HasValue ? L.T("Updated at ", "Обновлено в ") + lastUpdate.Value.ToString("HH:mm")
                : L.T("Waiting for the first refresh…", "Ожидание первого обновления…"));
            footer.ForeColor = error == null ? Palette.Muted : Palette.Low;
        }

        private void SizeCards()
        {
            int width = Math.Max(1, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
            foreach (Control c in list.Controls) if (c.Width != width) c.Width = width;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            Icon owned = disposing && !IsDisposed ? Icon : null;
            base.Dispose(disposing);
            owned?.Dispose();
        }

        private static Label Caption(string text, Font font, Color color) => new Label { Text = text, Font = font, ForeColor = color, AutoSize = true, Margin = Padding.Empty };
        private static Button ActionButton(string text)
        {
            var button = new Button { Text = text, Width = 115, Height = 30, Margin = new Padding(0, 2, 8, 2),
                FlatStyle = FlatStyle.Flat, BackColor = Palette.Raised, ForeColor = Palette.Main, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Palette.Border;
            button.FlatAppearance.MouseOverBackColor = Palette.Border;
            return button;
        }

        private sealed class DeviceCard : Control
        {
            private readonly BatteryDevice device;
            private readonly int threshold;
            private readonly string detail;
            internal DeviceCard(BatteryDevice device, int threshold)
            {
                this.device = device; this.threshold = threshold;
                SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Height = 65;
                Margin = new Padding(0, 0, 0, 7);
                detail = KindName(device.Kind) + "  ·  " + (!device.Connected ? L.T("Disconnected", "Отключено")
                    : !device.Percent.HasValue ? L.T("No battery data", "Нет данных о заряде")
                    : device.Percent.Value <= threshold ? L.T("Low battery", "Низкий заряд") : L.T("Connected", "Подключено"));
                AccessibleRole = AccessibleRole.StaticText;
                AccessibleName = device.Name + ", " + detail + (device.Percent.HasValue ? ", " + device.Percent + "%" : "");
                TabStop = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = DeviceDpi / 96f;
                int P(float n) => (int)Math.Round(n * scale);
                using (var bg = new SolidBrush(Palette.Surface)) g.FillRounded(bg, new RectangleF(0, 0, Width - 1, Height - 1), P(10));
                using (var border = new Pen(Focused ? Palette.Cyan : Palette.Border)) g.DrawRounded(border, new RectangleF(.5f, .5f, Width - 2, Height - 2), P(10));
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                int textWidth = Math.Max(0, Width - P(105));
                TextRenderer.DrawText(g, device.Name, SectionFont, new Rectangle(P(14), P(5), textWidth, P(24)), Palette.Main, flags);
                TextRenderer.DrawText(g, detail, SmallFont, new Rectangle(P(14), P(28), textWidth, P(20)), Palette.Muted, flags);
                Color color = Palette.Level(device.Percent, threshold);
                TextRenderer.DrawText(g, device.Percent.HasValue ? device.Percent + "%" : "—", SectionFont,
                    new Rectangle(Width - P(72), P(6), P(56), P(40)), color, flags | TextFormatFlags.Right);
                var rail = new RectangleF(P(14), Height - P(12), Math.Max(0, Width - P(28)), P(4));
                using (var track = new SolidBrush(Palette.Border)) g.FillRectangle(track, rail);
                if (device.Percent.HasValue)
                {
                    rail.Width *= device.Percent.Value / 100f;
                    using (var fill = new SolidBrush(color)) g.FillRectangle(fill, rail);
                }
            }

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            private static string KindName(DeviceKind kind)
            {
                switch (kind)
                {
                    case DeviceKind.Headphones: return L.T("Headphones", "Наушники");
                    case DeviceKind.Mouse: return L.T("Mouse", "Мышь");
                    case DeviceKind.Keyboard: return L.T("Keyboard", "Клавиатура");
                    case DeviceKind.Controller: return L.T("Controller", "Геймпад");
                    default: return "Bluetooth";
                }
            }
        }
    }
}
