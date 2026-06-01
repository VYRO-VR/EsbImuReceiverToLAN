package com.vyrovr.connect

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbManager
import android.os.Build
import android.os.IBinder
import com.vyrovr.connect.data.AppState
import com.vyrovr.connect.data.Prefs
import com.vyrovr.connect.net.ServerDiscovery
import com.vyrovr.connect.slime.SlimeUdpClient
import com.vyrovr.connect.usb.Acceleration
import com.vyrovr.connect.usb.Battery
import com.vyrovr.connect.usb.DeviceAddress
import com.vyrovr.connect.usb.DeviceInfo
import com.vyrovr.connect.usb.ESB_PID
import com.vyrovr.connect.usb.ESB_VID
import com.vyrovr.connect.usb.EsbEvent
import com.vyrovr.connect.usb.EsbUsbReader
import com.vyrovr.connect.usb.Rotation
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import java.net.Inet4Address
import java.net.NetworkInterface

/** Foreground service that owns the USB read loop and the per-tracker UDP clients. */
class TrackerService : Service() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var reader: EsbUsbReader? = null
    private var readerThread: Thread? = null

    private val clients = HashMap<Int, SlimeUdpClient>()
    private val macs = HashMap<Int, ByteArray>()
    private val infos = HashMap<Int, DeviceInfo>()
    private var serverIp: String? = null

    private val detachReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent?.action == UsbManager.ACTION_USB_DEVICE_DETACHED) {
                val dev = intent.getParcelableExtra<UsbDevice>(UsbManager.EXTRA_DEVICE)
                if (dev != null && dev.vendorId == ESB_VID && dev.productId == ESB_PID) {
                    AppState.log("Receiver unplugged")
                    stopSelf()
                }
            }
        }
    }

    override fun onCreate() {
        super.onCreate()
        startForegroundCompat()
        val filter = IntentFilter(UsbManager.ACTION_USB_DEVICE_DETACHED)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerReceiver(detachReceiver, filter, Context.RECEIVER_NOT_EXPORTED)
        } else {
            @Suppress("UnspecifiedRegisterReceiverFlag")
            registerReceiver(detachReceiver, filter)
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (reader == null) startWork()
        return START_STICKY
    }

    private fun startWork() {
        val manager = getSystemService(Context.USB_SERVICE) as UsbManager
        val device = EsbUsbReader.findReceiver(manager)
        if (device == null) {
            AppState.setStatus("No receiver found")
            AppState.log("No receiver dongle detected")
            stopSelf()
            return
        }
        scope.launch {
            serverIp = Prefs(this@TrackerService).serverIp
            if (serverIp.isNullOrBlank()) {
                AppState.setStatus("Searching for SlimeVR…")
                AppState.log("Scanning local network for SlimeVR…")
                val found = ServerDiscovery.scan(localIp() ?: "")
                if (found == null) {
                    AppState.setStatus("SlimeVR not found")
                    AppState.log("No SlimeVR server found — set the IP manually and Start again.")
                    stopSelf()
                    return@launch
                }
                serverIp = found
                Prefs(this@TrackerService).serverIp = found
                AppState.log("Found SlimeVR at $found")
            }
            AppState.setServerIp(serverIp)
            AppState.setStatus("Connected to $serverIp")
            startReader(manager, device)
        }
    }

    private fun startReader(manager: UsbManager, device: UsbDevice) {
        val r = EsbUsbReader(manager, device) { events -> onEvents(events) }
        reader = r
        readerThread = Thread { r.run() }.apply { isDaemon = true; start() }
        AppState.log("Reading from receiver")
    }

    private fun onEvents(events: List<EsbEvent>) {
        for (e in events) {
            when (e) {
                is DeviceAddress -> { macs[e.deviceId] = e.mac; ensureClient(e.deviceId) }
                is DeviceInfo -> { infos[e.deviceId] = e; ensureClient(e.deviceId) }
                is Rotation -> clients[e.deviceId]?.setRotation(e.x, e.y, e.z, e.w)
                is Acceleration -> clients[e.deviceId]?.setAcceleration(e.x, e.y, e.z)
                is Battery -> clients[e.deviceId]?.setBattery(e.percent, e.voltage)
            }
        }
    }

    private fun ensureClient(id: Int) {
        if (clients.containsKey(id)) return
        val info = infos[id] ?: return
        val ip = serverIp ?: return
        val mac = macs[id] ?: ByteArray(6).also { it[5] = id.toByte() }
        val client = try {
            SlimeUdpClient(
                scope = scope,
                serverIp = ip,
                identifier = "${info.firmware}_EsbToLan_$id",
                mac = mac,
                boardType = info.boardType,
                imuType = info.imuType,
                mcuType = info.mcuType,
                magStatus = info.magStatus,
            )
        } catch (e: Exception) {
            AppState.logError("Could not open UDP client for tracker $id ($ip)", e)
            return
        }
        clients[id] = client
        client.start()
        AppState.setTrackerCount(clients.size)
        AppState.log("Tracker $id registered (fw ${info.firmware})")
    }

    private fun localIp(): String? {
        try {
            for (ni in NetworkInterface.getNetworkInterfaces()) {
                if (!ni.isUp || ni.isLoopback) continue
                for (addr in ni.inetAddresses) {
                    if (addr is Inet4Address && !addr.isLoopbackAddress) return addr.hostAddress
                }
            }
        } catch (_: Exception) {
        }
        return null
    }

    private fun startForegroundCompat() {
        val notification = buildNotification()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIF_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        } else {
            startForeground(NOTIF_ID, notification)
        }
    }

    private fun buildNotification(): Notification {
        val channelId = "tracker_service"
        val nm = getSystemService(NotificationManager::class.java)
        if (nm.getNotificationChannel(channelId) == null) {
            nm.createNotificationChannel(
                NotificationChannel(channelId, "Tracker streaming", NotificationManager.IMPORTANCE_LOW)
            )
        }
        return Notification.Builder(this, channelId)
            .setContentTitle("VYRO VR")
            .setContentText("Streaming trackers to SlimeVR")
            .setSmallIcon(android.R.drawable.ic_menu_compass)
            .setOngoing(true)
            .build()
    }

    override fun onDestroy() {
        reader?.stop()
        clients.values.forEach { it.close() }
        clients.clear()
        try { unregisterReceiver(detachReceiver) } catch (_: Exception) {}
        scope.cancel()
        AppState.setStatus("Stopped")
        AppState.setTrackerCount(0)
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    companion object {
        private const val NOTIF_ID = 1
    }
}
