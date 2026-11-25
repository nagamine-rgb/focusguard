package com.focusguard.app

import java.util.Calendar

/** 手動ブロックの記録（d=端末, s=開始, e=終了予定または止めた時刻, 0=手動で止めるまで） */
data class HistoryEntry(var d: String, var s: Long, var e: Long)

/** 曜日つきの時間帯（スケジュールの記録と、累計に数えない時間帯の両方で使う） */
data class StatsRule(
    var id: String = "",
    var name: String = "",
    var days: List<Int> = listOf(0, 1, 2, 3, 4, 5, 6),
    var start: String = "00:00",
    var end: String = "00:00"
)

/** スケジュールを変えた時刻 t と、その時点の有効なスケジュール */
data class ScheduleSnapshot(var t: Long, var rules: List<StatsRule>)

/**
 * ブロックの累計時間。「予定どおりの時間」を PC とスマホで共通の1つの時間として数える
 * （同じ時間に両方止めていても1回だけ）。睡眠や仕事など、数えない時間帯は差し引く。
 * Web リモコン（index.html）と PC（BlockStats.cs）と同じ計算。
 */
object BlockStats {
    private const val DAY_MS = 24L * 60 * 60 * 1000
    private const val HISTORY_KEEP_MS = 70 * DAY_MS
    private val devices = listOf("pc", "android")

    fun parseMin(s: String?): Int? = Fg.parseMin(s)

    fun rulesOf(st: GuardState?): List<StatsRule> =
        (st?.schedules ?: emptyList())
            .filter { it.enabled && parseMin(it.startTime) != null && parseMin(it.endTime) != null }
            .map { StatsRule(days = it.days.filter { d -> d in 0..6 }.sorted(), start = it.startTime, end = it.endTime) }
            .sortedBy { ruleKey(it) }

    private fun ruleKey(r: StatsRule) = r.days.joinToString(",") + " " + r.start + "-" + r.end
    private fun key(rules: List<StatsRule>) = rules.joinToString("|") { ruleKey(it) }
    private fun startOf(s: ManualSession, now: Long) = if (s.startedAt > 0L) s.startedAt else now

    /** 保存の直前に呼ぶ: cur → next の変化を履歴に書く（何度呼んでも同じ結果になる） */
    fun recordHistory(cur: GuardState?, next: GuardState, now: Long) {
        // 履歴は前の状態と合わせる（古い版が履歴なしで保存してきても消えないように）
        val hist = next.blockHistory.map { it.copy() }.toMutableList()
        for (h in cur?.blockHistory ?: emptyList()) {
            if (hist.none { it.d == h.d && it.s == h.s }) hist.add(h.copy())
        }
        if (next.scheduleSnapshots.isEmpty() && cur != null) next.scheduleSnapshots = cur.scheduleSnapshots.toMutableList()

        for (dev in devices) {
            val a = cur?.sessionFor(dev)
            val b = next.sessionFor(dev)
            val aLive = a != null && RuleEngine.isLive(a, now)
            val bLive = RuleEngine.isLive(b, now)
            if (aLive && !(bLive && startOf(b, now) == startOf(a!!, now) && b.expiresAt == a.expiresAt)) {
                val rec = hist.firstOrNull { it.d == dev && it.s == startOf(a!!, now) }
                if (rec != null && (rec.e == 0L || rec.e > now)) {
                    rec.e = if (bLive && startOf(b, now) == startOf(a!!, now)) b.expiresAt else now
                }
            }
            if (bLive) {
                val s = startOf(b, now)
                val rec = hist.firstOrNull { it.d == dev && it.s == s }
                if (rec == null) hist.add(HistoryEntry(dev, s, b.expiresAt))
                else if (!(aLive && startOf(a!!, now) == s && a.expiresAt == b.expiresAt)) rec.e = b.expiresAt
            }
        }

        val prevRules = rulesOf(cur)
        val nextRules = rulesOf(next)
        val snaps = next.scheduleSnapshots.toMutableList()
        if (key(prevRules) != key(nextRules)) {
            // 記録を始める前のスケジュールは、今月の月初から続いていたものとして扱う
            if (snaps.isEmpty()) snaps.add(ScheduleSnapshot(monthRange(0, now).first, prevRules))
            if (key(snaps.last().rules) != key(nextRules)) snaps.add(ScheduleSnapshot(now, nextRules))
        }

        // 古い記録を捨てる（先月の分が計算できるよう約70日は残す）
        val cutoff = now - HISTORY_KEEP_MS
        next.blockHistory = hist.filter { it.e == 0L || it.e > cutoff }.toMutableList()
        while (snaps.size > 1 && snaps[1].t <= cutoff) snaps.removeAt(0)
        next.scheduleSnapshots = snaps
    }

