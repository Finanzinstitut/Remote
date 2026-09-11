package de.finanzinstitut.spaceremote

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.os.SystemClock
import android.text.InputType
import android.view.HapticFeedbackConstants
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.view.ViewConfiguration
import android.view.inputmethod.BaseInputConnection
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputConnection
import android.view.inputmethod.InputMethodManager
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import kotlin.math.hypot
import kotlin.math.min

/**
 * Zeigt den PC-Bildschirm und übersetzt Touch in Maus:
 *  Tippen = Linksklick, Doppeltippen = Doppelklick, lange drücken = Rechtsklick,
 *  ziehen = mit gedrückter Maustaste ziehen, zwei Finger = scrollen.
 */
class RemoteView(context: Context) : View(context) {

    @Volatile var client: RemoteClient? = null

    @Volatile var lastFrameAt = 0L
        private set

    private var bitmap: Bitmap? = null
    private val dst = RectF()
    private val paint = Paint(Paint.FILTER_BITMAP_FLAG or Paint.ANTI_ALIAS_FLAG)
    private val touchSlop = ViewConfiguration.get(context).scaledTouchSlop.toFloat()
    private val longPressMs = ViewConfiguration.getLongPressTimeout().toLong()
    private val scrollStepPx = 24f * resources.displayMetrics.density

    private var downX = 0f
    private var downY = 0f
    private var dragging = false
    private var longPressed = false
    private var multiTouch = false
    private var lastScrollY = 0f
    private var scrollAcc = 0f
    private var lastTapTime = 0L
    private var lastTapX = 0f
    private var lastTapY = 0f
    private var composing = ""

    init {
        isFocusable = true
        isFocusableInTouchMode = true
        keepScreenOn = true
        setBackgroundColor(Color.BLACK)
    }

    /** Aus dem Netzwerk-Thread aufrufbar. */
    fun setFrame(bmp: Bitmap) {
        lastFrameAt = SystemClock.elapsedRealtime()
        post {
            bitmap = bmp
            invalidate()
        }
    }

    fun clear() {
        bitmap = null
        lastFrameAt = 0L
        invalidate()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val b = bitmap ?: return
        val scale = min(width / b.width.toFloat(), height / b.height.toFloat())
        val w = b.width * scale
        val h = b.height * scale
        dst.set((width - w) / 2f, (height - h) / 2f, (width + w) / 2f, (height + h) / 2f)
        canvas.drawBitmap(b, null, dst, paint)
    }

    private fun nx(x: Float) = if (dst.width() > 0f) ((x - dst.left) / dst.width()).coerceIn(0f, 1f) else 0.5f
    private fun ny(y: Float) = if (dst.height() > 0f) ((y - dst.top) / dst.height()).coerceIn(0f, 1f) else 0.5f

    private val longPressRunnable = Runnable {
        if (!dragging && !multiTouch) {
            longPressed = true
            client?.let { c ->
                c.move(nx(downX), ny(downY))
                c.click(1)
            }
            performHapticFeedback(HapticFeedbackConstants.LONG_PRESS)
        }
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        val c = client ?: return true
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                downX = e.x
                downY = e.y
                dragging = false
                longPressed = false
                multiTouch = false
                postDelayed(longPressRunnable, longPressMs)
            }

            MotionEvent.ACTION_POINTER_DOWN -> {
                removeCallbacks(longPressRunnable)
                if (dragging) {
                    c.button(0, false)
                    dragging = false
                }
                multiTouch = true
                lastScrollY = avgY(e)
                scrollAcc = 0f
            }

            MotionEvent.ACTION_MOVE -> {
                if (multiTouch) {
                    if (e.pointerCount >= 2) {
                        val y = avgY(e)
                        scrollAcc += y - lastScrollY
                        lastScrollY = y
                        val notches = (scrollAcc / scrollStepPx).toInt()
                        if (notches != 0) {
                            c.scroll(notches * 120)
                            scrollAcc -= notches * scrollStepPx
                        }
                    }
                } else if (!longPressed) {
                    if (!dragging && hypot(e.x - downX, e.y - downY) > touchSlop) {
                        removeCallbacks(longPressRunnable)
                        dragging = true
                        c.move(nx(downX), ny(downY))
                        c.button(0, true)
                    }
                    if (dragging) c.move(nx(e.x), ny(e.y))
                }
            }

            MotionEvent.ACTION_POINTER_UP -> {
                lastScrollY = avgY(e, excludeIndex = e.actionIndex)
            }

            MotionEvent.ACTION_UP -> {
                removeCallbacks(longPressRunnable)
                if (dragging) {
                    c.move(nx(e.x), ny(e.y))
                    c.button(0, false)
                } else if (!longPressed && !multiTouch) {
                    var tx = e.x
                    var ty = e.y
                    val now = SystemClock.elapsedRealtime()
                    // Doppeltippen: gleiche Position verwenden, damit Windows einen Doppelklick erkennt
                    if (now - lastTapTime < 400 && hypot(tx - lastTapX, ty - lastTapY) < touchSlop * 2) {
                        tx = lastTapX
                        ty = lastTapY
                    }
                    lastTapTime = now
                    lastTapX = tx
                    lastTapY = ty
                    c.move(nx(tx), ny(ty))
                    c.click(0)
                }
                dragging = false
            }

