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
        menu.Items.Add("Verbindungsdaten anzeigen", null, (_, _) => ShowInfo());

        var autostart = new ToolStripMenuItem("Mit Windows starten") { CheckOnClick = true, Checked = Autostart.IsEnabled() };
        autostart.CheckedChanged += (_, _) => Autostart.Set(autostart.Checked);
        menu.Items.Add(autostart);

        menu.Items.Add("Firewall freigeben (Admin)", null, (_, _) => AddFirewallRule());
        menu.Items.Add("Einstellungsordner öffnen", null, (_, _) => OpenConfigDir());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => { tray.Visible = false; ExitThread(); });

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
            string t = "Space Remote – " + s;
            tray.Text = t.Length > 63 ? t[..63] : t;
        }));

        // Pfad aktuell halten, falls die .exe verschoben wurde
        if (Autostart.IsEnabled()) Autostart.Set(true);

        if (!cfg.FirstRunDone)
        {
            autostart.Checked = true; // löst Autostart.Set(true) aus
            cfg.FirstRunDone = true;
            cfg.Save();
            invoker.BeginInvoke(new Action(ShowInfo));
        }
    }

    void ShowInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Trage diese Daten in der Android-App ein:");
        sb.AppendLine();
        var ips = GetIPv4().ToList();
        sb.AppendLine("IP-Adresse:  " + (ips.Count > 0 ? string.Join("  /  ", ips) : "keine Netzwerkverbindung"));
        sb.AppendLine("Port:  " + cfg.Port);
        sb.AppendLine("Passwort:  " + cfg.Password);
        sb.AppendLine();
        sb.AppendLine("MAC-Adresse für Wake-on-LAN:");
        foreach (var (kind, name, mac) in GetMacs())
            sb.AppendLine($"   {kind}:  {mac}   ({name})");
        sb.AppendLine();
        sb.AppendLine("Nimm die MAC-Adresse vom LAN-Adapter – Wake-on-LAN");
        sb.AppendLine("funktioniert praktisch nur per Netzwerkkabel.");
        sb.AppendLine();
        sb.AppendLine("Tipp: Strg+C kopiert den Text dieses Fensters.");
        MessageBox.Show(sb.ToString(), "Space Remote – Verbindungsdaten", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

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
        string[] virtualHints = { "virtual", "hyper-v", "vmware", "virtualbox", "bluetooth", "tap-", "wan miniport", "vpn", "loopback" };
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            bool wired = n.NetworkInterfaceType == NetworkInterfaceType.Ethernet;
            bool wifi = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            if (!wired && !wifi) continue;
            string desc = (n.Description + " " + n.Name).ToLowerInvariant();
            if (virtualHints.Any(h => desc.Contains(h))) continue;
            byte[] bytes = n.GetPhysicalAddress().GetAddressBytes();
            if (bytes.Length != 6) continue;
            yield return (wired ? "LAN-Kabel" : "WLAN", n.Description, string.Join(":", bytes.Select(b => b.ToString("X2"))));
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
            MessageBox.Show("Firewall-Regel wurde nicht erstellt: " + ex.Message, "Space Remote");
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
