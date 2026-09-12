using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace SpaceRemote;

/*
 * Protocol (TCP, big endian):
 *  Server -> client:  "SPRM" | version (1 byte) | nonce (16 bytes)
 *  Client -> server:  HMAC-SHA256(password, nonce) (32 bytes)
 *  Server -> client:  1 = ok, 0 = wrong password
 *  then server -> client: [int32 length][JPEG]   (length 0 = keepalive)
 *  then client -> server: [type][data]
 *     1 Move    float x, float y (0..1)
 *     2 Button  byte button (0 L, 1 R, 2 M), byte down
 *     3 Scroll  int32 delta (120 = one notch)
 *     4 Text    uint16 length, UTF-8
 *     5 Key     byte mods (1 Ctrl, 2 Alt, 4 Shift, 8 Win), uint16 VK
 *     6 Power   byte (0 shut down, 1 restart, 2 sleep, 3 lock)
 */
sealed class RemoteServer
{
    readonly Config cfg;
    readonly object gate = new();
    TcpListener listener;
    volatile bool running;
    Session current;

    public event Action<string> StatusChanged;
    public string Status { get; private set; } = "Waiting for a connection";

    public RemoteServer(Config cfg) => this.cfg = cfg;

    public void Start()
    {
        listener = new TcpListener(IPAddress.Any, cfg.Port);
        listener.Start();
        running = true;
        new Thread(AcceptLoop) { IsBackground = true, Name = "accept" }.Start();
    }

    public void Stop()
    {
        running = false;
        try { listener?.Stop(); } catch { }
        lock (gate) current?.Close();
    }

    void SetStatus(string s)
    {
        Status = s;
        StatusChanged?.Invoke(s);
    }

    void AcceptLoop()
    {
        while (running)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch
            {
                if (!running) return;
                Thread.Sleep(100);
                continue;
            }
            new Thread(() => Handle(client)) { IsBackground = true, Name = "client" }.Start();
        }
    }

    void Handle(TcpClient client)
    {
        string remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        Session session = null;
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            stream.ReadTimeout = 10000;

            byte[] nonce = RandomNumberGenerator.GetBytes(16);
            var hello = new byte[21];
            Encoding.ASCII.GetBytes("SPRM").CopyTo(hello, 0);
            hello[4] = 1;
            nonce.CopyTo(hello, 5);
            stream.Write(hello);

            var answer = new byte[32];
            stream.ReadExactly(answer);
            byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(cfg.Password), nonce);
            if (!CryptographicOperations.FixedTimeEquals(answer, expected))
            {
                Thread.Sleep(1500); // slows down password guessing
                stream.WriteByte(0);
                SetStatus("Wrong password from " + remote);
                return;
            }

            stream.WriteByte(1);
            stream.ReadTimeout = Timeout.Infinite;

            session = new Session(client, stream, cfg);
            lock (gate)
            {
                current?.Close(); // only one phone at a time
                current = session;
            }
            SetStatus("Connected to " + remote);
            session.Run();
        }
        catch { }
        finally
        {
            try { client.Close(); } catch { }
            bool wasCurrent = false;
            lock (gate)
            {
                if (session != null && current == session)
                {
                    current = null;
                    wasCurrent = true;
                }
            }
            if (wasCurrent) SetStatus("Waiting for a connection");
        }
    }
}

sealed class Session
{
    readonly TcpClient client;
    readonly NetworkStream stream;
    readonly Config cfg;
    volatile bool closed;

    public Session(TcpClient client, NetworkStream stream, Config cfg)
    {
        this.client = client;
        this.stream = stream;
        this.cfg = cfg;
    }

    public void Close()
    {
        closed = true;
        try { client.Close(); } catch { }
    }

    public void Run()
    {
        var sender = new Thread(SendLoop) { IsBackground = true, Name = "capture" };
        sender.Start();
        try { ReadLoop(); } catch { }
        Close();
        sender.Join(3000);
    }

    void SendLoop()
    {
        using var capture = new ScreenCapture();
        var header = new byte[4];
        long lastSent = 0;

        while (!closed)
        {
            long t0 = Environment.TickCount64;
            byte[] jpeg = null;
            try { jpeg = capture.CaptureJpeg(cfg.MaxWidth, cfg.JpegQuality); }
            catch { /* lock screen or UAC prompt -> no capture possible */ }

            try
            {
                if (jpeg != null)
                {
                    BinaryPrimitives.WriteInt32BigEndian(header, jpeg.Length);
                    stream.Write(header);
                    stream.Write(jpeg);
                    lastSent = Environment.TickCount64;
                }
                else if (Environment.TickCount64 - lastSent > 2000)
                {
                    BinaryPrimitives.WriteInt32BigEndian(header, 0);
                    stream.Write(header);
                    lastSent = Environment.TickCount64;
                }
            }
            catch
            {
                Close();
                return;
            }

            int frameMs = 1000 / cfg.Fps;
            int wait = jpeg == null ? 500 : frameMs - (int)(Environment.TickCount64 - t0);
            if (wait > 0) Thread.Sleep(wait);
        }
    }

    void ReadLoop()
    {
        var buf = new byte[8];
        while (!closed)
        {
            int type = stream.ReadByte();
            if (type < 0) return;

            switch (type)
            {
                case 1:
                    stream.ReadExactly(buf, 0, 8);
                    InputSim.MoveTo(BinaryPrimitives.ReadSingleBigEndian(buf.AsSpan(0, 4)),
                                    BinaryPrimitives.ReadSingleBigEndian(buf.AsSpan(4, 4)));
                    break;
                case 2:
                    stream.ReadExactly(buf, 0, 2);
                    InputSim.Button(buf[0], buf[1] != 0);
                    break;
                case 3:
                    stream.ReadExactly(buf, 0, 4);
                    InputSim.Scroll(BinaryPrimitives.ReadInt32BigEndian(buf.AsSpan(0, 4)));
                    break;
                case 4:
                {
                    stream.ReadExactly(buf, 0, 2);
                    int len = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(0, 2));
                    var data = new byte[len];
                    stream.ReadExactly(data, 0, len);
                    InputSim.Text(Encoding.UTF8.GetString(data));
                    break;
                }
                case 5:
                    stream.ReadExactly(buf, 0, 3);
                    InputSim.KeyPress(buf[0], BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(1, 2)));
                    break;
                case 6:
                    stream.ReadExactly(buf, 0, 1);
                    Power.Do(buf[0]);
                    break;
                default:
                    return; // unknown packet -> drop the connection
            }
        }
    }
}
