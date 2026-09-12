package de.finanzinstitut.spaceremote

import androidx.compose.foundation.background
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.clickable

/** What a key does when it's pressed. */
private enum class Kind { CHAR, BACKSPACE, ENTER, TAB, ESC, DEL, CAPS, SHIFT, CTRL, ALT, WIN, SPACE, ARROW }

private data class Key(
    val kind: Kind,
    val lower: String = "",
    val upper: String = "",
    val vk: Int = 0,
    val weight: Float = 1f,
)

private fun ch(lower: String, upper: String, vk: Int = 0) =
    Key(Kind.CHAR, lower, upper, if (vk != 0) vk else lower.uppercase().firstOrNull()?.code ?: 0)

// German QWERTZ layout, trimmed to fit a phone.
private val row1 = listOf(
    ch("1", "!", 0x31), ch("2", "\"", 0x32), ch("3", "§", 0x33), ch("4", "$", 0x34), ch("5", "%", 0x35),
    ch("6", "&", 0x36), ch("7", "/", 0x37), ch("8", "(", 0x38), ch("9", ")", 0x39), ch("0", "=", 0x30),
    ch("ß", "?"), Key(Kind.BACKSPACE, "⌫", weight = 1.6f),
)
private val row2 = listOf(
    ch("q", "Q"), ch("w", "W"), ch("e", "E"), ch("r", "R"), ch("t", "T"), ch("z", "Z"),
    ch("u", "U"), ch("i", "I"), ch("o", "O"), ch("p", "P"), ch("ü", "Ü"), ch("+", "*"),
)
private val row3 = listOf(
    ch("a", "A"), ch("s", "S"), ch("d", "D"), ch("f", "F"), ch("g", "G"), ch("h", "H"),
    ch("j", "J"), ch("k", "K"), ch("l", "L"), ch("ö", "Ö"), ch("ä", "Ä"),
    Key(Kind.ENTER, "⏎", weight = 1.6f),
)
private val row4 = listOf(
    Key(Kind.SHIFT, "⇧", weight = 1.5f),
    ch("y", "Y"), ch("x", "X"), ch("c", "C"), ch("v", "V"), ch("b", "B"), ch("n", "N"), ch("m", "M"),
    ch(",", ";"), ch(".", ":"), ch("-", "_"),
    Key(Kind.CAPS, "⇪", weight = 1.3f),
)
private val row5 = listOf(
    Key(Kind.CTRL, "Ctrl", weight = 1.3f),
    Key(Kind.WIN, "Win", weight = 1.2f),
    Key(Kind.ALT, "Alt", weight = 1.1f),
    Key(Kind.TAB, "Tab", weight = 1.1f),
    Key(Kind.SPACE, "space", weight = 3.2f),
    Key(Kind.ESC, "Esc", weight = 1.1f),
    Key(Kind.ARROW, "←", vk = Vk.LEFT, weight = 0.9f),
    Key(Kind.ARROW, "↑", vk = Vk.UP, weight = 0.9f),
    Key(Kind.ARROW, "↓", vk = Vk.DOWN, weight = 0.9f),
    Key(Kind.ARROW, "→", vk = Vk.RIGHT, weight = 0.9f),
)

/**
 * On-screen QWERTZ keyboard that talks to Windows directly, so Ctrl, Alt, Win and
 * Shift behave like real keys instead of going through the Android IME.
 *
 * Ctrl/Alt/Win stay held until the next key (tap Ctrl then C = Ctrl+C). Tap one twice
 * to lock it, tap again to release.
 */
