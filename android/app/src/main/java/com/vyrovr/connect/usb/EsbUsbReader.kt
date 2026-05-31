package com.vyrovr.connect.usb

import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbDeviceConnection
import android.hardware.usb.UsbEndpoint
import android.hardware.usb.UsbInterface
import android.hardware.usb.UsbManager

const val ESB_VID = 0x1209
const val ESB_PID = 0x7690

/**
 * Opens the ESB receiver dongle and continuously reads 16-byte report packets
 * from interface 2 / endpoint 0 via bulk transfer, parsing each into events.
 * [run] blocks until [stop] is called or the device errors, so run it on its
 * own thread.
 */
class EsbUsbReader(
    private val manager: UsbManager,
    private val device: UsbDevice,
    private val onEvents: (List<EsbEvent>) -> Unit,
) {
    @Volatile private var running = false

    fun run() {
        val connection: UsbDeviceConnection = manager.openDevice(device) ?: return
        val iface: UsbInterface = device.getInterface(2)
        val endpoint: UsbEndpoint = iface.getEndpoint(0)
        if (!connection.claimInterface(iface, true)) {
            connection.close()
            return
        }
        running = true
        val buf = ByteArray(64) // up to 4 packets per transfer
        val events = ArrayList<EsbEvent>(16)
        try {
            while (running) {
                val read = connection.bulkTransfer(endpoint, buf, buf.size, 20)
                if (read <= 0) continue
                if (read % PACKET_SIZE != 0) continue
                events.clear()
                var i = 0
                while (i + PACKET_SIZE <= read) {
                    PacketParser.parse(buf, i, events)
                    i += PACKET_SIZE
                }
                if (events.isNotEmpty()) onEvents(ArrayList(events))
            }
        } catch (_: Exception) {
        } finally {
            try { connection.releaseInterface(iface) } catch (_: Exception) {}
            connection.close()
        }
    }

    fun stop() { running = false }

    companion object {
        fun findReceiver(manager: UsbManager): UsbDevice? =
            manager.deviceList.values.firstOrNull { it.vendorId == ESB_VID && it.productId == ESB_PID }
    }
}
