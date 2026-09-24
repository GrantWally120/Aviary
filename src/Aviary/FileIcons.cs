using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Aviary;

/// <summary>
/// The icon Windows gives a kind of file — the same one Explorer shows — for a row in the download
/// list (Alpha Build 5 M5). Safari shows the Finder's icon for each download, and a list of
/// identical grey rectangles is exactly what makes a download list hard to scan.
///
/// <para><b>By extension, never by file.</b> <c>SHGFI_USEFILEATTRIBUTES</c> asks Windows about a
/// name without touching the disk, so a file still downloading, or one since deleted, has its icon
/// too, and nothing a website named is ever opened to get it. The extension is reduced to letters
/// and digits first (<see cref="DownloadRecord.Extension"/>).</para>
///
/// <para>The icon arrives as an HICON; its colour bitmap is read out as BGRA, the mask applied for
/// the old icons that carry no alpha of their own, premultiplied as XAML expects, and cached per
/// extension for the life of the process.</para>
/// </summary>
internal static class FileIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(string extension)
    {
        string key = extension.Length == 0 ? "?" : extension;
        if (Cache.TryGetValue(key, out ImageSource? cached))
        {
            return cached;
        }

        ImageSource? made = null;
        try
        {
            made = Make(extension.Length == 0 ? "file" : "file." + extension);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "file icons: " + key);
        }

        Cache[key] = made;
        return made;
    }

    private static ImageSource? Make(string name)
    {
        SHFILEINFO info = new();
        IntPtr ok = SHGetFileInfo(name, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
        if (ok == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        IntPtr dc = IntPtr.Zero;
        ICONINFO icon = default;
        try
        {
            if (!GetIconInfo(info.hIcon, out icon) || icon.hbmColor == IntPtr.Zero)
            {
                return null;
            }

            BITMAP bm = default;
            if (GetObject(icon.hbmColor, Marshal.SizeOf<BITMAP>(), ref bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight <= 0)
            {
                return null;
            }

            int w = bm.bmWidth, h = bm.bmHeight;
            dc = GetDC(IntPtr.Zero);
            byte[] colour = Bits(dc, icon.hbmColor, w, h);

            bool hasAlpha = false;
            for (int i = 3; i < colour.Length; i += 4)
            {
                if (colour[i] != 0) { hasAlpha = true; break; }
            }

            // An icon from before alpha channels: its mask says what is transparent (white = see-through).
            if (!hasAlpha && icon.hbmMask != IntPtr.Zero)
            {
                byte[] mask = Bits(dc, icon.hbmMask, w, h);
                for (int i = 0; i < colour.Length; i += 4)
                {
                    colour[i + 3] = mask[i] == 0 ? (byte)255 : (byte)0;
                }
            }

            // XAML's bitmaps are premultiplied.
            for (int i = 0; i < colour.Length; i += 4)
            {
                int a = colour[i + 3];
                if (a < 255)
                {
                    colour[i] = (byte)(colour[i] * a / 255);
                    colour[i + 1] = (byte)(colour[i + 1] * a / 255);
                    colour[i + 2] = (byte)(colour[i + 2] * a / 255);
                }
            }

            WriteableBitmap bitmap = new(w, h);
            using (System.IO.Stream stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(colour, 0, colour.Length);
            }

            bitmap.Invalidate();
            return bitmap;
        }
        finally
        {
            if (dc != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, dc); }
            if (icon.hbmColor != IntPtr.Zero) { DeleteObject(icon.hbmColor); }
            if (icon.hbmMask != IntPtr.Zero) { DeleteObject(icon.hbmMask); }
            DestroyIcon(info.hIcon);
        }
    }

    /// <summary>A bitmap's pixels as top-down 32-bit BGRA, whatever its own depth.</summary>
    private static byte[] Bits(IntPtr dc, IntPtr bitmap, int w, int h)
    {
        BITMAPINFO bi = new()
        {
            // The HEADER's size (40), not the struct's: the palette after it is not part of it.
            biSize = 40,
            biWidth = w,
            biHeight = -h,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
            bmiColors = new uint[256],
        };

        byte[] pixels = new byte[w * h * 4];
        GetDIBits(dc, bitmap, 0, (uint)h, pixels, ref bi, 0);
        return pixels;
    }

    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;

        // Room for a palette, which GetDIBits may write for a 1-bit mask even when asked for 32 bits.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public uint[] bmiColors;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int c, ref BITMAP pv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, [Out] byte[] lpvBits, ref BITMAPINFO lpbmi, uint usage);
}
