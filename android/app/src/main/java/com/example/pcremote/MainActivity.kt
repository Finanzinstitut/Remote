package com.example.pcremote

import android.graphics.BitmapFactory
import android.os.Bundle
import android.view.*
import android.widget.*
import androidx.appcompat.app.AppCompatActivity
import org.json.JSONObject
import java.io.BufferedInputStream
import java.net.*
import kotlin.concurrent.thread
import android.content.Context
import android.view.inputmethod.InputMethodManager

class MainActivity: AppCompatActivity() {
    private lateinit var host: EditText
    private lateinit var token: EditText
    private lateinit var mac: EditText
    private lateinit var screen: ImageView
    private var running=false
    private var sw=1920
    private var sh=1080
    private var downX=0f
    private var downY=0f
    private var lastY=0f

    override fun onCreate(b:Bundle?) {
        super.onCreate(b); setContentView(R.layout.activity_main)
        host=findViewById(R.id.host); token=findViewById(R.id.token); mac=findViewById(R.id.mac)
        screen=findViewById(R.id.screen)

        findViewById<Button>(R.id.discover).setOnClickListener { discover() }
        findViewById<Button>(R.id.wake).setOnClickListener {
            thread { wol(mac.text.toString()) }
            toast("Wake-on-LAN gesendet")
        }
        findViewById<Button>(R.id.connect).setOnClickListener { startStream(); fullscreen() }
        findViewById<Button>(R.id.right).setOnClickListener {
            val x=(downX/screen.width*sw).toInt(); val y=(downY/screen.height*sh).toInt()
            mouse(x,y,"rightDown"); mouse(x,y,"rightUp")
        }
        findViewById<Button>(R.id.keyboard).setOnClickListener { showKeyboard() }
        findViewById<Button>(R.id.exit).setOnClickListener { running=false; window.decorView.systemUiVisibility=0 }

        screen.setOnTouchListener { _,e ->
            val x=(e.x/screen.width*sw).toInt()
            val y=(e.y/screen.height*sh).toInt()
            when(e.actionMasked) {
                MotionEvent.ACTION_DOWN -> { downX=e.x; downY=e.y; lastY=e.y; mouse(x,y,"leftDown") }
                MotionEvent.ACTION_MOVE -> { mouse(x,y,"move"); if(kotlin.math.abs(e.y-lastY)>25) { scroll((lastY-e.y).toInt()); lastY=e.y } }
                MotionEvent.ACTION_UP -> { mouse(x,y,"leftUp") }
            }; true
        }
    }

    private fun base():String {
        var h=host.text.toString().trim()
        if(!h.startsWith("http://")&&!h.startsWith("https://")) h="http://$h"
        if(!h.contains(":8765")) h+=":8765"
        return h
    }
    private fun headers(c:HttpURLConnection){ c.setRequestProperty("X-PCRemote-Token",token.text.toString().trim()) }

    private fun startStream() {
        if(running)return
        running=true
        thread {
            try {
                val c=URL(base()+"/stream").openConnection() as HttpURLConnection
                headers(c); c.connectTimeout=3000; c.readTimeout=0
                val input=BufferedInputStream(c.inputStream)
                val buf=ByteArray(65536); val jpeg=java.io.ByteArrayOutputStream()
                var state=0
                while(running) {
                    val n=input.read(buf); if(n<0) break
                    for(i in 0 until n) {
                        val v=buf[i].toInt() and 255
                        if(state==0 && v==0xFF) state=1
                        else if(state==1 && v==0xD8) { jpeg.reset(); jpeg.write(0xFF); jpeg.write(0xD8); state=2 }
                        else if(state==2) {
                            jpeg.write(v)
                            if(v==0xFF) state=3
                        } else if(state==3) {
                            jpeg.write(v)
                            if(v==0xD9) {
                                val bytes=jpeg.toByteArray()
                                val bmp=BitmapFactory.decodeByteArray(bytes,0,bytes.size)
                                if(bmp!=null) { sw=bmp.width; sh=bmp.height; runOnUiThread{screen.setImageBitmap(bmp)} }
                                state=0
                            } else state=2
                        }
                    }
                }
                input.close(); c.disconnect()
            } catch(e:Exception) { runOnUiThread{toast("Verbindung verloren")} }
            running=false
        }
    }

    private fun mouse(x:Int,y:Int,a:String)=thread {
        post("/mouse",JSONObject().put("x",x).put("y",y).put("action",a))
    }
    private fun scroll(d:Int)=thread { post("/scroll",JSONObject().put("delta",d*20)) }
    private fun post(path: String, obj:JSONObject) {
        try {
            val c=URL(base()+path).openConnection() as HttpURLConnection
            c.requestMethod="POST"; c.doOutput=true; headers(c)
            c.outputStream.use{it.write(obj.toString().toByteArray())}; c.inputStream.close(); c.disconnect()
        }catch(_:Exception){}
    }

    private fun showKeyboard() {
        val input=EditText(this); input.hint="Text an PC"
        AlertDialog.Builder(this).setTitle("Tastatur").setView(input)
            .setPositiveButton("Senden"){_,_-> thread{post("/key",JSONObject().put("text",input.text.toString()))}}
            .setNegativeButton("Abbrechen",null).show()
    }
    private fun fullscreen(){ window.decorView.systemUiVisibility=(View.SYSTEM_UI_FLAG_FULLSCREEN or View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION) }
    private fun discover() {
        thread {
            try {
                DatagramSocket().use { s ->
                    s.broadcast=true; s.soTimeout=1200
                    val data="PCREMOTE_DISCOVER".toByteArray()
                    s.send(DatagramPacket(data,data.size,InetAddress.getByName("255.255.255.255"),8766))
                    val b=ByteArray(2048)
                    val p=DatagramPacket(b,b.size); s.receive(p)
                    val j=JSONObject(String(p.data,0,p.length))
                    runOnUiThread { host.setText(j.optString("ip")); toast("PC gefunden") }
                }
            }catch(_:Exception){runOnUiThread{toast("Kein PC gefunden")}}
        }
    }
    private fun wol(s:String) {
        val x=s.replace(":","").replace("-","")
        if(x.length!=12)return
        val m=ByteArray(6){i->x.substring(i*2,i*2+2).toInt(16).toByte()}
        val p=ByteArray(102){i->if(i<6)0xFF.toByte() else m[(i-6)%6]}
        try{DatagramSocket().use{it.broadcast=true;it.send(DatagramPacket(p,p.size,InetAddress.getByName("255.255.255.255"),9))}}catch(_:Exception){}
    }
    private fun toast(s:String)=Toast.makeText(this,s,Toast.LENGTH_SHORT).show()
}
