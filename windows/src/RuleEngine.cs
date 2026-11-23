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
        /// Checks whether an update would weaken an active Lv.3 hard lock.
        /// </summary>
        public static bool WouldWeakenHardLock(GuardState current, GuardState proposed)
        {
            var curPc = Evaluate(current, "pc", 0);
            var curAndroid = Evaluate(current, "android", 0);

            if (curPc.IsHardLocked)
            {
                var nextPc = Evaluate(proposed, "pc", 0);
                if (!nextPc.IsHardLocked) return true;
                if (curPc.EndsAtMs.HasValue && nextPc.EndsAtMs.HasValue && nextPc.EndsAtMs.Value + 5000 < curPc.EndsAtMs.Value)
                    return true;
            }
            if (curAndroid.IsHardLocked)
            {
                var nextAndroid = Evaluate(proposed, "android", 0);
                if (!nextAndroid.IsHardLocked) return true;
                if (curAndroid.EndsAtMs.HasValue && nextAndroid.EndsAtMs.HasValue && nextAndroid.EndsAtMs.Value + 5000 < curAndroid.EndsAtMs.Value)
                    return true;
            }
            return false;
        }
    }
}
