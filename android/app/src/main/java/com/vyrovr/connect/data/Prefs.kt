package com.vyrovr.connect.data

import android.content.Context

/** Tiny persistence wrapper for the configured SlimeVR server IP. */
class Prefs(context: Context) {
    private val sp = context.getSharedPreferences("vyro_connect", Context.MODE_PRIVATE)

    var serverIp: String?
        get() = sp.getString("server_ip", null)
        set(value) { sp.edit().putString("server_ip", value).apply() }
}
