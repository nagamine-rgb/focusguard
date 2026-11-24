package com.focusguard.app

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.graphics.drawable.Drawable
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.LayerDrawable
import android.view.View
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.sin

/** 霧の朝デザインの配色。Lv.3 ロック中は赤みのある配色に切り替わる。 */
object Fg {
    var locked = false

    val ink = Color.parseColor("#1C2620")
    val white = Color.WHITE
    val ok = Color.parseColor("#2E6B4A")

    val ground get() = if (locked) Color.parseColor("#ECE2DF") else Color.parseColor("#E4E8E1")
    val paper get() = if (locked) Color.parseColor("#F7F0EE") else Color.parseColor("#F3F5F1")
    val well get() = if (locked) Color.parseColor("#E6D9D5") else Color.parseColor("#DDE3DA")
    val sub get() = if (locked) Color.parseColor("#5A4744") else Color.parseColor("#4D5A52")
    val line get() = if (locked) Color.parseColor("#C9B4B0") else Color.parseColor("#B9C2B6")
    val track get() = if (locked) Color.parseColor("#DCCBC7") else Color.parseColor("#CDD4CA")
    val tick get() = if (locked) Color.parseColor("#B8A29E") else Color.parseColor("#9AA597")
    val sched get() = if (locked) Color.parseColor("#A8928E") else Color.parseColor("#7F8C7C")

    fun lv(level: Int): Int = when (level) {
        1 -> Color.parseColor("#D9A21B")
        2 -> Color.parseColor("#B4501E")
        else -> Color.parseColor("#8E2424")
    }

    fun lvText(level: Int): Int = when (level) {
        1 -> Color.parseColor("#6E5200")
        2 -> Color.parseColor("#8A3A14")
        else -> Color.parseColor("#8E2424")
    }

    fun lvName(level: Int): String = when (level) {
        1 -> "気づき"
        2 -> "ひと手間"
        else -> "強制"
    }

    val serif: Typeface = Typeface.create(Typeface.SERIF, Typeface.BOLD)

    fun rounded(fill: Int, radiusPx: Float, strokePx: Int = 0, stroke: Int = Color.TRANSPARENT, dashPx: Float = 0f): GradientDrawable =
        GradientDrawable().apply {
            setColor(fill)
            cornerRadius = radiusPx
            if (strokePx > 0) {
                if (dashPx > 0f) setStroke(strokePx, stroke, dashPx, dashPx) else setStroke(strokePx, stroke)
            }
        }

    /** 墨色の主ボタン。下端に強さの色の線が入る */
    fun primary(accent: Int, radiusPx: Float, accentPx: Int): LayerDrawable {
        val layer = LayerDrawable(arrayOf<Drawable>(rounded(accent, radiusPx), rounded(ink, radiusPx)))
        layer.setLayerInset(1, 0, 0, 0, accentPx)
        return layer
    }

    fun parseMin(s: String?): Int? {
        val p = (s ?: "").trim().split(":")
        if (p.size != 2) return null
        val h = p[0].toIntOrNull() ?: return null
        val m = p[1].toIntOrNull() ?: return null
        return h * 60 + m
    }
}

/** 24時間の文字盤 */
class DialView(context: Context) : View(context) {
    data class Arc(val startMin: Double, val lenMin: Double, val color: Int)

    val arcs = mutableListOf<Arc>()
    var nowMin = 0.0
    var centerLabel = ""
    var centerTime = ""
    var centerTimeSmall = true
    var centerState = ""
    var centerStateColor = Fg.ink
    var showLock = false

    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val ring = RectF()

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val w = MeasureSpec.getSize(widthMeasureSpec)
        val size = min(w, (300 * resources.displayMetrics.density).toInt())
        setMeasuredDimension(w, size)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val size = min(width, height).toFloat()
        val s = size / 300f
        canvas.save()
        canvas.translate((width - size) / 2f, 0f)
        canvas.scale(s, s)

        paint.style = Paint.Style.STROKE
        paint.strokeCap = Paint.Cap.BUTT

