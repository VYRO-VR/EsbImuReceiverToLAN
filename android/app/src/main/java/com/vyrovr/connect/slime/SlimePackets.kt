package com.vyrovr.connect.slime

import java.nio.ByteBuffer

/**
 * Builders for the SlimeVR UDP "feeder" protocol. SlimeVR expects network
 * (big-endian) byte order, which is exactly [ByteBuffer]'s default — so we just
 * write fields in order. Ported byte-for-byte from the original C# PacketBuilder.
 */
object SlimePackets {
    const val PORT = 6969
    const val PROTOCOL_VERSION = 19

    // Outgoing packet type ids
    const val HEARTBEAT = 0
    const val HANDSHAKE = 3
    const val ACCELERATION = 4
    const val BATTERY_LEVEL = 12
    const val SENSOR_INFO = 15
    const val ROTATION_DATA = 17

    // Incoming packet type ids (server -> tracker)
    const val SERVER_HEARTBEAT = 1
    const val PING_PONG = 10

    /** ASCII reply the SlimeVR server sends to a handshake. */
    const val DISCOVERY_REPLY = "Hey OVR =D 5"

    fun handshake(
        packetId: Long,
        boardType: Int,
        imuType: Int,
        mcuType: Int,
        magStatus: Int,
        identifier: String,
        mac: ByteArray,
    ): ByteArray {
        val idBytes = identifier.toByteArray(Charsets.UTF_8)
        val b = ByteBuffer.allocate(4 + 8 + 4 * 7 + 1 + idBytes.size + mac.size)
        b.putInt(HANDSHAKE)
        b.putLong(packetId)
        b.putInt(boardType)
        b.putInt(imuType)
        b.putInt(mcuType)
        b.putInt(magStatus)
        b.putInt(magStatus)
        b.putInt(magStatus)
        b.putInt(PROTOCOL_VERSION)
        b.put(idBytes.size.toByte())
        b.put(idBytes)
        b.put(mac)
        return b.array()
    }

    fun sensorInfo(packetId: Long, trackerId: Int, imuType: Int): ByteArray {
        val b = ByteBuffer.allocate(4 + 8 + 1 + 1 + 1 + 2 + 1 + 1)
        b.putInt(SENSOR_INFO)
        b.putLong(packetId)
        b.put(trackerId.toByte())   // tracker id
        b.put(0.toByte())           // sensor status
        b.put(imuType.toByte())     // imu type
        b.putShort(1.toShort())     // calibration state
        b.put(0.toByte())           // tracker position = NONE
        b.put(0.toByte())           // data type = ROTATION
        return b.array()
    }

    fun heartbeat(packetId: Long): ByteArray {
        val b = ByteBuffer.allocate(4 + 8 + 1)
        b.putInt(HEARTBEAT)
        b.putLong(packetId)
        b.put(0.toByte())
        return b.array()
    }

    fun rotation(packetId: Long, trackerId: Int, x: Float, y: Float, z: Float, w: Float): ByteArray {
        val b = ByteBuffer.allocate(4 + 8 + 1 + 1 + 16 + 1)
        b.putInt(ROTATION_DATA)
        b.putLong(packetId)
        b.put(trackerId.toByte())
        b.put(1.toByte())           // data type
        b.putFloat(x); b.putFloat(y); b.putFloat(z); b.putFloat(w)
        b.put(0.toByte())           // calibration info
        return b.array()
    }

    fun acceleration(packetId: Long, trackerId: Int, x: Float, y: Float, z: Float): ByteArray {
        val b = ByteBuffer.allocate(4 + 8 + 12 + 1)
        b.putInt(ACCELERATION)
        b.putLong(packetId)
        b.putFloat(x); b.putFloat(y); b.putFloat(z)
        b.put(trackerId.toByte())
        return b.array()
    }

    fun battery(packetId: Long, voltage: Float, percentage: Float): ByteArray {
        val b = ByteBuffer.allocate(4 + 8 + 8)
        b.putInt(BATTERY_LEVEL)
        b.putLong(packetId)
        b.putFloat(voltage)
        b.putFloat(percentage)
        return b.array()
    }
}
