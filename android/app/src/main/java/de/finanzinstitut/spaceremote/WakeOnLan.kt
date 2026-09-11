package de.finanzinstitut.spaceremote

import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

object WakeOnLan {

    fun parseMac(mac: String): ByteArray? {
        val hex = mac.replace(Regex("[^0-9A-Fa-f]"), "")
        if (hex.length != 12) return null
        return ByteArray(6) { i -> hex.substring(i * 2, i * 2 + 2).toInt(16).toByte() }
    }

    /** Sendet das Magic Packet (blockierend, im IO-Thread aufrufen). */
    fun send(mac: String, host: String, broadcast: String?) {
        val macBytes = parseMac(mac) ?: throw IllegalArgumentException("Ungültige MAC-Adresse")

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
        if (sent == 0) throw IllegalStateException("Kein Netzwerk – ist das Handy im WLAN?")
    }

    /** 192.168.178.20 -> 192.168.178.255 (passt für die üblichen /24-Heimnetze) */
    private fun subnetBroadcast(host: String): String? {
        val parts = host.trim().split(".")
        if (parts.size != 4 || parts.any { it.toIntOrNull() == null }) return null
        return "${parts[0]}.${parts[1]}.${parts[2]}.255"
    }
}
