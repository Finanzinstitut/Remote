using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

const int Port = 8765;
const string Prefix = "http://0.0.0.0:8765/";
const string TokenFile = "pcremote.token";

string token;
if (File.Exists(TokenFile))
{
    token = File.ReadAllText(TokenFile).Trim();
}
else
{
    token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    File.WriteAllText(TokenFile, token);
}

using var listener = new HttpListener();
listener.Prefixes.Add(Prefix);
listener.Start();

Console.WriteLine("PCRemote Windows Host");
Console.WriteLine($"Listening on port {Port}");
Console.WriteLine($"Token: {token}");
Console.WriteLine("Keep this token private.");

while (true)
{
    var ctx = await listener.GetContextAsync();
    _ = Task.Run(() => Handle(ctx));
}

void Handle(HttpListenerContext ctx)
{
    try
    {
        if (!Authenticate(ctx))
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.Close();
            return;
        }

        ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";

        switch (ctx.Request.Url?.AbsolutePath)
        {
            case "/status":
                WriteJson(ctx, new { ok = true, computer = Environment.MachineName });
                break;

            case "/screen":
                WriteScreen(ctx);
                break;

            case "/mouse":
                HandleMouse(ctx);
                break;

            case "/key":
                HandleKey(ctx);
                break;

            default:
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                break;
        }
    }
    catch (Exception ex)
    {
        try
        {
            ctx.Response.StatusCode = 500;
            WriteJson(ctx, new { ok = false, error = ex.Message });
        }
        catch { }
    }
}

bool Authenticate(HttpListenerContext ctx)
{
    var supplied = ctx.Request.Headers["X-PCRemote-Token"];
    return !string.IsNullOrWhiteSpace(supplied) &&
           CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(supplied),
               Encoding.UTF8.GetBytes(token));
}

void WriteJson(HttpListenerContext ctx, object value)
{
    var data = JsonSerializer.SerializeToUtf8Bytes(value);
    ctx.Response.ContentType = "application/json";
    ctx.Response.ContentLength64 = data.Length;
    ctx.Response.OutputStream.Write(data);
    ctx.Response.Close();
}

void WriteScreen(HttpListenerContext ctx)
{
    using var bmp = CaptureScreen();
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Jpeg);

    ctx.Response.ContentType = "image/jpeg";
    ctx.Response.ContentLength64 = ms.Length;
    ms.Position = 0;
    ms.CopyTo(ctx.Response.OutputStream);
    ctx.Response.Close();
}

Bitmap CaptureScreen()
{
    var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
    var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
    using var g = Graphics.FromImage(bmp);
    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
    return bmp;
}

void HandleMouse(HttpListenerContext ctx)
{
    using var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
    var body = sr.ReadToEnd();
    var cmd = JsonSerializer.Deserialize<MouseCommand>(body);

    if (cmd is null)
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.Close();
        return;
    }

    SetCursorPos(cmd.X, cmd.Y);

    if (cmd.Action == "down") MouseClick(MOUSEEVENTF_LEFTDOWN);
    if (cmd.Action == "up") MouseClick(MOUSEEVENTF_LEFTUP);

    WriteJson(ctx, new { ok = true });
}

void HandleKey(HttpListenerContext ctx)
{
    using var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
    var body = sr.ReadToEnd();
    var cmd = JsonSerializer.Deserialize<KeyCommand>(body);

    if (cmd?.Text is null)
    {
        ctx.Response.StatusCode = 400;
        ctx.Response.Close();
        return;
    }

    // Simple Unicode text input using the Windows SendInput API.
    foreach (var ch in cmd.Text)
        SendUnicodeChar(ch);

    WriteJson(ctx, new { ok = true });
}

record MouseCommand(int X, int Y, string Action);
record KeyCommand(string Text);

[DllImport("user32.dll")]
static extern bool SetCursorPos(int X, int Y);

[DllImport("user32.dll")]
static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
const uint MOUSEEVENTF_LEFTUP = 0x0004;

static void MouseClick(uint flag) => mouse_event(flag, 0, 0, 0, UIntPtr.Zero);

[DllImport("user32.dll", SetLastError = true)]
static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

[StructLayout(LayoutKind.Sequential)]
struct INPUT
{
    public uint type;
    public InputUnion U;
}

[StructLayout(LayoutKind.Explicit)]
struct InputUnion
{
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public UIntPtr dwExtraInfo;
}

const uint INPUT_KEYBOARD = 1;
const uint KEYEVENTF_UNICODE = 0x0004;

static void SendUnicodeChar(char c)
{
    var input = new INPUT
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = KEYEVENTF_UNICODE
            }
        }
    };

    var up = input;
    up.U.ki.dwFlags = KEYEVENTF_UNICODE | 0x0002;

    SendInput(2, new[] { input, up }, Marshal.SizeOf<INPUT>());
}
