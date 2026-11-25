using System;
using System.Collections.Generic;
using System.Linq;

namespace FocusGuard
{
    /// <summary>手動ブロックの記録（d=端末, s=開始, e=終了予定または止めた時刻, 0=手動で止めるまで）</summary>
    public class HistoryEntry
    {
        public string d { get; set; }
        public long s { get; set; }
        public long e { get; set; }
    }

    /// <summary>曜日つきの時間帯（スケジュールの記録と、累計に数えない時間帯の両方で使う）</summary>
    public class StatsRule
    {
        public string id { get; set; }
        public string name { get; set; }
        public List<int> days { get; set; }
        public string start { get; set; }
        public string end { get; set; }
    }

    /// <summary>スケジュールを変えた時刻 t と、その時点の有効なスケジュール</summary>
    public class ScheduleSnapshot
    {
        public long t { get; set; }
        public List<StatsRule> rules { get; set; }
    }

    /// <summary>
    /// ブロックの累計時間。「予定どおりの時間」を PC とスマホで共通の1つの時間として数える
    /// （同じ時間に両方止めていても1回だけ）。睡眠や仕事など、数えない時間帯は差し引く。
    /// Web リモコン（index.html）と Android（BlockStats.kt）と同じ計算。
    /// </summary>
    public static class BlockStats
    {
        private const long DayMs = 24L * 60 * 60 * 1000;
        private const long HistoryKeepMs = 70 * DayMs;
        private static readonly string[] Devices = { "pc", "android" };

        public static int? ParseMin(string s)
        {
            TimeSpan ts;
            if (string.IsNullOrWhiteSpace(s) || !TimeSpan.TryParse(s.Trim(), out ts)) return null;
            return (int)ts.TotalMinutes;
        }

        public static List<StatsRule> RulesOf(GuardState st)
        {
            var list = new List<StatsRule>();
            if (st == null || st.schedules == null) return list;
            foreach (var r in st.schedules)
            {
                if (r == null || !r.enabled || ParseMin(r.startTime) == null || ParseMin(r.endTime) == null) continue;
                list.Add(new StatsRule
                {
                    days = (r.days ?? new List<int>()).Where(d => d >= 0 && d <= 6).OrderBy(d => d).ToList(),
                    start = r.startTime,
                    end = r.endTime
                });
            }
            return list.OrderBy(RuleKey, StringComparer.Ordinal).ToList();
        }

        private static string RuleKey(StatsRule r)
        {
            return string.Join(",", (r.days ?? new List<int>()).Select(d => d.ToString()).ToArray()) + " " + r.start + "-" + r.end;
        }

        private static string Key(List<StatsRule> rules)
        {
            return string.Join("|", (rules ?? new List<StatsRule>()).Select(RuleKey).ToArray());
        }

        private static long StartOf(ManualSession s, long now)
        {
            return s.startedAt > 0 ? s.startedAt : now;
        }

        /// <summary>保存の直前に呼ぶ: cur → next の変化を履歴に書く（何度呼んでも同じ結果になる）</summary>
        public static void RecordHistory(GuardState cur, GuardState next, long now)
        {
            // 履歴は前の状態と合わせる（古い版が履歴なしで保存してきても消えないように）
            var hist = (next.blockHistory ?? new List<HistoryEntry>()).Select(h => new HistoryEntry { d = h.d, s = h.s, e = h.e }).ToList();
            if (cur != null && cur.blockHistory != null)
                foreach (var h in cur.blockHistory)
                    if (!hist.Any(x => x.d == h.d && x.s == h.s)) hist.Add(new HistoryEntry { d = h.d, s = h.s, e = h.e });
            next.blockHistory = hist;
            if ((next.scheduleSnapshots == null || next.scheduleSnapshots.Count == 0) && cur != null && cur.scheduleSnapshots != null)
                next.scheduleSnapshots = cur.scheduleSnapshots.ToList();
            if (next.scheduleSnapshots == null) next.scheduleSnapshots = new List<ScheduleSnapshot>();
            if (next.statsExclusions == null && cur != null) next.statsExclusions = cur.statsExclusions;

            foreach (var dev in Devices)
            {
                ManualSession a = cur != null ? cur.SessionFor(dev) : null;
                ManualSession b = next.SessionFor(dev);
                bool aLive = GuardState.IsLive(a, now), bLive = GuardState.IsLive(b, now);
                if (aLive && !(bLive && StartOf(b, now) == StartOf(a, now) && b.expiresAt == a.expiresAt))
                {
                    var rec = hist.FirstOrDefault(h => h.d == dev && h.s == StartOf(a, now));
                    if (rec != null && (rec.e == 0 || rec.e > now))
                        rec.e = (bLive && StartOf(b, now) == StartOf(a, now)) ? b.expiresAt : now;
                }
                if (bLive)
                {
                    long s = StartOf(b, now);
                    var rec = hist.FirstOrDefault(h => h.d == dev && h.s == s);
                    if (rec == null) hist.Add(new HistoryEntry { d = dev, s = s, e = b.expiresAt });
                    else if (!(aLive && StartOf(a, now) == s && a.expiresAt == b.expiresAt)) rec.e = b.expiresAt;
                }
            }

            var prevRules = RulesOf(cur);
            var nextRules = RulesOf(next);
            var snaps = next.scheduleSnapshots;
            if (Key(prevRules) != Key(nextRules))
            {
                long monthStart, monthEnd;
                MonthRange(0, now, out monthStart, out monthEnd);
                // 記録を始める前のスケジュールは、今月の月初から続いていたものとして扱う
                if (snaps.Count == 0) snaps.Add(new ScheduleSnapshot { t = monthStart, rules = prevRules });
                if (Key(snaps[snaps.Count - 1].rules) != Key(nextRules)) snaps.Add(new ScheduleSnapshot { t = now, rules = nextRules });
            }

            // 古い記録を捨てる（先月の分が計算できるよう約70日は残す）
            long cutoff = now - HistoryKeepMs;
            next.blockHistory = hist.Where(h => h.e == 0 || h.e > cutoff).ToList();
            while (snaps.Count > 1 && snaps[1].t <= cutoff) snaps.RemoveAt(0);
        }

