package com.vyrovr.connect.data

import android.util.Log
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Logcat tag for the whole app: `adb logcat -s VyroVrConnect:*`. */
const val TAG = "VyroVrConnect"

/** Shared, observable UI state. Written by the service, observed by Compose. */
object AppState {
    private val _status = MutableStateFlow("Idle")
    val status: StateFlow<String> = _status.asStateFlow()

    private val _serverIp = MutableStateFlow<String?>(null)
    val serverIp: StateFlow<String?> = _serverIp.asStateFlow()

    private val _trackerCount = MutableStateFlow(0)
    val trackerCount: StateFlow<Int> = _trackerCount.asStateFlow()

    private val _log = MutableStateFlow<List<String>>(emptyList())
    val log: StateFlow<List<String>> = _log.asStateFlow()

    private val fmt = SimpleDateFormat("HH:mm:ss", Locale.US)

    fun setStatus(s: String) { _status.value = s }
    fun setServerIp(ip: String?) { _serverIp.value = ip }
    fun setTrackerCount(n: Int) { _trackerCount.value = n }

    fun log(msg: String) {
        Log.i(TAG, msg)
        val line = "${fmt.format(Date())}  $msg"
        _log.update { (it + line).takeLast(200) }
    }

    /** Log an error to both the on-screen activity log and logcat (with stacktrace). */
    fun logError(msg: String, t: Throwable? = null) {
        Log.e(TAG, msg, t)
        val detail = t?.message?.let { ": $it" } ?: ""
        val line = "${fmt.format(Date())}  ERROR: $msg$detail"
        _log.update { (it + line).takeLast(200) }
    }

    fun clearLog() { _log.value = emptyList() }
}
