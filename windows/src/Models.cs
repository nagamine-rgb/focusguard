using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace FocusGuard
{
    public class AppSettings
    {
        public string SyncMode { get; set; } // "local" or "supabase"
        public string SupabaseUrl { get; set; }
        public string SupabaseAnonKey { get; set; }
        public string SyncKey { get; set; }
        public int LocalPort { get; set; }
        public int PollIntervalSeconds { get; set; }
        public bool StartMinimized { get; set; }
        public bool StartWithWindows { get; set; }

        public AppSettings()
        {
            SyncMode = "local";
            SupabaseUrl = "";
            SupabaseAnonKey = "";
            SyncKey = Guid.NewGuid().ToString("N").Substring(0, 16);
            LocalPort = 8787;
            PollIntervalSeconds = 4;
            StartMinimized = false;
            StartWithWindows = false;
        }
    }

    public class ManualSession
    {
        public bool active { get; set; }
        public int level { get; set; } // 1, 2, 3
        public string target { get; set; } // "all", "pc", "android"
        public long startedAt { get; set; } // Unix ms
        public long expiresAt { get; set; } // Unix ms (0 = until stopped)

        public ManualSession()
        {
            active = false;
            level = 2;
            target = "all";
            startedAt = 0;
            expiresAt = 0;
        }
    }

    public class ScheduleRule
    {
        public string id { get; set; }
        public string name { get; set; }
        public bool enabled { get; set; }
        public List<int> days { get; set; } // 0=Sun .. 6=Sat
        public string startTime { get; set; } // "HH:mm"
        public string endTime { get; set; }   // "HH:mm"
        public int level { get; set; }        // 1, 2, 3
        public string target { get; set; }    // "all", "pc", "android"

        public ScheduleRule()
        {
            id = "sched-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            name = "集中タイム";
            enabled = true;
            days = new List<int> { 0, 1, 2, 3, 4, 5, 6 };
            startTime = "22:00";
            endTime = "07:00";
            level = 2;
            target = "all";
        }
    }

    public class BrowserTargets
    {
        public List<string> windows { get; set; }
        public List<string> android { get; set; }

        public BrowserTargets()
        {
            windows = new List<string>
            {
                "chrome",
                "msedge",
                "firefox",
                "brave",
                "opera",
                "vivaldi",
                "arc",
                "waterfox",
                "floorp"
            };
            android = new List<string>
            {
                "com.android.chrome",
                "org.mozilla.firefox",
                "com.microsoft.emmx",
                "com.brave.browser",
                "com.sec.android.app.sbrowser",
                "com.opera.browser",
                "com.vivaldi.browser"
            };
        }
    }

    public class DeviceHeartbeat
    {
        public long lastSeen { get; set; }
        public string status { get; set; }
        public int effectiveLevel { get; set; }

        public DeviceHeartbeat()
        {
            lastSeen = 0;
            status = "offline";
            effectiveLevel = 0;
        }
    }

    public class DevicesMap
    {
        public DeviceHeartbeat pc { get; set; }
        public DeviceHeartbeat android { get; set; }

        public DevicesMap()
        {
            pc = new DeviceHeartbeat();
            android = new DeviceHeartbeat();
        }
    }

    public class GuardState
    {
        public int version { get; set; }
        public long updatedAt { get; set; }
        public ManualSession manualSession { get; set; }
        public List<ScheduleRule> schedules { get; set; }
        public BrowserTargets browsers { get; set; }
        public int lv1WaitSeconds { get; set; }
        public int lv1AllowMinutes { get; set; }
        public int lv2AllowMinutes { get; set; }
        public DevicesMap devices { get; set; }
        /// <summary>ブロック中でも開けるサイト（ホワイトリスト）。例: example.com</summary>
        public List<string> allowedSites { get; set; }

        public GuardState()
        {
            version = 1;
            updatedAt = TimeUtil.NowMs();
            manualSession = new ManualSession();
            schedules = new List<ScheduleRule>
            {
                new ScheduleRule
                {
                    id = "sched-night",
                    name = "夜間デジタルデトックス",
                    enabled = false,
                    days = new List<int> { 0, 1, 2, 3, 4, 5, 6 },
                    startTime = "23:00",
                    endTime = "07:00",
                    level = 2,
                    target = "all"
                }
            };
            browsers = new BrowserTargets();
            lv1WaitSeconds = 15;
            lv1AllowMinutes = 5;
            lv2AllowMinutes = 10;
            devices = new DevicesMap();
            allowedSites = new List<string>();
        }

        public void EnsureDefaults()
        {
            if (version <= 0) version = 1;
            if (manualSession == null) manualSession = new ManualSession();
            if (string.IsNullOrEmpty(manualSession.target)) manualSession.target = "all";
            if (manualSession.level < 1 || manualSession.level > 3) manualSession.level = 2;
            if (schedules == null) schedules = new List<ScheduleRule>();
            foreach (var s in schedules)
            {
                if (string.IsNullOrEmpty(s.id)) s.id = "sched-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                if (s.days == null) s.days = new List<int> { 0, 1, 2, 3, 4, 5, 6 };
                if (string.IsNullOrEmpty(s.target)) s.target = "all";
                if (s.level < 1 || s.level > 3) s.level = 2;
            }
            if (browsers == null) browsers = new BrowserTargets();
            if (browsers.windows == null || browsers.windows.Count == 0)
                browsers.windows = new BrowserTargets().windows;
            if (browsers.android == null || browsers.android.Count == 0)
                browsers.android = new BrowserTargets().android;
            if (lv1WaitSeconds < 3) lv1WaitSeconds = 15;
            if (lv1AllowMinutes < 1) lv1AllowMinutes = 5;
            if (lv2AllowMinutes < 1) lv2AllowMinutes = 10;
            if (devices == null) devices = new DevicesMap();
            if (devices.pc == null) devices.pc = new DeviceHeartbeat();
            if (devices.android == null) devices.android = new DeviceHeartbeat();
            allowedSites = SiteAllowlist.NormalizeList(allowedSites);
        }
    }

    public class EffectiveBlockState
    {
        public bool IsActive { get; set; }
        public int Level { get; set; }
        public string Reason { get; set; }
        public long? EndsAtMs { get; set; }
        public bool IsSnoozed { get; set; }
        public int SnoozeRemainingSeconds { get; set; }
        public bool IsHardLocked { get { return IsActive && Level >= 3; } }

        public string RemainingText
        {
            get
            {
                if (!EndsAtMs.HasValue || EndsAtMs.Value <= 0) return "手動解除まで";
                long diff = EndsAtMs.Value - TimeUtil.NowMs();
                if (diff <= 0) return "まもなく終了";
                TimeSpan ts = TimeSpan.FromMilliseconds(diff);
                if (ts.TotalHours >= 1)
                    return string.Format("{0}時間{1}分", (int)ts.TotalHours, ts.Minutes);
                if (ts.TotalMinutes >= 1)
                    return string.Format("{0}分{1}秒", ts.Minutes, ts.Seconds);
                return string.Format("{0}秒", ts.Seconds);
            }
        }
    }

    public static class TimeUtil
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long NowMs()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
        }

        public static long FromLocalDateTime(DateTime localDt)
        {
            return (long)(localDt.ToUniversalTime() - Epoch).TotalMilliseconds;
        }

        public static DateTime ToLocalDateTime(long ms)
        {
            return Epoch.AddMilliseconds(ms).ToLocalTime();
        }
    }

    public static class JsonUtil
    {
        public static JavaScriptSerializer CreateSerializer()
        {
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 4 * 1024 * 1024;
            return ser;
        }

        public static string Serialize(object obj)
        {
            return CreateSerializer().Serialize(obj);
        }

        public static T Deserialize<T>(string json)
        {
            return CreateSerializer().Deserialize<T>(json);
        }
    }
}
