package de.finanzinstitut.spaceremote

import android.os.Bundle
import android.os.SystemClock
import android.view.ViewGroup
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.displayCutoutPadding
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.lifecycle.lifecycleScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

enum class Screen { HOME, SETTINGS, CONNECTING, REMOTE }

private val Muted = Color(0xFF9AA3C7)
private val ErrorRed = Color(0xFFFF8A80)

private val specialKeys = listOf(
    Triple("Esc", Vk.ESCAPE, 0),
    Triple("Tab", Vk.TAB, 0),
    Triple("Win", 0x5B, 0),
    Triple("Alt+Tab", Vk.TAB, Mod.ALT),
    Triple("Alt+F4", 0x73, Mod.ALT),
    Triple("Win+D", 0x44, Mod.WIN),
    Triple("Strg+C", 0x43, Mod.CTRL),
    Triple("Strg+V", 0x56, Mod.CTRL),
    Triple("Strg+Z", 0x5A, Mod.CTRL),
    Triple("Entf", Vk.DELETE, 0),
    Triple("Task-Manager", Vk.ESCAPE, Mod.CTRL or Mod.SHIFT),
    Triple("←", Vk.LEFT, 0),
    Triple("↑", Vk.UP, 0),
    Triple("↓", Vk.DOWN, 0),
    Triple("→", Vk.RIGHT, 0),
    Triple("F5", 0x74, 0),
    Triple("F11", 0x7A, 0),
)

class MainActivity : ComponentActivity() {

    private lateinit var prefs: Prefs
    private lateinit var remoteView: RemoteView

    private var screen by mutableStateOf(Screen.HOME)
    private var status by mutableStateOf("")
    private var message by mutableStateOf<String?>(null)
    private var hasFrame by mutableStateOf(false)

    private var client: RemoteClient? = null
    private var connectJob: Job? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        prefs = Prefs(this)
        remoteView = RemoteView(this)
        screen = if (prefs.isConfigured) Screen.HOME else Screen.SETTINGS

