package com.focusguard.app

import android.app.AlertDialog
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.provider.Settings
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Spinner
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import java.util.concurrent.Executors

class MainActivity : AppCompatActivity() {

    private lateinit var repo: SyncRepository
    private val io = Executors.newSingleThreadExecutor()
    private val mainHandler = Handler(Looper.getMainLooper())

    private var selectedLevel = 2
    private val dayLabels = arrayOf("日", "月", "火", "水", "木", "金", "土")

    // Top compact warning (hidden once permissions are granted)
    private lateinit var scrollView: ScrollView
    private lateinit var permWarningBanner: TextView

    // 1. Live dual status cards
    private lateinit var txtPcLevel: TextView
    private lateinit var txtPcSub: TextView
    private lateinit var txtAndroidLevel: TextView
    private lateinit var txtAndroidSub: TextView
    private lateinit var txtSyncStatus: TextView
    private lateinit var btnEndSnooze: Button

    // 2. Remote controls
    private lateinit var btnLv1: Button
    private lateinit var btnLv2: Button
    private lateinit var btnLv3: Button
    private lateinit var spnTarget: Spinner
    private lateinit var spnDuration: Spinner
    private lateinit var btnStopManual: Button

    // 3. Schedules
    private lateinit var scheduleContainer: LinearLayout

    // 4. Bottom setup (Permissions + Supabase)
    private lateinit var setupSection: LinearLayout
    private lateinit var txtPermAccess: TextView
    private lateinit var btnPermAccess: Button
    private lateinit var txtPermOverlay: TextView
    private lateinit var btnPermOverlay: Button

    private lateinit var inHashImport: EditText
    private lateinit var inUrl: EditText
    private lateinit var inAnonKey: EditText
    private lateinit var inSyncKey: EditText

    private val uiTicker = object : Runnable {
        private var tickCount = 0
        override fun run() {
            refreshUiFromCache()
            tickCount++
            if (tickCount % 4 == 0) {
                io.execute {
                    repo.pullFromSupabase(false)
                    mainHandler.post { refreshUiFromCache() }
                }
            }
            mainHandler.postDelayed(this, 1000L)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        repo = SyncRepository.getInstance(this)
        buildUi()
        loadConfigToInputs()
        refreshPermissionCard()
        refreshUiFromCache()

        io.execute {
            repo.pullFromSupabase(true)
            mainHandler.post { refreshUiFromCache() }
        }
    }

    override fun onResume() {
        super.onResume()
        refreshPermissionCard()
        refreshUiFromCache()
        mainHandler.removeCallbacks(uiTicker)
        mainHandler.post(uiTicker)
    }

    override fun onPause() {
        mainHandler.removeCallbacks(uiTicker)
        super.onPause()
    }

    private fun buildUi() {
        scrollView = ScrollView(this).apply {
            setBackgroundColor(Color.parseColor("#0D1119"))
        }
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(14), dp(14), dp(14), dp(40))
        }
        scrollView.addView(root)

