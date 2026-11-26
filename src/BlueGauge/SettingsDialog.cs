using System;
using System.Drawing;
using System.IO;
using System.Security;
using System.Windows.Forms;

namespace BlueGauge
{
    internal sealed class SettingsDialog : Form
    {
        private static readonly Font Heading = new Font("Segoe UI", 16, FontStyle.Bold);
        private static readonly Font Body = new Font("Segoe UI", 9.5f);
        private static readonly int[] Intervals = { 30, 60, 120 };
        private readonly Preferences settings;
        private readonly NumericUpDown threshold;
        private readonly ComboBox interval;
        private readonly CheckBox alerts;
        private readonly CheckBox startup;
        private readonly CheckBox taskbar;
        private readonly bool demo;

        internal SettingsDialog(Preferences settings, bool demo)
        {
            this.settings = settings;
            this.demo = demo;
            Text = L.T("BlueGauge settings", "Настройки BlueGauge");
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Palette.Back;
            ForeColor = Palette.Main;
            Font = Body;
            ClientSize = new Size(400, 310);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            foreach (int height in new[] { 40, 34, 34, 34, 34, 34, 38 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Controls.Add(layout);

            var heading = new Label { Text = L.T("Preferences", "Параметры"), Font = Heading, Dock = DockStyle.Fill };
            AddWide(layout, heading, 0);
            layout.Controls.Add(Caption(L.T("Low battery threshold (%)", "Порог низкого заряда (%)")), 0, 1);
            threshold = new NumericUpDown { Minimum = 5, Maximum = 50, Value = settings.LowThreshold, Dock = DockStyle.Fill,
                BackColor = Palette.Surface, ForeColor = Palette.Main, TextAlign = HorizontalAlignment.Right,
                AccessibleName = L.T("Low battery threshold (%)", "Порог низкого заряда (%)") };
            layout.Controls.Add(threshold, 1, 1);
            layout.Controls.Add(Caption(L.T("Bluetooth refresh interval", "Опрос Bluetooth")), 0, 2);
            interval = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill,
                BackColor = Palette.Surface, ForeColor = Palette.Main, AccessibleName = L.T("Bluetooth refresh interval", "Опрос Bluetooth") };
            foreach (var seconds in Intervals) interval.Items.Add(seconds + L.T(" s", " с"));
            interval.SelectedIndex = Math.Max(0, Array.IndexOf(Intervals, settings.RefreshSeconds));
            layout.Controls.Add(interval, 1, 2);
            alerts = new CheckBox { Text = L.T("Show Windows notifications", "Показывать уведомления Windows"),
                Checked = settings.Alerts && !demo, Dock = DockStyle.Fill, Enabled = !demo };
            AddWide(layout, alerts, 3);
            startup = new CheckBox { Text = L.T("Start with Windows", "Запускать вместе с Windows"),
                Checked = !demo && Preferences.IsStartupEnabled(), Dock = DockStyle.Fill, Enabled = !demo };
            AddWide(layout, startup, 4);
            taskbar = new CheckBox { Text = L.T("Show battery panel by Start (Windows 11)", "Показывать заряд у кнопки «Пуск» (Windows 11)"),
                Checked = settings.TaskbarPanel, Dock = DockStyle.Fill, Enabled = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) };
            AddWide(layout, taskbar, 5);
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false };
            var ok = ActionButton(L.T("Save", "Сохранить"));
            ok.Click += Save;
            var cancel = ActionButton(L.T("Cancel", "Отмена"));
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            AddWide(layout, buttons, 6);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Save(object sender, EventArgs e)
        {
            var next = new Preferences { LowThreshold = (int)threshold.Value, RefreshSeconds = Intervals[interval.SelectedIndex], Alerts = alerts.Checked, TaskbarPanel = taskbar.Checked };
            if (!demo)
            {
                try
                {
                    next.Save();
                    if (Preferences.IsStartupEnabled() != startup.Checked) Preferences.SetStartup(startup.Checked);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    MessageBox.Show(this, L.T("Some preferences could not be saved. Check your Windows permissions.",
                        "Не все настройки удалось сохранить. Проверьте разрешения Windows."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            settings.LowThreshold = next.LowThreshold;
            settings.RefreshSeconds = next.RefreshSeconds;
            settings.Alerts = next.Alerts;
            settings.TaskbarPanel = next.TaskbarPanel;
            DialogResult = DialogResult.OK;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private static void AddWide(TableLayoutPanel layout, Control control, int row)
        {
            layout.Controls.Add(control, 0, row);
            layout.SetColumnSpan(control, 2);
        }

        private static Label Caption(string text) => new Label { Text = text, ForeColor = Palette.Muted,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

        private static Button ActionButton(string text)
        {
            var button = new Button { Text = text, Width = 105, Height = 29, FlatStyle = FlatStyle.Flat,
                BackColor = Palette.Raised, ForeColor = Palette.Main, Margin = new Padding(4, 3, 0, 3) };
            button.FlatAppearance.BorderColor = Palette.Border;
            return button;
        }
    }
}
