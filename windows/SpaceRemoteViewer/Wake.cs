using System.Net;
using System.Net.Sockets;

namespace SpaceRemoteViewer;

/// <summary>Wake-on-LAN magic packet and optional smart-plug URL, same logic as the Android app.</summary>
static class Wake
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Only addresses on the same home network can be reached by a broadcast.</summary>
    public static bool IsLocalNetwork(string host)
    {
        if (!IPAddress.TryParse(host.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return (b[0] == 192 && b[1] == 168) || b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31);
    }

    public static byte[] ParseMac(string mac)
    {
        var hex = new string((mac ?? "").Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12) return null;
        return Enumerable.Range(0, 6).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
    }

    public static void SendMagicPacket(string mac, string host)
    {
        var macBytes = ParseMac(mac) ?? throw new ArgumentException("Invalid MAC address");
        var packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int i = 1; i <= 16; i++) Buffer.BlockCopy(macBytes, 0, packet, i * 6, 6);

        var targets = new List<IPAddress> { IPAddress.Broadcast };
        if (IPAddress.TryParse(host.Trim(), out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            targets.Add(new IPAddress(new byte[] { b[0], b[1], b[2], 255 }));
        }

        using var udp = new UdpClient { EnableBroadcast = true };
        for (int round = 0; round < 3; round++)
        {
            foreach (var t in targets)
                foreach (var port in new[] { 9, 7 })
                    try { udp.Send(packet, packet.Length, new IPEndPoint(t, port)); } catch { }
            Thread.Sleep(100);
        }
    }

    public static void CallUrl(string url)
    {
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            throw new ArgumentException("Wake URL must start with http:// or https://");
        using var resp = http.GetAsync(url).GetAwaiter().GetResult();
        if ((int)resp.StatusCode >= 400) throw new InvalidOperationException($"Smart plug answered with HTTP {(int)resp.StatusCode}");
    }
}