    /** ルール（曜日・始まり・終わり）が当たる時間帯を [from, to) の範囲で並べる */
    fun occurrences(rules: List<StatsRule>, from: Long, to: Long): List<LongArray> {
        val out = mutableListOf<LongArray>()
        if (to <= from) return out
        val d = Calendar.getInstance().apply {
            timeInMillis = from
            set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0)
            add(Calendar.DAY_OF_MONTH, -1)
        }
        while (d.timeInMillis < to) {
            val day = d.timeInMillis
            val dow = d.get(Calendar.DAY_OF_WEEK) - 1
            val nextDay = (d.clone() as Calendar).apply { add(Calendar.DAY_OF_MONTH, 1) }.timeInMillis
            for (r in rules) {
                if (dow !in r.days) continue
                val s = parseMin(r.start) ?: continue
                val e = parseMin(r.end) ?: continue
                var a: Long
                var b: Long
                when {
                    s == e -> { a = day; b = nextDay }
                    s < e -> { a = day + s * 60_000L; b = day + e * 60_000L }
                    else -> { a = day + s * 60_000L; b = nextDay + e * 60_000L }
                }
                a = maxOf(a, from)
                b = minOf(b, to)
                if (b > a) out.add(longArrayOf(a, b))
            }
            d.add(Calendar.DAY_OF_MONTH, 1)
        }
        return out
    }

    fun merge(iv: List<LongArray>): List<LongArray> {
        val out = mutableListOf<LongArray>()
        for (x in iv.sortedBy { it[0] }) {
            if (out.isNotEmpty() && x[0] <= out.last()[1]) out.last()[1] = maxOf(out.last()[1], x[1])
            else out.add(longArrayOf(x[0], x[1]))
        }
        return out
    }

    /** [from, to) のブロック時間（ミリ秒）。数えない時間帯は差し引く */
    fun blockedMs(st: GuardState, from: Long, to: Long, now: Long): Long {
        val end = minOf(to, now)
        if (end <= from) return 0L
        val iv = mutableListOf<LongArray>()
        for (h in st.blockHistory) {
            val a = maxOf(h.s, from)
            val b = if (h.e == 0L) end else minOf(h.e, end)
            if (b > a) iv.add(longArrayOf(a, b))
        }
        // 記録前から続いている手動ブロックも数える
        for (dev in devices) {
            val ms = st.sessionFor(dev)
            if (RuleEngine.isLive(ms, now) && st.blockHistory.none { it.d == dev && it.s == ms.startedAt }) {
                val a = maxOf(if (ms.startedAt > 0L) ms.startedAt else now, from)
                val b = if (ms.expiresAt > 0L) minOf(ms.expiresAt, end) else end
                if (b > a) iv.add(longArrayOf(a, b))
            }
        }
        // 記録がまだないときは、今のスケジュールが今月の月初から続いていたものとして扱う
        val snaps: List<ScheduleSnapshot> = if (st.scheduleSnapshots.isEmpty())
            listOf(ScheduleSnapshot(monthRange(0, now).first, rulesOf(st))) else st.scheduleSnapshots
        for (i in snaps.indices) {
            val a = maxOf(snaps[i].t, from)
            val b = minOf(if (i + 1 < snaps.size) snaps[i + 1].t else end, end)
            iv.addAll(occurrences(snaps[i].rules, a, b))
        }
        val blocked = merge(iv)
        val ex = merge(occurrences(st.statsExclusions, from, end))
        var total = 0L
        for (x in blocked) {
            total += x[1] - x[0]
            for (y in ex) {
                val o = minOf(x[1], y[1]) - maxOf(x[0], y[0])
                if (o > 0) total -= o
            }
        }
        return total
    }

    fun monthRange(offset: Int, now: Long): Pair<Long, Long> {
        val c = Calendar.getInstance().apply {
            timeInMillis = now
            set(Calendar.DAY_OF_MONTH, 1)
            set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0)
            add(Calendar.MONTH, offset)
        }
        val start = c.timeInMillis
        c.add(Calendar.MONTH, 1)
        return Pair(start, c.timeInMillis)
    }

    fun monthMs(st: GuardState, offset: Int, now: Long): Long {
        val (a, b) = monthRange(offset, now)
        return blockedMs(st, a, b, now)
    }

    fun formatDuration(ms: Long): String {
        val min = ms / 60_000L
        val h = min / 60
        val m = min % 60
        return if (h > 0) "${h}時間${m}分" else "${m}分"
    }

    fun describeDays(days: List<Int>): String {
        val dn = arrayOf("日", "月", "火", "水", "木", "金", "土")
        val d = days.filter { it in 0..6 }.sorted()
        val j = d.joinToString(",")
        return when {
            d.size == 7 -> "毎日"
            j == "1,2,3,4,5" -> "平日"
            j == "0,6" -> "土日"
            else -> d.joinToString("・") { dn[it] }
        }
    }
}
