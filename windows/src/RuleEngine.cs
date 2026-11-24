using System;
using System.Globalization;

namespace FocusGuard
{
    public static class RuleEngine
    {
        public static bool TargetMatches(string target, string deviceType)
        {
            if (string.IsNullOrEmpty(target)) return true;
            string t = target.Trim().ToLowerInvariant();
            return t == "all" || t == deviceType.ToLowerInvariant();
        }

        public static EffectiveBlockState Evaluate(GuardState state, string deviceType, long snoozeUntilMs)
        {
            var result = new EffectiveBlockState
            {
                IsActive = false,
                Level = 0,
                Reason = "待機中",
                EndsAtMs = null,
                IsSnoozed = false,
                SnoozeRemainingSeconds = 0
            };

            if (state == null) return result;

            long nowMs = TimeUtil.NowMs();
            DateTime nowLocal = DateTime.Now;

            int bestLevel = 0;
            string bestReason = "待機中";
            long? bestEndsAt = null;

            // 1. Check manual session
            if (state.manualSession != null && state.manualSession.active)
            {
                bool notExpired = state.manualSession.expiresAt <= 0 || state.manualSession.expiresAt > nowMs;
                if (notExpired && TargetMatches(state.manualSession.target, deviceType))
                {
                    bestLevel = state.manualSession.level;
                    bestReason = "手動集中モード";
                    bestEndsAt = state.manualSession.expiresAt > 0 ? (long?)state.manualSession.expiresAt : null;
                }
            }

            // 2. Check schedule rules (supports overnight ranges like 22:00 - 07:00)
            if (state.schedules != null)
            {
                foreach (var rule in state.schedules)
                {
                    if (rule == null || !rule.enabled) continue;
                    if (!TargetMatches(rule.target, deviceType)) continue;

                    DateTime ruleEndLocal;
                    if (IsScheduleActiveNow(rule, nowLocal, out ruleEndLocal))
                    {
                        long ruleEndMs = TimeUtil.FromLocalDateTime(ruleEndLocal);
                        if (rule.level > bestLevel)
                        {
                            bestLevel = rule.level;
                            bestReason = "スケジュール: " + (string.IsNullOrEmpty(rule.name) ? "無題" : rule.name);
                            bestEndsAt = ruleEndMs;
                        }
                        else if (rule.level == bestLevel && bestLevel > 0)
                        {
                            // Keep the later end time if same level
                            if (bestEndsAt.HasValue && ruleEndMs > bestEndsAt.Value)
                            {
                                bestEndsAt = ruleEndMs;
                            }
                        }
                    }
                }
            }

            if (bestLevel > 0)
            {
                result.IsActive = true;
                result.Level = bestLevel;
                result.Reason = bestReason;
                result.EndsAtMs = bestEndsAt;

                // Snooze only applies to Lv.1 and Lv.2 (never bypasses Lv.3)
                if (bestLevel < 3 && snoozeUntilMs > nowMs)
                {
                    result.IsSnoozed = true;
                    result.SnoozeRemainingSeconds = (int)Math.Ceiling((snoozeUntilMs - nowMs) / 1000.0);
                }
            }

            return result;
        }

        public static bool IsScheduleActiveNow(ScheduleRule rule, DateTime nowLocal, out DateTime endLocal)
        {
            endLocal = nowLocal;
            if (rule.days == null || rule.days.Count == 0) return false;

            TimeSpan startTs, endTs;
            if (!TryParseTime(rule.startTime, out startTs) || !TryParseTime(rule.endTime, out endTs))
                return false;

            int todayDow = (int)nowLocal.DayOfWeek; // 0=Sun..6=Sat
            int yesterdayDow = (todayDow + 6) % 7;
            TimeSpan currentTs = nowLocal.TimeOfDay;

            if (startTs == endTs)
            {
                // Full 24h block on selected days
                if (rule.days.Contains(todayDow))
                {
                    endLocal = nowLocal.Date.AddDays(1);
                    return true;
                }
                return false;
            }
            else if (startTs < endTs)
            {
                // Same-day window (e.g. 09:00 - 17:00)
                if (rule.days.Contains(todayDow) && currentTs >= startTs && currentTs < endTs)
                {
                    endLocal = nowLocal.Date.Add(endTs);
                    return true;
                }
                return false;
            }
            else
            {
                // Overnight window (e.g. 22:00 - 07:00)
                // Case A: Started today (current >= start)
                if (rule.days.Contains(todayDow) && currentTs >= startTs)
                {
                    endLocal = nowLocal.Date.AddDays(1).Add(endTs);
                    return true;
                }
                // Case B: Started yesterday (current < end)
                if (rule.days.Contains(yesterdayDow) && currentTs < endTs)
                {
                    endLocal = nowLocal.Date.Add(endTs);
                    return true;
                }
                return false;
            }
        }

