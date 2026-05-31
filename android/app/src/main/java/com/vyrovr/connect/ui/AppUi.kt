package com.vyrovr.connect.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.vyrovr.connect.data.AppState

@Composable
fun AppUi(onStart: (String?) -> Unit, onStop: () -> Unit) {
    MaterialTheme {
        val status by AppState.status.collectAsState()
        val serverIp by AppState.serverIp.collectAsState()
        val trackerCount by AppState.trackerCount.collectAsState()
        val log by AppState.log.collectAsState()
        var ip by remember { mutableStateOf("") }

        Surface(modifier = Modifier.fillMaxSize()) {
            Column(
                modifier = Modifier.fillMaxSize().padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Text("VYRO VR Connect", style = MaterialTheme.typography.headlineSmall)
                Text("Streams SlimeVR tracker data to your PC over Wi-Fi")

                OutlinedTextField(
                    value = ip,
                    onValueChange = { ip = it },
                    label = { Text("SlimeVR IP (blank = auto-detect)") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )

                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(onClick = { onStart(ip) }) { Text("Start") }
                    OutlinedButton(onClick = onStop) { Text("Stop") }
                }

                Text("Status: $status")
                Text("Server: ${serverIp ?: "—"}    Trackers: $trackerCount")

                HorizontalDivider()
                Text("Activity log", style = MaterialTheme.typography.titleMedium)

                val scroll = rememberScrollState()
                LaunchedEffect(log.size) { scroll.scrollTo(scroll.maxValue) }
                Column(
                    modifier = Modifier.weight(1f).fillMaxWidth().verticalScroll(scroll),
                ) {
                    log.forEach { Text(it, style = MaterialTheme.typography.bodySmall) }
                }
                OutlinedButton(onClick = { AppState.clearLog() }) { Text("Clear log") }
            }
        }
    }
}
