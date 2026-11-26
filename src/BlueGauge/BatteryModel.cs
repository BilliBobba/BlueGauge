using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BlueGauge
{
    internal enum DeviceKind { Headphones, Mouse, Keyboard, Controller, Other }

    internal sealed class BatteryDevice
    {
        internal string Id { get; }
        internal string Name { get; }
        internal DeviceKind Kind { get; }
        internal bool Connected { get; }
        internal int? Percent { get; }

        internal BatteryDevice(string id, string name, DeviceKind kind, bool connected, int? percent)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Connected = connected;
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
                new BatteryDevice("demo-headphones", "Studio Headphones", DeviceKind.Headphones, true, 78),
                new BatteryDevice("demo-mouse", "Arc Mouse", DeviceKind.Mouse, true, 42),
                new BatteryDevice("demo-controller", "Game Controller", DeviceKind.Controller, true, 16),
                new BatteryDevice("demo-keyboard", "Compact Keyboard", DeviceKind.Keyboard, false, null)
            };
            return Task.FromResult(devices);
        }
    }

    internal static class BatteryMetrics
    {
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
