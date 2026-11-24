package com.focusguard.app

import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

data class ManualSession(
    var active: Boolean = false,
    var level: Int = 2,
    var target: String = "all",
    var startedAt: Long = 0L,
    var expiresAt: Long = 0L
)

data class ScheduleRule(
    var id: String = "sched-" + UUID.randomUUID().toString().replace("-", "").take(8),
    var name: String = "集中スケジュール",
    var enabled: Boolean = true,
    var days: MutableList<Int> = mutableListOf(0, 1, 2, 3, 4, 5, 6),
    var startTime: String = "23:00",
    var endTime: String = "07:00",
    var level: Int = 2,
    var target: String = "all"
)

data class BrowserTargets(
    var windows: MutableList<String> = mutableListOf(
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "waterfox", "floorp"
    ),
    var android: MutableList<String> = mutableListOf(
        "com.android.chrome",
        "com.chrome.beta",
        "com.chrome.dev",
        "org.mozilla.firefox",
        "org.mozilla.fenix",
        "com.microsoft.emmx",
        "com.brave.browser",
        "com.sec.android.app.sbrowser",
        "com.opera.browser",
        "com.opera.mini.native",
        "com.vivaldi.browser",
        "com.duckduckgo.mobile.android",
        "com.kiwibrowser.browser",
        "com.google.android.googlequicksearchbox"
    )
)

data class DeviceHeartbeat(
    var lastSeen: Long = 0L,
    var status: String = "offline",
    var effectiveLevel: Int = 0
)

