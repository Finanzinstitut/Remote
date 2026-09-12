package de.finanzinstitut.spaceremote

import android.os.Bundle
import android.os.SystemClock
import android.view.ViewGroup
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.FastOutSlowInEasing
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
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
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.draw.rotate
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
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

private val Ink = Color(0xFF070A18)
private val Deep = Color(0xFF141A3C)
private val Accent = Color(0xFF7C9CFF)
private val Mint = Color(0xFF5BE0C0)
private val Muted = Color(0xFF98A2C8)
private val Danger = Color(0xFFFF8A80)

private val specialKeys = listOf(
    Triple("Esc", Vk.ESCAPE, 0),
    Triple("Tab", Vk.TAB, 0),
    Triple("Win", 0x5B, 0),
    Triple("Alt+Tab", Vk.TAB, Mod.ALT),
    Triple("Alt+F4", 0x73, Mod.ALT),
    Triple("Win+D", 0x44, Mod.WIN),
    Triple("Ctrl+C", 0x43, Mod.CTRL),
    Triple("Ctrl+V", 0x56, Mod.CTRL),
    Triple("Ctrl+Z", 0x5A, Mod.CTRL),
    Triple("Del", Vk.DELETE, 0),
    Triple("Task Manager", Vk.ESCAPE, Mod.CTRL or Mod.SHIFT),
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
                    primary = Accent,
                    onPrimary = Ink,
                    secondary = Mint,
                    background = Ink,
                    surface = Color(0xFF171D3A),
                )
            ) {
                Surface(Modifier.fillMaxSize(), color = Ink) { Root() }
            }
        }
    }

    override fun onDestroy() {
        client?.close()
        super.onDestroy()
    }

    // ------------------------------------------------------------ logic

    /** Try to connect first; if that fails, wake the PC and keep retrying until it answers. */
    private fun startConnect() {
        message = null
        status = "Connecting to ${prefs.host}…"
        screen = Screen.CONNECTING
        connectJob?.cancel()

        connectJob = lifecycleScope.launch {
            val host = prefs.host
            val port = prefs.port
            val password = prefs.password
            val mac = prefs.mac
            val broadcast = prefs.broadcast
            val wakeUrl = prefs.wakeUrl
            val local = WakeOnLan.isLocalNetwork(host)
            // A Wake-on-LAN broadcast never reaches the home network from outside.
            val canWol = mac.isNotBlank() && local
            val canWakeUrl = wakeUrl.isNotBlank()
            val canWake = canWol || canWakeUrl
            val limitMs = if (canWake) 300_000L else if (local) 20_000L else 45_000L
            val start = SystemClock.elapsedRealtime()
            var lastWake = 0L
            var urlCalled = false
            var first = true

            while (isActive) {
                if (SystemClock.elapsedRealtime() - start > limitMs) {
                    disconnect(
                        when {
                            canWake -> "The PC never answered. Check that it actually powers on and that Space Remote starts automatically."
                            local -> "PC unreachable. Add the MAC address in settings so the app can wake it."
                            else -> "PC unreachable. Is Tailscale running on both devices? Powering on from outside needs a wake URL."
                        }
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
                    val timeout = if (local) (if (first) 1500 else 2500) else (if (first) 4000 else 6000)
                    withContext(Dispatchers.IO) { c.connect(host, port, password, timeout) }
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
                    disconnect("Wrong password. The Windows app shows the correct one under “Show connection details”.")
                    return@launch
                } catch (e: Exception) {
                    c.close()
                    first = false

                    if (canWakeUrl && !urlCalled) {
                        status = "Powering on the PC…"
                        urlCalled = true
                        try {
                            withContext(Dispatchers.IO) { WakeUrl.call(wakeUrl) }
                            lastWake = SystemClock.elapsedRealtime()
                        } catch (w: Exception) {
                            disconnect("Wake URL failed: ${w.message}")
                            return@launch
                        }
                    }

                    if (canWol && SystemClock.elapsedRealtime() - lastWake > 20_000) {
                        status = "PC is off — sending Wake-on-LAN…"
                        try {
                            withContext(Dispatchers.IO) { WakeOnLan.send(mac, host, broadcast) }
                            lastWake = SystemClock.elapsedRealtime()
                        } catch (w: Exception) {
                            disconnect("Wake-on-LAN failed: ${w.message}")
                            return@launch
                        }
                    }

                    val secs = (SystemClock.elapsedRealtime() - start) / 1000
                    status = if (lastWake > 0) "PC is booting…\nWaiting for Windows and Space Remote (${secs}s)"
                    else "Connecting… (${secs}s)"
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
        applyImmersive(false)
        message = msg
        screen = if (prefs.isConfigured) Screen.HOME else Screen.SETTINGS
    }

    private fun onClientClosed(c: RemoteClient, err: String?) {
        if (client !== c) return
        disconnect(if (err != null) "Disconnected: $err" else "Disconnected.")
    }

    private fun powerAction(action: Int) {
        client?.power(action)
        if (action in 0..2) {
            val text = when (action) {
                0 -> "Shutting the PC down."
                1 -> "PC is restarting. Tap “Start PC” again in a moment."
                else -> "PC is asleep."
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
        AnimatedContent(
            targetState = screen,
            transitionSpec = {
                (fadeIn(tween(280)) + slideInVertically(tween(320)) { it / 14 })
                    .togetherWith(fadeOut(tween(180)) + slideOutVertically(tween(220)) { -it / 20 })
            },
            label = "screen",
        ) { target ->
            when (target) {
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
    }

    /** Slowly drifting background glow, shared by the calm screens. */
    @Composable
    private fun AuroraBackground(content: @Composable () -> Unit) {
        val drift = rememberInfiniteTransition(label = "aurora")
        val shift by drift.animateFloat(
            initialValue = 0f,
            targetValue = 1f,
            animationSpec = infiniteRepeatable(tween(9000, easing = FastOutSlowInEasing), RepeatMode.Reverse),
            label = "shift",
        )
        Box(
            Modifier
                .fillMaxSize()
                .background(Brush.verticalGradient(listOf(Deep, Ink)))
                .drawBehind {
                    val cx = size.width * (0.25f + 0.5f * shift)
                    val cy = size.height * (0.18f + 0.12f * shift)
                    drawCircle(
                        brush = Brush.radialGradient(
                            colors = listOf(Accent.copy(alpha = 0.30f), Color.Transparent),
                            center = Offset(cx, cy),
                            radius = size.minDimension * 0.85f,
                        ),
                        radius = size.minDimension * 0.85f,
                        center = Offset(cx, cy),
                    )
                    val mx = size.width * (0.85f - 0.45f * shift)
                    val my = size.height * (0.82f - 0.1f * shift)
                    drawCircle(
                        brush = Brush.radialGradient(
                            colors = listOf(Mint.copy(alpha = 0.16f), Color.Transparent),
                            center = Offset(mx, my),
                            radius = size.minDimension * 0.7f,
                        ),
                        radius = size.minDimension * 0.7f,
                        center = Offset(mx, my),
                    )
                },
            content = { content() },
        )
    }

    @Composable
    private fun HomeScreen() {
        AuroraBackground {
            Column(
                Modifier
                    .fillMaxSize()
                    .safeDrawingPadding()
                    .padding(24.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center,
            ) {
                Text("Space Remote", fontSize = 32.sp, fontWeight = FontWeight.Bold, color = Color.White)
                Spacer(Modifier.height(4.dp))
                Text("${prefs.host}:${prefs.port}", color = Muted, fontSize = 14.sp)

                Spacer(Modifier.height(52.dp))
                PowerButton()
                Spacer(Modifier.height(28.dp))

                Text(
                    when {
                        prefs.wakeUrl.isNotBlank() -> "Switches the outlet on, then shows your screen once Windows is ready."
                        !WakeOnLan.isLocalNetwork(prefs.host) -> "Connecting over Tailscale. The PC needs to be running already."
                        prefs.mac.isBlank() -> "Without a MAC address the app only connects, it can't wake the PC."
                        else -> "Wakes your PC and shows its screen once Windows is ready."
                    },
                    color = Muted,
                    fontSize = 13.sp,
                    textAlign = TextAlign.Center,
                )

                AnimatedVisibility(
                    visible = message != null,
                    enter = fadeIn(tween(250)) + slideInVertically(tween(280)) { it / 3 },
                    exit = fadeOut(tween(150)),
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Spacer(Modifier.height(20.dp))
                        Box(
                            Modifier
                                .background(Color(0x33FF5252), RoundedCornerShape(14.dp))
                                .padding(horizontal = 16.dp, vertical = 12.dp)
                        ) {
                            Text(message ?: "", color = Danger, textAlign = TextAlign.Center, fontSize = 14.sp)
                        }
                    }
                }

                Spacer(Modifier.height(36.dp))
                TextButton(onClick = { message = null; screen = Screen.SETTINGS }) {
                    Text("Settings", color = Muted)
                }
            }
        }
    }

    /** Big round button with a breathing halo and a press-down response. */
    @Composable
    private fun PowerButton() {
        val breathe = rememberInfiniteTransition(label = "breathe")
        val pulse by breathe.animateFloat(
            initialValue = 0f,
            targetValue = 1f,
            animationSpec = infiniteRepeatable(tween(2600, easing = LinearEasing), RepeatMode.Restart),
            label = "pulse",
        )
        val interaction = remember { MutableInteractionSource() }
        val pressed by interaction.collectIsPressedAsState()
        val scale by animateFloatAsState(if (pressed) 0.94f else 1f, tween(140), label = "press")

        Box(contentAlignment = Alignment.Center) {
            Box(
                Modifier
                    .size(260.dp)
                    .drawBehind {
                        // two rings expanding outwards, offset in time
                        for (i in 0..1) {
                            val p = (pulse + i * 0.5f) % 1f
                            val radius = size.minDimension * (0.34f + 0.16f * p)
                            drawCircle(
                                color = Accent.copy(alpha = 0.35f * (1f - p)),
                                radius = radius,
                                style = Stroke(width = 2.dp.toPx()),
                            )
                        }
                        drawRect(
                            brush = Brush.radialGradient(
                                colors = listOf(Accent.copy(alpha = 0.22f), Color.Transparent),
                                center = Offset(size.width / 2f, size.height / 2f),
                                radius = size.minDimension * 0.5f,
                            ),
                            size = Size(size.width, size.height),
                        )
                    }
            )
            Button(
                onClick = { startConnect() },
                shape = CircleShape,
                interactionSource = interaction,
                elevation = ButtonDefaults.buttonElevation(defaultElevation = 0.dp, pressedElevation = 0.dp),
                modifier = Modifier
                    .size(196.dp)
                    .scale(scale),
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("⏻", fontSize = 54.sp)
                    Spacer(Modifier.height(2.dp))
                    Text("Start PC", fontSize = 19.sp, fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }

    @Composable
    private fun SettingsScreen() {
        var host by remember { mutableStateOf(prefs.host) }
        var port by remember { mutableStateOf(prefs.port.toString()) }
        var password by remember { mutableStateOf(prefs.password) }
        var mac by remember { mutableStateOf(prefs.mac) }
        var broadcast by remember { mutableStateOf(prefs.broadcast) }
        var wakeUrl by remember { mutableStateOf(prefs.wakeUrl) }
        var error by remember { mutableStateOf<String?>(null) }
        var showAdvanced by remember { mutableStateOf(prefs.mac.isNotBlank() || prefs.wakeUrl.isNotBlank()) }

        AuroraBackground {
            Column(
                Modifier
                    .fillMaxSize()
                    .safeDrawingPadding()
                    .imePadding()
                    .verticalScroll(rememberScrollState())
                    .padding(24.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Text("Settings", fontSize = 28.sp, fontWeight = FontWeight.Bold, color = Color.White)
                Text(
                    "The Windows app shows every value: right-click the Space Remote icon in the taskbar tray → “Show connection details”.",
                    color = Muted,
                    fontSize = 14.sp,
                )

                Field("PC address", host, { host = it }, "192.168.178.20 or 100.x.x.x", KeyboardType.Uri)
                Field("Port", port, { port = it.filter(Char::isDigit) }, "47800", KeyboardType.Number)
                Field("Password", password, { password = it }, "from the Windows app", KeyboardType.Ascii)

                TextButton(onClick = { showAdvanced = !showAdvanced }) {
                    Text(if (showAdvanced) "Hide wake options" else "Wake options", color = Accent)
                }

                AnimatedVisibility(
                    visible = showAdvanced,
                    enter = fadeIn(tween(200)) + slideInVertically(tween(260)) { -it / 6 },
                    exit = fadeOut(tween(140)) + slideOutVertically(tween(200)) { -it / 6 },
                ) {
                    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                        Field("MAC address (Wake-on-LAN)", mac, { mac = it }, "AA:BB:CC:DD:EE:FF", KeyboardType.Ascii)
                        Field("Broadcast address (optional)", broadcast, { broadcast = it }, "empty = automatic", KeyboardType.Uri)
                        Field("Wake URL (optional)", wakeUrl, { wakeUrl = it }, "http://…/relay/0?turn=on", KeyboardType.Uri)
                        Text(
                            "Away from home: enter your PC's Tailscale address above. Wake-on-LAN no longer reaches it from " +
                                "outside, so powering on needs a smart plug whose switch URL you put in the wake URL field.",
                            color = Muted,
                            fontSize = 13.sp,
                        )
                    }
                }

                AnimatedVisibility(visible = error != null, enter = fadeIn(tween(200)), exit = fadeOut(tween(120))) {
                    Text(error ?: "", color = Danger)
                }

                Button(
                    onClick = {
                        val p = port.toIntOrNull()
                        error = when {
                            host.isBlank() -> "Enter the address of your PC."
                            p == null || p !in 1..65535 -> "The port has to be between 1 and 65535."
                            password.isBlank() -> "Enter the password from the Windows app."
                            mac.isNotBlank() && WakeOnLan.parseMac(mac) == null ->
                                "A MAC address needs 12 hex characters, for example AA:BB:CC:DD:EE:FF."
                            wakeUrl.isNotBlank() && !wakeUrl.startsWith("http://") && !wakeUrl.startsWith("https://") ->
                                "The wake URL has to start with http:// or https://."
                            else -> null
                        }
                        if (error == null) {
                            prefs.host = host
                            prefs.port = p!!
                            prefs.password = password
                            prefs.mac = mac
                            prefs.broadcast = broadcast
                            prefs.wakeUrl = wakeUrl
                            screen = Screen.HOME
                        }
                    },
                    shape = RoundedCornerShape(16.dp),
                    modifier = Modifier.fillMaxWidth(),
                ) { Text("Save", fontSize = 16.sp) }

                if (prefs.isConfigured) {
                    TextButton(onClick = { screen = Screen.HOME }, modifier = Modifier.fillMaxWidth()) {
                        Text("Cancel", color = Muted)
                    }
                }
            }
        }
    }

    @Composable
    private fun Field(label: String, value: String, onChange: (String) -> Unit, hint: String, type: KeyboardType) {
        OutlinedTextField(
            value = value,
            onValueChange = onChange,
            label = { Text(label) },
            placeholder = { Text(hint, color = Muted) },
            singleLine = true,
            shape = RoundedCornerShape(14.dp),
            keyboardOptions = KeyboardOptions(keyboardType = type),
            modifier = Modifier.fillMaxWidth(),
        )
    }

    @Composable
    private fun ConnectingScreen() {
        AuroraBackground {
            Column(
                Modifier
                    .fillMaxSize()
                    .safeDrawingPadding()
                    .padding(24.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center,
            ) {
                Orbit()
                Spacer(Modifier.height(36.dp))
                AnimatedContent(
                    targetState = status,
                    transitionSpec = { fadeIn(tween(220)).togetherWith(fadeOut(tween(160))) },
                    label = "status",
                ) { s ->
                    Text(s, fontSize = 16.sp, color = Color.White, textAlign = TextAlign.Center)
                }
                Spacer(Modifier.height(40.dp))
                OutlinedButton(onClick = { disconnect() }, shape = RoundedCornerShape(14.dp)) {
                    Text("Cancel")
                }
            }
        }
    }

    /** Two arcs spinning at different speeds — calmer than a plain spinner. */
    @Composable
    private fun Orbit() {
        val spin = rememberInfiniteTransition(label = "orbit")
        val outer by spin.animateFloat(
            initialValue = 0f,
            targetValue = 360f,
            animationSpec = infiniteRepeatable(tween(2200, easing = LinearEasing), RepeatMode.Restart),
            label = "outer",
        )
        val inner by spin.animateFloat(
            initialValue = 360f,
            targetValue = 0f,
            animationSpec = infiniteRepeatable(tween(3400, easing = LinearEasing), RepeatMode.Restart),
            label = "inner",
        )
        Box(Modifier.size(96.dp), contentAlignment = Alignment.Center) {
            Box(
                Modifier
                    .size(96.dp)
                    .rotate(outer)
                    .drawBehind {
                        drawArc(
                            color = Accent,
                            startAngle = 0f,
                            sweepAngle = 110f,
                            useCenter = false,
                            style = Stroke(width = 4.dp.toPx()),
                        )
                    }
            )
            Box(
                Modifier
                    .size(64.dp)
                    .rotate(inner)
                    .drawBehind {
                        drawArc(
                            color = Mint,
                            startAngle = 40f,
                            sweepAngle = 80f,
                            useCenter = false,
                            style = Stroke(width = 3.dp.toPx()),
                        )
                    }
            )
        }
    }

    @Composable
    private fun RemoteScreen() {
        var showKeyboard by remember { mutableStateOf(false) }
        var showKeys by remember { mutableStateOf(false) }
        var showPower by remember { mutableStateOf(false) }
        var confirmLeave by remember { mutableStateOf(false) }
        var trackpad by remember { mutableStateOf(remoteView.trackpad) }
        var modeHint by remember { mutableStateOf<String?>(null) }
        var stale by remember { mutableStateOf(false) }

        LaunchedEffect(Unit) {
            while (true) {
                delay(1000)
                stale = hasFrame && SystemClock.elapsedRealtime() - remoteView.lastFrameAt > 4000
            }
        }
        LaunchedEffect(modeHint) {
            if (modeHint != null) {
                delay(1800)
                modeHint = null
            }
        }
        BackHandler {
            when {
                showKeyboard -> showKeyboard = false
                showKeys -> showKeys = false
                else -> confirmLeave = true
            }
        }

        val toggleTrackpad = {
            trackpad = !trackpad
            remoteView.trackpad = trackpad
            modeHint = if (trackpad) "Touchpad mode" else "Direct touch mode"
        }

        BoxWithConstraints(
            Modifier
                .fillMaxSize()
                .background(Color.Black)
                .displayCutoutPadding()
        ) {
            val landscape = maxWidth > maxHeight
            Column(Modifier.fillMaxSize()) {
                Box(Modifier.weight(1f).fillMaxWidth()) {
                    if (landscape) {
                        Row(Modifier.fillMaxSize()) {
                            StreamArea(Modifier.weight(1f).fillMaxHeight(), showKeys, stale, modeHint)
                            ToolBar(true, trackpad, { showKeyboard = !showKeyboard }, toggleTrackpad,
                                { showKeys = !showKeys }, { showPower = true }, { confirmLeave = true })
                        }
                    } else {
                        Column(Modifier.fillMaxSize()) {
                            StreamArea(Modifier.weight(1f).fillMaxWidth(), showKeys, stale, modeHint)
                            ToolBar(false, trackpad, { showKeyboard = !showKeyboard }, toggleTrackpad,
                                { showKeys = !showKeys }, { showPower = true }, { confirmLeave = true })
                        }
                    }
                }

                AnimatedVisibility(
                    visible = showKeyboard,
                    enter = fadeIn(tween(160)) + slideInVertically(tween(240)) { it },
                    exit = fadeOut(tween(120)) + slideOutVertically(tween(200)) { it },
                ) {
                    WindowsKeyboard(client)
                }
            }
        }

        if (showPower) {
            AlertDialog(
                onDismissRequest = { showPower = false },
                shape = RoundedCornerShape(20.dp),
                title = { Text("PC power") },
                text = {
                    Column {
                        listOf("Shut down" to 0, "Restart" to 1, "Sleep" to 2, "Lock" to 3).forEach { (label, action) ->
                            TextButton(
                                onClick = { showPower = false; powerAction(action) },
                                modifier = Modifier.fillMaxWidth(),
                            ) { Text(label) }
                        }
                    }
                },
                confirmButton = { TextButton(onClick = { showPower = false }) { Text("Cancel") } },
            )
        }

        if (confirmLeave) {
            AlertDialog(
                onDismissRequest = { confirmLeave = false },
                shape = RoundedCornerShape(20.dp),
                title = { Text("Disconnect?") },
                text = { Text("Your PC keeps running.") },
                confirmButton = { TextButton(onClick = { confirmLeave = false; disconnect() }) { Text("Disconnect") } },
                dismissButton = { TextButton(onClick = { confirmLeave = false }) { Text("Cancel") } },
            )
        }
    }

    @Composable
    private fun StreamArea(modifier: Modifier, showKeys: Boolean, stale: Boolean, modeHint: String?) {
        Box(modifier) {
            // the first frame fades in instead of snapping into place
            val alpha by animateFloatAsState(if (hasFrame) 1f else 0f, tween(420), label = "frame")
            AndroidView(
                factory = { remoteView.also { v -> (v.parent as? ViewGroup)?.removeView(v) } },
                modifier = Modifier
                    .fillMaxSize()
                    .scale(0.985f + 0.015f * alpha),
            )

            AnimatedVisibility(
                visible = !hasFrame,
                enter = fadeIn(tween(200)),
                exit = fadeOut(tween(300)),
                modifier = Modifier.align(Alignment.Center),
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Orbit()
                    Spacer(Modifier.height(16.dp))
                    Text("Waiting for the first frame…", color = Muted, fontSize = 14.sp)
                }
            }

            AnimatedVisibility(
                visible = modeHint != null,
                enter = fadeIn(tween(150)),
                exit = fadeOut(tween(400)),
                modifier = Modifier.align(Alignment.Center),
            ) {
                Text(
                    modeHint ?: "",
                    color = Color.White,
                    fontSize = 15.sp,
                    modifier = Modifier
                        .background(Color(0xCC10142C), RoundedCornerShape(12.dp))
                        .padding(horizontal = 18.dp, vertical = 10.dp),
                )
            }

            AnimatedVisibility(
                visible = stale,
                enter = fadeIn(tween(250)) + slideInVertically(tween(280)) { -it },
                exit = fadeOut(tween(180)) + slideOutVertically(tween(220)) { -it },
                modifier = Modifier.align(Alignment.TopCenter),
            ) {
                Text(
                    "No image — PC locked or on the sign-in screen?",
                    color = Color.White,
                    fontSize = 13.sp,
                    modifier = Modifier
                        .padding(8.dp)
                        .background(Color(0xCC000000), RoundedCornerShape(10.dp))
                        .padding(horizontal = 12.dp, vertical = 6.dp),
                )
            }

            AnimatedVisibility(
                visible = showKeys,
                enter = fadeIn(tween(180)) + slideInVertically(tween(240)) { it },
                exit = fadeOut(tween(140)) + slideOutVertically(tween(200)) { it },
                modifier = Modifier.align(Alignment.BottomCenter),
            ) {
                Row(
                    Modifier
                        .fillMaxWidth()
                        .background(Color(0xE6060A18))
                        .horizontalScroll(rememberScrollState())
                        .padding(6.dp)
                ) {
                    specialKeys.forEach { (label, vk, mods) ->
                        FilledTonalButton(
                            onClick = { client?.key(vk, mods) },
                            shape = RoundedCornerShape(12.dp),
                            modifier = Modifier.padding(horizontal = 3.dp),
                            contentPadding = PaddingValues(horizontal = 12.dp, vertical = 6.dp),
                        ) { Text(label) }
                    }
                }
            }
        }
    }

    @Composable
    private fun ToolBar(
        vertical: Boolean,
        trackpad: Boolean,
        onKeyboard: () -> Unit,
        onMode: () -> Unit,
        onKeys: () -> Unit,
        onPower: () -> Unit,
        onClose: () -> Unit,
    ) {
        val bg = Color(0xFF10142C)
        if (vertical) {
            Column(
                Modifier.fillMaxHeight().background(bg).padding(4.dp),
                verticalArrangement = Arrangement.SpaceEvenly,
                horizontalAlignment = Alignment.CenterHorizontally,
            ) { ToolButtons(trackpad, onKeyboard, onMode, onKeys, onPower, onClose) }
        } else {
            Row(
                Modifier.fillMaxWidth().background(bg).padding(4.dp),
                horizontalArrangement = Arrangement.SpaceEvenly,
                verticalAlignment = Alignment.CenterVertically,
            ) { ToolButtons(trackpad, onKeyboard, onMode, onKeys, onPower, onClose) }
        }
    }

    @Composable
    private fun ToolButtons(
        trackpad: Boolean,
        onKeyboard: () -> Unit,
        onMode: () -> Unit,
        onKeys: () -> Unit,
        onPower: () -> Unit,
        onClose: () -> Unit,
    ) {
        ToolButton("⌨") { onKeyboard() }
        ToolButton(if (trackpad) "◍" else "✛") { onMode() }
        ToolButton("Fn") { onKeys() }
        ToolButton("⏻") { onPower() }
        ToolButton("✕") { onClose() }
    }

    @Composable
    private fun ToolButton(label: String, onClick: () -> Unit) {
        val interaction = remember { MutableInteractionSource() }
        val pressed by interaction.collectIsPressedAsState()
        val scale by animateFloatAsState(if (pressed) 0.86f else 1f, tween(110), label = "tool")
        TextButton(onClick = onClick, interactionSource = interaction, modifier = Modifier.scale(scale)) {
            Text(label, color = Color.White, fontSize = 20.sp)
        }
    }
}
