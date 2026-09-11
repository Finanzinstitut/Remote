using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SpaceRemote;

/// <summary>Nimmt den Hauptbildschirm inkl. Mauszeiger auf und liefert ein JPEG.</summary>
sealed class ScreenCapture : IDisposable
{
    readonly ImageCodecInfo jpegCodec =
        ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    Bitmap full;
    Bitmap scaled;

    public byte[] CaptureJpeg(int maxWidth, int quality)
    {
        Rectangle b = Screen.PrimaryScreen.Bounds;

        if (full == null || full.Width != b.Width || full.Height != b.Height)
        {
            full?.Dispose();
            full = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb);
        }

        using (var g = Graphics.FromImage(full))
        {
            g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size, CopyPixelOperation.SourceCopy);
            DrawCursor(g, b);
        }

        Bitmap output = full;
        if (b.Width > maxWidth)
        {
            int w = maxWidth;
            int h = (int)Math.Round(b.Height * (double)maxWidth / b.Width);
            if (scaled == null || scaled.Width != w || scaled.Height != h)
            {
                scaled?.Dispose();
                scaled = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            }
            using var g2 = Graphics.FromImage(scaled);
            g2.InterpolationMode = InterpolationMode.Bilinear;
            g2.PixelOffsetMode = PixelOffsetMode.Half;
            g2.DrawImage(full, 0, 0, w, h);
            output = scaled;
        }

        using var ms = new MemoryStream();
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
        output.Save(ms, jpegCodec, ep);
        return ms.ToArray();
    }

    // ---- Mauszeiger einzeichnen ----
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; }
    [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO pci);
    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, int step, IntPtr brush, int flags);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);

    const int CURSOR_SHOWING = 1;
    const int DI_NORMAL = 3;

    static void DrawCursor(Graphics g, Rectangle bounds)
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || (ci.flags & CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero) return;

        int hx = 0, hy = 0;
        if (GetIconInfo(ci.hCursor, out var ii))
        {
            hx = ii.xHotspot;
            hy = ii.yHotspot;
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
        }

        IntPtr hdc = g.GetHdc();
        try
        {
            DrawIconEx(hdc, ci.ptScreenPos.X - bounds.X - hx, ci.ptScreenPos.Y - bounds.Y - hy,
                ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
        }
        finally { g.ReleaseHdc(hdc); }
    }

    public void Dispose()
    {
        full?.Dispose();
        scaled?.Dispose();
    }
}
