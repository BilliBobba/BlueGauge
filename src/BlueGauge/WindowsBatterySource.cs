using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace BlueGauge
{
    internal sealed class WindowsBatterySource : IBatterySource
    {
        private const string Connected = "System.Devices.Aep.IsConnected";
        private const string Address = "System.Devices.Aep.DeviceAddress";
        private const string Container = "System.Devices.Aep.ContainerId";
        private const string Category = "System.Devices.Aep.Category";
        private static readonly string[] EndpointProperties = { Connected, Address, Container, Category };
        private static readonly Regex AddressInId = new Regex(
            @"(?i)(?:DEV_|\\|&)([0-9a-f]{12})(?=[^0-9a-f]|$)", RegexOptions.CultureInvariant);
        private static readonly Regex SeparatedAddress = new Regex(@"(?i)(?<![0-9a-f])(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}(?![0-9a-f])", RegexOptions.CultureInvariant);

        public async Task<IReadOnlyList<BatteryDevice>> ReadAsync(CancellationToken cancellationToken)
        {
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                budget.CancelAfter(TimeSpan.FromSeconds(10));
                var classic = FindEndpointsAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), budget.Token);
                var le = FindEndpointsAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), budget.Token);
                var endpoints = (await Task.WhenAll(classic, le).ConfigureAwait(false))
                    .SelectMany((list, index) => list.Select(d => new Endpoint(d, index == 1))).ToList();
                cancellationToken.ThrowIfCancellationRequested();
                if (endpoints.Count == 0) return Array.Empty<BatteryDevice>();

                var groups = GroupEndpoints(endpoints);
                var pnp = groups.Any(group => group.Any(endpoint => endpoint.Connected))
                    ? PnpBatteryReader.Read(cancellationToken) : new List<PnpBatteryReader.Reading>();
                using (var slots = new SemaphoreSlim(3))
                {
                    var rows = groups.Select(g => ReadGroupAsync(g, pnp, slots, budget.Token));
                    var devices = await Task.WhenAll(rows).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return devices.OrderByDescending(d => d.Connected).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(d => d.Id, StringComparer.Ordinal).ToArray();
                }
            }
        }

        private static async Task<DeviceInformationCollection> FindEndpointsAsync(string selector, CancellationToken token)
        {
            return await DeviceInformation.FindAllAsync(selector, EndpointProperties, DeviceInformationKind.AssociationEndpoint)
                .AsTask(token).ConfigureAwait(false);
        }

        private static List<List<Endpoint>> GroupEndpoints(IEnumerable<Endpoint> endpoints)
        {
            var groups = new List<List<Endpoint>>();
            foreach (var e in endpoints)
            {
                var matches = groups.Where(g => g.Any(x => x.SameDevice(e))).ToList();
                var group = new List<Endpoint> { e };
                foreach (var match in matches) { group.AddRange(match); groups.Remove(match); }
                groups.Add(group);
            }
            return groups;
        }

        private static async Task<BatteryDevice> ReadGroupAsync(List<Endpoint> group, List<PnpBatteryReader.Reading> pnp,
            SemaphoreSlim slots, CancellationToken token)
        {
            // AEP's cached connection flag can remain set after a headset disconnects.
            var candidates = group.Where(e => e.Connected).ToArray();
            var confirmed = await Task.WhenAll(candidates.Select(e => ConfirmConnectionAsync(e, token))).ConfigureAwait(false);
            var active = candidates.Where((_, index) => confirmed[index]).ToArray();
            var preferred = active.OrderBy(e => e.Le).FirstOrDefault() ?? group.OrderBy(e => e.Le).First();
            string id = group.Select(e => e.Container).Where(c => c != Guid.Empty).OrderBy(c => c).Select(c => c.ToString("D")).FirstOrDefault()
                ?? group.Select(e => e.Address).Where(a => a != null).OrderBy(a => a, StringComparer.Ordinal).FirstOrDefault()
                ?? preferred.Info.Id;
            string name = preferred.Info.Name;
            if (string.IsNullOrWhiteSpace(name)) name = L.T("Bluetooth device", "Bluetooth-устройство");
            var kind = Classify(preferred.Category + " " + name);
            int? percent = null;
            if (active.Length != 0)
            {
                percent = pnp.Where(r => active.Any(e => e.Address != null && e.Address == r.Address))
                    .Select(r => (int?)r.Percent).Min();
                if (!percent.HasValue)
                    percent = pnp.Where(r => r.Container != Guid.Empty && active.Any(e => r.Container == e.Container))
                        .Select(r => (int?)r.Percent).Min();
                if (!percent.HasValue && active.Any(e => e.Le))
                {
                    bool acquired = false;
                    try
                    {
                        await slots.WaitAsync(token).ConfigureAwait(false);
                        acquired = true;
                        using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            attempt.CancelAfter(TimeSpan.FromSeconds(4));
                            percent = await ReadGattAsync(active.First(e => e.Le).Info.Id, attempt.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) when (ex is COMException || ex is UnauthorizedAccessException || ex is InvalidOperationException) { }
                    finally { if (acquired) slots.Release(); }
                }
            }
            return new BatteryDevice(id, name, kind, active.Length != 0, percent);
        }

        private static async Task<bool> ConfirmConnectionAsync(Endpoint endpoint, CancellationToken token)
        {
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                attempt.CancelAfter(TimeSpan.FromSeconds(2));
                if (endpoint.Le)
                {
                    using var device = await BluetoothLEDevice.FromIdAsync(endpoint.Info.Id).AsTask(attempt.Token).ConfigureAwait(false);
                    return device?.ConnectionStatus == BluetoothConnectionStatus.Connected;
                }
                using var classic = await BluetoothDevice.FromIdAsync(endpoint.Info.Id).AsTask(attempt.Token).ConfigureAwait(false);
                return classic?.ConnectionStatus == BluetoothConnectionStatus.Connected;
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                return false;
            }
        }

        private static async Task<int?> ReadGattAsync(string id, CancellationToken token)
        {
            using (var device = await BluetoothLEDevice.FromIdAsync(id).AsTask(token).ConfigureAwait(false))
            {
                if (device == null) return null;
                var result = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached)
                    .AsTask(token).ConfigureAwait(false);
                if (result.Status != GattCommunicationStatus.Success) return null;
                int? lowest = null;
                try
                {
                    foreach (var service in result.Services)
                    {
                        var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached)
                            .AsTask(token).ConfigureAwait(false);
                        if (chars.Status != GattCommunicationStatus.Success) continue;
                        foreach (var characteristic in chars.Characteristics)
                        {
                            var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(token).ConfigureAwait(false);
                            if (value.Status != GattCommunicationStatus.Success || value.Value == null || value.Value.Length != 1) continue;
                            using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(value.Value))
                            {
                                int percent = reader.ReadByte();
                                if (percent <= 100 && (!lowest.HasValue || percent < lowest.Value)) lowest = percent;
                            }
                        }
                    }
                }
                finally { foreach (var service in result.Services) service.Dispose(); }
                return lowest;
            }
        }

        private static DeviceKind Classify(string text)
        {
            text = text.ToLowerInvariant();
            if (text.Contains("headphone") || text.Contains("headset") || text.Contains("buds") || text.Contains("airpods") || text.Contains("наушник") || text.Contains("audio")) return DeviceKind.Headphones;
            if (text.Contains("mouse") || text.Contains("мыш")) return DeviceKind.Mouse;
            if (text.Contains("keyboard") || text.Contains("клавиатур")) return DeviceKind.Keyboard;
            if (text.Contains("controller") || text.Contains("gamepad") || text.Contains("dualsense") || text.Contains("геймпад")) return DeviceKind.Controller;
            return DeviceKind.Other;
        }

        internal static string NormalizeAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            string hex = address.Trim().Replace(":", "").Replace("-", "");
            return hex.Length == 12 && ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)
                && value != 0 && value != 0xffffffffffffUL ? hex.ToUpperInvariant() : null;
        }

        internal static string AddressFromPnpId(string id)
        {
            if (string.IsNullOrEmpty(id) || !(id.StartsWith(@"BTHENUM\", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith(@"BTHLE\", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith(@"BTHHFENUM\", StringComparison.OrdinalIgnoreCase))) return null;
            foreach (Match match in AddressInId.Matches(id))
            {
                string address = NormalizeAddress(match.Groups[1].Value);
                if (address != null) return address;
            }
            return null;
        }

        private sealed class Endpoint
        {
            internal readonly DeviceInformation Info;
            internal readonly bool Le;
            internal readonly bool Connected;
            internal readonly string Address;
            internal readonly Guid Container;
            internal readonly string Category;

            internal Endpoint(DeviceInformation info, bool le)
            {
                Info = info; Le = le;
                info.Properties.TryGetValue(WindowsBatterySource.Connected, out var connected);
                info.Properties.TryGetValue(WindowsBatterySource.Address, out var address);
                info.Properties.TryGetValue(WindowsBatterySource.Container, out var container);
                info.Properties.TryGetValue(WindowsBatterySource.Category, out var category);
                Connected = connected is bool b && b;
                var match = SeparatedAddress.Matches(info.Id).Cast<Match>().LastOrDefault();
                Address = NormalizeAddress(address as string) ?? (match != null ? NormalizeAddress(match.Value) : null);
                Container = container is Guid g ? g : Guid.TryParse(container as string, out var parsed) ? parsed : Guid.Empty;
                // The all-FF system container is shared by internal nodes, not a device identity.
                if (Container == new Guid("00000000-0000-0000-ffff-ffffffffffff")) Container = Guid.Empty;
                Category = category as string ?? string.Join(" ", category as string[] ?? Array.Empty<string>());
            }

            internal bool SameDevice(Endpoint other) => (Address != null && Address == other.Address)
                || (Container != Guid.Empty && Container == other.Container)
                || string.Equals(Info.Id, other.Info.Id, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class PnpBatteryReader
    {
        internal sealed class Reading
        {
            internal string Address;
            internal Guid Container;
            internal byte Percent;
        }

        private static readonly DevPropKey BatteryKey = new DevPropKey("104ea319-6ee2-4701-bd47-8ddbf425bbe5", 2);
        private static readonly DevPropKey ContainerKey = new DevPropKey("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2);
        private const uint AllPresentClasses = 0x06;
        private const int NoMoreItems = 259;
        private const uint ByteType = 0x03;
        private const uint GuidType = 0x0d;

        internal static List<Reading> Read(CancellationToken token)
        {
            var results = new List<Reading>();
            IntPtr handle = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, AllPresentClasses);
            if (handle == new IntPtr(-1)) return results; // BLE remains usable if PnP cannot be queried.
            try
            {
                var buffer = new byte[16];
                var id = new StringBuilder(512);
                for (uint index = 0; ; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var node = new DevInfoData { Size = Marshal.SizeOf(typeof(DevInfoData)) };
                    if (!SetupDiEnumDeviceInfo(handle, index, ref node))
                    {
                        if (Marshal.GetLastWin32Error() == NoMoreItems) break;
                        return results;
                    }
                    var key = BatteryKey;
                    if (!SetupDiGetDevicePropertyW(handle, ref node, ref key, out var type, buffer, (uint)buffer.Length, out var size, 0)
                        || type != ByteType || size != 1 || buffer[0] > 100) continue;
                    byte level = buffer[0];
                    key = ContainerKey;
                    Guid container = Guid.Empty;
                    if (SetupDiGetDevicePropertyW(handle, ref node, ref key, out type, buffer, (uint)buffer.Length, out size, 0)
                        && type == GuidType && size == 16) container = new Guid(buffer);
                    id.Clear();
                    string address = SetupDiGetDeviceInstanceIdW(handle, ref node, id, (uint)id.Capacity, out _)
                        ? WindowsBatterySource.AddressFromPnpId(id.ToString()) : null;
                    if (address != null || container != Guid.Empty)
                        results.Add(new Reading { Address = address, Container = container, Percent = level });
                }
            }
            finally { SetupDiDestroyDeviceInfoList(handle); }
            return results;
        }

        // Only positively identified wired models: a generic USB audio endpoint may be a wireless receiver.
        internal static bool IsHecateG2II(string id) => id.StartsWith(@"USB\VID_2D99&PID_0026&MI_00\", StringComparison.OrdinalIgnoreCase);

        private static readonly Guid KeyboardClass = new("4d36e96b-e325-11ce-bfc1-08002be10318");
        private static readonly Guid MouseClass = new("4d36e96f-e325-11ce-bfc1-08002be10318");
        private static readonly Regex UsbPhysicalId = new(@"^USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4}\\[^\\]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly DevPropKey BusDescriptionKey = new("540b947e-8b40-45bc-a8a2-6a0b894cbda2", 4);

        internal static IEnumerable<BatteryDevice> ReadUsbKeyboards(CancellationToken token)
        {
            var mice = ReadUsbInputRoots(MouseClass, token);
            return ReadUsbInputRoots(KeyboardClass, token)
                .Where(k => !mice.ContainsKey(k.Key) && !IsKnownMouseUsbId(k.Key))
                .Select(k => new BatteryDevice(k.Key, string.IsNullOrWhiteSpace(k.Value) ? L.T("USB keyboard", "USB-клавиатура") : k.Value,
                    DeviceKind.Keyboard, true, null, DeviceTransport.UsbCable, usbPowered: true));
        }

        internal static bool IsPhysicalUsbId(string id) => id != null && UsbPhysicalId.IsMatch(id);
        internal static bool IsKnownMouseUsbId(string id) => id.StartsWith(@"USB\VID_3554&PID_F52D\", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith(@"USB\VID_3554&PID_F52E\", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith(@"USB\VID_25A7&PID_FA4A\", StringComparison.OrdinalIgnoreCase);

        private static Dictionary<string, string> ReadUsbInputRoots(Guid classId, CancellationToken token)
        {
            var devices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IntPtr set = SetupDiGetClassDevsByClass(ref classId, IntPtr.Zero, IntPtr.Zero, 0x02); // DIGCF_PRESENT
            if (set == new IntPtr(-1)) return devices;
            try
            {
                for (uint index = 0; ; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var node = new DevInfoData { Size = Marshal.SizeOf<DevInfoData>() };
                    if (!SetupDiEnumDeviceInfo(set, index, ref node)) break;
                    uint current = node.DevInst;
                    var instance = new StringBuilder(512);
                    for (int depth = 0; depth < 12; depth++)
                    {
                        instance.Clear();
                        if (CM_Get_Device_IDW(current, instance, (uint)instance.Capacity, 0) != 0) break;
                        string id = instance.ToString();
                        if (IsPhysicalUsbId(id))
                        {
                            if (!devices.ContainsKey(id))
                            {
                                var key = BusDescriptionKey;
                                var buffer = new byte[512];
                                uint size = (uint)buffer.Length;
                                string name = CM_Get_DevNode_PropertyW(current, ref key, out uint type, buffer, ref size, 0) == 0 && type == 0x12
                                    ? Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0').Trim() : null;
                                devices.Add(id, name);
                            }
                            break;
                        }
                        if (!id.StartsWith(@"HID\", StringComparison.OrdinalIgnoreCase) && !id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)) break;
                        if (CM_Get_Parent(out uint parent, current, 0) != 0) break;
                        current = parent;
                    }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return devices;
        }

        internal static IReadOnlyList<BatteryDevice> ReadWiredHeadsets(CancellationToken token)
        {
            var rows = new List<BatteryDevice>();
            IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, "USB", IntPtr.Zero, AllPresentClasses);
            if (set == new IntPtr(-1)) return rows;
            try
            {
                var id = new StringBuilder(512);
                for (uint index = 0; ; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var node = new DevInfoData { Size = Marshal.SizeOf<DevInfoData>() };
                    if (!SetupDiEnumDeviceInfo(set, index, ref node)) break;
                    id.Clear();
                    if (!SetupDiGetDeviceInstanceIdW(set, ref node, id, (uint)id.Capacity, out _)) continue;
                    if (IsHecateG2II(id.ToString()))
                        rows.Add(new BatteryDevice(id.ToString(), "HECATE G2 II", DeviceKind.Headphones, true, null,
                            DeviceTransport.UsbCable, usbPowered: true));
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return rows;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DevInfoData { internal int Size; internal Guid ClassGuid; internal uint DevInst; internal UIntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DevPropKey
        {
            internal Guid Format; internal uint Property;
            internal DevPropKey(string format, uint property) { Format = new Guid(format); Property = property; }
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsByClass(ref Guid classId, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint instance, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint CM_Get_Device_IDW(uint instance, StringBuilder id, uint length, uint flags);
        [DllImport("cfgmgr32.dll", ExactSpelling = true)] private static extern uint CM_Get_DevNode_PropertyW(uint instance, ref DevPropKey key, out uint type, [Out] byte[] buffer, ref uint length, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DevInfoData info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref DevInfoData info, ref DevPropKey key, out uint type,
            [Out] byte[] buffer, uint bufferSize, out uint requiredSize, uint flags);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref DevInfoData info, StringBuilder id, uint size, out uint requiredSize);
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    }
}
