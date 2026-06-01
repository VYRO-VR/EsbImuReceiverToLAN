package com.vyrovr.connect.slime

import com.vyrovr.connect.data.AppState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

/**
 * One SlimeVR UDP connection per physical tracker. Performs the handshake,
 * keeps a heartbeat alive, answers ping/heartbeat from the server, and forwards
 * rotation / acceleration / battery. Mirrors the original C# UDPHandler.
 */
class SlimeUdpClient(
    private val scope: CoroutineScope,
    serverIp: String,
    private val identifier: String,
    private val mac: ByteArray,
    private val boardType: Int,
    private val imuType: Int,
    private val mcuType: Int,
    private val magStatus: Int,
) {
    private val address: InetAddress = InetAddress.getByName(serverIp)
    private val socket = DatagramSocket().apply {
        connect(address, SlimePackets.PORT)
        soTimeout = 1000
    }
    private val packetId = AtomicLong(0)
    @Volatile var isConnected: Boolean = false
        private set
    private val jobs = mutableListOf<Job>()
    /** Avoids spamming the log: only the first send failure is reported. */
    private val sendErrorLogged = AtomicBoolean(false)

    private fun next() = packetId.getAndIncrement()

    private fun send(data: ByteArray) {
        try {
            socket.send(DatagramPacket(data, data.size))
        } catch (e: Exception) {
            if (sendErrorLogged.compareAndSet(false, true)) {
                AppState.logError("UDP send to $address failed", e)
            }
        }
    }

    fun start() {
        jobs += scope.launch(Dispatchers.IO) { handshakeLoop() }
        jobs += scope.launch(Dispatchers.IO) { receiveLoop() }
        jobs += scope.launch(Dispatchers.IO) { heartbeatLoop() }
    }

    private suspend fun handshakeLoop() {
        AppState.log("Handshaking with SlimeVR at $address ($identifier)")
        while (scope.isActive && !isConnected) {
            send(SlimePackets.handshake(next(), boardType, imuType, mcuType, magStatus, identifier, mac))
            delay(500)
        }
    }

    private suspend fun heartbeatLoop() {
        while (scope.isActive) {
            if (isConnected) send(SlimePackets.heartbeat(next()))
            delay(900)
        }
    }

    private fun receiveLoop() {
        val buf = ByteArray(256)
        while (scope.isActive) {
            val packet = DatagramPacket(buf, buf.size)
            try {
                socket.receive(packet)
            } catch (_: Exception) {
                continue
            }
            val len = packet.length
            if (len <= 0) continue
            val text = String(buf, 0, len, Charsets.UTF_8)
            if (text.contains(SlimePackets.DISCOVERY_REPLY)) {
                if (!isConnected) {
                    isConnected = true
                    AppState.log("SlimeVR accepted handshake ($identifier) — streaming")
                    send(SlimePackets.sensorInfo(next(), 0, imuType))
                }
                continue
            }
            if (len >= 4) {
                val ptype = ((buf[0].toInt() and 0xFF) shl 24) or
                    ((buf[1].toInt() and 0xFF) shl 16) or
                    ((buf[2].toInt() and 0xFF) shl 8) or
                    (buf[3].toInt() and 0xFF)
                when (ptype) {
                    SlimePackets.PING_PONG -> send(buf.copyOf(len)) // echo back verbatim
                    SlimePackets.SERVER_HEARTBEAT -> send(SlimePackets.heartbeat(next()))
                }
            }
        }
    }

    fun setRotation(x: Float, y: Float, z: Float, w: Float) {
        if (isConnected) send(SlimePackets.rotation(next(), 0, x, y, z, w))
    }

    fun setAcceleration(x: Float, y: Float, z: Float) {
        if (isConnected) send(SlimePackets.acceleration(next(), 0, x, y, z))
    }

    fun setBattery(percent: Int, voltage: Float) {
        if (isConnected) send(SlimePackets.battery(next(), voltage, percent / 100f))
    }

    fun close() {
        jobs.forEach { it.cancel() }
        try {
            socket.close()
        } catch (_: Exception) {
        }
    }
}
