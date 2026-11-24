package com.focusguard.app

import android.content.Context
import android.net.Uri
import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.HttpURLConnection
import java.net.URL
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

data class SupabaseConfig(
    val url: String = "",
    val anonKey: String = "",
    val syncKey: String = ""
) {
    val isConfigured: Boolean
        get() = url.isNotBlank() && anonKey.isNotBlank() && syncKey.length >= 8
}

class SyncRepository private constructor(context: Context) {

    private val prefs = context.applicationContext.getSharedPreferences("focus_guard_prefs", Context.MODE_PRIVATE)
    private val lock = Any()

    @Volatile
    var lastSyncMessage: String = "同期待機中"
        private set

    @Volatile
    var lastSyncOk: Boolean = true
        private set

    fun getConfig(): SupabaseConfig = SupabaseConfig(
        url = prefs.getString("supabase_url", "") ?: "",
        anonKey = prefs.getString("supabase_key", "") ?: "",
        syncKey = prefs.getString("sync_key", "") ?: ""
    )

    fun saveConfig(cfg: SupabaseConfig) {
        prefs.edit()
            .putString("supabase_url", cfg.url.trim().trimEnd('/'))
            .putString("supabase_key", cfg.anonKey.trim())
            .putString("sync_key", cfg.syncKey.trim())
            .apply()
    }

    /**
     * Parses a hash or full URL like:
     * https://.../focusguard/#url=https%3A...&key=eyJ...&sync=abcd1234
     * or #url=...&key=...&sync=...
     */
    fun parseConfigFromHashOrUrl(raw: String): SupabaseConfig? {
        val trimmed = raw.trim()
        val queryPart = when {
            trimmed.contains("#") -> trimmed.substringAfter("#")
            trimmed.contains("?") -> trimmed.substringAfter("?")
            else -> trimmed
        }
        val fakeUri = Uri.parse("https://local/?$queryPart")
        val u = fakeUri.getQueryParameter("url")?.trim() ?: return null
        val k = fakeUri.getQueryParameter("key")?.trim() ?: return null
        val s = fakeUri.getQueryParameter("sync")?.trim() ?: return null
        if (u.isBlank() || k.isBlank() || s.length < 8) return null
        return SupabaseConfig(u.trimEnd('/'), k, s)
    }

    fun getSnoozeUntilMs(): Long = prefs.getLong("snooze_until_ms", 0L)

    fun setSnoozeUntilMs(ms: Long) {
        prefs.edit().putLong("snooze_until_ms", ms).apply()
    }

    fun getState(): GuardState {
        synchronized(lock) {
            val raw = prefs.getString("cached_state", null)
            return try {
                if (raw.isNullOrBlank()) GuardState() else GuardState.fromJson(JSONObject(raw))
            } catch (_: Exception) {
                GuardState()
            }
        }
    }

    private fun saveLocalState(state: GuardState) {
        synchronized(lock) {
            prefs.edit().putString("cached_state", state.toJson().toString()).apply()
        }
    }

    fun updateState(next: GuardState, lv2ChallengePassed: Boolean = false): Pair<Boolean, String?> {
        val current = getState()
        val blockErr = RuleEngine.validateTransition(current, next, lv2ChallengePassed)
        if (blockErr != null) {
            return Pair(false, blockErr)
        }
        if (next.manualSession.active && next.manualSession.expiresAt > 0L && next.manualSession.expiresAt <= System.currentTimeMillis()) {
            next.manualSession.active = false
        }
        next.updatedAt = System.currentTimeMillis()
        saveLocalState(next)
        return pushToSupabase(next)
    }

    fun testConnection(cfg: SupabaseConfig): Pair<Boolean, String> {
        if (!cfg.isConfigured) {
            return Pair(false, "URL / Anon Key / Sync Key（8文字以上）を入力してください。")
        }
        return try {
            val body = JSONObject().put("p_sync_key", cfg.syncKey)
            val (code, resp) = postRpc(cfg, "focus_guard_get", body)
            if (code in 200..299) {
                Pair(true, "Supabase への接続に成功しました！")
            } else {
                Pair(false, "接続エラー (HTTP $code): $resp")
            }
        } catch (e: Exception) {
            Pair(false, "接続例外: ${e.message}")
        }
    }

