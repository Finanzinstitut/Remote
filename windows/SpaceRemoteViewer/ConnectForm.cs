using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Security.Authentication;

namespace SpaceRemoteViewer;

sealed class ConnectForm : Form
{
    readonly Settings settings = Settings.Load();
    readonly TextBox host, port, password, mac, wakeUrl;
    readonly CheckBox remember;
    readonly Label status;
    readonly PowerButton power;
    CancellationTokenSource cts;

    public ConnectForm()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Space Remote Viewer";
        ClientSize = new Size(420, 680);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Ink;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        DoubleBuffered = true;

        Controls.Add(new Label
        {
            Text = "Space Remote",
            Font = new Font("Segoe UI", 20f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(30, 24),
            BackColor = Color.Transparent,
        });
        Controls.Add(new Label
        {
            Text = "Control your PC from another PC",
            ForeColor = Theme.Muted,
            AutoSize = true,
            Location = new Point(33, 66),
            BackColor = Color.Transparent,
        });

        power = new PowerButton { Location = new Point(100, 96), Size = new Size(220, 220) };
        power.Click += (_, _) => { if (power.Busy) Cancel(); else StartConnect(); };
        Controls.Add(power);

        status = new Label
        {
            Location = new Point(30, 318),
            Size = new Size(360, 44),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
        };
        Controls.Add(status);

        host = Field("PC address  (home network IP or Tailscale 100.x.x.x)", 30, 368, 360);
        port = Field("Port", 30, 424, 100);
        password = Field("Password", 142, 424, 248);
        password.UseSystemPasswordChar = true;
        mac = Field("MAC address for Wake-on-LAN  (optional)", 30, 480, 360);
        wakeUrl = Field("Wake URL for a smart plug  (optional)", 30, 536, 360);

        remember = new CheckBox
        {
            Text = "Remember these details",
            Location = new Point(30, 588),
            AutoSize = true,
            BackColor = Color.Transparent,
            ForeColor = Theme.Muted,
        };
        Controls.Add(remember);

        Controls.Add(new Label
        {
            Text = "Ctrl+Alt+Enter toggles fullscreen while connected.",
            Location = new Point(30, 624),
            AutoSize = true,
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI", 8.5f),
            BackColor = Color.Transparent,
        });

        host.Text = settings.Host;
        port.Text = settings.Port.ToString();
        password.Text = settings.GetPassword();
        mac.Text = settings.Mac;
        wakeUrl.Text = settings.WakeUrl;
        remember.Checked = settings.Remember;

        AcceptButton = null;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !power.Busy) { e.SuppressKeyPress = true; StartConnect(); }
            if (e.KeyCode == Keys.Escape && power.Busy) { e.SuppressKeyPress = true; Cancel(); }
        };

        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new LinearGradientBrush(ClientRectangle, Theme.Deep, Theme.Ink, LinearGradientMode.Vertical))
            g.FillRectangle(bg, ClientRectangle);

        // soft glow behind the button
        using var path = new GraphicsPath();
        var glow = new RectangleF(ClientSize.Width * 0.5f - 230, 206 - 230, 460, 460);
        path.AddEllipse(glow);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(70, Theme.Accent),
            SurroundColors = new[] { Color.FromArgb(0, Theme.Accent) },
        };
        g.FillPath(brush, path);
    }

    TextBox Field(string label, int x, int y, int width)
    {
        Controls.Add(new Label
        {
            Text = label,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Theme.Muted,
            Font = new Font("Segoe UI", 8.5f),
            BackColor = Color.Transparent,
        });
        var box = new TextBox
        {
            Location = new Point(x, y + 20),
            Width = width,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Surface,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f),
        };
        Controls.Add(box);
        return box;
    }

    void SetStatus(string text, bool error = false)
    {
        status.ForeColor = error ? Theme.Danger : Theme.Muted;
        status.Text = text;
    }

    void SetFieldsEnabled(bool on)
    {
        foreach (var c in new Control[] { host, port, password, mac, wakeUrl, remember }) c.Enabled = on;
    }

    void StartConnect()
    {
        string h = host.Text.Trim(), pw = password.Text, m = mac.Text.Trim(), url = wakeUrl.Text.Trim();
        if (h.Length == 0) { SetStatus("Enter the address of your PC.", true); return; }
        if (!int.TryParse(port.Text.Trim(), out int p) || p < 1 || p > 65535) { SetStatus("The port has to be between 1 and 65535.", true); return; }
        if (pw.Length == 0) { SetStatus("Enter the password shown by Space Remote on the host PC.", true); return; }
        if (m.Length > 0 && Wake.ParseMac(m) == null) { SetStatus("A MAC address needs 12 hex characters.", true); return; }
        if (url.Length > 0 && !url.StartsWith("http://") && !url.StartsWith("https://")) { SetStatus("The wake URL has to start with http:// or https://.", true); return; }

        settings.Remember = remember.Checked;
        if (remember.Checked)
        {
            settings.Host = h;
            settings.Port = p;
            settings.SetPassword(pw);
            settings.Mac = m;
            settings.WakeUrl = url;
        }
        else
        {
            settings.Host = "";
            settings.SetPassword("");
            settings.Mac = "";
            settings.WakeUrl = "";
        }
        settings.Save();

        power.Busy = true;
        SetFieldsEnabled(false);
        SetStatus($"Connecting to {h}…");
        cts = new CancellationTokenSource();
        var token = cts.Token;

        Task.Run(() =>
        {
            try
            {
                var conn = ConnectLoop(h, p, pw, m, url, token, s => BeginInvoke(new Action(() => SetStatus(s))));
                if (token.IsCancellationRequested) { conn.Close(); return; }
                BeginInvoke(new Action(() => OpenViewer(conn, h)));
            }
            catch (OperationCanceledException) { }
            catch (AuthenticationException) when (!token.IsCancellationRequested)
            {
                BeginInvoke(new Action(() => Fail("Wrong password. Check “Show connection details” on the host PC.")));
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                BeginInvoke(new Action(() => Fail(ex.Message)));
            }
            catch { /* cancelled by the user meanwhile */ }
        });
    }

    /// <summary>Connect; if the PC is off, wake it and keep retrying until Space Remote answers.</summary>
    static RemoteConnection ConnectLoop(string host, int port, string password, string mac, string url,
                                        CancellationToken ct, Action<string> report)
    {
        bool local = Wake.IsLocalNetwork(host);
        bool canWol = mac.Length > 0 && local;   // a broadcast never leaves the home network
        bool canUrl = url.Length > 0;
        bool canWake = canWol || canUrl;
        long limit = canWake ? 300_000 : local ? 20_000 : 45_000;
        var clock = Stopwatch.StartNew();
        long lastWake = -1;
        bool urlCalled = false, first = true;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (clock.ElapsedMilliseconds > limit)
            {
                throw new Exception(
                    canWake ? "The PC never answered. Check that it powers on and Space Remote starts automatically."
                    : local ? "PC unreachable. Add its MAC address so the viewer can wake it."
                    : "PC unreachable. Is Tailscale running on both PCs, and is the host PC switched on?");
            }

            try
            {
                int timeout = local ? (first ? 1500 : 2500) : (first ? 4000 : 6000);
                return RemoteConnection.Connect(host, port, password, timeout);
            }
            catch (AuthenticationException) { throw; }
            catch when (!ct.IsCancellationRequested) { }

            ct.ThrowIfCancellationRequested();
            first = false;

            if (canUrl && !urlCalled)
            {
                report("Powering on the PC…");
                urlCalled = true;
                try { Wake.CallUrl(url); }
                catch (Exception ex) { throw new Exception("Wake URL failed: " + ex.Message); }
                lastWake = clock.ElapsedMilliseconds;
            }

            if (canWol && (lastWake < 0 || clock.ElapsedMilliseconds - lastWake > 20_000))
            {
                report("PC is off — sending Wake-on-LAN…");
                try { Wake.SendMagicPacket(mac, host); }
                catch (Exception ex) { throw new Exception("Wake-on-LAN failed: " + ex.Message); }
                lastWake = clock.ElapsedMilliseconds;
            }

            long secs = clock.ElapsedMilliseconds / 1000;
            report(lastWake >= 0 ? $"PC is booting… waiting for Windows and Space Remote ({secs}s)" : $"Connecting… ({secs}s)");
            ct.WaitHandle.WaitOne(2000);
        }
    }

    void Cancel()
    {
        cts?.Cancel();
        power.Busy = false;
        SetFieldsEnabled(true);
        SetStatus("");
    }

    void Fail(string message)
    {
        power.Busy = false;
        SetFieldsEnabled(true);
        SetStatus(message, true);
    }

    void OpenViewer(RemoteConnection conn, string hostName)
    {
        var viewer = new ViewerForm(conn, hostName, settings);
        viewer.FormClosed += (_, _) =>
        {
            Show();
            Activate();
            power.Busy = false;
            SetFieldsEnabled(true);
            SetStatus(viewer.EndMessage ?? "", viewer.EndMessage != null && viewer.EndIsError);
        };
        Hide();
        viewer.Show();
    }
}
