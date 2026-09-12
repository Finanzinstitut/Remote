using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace SpaceRemote;

sealed class TrayContext : ApplicationContext
{
    readonly Config cfg;
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem statusItem;
    readonly Control invoker;

    public TrayContext(Config cfg, RemoteServer server)
    {
        this.cfg = cfg;

        invoker = new Control();
        invoker.CreateControl();

        var menu = new ContextMenuStrip();
        statusItem = new ToolStripMenuItem(server.Status) { Enabled = false };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Show connection details", null, (_, _) => ShowInfo());

        var autostart = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = Autostart.IsEnabled() };
        autostart.CheckedChanged += (_, _) => Autostart.Set(autostart.Checked);
        menu.Items.Add(autostart);

        var keepAwake = new ToolStripMenuItem("Keep PC awake") { CheckOnClick = true, Checked = cfg.KeepAwake };
        keepAwake.CheckedChanged += (_, _) =>
        {
            cfg.KeepAwake = keepAwake.Checked;
            cfg.Save();
            StayAwake.Set(keepAwake.Checked);
        };
        menu.Items.Add(keepAwake);
        StayAwake.Set(cfg.KeepAwake);

        menu.Items.Add("Allow through firewall (admin)", null, (_, _) => AddFirewallRule());
        menu.Items.Add("Open settings folder", null, (_, _) => OpenConfigDir());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => { tray.Visible = false; ExitThread(); });

        tray = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Space Remote",
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => ShowInfo();

        server.StatusChanged += s => invoker.BeginInvoke(new Action(() =>
        {
            statusItem.Text = s;
            string t = "Space Remote - " + s;
            tray.Text = t.Length > 63 ? t[..63] : t;
        }));

        // keep the autostart path current in case the .exe was moved
        if (Autostart.IsEnabled()) Autostart.Set(true);

        if (!cfg.FirstRunDone)
        {
            autostart.Checked = true; // triggers Autostart.Set(true)
            cfg.FirstRunDone = true;
            cfg.Save();
            invoker.BeginInvoke(new Action(ShowInfo));
        }
    }

    void ShowInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Enter these values in the Android app:");
        sb.AppendLine();
        var ips = GetIPv4().ToList();
        sb.AppendLine("Home network address:  " + (ips.Count > 0 ? string.Join("  /  ", ips) : "no network connection"));
        string ts = GetTailscaleIp();
        sb.AppendLine("Tailscale address:  " + (ts ?? "not found (Tailscale not installed or not signed in)"));
        sb.AppendLine("Port:  " + cfg.Port);
        sb.AppendLine("Password:  " + cfg.Password);
        sb.AppendLine();
        sb.AppendLine("Away from home use the Tailscale address,");
        sb.AppendLine("on your own Wi-Fi the home network address.");
        sb.AppendLine();
        sb.AppendLine("MAC address for Wake-on-LAN:");
        foreach (var (kind, name, mac) in GetMacs())
            sb.AppendLine($"   {kind}:  {mac}   ({name})");
        sb.AppendLine();
        sb.AppendLine("Use the MAC of the wired adapter - Wake-on-LAN");
        sb.AppendLine("hardly ever works over Wi-Fi.");
        sb.AppendLine();
        sb.AppendLine("Tip: Ctrl+C copies the text of this window.");
        MessageBox.Show(sb.ToString(), "Space Remote - connection details", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Tailscale hands out addresses from 100.64.0.0/10 (the CGNAT range).</summary>
    static string GetTailscaleIp() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.GetAddressBytes())
            .Where(b => b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            .Select(b => $"{b[0]}.{b[1]}.{b[2]}.{b[3]}")
            .FirstOrDefault();

    static IEnumerable<string> GetIPv4() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString())
            .Distinct();

    static IEnumerable<(string kind, string name, string mac)> GetMacs()
    {
        string[] virtualHints = { "virtual", "hyper-v", "vmware", "virtualbox", "bluetooth", "tap-", "wan miniport", "vpn", "loopback", "tailscale" };
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            bool wired = n.NetworkInterfaceType == NetworkInterfaceType.Ethernet;
            bool wifi = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            if (!wired && !wifi) continue;
            string desc = (n.Description + " " + n.Name).ToLowerInvariant();
            if (virtualHints.Any(h => desc.Contains(h))) continue;
            byte[] bytes = n.GetPhysicalAddress().GetAddressBytes();
            if (bytes.Length != 6) continue;
            yield return (wired ? "Wired" : "Wi-Fi", n.Description, string.Join(":", bytes.Select(b => b.ToString("X2"))));
        }
    }

    void AddFirewallRule()
    {
        try
        {
            Process.Start(new ProcessStartInfo("netsh",
                $"advfirewall firewall add rule name=\"Space Remote\" dir=in action=allow protocol=TCP localport={cfg.Port} profile=private,domain")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Firewall rule was not created: " + ex.Message, "Space Remote");
        }
    }

    static void OpenConfigDir()
    {
        try { Process.Start("explorer.exe", Config.Dir); } catch { }
    }

    static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(Color.FromArgb(124, 156, 255));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var pen = new Pen(Color.White, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, 8, 9, 16, 16, -60, 300);
            g.DrawLine(pen, 16, 6, 16, 15);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