        setContent {
            MaterialTheme(
                colorScheme = darkColorScheme(
                    primary = Color(0xFF8FA8FF),
                    onPrimary = Color(0xFF0A0D1F),
                    background = Color(0xFF0A0D1F),
                    surface = Color(0xFF151A33),
                )
            ) {
                Surface(Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
                    Root()
                }
            }
        }
    }

    override fun onDestroy() {
        client?.close()
        super.onDestroy()
    }

    // ------------------------------------------------------------ Logik

    /** Erst verbinden; klappt das nicht, PC per Wake-on-LAN wecken und warten, bis er da ist. */
    private fun startConnect() {
        message = null
        status = "Verbinde mit ${prefs.host} …"
        screen = Screen.CONNECTING
        connectJob?.cancel()

        connectJob = lifecycleScope.launch {
            val host = prefs.host
            val port = prefs.port
            val password = prefs.password
            val mac = prefs.mac
            val broadcast = prefs.broadcast
            val canWake = mac.isNotBlank()
            val limitMs = if (canWake) 240_000L else 20_000L
            val start = SystemClock.elapsedRealtime()
            var lastWake = 0L
            var first = true

            while (isActive) {
                if (SystemClock.elapsedRealtime() - start > limitMs) {
                    disconnect(
                        if (canWake) "Der PC hat sich nicht gemeldet. Prüfe Wake-on-LAN und ob Space Remote auf dem PC automatisch startet."
                        else "PC nicht erreichbar. Trag in den Einstellungen die MAC-Adresse ein, damit die App ihn aufwecken kann."
                    )
                    return@launch
                }

                lateinit var c: RemoteClient
                c = RemoteClient(
                    onFrame = { bmp ->
                        remoteView.setFrame(bmp)
                        if (!hasFrame) runOnUiThread { hasFrame = true }
                    },
                    onDisconnected = { err -> runOnUiThread { onClientClosed(c, err) } },
                )

                try {
                    remoteView.clear()
                    hasFrame = false
                    withContext(Dispatchers.IO) { c.connect(host, port, password, if (first) 1500 else 2500) }
                    client = c
                    remoteView.client = c
                    screen = Screen.REMOTE
                    applyImmersive(true)
                    return@launch
                } catch (e: CancellationException) {
                    c.close()
                    throw e
                } catch (e: AuthException) {
                    c.close()
                    disconnect("Falsches Passwort. Das richtige zeigt dir die Windows-App unter „Verbindungsdaten anzeigen“.")
                    return@launch
                } catch (e: Exception) {
                    c.close()
                    first = false
                    if (canWake && SystemClock.elapsedRealtime() - lastWake > 20_000) {
                        status = "PC ist aus – sende Wake-on-LAN …"
                        try {
                            withContext(Dispatchers.IO) { WakeOnLan.send(mac, host, broadcast) }
                            lastWake = SystemClock.elapsedRealtime()
                        } catch (w: Exception) {
                            disconnect("Wake-on-LAN fehlgeschlagen: ${w.message}")
                            return@launch
                        }
                    }
                    val secs = (SystemClock.elapsedRealtime() - start) / 1000
                    status = if (lastWake > 0) "PC startet …\nWarte auf Windows und Space Remote ($secs s)"
                    else "Verbinde … ($secs s)"
                    delay(2000)
                }
            }
        }
    }

    private fun disconnect(msg: String? = null) {
        connectJob?.cancel()
        connectJob = null
        val c = client
        client = null
        remoteView.client = null
        c?.close()
        remoteView.hideKeyboard()
        applyImmersive(false)
        message = msg
        screen = if (prefs.isConfigured) Screen.HOME else Screen.SETTINGS
    }

    private fun onClientClosed(c: RemoteClient, err: String?) {
        if (client !== c) return
        disconnect(if (err != null) "Verbindung getrennt: $err" else "Verbindung getrennt.")
    }

    private fun powerAction(action: Int) {
        client?.power(action)
        if (action in 0..2) {
            val text = when (action) {
                0 -> "PC wird heruntergefahren."
                1 -> "PC startet neu. Tippe gleich wieder auf „PC starten“."
                else -> "PC ist im Energiesparmodus."
            }
            lifecycleScope.launch {
                delay(700)
                disconnect(text)
            }
        }
    }

    private fun applyImmersive(on: Boolean) {
        val ctrl = WindowCompat.getInsetsController(window, window.decorView)
        if (on) {
            ctrl.systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            ctrl.hide(WindowInsetsCompat.Type.systemBars())
        } else {
            ctrl.show(WindowInsetsCompat.Type.systemBars())
        }
    }

    // ------------------------------------------------------------ UI

    @Composable
    private fun Root() {
        when (screen) {
            Screen.SETTINGS -> {
                BackHandler(enabled = prefs.isConfigured) { screen = Screen.HOME }
                SettingsScreen()
            }
            Screen.HOME -> HomeScreen()
            Screen.CONNECTING -> {
                BackHandler { disconnect() }
                ConnectingScreen()
            }
            Screen.REMOTE -> RemoteScreen()
        }
    }

    @Composable
    private fun HomeScreen() {
        Column(
            Modifier
                .fillMaxSize()
                .background(Brush.verticalGradient(listOf(Color(0xFF1B2150), Color(0xFF0A0D1F))))
                .safeDrawingPadding()
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            Text("Space Remote", fontSize = 30.sp, fontWeight = FontWeight.Bold, color = Color.White)
            Text("${prefs.host}:${prefs.port}", color = Muted)
            Spacer(Modifier.height(48.dp))
            Button(
                onClick = { startConnect() },
                shape = CircleShape,
                modifier = Modifier.size(210.dp),
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("⏻", fontSize = 56.sp)
                    Text("PC starten", fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
                }
            }
            Spacer(Modifier.height(20.dp))
            Text(
                if (prefs.mac.isBlank()) "Ohne MAC-Adresse wird nur verbunden, nicht aufgeweckt."
                else "Weckt den PC auf und zeigt seinen Bildschirm, sobald Windows bereit ist.",
                color = Muted,
                fontSize = 13.sp,
                textAlign = TextAlign.Center,
            )
            message?.let {
                Spacer(Modifier.height(16.dp))
                Text(it, color = ErrorRed, textAlign = TextAlign.Center)
            }
            Spacer(Modifier.height(32.dp))
            TextButton(onClick = { message = null; screen = Screen.SETTINGS }) { Text("Einstellungen") }
        }
    }

    @Composable
    private fun SettingsScreen() {
        var host by remember { mutableStateOf(prefs.host) }
        var port by remember { mutableStateOf(prefs.port.toString()) }
        var password by remember { mutableStateOf(prefs.password) }
        var mac by remember { mutableStateOf(prefs.mac) }
        var broadcast by remember { mutableStateOf(prefs.broadcast) }
        var error by remember { mutableStateOf<String?>(null) }

        Column(
            Modifier
                .fillMaxSize()
                .safeDrawingPadding()
                .imePadding()
                .verticalScroll(rememberScrollState())
                .padding(24.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text("Einstellungen", fontSize = 26.sp, fontWeight = FontWeight.Bold)
            Text(
                "Alle Werte zeigt dir die Windows-App: Rechtsklick auf das Space-Remote-Symbol unten rechts in der Taskleiste → „Verbindungsdaten anzeigen“.",
                color = Muted,
                fontSize = 14.sp,
            )
            Field("IP-Adresse des PCs", host, { host = it }, "z. B. 192.168.178.20", KeyboardType.Uri)
            Field("Port", port, { port = it.filter(Char::isDigit) }, "47800", KeyboardType.Number)
            Field("Passwort", password, { password = it }, "aus der Windows-App", KeyboardType.Ascii)
            Field("MAC-Adresse (für Wake-on-LAN)", mac, { mac = it }, "AA:BB:CC:DD:EE:FF", KeyboardType.Ascii)
            Field("Broadcast-Adresse (optional)", broadcast, { broadcast = it }, "leer = automatisch", KeyboardType.Uri)
            error?.let { Text(it, color = ErrorRed) }
            Button(
                onClick = {
                    val p = port.toIntOrNull()
                    error = when {
                        host.isBlank() -> "Bitte die IP-Adresse des PCs eintragen."
                        p == null || p !in 1..65535 -> "Der Port muss zwischen 1 und 65535 liegen."
                        password.isBlank() -> "Bitte das Passwort aus der Windows-App eintragen."
                        mac.isNotBlank() && WakeOnLan.parseMac(mac) == null -> "Die MAC-Adresse braucht 12 Hex-Zeichen, z. B. AA:BB:CC:DD:EE:FF."
                        else -> null
                    }
                    if (error == null) {
                        prefs.host = host
                        prefs.port = p!!
                        prefs.password = password
                        prefs.mac = mac
                        prefs.broadcast = broadcast
                        screen = Screen.HOME
                    }
                },
                modifier = Modifier.fillMaxWidth(),
            ) { Text("Speichern") }
            if (prefs.isConfigured) {
                TextButton(onClick = { screen = Screen.HOME }, modifier = Modifier.fillMaxWidth()) { Text("Abbrechen") }
            }
        }
    }

    @Composable
    private fun Field(label: String, value: String, onChange: (String) -> Unit, hint: String, type: KeyboardType) {
        OutlinedTextField(
            value = value,
            onValueChange = onChange,
            label = { Text(label) },
            placeholder = { Text(hint) },
            singleLine = true,
            keyboardOptions = KeyboardOptions(keyboardType = type),
            modifier = Modifier.fillMaxWidth(),
        )
    }

    @Composable
    private fun ConnectingScreen() {
        Column(
            Modifier
                .fillMaxSize()
                .safeDrawingPadding()
                .padding(24.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.Center,
        ) {
            CircularProgressIndicator(modifier = Modifier.size(64.dp))
            Spacer(Modifier.height(28.dp))
            Text(status, fontSize = 17.sp, textAlign = TextAlign.Center)
            Spacer(Modifier.height(36.dp))
            OutlinedButton(onClick = { disconnect() }) { Text("Abbrechen") }
        }
    }

    @Composable
    private fun RemoteScreen() {
        var showKeys by remember { mutableStateOf(false) }
        var showPower by remember { mutableStateOf(false) }
        var confirmLeave by remember { mutableStateOf(false) }
        var stale by remember { mutableStateOf(false) }

        LaunchedEffect(Unit) {
            while (true) {
                delay(1000)
                stale = hasFrame && SystemClock.elapsedRealtime() - remoteView.lastFrameAt > 4000
            }
        }
        BackHandler { confirmLeave = true }

        BoxWithConstraints(
            Modifier
                .fillMaxSize()
                .background(Color.Black)
                .displayCutoutPadding()
                .imePadding()
        ) {
            val landscape = maxWidth > maxHeight
            if (landscape) {
                Row(Modifier.fillMaxSize()) {
                    StreamArea(Modifier.weight(1f).fillMaxHeight(), showKeys, stale)
                    ToolBar(true, { showKeys = !showKeys }, { showPower = true }, { disconnect() })
                }
            } else {
                Column(Modifier.fillMaxSize()) {
                    StreamArea(Modifier.weight(1f).fillMaxWidth(), showKeys, stale)
                    ToolBar(false, { showKeys = !showKeys }, { showPower = true }, { disconnect() })
                }
            }
        }

        if (showPower) {
            AlertDialog(
                onDismissRequest = { showPower = false },
                title = { Text("PC-Energie") },
                text = {
                    Column {
                        listOf("Herunterfahren" to 0, "Neu starten" to 1, "Energiesparen" to 2, "Sperren" to 3)
                            .forEach { (label, action) ->
                                TextButton(
                                    onClick = { showPower = false; powerAction(action) },
                                    modifier = Modifier.fillMaxWidth(),
                                ) { Text(label) }
                            }
                    }
                },
                confirmButton = { TextButton(onClick = { showPower = false }) { Text("Abbrechen") } },
            )
        }

        if (confirmLeave) {
            AlertDialog(
                onDismissRequest = { confirmLeave = false },
                title = { Text("Verbindung trennen?") },
                text = { Text("Der PC läuft weiter.") },
                confirmButton = { TextButton(onClick = { confirmLeave = false; disconnect() }) { Text("Trennen") } },
                dismissButton = { TextButton(onClick = { confirmLeave = false }) { Text("Abbrechen") } },
            )
        }
    }

    @Composable
    private fun StreamArea(modifier: Modifier, showKeys: Boolean, stale: Boolean) {
        Box(modifier) {
            AndroidView(
                factory = { remoteView.also { v -> (v.parent as? ViewGroup)?.removeView(v) } },
                modifier = Modifier.fillMaxSize(),
            )
            if (!hasFrame) {
                Text("Warte auf Bild …", color = Color.White, modifier = Modifier.align(Alignment.Center))
            } else if (stale) {
                Text(
                    "Kein Bild – PC gesperrt oder Anmeldebildschirm?",
                    color = Color.White,
                    fontSize = 13.sp,
                    modifier = Modifier
                        .align(Alignment.TopCenter)
                        .padding(8.dp)
                        .background(Color(0xAA000000))
                        .padding(horizontal = 10.dp, vertical = 4.dp),
                )
            }
            if (showKeys) {
                Row(
                    Modifier
                        .align(Alignment.BottomCenter)
                        .fillMaxWidth()
                        .background(Color(0xCC000000))
                        .horizontalScroll(rememberScrollState())
                        .padding(6.dp)
                ) {
                    specialKeys.forEach { (label, vk, mods) ->
                        FilledTonalButton(
                            onClick = { client?.key(vk, mods) },
                            modifier = Modifier.padding(horizontal = 3.dp),
                            contentPadding = PaddingValues(horizontal = 12.dp, vertical = 6.dp),
                        ) { Text(label) }
                    }
                }
            }
        }
    }

    @Composable
    private fun ToolBar(vertical: Boolean, onKeys: () -> Unit, onPower: () -> Unit, onClose: () -> Unit) {
        val bg = Color(0xFF12152A)
        if (vertical) {
            Column(
                Modifier.fillMaxHeight().background(bg).padding(4.dp),
                verticalArrangement = Arrangement.SpaceEvenly,
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                ToolButtons(onKeys, onPower, onClose)
            }
        } else {
            Row(
                Modifier.fillMaxWidth().background(bg).padding(4.dp),
                horizontalArrangement = Arrangement.SpaceEvenly,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                ToolButtons(onKeys, onPower, onClose)
            }
        }
    }

    @Composable
    private fun ToolButtons(onKeys: () -> Unit, onPower: () -> Unit, onClose: () -> Unit) {
        ToolButton("⌨") { remoteView.toggleKeyboard() }
        ToolButton("Fn") { onKeys() }
        ToolButton("⏻") { onPower() }
        ToolButton("✕") { onClose() }
    }

    @Composable
    private fun ToolButton(label: String, onClick: () -> Unit) {
        TextButton(onClick = onClick) { Text(label, color = Color.White, fontSize = 20.sp) }
    }
}
