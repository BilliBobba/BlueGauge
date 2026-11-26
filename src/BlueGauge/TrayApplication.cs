using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        private readonly System.Windows.Forms.Timer bluetoothTimer;
        private readonly System.Windows.Forms.Timer hidTimer;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly LowBatteryTracker alerts = new LowBatteryTracker();
        private readonly IBatterySource bluetooth;
        private readonly HidBatterySource hid = new HidBatterySource();
        private readonly System.Windows.Forms.Timer taskbarTimer;
        private TaskbarPanel taskbarPanel;
        private TaskbarSlot? taskbarSlot;
        private bool checkingTaskbar;
        private readonly bool demo;
        private readonly Preferences settings;
        private Icon icon;
        private bool checkingBluetooth, checkingHid, stopping;
        private IReadOnlyList<BatteryDevice> bluetoothRows = Array.Empty<BatteryDevice>();
        private IReadOnlyList<BatteryDevice> hidRows = Array.Empty<BatteryDevice>();
        private DateTime? updatedAt;
        private string bluetoothError, hidError;
        private int? iconLevel;
        private int iconThreshold;

        internal TrayApplication(bool demo, bool background)
        {
            this.demo = demo;
            bluetooth = demo ? (IBatterySource)new DemoBatterySource() : new WindowsBatterySource();
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
            iconThreshold = settings.LowThreshold;
            icon = TrayArtwork.Create(null, iconThreshold);
            tray = new NotifyIcon { ContextMenuStrip = menu, Icon = icon, Text = "BlueGauge", Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            tray.BalloonTipClicked += (s, e) => ShowWindow();
            bluetoothTimer = new System.Windows.Forms.Timer { Interval = settings.RefreshSeconds * 1000 };
            bluetoothTimer.Tick += async (s, e) => await RefreshBluetoothAsync();
            hidTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            hidTimer.Tick += async (s, e) => await RefreshHidAsync();
            taskbarTimer = new System.Windows.Forms.Timer { Interval = 750 };
            taskbarTimer.Tick += async (s, e) => await RefreshTaskbarPlacementAsync();
            _ = window.Handle;
            if (!background) ShowWindow();
            bluetoothTimer.Start();
            if (!demo) hidTimer.Start();
            window.BeginInvoke((Action)(async () =>
            {
                ApplyTaskbarSetting();
                await RefreshAsync();
            }));
        }

        private void ShowWindow()
        {
            window.Show();
            if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
            window.Activate();
            if (!demo) _ = RefreshHidAsync();
        }

        private void OpenSettings()
        {
            ShowWindow();
            using (var dialog = new SettingsDialog(settings, demo))
            {
                if (dialog.ShowDialog(window) != DialogResult.OK) return;
                alerts.Reset();
                bluetoothTimer.Interval = settings.RefreshSeconds * 1000;
                ApplyTaskbarSetting();
                Render();
            }
        }

        private async Task RefreshAsync()
        {
            if (stopping) return;
            window.SetBusy(true);
            try { await Task.WhenAll(RefreshHidAsync(), RefreshBluetoothAsync()); }
            finally { if (!stopping) window.SetBusy(false); }
        }

        private async Task RefreshHidAsync()
        {
            if (demo || checkingHid || stopping) return;
            checkingHid = true;
            var token = lifetime.Token;
            try
            {
                var rows = await Task.Run(() => hid.Read(token), token);
                if (stopping) return;
                hidRows = rows;
                hidError = null;
                if (rows.Count > 0) updatedAt = DateTime.Now;
                Render();
            }
            catch (Exception ex) when (ex is Win32Exception || ex is OperationCanceledException)
            {
                if (stopping) return;
                hidRows = Array.Empty<BatteryDevice>();
                hidError = L.T("Could not read USB receivers.", "Не удалось прочитать USB-приёмники.");
                Render();
            }
            finally { checkingHid = false; }
        }

        private async Task RefreshBluetoothAsync()
        {
            if (checkingBluetooth || stopping) return;
            checkingBluetooth = true;
            var token = lifetime.Token;
            try
            {
                var rows = await Task.Run(() => bluetooth.ReadAsync(token), token);
                if (stopping) return;
                bluetoothRows = rows;
                bluetoothError = null;
                updatedAt = DateTime.Now;
                Render();
            }
            catch (Exception ex)
            {
                if (stopping) return;
                System.Diagnostics.Trace.TraceWarning("Bluetooth refresh failed: {0}", ex.GetType().Name);
                bluetoothRows = Array.Empty<BatteryDevice>();
                bluetoothError = L.T("Could not refresh Bluetooth.", "Не удалось обновить Bluetooth.");
                Render();
            }
            finally { checkingBluetooth = false; }
        }

        private void Render()
        {
            var devices = hidRows.Concat(bluetoothRows).ToArray();
            var active = BatteryMetrics.VisibleDevices(devices);
            window.UpdateDevices(active, updatedAt, settings.LowThreshold, hidError ?? (active.Length == 0 ? bluetoothError : null));
            UpdateTaskbarPanel(active);
            int? percent = BatteryMetrics.Lowest(active)?.Percent;
            if (iconLevel != percent || iconThreshold != settings.LowThreshold)
            {
                var next = TrayArtwork.Create(percent, settings.LowThreshold);
                tray.Icon = next;
                icon.Dispose();
                icon = next;
                iconLevel = percent;
                iconThreshold = settings.LowThreshold;
            }
            tray.Text = "BlueGauge" + (demo ? " · Demo" : "") + (percent.HasValue ? " · " + percent + "%" : "");
            var low = alerts.Update(devices, settings.LowThreshold, settings.Alerts && !demo);
            if (low.Count == 0) return;
            string list = string.Join("\n", low.Take(4).Select(d => d.Name + " — " + d.Percent + "%"));
            if (low.Count > 4) list += "\n+" + (low.Count - 4);
            tray.ShowBalloonTip(5000, L.T("BlueGauge · Low battery", "BlueGauge · Низкий заряд"), list, ToolTipIcon.Warning);
        }

        private void ApplyTaskbarSetting()
        {
            if (settings.TaskbarPanel && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                taskbarTimer.Start();
                _ = RefreshTaskbarPlacementAsync();
            }
            else
            {
                taskbarTimer.Stop();
                taskbarSlot = null;
                taskbarPanel?.Hide();
            }
        }

        private async Task RefreshTaskbarPlacementAsync()
        {
            if (stopping || checkingTaskbar || !settings.TaskbarPanel) return;
            checkingTaskbar = true;
            try
            {
                // UI Automation is cross-process: never wait for Explorer on the message loop.
                var slot = await Task.Run(TaskbarPlacement.Probe);
                if (stopping || !settings.TaskbarPanel) return;
                taskbarSlot = slot;
                UpdateTaskbarPanel(BatteryMetrics.VisibleDevices(hidRows.Concat(bluetoothRows)));
            }
            catch (Exception ex)
            {
                if (stopping) return;
                System.Diagnostics.Trace.TraceWarning("Taskbar probe failed: {0}", ex.GetType().Name);
                taskbarSlot = null;
                taskbarPanel?.Hide();
            }
            finally { checkingTaskbar = false; }
        }

        private void UpdateTaskbarPanel(BatteryDevice[] active)
        {
            if (!settings.TaskbarPanel || taskbarSlot is not TaskbarSlot slot || active.Length == 0)
            {
                taskbarPanel?.Hide();
                return;
            }
            taskbarPanel ??= new TaskbarPanel(ShowWindow, demo);
            taskbarPanel.UpdateDevices(active, settings.LowThreshold);
            taskbarPanel.Place(slot);
        }

        protected override void ExitThreadCore()
        {
            if (stopping) return;
            stopping = true;
            lifetime.Cancel();
            bluetoothTimer.Dispose();
            hidTimer.Dispose();
            taskbarTimer.Dispose();
            taskbarPanel?.Close();
            taskbarPanel?.Dispose();
            tray.Visible = false;
            tray.Dispose();
            icon.Dispose();
            menu.Dispose();
            window.AllowClose = true;
            window.Close();
            window.Dispose();
            // A running HID exchange may still be draining native I/O. Its token is owned by the context.
            if (!checkingBluetooth && !checkingHid) lifetime.Dispose();
            base.ExitThreadCore();
        }
    }
}
