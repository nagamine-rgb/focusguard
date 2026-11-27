package com.focusguard.app

import android.accessibilityservice.AccessibilityService
import android.content.Intent
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import android.widget.Toast
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.TimeUnit

class BrowserBlockerService : AccessibilityService() {

    private lateinit var repo: SyncRepository
    private val mainHandler = Handler(Looper.getMainLooper())
    private var scheduler: ScheduledExecutorService? = null

    @Volatile
    private var currentForegroundPkg: String = ""
    private var lastTriggerAtMs: Long = 0L
    private var lastHardToastAtMs: Long = 0L
    private var pollTick: Int = 0
    private var lastContentCheckAtMs: Long = 0L
    /** 最後に読めたサイト（ブラウザごと）。アドレスバーが一時的に隠れたときに使う */
    private val lastHostByPkg = mutableMapOf<String, String?>()
    /** アドレスバーの入力中として止めずにおく最長時間（ずっと入力欄に触れたままの抜け道を防ぐ） */
    private val editingSinceByPkg = mutableMapOf<String, Long>()
    private val unreadableStreakByPkg = mutableMapOf<String, Int>()
    /** 止める対象のアプリ（小文字）。イベントのたびに状態を読み直さないよう、ここに覚えておく */
    @Volatile
    private var targetPkgs: Set<String> = emptySet()

    override fun onServiceConnected() {
        super.onServiceConnected()
        repo = SyncRepository.getInstance(this)
        refreshTargets()
        isRunning = true
        instance = this

        scheduler?.shutdownNow()
        scheduler = Executors.newSingleThreadScheduledExecutor().apply {
            scheduleWithFixedDelay({
                try {
                    pollTick++
                    repo.pullFromSupabase(false)
                    refreshTargets()
                    if (pollTick % 4 == 0) {
                        repo.sendHeartbeat()
                    }
                    // ブロック開始時にすでに開いているアプリも止める。
                    // 通知やキーボードのイベントで「いま前にあるアプリ」がずれることがあるので、実際の画面から確かめる
                    mainHandler.post {
                        val fg = activePackage() ?: currentForegroundPkg
                        if (fg.isNotEmpty()) {
                            currentForegroundPkg = fg
                            enforceForPackage(fg, fromPoll = true)
                        }
                    }
                } catch (_: Exception) {
                }
            }, 1, 5, TimeUnit.SECONDS)
        }
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        if (event == null) return
        val pkg = event.packageName?.toString()?.trim() ?: return
        if (pkg.isEmpty()) return

        // 止める対象のアプリの画面が動いたときも確認する
        // （ブラウザのページ移動、ブロック前から開いていたアプリ、ホーム以外から戻ってきたアプリなど）
        if (event.eventType == AccessibilityEvent.TYPE_WINDOW_CONTENT_CHANGED) {
            if (pkg == packageName || pkg.lowercase() !in targetPkgs) return
            val now = System.currentTimeMillis()
            if (now - lastContentCheckAtMs < 700L) return
            lastContentCheckAtMs = now
            if (activePackage() != pkg) return
            currentForegroundPkg = pkg
            enforceForPackage(pkg, fromPoll = true)
            return
        }
        if (event.eventType != AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) return

        // Ignore system overlays / keyboard / launcher transitions without clobbering state wrongly
        if (pkg == packageName) {
            currentForegroundPkg = pkg
            return
        }
        currentForegroundPkg = pkg
        enforceForPackage(pkg, fromPoll = false)
    }

