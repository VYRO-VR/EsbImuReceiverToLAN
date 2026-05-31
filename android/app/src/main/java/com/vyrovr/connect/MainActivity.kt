package com.vyrovr.connect

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.hardware.usb.UsbManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import com.vyrovr.connect.data.AppState
import com.vyrovr.connect.data.Prefs
import com.vyrovr.connect.ui.AppUi
import com.vyrovr.connect.usb.EsbUsbReader

class MainActivity : ComponentActivity() {

    private val usbPermissionReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent?.action == ACTION_USB_PERMISSION) {
                val granted = intent.getBooleanExtra(UsbManager.EXTRA_PERMISSION_GRANTED, false)
                if (granted) startTrackerService() else AppState.log("USB permission denied")
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val filter = IntentFilter(ACTION_USB_PERMISSION)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerReceiver(usbPermissionReceiver, filter, Context.RECEIVER_NOT_EXPORTED)
        } else {
            @Suppress("UnspecifiedRegisterReceiverFlag")
            registerReceiver(usbPermissionReceiver, filter)
        }

        setContent {
            AppUi(
                onStart = { ip -> onStartRequested(ip) },
                onStop = { stopService(Intent(this, TrackerService::class.java)) },
            )
        }
        maybeAutoStartFromAttach(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        maybeAutoStartFromAttach(intent)
    }

    private fun maybeAutoStartFromAttach(intent: Intent?) {
        if (intent?.action == UsbManager.ACTION_USB_DEVICE_ATTACHED) {
            onStartRequested(Prefs(this).serverIp)
        }
    }

    private fun onStartRequested(ip: String?) {
        Prefs(this).serverIp = ip?.trim()?.ifBlank { null }
        val manager = getSystemService(Context.USB_SERVICE) as UsbManager
        val device = EsbUsbReader.findReceiver(manager)
        if (device == null) {
            AppState.log("No receiver dongle found. Plug it in.")
            return
        }
        if (manager.hasPermission(device)) {
            startTrackerService()
        } else {
            val flags = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) PendingIntent.FLAG_IMMUTABLE else 0
            val pi = PendingIntent.getBroadcast(
                this, 0, Intent(ACTION_USB_PERMISSION).setPackage(packageName), flags
            )
            manager.requestPermission(device, pi)
        }
    }

    private fun startTrackerService() {
        val intent = Intent(this, TrackerService::class.java)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) startForegroundService(intent)
        else startService(intent)
    }

    override fun onDestroy() {
        try { unregisterReceiver(usbPermissionReceiver) } catch (_: Exception) {}
        super.onDestroy()
    }

    companion object {
        private const val ACTION_USB_PERMISSION = "com.vyrovr.connect.USB_PERMISSION"
    }
}
