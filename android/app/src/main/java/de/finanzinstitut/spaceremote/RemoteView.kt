package de.finanzinstitut.spaceremote

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.os.SystemClock
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import android.view.ViewConfiguration
import kotlin.math.abs
import kotlin.math.hypot
import kotlin.math.min

/**
 * Shows the PC screen and turns touch into mouse input.
 *
 * Trackpad mode (default): the finger moves the cursor relatively, like a laptop
 * touchpad. Tap = left click, two-finger tap = right click, two fingers = scroll.
 *
 * Direct mode: the cursor jumps to wherever you touch. Handy for precise clicking,
 * awkward for dragging.
 */
class RemoteView(context: Context) : View(context) {

    @Volatile var client: RemoteClient? = null

    /** true = touchpad-style relative movement, false = cursor jumps to the touch point. */
    var trackpad = true
        set(value) {
            field = value
            releaseButton()
        }

    /** Cursor speed in trackpad mode. */
    var sensitivity = 1.7f

    /** Scroll speed multiplier. */
    var scrollSpeed = 1.0f

    @Volatile var lastFrameAt = 0L
        private set

    private var bitmap: Bitmap? = null
    private val dst = RectF()
    private val paint = Paint(Paint.FILTER_BITMAP_FLAG or Paint.ANTI_ALIAS_FLAG)
    private val density = resources.displayMetrics.density
    private val touchSlop = ViewConfiguration.get(context).scaledTouchSlop.toFloat()
    private val longPressMs = ViewConfiguration.getLongPressTimeout().toLong()

    /** One wheel notch per this many pixels of finger travel. Small = scrolls easily. */
    private val scrollStepPx = 9f * density

    private var downX = 0f
    private var downY = 0f
    private var lastX = 0f
    private var lastY = 0f
    private var downTime = 0L
    private var moved = false
    private var dragging = false
    private var longPressed = false
    private var gestureFingers = 1
    private var scrolling = false
    private var lastScrollY = 0f
    private var lastScrollX = 0f
    private var scrollAccY = 0f
    private var scrollAccX = 0f
    private var lastTapUp = 0L
    private var lastTapX = 0f
    private var lastTapY = 0f

    init {
        isFocusable = false
        keepScreenOn = true
        setBackgroundColor(Color.BLACK)
    }

    /** Safe to call from the network thread. */
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

