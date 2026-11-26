using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BlueGauge.Tests
{
    internal static class BatteryTests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Contains("--hid")) { LiveHid(); return 0; }
            var checks = new List<Action> { Levels, LowestKnown, AlertHysteresis, AlertIndependence, Normalize, Demo, TrayIcon, VisibleReadings, HidFrames, LastReadings, WiredHeadset, TaskbarSpace, TaskbarCapacity };
            if (args.Contains("--live")) { checks.Add(Live); checks.Add(LiveHid); checks.Add(LiveTaskbar); }
            int failures = 0;
            foreach (var check in checks)
            {
                try { check(); Console.WriteLine("PASS " + check.Method.Name); }
                catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + check.Method.Name + ": " + error); }
            }
            Console.WriteLine($"{checks.Count - failures}/{checks.Count} checks passed");
            return failures == 0 ? 0 : 1;
        }

        private static void Levels()
        {
            Equal((int?)0, Device(0).Percent);
            Equal((int?)100, Device(100).Percent);
            Equal(null, Device(null).Percent);
            Equal(null, Device(-1).Percent);
            Equal(null, Device(101).Percent);
            Equal(null, Device(18, false).Percent);
        }

        private static void LowestKnown()
        {
            Equal((int?)8, BatteryMetrics.Lowest(new[] { Device(42), Device(0, false), Device(null), Device(8) }).Percent);
            Equal(null, BatteryMetrics.Lowest(new[] { Device(null), Device(19, false) }));
            Equal("a", BatteryMetrics.Lowest(new[] { Device(40, id: "z"), Device(40, id: "a") }).Id);
        }

        private static void AlertHysteresis()
        {
            var tracker = new LowBatteryTracker();
            int Count(int? percent, bool connected = true) => tracker.Update(new[] { Device(percent, connected) }, 20, true).Count;
            Equal(0, Count(21)); Equal(1, Count(20)); Equal(0, Count(18));
            Equal(0, Count(null)); Equal(0, Count(15, false)); Equal(0, Count(18));
            Equal(0, Count(24)); Equal(0, Count(20)); Equal(0, Count(25)); Equal(1, Count(20));
        }

        private static void AlertIndependence()
        {
            var tracker = new LowBatteryTracker();
            var devices = new[] { Device(0, id: "a"), Device(12, id: "b"), Device(null, id: "c") };
            Equal(0, tracker.Update(devices, 20, false).Count);
            Equal(2, tracker.Update(devices, 20, true).Count);
            Equal(0, tracker.Update(devices, 20, true).Count);
            tracker.Update(Array.Empty<BatteryDevice>(), 20, true);
            Equal(2, tracker.Update(devices, 20, true).Count);
        }

        private static void Normalize()
        {
            Equal("A1BBCCDDEEFF", WindowsBatterySource.NormalizeAddress("a1:bb:cc:dd:ee:ff"));
            Equal("A1BBCCDDEEFF", WindowsBatterySource.NormalizeAddress("a1-bb-cc-dd-ee-ff"));
            Equal(null, WindowsBatterySource.NormalizeAddress("unknown"));
            Equal(null, WindowsBatterySource.NormalizeAddress("00:00:00:00:00:00"));
            Equal(null, WindowsBatterySource.NormalizeAddress("ff:ff:ff:ff:ff:ff"));
            Equal("A1BBCCDDEEFF", WindowsBatterySource.AddressFromPnpId(@"BTHENUM\DEV_A1BBCCDDEEFF\7&abc&0"));
            Equal("A1BBCCDDEEFF", WindowsBatterySource.AddressFromPnpId(@"BTHENUM\{0000}\7&abc&0&A1BBCCDDEEFF_C00000000"));
            Equal(null, WindowsBatterySource.AddressFromPnpId(@"USB\DEV_A1BBCCDDEEFF\7&abc&0"));
        }

        private static void Demo()
        {
            var source = new DemoBatterySource();
            var rows = source.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Equal(4, rows.Count);
            Equal((int?)17, BatteryMetrics.Lowest(rows).Percent);
            Equal(1, rows.Count(row => row.Transport == DeviceTransport.UsbReceiver));
            Equal(1, rows.Count(row => row.Charging));
            try { source.ReadAsync(new CancellationToken(true)).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { return; }
            throw new Exception("Demo source ignored cancellation.");
        }

        private static void TrayIcon()
        {
            foreach (int? p in new int?[] { null, 0, 15, 72, 100 })
                using (var icon = TrayArtwork.Create(p, 20)) Equal(32, icon.Width);
        }

        private static void Live()
        {
            var source = new WindowsBatterySource();
            using (var token = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                var rows = source.ReadAsync(token.Token).GetAwaiter().GetResult();
                Equal(rows.Count, rows.Select(row => row.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                var pnp = PnpBatteryReader.Read(token.Token);
                Console.WriteLine("  Windows: {0} paired, {1} connected, {2} with a battery reading; {3} PnP battery node(s).",
                    rows.Count, rows.Count(row => row.Connected), rows.Count(row => row.Percent.HasValue), pnp.Count);
            }
        }

        private static void LiveHid()
        {
            using (var token = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
            {
                var reader = new HidBatterySource();
                var rows = reader.Read(token.Token, Console.WriteLine);
                foreach (var row in rows) Console.WriteLine("  {0}: {1} ({2}, charging: {3})", row.Name, row.LevelText, row.Transport, row.Charging);
                Console.WriteLine("  {0} supported USB device(s) present; {1} reported battery level.", rows.Count, rows.Count(d => d.Percent.HasValue));
                Equal(rows.Count, rows.Select(r => r.Id).Distinct().Count());
            }
        }

        private static void VisibleReadings()
        {
            var active = BatteryMetrics.VisibleDevices(new[] {
                Device(null, id: "unknown"), Device(24, false, "off"), Device(45),
                new BatteryDevice("charging", "Charging USB device", DeviceKind.Mouse, true, null, DeviceTransport.UsbCable, true)
            });
            Equal(3, active.Length);
            Equal((int?)45, active[0].Percent);
            Equal(true, active.Any(d => d.Charging));
            Equal(true, active.Any(d => !d.Charging && d.Percent == null));
        }

        private static void TaskbarSpace()
        {
            var bar = new System.Drawing.Rectangle(0, 1020, 1920, 60);
            var start = new System.Drawing.Rectangle(547, 1020, 56, 60);
            var widgets = new System.Drawing.Rectangle(8, 1020, 190, 60);
            Equal(new System.Drawing.Rectangle(204, 1020, 337, 60), TaskbarPlacement.FindFreeSpace(bar, start, widgets, 6));
            Equal(new System.Drawing.Rectangle(6, 1020, 535, 60), TaskbarPlacement.FindFreeSpace(bar, start, System.Drawing.Rectangle.Empty, 6));
            start.X = 0;
            Equal(System.Drawing.Rectangle.Empty, TaskbarPlacement.FindFreeSpace(bar, start, widgets, 6));
        }

        private static void TaskbarCapacity()
        {
            Equal((262, 4), TaskbarPanel.Fit(337, 96, 4));
            Equal((174, 2), TaskbarPanel.Fit(200, 96, 4));
            Equal((50, 0), TaskbarPanel.Fit(52, 96, 4));
            Equal((0, 0), TaskbarPanel.Fit(40, 96, 4));
            Equal((330, 4), TaskbarPanel.Fit(350, 120, 4));
        }

        private static void LiveTaskbar()
        {
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            var slot = System.Threading.Tasks.Task.Run(TaskbarPlacement.Probe).GetAwaiter().GetResult();
            if (!slot.HasValue) { Console.WriteLine("  No free slot left of Start (or taskbar is hidden)."); return; }
            Console.WriteLine($"  Taskbar space left of Start: {slot.Value.Free}, DPI {slot.Value.Dpi}.");
            using var panel = new TaskbarPanel(() => { }, true);
            panel.UpdateDevices(new[] { Device(55), Device(null, id: "unknown") }, 20);
            panel.Place(slot.Value);
            if (TaskbarPanel.Fit(slot.Value.Free.Width, slot.Value.Dpi, 2).Width == 0) return;
            Equal(true, panel.Visible);
            var bar = slot.Value.Free;
            Equal(bar.Right, panel.Bounds.Right);
            Equal(true, bar.Contains(panel.Bounds));
            Equal(false, panel.Region.IsVisible(0, 0));
            Equal(true, panel.Region.IsVisible(panel.Width / 2, panel.Height / 2));
            using var image = new System.Drawing.Bitmap(panel.Width, panel.Height);
            panel.DrawToBitmap(image, panel.ClientRectangle);
            Console.WriteLine($"  Panel rendered at {panel.Bounds}.");
        }

        private static void LastReadings()
        {
            var cache = new LastBatteryReadings();
            BatteryDevice Receiver(int? percent) => new BatteryDevice("mouse", "Mouse", DeviceKind.Mouse, true, percent, DeviceTransport.UsbReceiver);
            Equal("—", cache.Update(new[] { Receiver(null) })[0].LevelText);
            Equal("15%", cache.Update(new[] { Receiver(15) })[0].LevelText);
            var last = cache.Update(new[] { Receiver(null) });
            Equal("15%*", last[0].LevelText);
            Equal(0, new LowBatteryTracker().Update(last, 20, true).Count);
            Equal("50%", cache.Update(new[] { Receiver(50) })[0].LevelText);
            Equal(0, cache.Update(Array.Empty<BatteryDevice>()).Length);
            Equal("—", cache.Update(new[] { Receiver(null) })[0].LevelText);
        }

        private static void WiredHeadset()
        {
            Equal(true, PnpBatteryReader.IsHecateG2II(@"USB\VID_2D99&PID_0026&MI_00\interface"));
            Equal(false, PnpBatteryReader.IsHecateG2II(@"USB\VID_2D99&PID_0026&MI_03\interface"));
            var headset = new BatteryDevice("wired", "HECATE G2 II", DeviceKind.Headphones, true, 100, DeviceTransport.UsbCable, usbPowered: true);
            Equal("USB", headset.LevelText);
            Equal(null, headset.Percent);
        }

        private static void HidFrames()
        {
            byte[] request = OemMouseBattery.Request(OemMouseBattery.Power, 17);
            Equal((byte)0x49, request[16]);
            byte[] renegade = { 9, 4, 0, 0, 0, 2, 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x3f };
            Equal((int?)70, OemMouseBattery.ParsePower(renegade, 9, 10, out bool charging));
            Equal(false, charging);
            Equal(null, OemMouseBattery.ParsePower(renegade, 8, 10, out _));
            renegade[16] ^= 1;
            Equal(null, OemMouseBattery.ParsePower(renegade, 9, 10, out _));
            byte[] off = { 8, 3, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x49 };
            Equal((bool?)false, OemMouseBattery.IsLinked(off, 8));
            // Real Phantom PRO V2 receiver report recorded during a local battery check.
            byte[] phantom = { 8, 4, 0, 0, 0, 2, 0x37, 0, 0x0f, 0x2a, 0, 0, 0, 0, 0, 0, 0xd7 };
            Equal((int?)55, OemMouseBattery.ParsePower(phantom, 8, 1, out charging));
            Equal(false, charging);
        }

        private static BatteryDevice Device(int? percent, bool connected = true, string id = "x") => new BatteryDevice(id, id, DeviceKind.Other, connected, percent);

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}.");
        }
    }
}
