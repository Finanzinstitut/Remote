package com.example.pcremote

import android.graphics.BitmapFactory
import android.os.Bundle
import android.view.MotionEvent
import android.widget.*
import androidx.appcompat.app.AppCompatActivity
import java.io.BufferedReader
import java.io.InputStreamReader
import java.net.HttpURLConnection
import java.net.URL
import java.net.DatagramPacket
import java.net.DatagramSocket
import kotlin.concurrent.thread
import org.json.JSONObject

class MainActivity : AppCompatActivity() {
    private lateinit var host: EditText
    private lateinit var token: EditText
    private lateinit var mac: EditText
    private lateinit var screen: ImageView
    private var running = false
    private var screenW = 1920
    private var screenH = 1080

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        host = findViewById(R.id.host)
        token = findViewById(R.id.token)
        mac = findViewById(R.id.mac)
        screen = findViewById(R.id.screen)

        findViewById<Button>(R.id.wake).setOnClickListener {
            thread { sendWakeOnLan(mac.text.toString()) }
            Toast.makeText(this, "Wake-on-LAN gesendet", Toast.LENGTH_SHORT).show()
        }

        findViewById<Button>(R.id.connect).setOnClickListener {
            startStream()
        }

        findViewById<Button>(R.id.send).setOnClickListener {
            val text = findViewById<EditText>(R.id.keyboard).text.toString()
            sendKey(text)
        }

        screen.setOnTouchListener { _, event ->
            if (event.action == MotionEvent.ACTION_DOWN ||
                event.action == MotionEvent.ACTION_UP) {
                val x = (event.x / screen.width * screenW).toInt()
                val y = (event.y / screen.height * screenH).toInt()
                sendMouse(x, y, if (event.action == MotionEvent.ACTION_DOWN) "down" else "up")
            }
            true
        }
    }

    private fun baseUrl(): String {
        var h = host.text.toString().trim()
        if (!h.startsWith("http://")) h = "http://$h"
        if (!h.contains(":8765")) h += ":8765"
        return h
    }

    private fun headers(c: HttpURLConnection) {
        c.setRequestProperty("X-PCRemote-Token", token.text.toString().trim())
    }

    private fun startStream() {
        if (running) return
        running = true
        thread {
            while (running) {
                try {
                    val c = URL(baseUrl() + "/screen").openConnection() as HttpURLConnection
                    headers(c)
                    c.connectTimeout = 2000
                    c.readTimeout = 4000
                    val bitmap = BitmapFactory.decodeStream(c.inputStream)
                    c.disconnect()
                    if (bitmap != null) {
                        screenW = bitmap.width
                        screenH = bitmap.height
                        runOnUiThread { screen.setImageBitmap(bitmap) }
                    }
                } catch (_: Exception) {
                    Thread.sleep(1000)
                }
                Thread.sleep(100)
            }
        }
    }

    private fun sendMouse(x: Int, y: Int, action: String) {
        thread {
            try {
                val c = URL(baseUrl() + "/mouse").openConnection() as HttpURLConnection
                c.requestMethod = "POST"
                c.doOutput = true
                headers(c)
                c.outputStream.use {
                    it.write(JSONObject().put("x", x).put("y", y).put("action", action).toString().toByteArray())
                }
                c.inputStream.close()
                c.disconnect()
            } catch (_: Exception) {}
        }
    }

    private fun sendKey(text: String) {
        thread {
            try {
                val c = URL(baseUrl() + "/key").openConnection() as HttpURLConnection
                c.requestMethod = "POST"
                c.doOutput = true
                headers(c)
                c.outputStream.use {
                    it.write(JSONObject().put("text", text).toString().toByteArray())
                }
                c.inputStream.close()
                c.disconnect()
            } catch (_: Exception) {}
        }
    }

    private fun sendWakeOnLan(macText: String) {
        val clean = macText.replace(":", "").replace("-", "").trim()
        if (clean.length != 12) return

        val macBytes = ByteArray(6) { i ->
            clean.substring(i * 2, i * 2 + 2).toInt(16).toByte()
        }
        val packet = ByteArray(102)
        for (i in 0 until 6) packet[i] = 0xFF.toByte()
        for (i in 6 until 102) packet[i] = macBytes[(i - 6) % 6]

        try {
            DatagramSocket().use { socket ->
                socket.broadcast = true
                socket.send(DatagramPacket(packet, packet.size,
                    java.net.InetAddress.getByName("255.255.255.255"), 9))
            }
        } catch (_: Exception) {}
    }

    override fun onDestroy() {
        running = false
        super.onDestroy()
    }
}
