namespace SpaceRemoteViewer;

sealed class ViewerForm : Form
{
    const int CTRL = 1, ALT = 2, SHIFT = 4, WIN = 8;

    readonly RemoteConnection conn;
    readonly Settings settings;
    readonly string hostName;
    readonly RemoteCanvas canvas;
    readonly Panel toolbar;
    readonly Label statusLabel;
    readonly Button systemKeysButton;
    readonly KeyboardHook hook;
    readonly System.Windows.Forms.Timer moveTimer = new() { Interval = 15 };
    readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 1000 };
    readonly System.Windows.Forms.Timer revealTimer = new() { Interval = 16 };

    // frames arrive on the network thread; only the newest one is kept
    Bitmap pendingFrame;
    int framePosted;
    int framesThisSecond;

    // mouse
    PointF? pendingMove;
    int buttonsDown;

    // keyboard state as seen through the hook
    bool ctrl, alt, shift, winDown, winUsed;
    bool systemKeys;
    int menusOpen;

    // fullscreen with a toolbar that slides in from the top edge
    bool fullscreen;
    float reveal = 1f, revealTarget = 1f;
    Rectangle windowedBounds;
    FormWindowState windowedState;

    bool closing;

    public string EndMessage { get; private set; }
    public bool EndIsError { get; private set; }

    public ViewerForm(RemoteConnection conn, string hostName, Settings settings)
    {
        this.conn = conn;
        this.hostName = hostName;
        this.settings = settings;
        systemKeys = settings.SystemKeys;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"{hostName} — Space Remote Viewer";
        BackColor = Color.Black;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        var area = Screen.PrimaryScreen.WorkingArea;
        Size = new Size((int)(area.Width * 0.8), (int)(area.Height * 0.85));
        MinimumSize = new Size(640, 420);

        canvas = new RemoteCanvas();
        toolbar = new Panel { BackColor = Color.FromArgb(16, 20, 44), Height = 44 };

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(6, 0, 0, 0),
        };

        var fullscreenButton = ToolButton("⛶  Fullscreen");
        fullscreenButton.Click += (_, _) => ToggleFullscreen();

        var keysButton = ToolButton("⌨  Keys ▾");
        var keysMenu = BuildKeysMenu();
        keysButton.Click += (_, _) => keysMenu.Show(keysButton, new Point(0, keysButton.Height));

        var powerButton = ToolButton("⏻  Power ▾");
        var powerMenu = BuildPowerMenu();
        powerButton.Click += (_, _) => powerMenu.Show(powerButton, new Point(0, powerButton.Height));

        systemKeysButton = ToolButton("");
        systemKeysButton.Click += (_, _) =>
        {
            systemKeys = !systemKeys;
            settings.SystemKeys = systemKeys;
            settings.Save();
            UpdateSystemKeysButton();
            canvas.Focus();
        };
        UpdateSystemKeysButton();

        statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Theme.Muted,
            Margin = new Padding(14, 13, 0, 0),
            BackColor = Color.Transparent,
        };

        left.Controls.AddRange(new Control[] { fullscreenButton, keysButton, powerButton, systemKeysButton, statusLabel });

        var disconnect = ToolButton("Disconnect");
        disconnect.Dock = DockStyle.Right;
        disconnect.AutoSize = false;
        disconnect.Width = 110;
        disconnect.ForeColor = Theme.Danger;
        disconnect.Click += (_, _) => Close();

        toolbar.Controls.Add(left);
        toolbar.Controls.Add(disconnect);

        Controls.Add(canvas);
        Controls.Add(toolbar);
        toolbar.BringToFront();
        ResumeLayout(false);

        // --- network ---
        conn.FrameReceived += OnFrame;
        conn.Disconnected += msg =>
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (closing) return;
                    EndMessage = msg == null ? null : "Disconnected: " + msg;
                    EndIsError = msg != null;
                    Close();
                }));
            }
            catch { }
        };

        // --- mouse ---
        canvas.MouseMove += OnCanvasMouseMove;
        canvas.MouseDown += OnCanvasMouseDown;
        canvas.MouseUp += OnCanvasMouseUp;
        canvas.MouseWheel += (_, e) => conn.Scroll(e.Delta);
        canvas.MouseEnter += (_, _) => { if (ActiveForm == this) canvas.Focus(); };
        moveTimer.Tick += (_, _) => FlushMove();
        moveTimer.Start();

        // --- keyboard ---
        hook = new KeyboardHook { Handler = OnHookKey };
        Deactivate += (_, _) => ResetModifiers();

        // --- status + fullscreen toolbar ---
        statusTimer.Tick += (_, _) => UpdateStatus();
        statusTimer.Start();
        revealTimer.Tick += (_, _) =>
        {
            reveal += (revealTarget - reveal) * 0.25f;
            if (Math.Abs(reveal - revealTarget) < 0.01f) { reveal = revealTarget; revealTimer.Stop(); }
            LayoutAll();
        };

        UpdateStatus();
        conn.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        canvas.Focus();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        LayoutAll();
    }

    int Scaled(int px) => (int)Math.Round(px * DeviceDpi / 96f);

    void LayoutAll()
    {
        if (canvas == null || toolbar == null) return;
        int tb = Scaled(44);
        toolbar.Height = tb;
        toolbar.Width = ClientSize.Width;
        toolbar.Left = 0;
        if (fullscreen)
        {
            canvas.Bounds = ClientRectangle;
            toolbar.Top = (int)Math.Round(-tb + tb * reveal);
            toolbar.Visible = reveal > 0.01f;
        }
        else
        {
            toolbar.Top = 0;
            toolbar.Visible = true;
            canvas.Bounds = new Rectangle(0, tb, ClientSize.Width, Math.Max(0, ClientSize.Height - tb));
        }
    }

    static Button ToolButton(string text)
    {
        var b = Theme.FlatButton(text);
        b.BackColor = Color.FromArgb(16, 20, 44);
        b.Margin = new Padding(2, 6, 2, 6);
        b.Height = 32;
        b.TabStop = false;
        return b;
    }

    void UpdateSystemKeysButton()
    {
        systemKeysButton.Text = systemKeys ? "System keys: On" : "System keys: Off";
        systemKeysButton.ForeColor = systemKeys ? Theme.Mint : Theme.Muted;
    }

    // ------------------------------------------------------------------ menus

    ContextMenuStrip TrackMenu(ContextMenuStrip menu)
    {
        menu.Opened += (_, _) => menusOpen++;
        menu.Closed += (_, _) => { menusOpen = Math.Max(0, menusOpen - 1); canvas.Focus(); };
        return menu;
    }

    ContextMenuStrip BuildKeysMenu()
    {
        var m = TrackMenu(Theme.DarkMenu());
        m.Items.Add("Task Manager  (Ctrl+Shift+Esc)", null, (_, _) => conn.Key(0x1B, CTRL | SHIFT));
        m.Items.Add("Windows key", null, (_, _) => conn.Key(0x5B, 0));
        m.Items.Add("Alt+Tab", null, (_, _) => conn.Key(0x09, ALT));
        m.Items.Add("Alt+F4", null, (_, _) => conn.Key(0x73, ALT));
        m.Items.Add("Show desktop  (Win+D)", null, (_, _) => conn.Key(0x44, WIN));
        m.Items.Add("Print Screen", null, (_, _) => conn.Key(0x2C, 0));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Type my clipboard text", null, (_, _) =>
        {
            try
            {
                if (Clipboard.ContainsText()) conn.Text(Clipboard.GetText());
            }
            catch { }
        });
        return m;
    }

    ContextMenuStrip BuildPowerMenu()
    {
        var m = TrackMenu(Theme.DarkMenu());
        m.Items.Add("Lock", null, (_, _) => conn.Power(3));
        m.Items.Add("Sleep", null, (_, _) => PowerAndLeave(2, "The PC is asleep."));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Restart", null, (_, _) =>
        {
            if (Confirm("Restart the remote PC?")) PowerAndLeave(1, "The PC is restarting. Connect again in a moment.");
        });
        m.Items.Add("Shut down", null, (_, _) =>
        {
            if (Confirm("Shut down the remote PC?")) PowerAndLeave(0, "The PC was shut down.");
        });
        return m;
    }

    bool Confirm(string question) =>
        MessageBox.Show(this, question, "Space Remote Viewer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    async void PowerAndLeave(int action, string message)
    {
        conn.Power(action);
        await Task.Delay(700);
        EndMessage = message;
        EndIsError = false;
        Close();
    }

    // ------------------------------------------------------------------ frames

    void OnFrame(Bitmap bmp)
    {
        Interlocked.Increment(ref framesThisSecond);
        Interlocked.Exchange(ref pendingFrame, bmp)?.Dispose();
        if (Interlocked.Exchange(ref framePosted, 1) == 0)
        {
            try { BeginInvoke(new Action(ApplyFrame)); }
            catch { Interlocked.Exchange(ref pendingFrame, null)?.Dispose(); }
        }
    }

    void ApplyFrame()
    {
        Volatile.Write(ref framePosted, 0);
        var bmp = Interlocked.Exchange(ref pendingFrame, null);
        if (bmp == null) return;
        if (closing) { bmp.Dispose(); return; }
        canvas.SetFrame(bmp);
    }

    void UpdateStatus()
    {
        int fps = Interlocked.Exchange(ref framesThisSecond, 0);
        statusLabel.Text = canvas.HasFrame ? $"{hostName}  ·  {fps} fps" : $"{hostName}  ·  connecting…";

        bool stale = canvas.HasFrame && Environment.TickCount64 - conn.LastFrameAt > 4000;
        string banner = stale ? "No image — PC locked or on the sign-in screen?" : null;
        if (banner != canvas.Banner)
        {
            canvas.Banner = banner;
            canvas.Invalidate();
        }
    }

    // ------------------------------------------------------------------ mouse

    static int MapButton(MouseButtons b) => b switch
    {
        MouseButtons.Left => 0,
        MouseButtons.Right => 1,
        MouseButtons.Middle => 2,
        _ => -1,
    };

    void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (fullscreen)
        {
            int tb = Scaled(44);
            if (e.Y <= 2 && revealTarget < 1f) SetReveal(1f);
            else if (e.Y > tb + Scaled(40) && revealTarget > 0f && menusOpen == 0) SetReveal(0f);
        }

        var p = canvas.ToRemote(e.Location, clamp: buttonsDown > 0);
        if (p != null) pendingMove = p;
    }

    void FlushMove()
    {
        if (pendingMove is PointF p)
        {
            conn.Move(p.X, p.Y);
            pendingMove = null;
        }
    }

    void OnCanvasMouseDown(object sender, MouseEventArgs e)
    {
        canvas.Focus();
        int b = MapButton(e.Button);
        if (b < 0) return;
        var p = canvas.ToRemote(e.Location, clamp: false);
        if (p == null) return;
        pendingMove = p;
        FlushMove();
        buttonsDown++;
        conn.Button(b, true);
    }

    void OnCanvasMouseUp(object sender, MouseEventArgs e)
    {
        int b = MapButton(e.Button);
        if (b < 0 || buttonsDown == 0) return;
        var p = canvas.ToRemote(e.Location, clamp: true);
        if (p != null) pendingMove = p;
        FlushMove();
        buttonsDown--;
        conn.Button(b, false);
    }

    // ------------------------------------------------------------------ keyboard

    void ResetModifiers()
    {
        ctrl = alt = shift = winDown = winUsed = false;
    }

    /// <summary>Called by the low-level hook for every physical key. Return true to keep it away from the local PC.</summary>
    bool OnHookKey(int vk, bool down)
    {
        if (closing || ActiveForm != this || menusOpen > 0) return false;

        switch (vk)
        {
            case 0xA2: case 0xA3: ctrl = down; return systemKeys;
            case 0xA4: case 0xA5: alt = down; return systemKeys;
            case 0xA0: case 0xA1: shift = down; return systemKeys;
            case 0x5B: case 0x5C:
                if (!systemKeys) { winDown = down; return false; }
                if (down)
                {
                    if (!winDown) { winDown = true; winUsed = false; }
                }
                else
                {
                    if (winDown && !winUsed) conn.Key(0x5B, 0); // Win pressed alone = Start menu
                    winDown = false;
                }
                return true;
        }

        // local-only shortcut, never forwarded
        if (ctrl && alt && vk == 0x0D)
        {
            if (down) BeginInvoke(new Action(ToggleFullscreen));
            return true;
        }

        // with system keys off, Windows keeps its own shortcuts
        if (!systemKeys && (winDown || (alt && (vk == 0x09 || vk == 0x1B)) || (ctrl && vk == 0x1B)))
            return false;

        if (down)
        {
            int mods = (ctrl ? CTRL : 0) | (alt ? ALT : 0) | (shift ? SHIFT : 0) | (winDown ? WIN : 0);
            if (winDown) winUsed = true;
            conn.Key(vk, mods);
        }
        return true;
    }

    // ------------------------------------------------------------------ fullscreen

    void SetReveal(float target)
    {
        revealTarget = target;
        revealTimer.Start();
    }

    void ToggleFullscreen()
    {
        fullscreen = !fullscreen;
        if (fullscreen)
        {
            windowedState = WindowState;
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            windowedBounds = Bounds;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = Screen.FromControl(this).Bounds;
            reveal = 1f;
            SetReveal(0f); // show the bar briefly, then slide it away
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = windowedBounds;
            WindowState = windowedState;
            reveal = revealTarget = 1f;
        }
        LayoutAll();
        canvas.Focus();
    }

    // ------------------------------------------------------------------ teardown

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closing = true;
        hook.Dispose();
        moveTimer.Stop();
        statusTimer.Stop();
        revealTimer.Stop();
        conn.FrameReceived -= OnFrame;
        conn.Close();
        Interlocked.Exchange(ref pendingFrame, null)?.Dispose();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            moveTimer.Dispose();
            statusTimer.Dispose();
            revealTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
