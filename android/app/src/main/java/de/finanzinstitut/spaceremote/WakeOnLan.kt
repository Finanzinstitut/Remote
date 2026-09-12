package de.finanzinstitut.spaceremote

import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

object WakeOnLan {

    /**
     * Is this address a device on the same home network? Only then can a Wake-on-LAN
     * broadcast arrive at all. Tailscale addresses (100.64-100.127) and host names
     * count as "away from home".
     */
    fun isLocalNetwork(host: String): Boolean {
        val parts = host.trim().split(".")
        if (parts.size != 4) return false
        val n = parts.map { it.toIntOrNull() ?: return false }
        if (n.any { it !in 0..255 }) return false
        return when {
            n[0] == 192 && n[1] == 168 -> true
            n[0] == 10 -> true
            n[0] == 172 && n[1] in 16..31 -> true
            else -> false
        }
    }

    fun parseMac(mac: String): ByteArray? {
        val hex = mac.replace(Regex("[^0-9A-Fa-f]"), "")
        if (hex.length != 12) return null
        return ByteArray(6) { i -> hex.substring(i * 2, i * 2 + 2).toInt(16).toByte() }
    }

    /** Sends the magic packet (blocking, call from an IO thread). */
    fun send(mac: String, host: String, broadcast: String?) {
        val macBytes = parseMac(mac) ?: throw IllegalArgumentException("Invalid MAC address")

        val packet = ByteArray(6 + 16 * 6)
        for (i in 0 until 6) packet[i] = 0xFF.toByte()
        for (i in 1..16) System.arraycopy(macBytes, 0, packet, i * 6, 6)

        val targets = linkedSetOf<String>()
        if (!broadcast.isNullOrBlank()) targets += broadcast.trim()
        subnetBroadcast(host)?.let { targets += it }
        targets += "255.255.255.255"

        var sent = 0
        DatagramSocket().use { socket ->
            socket.broadcast = true
            repeat(3) {
                for (target in targets) {
                    for (port in intArrayOf(9, 7)) {
                        runCatching {
                            socket.send(DatagramPacket(packet, packet.size, InetAddress.getByName(target), port))
                            sent++
                        }
                    }
                }
                Thread.sleep(100)
            }
        }
        if (sent == 0) throw IllegalStateException("No network - is the phone on Wi-Fi?")
    }

    /** 192.168.178.20 -> 192.168.178.255 (fits the usual /24 home networks) */
    private fun subnetBroadcast(host: String): String? {
        val parts = host.trim().split(".")
        if (parts.size != 4 || parts.any { it.toIntOrNull() == null }) return null
        return "${parts[0]}.${parts[1]}.${parts[2]}.255"
    }
}