    fun pullFromSupabase(pushIfEmpty: Boolean = false): Boolean {
        val cfg = getConfig()
        if (!cfg.isConfigured) {
            lastSyncMessage = "Supabase 未設定（オフライン稼働中）"
            lastSyncOk = true
            return false
        }

        // Expire manual session locally if already past expiresAt
        val curLocal = getState()
        if (curLocal.manualSession.active && curLocal.manualSession.expiresAt > 0L && curLocal.manualSession.expiresAt <= System.currentTimeMillis()) {
            curLocal.manualSession.active = false
            curLocal.updatedAt = System.currentTimeMillis()
            saveLocalState(curLocal)
            pushToSupabase(curLocal)
        }

        return try {
            val payload = JSONObject().put("p_sync_key", cfg.syncKey)
            val (code, resp) = postRpc(cfg, "focus_guard_get", payload)
            if (code !in 200..299) {
                lastSyncMessage = "同期取得エラー (HTTP $code)"
                lastSyncOk = false
                return false
            }

            val trimmed = resp.trim()
            if (trimmed.isEmpty() || trimmed == "null" || trimmed == "[]") {
                if (pushIfEmpty) {
                    pushToSupabase(getState())
                } else {
                    lastSyncMessage = "同期済み (${nowTimeStr()})"
                    lastSyncOk = true
                }
                return true
            }

            val root = JSONObject(trimmed)
            val stateObj = root.optJSONObject("state") ?: root
            val remote = GuardState.fromJson(stateObj)
            val local = getState()
            if (remote.updatedAt > local.updatedAt) {
                saveLocalState(remote)
            }
            lastSyncMessage = "Supabase 同期済み (${nowTimeStr()})"
            lastSyncOk = true
            true
        } catch (e: Exception) {
            lastSyncMessage = "オフライン (キャッシュ稼働中)"
            lastSyncOk = false
            false
        }
    }

    fun pushToSupabase(state: GuardState): Pair<Boolean, String?> {
        val cfg = getConfig()
        if (!cfg.isConfigured) {
            lastSyncMessage = "ローカル保存済み（Supabase未設定）"
            lastSyncOk = true
            return Pair(true, null)
        }
        return try {
            val payload = JSONObject()
                .put("p_sync_key", cfg.syncKey)
                .put("p_state", state.toJson())
            val (code, resp) = postRpc(cfg, "focus_guard_save", payload)
            if (code in 200..299) {
                lastSyncMessage = "Supabase 送信完了 (${nowTimeStr()})"
                lastSyncOk = true
                Pair(true, null)
            } else {
                lastSyncMessage = "送信エラー (HTTP $code)"
                lastSyncOk = false
                Pair(false, "Supabase 保存エラー ($code): $resp")
            }
        } catch (e: Exception) {
            lastSyncMessage = "送信失敗 (オフライン)"
            lastSyncOk = false
            Pair(false, "通信エラー: ${e.message}")
        }
    }

    fun sendHeartbeat() {
        val cfg = getConfig()
        if (!cfg.isConfigured) return
        try {
            val eff = RuleEngine.evaluate(getState(), "android", getSnoozeUntilMs())
            val status = when {
                !eff.isActive -> "idle"
                eff.isSnoozed -> "snoozed"
                else -> "blocking"
            }
            val payload = JSONObject()
                .put("p_sync_key", cfg.syncKey)
                .put("p_device", "android")
                .put("p_status", status)
                .put("p_level", eff.level)
            postRpc(cfg, "focus_guard_heartbeat", payload)
        } catch (_: Exception) {
        }
    }

    private fun postRpc(cfg: SupabaseConfig, rpcName: String, body: JSONObject): Pair<Int, String> {
        val endpoint = "${cfg.url.trimEnd('/')}/rest/v1/rpc/$rpcName"
        val conn = (URL(endpoint).openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            connectTimeout = 8000
            readTimeout = 8000
            doOutput = true
            setRequestProperty("Content-Type", "application/json; charset=utf-8")
            setRequestProperty("apikey", cfg.anonKey)
            setRequestProperty("Authorization", "Bearer ${cfg.anonKey}")
        }
        return try {
            OutputStreamWriter(conn.outputStream, Charsets.UTF_8).use { it.write(body.toString()) }
            val code = conn.responseCode
            val stream = if (code in 200..299) conn.inputStream else conn.errorStream
            val text = if (stream != null) {
                BufferedReader(InputStreamReader(stream, Charsets.UTF_8)).use { it.readText() }
            } else ""
            Pair(code, text)
        } finally {
            conn.disconnect()
        }
    }

    private fun nowTimeStr(): String = SimpleDateFormat("HH:mm:ss", Locale.getDefault()).format(Date())

    companion object {
        @Volatile
        private var instance: SyncRepository? = null

        fun getInstance(context: Context): SyncRepository {
            return instance ?: synchronized(this) {
                instance ?: SyncRepository(context).also { instance = it }
            }
        }
    }
}
