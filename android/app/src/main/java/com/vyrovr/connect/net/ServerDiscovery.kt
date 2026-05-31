package com.vyrovr.connect.net

import com.vyrovr.connect.slime.SlimePackets
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

/**
 * Finds a SlimeVR server on the local /24 by spraying handshake packets at every
 * host on UDP 6969 and watching for the "Hey OVR =D 5" reply.
 */
object ServerDiscovery {
    suspend fun scan(localIp: String, timeoutMs: Long = 8000): String? = withContext(Dispatchers.IO) {
        val base = localIp.substringBeforeLast('.', "")
        if (base.isEmpty()) return@withContext null

        val socket = DatagramSocket()
        socket.broadcast = true
        socket.soTimeout = 200
        try {
            val probe = SlimePackets.handshake(0, 0, 0, 0, 0, "VYRO VR Connect", ByteArray(6))
            val deadline = System.currentTimeMillis() + timeoutMs

            for (host in 1..254) {
                val ip = "$base.$host"
                try {
                    socket.send(DatagramPacket(probe, probe.size, InetAddress.getByName(ip), SlimePackets.PORT))
                } catch (_: Exception) {
                }
                if (host % 16 == 0) delay(4)
            }

            val buf = ByteArray(256)
            while (System.currentTimeMillis() < deadline) {
                val packet = DatagramPacket(buf, buf.size)
                try {
                    socket.receive(packet)
                } catch (_: Exception) {
                    continue
                }
                val text = String(buf, 0, packet.length, Charsets.UTF_8)
                if (text.contains(SlimePackets.DISCOVERY_REPLY)) {
                    return@withContext packet.address.hostAddress
                }
            }
            null
        } finally {
            socket.close()
        }
    }
}