        private static bool TryParseTime(string hhmm, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(hhmm)) return false;
            DateTime dt;
            if (DateTime.TryParseExact(hhmm.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                result = dt.TimeOfDay;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Validates rule transitions against Lv.3 hard lock and Lv.2 downgrade/shortening rules.
        /// Returns null if allowed, or an error message if blocked.
        /// </summary>
        public static string ValidateTransition(GuardState current, GuardState proposed, bool lv2ChallengePassed)
        {
            string[] devices = new[] { "pc", "android" };
            foreach (var dev in devices)
            {
                var cur = Evaluate(current, dev, 0);
                var next = Evaluate(proposed, dev, 0);

                // Lv.3: never allow stopping, downgrading, removing target, shortening remaining time, or removing blocked apps
                if (cur.IsHardLocked)
                {
                    if (!next.IsHardLocked)
                        return "Lv.3 ハードブロック中のため、終了時刻まで解除またはレベル引き下げはできません。";
                    if (IsShortened(cur.EndsAtMs, next.EndsAtMs))
                        return "Lv.3 ハードブロック中のため、残り時間を短縮することはできません。";
                    if (RemovedAnyTarget(current, proposed, dev))
                        return "Lv.3 ハードブロック中のため、ブロック対象アプリを外すことはできません（追加は可能です）。";
                }
                // Lv.2: prevent silent downgrade (e.g. Lv.2 -> Lv.1), shortening, or removing apps unless explicit challenge passed
                else if (cur.IsActive && cur.Level == 2 && !lv2ChallengePassed)
                {
                    if (!next.IsActive)
                        return "Lv.2 ブロックを停止するには、確認フレーズの入力が必要です。";
                    if (next.Level < 2)
                        return "Lv.2 稼働中は Lv.1 へ引き下げできません（解除するには「停止」から確認フレーズを入力してください）。";
                    if (next.Level == 2 && IsShortened(cur.EndsAtMs, next.EndsAtMs))
                        return "Lv.2 稼働中は残り時間を短縮できません（延長や Lv.3 への強化は可能です）。";
                    if (RemovedAnyTarget(current, proposed, dev))
                        return "Lv.2 稼働中にブロック対象アプリを外すには確認フレーズの入力が必要です（追加はそのまま可能です）。";
                }
            }
            return null;
        }

        private static bool RemovedAnyTarget(GuardState current, GuardState proposed, string dev)
        {
            if (current == null || current.browsers == null || proposed == null || proposed.browsers == null)
                return false;
            var curList = dev == "pc" ? current.browsers.windows : current.browsers.android;
            var nextList = dev == "pc" ? proposed.browsers.windows : proposed.browsers.android;
            if (curList == null || curList.Count == 0) return false;
            var nextSet = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (nextList != null)
            {
                foreach (var x in nextList)
                    if (!string.IsNullOrWhiteSpace(x)) nextSet.Add(x.Trim());
            }
            foreach (var item in curList)
            {
                if (!string.IsNullOrWhiteSpace(item) && !nextSet.Contains(item.Trim()))
                    return true;
            }
            return false;
        }

        private static bool IsShortened(long? curEndsAt, long? nextEndsAt)
        {
            // Indefinite (null) -> finite is a shortening
            if (!curEndsAt.HasValue && nextEndsAt.HasValue) return true;
            if (curEndsAt.HasValue && nextEndsAt.HasValue && nextEndsAt.Value + 5000 < curEndsAt.Value)
                return true;
            return false;
        }

        public static bool WouldWeakenHardLock(GuardState current, GuardState proposed)
        {
            return ValidateTransition(current, proposed, true) != null;
        }
    }
}