data class GuardState(
    var version: Int = 1,
    var updatedAt: Long = System.currentTimeMillis(),
    var manualSession: ManualSession = ManualSession(),
    var schedules: MutableList<ScheduleRule> = mutableListOf(),
    var browsers: BrowserTargets = BrowserTargets(),
    var lv1WaitSeconds: Int = 15,
    var lv1AllowMinutes: Int = 5,
    var lv2AllowMinutes: Int = 10,
    var pcHeartbeat: DeviceHeartbeat = DeviceHeartbeat(),
    var androidHeartbeat: DeviceHeartbeat = DeviceHeartbeat(),
    /** ブロック中でも開けるサイト（ホワイトリスト）。例: example.com */
    var allowedSites: MutableList<String> = mutableListOf()
) {
    fun toJson(): JSONObject {
        val root = JSONObject()
        root.put("version", version)
        root.put("updatedAt", updatedAt)

        val ms = JSONObject()
        ms.put("active", manualSession.active)
        ms.put("level", manualSession.level)
        ms.put("target", manualSession.target)
        ms.put("startedAt", manualSession.startedAt)
        ms.put("expiresAt", manualSession.expiresAt)
        root.put("manualSession", ms)

        val schedArr = JSONArray()
        for (s in schedules) {
            val o = JSONObject()
            o.put("id", s.id)
            o.put("name", s.name)
            o.put("enabled", s.enabled)
            val dArr = JSONArray()
            s.days.forEach { dArr.put(it) }
            o.put("days", dArr)
            o.put("startTime", s.startTime)
            o.put("endTime", s.endTime)
            o.put("level", s.level)
            o.put("target", s.target)
            schedArr.put(o)
        }
        root.put("schedules", schedArr)

        val bObj = JSONObject()
        val wArr = JSONArray()
        browsers.windows.forEach { wArr.put(it) }
        val aArr = JSONArray()
        browsers.android.forEach { aArr.put(it) }
        bObj.put("windows", wArr)
        bObj.put("android", aArr)
        root.put("browsers", bObj)

        root.put("lv1WaitSeconds", lv1WaitSeconds)
        root.put("lv1AllowMinutes", lv1AllowMinutes)
        root.put("lv2AllowMinutes", lv2AllowMinutes)
        root.put("allowedSites", JSONArray().apply { SiteAllowlist.normalizeList(allowedSites).forEach { put(it) } })

        val devs = JSONObject()
        devs.put("pc", JSONObject().apply {
            put("lastSeen", pcHeartbeat.lastSeen)
            put("status", pcHeartbeat.status)
            put("effectiveLevel", pcHeartbeat.effectiveLevel)
        })
        devs.put("android", JSONObject().apply {
            put("lastSeen", androidHeartbeat.lastSeen)
            put("status", androidHeartbeat.status)
            put("effectiveLevel", androidHeartbeat.effectiveLevel)
        })
        root.put("devices", devs)
        return root
    }

    fun deepCopy(): GuardState = fromJson(toJson())

    companion object {
        fun fromJson(obj: JSONObject?): GuardState {
            if (obj == null) return GuardState()
            val st = GuardState()
            st.version = obj.optInt("version", 1)
            st.updatedAt = obj.optLong("updatedAt", System.currentTimeMillis())

            obj.optJSONObject("manualSession")?.let { ms ->
                st.manualSession = ManualSession(
                    active = ms.optBoolean("active", false),
                    level = ms.optInt("level", 2).coerceIn(1, 3),
                    target = ms.optString("target", "all").ifBlank { "all" },
                    startedAt = ms.optLong("startedAt", 0L),
                    expiresAt = ms.optLong("expiresAt", 0L)
                )
            }

            obj.optJSONArray("schedules")?.let { arr ->
                st.schedules = mutableListOf()
                for (i in 0 until arr.length()) {
                    val o = arr.optJSONObject(i) ?: continue
                    val days = mutableListOf<Int>()
                    o.optJSONArray("days")?.let { dArr ->
                        for (j in 0 until dArr.length()) {
                            days.add(dArr.optInt(j))
                        }
                    }
                    st.schedules.add(
                        ScheduleRule(
                            id = o.optString("id", "sched-$i").ifBlank { "sched-$i" },
                            name = o.optString("name", "スケジュール"),
                            enabled = o.optBoolean("enabled", true),
                            days = if (days.isEmpty()) mutableListOf(0, 1, 2, 3, 4, 5, 6) else days,
                            startTime = o.optString("startTime", "23:00"),
                            endTime = o.optString("endTime", "07:00"),
                            level = o.optInt("level", 2).coerceIn(1, 3),
                            target = o.optString("target", "all").ifBlank { "all" }
                        )
                    )
                }
            }

            obj.optJSONObject("browsers")?.let { b ->
                val def = BrowserTargets()
                val winList = mutableListOf<String>()
                b.optJSONArray("windows")?.let { arr ->
                    for (i in 0 until arr.length()) {
                        val s = arr.optString(i).trim()
                        if (s.isNotEmpty()) winList.add(s)
                    }
                }
                val andList = mutableListOf<String>()
                b.optJSONArray("android")?.let { arr ->
                    for (i in 0 until arr.length()) {
                        val s = arr.optString(i).trim()
                        if (s.isNotEmpty()) andList.add(s)
                    }
                }
                st.browsers = BrowserTargets(
                    windows = if (winList.isEmpty()) def.windows else winList,
                    android = if (andList.isEmpty()) def.android else andList
                )
            }

            st.lv1WaitSeconds = obj.optInt("lv1WaitSeconds", 15).coerceAtLeast(3)
            st.lv1AllowMinutes = obj.optInt("lv1AllowMinutes", 5).coerceAtLeast(1)
            st.lv2AllowMinutes = obj.optInt("lv2AllowMinutes", 10).coerceAtLeast(1)
            obj.optJSONArray("allowedSites")?.let { arr ->
                st.allowedSites = SiteAllowlist.normalizeList((0 until arr.length()).map { arr.optString(it) }).toMutableList()
            }

            obj.optJSONObject("devices")?.let { devs ->
                devs.optJSONObject("pc")?.let { d ->
                    st.pcHeartbeat = DeviceHeartbeat(
                        lastSeen = d.optLong("lastSeen", 0L),
                        status = d.optString("status", "offline"),
                        effectiveLevel = d.optInt("effectiveLevel", 0)
                    )
                }
                devs.optJSONObject("android")?.let { d ->
                    st.androidHeartbeat = DeviceHeartbeat(
                        lastSeen = d.optLong("lastSeen", 0L),
                        status = d.optString("status", "offline"),
                        effectiveLevel = d.optInt("effectiveLevel", 0)
                    )
                }
            }
            return st
        }
    }
}

data class EffectiveBlockState(
    val isActive: Boolean = false,
    val level: Int = 0,
    val reason: String = "待機中",
    val endsAtMs: Long? = null,
    val isSnoozed: Boolean = false,
    val snoozeRemainingSeconds: Int = 0
) {
    val isHardLocked: Boolean
        get() = isActive && level >= 3

    fun remainingText(): String {
        val end = endsAtMs ?: return "手動解除まで"
        if (end <= 0L) return "手動解除まで"
        val diffSec = ((end - System.currentTimeMillis()) / 1000L).coerceAtLeast(0L)
        if (diffSec <= 0L) return "まもなく終了"
        val h = diffSec / 3600
        val m = (diffSec % 3600) / 60
        val s = diffSec % 60
        return when {
            h > 0 -> "${h}時間${m}分"
            m > 0 -> "${m}分${s}秒"
            else -> "${s}秒"
        }
    }
}