            MotionEvent.ACTION_CANCEL -> {
                removeCallbacks(longPressRunnable)
                if (dragging) c.button(0, false)
                dragging = false
            }
        }
        return true
    }

    private fun avgY(e: MotionEvent, excludeIndex: Int = -1): Float {
        var sum = 0f
        var n = 0
        for (i in 0 until e.pointerCount) {
            if (i != excludeIndex) {
                sum += e.getY(i)
                n++
            }
        }
        return if (n > 0) sum / n else 0f
    }

    // ---------------- Tastatur ----------------

    fun toggleKeyboard() {
        val visible = ViewCompat.getRootWindowInsets(this)?.isVisible(WindowInsetsCompat.Type.ime()) == true
        if (visible) hideKeyboard() else showKeyboard()
    }

    fun showKeyboard() {
        requestFocus()
        val imm = context.getSystemService(InputMethodManager::class.java) ?: return
        imm.restartInput(this)
        imm.showSoftInput(this, 0)
    }

    fun hideKeyboard() {
        composing = ""
        val imm = context.getSystemService(InputMethodManager::class.java) ?: return
        imm.hideSoftInputFromWindow(windowToken, 0)
    }

    override fun onCheckIsTextEditor(): Boolean = true

    override fun onCreateInputConnection(outAttrs: EditorInfo): InputConnection {
        // "sichtbares Passwort" = keine Autokorrektur/Vorschläge -> Zeichen kommen direkt an
        outAttrs.inputType = InputType.TYPE_CLASS_TEXT or
            InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD or
            InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
        outAttrs.imeOptions = EditorInfo.IME_FLAG_NO_EXTRACT_UI or
            EditorInfo.IME_FLAG_NO_FULLSCREEN or
            EditorInfo.IME_ACTION_NONE
        composing = ""

        return object : BaseInputConnection(this, false) {
            override fun commitText(text: CharSequence?, newCursorPosition: Int): Boolean {
                applyComposing(text?.toString() ?: "")
                composing = ""
                return true
            }

            override fun setComposingText(text: CharSequence?, newCursorPosition: Int): Boolean {
                applyComposing(text?.toString() ?: "")
                return true
            }

            override fun finishComposingText(): Boolean {
                composing = ""
                return true
            }

            override fun deleteSurroundingText(beforeLength: Int, afterLength: Int): Boolean {
                repeat(beforeLength.coerceIn(0, 64)) { client?.key(Vk.BACK) }
                repeat(afterLength.coerceIn(0, 64)) { client?.key(Vk.DELETE) }
                return true
            }

            override fun sendKeyEvent(event: KeyEvent): Boolean {
                if (event.action == KeyEvent.ACTION_DOWN) handleKey(event)
                return true
            }

            override fun performEditorAction(actionCode: Int): Boolean {
                client?.key(Vk.RETURN)
                return true
            }
        }
    }

    /** Überträgt nur die Änderung gegenüber dem bisherigen "Composing"-Text. */
    private fun applyComposing(new: String) {
        val c = client ?: return
        var p = 0
        while (p < composing.length && p < new.length && composing[p] == new[p]) p++
        repeat(composing.length - p) { c.key(Vk.BACK) }
        if (new.length > p) c.text(new.substring(p))
        composing = new
    }

    private fun handleKey(event: KeyEvent): Boolean {
        val c = client ?: return false
        val vk = when (event.keyCode) {
            KeyEvent.KEYCODE_DEL -> Vk.BACK
            KeyEvent.KEYCODE_FORWARD_DEL -> Vk.DELETE
            KeyEvent.KEYCODE_ENTER, KeyEvent.KEYCODE_NUMPAD_ENTER -> Vk.RETURN
            KeyEvent.KEYCODE_TAB -> Vk.TAB
            KeyEvent.KEYCODE_ESCAPE -> Vk.ESCAPE
            KeyEvent.KEYCODE_DPAD_LEFT -> Vk.LEFT
            KeyEvent.KEYCODE_DPAD_UP -> Vk.UP
            KeyEvent.KEYCODE_DPAD_RIGHT -> Vk.RIGHT
            KeyEvent.KEYCODE_DPAD_DOWN -> Vk.DOWN
            else -> 0
        }
        if (vk != 0) {
            c.key(vk)
            return true
        }
        val ch = event.unicodeChar
        if (ch > 0 && !Character.isISOControl(ch)) {
            c.text(String(Character.toChars(ch)))
            return true
        }
        return false
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent): Boolean {
        if (keyCode == KeyEvent.KEYCODE_BACK) return super.onKeyDown(keyCode, event)
        return handleKey(event) || super.onKeyDown(keyCode, event)
    }
}