        private static DateTime ToLocal(long ms)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime();
        }

        /// <summary>ルール（曜日・始まり・終わり）が当たる時間帯を [from, to) の範囲で並べる</summary>
        public static List<long[]> Occurrences(List<StatsRule> rules, long from, long to)
        {
            var output = new List<long[]>();
            if (to <= from || rules == null) return output;
            DateTime d = ToLocal(from).Date.AddDays(-1);
            for (; TimeUtil.FromLocalDateTime(d) < to; d = d.AddDays(1))
            {
                long day = TimeUtil.FromLocalDateTime(d);
                long next = TimeUtil.FromLocalDateTime(d.AddDays(1));
                int dow = (int)d.DayOfWeek;
                foreach (var r in rules)
                {
                    if (r == null || r.days == null || !r.days.Contains(dow)) continue;
                    int? s = ParseMin(r.start), e = ParseMin(r.end);
                    if (s == null || e == null) continue;
                    long a, b;
                    if (s.Value == e.Value) { a = day; b = next; }
                    else if (s.Value < e.Value) { a = day + s.Value * 60000L; b = day + e.Value * 60000L; }
                    else { a = day + s.Value * 60000L; b = next + e.Value * 60000L; }
                    a = Math.Max(a, from);
                    b = Math.Min(b, to);
                    if (b > a) output.Add(new[] { a, b });
                }
            }
            return output;
        }

        public static List<long[]> Merge(List<long[]> iv)
        {
            var output = new List<long[]>();
            foreach (var x in iv.OrderBy(v => v[0]))
            {
                if (output.Count > 0 && x[0] <= output[output.Count - 1][1])
                    output[output.Count - 1][1] = Math.Max(output[output.Count - 1][1], x[1]);
                else output.Add(new[] { x[0], x[1] });
            }
            return output;
        }

        /// <summary>[from, to) のブロック時間（ミリ秒）。数えない時間帯は差し引く</summary>
        public static long BlockedMs(GuardState st, long from, long to, long now)
        {
            long end = Math.Min(to, now);
            if (end <= from || st == null) return 0;
            var iv = new List<long[]>();
            var hist = st.blockHistory ?? new List<HistoryEntry>();
            foreach (var h in hist)
            {
                long a = Math.Max(h.s, from), b = h.e == 0 ? end : Math.Min(h.e, end);
                if (b > a) iv.Add(new[] { a, b });
            }
            // 記録前から続いている手動ブロックも数える
            foreach (var dev in Devices)
            {
                var ms = st.SessionFor(dev);
                if (GuardState.IsLive(ms, now) && !hist.Any(h => h.d == dev && h.s == ms.startedAt))
                {
                    long a = Math.Max(ms.startedAt > 0 ? ms.startedAt : now, from);
                    long b = ms.expiresAt > 0 ? Math.Min(ms.expiresAt, end) : end;
                    if (b > a) iv.Add(new[] { a, b });
                }
            }
            var snaps = st.scheduleSnapshots;
            if (snaps == null || snaps.Count == 0)
            {
                // 記録がまだないときは、今のスケジュールが今月の月初から続いていたものとして扱う
                long ms0, me0;
                MonthRange(0, now, out ms0, out me0);
                snaps = new List<ScheduleSnapshot> { new ScheduleSnapshot { t = ms0, rules = RulesOf(st) } };
            }
            for (int i = 0; i < snaps.Count; i++)
            {
                long a = Math.Max(snaps[i].t, from);
                long b = Math.Min(i + 1 < snaps.Count ? snaps[i + 1].t : end, end);
                iv.AddRange(Occurrences(snaps[i].rules, a, b));
            }
            var blocked = Merge(iv);
            var ex = Merge(Occurrences(st.statsExclusions, from, end));
            long total = 0;
            foreach (var x in blocked)
            {
                total += x[1] - x[0];
                foreach (var y in ex)
                {
                    long o = Math.Min(x[1], y[1]) - Math.Max(x[0], y[0]);
                    if (o > 0) total -= o;
                }
            }
            return total;
        }

        public static void MonthRange(int offset, long now, out long start, out long end)
        {
            var d = ToLocal(now);
            var first = new DateTime(d.Year, d.Month, 1).AddMonths(offset);
            start = TimeUtil.FromLocalDateTime(first);
            end = TimeUtil.FromLocalDateTime(first.AddMonths(1));
        }

        public static long MonthMs(GuardState st, int offset, long now)
        {
            long a, b;
            MonthRange(offset, now, out a, out b);
            return BlockedMs(st, a, b, now);
        }

        public static string FormatDuration(long ms)
        {
            long min = ms / 60000;
            long h = min / 60, m = min % 60;
            return h > 0 ? string.Format("{0}時間{1}分", h, m) : string.Format("{0}分", m);
        }

        public static string DescribeDays(List<int> days)
        {
            string[] dn = { "日", "月", "火", "水", "木", "金", "土" };
            var d = (days ?? new List<int>()).Where(x => x >= 0 && x <= 6).OrderBy(x => x).ToList();
            string j = string.Join(",", d.Select(x => x.ToString()).ToArray());
            if (d.Count == 7) return "毎日";
            if (j == "1,2,3,4,5") return "平日";
            if (j == "0,6") return "土日";
            return string.Join("・", d.Select(x => dn[x]).ToArray());
        }
    }
}
