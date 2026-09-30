using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;

namespace SpaceRemoteViewer;

/*
 * Talks to the Space Remote server on the host PC.
 *
 * Deliberately uses only packet types 1-6, which every server version understands,
 * so the host never needs an update for this viewer:
 *   1 Move    float x, float y (0..1)
 *   2 Button  byte button (0 L, 1 R, 2 M), byte down
 *   3 Scroll  int32 delta
 *   4 Text    uint16 length, UTF-8
 *   5 Key     byte mods (1 Ctrl, 2 Alt, 4 Shift, 8 Win), uint16 VK
 *   6 Power   byte (0 shut down, 1 restart, 2 sleep, 3 lock)
 */
sealed class RemoteConnection
{
    readonly TcpClient tcp;
    readonly NetworkStream stream;
    readonly BlockingCollection<byte[]> outbox = new(new ConcurrentQueue<byte[]>());
    volatile bool closedByUser;
    int disconnectRaised;

    /// <summary>Raised on the network thread with a freshly decoded frame. The receiver owns the bitmap.</summary>
    public event Action<Bitmap> FrameReceived;

    /// <summary>Raised once when the connection ends. Null message = closed on purpose.</summary>
    public event Action<string> Disconnected;

    public long LastFrameAt { get; private set; }

    RemoteConnection(TcpClient tcp, NetworkStream stream)
    {
        this.tcp = tcp;
        this.stream = stream;
    }

    /// <summary>Blocking handshake. Throws AuthenticationException on a wrong password.</summary>
    public static RemoteConnection Connect(string host, int port, string password, int timeoutMs)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                try { tcp.ConnectAsync(host, port, cts.Token).AsTask().GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { throw new TimeoutException("Connection timed out"); }
            }

            var s = tcp.GetStream();
            s.ReadTimeout = 6000;

            var hello = new byte[21];
            s.ReadExactly(hello);
            if (Encoding.ASCII.GetString(hello, 0, 4) != "SPRM") throw new IOException("Not a Space Remote server");
            var nonce = hello.AsSpan(5, 16).ToArray();

            s.Write(HMACSHA256.HashData(Encoding.UTF8.GetBytes(password), nonce));
            if (s.ReadByte() != 1) throw new AuthenticationException("Wrong password");

            s.ReadTimeout = 20000; // the server sends at least a keepalive every 2 s
            return new RemoteConnection(tcp, s);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public void Start()
    {
        new Thread(ReadLoop) { IsBackground = true, Name = "frames" }.Start();
        new Thread(SendLoop) { IsBackground = true, Name = "input" }.Start();
    }

    void ReadLoop()
    {
        string error = null;
        var header = new byte[4];
        try
        {
            while (true)
            {
                stream.ReadExactly(header);
                int len = BinaryPrimitives.ReadInt32BigEndian(header);
                if (len == 0) continue; // keepalive
                if (len < 0 || len > 30_000_000) throw new IOException("Invalid packet");

                var data = new byte[len];
                stream.ReadExactly(data);

                // Copy into a standalone bitmap so the stream can be dropped right away.
                Bitmap frame;
                using (var ms = new MemoryStream(data))
                using (var img = Image.FromStream(ms))
                    frame = new Bitmap(img);

                LastFrameAt = Environment.TickCount64;
                FrameReceived?.Invoke(frame);
            }
        }
        catch (Exception ex)
        {
            if (!closedByUser) error = ex.Message;
        }
        Close(fromReader: true);
        RaiseDisconnected(closedByUser ? null : (error ?? "Connection lost"));
    }

    void SendLoop()
    {
        try
        {
            foreach (var packet in outbox.GetConsumingEnumerable())
                stream.Write(packet);
        }
        catch { }
    }

    void RaiseDisconnected(string message)
    {
        if (Interlocked.Exchange(ref disconnectRaised, 1) == 0)
            Disconnected?.Invoke(message);
    }

    void Enqueue(byte[] packet)
    {
        try { if (!outbox.IsAddingCompleted) outbox.Add(packet); } catch { }
    }

    public void Move(float x, float y)
    {
        var p = new byte[9];
        p[0] = 1;
        BinaryPrimitives.WriteSingleBigEndian(p.AsSpan(1), Math.Clamp(x, 0f, 1f));
        BinaryPrimitives.WriteSingleBigEndian(p.AsSpan(5), Math.Clamp(y, 0f, 1f));
        Enqueue(p);
    }

    public void Button(int button, bool down) => Enqueue(new byte[] { 2, (byte)button, (byte)(down ? 1 : 0) });

    public void Scroll(int delta)
    {
        var p = new byte[5];
        p[0] = 3;
        BinaryPrimitives.WriteInt32BigEndian(p.AsSpan(1), delta);
        Enqueue(p);
    }

    public void Text(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length == 0 || bytes.Length > 60000) return;
        var p = new byte[3 + bytes.Length];
        p[0] = 4;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(1), (ushort)bytes.Length);
        bytes.CopyTo(p, 3);
        Enqueue(p);
    }

    public void Key(int vk, int mods)
    {
        var p = new byte[4];
        p[0] = 5;
        p[1] = (byte)mods;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2), (ushort)vk);
        Enqueue(p);
    }

    public void Power(int action) => Enqueue(new byte[] { 6, (byte)action });

    public void Close() => Close(fromReader: false);

    void Close(bool fromReader)
    {
        if (!fromReader) closedByUser = true;
        try { outbox.CompleteAdding(); } catch { }
        try { tcp.Close(); } catch { }
    }
}
