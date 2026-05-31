package com.vyrovr.connect.usb

import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.sin
import kotlin.math.sqrt

/** Each ESB receiver report is a fixed 16-byte packet. */
const val PACKET_SIZE = 16

/** Parsed events emitted from the raw USB stream. */
sealed interface EsbEvent {
    val deviceId: Int
}

data class DeviceInfo(
    override val deviceId: Int,
    val batteryPercent: Int,
    val batteryVoltage: Float,
    val boardType: Int,
    val mcuType: Int,
    val imuType: Int,
    val magStatus: Int,
    val firmware: String,
    val rssi: Int,
) : EsbEvent

data class DeviceAddress(override val deviceId: Int, val mac: ByteArray) : EsbEvent {
    override fun equals(other: Any?) =
        other is DeviceAddress && other.deviceId == deviceId && other.mac.contentEquals(mac)
    override fun hashCode() = 31 * deviceId + mac.contentHashCode()
}

data class Rotation(
    override val deviceId: Int,
    val x: Float, val y: Float, val z: Float, val w: Float,
) : EsbEvent

data class Acceleration(
    override val deviceId: Int,
    val x: Float, val y: Float, val z: Float,
) : EsbEvent

data class Battery(override val deviceId: Int, val percent: Int, val voltage: Float) : EsbEvent

/**
 * Decodes the dongle's 16-byte packets. Field layouts and scaling are ported
 * directly from the original TrackersHID_Android.cs. All multi-byte ints are
 * little-endian.
 */
object PacketParser {

    /** Acceleration axis remap used by the original app: (x, z, -y). */
    private fun unsandwich(x: Float, y: Float, z: Float) = floatArrayOf(x, z, -y)

    fun parse(buf: ByteArray, offset: Int, out: MutableList<EsbEvent>) {
        fun u8(i: Int) = buf[offset + i].toInt() and 0xFF
        // unsigned 16-bit assembled little-endian, then reinterpreted as signed
        fun s16(i: Int): Int = (((u8(i + 1) shl 8) or u8(i)).toShort()).toInt()

        val type = u8(0)
        val id = u8(1)

        when (type) {
            255 -> {
                // 6-byte hardware address (little-endian) in bytes 2..7
                val mac = ByteArray(6) { buf[offset + 2 + it] }
                out.add(DeviceAddress(id, mac))
            }
            0 -> {
                val battery = u8(2) and 0x7F
                val voltage = (u8(3) + 245) / 100f
                val board = u8(5)
                val mcu = u8(6)
                val imu = u8(8)
                val mag = u8(9)
                val fwMajor = u8(12); val fwMinor = u8(13); val fwPatch = u8(14)
                val rssi = u8(15)
                out.add(
                    DeviceInfo(
                        deviceId = id,
                        batteryPercent = battery,
                        batteryVoltage = voltage,
                        boardType = board,
                        mcuType = mcu,
                        imuType = imu,
                        magStatus = mag,
                        firmware = "$fwMajor.$fwMinor.$fwPatch",
                        rssi = rssi,
                    )
                )
                out.add(Battery(id, battery, voltage))
            }
            1, 4 -> {
                // Full-precision quaternion (Q15) in bytes 2..9, ordered x,y,z,w
                val x = s16(2) / 32768f
                val y = s16(4) / 32768f
                val z = s16(6) / 32768f
                val w = s16(8) / 32768f
                out.add(Rotation(id, x, y, z, w))
                if (type == 1) {
                    // Acceleration (1/128) in bytes 10..15
                    val a = unsandwich(s16(10) / 128f, s16(12) / 128f, s16(14) / 128f)
                    out.add(Acceleration(id, a[0], a[1], a[2]))
                }
            }
            2 -> {
                val battery = u8(2) and 0x7F
                val voltage = (u8(3) + 245) / 100f
                // Packed quaternion: 10/11/11 bits across bytes 5..8 (little-endian u32)
                val qBuf = (u8(5).toLong()) or
                    (u8(6).toLong() shl 8) or
                    (u8(7).toLong() shl 16) or
                    (u8(8).toLong() shl 24)
                val q0 = (qBuf and 1023L).toInt()
                val q1 = ((qBuf shr 10) and 2047L).toInt()
                val q2 = ((qBuf shr 21) and 2047L).toInt()
                var v0 = q0 / 1024f * 2f - 1f
                var v1 = q1 / 2048f * 2f - 1f
                var v2 = q2 / 2048f * 2f - 1f
                val d = v0 * v0 + v1 * v1 + v2 * v2
                val invSqrtD = 1.0f / sqrt(d + 1e-6f)
                val aAngle = (PI.toFloat() / 2f) * d * invSqrtD
                val s = sin(aAngle)
                val k = s * invSqrtD
                out.add(Rotation(id, k * v0, k * v1, k * v2, cos(aAngle)))
                // Acceleration (1/128) in bytes 9..14
                val a = unsandwich(s16(9) / 128f, s16(11) / 128f, s16(13) / 128f)
                out.add(Acceleration(id, a[0], a[1], a[2]))
                out.add(Battery(id, battery, voltage))
            }
            // type 3 (status) carries nothing we relay in the MVP
        }
    }
}
