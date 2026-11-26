using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace BlueGauge
{
    // CompX/Areson battery protocol documented by BatteryTray (see THIRD-PARTY-NOTICES.md).
    internal static class OemMouseBattery
    {
        internal const int FrameSize = 17;
        internal const byte Link = 0x03, Power = 0x04;

        internal static byte[] Request(byte command, int length)
        {
            if (command != Link && command != Power) throw new ArgumentOutOfRangeException(nameof(command));
            if (length < FrameSize) throw new ArgumentOutOfRangeException(nameof(length));
            var report = new byte[length];
            report[0] = 0x08;
            report[1] = command;
            report[16] = Checksum(report);
            return report;
        }

        internal static bool Valid(byte[] report, byte id, byte command) => report != null && report.Length >= FrameSize
            && report[0] == id && report[1] == command && report[16] == Checksum(report);

        internal static bool? IsLinked(byte[] reply, byte id)
        {
            if (!Valid(reply, id, Link) || reply[2] != 0 || reply[5] < 1 || reply[5] > 10) return null;
            return reply[6] != 0;
        }

        internal static int? ParsePower(byte[] reply, byte id, int scale, out bool charging)
        {
            charging = false;
            if (!Valid(reply, id, Power) || reply[2] != 0 || reply[5] < 1 || reply[5] > 10) return null;
            int level = reply[6] * scale;
            bool onCable = reply[5] >= 2 && reply[7] != 0;
            if (level > 100 || (level == 0 && !onCable)) return null;
            charging = onCable;
            return level;
        }

        private static byte Checksum(byte[] frame)
        {
            int sum = 0;
            for (int i = 0; i < FrameSize - 1; i++) sum += frame[i];
            return unchecked((byte)(0x55 - sum));
        }
    }

    internal sealed class HidBatterySource
    {
        private sealed class Profile
        {
            internal readonly string UsbId, Key, Name;
            internal readonly DeviceTransport Transport;
            internal readonly int Scale;
            internal readonly bool Split;
            internal bool Receiver => Transport == DeviceTransport.UsbReceiver;
            internal Profile(string usbId, string key, string name, DeviceTransport transport, int scale = 1, bool split = false)
            { UsbId = usbId; Key = key; Name = name; Transport = transport; Scale = scale; Split = split; }
        }

        private static readonly Profile[] Profiles = {
            new Profile("vid_3554&pid_f52e", "ardor-phantom-pro-v2", "ARDOR GAMING Phantom PRO V2", DeviceTransport.UsbCable),
            new Profile("vid_3554&pid_f52d", "ardor-phantom-pro-v2", "ARDOR GAMING Phantom PRO V2", DeviceTransport.UsbReceiver),
            new Profile("vid_25a7&pid_fa4a", "ardor-renegade", "ARDOR GAMING Renegade", DeviceTransport.UsbReceiver, 10, true)
        };

        internal IReadOnlyList<BatteryDevice> Read(CancellationToken token, Action<string> diagnostic = null)
        {
            var rows = new List<BatteryDevice>();
            var collections = HidPort.Enumerate(Profiles.Select(p => p.UsbId).ToArray(), token);
            foreach (var profile in Profiles)
            {
                token.ThrowIfCancellationRequested();
                if (rows.Any(d => d.Id == profile.Key)) continue; // Prefer a plugged-in cable over the same mouse's dongle.
                var matches = collections.Where(c => c.Path.IndexOf(profile.UsbId, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                foreach (var c in matches)
                    diagnostic?.Invoke($"  {profile.UsbId} HID {c.Page:X4}:{c.Usage:X4} in/out/feature={c.Input}/{c.Output}/{c.Feature}");
                var writers = matches.Where(c => c.Page == 0xff02 && c.Usage == 2 && (profile.Split
                    ? c.Feature == OemMouseBattery.FrameSize : c.Input == OemMouseBattery.FrameSize && c.Output == OemMouseBattery.FrameSize));
                bool found = false;
                foreach (var writer in writers)
                {
                    var reader = profile.Split ? matches.FirstOrDefault(c => c.Input == OemMouseBattery.FrameSize && c.Page == 0xff01
                        && c.Path.StartsWith(writer.Path.Split(new[] { "&col" }, StringSplitOptions.None)[0], StringComparison.OrdinalIgnoreCase)) : writer;
                    if (reader == null) continue;
                    try
                    {
                        var row = Query(profile, writer, reader, token, diagnostic);
                        if (row != null) { rows.Add(row); found = true; break; }
                    }
                    catch (Win32Exception ex) { diagnostic?.Invoke("  HID error " + ex.NativeErrorCode); }
                }
                // A plugged-in known receiver remains on the panel while its mouse sleeps.
                // Do not mark an adapter visible merely from a generic HID vendor ID.
                if (!found && profile.Receiver && writers.Any())
                    rows.Add(new BatteryDevice(profile.Key, profile.Name, DeviceKind.Mouse, true, null, profile.Transport));
            }
            rows.AddRange(PnpBatteryReader.ReadWiredHeadsets(token));
            return rows;
        }

        private static BatteryDevice Query(Profile profile, HidPort.Collection writer, HidPort.Collection reader,
            CancellationToken token, Action<string> trace)
        {
            using (var output = HidPort.Open(writer.Path, profile.Split ? HidPort.WriteAccess : HidPort.ReadAccess | HidPort.WriteAccess, !profile.Split))
            using (var input = profile.Split ? HidPort.Open(reader.Path, HidPort.ReadAccess, true) : null)
            {
                const int timeout = 500;
                var readHandle = input ?? output;
                byte id = profile.Split ? (byte)9 : (byte)8;
                byte[] Ask(byte command)
                {
                    token.ThrowIfCancellationRequested();
                    HidPort.HidD_FlushQueue(readHandle);
                    byte[] request = OemMouseBattery.Request(command, profile.Split ? writer.Feature : writer.Output);
                    var clock = Stopwatch.StartNew();
                    if (!(profile.Split ? HidPort.HidD_SetFeature(output, request, request.Length)
                        : HidPort.Transfer(output, request, true, timeout, token) == request.Length)) return null;
                    int remaining;
                    while ((remaining = timeout - (int)clock.ElapsedMilliseconds) > 0)
                    {
                        var reply = new byte[reader.Input];
                        if (HidPort.Transfer(readHandle, reply, false, remaining, token) != reply.Length) break;
                        trace?.Invoke("  HID reply: " + BitConverter.ToString(reply));
                        if (OemMouseBattery.Valid(reply, id, command)) return reply;
                    }
                    return null;
                }

                // Link status prevents a receiver from displaying a cached level while the mouse is off.
                bool? linked = profile.Receiver ? OemMouseBattery.IsLinked(Ask(OemMouseBattery.Link), id) : true;
                if (linked == false) return null;
                int? percent = OemMouseBattery.ParsePower(Ask(OemMouseBattery.Power), id, profile.Scale, out bool charging);
                if (profile.UsbId == "vid_3554&pid_f52d" && linked != true && !charging) return null;
                if (!percent.HasValue && (!profile.Receiver || linked != true)) return null;
                return new BatteryDevice(profile.Key, profile.Name, DeviceKind.Mouse, true, percent, profile.Transport, charging);
            }
        }
    }

    internal static class HidPort
    {
        internal const uint ReadAccess = 0x80000000, WriteAccess = 0x40000000;
        private const int Pending = 997, NoMoreItems = 259, InsufficientBuffer = 122;
        private const uint OverlappedFlag = 0x40000000;

        internal sealed class Collection
        {
            internal string Path;
            internal ushort Page, Usage, Input, Output, Feature;
        }

        internal static List<Collection> Enumerate(string[] ids, CancellationToken token)
        {
            var result = new List<Collection>();
            HidD_GetHidGuid(out Guid guid);
            IntPtr set = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
            if (set == new IntPtr(-1)) return result;
            try
            {
                for (uint index = 0; ; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var data = new InterfaceData { Size = Marshal.SizeOf(typeof(InterfaceData)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err == NoMoreItems) break;
                        throw new Win32Exception(err);
                    }
                    SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
                    if (Marshal.GetLastWin32Error() != InsufficientBuffer || size < 6) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, size, out _, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        if (path == null || !ids.Any(id => path.IndexOf(id + "&", StringComparison.OrdinalIgnoreCase) >= 0
                           || path.IndexOf(id + "#", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        using (var handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                        {
                            if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out IntPtr preparsed)) continue;
                            try
                            {
                                if (HidP_GetCaps(preparsed, out var caps) != 0x00110000) continue;
                                result.Add(new Collection { Path = path, Page = caps.UsagePage, Usage = caps.Usage,
                                    Input = caps.Input, Output = caps.Output, Feature = caps.Feature });
                            }
                            finally { HidD_FreePreparsedData(preparsed); }
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        internal static SafeFileHandle Open(string path, uint access, bool async)
        {
            var handle = CreateFileW(path, access, 3, IntPtr.Zero, 3, async ? OverlappedFlag : 0, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        internal static int Transfer(SafeFileHandle handle, byte[] report, bool write, int timeout, CancellationToken token)
        {
            using (var completed = new ManualResetEvent(false))
            {
                IntPtr ov = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Overlapped)));
                var bytes = GCHandle.Alloc(report, GCHandleType.Pinned);
                bool pending = false;
                try
                {
                    token.ThrowIfCancellationRequested();
                    Marshal.StructureToPtr(new Overlapped { Event = completed.SafeWaitHandle.DangerousGetHandle() }, ov, false);
                    bool done = write ? WriteFile(handle, bytes.AddrOfPinnedObject(), report.Length, out int count, ov)
                        : ReadFile(handle, bytes.AddrOfPinnedObject(), report.Length, out count, ov);
                    if (done) return count;
                    int err = Marshal.GetLastWin32Error();
                    if (err != Pending) throw new Win32Exception(err);
                    pending = true;
                    int signal = WaitHandle.WaitAny(new[] { completed, token.WaitHandle }, timeout);
                    if (signal != 0) { token.ThrowIfCancellationRequested(); return 0; }
                    bool ok = GetOverlappedResult(handle, ov, out count, false);
                    err = Marshal.GetLastWin32Error();
                    pending = false;
                    if (!ok) throw new Win32Exception(err);
                    return count;
                }
                finally
                {
                    // Do not leave a canceled read alive to consume the reply to the next request.
                    if (pending) { CancelIoEx(handle, ov); GetOverlappedResult(handle, ov, out _, true); }
                    bytes.Free();
                    Marshal.FreeHGlobal(ov);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { internal int Size; internal Guid Guid; internal uint Flags; internal UIntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential, Size = 64)] private struct Caps { internal ushort Usage, UsagePage, Input, Output, Feature; }
        [StructLayout(LayoutKind.Sequential)] private struct Overlapped { internal IntPtr Internal, InternalHigh; internal uint Offset, OffsetHigh; internal IntPtr Event; }
        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle file, out IntPtr data);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out Caps caps);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_FlushQueue(SafeFileHandle file);
        [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_SetFeature(SafeFileHandle file, byte[] data, int length);
        [DllImport("setupapi.dll", SetLastError = true, ExactSpelling = true)] private static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref InterfaceData data);
        [DllImport("setupapi.dll", SetLastError = true, ExactSpelling = true)] private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint needed, IntPtr info);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, int length, out int count, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeFileHandle file, IntPtr buffer, int length, out int count, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out int count, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
    }
}
