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

    fun validateTransition(current: GuardState, proposed: GuardState, lv2ChallengePassed: Boolean = false): String? {
        for (dev in listOf("pc", "android")) {
            val cur = evaluate(current, dev, 0L)
            val next = evaluate(proposed, dev, 0L)
            if (cur.isHardLocked) {
                if (!next.isHardLocked) {
                    return "Lv.3 ハードブロック中のため、終了時刻まで解除またはレベル引き下げはできません。"
                }
                if (isShortened(cur.endsAtMs, next.endsAtMs)) {
                    return "Lv.3 ハードブロック中のため、残り時間を短縮することはできません。"
                }
                if (removedAnyTarget(current, proposed, dev)) {
                    return "Lv.3 ハードブロック中のため、ブロック対象アプリを外すことはできません（追加は可能です）。"
                }
            } else if (cur.isActive && cur.level == 2 && !lv2ChallengePassed) {
                if (!next.isActive) {
                    return "Lv.2 ブロックを停止するには確認フレーズの入力が必要です。"
                }
                if (next.level < 2) {
                    return "Lv.2 稼働中は Lv.1 へ引き下げできません（解除するには「停止」から確認フレーズを入力してください）。"
                }
                if (next.level == 2 && isShortened(cur.endsAtMs, next.endsAtMs)) {
                    return "Lv.2 稼働中は残り時間を短縮できません（延長や Lv.3 への強化は可能です）。"
                }
                if (removedAnyTarget(current, proposed, dev)) {
                    return "Lv.2 稼働中にブロック対象アプリを外すには確認フレーズの入力が必要です（追加はそのまま可能です）。"
                }
            }
        }
        return null
    }

    private fun removedAnyTarget(cur: GuardState, next: GuardState, dev: String): Boolean {
        val curList = if (dev == "pc") cur.browsers.windows else cur.browsers.android
        val nextSet = (if (dev == "pc") next.browsers.windows else next.browsers.android)
            .map { it.trim().lowercase() }
            .toSet()
        return curList.any { it.trim().lowercase() !in nextSet }
    }

    private fun isShortened(curEndsAt: Long?, nextEndsAt: Long?): Boolean {
        if (curEndsAt == null && nextEndsAt != null) return true
        if (curEndsAt != null && nextEndsAt != null && nextEndsAt + 5000L < curEndsAt) return true
        return false
    }

    fun wouldWeakenHardLock(current: GuardState, proposed: GuardState): Boolean {
        return validateTransition(current, proposed, lv2ChallengePassed = true) != null
    }
}
