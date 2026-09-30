using System.Runtime.InteropServices;

namespace SpaceRemoteViewer;

/// <summary>Colours and small helpers shared by both windows, matching the Android app.</summary>
static class Theme
{
    public static readonly Color Ink = Color.FromArgb(7, 10, 24);
    public static readonly Color Deep = Color.FromArgb(20, 26, 60);
    public static readonly Color Surface = Color.FromArgb(23, 29, 58);
    public static readonly Color SurfaceHi = Color.FromArgb(34, 42, 82);
    public static readonly Color Accent = Color.FromArgb(124, 156, 255);
    public static readonly Color AccentHi = Color.FromArgb(150, 176, 255);
    public static readonly Color Mint = Color.FromArgb(91, 224, 192);
    public static readonly Color Muted = Color.FromArgb(152, 162, 200);
    public static readonly Color Danger = Color.FromArgb(255, 138, 128);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar on Windows 10 (20H1+) and 11. Silently ignored elsewhere.</summary>
    public static void DarkTitleBar(Form form)
    {
        try
        {
            int on = 1;
            DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int));
        }
        catch { }
    }

    public static Button FlatButton(string text, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : SurfaceHi,
            ForeColor = primary ? Ink : Color.White,
            Font = new Font("Segoe UI", primary ? 11f : 9.5f, primary ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand,
            AutoSize = !primary,
            Padding = new Padding(8, 2, 8, 2),
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentHi : Color.FromArgb(48, 58, 108);
        b.FlatAppearance.MouseDownBackColor = primary ? Mint : Color.FromArgb(60, 72, 130);
        return b;
    }

    public static ContextMenuStrip DarkMenu() =>
        new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkColors()),
            BackColor = Surface,
            ForeColor = Color.White,
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 10f),
        };

    sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuItemSelected => SurfaceHi;
        public override Color MenuItemBorder => SurfaceHi;
        public override Color MenuBorder => SurfaceHi;
        public override Color MenuItemSelectedGradientBegin => SurfaceHi;
        public override Color MenuItemSelectedGradientEnd => SurfaceHi;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => SurfaceHi;
        public override Color SeparatorLight => SurfaceHi;
    }
}
