package com.vyrovr.connect.data

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

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
        val line = "${fmt.format(Date())}  $msg"
        _log.update { (it + line).takeLast(200) }
    }

    fun clearLog() { _log.value = emptyList() }
}
