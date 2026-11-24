package com.focusguard.app

import android.accessibilityservice.AccessibilityService
import android.content.Intent
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.view.accessibility.AccessibilityEvent
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

    override fun onServiceConnected() {
        super.onServiceConnected()
        repo = SyncRepository.getInstance(this)
        isRunning = true

        scheduler?.shutdownNow()
        scheduler = Executors.newSingleThreadScheduledExecutor().apply {
            scheduleWithFixedDelay({
                try {
                    pollTick++
                    repo.pullFromSupabase(false)
                    if (pollTick % 4 == 0) {
                        repo.sendHeartbeat()
                    }
                    // Also enforce if user already has a browser open when PC triggers a block!
                    val fg = currentForegroundPkg
                    if (fg.isNotEmpty()) {
                        mainHandler.post { enforceForPackage(fg, fromPoll = true) }
                    }
                } catch (_: Exception) {
                }
            }, 1, 5, TimeUnit.SECONDS)
        }
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        if (event == null) return
        if (event.eventType != AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED) return

        val pkg = event.packageName?.toString()?.trim() ?: return
        if (pkg.isEmpty()) return

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
        val isTargetBrowser = state.browsers.android.any { it.equals(pkg, ignoreCase = true) }
        if (!isTargetBrowser) return

        val eff = RuleEngine.evaluate(state, "android", repo.getSnoozeUntilMs())
        if (!eff.isActive || eff.isSnoozed) return

        val now = System.currentTimeMillis()
        if (now - lastTriggerAtMs < 900L) return
        lastTriggerAtMs = now

        val appLabel = try {
            val ai = packageManager.getApplicationInfo(pkg, 0)
            packageManager.getApplicationLabel(ai)?.toString()?.ifBlank { pkg } ?: pkg
        } catch (_: Exception) {
            pkg
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
        }
        try {
            startActivity(intent)
        } catch (_: Exception) {
            performGlobalAction(GLOBAL_ACTION_HOME)
        }
    }

    override fun onInterrupt() {
        // Nothing special needed
    }

    override fun onDestroy() {
        isRunning = false
        scheduler?.shutdownNow()
        scheduler = null
        super.onDestroy()
    }

    companion object {
        @Volatile
        var isRunning: Boolean = false
    }
}
