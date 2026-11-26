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

        internal BatteryDevice(string id, string name, DeviceKind kind, bool connected, int? percent,
            DeviceTransport transport = DeviceTransport.Bluetooth, bool charging = false)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Connected = connected;
            Transport = transport;
            Charging = connected && charging;
            Percent = connected && percent.HasValue && percent.Value >= 0 && percent.Value <= 100 ? percent : null;
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
        internal static BatteryDevice[] VisibleDevices(IEnumerable<BatteryDevice> devices) => devices
            .Where(d => d.Connected && (d.Percent.HasValue || d.Charging)).ToArray();

        internal static BatteryDevice Lowest(IEnumerable<BatteryDevice> devices) => devices
            .Where(d => d.Connected && d.Percent.HasValue).OrderBy(d => d.Percent.Value)
            .ThenBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault();
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
                if (!device.Connected || !device.Percent.HasValue) continue;
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
