package de.finanzinstitut.spaceremote

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.Executors
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec
import kotlin.concurrent.thread

class AuthException : Exception("Wrong password")

object Mod {
    const val CTRL = 1
    const val ALT = 2
    const val SHIFT = 4
    const val WIN = 8
}

object Vk {
    const val BACK = 0x08
    const val TAB = 0x09
    const val RETURN = 0x0D
    const val ESCAPE = 0x1B
    const val LEFT = 0x25
    const val UP = 0x26
    const val RIGHT = 0x27
    const val DOWN = 0x28
    const val DELETE = 0x2E
}

/** Connection to the Windows app (protocol: see windows/SpaceRemote/RemoteServer.cs). */
class RemoteClient(
    private val onFrame: (Bitmap) -> Unit,
    private val onDisconnected: (String?) -> Unit,
) {
    @Volatile private var socket: Socket? = null
    @Volatile private var out: DataOutputStream? = null
    @Volatile private var closedByUser = false
    private val sender = Executors.newSingleThreadExecutor()

    /** Blocking — call from an IO thread. */
    fun connect(host: String, port: Int, password: String, timeoutMs: Int) {
        val s = Socket()
        try {
            s.tcpNoDelay = true
            s.connect(InetSocketAddress(host, port), timeoutMs)
            s.soTimeout = 6000

            val input = DataInputStream(BufferedInputStream(s.getInputStream(), 256 * 1024))
            val output = DataOutputStream(BufferedOutputStream(s.getOutputStream()))

            val magic = ByteArray(4)
            input.readFully(magic)
            if (String(magic, Charsets.US_ASCII) != "SPRM") throw IOException("Not a Space Remote server")
            input.readByte() // protocol version
            val nonce = ByteArray(16)
            input.readFully(nonce)

            val mac = Mac.getInstance("HmacSHA256")
            mac.init(SecretKeySpec(password.toByteArray(Charsets.UTF_8), "HmacSHA256"))
            output.write(mac.doFinal(nonce))
            output.flush()

            if (input.readByte().toInt() != 1) throw AuthException()

            s.soTimeout = 20000 // the server sends something at least every 2 s
            socket = s
            out = output
            thread(name = "remote-reader", isDaemon = true) { readLoop(s, input) }
        } catch (e: Exception) {
            runCatching { s.close() }
            throw e
        }
    }

    private fun readLoop(s: Socket, input: DataInputStream) {
        var error: String? = null
        // reuse 3 bitmaps in rotation -> almost no garbage collection
        val ring = arrayOfNulls<Bitmap>(3)
        var idx = 0
        try {
            while (true) {
                val len = input.readInt()
                if (len == 0) continue // Keepalive
                if (len < 0 || len > 30_000_000) throw IOException("Invalid packet")
                val data = ByteArray(len)
                input.readFully(data)

                val opts = BitmapFactory.Options().apply {
                    inMutable = true
                    inBitmap = ring[idx]
                }
                val bmp = try {
                    BitmapFactory.decodeByteArray(data, 0, len, opts)
                } catch (e: IllegalArgumentException) {
                    opts.inBitmap = null // resolution changed
                    BitmapFactory.decodeByteArray(data, 0, len, opts)
                }
                if (bmp != null) {
                    ring[idx] = bmp
                    idx = (idx + 1) % ring.size
                    onFrame(bmp)
                }
            }
        } catch (e: Exception) {
            if (!closedByUser) error = e.message ?: "Connection lost"
        }
        runCatching { s.close() }
        onDisconnected(if (closedByUser) null else error)
    }

    private fun send(block: DataOutputStream.() -> Unit) {
        val o = out ?: return
        try {
            sender.execute {
                try {
                    o.block()
                    o.flush()
                } catch (_: Exception) {
                }
            }
        } catch (_: Exception) {
        }
    }

    fun move(x: Float, y: Float) = send { writeByte(1); writeFloat(x); writeFloat(y) }

    fun button(button: Int, down: Boolean) = send { writeByte(2); writeByte(button); writeByte(if (down) 1 else 0) }

    fun click(button: Int) = send {
        writeByte(2); writeByte(button); writeByte(1)
        writeByte(2); writeByte(button); writeByte(0)
    }

    fun scroll(delta: Int) = send { writeByte(3); writeInt(delta) }

    fun text(t: String) {
        val bytes = t.toByteArray(Charsets.UTF_8)
        if (bytes.isEmpty() || bytes.size > 60000) return
        send { writeByte(4); writeShort(bytes.size); write(bytes) }
    }

    fun key(vk: Int, mods: Int = 0) = send { writeByte(5); writeByte(mods); writeShort(vk) }

    fun power(action: Int) = send { writeByte(6); writeByte(action) }

    fun close() {
        closedByUser = true
        runCatching { socket?.close() }
        sender.shutdown()
    }
}
