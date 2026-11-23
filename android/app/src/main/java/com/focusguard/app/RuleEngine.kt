package com.focusguard.app

import java.util.Calendar
import kotlin.math.ceil

object RuleEngine {

    fun targetMatches(target: String?, deviceType: String): Boolean {
        val t = target?.trim()?.lowercase() ?: "all"
        return t.isEmpty() || t == "all" || t == deviceType.lowercase()
    }

    fun evaluate(state: GuardState, deviceType: String, snoozeUntilMs: Long): EffectiveBlockState {
        val nowMs = System.currentTimeMillis()
        val nowCal = Calendar.getInstance()

        var bestLevel = 0
        var bestReason = "待機中"
        var bestEndsAt: Long? = null

        val ms = state.manualSession
        if (ms.active && targetMatches(ms.target, deviceType)) {
            val notExpired = ms.expiresAt <= 0L || ms.expiresAt > nowMs
            if (notExpired) {
                bestLevel = ms.level
                bestReason = "手動集中モード"
                bestEndsAt = if (ms.expiresAt > 0L) ms.expiresAt else null
            }
        }

        for (rule in state.schedules) {
            if (!rule.enabled || !targetMatches(rule.target, deviceType)) continue
            val endMs = checkScheduleActiveEndMs(rule, nowCal)
            if (endMs != null) {
                if (rule.level > bestLevel) {
                    bestLevel = rule.level
                    bestReason = "スケジュール: ${rule.name.ifBlank { "無題" }}"
                    bestEndsAt = endMs
                } else if (rule.level == bestLevel && bestLevel > 0) {
                    if (bestEndsAt != null && endMs > bestEndsAt) {
                        bestEndsAt = endMs
                    }
                }
            }
        }

        if (bestLevel <= 0) {
            return EffectiveBlockState()
        }

        val snoozed = bestLevel < 3 && snoozeUntilMs > nowMs
        val snoozeSec = if (snoozed) ceil((snoozeUntilMs - nowMs) / 1000.0).toInt() else 0

        return EffectiveBlockState(
            isActive = true,
            level = bestLevel,
            reason = bestReason,
            endsAtMs = bestEndsAt,
            isSnoozed = snoozed,
            snoozeRemainingSeconds = snoozeSec
        )
    }

    private fun parseMinutes(hhmm: String): Int? {
        val parts = hhmm.trim().split(":")
        if (parts.size != 2) return null
        val h = parts[0].toIntOrNull() ?: return null
        val m = parts[1].toIntOrNull() ?: return null
        if (h !in 0..23 || m !in 0..59) return null
        return h * 60 + m
    }

    private fun checkScheduleActiveEndMs(rule: ScheduleRule, nowCal: Calendar): Long? {
        if (rule.days.isEmpty()) return null
        val startMin = parseMinutes(rule.startTime) ?: return null
        val endMin = parseMinutes(rule.endTime) ?: return null

        val todayDow = nowCal.get(Calendar.DAY_OF_WEEK) - 1 // 0=Sun..6=Sat
        val yestDow = (todayDow + 6) % 7
        val curMin = nowCal.get(Calendar.HOUR_OF_DAY) * 60 + nowCal.get(Calendar.MINUTE)

        fun buildEndTime(addDays: Int, totalMin: Int): Long {
            val c = nowCal.clone() as Calendar
            c.add(Calendar.DAY_OF_YEAR, addDays)
            c.set(Calendar.HOUR_OF_DAY, totalMin / 60)
            c.set(Calendar.MINUTE, totalMin % 60)
            c.set(Calendar.SECOND, 0)
            c.set(Calendar.MILLISECOND, 0)
            return c.timeInMillis
        }

        return when {
            startMin == endMin -> {
                if (rule.days.contains(todayDow)) buildEndTime(1, 0) else null
            }
            startMin < endMin -> {
                if (rule.days.contains(todayDow) && curMin in startMin until endMin) {
                    buildEndTime(0, endMin)
                } else null
            }
            else -> {
                // Overnight (e.g. 23:00 - 07:00)
                when {
                    rule.days.contains(todayDow) && curMin >= startMin -> buildEndTime(1, endMin)
                    rule.days.contains(yestDow) && curMin < endMin -> buildEndTime(0, endMin)
                    else -> null
                }
            }
        }
    }

    fun wouldWeakenHardLock(current: GuardState, proposed: GuardState): Boolean {
        for (dev in listOf("pc", "android")) {
            val cur = evaluate(current, dev, 0L)
            if (cur.isHardLocked) {
                val next = evaluate(proposed, dev, 0L)
                if (!next.isHardLocked) return true
                if (cur.endsAtMs != null && next.endsAtMs != null && next.endsAtMs + 5000L < cur.endsAtMs) {
                    return true
                }
            }
        }
        return false
    }
}
