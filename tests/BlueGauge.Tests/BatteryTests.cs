using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BlueGauge.Tests
{
    internal static class BatteryTests
    {
        private static int Main(string[] args)
        {
            var checks = new List<Action> { Levels, LowestKnown, AlertHysteresis, AlertIndependence, Normalize, Demo, TrayIcon };
            if (args.Contains("--live")) checks.Add(Live);
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
            Equal((int?)16, BatteryMetrics.Lowest(rows).Percent);
            Equal(1, rows.Count(row => !row.Connected));
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

        private static BatteryDevice Device(int? percent, bool connected = true, string id = "x") => new BatteryDevice(id, id, DeviceKind.Other, connected, percent);

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}.");
        }
    }
}