        // 時刻の目盛り
        paint.color = Fg.tick
        paint.strokeWidth = 6f
        ring.set(14f, 14f, 286f, 286f)
        for (h in 0 until 24) canvas.drawArc(ring, -90f + h * 15f - 0.6f, 1.2f, false, paint)

        // リング
        ring.set(30f, 30f, 270f, 270f)
        paint.strokeWidth = 18f
        paint.color = Fg.track
        canvas.drawOval(ring, paint)
        for (a in arcs) {
            if (a.lenMin <= 0) continue
            val start = (-90.0 + (a.startMin % 1440.0) / 1440.0 * 360.0).toFloat()
            val sweep = (min(a.lenMin, 1440.0) / 1440.0 * 360.0).toFloat().coerceAtLeast(0.5f)
            paint.color = a.color
            canvas.drawArc(ring, start, sweep, false, paint)
        }

        // 現在時刻
        val th = nowMin / 1440.0 * 2 * Math.PI
        val mx = (150 + 120 * sin(th)).toFloat()
        val my = (150 - 120 * cos(th)).toFloat()
        paint.style = Paint.Style.FILL
        paint.color = Fg.paper
        canvas.drawCircle(mx, my, 7f, paint)
        paint.style = Paint.Style.STROKE
        paint.strokeWidth = 2.5f
        paint.color = Fg.ink
        canvas.drawCircle(mx, my, 7f, paint)

        // 時刻の数字
        paint.style = Paint.Style.FILL
        paint.textAlign = Paint.Align.CENTER
        paint.typeface = Typeface.DEFAULT
        paint.textSize = 12f
        paint.color = Fg.sub
        canvas.drawText("0", 150f, 58f, paint)
        canvas.drawText("6", 246f, 154f, paint)
        canvas.drawText("12", 150f, 250f, paint)
        canvas.drawText("18", 54f, 154f, paint)

        // 中央
        val timeSize = if (centerTimeSmall) 28f else 52f
        val total = (if (showLock) 30f else 0f) + 22f + timeSize * 1.15f + 22f
        var y = 150f - total / 2f
        if (showLock) {
            paint.style = Paint.Style.STROKE
            paint.strokeWidth = 2.4f
            paint.color = Fg.lv(3)
            canvas.drawRect(140f, y + 11f, 160f, y + 25f, paint)
            canvas.drawArc(RectF(143.5f, y + 1f, 156.5f, y + 19f), 180f, 180f, false, paint)
            paint.style = Paint.Style.FILL
            y += 30f
        }
        paint.typeface = Typeface.DEFAULT
        paint.textSize = 13f
        paint.color = Fg.sub
        canvas.drawText(centerLabel, 150f, y + 15f, paint)
        y += 22f
        paint.typeface = Fg.serif
        paint.textSize = timeSize
        paint.color = Fg.ink
        canvas.drawText(centerTime, 150f, y + timeSize * 0.95f, paint)
        y += timeSize * 1.15f
        paint.typeface = Typeface.DEFAULT_BOLD
        paint.textSize = 13f
        paint.color = centerStateColor
        canvas.drawText(centerState, 150f, y + 16f, paint)
        canvas.restore()
    }
}

/** スケジュールの24時間バー */
class TimelineView(context: Context, private val startMin: Int?, private val endMin: Int?, private val color: Int) : View(context) {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val w = width.toFloat()
        val h = height.toFloat()
        val r = h / 2f
        paint.color = Fg.well
        canvas.drawRoundRect(RectF(0f, 0f, w, h), r, r, paint)
        val s = startMin ?: return
        val e = endMin ?: return
        paint.color = color
        fun x(m: Int) = w * m / 1440f
        when {
            s == e -> canvas.drawRoundRect(RectF(0f, 0f, w, h), r, r, paint)
            s < e -> canvas.drawRect(x(s), 0f, x(e), h, paint)
            else -> {
                canvas.drawRect(0f, 0f, x(e), h, paint)
                canvas.drawRect(x(s), 0f, w, h, paint)
            }
        }
    }
}
