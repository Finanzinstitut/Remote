package de.finanzinstitut.spaceremote

import java.net.HttpURLConnection
import java.net.URL

/** Calls a freely configurable URL, for example to switch on a smart plug. */
object WakeUrl {

    /** Blocking — call from an IO thread. Throws on failure. */
    fun call(url: String) {
        val u = URL(url.trim())
        if (u.protocol != "http" && u.protocol != "https")
            throw IllegalArgumentException("URL must start with http:// or https://")

        val conn = u.openConnection() as HttpURLConnection
        try {
            conn.requestMethod = "GET"
            conn.connectTimeout = 8000
            conn.readTimeout = 8000
            conn.instanceFollowRedirects = true
            val code = conn.responseCode
            if (code !in 200..399) throw IllegalStateException("Smart plug answered with HTTP $code")
            conn.inputStream.use { it.readBytes() }
        } finally {
            conn.disconnect()
        }
    }
}