@Composable
fun WindowsKeyboard(client: RemoteClient?, modifier: Modifier = Modifier) {
    var shift by remember { mutableStateOf(false) }
    var caps by remember { mutableStateOf(false) }
    var ctrl by remember { mutableStateOf(0) }  // 0 off, 1 one-shot, 2 locked
    var alt by remember { mutableStateOf(0) }
    var win by remember { mutableStateOf(0) }
    val haptics = LocalHapticFeedback.current

    fun mods(): Int {
        var m = 0
        if (ctrl > 0) m = m or Mod.CTRL
        if (alt > 0) m = m or Mod.ALT
        if (win > 0) m = m or Mod.WIN
        if (shift) m = m or Mod.SHIFT
        return m
    }

    fun clearOneShots() {
        if (ctrl == 1) ctrl = 0
        if (alt == 1) alt = 0
        if (win == 1) win = 0
        shift = false
    }

    fun press(key: Key) {
        val c = client ?: return
        haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
        val comboActive = ctrl > 0 || alt > 0 || win > 0

        when (key.kind) {
            Kind.CHAR -> {
                if (comboActive && key.vk != 0) {
                    c.key(key.vk, mods())
                } else {
                    val upper = shift != caps // XOR: caps inverts what shift does
                    c.text(if (upper) key.upper else key.lower)
                }
                clearOneShots()
            }
            Kind.SPACE -> {
                if (comboActive) c.key(0x20, mods()) else c.text(" ")
                clearOneShots()
            }
            Kind.BACKSPACE -> { c.key(Vk.BACK, mods()); clearOneShots() }
            Kind.ENTER -> { c.key(Vk.RETURN, mods()); clearOneShots() }
            Kind.TAB -> { c.key(Vk.TAB, mods()); clearOneShots() }
            Kind.ESC -> { c.key(Vk.ESCAPE, mods()); clearOneShots() }
            Kind.DEL -> { c.key(Vk.DELETE, mods()); clearOneShots() }
            Kind.ARROW -> { c.key(key.vk, mods()); clearOneShots() }
            Kind.SHIFT -> shift = !shift
            Kind.CAPS -> caps = !caps
            Kind.CTRL -> ctrl = (ctrl + 1) % 3
            Kind.ALT -> alt = (alt + 1) % 3
            Kind.WIN -> win = (win + 1) % 3
        }
    }

    fun activeLevel(key: Key): Int = when (key.kind) {
        Kind.SHIFT -> if (shift) 2 else 0
        Kind.CAPS -> if (caps) 2 else 0
        Kind.CTRL -> ctrl
        Kind.ALT -> alt
        Kind.WIN -> win
        else -> 0
    }

    Surface(modifier.fillMaxWidth(), color = Color(0xF21A2040)) {
        Column(Modifier.padding(horizontal = 3.dp, vertical = 5.dp)) {
            listOf(row1, row2, row3, row4, row5).forEach { row ->
                Row(
                    Modifier.fillMaxWidth().padding(vertical = 2.dp),
                    horizontalArrangement = Arrangement.spacedBy(3.dp),
                ) {
                    row.forEach { key ->
                        KeyCap(
                            key = key,
                            label = when {
                                key.kind != Kind.CHAR -> key.lower
                                shift != caps -> key.upper
                                else -> key.lower
                            },
                            level = activeLevel(key),
                            onPress = { press(key) },
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun RowScope.KeyCap(key: Key, label: String, level: Int, onPress: () -> Unit) {
    val interaction = remember { MutableInteractionSource() }
    val pressed by interaction.collectIsPressedAsState()
    val scale by animateFloatAsState(if (pressed) 0.9f else 1f, tween(90), label = "keycap")

    val bg = when {
        pressed -> Color(0xFF7C9CFF)
        level == 2 -> Color(0xFF5BE0C0)          // locked
        level == 1 -> Color(0xFF3E62C4)          // armed for the next key
        key.kind == Kind.CHAR -> Color(0xFF2A3160)
        else -> Color(0xFF1F2547)
    }
    val fg = if (level == 2 || pressed) Color(0xFF070A18) else Color.White

    Box(
        Modifier
            .weight(key.weight)
            .height(46.dp)
            .scale(scale)
            .background(bg, RoundedCornerShape(8.dp))
            .clickable(interactionSource = interaction, indication = null) { onPress() },
        contentAlignment = Alignment.Center,
    ) {
        Text(
            label,
            color = fg,
            fontSize = if (label.length > 2) 12.sp else 16.sp,
            fontWeight = if (key.kind == Kind.CHAR) FontWeight.Normal else FontWeight.Medium,
            textAlign = TextAlign.Center,
        )
    }
}
