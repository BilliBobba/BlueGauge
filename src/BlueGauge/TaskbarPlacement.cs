using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace BlueGauge
{
    internal readonly record struct TaskbarSlot(Rectangle Free, int Dpi);

    internal static class TaskbarPlacement
    {
        private static readonly PropertyCondition Start = new(AutomationElement.AutomationIdProperty, "StartButton");
        private static readonly PropertyCondition Widgets = new(AutomationElement.AutomationIdProperty, "WidgetsButton");

        internal static TaskbarSlot? Probe()
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return null;
            // UIA rectangles are physical pixels even in a host that uses DPI virtualization.
            IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2
            try { return ProbePhysical(); }
            finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
        }

        private static TaskbarSlot? ProbePhysical()
        {
            IntPtr taskbar = FindWindowW("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero || !IsWindowVisible(taskbar) || !GetWindowRect(taskbar, out var bar)) return null;
            IntPtr monitor = MonitorFromWindow(taskbar, 2);
            var mi = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfoW(monitor, ref mi)) return null;
            var visibleBar = Rectangle.Intersect(bar.Rectangle, mi.Monitor.Rectangle);
            int dpi = (int)GetDpiForWindow(taskbar);
            if (dpi <= 0) dpi = 96;
            // Win11 uses a horizontal taskbar. Don't draw while it is sliding off-screen in auto-hide mode.
            if (visibleBar.Width < visibleBar.Height * 2 || visibleBar.Height < Scale(30, dpi) || visibleBar.Height < bar.Rectangle.Height - 3)
                return null;
            if (IsFullscreen(taskbar, monitor, mi.Monitor.Rectangle)) return null;

            try
            {
                var root = AutomationElement.FromHandle(taskbar);
                var start = Bounds(root.FindFirst(TreeScope.Descendants, Start));
                if (start.IsEmpty || !visibleBar.IntersectsWith(start)) return null;
                var widgets = Bounds(root.FindFirst(TreeScope.Descendants, Widgets));
                var free = FindFreeSpace(visibleBar, start, widgets, Scale(6, dpi));
                return free.IsEmpty ? null : new TaskbarSlot(free, dpi);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
            {
                // Explorer may have restarted or the taskbar is not ready. Try again next tick.
                return null;
            }
        }

        internal static Rectangle FindFreeSpace(Rectangle bar, Rectangle start, Rectangle widgets, int gap)
        {
            int left = bar.Left + gap;
            if (!widgets.IsEmpty && widgets.Left < start.Left && bar.IntersectsWith(widgets))
                left = Math.Max(left, widgets.Right + gap);
            int right = Math.Min(start.Left, bar.Right) - gap;
            if (right <= left || bar.Height <= 0) return Rectangle.Empty;
            return Rectangle.FromLTRB(left, bar.Top, right, bar.Bottom);
        }

        internal static int Scale(int px, int dpi) => (int)Math.Round(px * dpi / 96d);

        private static Rectangle Bounds(AutomationElement element)
        {
            if (element == null || element.Current.IsOffscreen) return Rectangle.Empty;
            var r = element.Current.BoundingRectangle;
            if (r.IsEmpty || !double.IsFinite(r.X + r.Y + r.Width + r.Height)) return Rectangle.Empty;
            return Rectangle.FromLTRB((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
        }

        private static bool IsFullscreen(IntPtr taskbar, IntPtr monitor, Rectangle area)
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == taskbar || foreground == GetShellWindow()
                || MonitorFromWindow(foreground, 0) != monitor || IsIconic(foreground)) return false;
            if (SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3) return true;
            if (DwmGetWindowAttribute(foreground, 9, out var frame, Marshal.SizeOf<NativeRect>()) != 0
                && !GetWindowRect(foreground, out frame)) return false;
            var rect = frame.Rectangle;
            return rect.Left <= area.Left + 1 && rect.Right >= area.Right - 1 && rect.Top <= area.Top + 1 && rect.Bottom >= area.Bottom - 1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            internal int Left, Top, Right, Bottom;
            internal readonly Rectangle Rectangle => Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { internal int Size; internal NativeRect Monitor, Work; internal uint Flags; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern IntPtr FindWindowW(string cls, string title);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect value, int size);
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    }
}
