using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlueGauge
{
    internal enum DeviceKind { Headphones, Mouse, Keyboard, Controller, Other }
    internal enum DeviceTransport { Bluetooth, UsbReceiver, UsbCable }

    internal sealed class BatteryDevice
    {
        internal string Id { get; }
        internal string Name { get; }
        internal DeviceKind Kind { get; }
        internal bool Connected { get; }
        internal int? Percent { get; }
        internal DeviceTransport Transport { get; }
        internal bool Charging { get; }
        internal bool UsbPowered { get; }
        internal bool IsStale { get; }
        internal string LevelText => UsbPowered ? "USB" : Percent is int level ? level + "%" + (IsStale ? "*" : "") : "—";

        internal BatteryDevice(string id, string name, DeviceKind kind, bool connected, int? percent,
            DeviceTransport transport = DeviceTransport.Bluetooth, bool charging = false, bool usbPowered = false, bool isStale = false)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Connected = connected;
            Transport = transport;
            Charging = connected && charging;
            UsbPowered = connected && usbPowered;
            Percent = connected && !usbPowered && percent.HasValue && percent.Value >= 0 && percent.Value <= 100 ? percent : null;
            IsStale = Percent.HasValue && isStale;
        }
    }

    internal interface IBatterySource
    {
        Task<IReadOnlyList<BatteryDevice>> ReadAsync(CancellationToken cancellationToken);
    }

    internal sealed class DemoBatterySource : IBatterySource
    {
        public Task<IReadOnlyList<BatteryDevice>> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<BatteryDevice> devices = new[] {
                new BatteryDevice("demo-mouse", "ARDOR GAMING Phantom PRO V2", DeviceKind.Mouse, true, 51, DeviceTransport.UsbReceiver),
                new BatteryDevice("demo-keyboard", "Compact Keyboard", DeviceKind.Keyboard, true, 94),
                new BatteryDevice("demo-headphones", "Studio Headphones", DeviceKind.Headphones, true, 17),
                new BatteryDevice("demo-controller", "Game Controller", DeviceKind.Controller, true, 80, DeviceTransport.UsbCable, true)
            };
            return Task.FromResult(devices);
        }
    }

    internal static class BatteryMetrics
    {
        internal static BatteryDevice[] VisibleDevices(IEnumerable<BatteryDevice> devices, bool hideUsbCableDevices = false) => devices
            .Where(d => d.Connected && !(hideUsbCableDevices && d.Transport == DeviceTransport.UsbCable))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(d => d.Transport != DeviceTransport.Bluetooth)
                .ThenByDescending(d => d.Percent.HasValue).First())
            .OrderBy(d => d.Percent ?? 101)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        internal static BatteryDevice Lowest(IEnumerable<BatteryDevice> devices) => devices
            .Where(d => d.Connected && d.Percent.HasValue).OrderBy(d => d.Percent.Value)
            .ThenBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    // Preserve last readings only while the same connection is still present.
    internal sealed class LastBatteryReadings
    {
        private readonly Dictionary<(string, DeviceTransport), int> last = new();

        internal BatteryDevice[] Update(IEnumerable<BatteryDevice> devices)
        {
            var rows = new List<BatteryDevice>();
            var present = new HashSet<(string, DeviceTransport)>();
            foreach (var device in devices)
            {
                if (!device.Connected || device.UsbPowered) { rows.Add(device); continue; }
                var key = (device.Id, device.Transport);
                present.Add(key);
                if (device.Percent is int fresh && !device.IsStale) last[key] = fresh;
                if (!device.Percent.HasValue && last.TryGetValue(key, out var level))
                    rows.Add(new BatteryDevice(device.Id, device.Name, device.Kind, true, level, device.Transport, isStale: true));
                else rows.Add(device);
            }
            foreach (var key in last.Keys.Where(key => !present.Contains(key)).ToArray()) last.Remove(key);
            return rows.ToArray();
        }
    }

    internal sealed class LowBatteryTracker
    {
        private readonly HashSet<string> notified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal IReadOnlyList<BatteryDevice> Update(IEnumerable<BatteryDevice> devices, int threshold, bool enabled)
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var alerts = new List<BatteryDevice>();
            foreach (var device in devices)
            {
                present.Add(device.Id);
                if (!device.Connected || device.IsStale || !device.Percent.HasValue) continue;
                if (device.Percent.Value >= Math.Min(100, threshold + 5)) notified.Remove(device.Id);
                if (enabled && device.Percent.Value <= threshold && notified.Add(device.Id)) alerts.Add(device);
            }
            notified.IntersectWith(present);
            return alerts;
        }

        internal void Reset() => notified.Clear();
    }

    internal static class L
    {
        internal static bool Russian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
        internal static string T(string en, string ru) => Russian ? ru : en;
    }
}
