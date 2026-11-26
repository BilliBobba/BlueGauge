using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BlueGauge;

internal static class LayeredSurface
{
    internal static void Present(IntPtr window, Rectangle bounds, Bitmap image)
    {
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new Win32Exception();
        IntPtr dib = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            var header = new BitmapHeader { Size = 40, Width = image.Width, Height = -image.Height, Planes = 1, Bits = 32 };
            dib = CreateDIBSection(dc, in header, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) throw new Win32Exception();
            var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int stride = image.Width * 4;
                var row = new byte[stride];
                for (int y = 0; y < image.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, stride);
                    Marshal.Copy(row, 0, IntPtr.Add(bits, y * stride), stride);
                }
            }
            finally { image.UnlockBits(data); }
            previous = SelectObject(dc, dib);
            var dest = new Vector(bounds.X, bounds.Y);
            var size = new Vector(image.Width, image.Height);
            var origin = new Vector(0, 0);
            var blend = new Blend { Opacity = 255, AlphaFormat = 1 };
            if (!UpdateLayeredWindow(window, IntPtr.Zero, in dest, in size, dc, in origin, 0, in blend, 2))
                throw new Win32Exception();
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(dc, previous);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private readonly record struct Vector(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private struct Blend { internal byte Operation, Flags, Opacity, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, Bits;
        internal uint Compression, ImageSize;
        internal int XPixelsPerMeter, YPixelsPerMeter;
        internal uint ColorsUsed, ColorsImportant;
    }
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, in BitmapHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destDc,
        in Vector dest, in Vector size, IntPtr sourceDc, in Vector source, uint key, in Blend blend, uint flags);
}
