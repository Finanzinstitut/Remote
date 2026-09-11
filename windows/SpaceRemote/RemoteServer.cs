using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace SpaceRemote;

/*
 * Protokoll (TCP, Big Endian):
 *  Server -> Client:  "SPRM" | Version (1 Byte) | Nonce (16 Byte)
 *  Client -> Server:  HMAC-SHA256(Passwort, Nonce) (32 Byte)
 *  Server -> Client:  1 = OK, 0 = falsches Passwort
 *  danach Server -> Client: [int32 Länge][JPEG]   (Länge 0 = Keepalive)
 *  danach Client -> Server: [Typ][Daten]
 *     1 Move    float x, float y (0..1)
 *     2 Button  byte button (0 L, 1 R, 2 M), byte down
 *     3 Scroll  int32 delta (120 = eine Raste)
 *     4 Text    uint16 Länge, UTF-8
 *     5 Key     byte mods (1 Strg, 2 Alt, 4 Shift, 8 Win), uint16 VK
 *     6 Power   byte (0 Aus, 1 Neustart, 2 Energiesparen, 3 Sperren)
 */
sealed class RemoteServer
{
    readonly Config cfg;
    readonly object gate = new();
    TcpListener listener;
    volatile bool running;
    Session current;

    public event Action<string> StatusChanged;
    public string Status { get; private set; } = "Wartet auf Verbindung";

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
                Thread.Sleep(1500); // bremst Passwort-Raten
                stream.WriteByte(0);
                SetStatus("Falsches Passwort von " + remote);
                return;
            }

            stream.WriteByte(1);
            stream.ReadTimeout = Timeout.Infinite;

            session = new Session(client, stream, cfg);
            lock (gate)
            {
                current?.Close(); // immer nur ein Handy gleichzeitig
                current = session;
            }
            SetStatus("Verbunden mit " + remote);
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
            if (wasCurrent) SetStatus("Wartet auf Verbindung");
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
            catch { /* z. B. Sperrbildschirm / UAC -> kein Bild möglich */ }

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
                    return; // unbekanntes Paket -> Verbindung beenden
            }
        }
    }
}