    private fun enforceForPackage(pkg: String, fromPoll: Boolean) {
        if (!::repo.isInitialized) return
        if (pkg == packageName) return

        val state = repo.getState()
        targetPkgs = state.browsers.android.map { it.trim().lowercase() }.toSet()
        val isTargetBrowser = state.browsers.android.any { it.equals(pkg, ignoreCase = true) }
        if (!isTargetBrowser) return

        val eff = RuleEngine.evaluate(state, "android", repo.getSnoozeUntilMs())
        if (!eff.isActive || eff.isSnoozed) return

        // ホワイトリスト運用中: URL を読めるブラウザでは、許可したサイトと新しいタブは止めない
        val sites = SiteAllowlist.normalizeList(state.allowedSites)
        var blockedHost: String? = null
        if (sites.isNotEmpty() && SiteAllowlist.urlBarIds.containsKey(pkg)) {
            val read = readUrlBar(pkg)
            val nowMs = System.currentTimeMillis()
            if (read.editing) {
                val since = editingSinceByPkg.getOrPut(pkg) { nowMs }
                if (nowMs - since < 20_000L) return
            } else {
                editingSinceByPkg.remove(pkg)
            }
            if (read.found && !read.editing) {
                unreadableStreakByPkg[pkg] = 0
                lastHostByPkg[pkg] = read.host
                val host = read.host ?: return          // 新しいタブ
                if (SiteAllowlist.isAllowed(host, sites)) return
                blockedHost = host
            } else if (!read.found) {
                // 読めないときは、少し待ってから安全側（アプリごと止める）に倒す
                val streak = (unreadableStreakByPkg[pkg] ?: 0) + 1
                unreadableStreakByPkg[pkg] = streak
                if (streak < 3) return
                // ここまで来たら blockedHost = null のまま → 従来どおりアプリごと止める
            }
            // 入力中が長すぎるときも blockedHost = null → アプリごと止める
        }

        val now = System.currentTimeMillis()
        if (now - lastTriggerAtMs < 900L) return
        lastTriggerAtMs = now

        val appLabel = blockedHost ?: try {
            val ai = packageManager.getApplicationInfo(pkg, 0)
            packageManager.getApplicationLabel(ai)?.toString()?.ifBlank { pkg } ?: pkg
        } catch (_: Exception) {
            pkg
        }

        if (blockedHost != null && eff.level >= 3) {
            // Lv.3 でも許可サイトは使えるように、ホームではなく前のページへ戻す
            performGlobalAction(GLOBAL_ACTION_BACK)
            if (now - lastHardToastAtMs > 2500L) {
                lastHardToastAtMs = now
                Toast.makeText(
                    applicationContext,
                    "Lv.3 強制ブロック中: 「$appLabel」は開けません。許可したサイトだけ使えます",
                    Toast.LENGTH_SHORT
                ).show()
            }
            return
        }

        if (eff.level >= 3) {
            // Lv.3: Hard block - immediately return to Home screen
            performGlobalAction(GLOBAL_ACTION_HOME)
            currentForegroundPkg = ""
            if (now - lastHardToastAtMs > 2500L) {
                lastHardToastAtMs = now
                Toast.makeText(
                    applicationContext,
                    "Lv.3 強制ブロック中: 「$appLabel」（残り: ${eff.remainingText()}）",
                    Toast.LENGTH_SHORT
                ).show()
            }
            return
        }

        // Lv.1 / Lv.2: Launch fullscreen friction activity
        if (BlockActivity.isShowing) return

        val canOverlay = Settings.canDrawOverlays(this)
        if (!canOverlay) {
            // Fallback if overlay permission not granted yet
            performGlobalAction(GLOBAL_ACTION_HOME)
            currentForegroundPkg = ""
            if (!fromPoll || now - lastHardToastAtMs > 4000L) {
                lastHardToastAtMs = now
                Toast.makeText(
                    applicationContext,
                    "FocusGuard: 「$appLabel」をブロック中（「他のアプリの上に表示」を許可すると待機画面が出ます）",
                    Toast.LENGTH_LONG
                ).show()
            }
            return
        }

        val intent = Intent(this, BlockActivity::class.java).apply {
            addFlags(
                Intent.FLAG_ACTIVITY_NEW_TASK or
                    Intent.FLAG_ACTIVITY_CLEAR_TOP or
                    Intent.FLAG_ACTIVITY_SINGLE_TOP
            )
            putExtra("level", eff.level)
            putExtra("reason", eff.reason)
            putExtra("remaining", eff.remainingText())
            putExtra("browserPkg", pkg)
            putExtra("appLabel", appLabel)
            putExtra("lv1WaitSec", state.lv1WaitSeconds)
            putExtra("lv1AllowMin", state.lv1AllowMinutes)
            putExtra("lv2AllowMin", state.lv2AllowMinutes)
            putExtra("siteMode", blockedHost != null)
        }
        try {
            startActivity(intent)
        } catch (_: Exception) {
            performGlobalAction(GLOBAL_ACTION_HOME)
        }
    }

    private fun refreshTargets() {
        if (!::repo.isInitialized) return
        targetPkgs = repo.getState().browsers.android.map { it.trim().lowercase() }.toSet()
    }

    /** いま操作できる画面（アクティブなウィンドウ）のアプリ */
    private fun activePackage(): String? = try {
        rootInActiveWindow?.packageName?.toString()?.takeIf { it.isNotBlank() }
    } catch (_: Exception) {
        null
    }

    private data class UrlRead(val found: Boolean, val host: String?, val editing: Boolean)

    /** アドレスバーの URL を読む。入力中（フォーカスあり）なら editing */
    private fun readUrlBar(pkg: String): UrlRead {
        val root = try { rootInActiveWindow } catch (_: Exception) { null } ?: return UrlRead(false, null, false)
        try {
            for (id in SiteAllowlist.urlBarIds[pkg] ?: emptyList()) {
                val nodes = root.findAccessibilityNodeInfosByViewId(id) ?: continue
                val node: AccessibilityNodeInfo = nodes.firstOrNull() ?: continue
                if (node.isFocused) return UrlRead(true, null, true)
                val text = if (node.isShowingHintText) "" else node.text?.toString() ?: ""
                return UrlRead(true, SiteAllowlist.hostFromUrlBar(text), false)
            }
        } catch (_: Exception) {
        }
        return UrlRead(false, null, false)
    }

    /** ブロック画面で「やめる」を押したとき: ブラウザの前のページへ戻す */
    fun goBackInBrowser() {
        mainHandler.postDelayed({ performGlobalAction(GLOBAL_ACTION_BACK) }, 350L)
    }

    override fun onInterrupt() {
        // Nothing special needed
    }

    override fun onDestroy() {
        isRunning = false
        if (instance === this) instance = null
        scheduler?.shutdownNow()
        scheduler = null
        super.onDestroy()
    }

    companion object {
        @Volatile
        var isRunning: Boolean = false

        @Volatile
        var instance: BrowserBlockerService? = null
    }
}
