using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BlueGauge
{
    internal sealed class TrayApplication : ApplicationContext
    {
        private readonly Dashboard window;
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly System.Windows.Forms.Timer timer;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly LowBatteryTracker alerts = new LowBatteryTracker();
        private readonly IBatterySource source;
        private readonly bool demo;
        private readonly Preferences settings;
        private Icon icon;
        private bool refreshing;
        private bool stopping;
        private IReadOnlyList<BatteryDevice> devices = Array.Empty<BatteryDevice>();
        private DateTime? updatedAt;

        internal TrayApplication(bool demo, bool background)
        {
            this.demo = demo;
            source = demo ? (IBatterySource)new DemoBatterySource() : new WindowsBatterySource();
            settings = demo ? new Preferences() : Preferences.Load();
            window = new Dashboard(demo);
            window.RefreshRequested += async (s, e) => await RefreshAsync();
            window.SettingsRequested += (s, e) => OpenSettings();
            menu = new ContextMenuStrip();
            menu.Items.Add(L.T("Open BlueGauge", "Открыть BlueGauge"), null, (s, e) => ShowWindow());
            menu.Items.Add(L.T("Refresh", "Обновить"), null, async (s, e) => await RefreshAsync());
            menu.Items.Add(L.T("Settings…", "Настройки…"), null, (s, e) => OpenSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Exit", "Выход"), null, (s, e) => ExitThread());
            icon = TrayArtwork.Create(null, settings.LowThreshold);
            tray = new NotifyIcon { ContextMenuStrip = menu, Icon = icon, Text = "BlueGauge", Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            tray.BalloonTipClicked += (s, e) => ShowWindow();
            timer = new System.Windows.Forms.Timer { Interval = settings.RefreshSeconds * 1000 };
            timer.Tick += async (s, e) => await RefreshAsync();
            // Create the WinForms handle even for a quiet launch to marshal refreshes to the UI thread.
            _ = window.Handle;
            if (!background) ShowWindow();
            timer.Start();
            window.BeginInvoke((Action)(async () => await RefreshAsync()));
        }

        private void ShowWindow()
        {
            window.Show();
            if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
            window.Activate();
        }

        private void OpenSettings()
        {
            ShowWindow();
            using (var dialog = new SettingsDialog(settings, demo))
            {
                if (dialog.ShowDialog(window) != DialogResult.OK) return;
                alerts.Reset();
                timer.Interval = settings.RefreshSeconds * 1000;
                Render(null);
            }
        }

        private async Task RefreshAsync()
        {
            if (refreshing || stopping) return;
            refreshing = true;
            window.SetBusy(true);
            var token = lifetime.Token;
            try
            {
                devices = await Task.Run(() => source.ReadAsync(token), token);
                if (stopping) return;
                updatedAt = DateTime.Now;
                Render(null);
                var low = alerts.Update(devices, settings.LowThreshold, settings.Alerts && !demo);
                if (low.Count > 0)
                {
                    var list = string.Join("\n", low.Take(4).Select(d => d.Name + " — " + d.Percent + "%"));
                    if (low.Count > 4) list += "\n+" + (low.Count - 4);
                    tray.ShowBalloonTip(5000, L.T("BlueGauge · Low battery", "BlueGauge · Низкий заряд"), list, ToolTipIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                if (stopping) return;
                System.Diagnostics.Trace.TraceWarning("Bluetooth refresh failed: {0}", ex.GetType().Name);
                Render(updatedAt.HasValue
                    ? L.T("Bluetooth refresh failed. Showing the last readings.", "Не удалось обновить Bluetooth. Показаны последние данные.")
                    : L.T("Couldn't refresh Bluetooth. Check the adapter and try again.", "Не удалось обновить Bluetooth. Проверьте адаптер и повторите попытку."));
            }
            finally
            {
                refreshing = false;
                if (!stopping) window.SetBusy(false);
            }
        }

        private void Render(string error)
        {
            window.UpdateDevices(devices, updatedAt, settings.LowThreshold, error);
            int? percent = error == null ? BatteryMetrics.Lowest(devices)?.Percent : null;
            var next = TrayArtwork.Create(percent, settings.LowThreshold);
            tray.Icon = next;
            icon.Dispose();
            icon = next;
            tray.Text = "BlueGauge" + (demo ? " · Demo" : "") + (percent.HasValue ? " · " + percent + "%" : "");
        }

        protected override void ExitThreadCore()
        {
            if (stopping) return;
            stopping = true;
            lifetime.Cancel();
            timer.Dispose();
            tray.Visible = false;
            tray.Dispose();
            icon.Dispose();
            menu.Dispose();
            window.AllowClose = true;
            window.Close();
            window.Dispose();
            lifetime.Dispose();
            base.ExitThreadCore();
        }
    }
}