    private fun releaseButton() {
        if (dragging) {
            client?.button(0, false)
            dragging = false
        }
        removeCallbacks(longPressRunnable)
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

    /** Holding still starts a drag: the button goes down and stays down until you lift off. */
    private val longPressRunnable = Runnable {
        if (scrolling || dragging) return@Runnable
        longPressed = true
        val c = client ?: return@Runnable
        if (!trackpad) c.move(nx(downX), ny(downY))
        c.button(0, true)
        dragging = true
        performHapticFeedback(HapticFeedbackConstants.LONG_PRESS)
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(e: MotionEvent): Boolean {
        val c = client ?: return true

        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                downX = e.x
                downY = e.y
                lastX = e.x
                lastY = e.y
                downTime = SystemClock.elapsedRealtime()
                moved = false
                longPressed = false
                scrolling = false
                gestureFingers = 1
                postDelayed(longPressRunnable, longPressMs)
            }

            MotionEvent.ACTION_POINTER_DOWN -> {
                gestureFingers = maxOf(gestureFingers, e.pointerCount)
                removeCallbacks(longPressRunnable)
                if (dragging) {
                    c.button(0, false)
                    dragging = false
                }
                scrolling = true
                lastScrollY = avgY(e)
                lastScrollX = avgX(e)
                scrollAccY = 0f
                scrollAccX = 0f
            }

            MotionEvent.ACTION_MOVE -> {
                if (scrolling) {
                    if (e.pointerCount >= 2) {
                        val y = avgY(e)
                        val x = avgX(e)
                        scrollAccY += (y - lastScrollY) * scrollSpeed
                        scrollAccX += (x - lastScrollX) * scrollSpeed
                        lastScrollY = y
                        lastScrollX = x

                        val notchesY = (scrollAccY / scrollStepPx).toInt()
                        if (notchesY != 0) {
                            c.scroll(notchesY * 120)
                            scrollAccY -= notchesY * scrollStepPx
                        }
                        // sideways two-finger swipe scrolls horizontally (Shift+wheel)
                        val notchesX = (scrollAccX / scrollStepPx).toInt()
                        if (notchesX != 0 && abs(scrollAccX) > abs(scrollAccY)) {
                            c.hScroll(-notchesX * 120)
                            scrollAccX -= notchesX * scrollStepPx
                        }
                    }
                } else {
                    if (!moved && hypot(e.x - downX, e.y - downY) > touchSlop) {
                        moved = true
                        if (!longPressed) removeCallbacks(longPressRunnable)
                    }
                    if (moved) {
                        if (trackpad) {
                            val dx = e.x - lastX
                            val dy = e.y - lastY
                            // gentle acceleration: slow finger = precise, fast finger = across the screen
                            val speed = hypot(dx, dy) / density
                            val gain = sensitivity * (0.55f + min(speed / 22f, 1.6f))
                            if (dst.width() > 0f && dst.height() > 0f) {
                                c.moveRel(dx * gain / dst.width(), dy * gain / dst.height())
                            }
                        } else {
                            c.move(nx(e.x), ny(e.y))
                        }
                        lastX = e.x
                        lastY = e.y
                    }
                }
            }

            MotionEvent.ACTION_POINTER_UP -> {
                lastScrollY = avgY(e, excludeIndex = e.actionIndex)
                lastScrollX = avgX(e, excludeIndex = e.actionIndex)
            }

            MotionEvent.ACTION_UP -> {
                removeCallbacks(longPressRunnable)
                val heldMs = SystemClock.elapsedRealtime() - downTime

                when {
                    dragging -> {
                        c.button(0, false)
                        dragging = false
                    }

                    // two-finger tap = right click
                    scrolling && gestureFingers >= 2 && !moved && heldMs < 300 &&
                        abs(scrollAccY) < scrollStepPx && abs(scrollAccX) < scrollStepPx -> {
                        c.click(1)
                        performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY)
                    }

                    !scrolling && !moved && !longPressed && heldMs < 400 -> {
                        val now = SystemClock.elapsedRealtime()
                        val isDouble = now - lastTapUp < 380 &&
                            hypot(e.x - lastTapX, e.y - lastTapY) < touchSlop * 3
                        if (!trackpad) {
                            // in direct mode keep the exact spot so Windows sees a real double click
                            val tx = if (isDouble) lastTapX else e.x
                            val ty = if (isDouble) lastTapY else e.y
                            c.move(nx(tx), ny(ty))
                            lastTapX = tx
                            lastTapY = ty
                        } else {
                            lastTapX = e.x
                            lastTapY = e.y
                        }
                        lastTapUp = now
                        c.click(0)
                    }
                }
                scrolling = false
                gestureFingers = 1
            }

            MotionEvent.ACTION_CANCEL -> {
                removeCallbacks(longPressRunnable)
                if (dragging) c.button(0, false)
                dragging = false
                scrolling = false
            }
        }
        return true
    }

    private fun avgY(e: MotionEvent, excludeIndex: Int = -1): Float {
        var sum = 0f
        var n = 0
        for (i in 0 until e.pointerCount) if (i != excludeIndex) { sum += e.getY(i); n++ }
        return if (n > 0) sum / n else 0f
    }

    private fun avgX(e: MotionEvent, excludeIndex: Int = -1): Float {
        var sum = 0f
        var n = 0
        for (i in 0 until e.pointerCount) if (i != excludeIndex) { sum += e.getX(i); n++ }
        return if (n > 0) sum / n else 0f
    }
}
