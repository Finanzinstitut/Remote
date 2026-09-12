package de.finanzinstitut.spaceremote

import android.content.Context

class Prefs(context: Context) {
    private val sp = context.getSharedPreferences("spaceremote", Context.MODE_PRIVATE)

    var host: String
        get() = sp.getString("host", "") ?: ""
        set(v) = sp.edit().putString("host", v.trim()).apply()

    var port: Int
        get() = sp.getInt("port", 47800)
        set(v) = sp.edit().putInt("port", v).apply()

    var password: String
        get() = sp.getString("password", "") ?: ""
        set(v) = sp.edit().putString("password", v.trim()).apply()

    var mac: String
        get() = sp.getString("mac", "") ?: ""
        set(v) = sp.edit().putString("mac", v.trim()).apply()

    var broadcast: String
        get() = sp.getString("broadcast", "") ?: ""
        set(v) = sp.edit().putString("broadcast", v.trim()).apply()

    /** Optional URL called before connecting, e.g. to switch on a smart plug. */
    var wakeUrl: String
        get() = sp.getString("wakeUrl", "") ?: ""
        set(v) = sp.edit().putString("wakeUrl", v.trim()).apply()

    val isConfigured: Boolean
        get() = host.isNotBlank() && password.isNotBlank()
}