        // Compact Header row
        val headerRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            ).apply { bottomMargin = dp(10) }
        }
        val titleWrap = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        titleWrap.addView(TextView(this).apply {
            text = "FocusGuard"
            setTextColor(Color.WHITE)
            textSize = 21f
            setTypeface(null, Typeface.BOLD)
        })
        txtSyncStatus = TextView(this).apply {
            text = "同期待機中"
            setTextColor(Color.parseColor("#95A4BF"))
            textSize = 11.5f
        }
        titleWrap.addView(txtSyncStatus)

        val btnSyncMini = Button(this).apply {
            text = "同期"
            isAllCaps = false
            setTextColor(Color.parseColor("#CFE0FF"))
            textSize = 12.5f
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#1D283B"))
                cornerRadius = dp(9).toFloat()
                setStroke(dp(1), Color.parseColor("#2E3E5C"))
            }
            layoutParams = LinearLayout.LayoutParams(dp(64), dp(34))
            setOnClickListener {
                io.execute {
                    repo.pullFromSupabase(false)
                    repo.sendHeartbeat()
                    mainHandler.post {
                        refreshUiFromCache()
                        toast(repo.lastSyncMessage)
                    }
                }
            }
        }
        headerRow.addView(titleWrap)
        headerRow.addView(btnSyncMini)
        root.addView(headerRow)

        // Compact warning banner (ONLY visible when required permissions are OFF)
        permWarningBanner = TextView(this).apply {
            text = "⚠️ スマホのブロック権限が未設定です（タップして設定へ移動）"
            setTextColor(Color.parseColor("#FDE68A"))
            textSize = 12.5f
            setTypeface(null, Typeface.BOLD)
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#422D0E"))
                cornerRadius = dp(10).toFloat()
                setStroke(dp(1), Color.parseColor("#B45309"))
            }
            setPadding(dp(12), dp(10), dp(12), dp(10))
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            ).apply { bottomMargin = dp(12) }
            visibility = View.GONE
            setOnClickListener {
                scrollView.post { scrollView.smoothScrollTo(0, setupSection.top) }
            }
        }
        root.addView(permWarningBanner)

        // ============================================================
        // 1. 現在の稼働状況（2カラム・最優先で見やすく！）
        // ============================================================
        val statusCard = makeCard()
        statusCard.addView(makeSectionTitle("現在の稼働状況"))

        val dualRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            )
        }

        val pcBox = makeDeviceTile("Windows PC").also { box ->
            txtPcLevel = TextView(this).apply {
                text = "待機中"
                textSize = 16.5f
                setTypeface(null, Typeface.BOLD)
                setTextColor(Color.parseColor("#6EE7B7"))
                setPadding(0, dp(4), 0, dp(2))
            }
            txtPcSub = TextView(this).apply {
                text = "通常利用OK"
                textSize = 12f
                setTextColor(Color.parseColor("#95A4BF"))
            }
            box.addView(txtPcLevel)
            box.addView(txtPcSub)
        }
        (pcBox.layoutParams as LinearLayout.LayoutParams).marginEnd = dp(8)

        val androidBox = makeDeviceTile("Android スマホ").also { box ->
            txtAndroidLevel = TextView(this).apply {
                text = "待機中"
                textSize = 16.5f
                setTypeface(null, Typeface.BOLD)
                setTextColor(Color.parseColor("#6EE7B7"))
                setPadding(0, dp(4), 0, dp(2))
            }
            txtAndroidSub = TextView(this).apply {
                text = "通常利用OK"
                textSize = 12f
                setTextColor(Color.parseColor("#95A4BF"))
            }
            box.addView(txtAndroidLevel)
            box.addView(txtAndroidSub)
        }

        dualRow.addView(pcBox)
        dualRow.addView(androidBox)
        statusCard.addView(dualRow)

        btnEndSnooze = makeButton("一時許可を今すぐ終了", "#475569") {
            repo.setSnoozeUntilMs(0L)
            refreshUiFromCache()
            toast("一時許可を終了しました")
        }.apply { visibility = View.GONE }
        statusCard.addView(btnEndSnooze)
        root.addView(statusCard)

        // ============================================================
        // 2. 今すぐブロック開始（リモコン）
        // ============================================================
        val remoteCard = makeCard()
        remoteCard.addView(makeSectionTitle("今すぐブロック開始"))

        val lvRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            ).apply { bottomMargin = dp(10) }
        }
        btnLv1 = makeLevelBtn("Lv.1\n気づき", 1)
        btnLv2 = makeLevelBtn("Lv.2\nひと手間", 2)
        btnLv3 = makeLevelBtn("Lv.3\n強制", 3)
        lvRow.addView(btnLv1)
        lvRow.addView(btnLv2)
        lvRow.addView(btnLv3)
        remoteCard.addView(lvRow)
        updateLevelButtons()

        // Side-by-side Target & Duration selectors (saves vertical space!)
        val optRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            ).apply { bottomMargin = dp(6) }
        }
        val colTarget = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f).apply {
                marginEnd = dp(8)
            }
        }
        colTarget.addView(makeLabel("対象端末"))
        spnTarget = Spinner(this).apply {
            adapter = ArrayAdapter(
                this@MainActivity,
                android.R.layout.simple_spinner_dropdown_item,
                listOf("両方 (PC+スマホ)", "PC のみ", "スマホのみ")
            )
            background = makeSpinnerBg()
        }
        colTarget.addView(spnTarget)

        val colDur = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        colDur.addView(makeLabel("継続時間"))
        spnDuration = Spinner(this).apply {
            adapter = ArrayAdapter(
                this@MainActivity,
                android.R.layout.simple_spinner_dropdown_item,
                listOf("15分", "30分", "60分", "120分", "手動解除まで")
            )
            setSelection(1)
            background = makeSpinnerBg()
        }
        colDur.addView(spnDuration)

        optRow.addView(colTarget)
        optRow.addView(colDur)
        remoteCard.addView(optRow)

        val btnStart = makeButton("この内容でブロック開始", "#3B82F6") { startManualSession() }
        btnStopManual = makeButton("手動ブロックを停止", "#3E4A61") { stopManualSession() }
        remoteCard.addView(btnStart)
        remoteCard.addView(btnStopManual)
        root.addView(remoteCard)

        // ============================================================
        // 3. 時間帯スケジュール
        // ============================================================
        val schedCard = makeCard()
        schedCard.addView(makeSectionTitle("時間帯スケジュール"))
        scheduleContainer = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        schedCard.addView(scheduleContainer)
        val btnAddSched = makeButton("＋ 新しいスケジュールを追加", "#1F9D5D") { showAddScheduleDialog() }
        schedCard.addView(btnAddSched)
        root.addView(schedCard)

        // ============================================================
        // 4. 初期設定エリア（権限チェック ＆ Supabase 接続 — 一番下へ移動）
        // ============================================================
        setupSection = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }

        val permCard = makeCard()
        permCard.addView(makeSectionTitle("初期設定：スマホの権限"))
        txtPermAccess = makeSubText("")
        btnPermAccess = makeButton("「ユーザー補助」設定を開く", "#3B82F6") {
            startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
        }
        txtPermOverlay = makeSubText("")
        btnPermOverlay = makeButton("「他のアプリの上に表示」設定を開く", "#3B82F6") {
            val i = Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName"))
            startActivity(i)
        }
        val btnBattery = makeButton("バッテリー制限なし（推奨）", "#334155") {
            try {
                val pm = getSystemService(POWER_SERVICE) as PowerManager
                if (pm.isIgnoringBatteryOptimizations(packageName)) {
                    toast("すでにバッテリー制限なしになっています！")
                } else {
                    val i = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS).apply {
                        data = Uri.parse("package:$packageName")
                    }
                    startActivity(i)
                }
            } catch (_: Exception) {
                startActivity(Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS))
            }
        }
        permCard.addView(txtPermAccess)
        permCard.addView(btnPermAccess)
        permCard.addView(txtPermOverlay)
        permCard.addView(btnPermOverlay)
        permCard.addView(btnBattery)
        setupSection.addView(permCard)

        val cfgCard = makeCard()
        cfgCard.addView(makeSectionTitle("初期設定：Supabase 同期"))
        cfgCard.addView(makeSubText("PC の「スマホ設定用ハッシュをコピー」を貼ると自動入力されます。"))

        inHashImport = makeInput("#url=...&key=...&sync=... を貼り付け", false)
        val btnImport = makeButton("URL / ハッシュから一括取り込み", "#2C6B5C") {
            val parsed = repo.parseConfigFromHashOrUrl(inHashImport.text.toString())
            if (parsed == null) {
                toast("取り込めませんでした。#url=...&key=...&sync=... の形式を確認してください。")
            } else {
                inUrl.setText(parsed.url)
                inAnonKey.setText(parsed.anonKey)
                inSyncKey.setText(parsed.syncKey)
                repo.saveConfig(parsed)
                inHashImport.setText("")
                toast("取り込み完了！同期を開始します...")
                io.execute {
                    repo.pullFromSupabase(true)
                    repo.sendHeartbeat()
                    mainHandler.post { refreshUiFromCache() }
                }
            }
        }
        cfgCard.addView(inHashImport)
        cfgCard.addView(btnImport)

        cfgCard.addView(makeLabel("Supabase URL"))
        inUrl = makeInput("https://xxxx.supabase.co", false)
        cfgCard.addView(inUrl)

        cfgCard.addView(makeLabel("Anon Key"))
        inAnonKey = makeInput("eyJ...", true)
        cfgCard.addView(inAnonKey)

        cfgCard.addView(makeLabel("Sync Key（PCと共通の秘密鍵）"))
        inSyncKey = makeInput("8文字以上の秘密鍵", false)
        cfgCard.addView(inSyncKey)

        val btnTest = makeButton("接続テスト", "#334155") {
            val testCfg = SupabaseConfig(inUrl.text.toString(), inAnonKey.text.toString(), inSyncKey.text.toString())
            io.execute {
                val (_, msg) = repo.testConnection(testCfg)
                mainHandler.post { toast(msg) }
            }
        }
        val btnSaveCfg = makeButton("設定を保存して同期", "#3B82F6") {
            val newCfg = SupabaseConfig(inUrl.text.toString(), inAnonKey.text.toString(), inSyncKey.text.toString())
            if (!newCfg.isConfigured) {
                toast("URL / Anon Key / Sync Key（8文字以上）を入力してください。")
                return@makeButton
            }
            repo.saveConfig(newCfg)
            io.execute {
                repo.pullFromSupabase(true)
                repo.sendHeartbeat()
                mainHandler.post {
                    refreshUiFromCache()
                    toast("保存して同期しました！")
                }
            }
        }
        cfgCard.addView(btnTest)
        cfgCard.addView(btnSaveCfg)
        setupSection.addView(cfgCard)

        root.addView(setupSection)
        setContentView(scrollView)
    }

    private fun refreshPermissionCard() {
        val accOk = isAccessibilityEnabled()
        val ovOk = Settings.canDrawOverlays(this)

        permWarningBanner.visibility = if (accOk && ovOk) View.GONE else View.VISIBLE

        txtPermAccess.text = if (accOk) "✅ ユーザー補助（ブラウザ検知）: 有効" else "⚠️ ユーザー補助（ブラウザ検知）: 未設定（必須）"
        txtPermAccess.setTextColor(if (accOk) Color.parseColor("#6EE7B7") else Color.parseColor("#FCD34D"))
        btnPermAccess.visibility = if (accOk) View.GONE else View.VISIBLE

        txtPermOverlay.text = if (ovOk) "✅ 他のアプリの上に表示（待機画面）: 許可済み" else "⚠️ 他のアプリの上に表示: 未設定（Lv.1/Lv.2に必要）"
        txtPermOverlay.setTextColor(if (ovOk) Color.parseColor("#6EE7B7") else Color.parseColor("#FCD34D"))
        btnPermOverlay.visibility = if (ovOk) View.GONE else View.VISIBLE
    }

    private fun isAccessibilityEnabled(): Boolean {
        if (BrowserBlockerService.isRunning) return true
        val enabled = Settings.Secure.getString(contentResolver, Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES) ?: return false
        return enabled.contains(packageName, ignoreCase = true)
    }

    private fun loadConfigToInputs() {
        val cfg = repo.getConfig()
        inUrl.setText(cfg.url)
        inAnonKey.setText(cfg.anonKey)
        inSyncKey.setText(cfg.syncKey)
    }

    private fun refreshUiFromCache() {
        val st = repo.getState()
        val effPc = RuleEngine.evaluate(st, "pc", 0L)
        val effAn = RuleEngine.evaluate(st, "android", repo.getSnoozeUntilMs())

        // PC tile
        if (effPc.isActive) {
            txtPcLevel.text = "Lv.${effPc.level} 稼働中"
            txtPcLevel.setTextColor(levelColor(effPc.level))
            txtPcSub.text = "残り ${effPc.remainingText()}\n${effPc.reason}"
        } else {
            txtPcLevel.text = "待機中"
            txtPcLevel.setTextColor(Color.parseColor("#6EE7B7"))
            txtPcSub.text = "通常利用OK"
        }

        // Android tile
        when {
            !effAn.isActive -> {
                txtAndroidLevel.text = "待機中"
                txtAndroidLevel.setTextColor(Color.parseColor("#6EE7B7"))
                txtAndroidSub.text = "通常利用OK"
            }
            effAn.isSnoozed -> {
                txtAndroidLevel.text = "一時許可中"
                txtAndroidLevel.setTextColor(Color.parseColor("#93C5FD"))
                txtAndroidSub.text = "残り ${effAn.snoozeRemainingSeconds} 秒\n(元: Lv.${effAn.level})"
            }
            else -> {
                txtAndroidLevel.text = "Lv.${effAn.level} 稼働中"
                txtAndroidLevel.setTextColor(levelColor(effAn.level))
                txtAndroidSub.text = "残り ${effAn.remainingText()}\n${effAn.reason}"
            }
        }

        btnEndSnooze.visibility = if (effAn.isSnoozed) View.VISIBLE else View.GONE

        txtSyncStatus.text = repo.lastSyncMessage
        txtSyncStatus.setTextColor(if (repo.lastSyncOk) Color.parseColor("#95A4BF") else Color.parseColor("#FCA5A5"))

        val locked = effPc.isHardLocked || effAn.isHardLocked
        val lv2Active = !locked && ((effPc.isActive && effPc.level == 2) || (effAn.isActive && effAn.level == 2))
        btnStopManual.isEnabled = !locked
        btnStopManual.alpha = if (locked) 0.45f else 1.0f
        btnStopManual.text = when {
            locked -> "Lv.3 ハードロック中（停止不可）"
            lv2Active -> "手動ブロックを停止（確認入力）"
            else -> "手動ブロックを停止"
        }

        renderSchedules(st)
    }

    private fun renderSchedules(st: GuardState) {
        scheduleContainer.removeAllViews()
        if (st.schedules.isEmpty()) {
            scheduleContainer.addView(makeSubText("スケジュールはまだありません。"))
            return
        }
        for (rule in st.schedules) {
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                background = GradientDrawable().apply {
                    setColor(Color.parseColor("#111722"))
                    cornerRadius = dp(10).toFloat()
                    setStroke(dp(1), Color.parseColor("#243045"))
                }
                setPadding(dp(12), dp(10), dp(10), dp(10))
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                ).apply { bottomMargin = dp(8) }
                alpha = if (rule.enabled) 1f else 0.55f
            }

            val daysStr = if (rule.days.size == 7) "毎日"
            else rule.days.sorted().filter { it in 0..6 }.joinToString("") { dayLabels[it] }
            val targetStr = when (rule.target) {
                "pc" -> "PC"
                "android" -> "スマホ"
                else -> "両方"
            }

            val info = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
            }
            info.addView(TextView(this).apply {
                text = "${rule.name}  Lv.${rule.level}"
                setTextColor(Color.WHITE)
                textSize = 14f
                setTypeface(null, Typeface.BOLD)
            })
            info.addView(TextView(this).apply {
                text = "${rule.startTime}-${rule.endTime} / $daysStr / $targetStr"
                setTextColor(Color.parseColor("#95A4BF"))
                textSize = 11.5f
            })

            val actions = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
            val btnToggle = makeSmallButton(if (rule.enabled) "ON" else "OFF", if (rule.enabled) "#1F9D5D" else "#475569") {
                val next = repo.getState().deepCopy()
                next.schedules.find { it.id == rule.id }?.let { it.enabled = !it.enabled }
                applyScheduleChangeWithLv2Check(next, "稼働中の Lv.2 スケジュールを OFF にします。")
            }
            val btnDel = makeSmallButton("削除", "#7F2933") {
                val next = repo.getState().deepCopy()
                next.schedules.removeAll { it.id == rule.id }
                applyScheduleChangeWithLv2Check(next, "稼働中の Lv.2 スケジュールを削除します。")
            }
            actions.addView(btnToggle)
            actions.addView(btnDel)

            row.addView(info)
            row.addView(actions)
            scheduleContainer.addView(row)
        }
    }

    private fun startManualSession() {
        val target = when (spnTarget.selectedItemPosition) {
            1 -> "pc"
            2 -> "android"
            else -> "all"
        }
        val minsArr = intArrayOf(15, 30, 60, 120, 0)
        var mins = minsArr[spnDuration.selectedItemPosition]

        if (selectedLevel == 3 && mins == 0) {
            mins = 60
            toast("Lv.3 は安全のため 60分タイマー で開始します")
        }

        val next = repo.getState().deepCopy()
        val now = System.currentTimeMillis()
        next.manualSession = ManualSession(
            active = true,
            level = selectedLevel,
            target = target,
            startedAt = now,
            expiresAt = if (mins > 0) now + mins * 60_000L else 0L
        )
        repo.setSnoozeUntilMs(0L)
        pushUpdatedState(next, "ブロックを開始しました！")
    }

    private fun stopManualSession() {
        val cur = repo.getState()
        val effPc = RuleEngine.evaluate(cur, "pc", 0L)
        val effAn = RuleEngine.evaluate(cur, "android", 0L)
        if (effPc.isHardLocked || effAn.isHardLocked) {
            toast("Lv.3 ハードブロック中のため、終了時刻まで停止できません。")
            return
        }
        val needsLv2 = (effPc.isActive && effPc.level == 2) || (effAn.isActive && effAn.level == 2)
        if (needsLv2) {
            showLv2ChallengeDialog("Lv.2 ブロックを停止するには下のフレーズを手入力してください：") {
                val next = repo.getState().deepCopy()
                next.manualSession.active = false
                next.manualSession.expiresAt = 0L
                pushUpdatedState(next, "手動ブロックを停止しました", lv2ChallengePassed = true)
            }
            return
        }
        val next = cur.deepCopy()
        next.manualSession.active = false
        next.manualSession.expiresAt = 0L
        pushUpdatedState(next, "手動ブロックを停止しました", lv2ChallengePassed = false)
    }

    private fun applyScheduleChangeWithLv2Check(proposed: GuardState, msg: String) {
        val cur = repo.getState()
        val errNo = RuleEngine.validateTransition(cur, proposed, lv2ChallengePassed = false)
        if (errNo == null) {
            pushUpdatedState(proposed, "スケジュールを更新しました", lv2ChallengePassed = false)
            return
        }
        val errHard = RuleEngine.validateTransition(cur, proposed, lv2ChallengePassed = true)
        if (errHard != null) {
            toast(errHard)
            return
        }
        showLv2ChallengeDialog(msg) {
            pushUpdatedState(proposed, "スケジュールを更新しました", lv2ChallengePassed = true)
        }
    }

    private fun showLv2ChallengeDialog(message: String, onSuccess: () -> Unit) {
        val phrases = listOf("confirm stop focus", "choose mindful break", "unlock intentionally", "pause focus session")
        val target = "${phrases.random()} ${(100..999).random()}"

        val box = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(14), dp(20), dp(8))
        }
        val lblMsg = TextView(this).apply { text = message; setPadding(0, 0, 0, dp(8)) }
        val lblPhrase = TextView(this).apply {
            text = target
            textSize = 17f
            typeface = Typeface.MONOSPACE
            setTextColor(Color.parseColor("#FFAA6E"))
            setPadding(0, dp(4), 0, dp(10))
        }
        val input = EditText(this).apply {
            hint = "フレーズを入力..."
            isLongClickable = false
            setTextIsSelectable(false)
        }
        box.addView(lblMsg)
        box.addView(lblPhrase)
        box.addView(input)

        AlertDialog.Builder(this)
            .setTitle("Lv.2 確認フレーズ")
            .setView(box)
            .setPositiveButton("確認して実行") { _, _ ->
                if (input.text.toString().trim() == target) {
                    onSuccess()
                } else {
                    toast("フレーズが一致しませんでした")
                }
            }
            .setNegativeButton("キャンセル", null)
            .show()
    }

    private fun showAddScheduleDialog() {
        val box = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(14), dp(20), dp(8))
        }
        val inName = EditText(this).apply { setText("夜間デジタルデトックス"); hint = "ルール名" }
        val inStart = EditText(this).apply { setText("23:00"); hint = "開始 (例: 23:00)" }
        val inEnd = EditText(this).apply { setText("07:00"); hint = "終了 (例: 07:00)" }
        val spnLv = Spinner(this).apply {
            adapter = ArrayAdapter(this@MainActivity, android.R.layout.simple_spinner_dropdown_item, listOf("Lv.1 気づき", "Lv.2 ひと手間", "Lv.3 強制"))
            setSelection(1)
        }
        val spnTgt = Spinner(this).apply {
            adapter = ArrayAdapter(this@MainActivity, android.R.layout.simple_spinner_dropdown_item, listOf("PC + スマホ両方", "PCのみ", "スマホのみ"))
        }

        val dayChecks = Array(7) { idx ->
            CheckBox(this).apply { text = dayLabels[idx]; isChecked = true }
        }
        val dayRow1 = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        val dayRow2 = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        for (i in 0..3) dayRow1.addView(dayChecks[i])
        for (i in 4..6) dayRow2.addView(dayChecks[i])

        box.addView(TextView(this).apply { text = "ルール名" })
        box.addView(inName)
        box.addView(TextView(this).apply { text = "開始時刻 (HH:mm)" })
        box.addView(inStart)
        box.addView(TextView(this).apply { text = "終了時刻 (HH:mm / 日跨ぎOK)" })
        box.addView(inEnd)
        box.addView(TextView(this).apply { text = "強度" })
        box.addView(spnLv)
        box.addView(TextView(this).apply { text = "対象" })
        box.addView(spnTgt)
        box.addView(TextView(this).apply { text = "曜日" })
        box.addView(dayRow1)
        box.addView(dayRow2)

        AlertDialog.Builder(this)
            .setTitle("新しいスケジュールを追加")
            .setView(box)
            .setPositiveButton("追加") { _, _ ->
                val days = mutableListOf<Int>()
                dayChecks.forEachIndexed { idx, cb -> if (cb.isChecked) days.add(idx) }
                if (days.isEmpty()) {
                    toast("曜日を1つ以上選んでください")
                    return@setPositiveButton
                }
                val rule = ScheduleRule(
                    name = inName.text.toString().trim().ifBlank { "集中タイム" },
                    enabled = true,
                    days = days,
                    startTime = inStart.text.toString().trim(),
                    endTime = inEnd.text.toString().trim(),
                    level = spnLv.selectedItemPosition + 1,
                    target = when (spnTgt.selectedItemPosition) {
                        1 -> "pc"
                        2 -> "android"
                        else -> "all"
                    }
                )
                val next = repo.getState().deepCopy()
                next.schedules.add(rule)
                pushUpdatedState(next, "スケジュールを追加しました")
            }
            .setNegativeButton("キャンセル", null)
            .show()
    }

    private fun pushUpdatedState(next: GuardState, successToast: String = "更新しました", lv2ChallengePassed: Boolean = false) {
        io.execute {
            val (ok, err) = repo.updateState(next, lv2ChallengePassed)
            mainHandler.post {
                refreshUiFromCache()
                if (ok) toast(successToast) else toast(err ?: "更新できませんでした")
            }
        }
    }

    private fun updateLevelButtons() {
        val btns = listOf(Pair(btnLv1, 1), Pair(btnLv2, 2), Pair(btnLv3, 3))
        for ((b, lv) in btns) {
            val active = selectedLevel == lv
            val borderCol = if (active) levelColor(lv) else Color.parseColor("#2B384F")
            val bgCol = if (active) Color.parseColor("#243147") else Color.parseColor("#111722")
            b.background = GradientDrawable().apply {
                setColor(bgCol)
                cornerRadius = dp(10).toFloat()
                setStroke(dp(2), borderCol)
            }
            b.setTextColor(if (active) levelColor(lv) else Color.parseColor("#B5C0D4"))
        }
    }

    private fun levelColor(lv: Int): Int = when (lv) {
        1 -> Color.parseColor("#F5B942")
        2 -> Color.parseColor("#F57642")
        3 -> Color.parseColor("#EF4455")
        else -> Color.parseColor("#6EE7B7")
    }

    private fun makeDeviceTile(deviceTitle: String): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        background = GradientDrawable().apply {
            setColor(Color.parseColor("#111722"))
            cornerRadius = dp(10).toFloat()
            setStroke(dp(1), Color.parseColor("#243045"))
        }
        setPadding(dp(12), dp(10), dp(12), dp(10))
        layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        addView(TextView(this@MainActivity).apply {
            text = deviceTitle
            textSize = 11.5f
            setTextColor(Color.parseColor("#8FA0BE"))
            setTypeface(null, Typeface.BOLD)
        })
    }

    private fun makeSpinnerBg(): GradientDrawable = GradientDrawable().apply {
        setColor(Color.parseColor("#111722"))
        cornerRadius = dp(9).toFloat()
        setStroke(dp(1), Color.parseColor("#2C3952"))
    }

    private fun makeLevelBtn(label: String, lv: Int): Button = Button(this).apply {
        text = label
        isAllCaps = false
        textSize = 13.5f
        setTypeface(null, Typeface.BOLD)
        layoutParams = LinearLayout.LayoutParams(0, dp(56), 1f).apply {
            marginEnd = if (lv < 3) dp(6) else 0
        }
        setOnClickListener {
            selectedLevel = lv
            updateLevelButtons()
        }
    }

    private fun makeCard(): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        background = GradientDrawable().apply {
            setColor(Color.parseColor("#171E2B"))
            cornerRadius = dp(14).toFloat()
            setStroke(dp(1), Color.parseColor("#263147"))
        }
        setPadding(dp(14), dp(12), dp(14), dp(14))
        layoutParams = LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            LinearLayout.LayoutParams.WRAP_CONTENT
        ).apply { bottomMargin = dp(12) }
    }

    private fun makeSectionTitle(t: String): TextView = TextView(this).apply {
        text = t
        setTextColor(Color.parseColor("#E2EAF7"))
        textSize = 15f
        setTypeface(null, Typeface.BOLD)
        setPadding(0, 0, 0, dp(8))
    }

    private fun makeLabel(t: String): TextView = TextView(this).apply {
        text = t
        setTextColor(Color.parseColor("#95A4BF"))
        textSize = 11.5f
        setPadding(0, dp(4), 0, dp(4))
    }

    private fun makeSubText(t: String): TextView = TextView(this).apply {
        text = t
        setTextColor(Color.parseColor("#95A4BF"))
        textSize = 12f
        setPadding(0, dp(2), 0, dp(6))
    }

    private fun makeInput(hintText: String, password: Boolean): EditText = EditText(this).apply {
        hint = hintText
        setHintTextColor(Color.parseColor("#64748B"))
        setTextColor(Color.WHITE)
        textSize = 14f
        if (password) {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        background = GradientDrawable().apply {
            setColor(Color.parseColor("#101622"))
            cornerRadius = dp(9).toFloat()
            setStroke(dp(1), Color.parseColor("#2C3952"))
        }
        setPadding(dp(12), dp(10), dp(12), dp(10))
    }

    private fun makeButton(label: String, hexBg: String, onClick: () -> Unit): Button = Button(this).apply {
        text = label
        isAllCaps = false
        setTextColor(Color.WHITE)
        textSize = 14f
        setTypeface(null, Typeface.BOLD)
        background = GradientDrawable().apply {
            setColor(Color.parseColor(hexBg))
            cornerRadius = dp(10).toFloat()
        }
        layoutParams = LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            dp(44)
        ).apply { topMargin = dp(8) }
        setOnClickListener { onClick() }
    }

    private fun makeSmallButton(label: String, hexBg: String, onClick: () -> Unit): Button = Button(this).apply {
        text = label
        isAllCaps = false
        setTextColor(Color.WHITE)
        textSize = 11.5f
        background = GradientDrawable().apply {
            setColor(Color.parseColor(hexBg))
            cornerRadius = dp(8).toFloat()
        }
        layoutParams = LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.WRAP_CONTENT,
            dp(34)
        ).apply { marginStart = dp(6) }
        setPadding(dp(10), 0, dp(10), 0)
        setOnClickListener { onClick() }
    }

    private fun toast(msg: String) {
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
    }

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()
}
