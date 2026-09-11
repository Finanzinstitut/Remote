using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

const int Port = 8765;
const string Prefix = "http://0.0.0.0:8765/";
const string TokenFile = "pcremote.token";
const int DiscoveryPort = 8766;

string token = File.Exists(TokenFile)
    ? File.ReadAllText(TokenFile).Trim()
    : Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

File.WriteAllText(TokenFile, token);

using var listener = new HttpListener();
listener.Prefixes.Add(Prefix);
listener.Start();

Console.WriteLine("PCRemote V2");
Console.WriteLine($"TCP port: {Port}");
Console.WriteLine($"Token: {token}");
foreach (var ip in GetLocalIPv4())
    Console.WriteLine($"LAN address: http://{ip}:{Port}");

_ = Task.Run(DiscoveryResponder);

while (true)
{
    var ctx = await listener.GetContextAsync();
    _ = Task.Run(() => Handle(ctx));
}

bool Authenticate(HttpListenerContext c) =>
    CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(c.Request.Headers["X-PCRemote-Token"] ?? ""),
        Encoding.UTF8.GetBytes(token));

async Task Handle(HttpListenerContext c)
{
    try
    {
        if (c.Request.Url?.AbsolutePath == "/discover")
        {
            WriteJson(c, new { name = Environment.MachineName, port = Port });
            return;
        }

        if (!Authenticate(c))
        {
            c.Response.StatusCode = 401;
            c.Response.Close();
            return;
        }

        switch (c.Request.Url?.AbsolutePath)
        {
            case "/status":
                WriteJson(c, new { ok = true, name = Environment.MachineName });
                break;
            case "/stream":
                await StreamScreen(c);
                break;
            case "/mouse":
                HandleMouse(c);
                break;
            case "/scroll":
                HandleScroll(c);
                break;
            case "/key":
                HandleText(c);
                break;
            case "/clipboard":
                HandleClipboard(c);
                break;
            default:
                c.Response.StatusCode = 404;
                c.Response.Close();
                break;
        }
    }
    catch { try { c.Response.Close(); } catch { } }
}

async Task StreamScreen(HttpListenerContext c)
{
    c.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
    c.Response.SendChunked = true;

    while (c.Response.OutputStream.CanWrite)
    {
        using var bmp = CapturePrimary();
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageCodecJpeg(), new EncoderParameters(1) {
            Param = { [0] = new EncoderParameter(Encoder.Quality, 65L) }
        });

        var jpeg = ms.ToArray();
        var header = Encoding.ASCII.GetBytes(
            $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n");

        await c.Response.OutputStream.WriteAsync(header);
        await c.Response.OutputStream.WriteAsync(jpeg);
        await c.Response.OutputStream.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
        await c.Response.OutputStream.FlushAsync();
        await Task.Delay(80); // target ~12.5 FPS; adaptive clients can reduce this later
    }
}

ImageCodecInfo ImageCodecJpeg() =>
    ImageCodecInfo.GetImageEncoders().First(x => x.MimeType == "image/jpeg");

Bitmap CapturePrimary()
{
    var bounds = Screen.PrimaryScreen!.Bounds;
    var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
    using var g = Graphics.FromImage(bmp);
    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
    return bmp;
}

void HandleMouse(HttpListenerContext c)
{
    var cmd = ReadJson<MouseCommand>(c);
    if (cmd is null) { c.Response.StatusCode = 400; c.Response.Close(); return; }

    SetCursorPos(cmd.X, cmd.Y);
    switch (cmd.Action)
    {
        case "leftDown": MouseEvent(MOUSEEVENTF_LEFTDOWN); break;
        case "leftUp": MouseEvent(MOUSEEVENTF_LEFTUP); break;
        case "rightDown": MouseEvent(MOUSEEVENTF_RIGHTDOWN); break;
        case "rightUp": MouseEvent(MOUSEEVENTF_RIGHTUP); break;
    }
    WriteJson(c, new { ok = true });
}

void HandleScroll(HttpListenerContext c)
{
    var cmd = ReadJson<ScrollCommand>(c);
    if (cmd is null) { c.Response.StatusCode = 400; c.Response.Close(); return; }
    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)cmd.Delta, UIntPtr.Zero);
    WriteJson(c, new { ok = true });
}

void HandleText(HttpListenerContext c)
{
    var cmd = ReadJson<TextCommand>(c);
    if (cmd is null) { c.Response.StatusCode = 400; c.Response.Close(); return; }
    foreach (var ch in cmd.Text)
        SendUnicodeChar(ch);
    WriteJson(c, new { ok = true });
}

void HandleClipboard(HttpListenerContext c)
{
    var cmd = ReadJson<TextCommand>(c);
    if (cmd is null) { c.Response.StatusCode = 400; c.Response.Close(); return; }

    Thread t = new(() => Clipboard.SetText(cmd.Text));
    t.SetApartmentState(ApartmentState.STA);
    t.Start();
    t.Join();
    WriteJson(c, new { ok = true });
}

T? ReadJson<T>(HttpListenerContext c)
{
    using var reader = new StreamReader(c.Request.InputStream, Encoding.UTF8);
    return JsonSerializer.Deserialize<T>(reader.ReadToEnd());
}

void WriteJson(HttpListenerContext c, object o)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(o);
    c.Response.ContentType = "application/json";
    c.Response.ContentLength64 = bytes.Length;
    c.Response.OutputStream.Write(bytes);
    c.Response.Close();
}

async Task DiscoveryResponder()
{
    using var udp = new UdpClient(DiscoveryPort) { EnableBroadcast = true };
    while (true)
    {
        var r = await udp.ReceiveAsync();
        var text = Encoding.UTF8.GetString(r.Buffer);
        if (text == "PCREMOTE_DISCOVER")
        {
            var response = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { ip = GetLocalIPv4().FirstOrDefault(), port = Port, name = Environment.MachineName }));
            await udp.SendAsync(response, response.Length, r.RemoteEndPoint);
        }
    }
}

IEnumerable<string> GetLocalIPv4() =>
    Dns.GetHostEntry(Dns.GetHostName()).AddressList
       .Where(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))
       .Select(x => x.ToString());

record MouseCommand(int X, int Y, string Action);
record ScrollCommand(int Delta);
record TextCommand(string Text);

[DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);
[DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

const uint MOUSEEVENTF_LEFTDOWN=0x0002, MOUSEEVENTF_LEFTUP=0x0004;
const uint MOUSEEVENTF_RIGHTDOWN=0x0008, MOUSEEVENTF_RIGHTUP=0x0010, MOUSEEVENTF_WHEEL=0x0800;

[DllImport("user32.dll")]
static extern uint SendInput(uint nInputs, INPUT[] inputs, int size);

[StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion U; }
[StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }
[StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT
{
    public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public UIntPtr extra;
}
const uint INPUT_KEYBOARD=1, KEYEVENTF_UNICODE=0x0004, KEYEVENTF_KEYUP=0x0002;

static void SendUnicodeChar(char c)
{
    var a = new INPUT { type=INPUT_KEYBOARD, U=new InputUnion { ki=new KEYBDINPUT { wScan=c, dwFlags=KEYEVENTF_UNICODE }}};
    var b = a; b.U.ki.dwFlags=KEYEVENTF_UNICODE|KEYEVENTF_KEYUP;
    SendInput(2, new[]{a,b}, Marshal.SizeOf<INPUT>());
}
static void MouseEvent(uint f) => mouse_event(f,0,0,0,UIntPtr.Zero);
