using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SpaceRemoteViewer;

/// <summary>Draws the remote screen letterboxed, plus the waiting animation and status banners.</summary>
sealed class RemoteCanvas : Control
{
    Bitmap frame;
    RectangleF dst;
    float fade;          // 0..1, first frame fades in
    float spin;          // waiting animation angle
    readonly System.Windows.Forms.Timer anim = new() { Interval = 16 };

    public string Banner { get; set; }

    public RemoteCanvas()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        BackColor = Color.Black;
        TabStop = true;
        Cursor = Cursors.Cross;
        anim.Tick += (_, _) =>
        {
            spin = (spin + 4.5f) % 360f;
            if (frame != null && fade < 1f) fade = Math.Min(1f, fade + 0.06f);
            if (frame == null || fade < 1f) Invalidate();
        };
        anim.Start();
    }

    public bool HasFrame => frame != null;

    /// <summary>Takes ownership of the bitmap. Call on the UI thread.</summary>
    public void SetFrame(Bitmap bmp)
    {
        var old = frame;
        frame = bmp;
        old?.Dispose();
        Invalidate();
    }

    public void ClearFrame()
    {
        frame?.Dispose();
        frame = null;
        fade = 0f;
        Invalidate();
    }

    /// <summary>Converts a point on this control to 0..1 screen coordinates, or null outside the image.</summary>
    public PointF? ToRemote(Point p, bool clamp)
    {
        if (frame == null || dst.Width <= 0 || dst.Height <= 0) return null;
        float x = (p.X - dst.X) / dst.Width;
        float y = (p.Y - dst.Y) / dst.Height;
        if (!clamp && (x < 0 || x > 1 || y < 0 || y > 1)) return null;
        return new PointF(Math.Clamp(x, 0f, 1f), Math.Clamp(y, 0f, 1f));
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Black);

        if (frame == null)
        {
            DrawWaiting(g);
            return;
        }

        float scale = Math.Min(Width / (float)frame.Width, Height / (float)frame.Height);
        float w = frame.Width * scale, h = frame.Height * scale;
        dst = new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);

        g.InterpolationMode = Math.Abs(scale - 1f) < 0.01f ? InterpolationMode.NearestNeighbor
            : scale < 1f ? InterpolationMode.HighQualityBilinear : InterpolationMode.Bilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        if (fade < 1f)
        {
            // fade and settle the first frame instead of popping it in
            float grow = 0.985f + 0.015f * fade;
            var r = new RectangleF(dst.X + dst.Width * (1 - grow) / 2, dst.Y + dst.Height * (1 - grow) / 2,
                                   dst.Width * grow, dst.Height * grow);
            using var attrs = new ImageAttributes();
            attrs.SetColorMatrix(new ColorMatrix { Matrix33 = fade });
            g.DrawImage(frame, Rectangle.Round(r), 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, attrs);
        }
        else
        {
            g.DrawImage(frame, dst);
        }

        if (!string.IsNullOrEmpty(Banner)) DrawBanner(g, Banner);
    }

    void DrawWaiting(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = Width / 2f, cy = Height / 2f - 16;
        using (var outer = new Pen(Theme.Accent, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(outer, cx - 40, cy - 40, 80, 80, spin, 110);
        using (var inner = new Pen(Theme.Mint, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(inner, cx - 26, cy - 26, 52, 52, -spin * 0.65f + 40, 80);

        using var font = new Font("Segoe UI", 10.5f);
        using var brush = new SolidBrush(Theme.Muted);
        const string text = "Waiting for the first frame…";
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, brush, cx - size.Width / 2, cy + 60);
    }

    void DrawBanner(Graphics g, string text)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font("Segoe UI", 10f);
        var size = g.MeasureString(text, font);
        var rect = new RectangleF((Width - size.Width) / 2 - 14, 14, size.Width + 28, size.Height + 12);
        using (var path = Rounded(rect, 10))
        using (var bg = new SolidBrush(Color.FromArgb(210, 10, 14, 30)))
            g.FillPath(bg, path);
        g.DrawString(text, font, Brushes.White, rect.X + 14, rect.Y + 6);
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            anim.Dispose();
            frame?.Dispose();
        }
        base.Dispose(disposing);
    }
}
