using System.Drawing.Drawing2D;

namespace SpaceRemoteViewer;

/// <summary>Round start button with a breathing halo. While busy it turns into an orbit spinner.</summary>
sealed class PowerButton : Control
{
    readonly System.Windows.Forms.Timer anim = new() { Interval = 16 };
    float pulse;        // 0..1 loop for the halo rings
    float spin;         // spinner angle
    float hover;        // 0..1 eased hover amount
    float press;        // 0..1 eased press amount
    bool isHover, isDown, busy;

    public bool Busy
    {
        get => busy;
        set { busy = value; Invalidate(); }
    }

    public PowerButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        anim.Tick += (_, _) =>
        {
            pulse = (pulse + 1f / 160f) % 1f;
            spin = (spin + 4.5f) % 360f;
            hover += ((isHover ? 1f : 0f) - hover) * 0.2f;
            press += ((isDown ? 1f : 0f) - press) * 0.35f;
            Invalidate();
        };
        anim.Start();
    }

    protected override void OnMouseEnter(EventArgs e) { isHover = true; base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { isHover = false; isDown = false; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { isDown = true; base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { isDown = false; base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = Width / 2f, cy = Height / 2f;
        float max = Math.Min(Width, Height) / 2f;

        if (busy)
        {
            float r1 = max * 0.42f, r2 = max * 0.28f;
            using (var p1 = new Pen(Theme.Accent, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(p1, cx - r1, cy - r1, r1 * 2, r1 * 2, spin, 110);
            using (var p2 = new Pen(Theme.Mint, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(p2, cx - r2, cy - r2, r2 * 2, r2 * 2, -spin * 0.65f + 40, 80);
            return;
        }

        // two halo rings drifting outwards, half a cycle apart
        for (int i = 0; i < 2; i++)
        {
            float p = (pulse + i * 0.5f) % 1f;
            float r = max * (0.66f + 0.32f * p);
            using var pen = new Pen(Color.FromArgb((int)(90 * (1 - p)), Theme.Accent), 2f);
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
        }

        float scale = 1f + 0.03f * hover - 0.06f * press;
        float rb = max * 0.64f * scale;
        var rect = new RectangleF(cx - rb, cy - rb, rb * 2, rb * 2);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(rect);
            using var fill = new PathGradientBrush(path)
            {
                CenterColor = Theme.AccentHi,
                SurroundColors = new[] { Theme.Accent },
                CenterPoint = new PointF(cx, cy - rb * 0.3f),
            };
            g.FillPath(fill, path);
        }

        // power symbol
        float ri = rb * 0.34f;
        using (var pen = new Pen(Theme.Ink, rb * 0.075f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawArc(pen, cx - ri, cy - ri - rb * 0.08f, ri * 2, ri * 2, -60, 300);
            g.DrawLine(pen, cx, cy - ri - rb * 0.22f, cx, cy - rb * 0.1f);
        }

        using var font = new Font("Segoe UI Semibold", Math.Max(8f, rb * 0.13f));
        using var brush = new SolidBrush(Theme.Ink);
        const string label = "Connect";
        var size = g.MeasureString(label, font);
        g.DrawString(label, font, brush, cx - size.Width / 2, cy + rb * 0.36f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) anim.Dispose();
        base.Dispose(disposing);
    }
}
