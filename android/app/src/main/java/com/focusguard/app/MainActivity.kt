package com.focusguard.app

import android.app.AlertDialog
import android.content.Intent
import android.content.res.ColorStateList
import android.graphics.Color
import android.graphics.Typeface
import android.net.Uri
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.provider.Settings
import android.text.Editable
import android.text.InputType
import android.text.TextWatcher
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
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
import androidx.appcompat.widget.SwitchCompat
import java.util.Calendar
import java.util.concurrent.Executors

class MainActivity : AppCompatActivity() {

    private lateinit var repo: SyncRepository
    private val io = Executors.newSingleThreadExecutor()
    private val mainHandler = Handler(Looper.getMainLooper())

    private var selectedLevel = 2
    private var selectedDuration = 30
    private var selectedTarget = "all"
    private val dayLabels = arrayOf("日", "月", "火", "水", "木", "金", "土")
    private val durValues = intArrayOf(15, 30, 60, 120, 0)
    private val durLabels = arrayOf("15分", "30分", "1時間", "2時間", "手動で止めるまで")
    private val tgtValues = arrayOf("all", "pc", "android")
    private val tgtLabels = arrayOf("両方", "PCだけ", "スマホだけ")
    private val lvDescs = arrayOf(
        "15秒待てば、5分だけ開けられます。",
        "表示された名言を手で打てば、10分だけ開けられます。",
        "すぐに閉じます。終わる時刻までだれも止められません。"
    )

    private lateinit var scrollView: ScrollView
    private lateinit var permWarningBanner: TextView

    // 文字盤と端末の状態
    private lateinit var dial: DialView
    private lateinit var legendNow: View
    private lateinit var txtPcLevel: TextView
    private lateinit var txtPcSub: TextView
    private lateinit var txtAndroidLevel: TextView
    private lateinit var txtAndroidSub: TextView
    private lateinit var txtSyncStatus: TextView
    private lateinit var btnEndSnooze: Button

    // リモコン
    private lateinit var lockCard: LinearLayout
    private val levelButtons = mutableListOf<LinearLayout>()
    private val durChips = mutableListOf<TextView>()
    private val tgtSegs = mutableListOf<TextView>()
    private lateinit var txtLvDesc: TextView
    private lateinit var txtLv3Warn: TextView
    private lateinit var btnStart: Button
    private lateinit var btnStopManual: Button

    // スケジュールと止めるアプリ
    private lateinit var scheduleContainer: LinearLayout
    private lateinit var txtTargetAppsSummary: TextView
    private lateinit var inSites: EditText
    private var scheduleSignature: String? = null

    // 初期設定（権限と Supabase）
    private lateinit var setupSection: LinearLayout
    private lateinit var txtPermAccess: TextView
    private lateinit var btnPermAccess: Button
    private lateinit var txtPermOverlay: TextView
    private lateinit var btnPermOverlay: Button

    private lateinit var inHashImport: EditText
    private lateinit var inUrl: EditText
    private lateinit var inAnonKey: EditText
    private lateinit var inSyncKey: EditText

    private var builtLocked: Boolean? = null

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
        rebuildUi(isLockedNow())

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

    private fun isLockedNow(): Boolean {
        val st = repo.getState()
        return RuleEngine.evaluate(st, "pc", 0L).isHardLocked || RuleEngine.evaluate(st, "android", 0L).isHardLocked
    }

    /** Lv.3 のロック状態が変わったら、配色ごと画面を作り直す */
    private fun rebuildUi(locked: Boolean) {
        val scrollY = if (::scrollView.isInitialized) scrollView.scrollY else 0
        Fg.locked = locked
        builtLocked = locked
        scheduleSignature = null
        buildUi()
        loadConfigToInputs()
        refreshPermissionCard()
        refreshUiFromCache()
        window.statusBarColor = Fg.ground
        window.navigationBarColor = Fg.ground
        scrollView.post { scrollView.scrollTo(0, scrollY) }
    }

    private fun lp(w: Int = ViewGroup.LayoutParams.MATCH_PARENT, h: Int = ViewGroup.LayoutParams.WRAP_CONTENT, top: Int = 0, bottom: Int = 0) =
        LinearLayout.LayoutParams(w, h).apply { topMargin = top; bottomMargin = bottom }

    private fun buildUi() {
        levelButtons.clear(); durChips.clear(); tgtSegs.clear()
        scrollView = ScrollView(this).apply { setBackgroundColor(Fg.ground) }
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(18), dp(20), dp(48))
        }
        scrollView.addView(root)

        // ヘッダー
        val headerRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            layoutParams = lp(bottom = dp(8))
        }
        val titleWrap = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        }
        titleWrap.addView(TextView(this).apply {
            text = "FocusGuard"
            setTextColor(Fg.ink)
            textSize = 22f
            typeface = Fg.serif
        })
        txtSyncStatus = TextView(this).apply {
            text = "同期を待っています"
            setTextColor(Fg.sub)
            textSize = 12f
        }
        titleWrap.addView(txtSyncStatus)
        headerRow.addView(titleWrap)
        if (Fg.locked) {
            headerRow.addView(TextView(this).apply {
                text = "Lv.3 ロック中"
                setTextColor(Color.parseColor("#FBEFEC"))
                textSize = 13f
                setTypeface(null, Typeface.BOLD)
                background = Fg.rounded(Fg.lv(3), dp(16).toFloat())
                setPadding(dp(12), dp(6), dp(12), dp(6))
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { marginEnd = dp(8) }
            })
        }
        headerRow.addView(makeLineButton("同期", small = true) {
            io.execute {
                repo.pullFromSupabase(false)
                repo.sendHeartbeat()
                mainHandler.post {
                    refreshUiFromCache()
                    toast(repo.lastSyncMessage)
                }
            }
        }.apply { layoutParams = LinearLayout.LayoutParams(dp(72), dp(44)) })
        root.addView(headerRow)

        // 権限が足りないときだけ出す案内
        permWarningBanner = TextView(this).apply {
            text = "スマホのブロックに必要な権限がまだです。タップして設定へ"
            setTextColor(Fg.lvText(1))
            textSize = 13f
            setTypeface(null, Typeface.BOLD)
            background = Fg.rounded(Color.parseColor("#F4E6C4"), dp(12).toFloat(), dp(1), Fg.lv(1))
            setPadding(dp(14), dp(12), dp(14), dp(12))
            layoutParams = lp(bottom = dp(12))
            visibility = View.GONE
            setOnClickListener {
                scrollView.post { scrollView.smoothScrollTo(0, setupSection.top) }
            }
        }
        root.addView(permWarningBanner)

        // 24時間の文字盤
        dial = DialView(this).apply { layoutParams = lp(top = dp(4)) }
        root.addView(dial)
        val legend = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
            layoutParams = lp(top = dp(6), bottom = dp(18))
        }
        legendNow = View(this).apply {
            background = Fg.rounded(Fg.lv(2), dp(3).toFloat())
            layoutParams = LinearLayout.LayoutParams(dp(14), dp(6)).apply { marginEnd = dp(6) }
        }
        legend.addView(legendNow)
        legend.addView(makeSub("いまのブロック", 12f).apply { layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { marginEnd = dp(16) } })
        legend.addView(View(this).apply {
            background = Fg.rounded(Fg.sched, dp(3).toFloat())
            layoutParams = LinearLayout.LayoutParams(dp(14), dp(6)).apply { marginEnd = dp(6) }
        })
        legend.addView(makeSub("今日のスケジュール", 12f))
        root.addView(legend)

        // 端末ごとの状態
        val dualRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            layoutParams = lp(bottom = dp(10))
        }
        val pcBox = makeDeviceTile("Windows PC")
        txtPcLevel = makeDeviceState(); txtPcSub = makeSub("いつも通り使えます", 12f)
        pcBox.addView(txtPcLevel); pcBox.addView(txtPcSub)
        (pcBox.layoutParams as LinearLayout.LayoutParams).marginEnd = dp(10)
        val anBox = makeDeviceTile("Android")
        txtAndroidLevel = makeDeviceState(); txtAndroidSub = makeSub("いつも通り使えます", 12f)
        anBox.addView(txtAndroidLevel); anBox.addView(txtAndroidSub)
        dualRow.addView(pcBox); dualRow.addView(anBox)
        root.addView(dualRow)

        btnEndSnooze = makeLineButton("一時的に開けているのを今すぐ終える") {
            repo.setSnoozeUntilMs(0L)
            refreshUiFromCache()
            toast("一時的に開けていたのを終えました")
        }.apply { visibility = View.GONE }
        root.addView(btnEndSnooze)

        // Lv.3 ロック中にできること
        lockCard = makeCard()
        lockCard.addView(makeSectionTitle("ロック中にできること"))
        lockCard.addView(makeSub("延ばすことと、止めるアプリを増やすことはできます。短くする・弱める・止めることはできません。", 13f).apply { setPadding(0, 0, 0, dp(10)) })
        val extRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        extRow.addView(makeStrongButton("30分延ばす") { extendHardLock(30) }.apply {
            layoutParams = LinearLayout.LayoutParams(0, dp(48), 1f).apply { marginEnd = dp(8) }
        })
        extRow.addView(makeStrongButton("1時間延ばす") { extendHardLock(60) }.apply {
            layoutParams = LinearLayout.LayoutParams(0, dp(48), 1f)
        })
        lockCard.addView(extRow)
        lockCard.visibility = if (Fg.locked) View.VISIBLE else View.GONE
        root.addView(lockCard)

        // 新しくブロックする
        val remoteCard = makeCard()
        remoteCard.addView(makeSectionTitle("新しくブロックする"))
        remoteCard.addView(makeGroupTitle("強さ"))
        val lvRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        for (lv in 1..3) {
            val b = makeLevelButton(lv)
            b.layoutParams = LinearLayout.LayoutParams(0, dp(80), 1f).apply { if (lv < 3) marginEnd = dp(8) }
            levelButtons.add(b)
            lvRow.addView(b)
        }
        remoteCard.addView(lvRow)
        txtLvDesc = makeSub("", 13f).apply { setPadding(0, dp(8), 0, dp(4)); minLines = 2 }
        remoteCard.addView(txtLvDesc)

        remoteCard.addView(makeGroupTitle("長さ"))
        val durRow1 = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; layoutParams = lp(bottom = dp(8)) }
        val durRow2 = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        for (i in durValues.indices) {
            val v = durValues[i]
            val chip = makeChip(durLabels[i]) { selectedDuration = v; renderStartControls() }
            chip.layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(44)).apply { marginEnd = dp(8) }
            durChips.add(chip)
            if (i < 4) durRow1.addView(chip) else durRow2.addView(chip)
        }
        remoteCard.addView(durRow1)
        remoteCard.addView(durRow2)

        remoteCard.addView(makeGroupTitle("止める端末"))
        val seg = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            background = Fg.rounded(Fg.well, dp(12).toFloat())
            setPadding(dp(4), dp(4), dp(4), dp(4))
        }
        for (i in tgtValues.indices) {
            val v = tgtValues[i]
            val s = TextView(this).apply {
                text = tgtLabels[i]
                textSize = 14f
                gravity = Gravity.CENTER
                setTextColor(Fg.ink)
                layoutParams = LinearLayout.LayoutParams(0, dp(44), 1f)
                isClickable = true
                isFocusable = true
                setOnClickListener { selectedTarget = v; renderStartControls() }
            }
            tgtSegs.add(s)
            seg.addView(s)
        }
        remoteCard.addView(seg)

        txtLv3Warn = TextView(this).apply {
            text = "Lv.3 は途中で止められないので、手動解除ではなく1時間で始めます。"
            setTextColor(Fg.lvText(3))
            textSize = 12f
            setPadding(0, dp(10), 0, 0)
            visibility = View.GONE
        }
        remoteCard.addView(txtLv3Warn)

        btnStart = Button(this).apply {
            isAllCaps = false
            textSize = 16f
            setTypeface(null, Typeface.BOLD)
            setTextColor(Fg.paper)
            stateListAnimator = null
            layoutParams = lp(h = dp(56), top = dp(16))
            setOnClickListener { startManualSession() }
        }
        remoteCard.addView(btnStart)
        btnStopManual = makeLineButton("手動のブロックはありません") { stopManualSession() }
        remoteCard.addView(btnStopManual)
        root.addView(remoteCard)
        renderStartControls()

        // スケジュール
        root.addView(makeSectionTitle("スケジュール").apply { setPadding(0, dp(10), 0, dp(10)) })
        scheduleContainer = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        root.addView(scheduleContainer)
        root.addView(makeLineButton("スケジュールを追加") { showAddScheduleDialog() }.apply {
            (layoutParams as LinearLayout.LayoutParams).bottomMargin = dp(18)
        })

        // ブロック中でも開けるサイト（ホワイトリスト）
        val sitesCard = makeCard()
        sitesCard.addView(makeSectionTitle("ブロック中でも開けるサイト"))
        sitesCard.addView(makeSub("仕事で必要なサイトを1行に1つ（例: example.com）。どの強さのブロック中でも、このサイトだけは開けます。スマホは Chrome・Edge・Firefox・Samsung インターネット・Brave が対象で、ほかのブラウザはアプリごと止まります。", 13f).apply {
            setPadding(0, 0, 0, dp(10))
        })
        inSites = makeInput("example.com\ndocs.google.com", false).apply {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE or InputType.TYPE_TEXT_VARIATION_URI
            minLines = 3
            gravity = Gravity.TOP or Gravity.START
        }
        sitesCard.addView(inSites)
        sitesCard.addView(makePrimaryButton("開けるサイトを保存") { saveAllowedSites() })
        root.addView(sitesCard)

        // 止めるアプリ
        val appsCard = makeCard()
        appsCard.addView(makeSectionTitle("止めるアプリ"))
        txtTargetAppsSummary = makeSub("読み込み中", 13f)
        appsCard.addView(txtTargetAppsSummary)
        appsCard.addView(makeLineButton("止めるアプリを選ぶ") { showAppPickerDialog() })
        root.addView(appsCard)

        // 初期設定
        setupSection = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }

        val permCard = makeCard()
        permCard.addView(makeSectionTitle("スマホの権限"))
        txtPermAccess = makeSub("", 13f)
        btnPermAccess = makePrimaryButton("「ユーザー補助」の設定を開く") {
            startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
        }
        txtPermOverlay = makeSub("", 13f).apply { setPadding(0, dp(12), 0, 0) }
        btnPermOverlay = makePrimaryButton("「他のアプリの上に表示」の設定を開く") {
            startActivity(Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName")))
        }
        val btnBattery = makeLineButton("バッテリーの制限をなくす（おすすめ）") {
            try {
                val pm = getSystemService(POWER_SERVICE) as PowerManager
                if (pm.isIgnoringBatteryOptimizations(packageName)) {
                    toast("すでにバッテリーの制限はありません")
                } else {
                    startActivity(Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS).apply {
                        data = Uri.parse("package:$packageName")
                    })
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
        cfgCard.addView(makeSectionTitle("Supabase で同期"))
        cfgCard.addView(makeSub("PC アプリの「スマホ用の設定をコピー」で写した文字を貼ると、まとめて入ります。", 13f))
        inHashImport = makeInput("#url=...&key=...&sync=... を貼り付け", false)
        val btnImport = makeLineButton("貼り付けた設定を取り込む") {
            val parsed = repo.parseConfigFromHashOrUrl(inHashImport.text.toString())
            if (parsed == null) {
                toast("取り込めませんでした。#url=...&key=...&sync=... の形か確認してください。")
            } else {
                inUrl.setText(parsed.url)
                inAnonKey.setText(parsed.anonKey)
                inSyncKey.setText(parsed.syncKey)
                repo.saveConfig(parsed)
                inHashImport.setText("")
                toast("取り込みました。同期を始めます")
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
        cfgCard.addView(makeLabel("Sync Key（PC とスマホで同じ合言葉）"))
        inSyncKey = makeInput("8文字以上", false)
        cfgCard.addView(inSyncKey)

        val btnTest = makeLineButton("接続テスト") {
            val testCfg = SupabaseConfig(inUrl.text.toString(), inAnonKey.text.toString(), inSyncKey.text.toString())
            io.execute {
                val (_, msg) = repo.testConnection(testCfg)
                mainHandler.post { toast(msg) }
            }
        }
        val btnSaveCfg = makePrimaryButton("接続を保存して同期") {
            val newCfg = SupabaseConfig(inUrl.text.toString(), inAnonKey.text.toString(), inSyncKey.text.toString())
            if (!newCfg.isConfigured) {
                toast("URL・Anon Key・Sync Key（8文字以上）を入れてください。")
                return@makePrimaryButton
            }
            repo.saveConfig(newCfg)
            io.execute {
                repo.pullFromSupabase(true)
                repo.sendHeartbeat()
                mainHandler.post {
                    refreshUiFromCache()
                    toast("保存して同期しました")
                }
            }
        }
        cfgCard.addView(btnTest)
        cfgCard.addView(btnSaveCfg)
        setupSection.addView(cfgCard)

        root.addView(setupSection)
        setContentView(scrollView)
    }

    private fun renderStartControls() {
        for ((i, b) in levelButtons.withIndex()) {
            val lv = i + 1
            val sel = lv == selectedLevel
            b.background = Fg.rounded(if (sel) Fg.white else Color.TRANSPARENT, dp(14).toFloat(), dp(2), if (sel) Fg.ink else Fg.line)
            val bars = b.getChildAt(0) as LinearLayout
            for (j in 0 until 3) {
                bars.getChildAt(j).background = Fg.rounded(if (sel && j < lv) Fg.lv(lv) else Color.parseColor("#C3CBC0"), dp(2).toFloat())
            }
            b.contentDescription = "強さ ${Fg.lvName(lv)}" + if (sel) "（選択中）" else ""
        }
        for ((i, c) in durChips.withIndex()) {
            val sel = durValues[i] == selectedDuration
            c.background = if (sel) Fg.rounded(Fg.ink, dp(22).toFloat()) else Fg.rounded(Color.TRANSPARENT, dp(22).toFloat(), dp(1), Fg.line)
            c.setTextColor(if (sel) Fg.paper else Fg.ink)
            c.setTypeface(null, if (sel) Typeface.BOLD else Typeface.NORMAL)
        }
        for ((i, s) in tgtSegs.withIndex()) {
            val sel = tgtValues[i] == selectedTarget
            s.background = if (sel) Fg.rounded(Fg.white, dp(9).toFloat()) else null
            s.setTypeface(null, if (sel) Typeface.BOLD else Typeface.NORMAL)
        }
        txtLvDesc.text = lvDescs[selectedLevel - 1]
        val lv3NoEnd = selectedLevel == 3 && selectedDuration == 0
        txtLv3Warn.visibility = if (lv3NoEnd) View.VISIBLE else View.GONE
        btnStart.text = when {
            lv3NoEnd -> "1時間ブロックする"
            selectedDuration == 0 -> "手動で止めるまでブロックする"
            else -> durLabels[durValues.indexOf(selectedDuration)] + "ブロックする"
        }
        btnStart.background = Fg.primary(Fg.lv(selectedLevel), dp(16).toFloat(), dp(4))
    }

    private fun refreshPermissionCard() {
        val accOk = isAccessibilityEnabled()
        val ovOk = Settings.canDrawOverlays(this)

        permWarningBanner.visibility = if (accOk && ovOk) View.GONE else View.VISIBLE

        txtPermAccess.text = if (accOk) "ユーザー補助（アプリの検知）: オン" else "ユーザー補助（アプリの検知）: まだです（必須）"
        txtPermAccess.setTextColor(if (accOk) Fg.ok else Fg.lvText(2))
        btnPermAccess.visibility = if (accOk) View.GONE else View.VISIBLE

        txtPermOverlay.text = if (ovOk) "他のアプリの上に表示（待ち画面）: 許可済み" else "他のアプリの上に表示: まだです（Lv.1・Lv.2 に必要）"
        txtPermOverlay.setTextColor(if (ovOk) Fg.ok else Fg.lvText(2))
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

    private fun clock(ms: Long): String {
        val c = Calendar.getInstance().apply { timeInMillis = ms }
        return "${c.get(Calendar.HOUR_OF_DAY)}:" + String.format("%02d", c.get(Calendar.MINUTE))
    }

    private fun refreshUiFromCache() {
        val st = repo.getState()
        val effPc = RuleEngine.evaluate(st, "pc", 0L)
        val effAn = RuleEngine.evaluate(st, "android", repo.getSnoozeUntilMs())
        val locked = effPc.isHardLocked || effAn.isHardLocked
        if (builtLocked != null && builtLocked != locked) {
            rebuildUi(locked)
            return
        }

        setDevice(txtPcLevel, txtPcSub, effPc)
        setDevice(txtAndroidLevel, txtAndroidSub, effAn)
        if (effAn.isActive && effAn.isSnoozed) {
            txtAndroidLevel.text = "一時的に開けています"
            txtAndroidLevel.setTextColor(Fg.ink)
            txtAndroidSub.text = "あと ${effAn.snoozeRemainingSeconds} 秒で Lv.${effAn.level} に戻ります"
        }
        btnEndSnooze.visibility = if (effAn.isSnoozed) View.VISIBLE else View.GONE

        txtSyncStatus.text = repo.lastSyncMessage
        txtSyncStatus.setTextColor(if (repo.lastSyncOk) Fg.sub else Fg.lvText(3))

        val top = if (effAn.isActive && (!effPc.isActive || effAn.level > effPc.level)) effAn else effPc
        updateDial(st, top)

        val pcStoppable = RuleEngine.isLive(st.manualPc) && !effPc.isHardLocked
        val anStoppable = RuleEngine.isLive(st.manualAndroid) && !effAn.isHardLocked
        val manualOn = pcStoppable || anStoppable
        val lv2Active = (pcStoppable && effPc.level == 2) || (anStoppable && effAn.level == 2)
        val which = if (pcStoppable && anStoppable) "" else if (pcStoppable) "PC の" else "スマホの"
        btnStopManual.isEnabled = manualOn
        styleLineButton(btnStopManual, btnStopManual.isEnabled)
        btnStopManual.text = when {
            locked && !manualOn -> {
                val lockEnd = maxOf(
                    if (effPc.isHardLocked) effPc.endsAtMs ?: 0L else 0L,
                    if (effAn.isHardLocked) effAn.endsAtMs ?: 0L else 0L
                )
                if (lockEnd > 0L) "ブロックを止める（${clock(lockEnd)} から）" else "ブロックを止める（ロック中）"
            }
            manualOn && lv2Active -> "${which}手動のブロックを止める（名言の入力あり）"
            manualOn -> "${which}手動のブロックを止める"
            else -> "手動のブロックはありません"
        }

        renderSchedules(st)
        updateTargetAppsSummary(st)
        if (!inSites.hasFocus()) {
            val text = SiteAllowlist.normalizeList(st.allowedSites).joinToString("\n")
            if (inSites.text.toString() != text) inSites.setText(text)
        }
    }

    private fun setDevice(state: TextView, sub: TextView, eff: EffectiveBlockState) {
        if (eff.isActive) {
            state.text = "Lv.${eff.level} ブロック中"
            state.setTextColor(Fg.lvText(eff.level))
            val end = eff.endsAtMs
            sub.text = if (end != null && end > 0L) "${clock(end)} まで" else "手動で止めるまで"
        } else {
            state.text = "ブロックなし"
            state.setTextColor(Fg.ink)
            sub.text = "いつも通り使えます"
        }
    }

    private fun updateDial(st: GuardState, top: EffectiveBlockState) {
        val cal = Calendar.getInstance()
        val nowMin = cal.get(Calendar.HOUR_OF_DAY) * 60 + cal.get(Calendar.MINUTE) + cal.get(Calendar.SECOND) / 60.0
        val dow = cal.get(Calendar.DAY_OF_WEEK) - 1
        val yest = (dow + 6) % 7

        dial.arcs.clear()
        for (r in st.schedules) {
            if (!r.enabled) continue
            val sm = Fg.parseMin(r.startTime) ?: continue
            val em = Fg.parseMin(r.endTime) ?: continue
            when {
                sm == em -> if (dow in r.days) dial.arcs.add(DialView.Arc(0.0, 1440.0, Fg.sched))
                sm < em -> if (dow in r.days) dial.arcs.add(DialView.Arc(sm.toDouble(), (em - sm).toDouble(), Fg.sched))
                else -> {
                    if (dow in r.days) dial.arcs.add(DialView.Arc(sm.toDouble(), (1440 - sm).toDouble(), Fg.sched))
                    if (yest in r.days) dial.arcs.add(DialView.Arc(0.0, em.toDouble(), Fg.sched))
                }
            }
        }

        val end = top.endsAtMs
        val hasEnd = end != null && end > 0L
        if (top.isActive) {
            val len = if (hasEnd) (end!! - System.currentTimeMillis()) / 60000.0 else 1440.0
            dial.arcs.add(DialView.Arc(nowMin, minOf(len, 1439.5), Fg.lv(top.level)))
            dial.centerLabel = if (hasEnd) "ブロック終了まで" else "ブロック中"
            dial.centerTime = if (hasEnd) {
                val sec = ((end!! - System.currentTimeMillis()) / 1000L).coerceAtLeast(0L)
                val h = sec / 3600; val m = (sec % 3600) / 60; val s = sec % 60
                if (h > 0) String.format("%d:%02d:%02d", h, m, s) else String.format("%d:%02d", m, s)
            } else "手動解除まで"
            dial.centerTimeSmall = !hasEnd
            dial.centerState = if (top.level >= 3) {
                if (hasEnd) "${clock(end!!)} まで解除できません" else "解除できません"
            } else "Lv.${top.level} ${Fg.lvName(top.level)}で止めています"
            dial.centerStateColor = Fg.lvText(top.level)
            legendNow.background = Fg.rounded(Fg.lv(top.level), dp(3).toFloat())
        } else {
            val nowMinInt = cal.get(Calendar.HOUR_OF_DAY) * 60 + cal.get(Calendar.MINUTE)
            var nextTime: String? = null
            var nextName: String? = null
            var nextAdd = 0
            for (add in 0 until 8) {
                if (nextTime != null) break
                val d = (dow + add) % 7
                var best = Int.MAX_VALUE
                for (r in st.schedules) {
                    if (!r.enabled || d !in r.days) continue
                    val sm = Fg.parseMin(r.startTime) ?: continue
                    if (add == 0 && sm <= nowMinInt) continue
                    if (sm < best) { best = sm; nextTime = r.startTime; nextName = r.name; nextAdd = add }
                }
            }
            dial.centerLabel = "いまはブロックしていません"
            dial.centerTimeSmall = true
            if (nextTime != null) {
                dial.centerTime = (when (nextAdd) { 0 -> ""; 1 -> "明日 "; else -> "${nextAdd}日後 " }) + nextTime
                dial.centerState = "から「${nextName?.ifBlank { null } ?: "スケジュール"}」"
            } else {
                dial.centerTime = "自由時間"
                dial.centerState = "スケジュールはありません"
            }
            dial.centerStateColor = Fg.sub
            legendNow.background = Fg.rounded(Fg.lv(selectedLevel), dp(3).toFloat())
        }
        dial.nowMin = nowMin
        dial.showLock = top.isActive && top.level >= 3
        dial.contentDescription = "${dial.centerLabel} ${dial.centerTime} ${dial.centerState}"
        dial.invalidate()
    }

    private fun updateTargetAppsSummary(st: GuardState) {
        val pkgs = st.browsers.android
        val pm = packageManager
        val installedLabels = pkgs.mapNotNull { pkg ->
            try {
                val ai = pm.getApplicationInfo(pkg, 0)
                pm.getApplicationLabel(ai)?.toString()?.takeIf { it.isNotBlank() }
            } catch (_: Exception) {
                null
            }
        }
        txtTargetAppsSummary.text = if (installedLabels.isEmpty()) {
            "ブラウザの既定リスト（${pkgs.size}件）"
        } else {
            val head = installedLabels.take(5).joinToString("、")
            if (installedLabels.size > 5) "$head など、この端末で ${installedLabels.size} 個"
            else "$head（この端末で ${installedLabels.size} 個）"
        }
    }

    private data class AppEntry(val label: String, val pkg: String)

    private fun showAppPickerDialog() {
        val pm = packageManager
        val launcherIntent = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER)
        val resolved = pm.queryIntentActivities(launcherIntent, 0)
        val curState = repo.getState()
        val selectedSet = curState.browsers.android.map { it.trim() }.filter { it.isNotEmpty() }.toMutableSet()

        val allApps = resolved
            .mapNotNull { ri ->
                val pkg = ri.activityInfo?.packageName ?: return@mapNotNull null
                if (pkg == packageName) return@mapNotNull null
                val label = ri.loadLabel(pm)?.toString()?.ifBlank { pkg } ?: pkg
                AppEntry(label, pkg)
            }
            .distinctBy { it.pkg }
            .sortedWith(
                compareByDescending<AppEntry> { selectedSet.any { s -> s.equals(it.pkg, ignoreCase = true) } }
                    .thenBy { it.label.lowercase() }
            )

        val container = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(16), dp(12), dp(16), dp(8))
        }
        val searchBox = makeInput("アプリ名で検索（例: YouTube, ゲーム名...）", false)
        container.addView(searchBox)

        val infoText = makeSub("チェックを入れたアプリを止めます（${allApps.size} 件）", 13f)
        container.addView(infoText)

        val listScroll = ScrollView(this).apply {
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                dp(360)
            )
        }
        val listLayout = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        listScroll.addView(listLayout)
        container.addView(listScroll)

        fun renderFiltered(query: String) {
            listLayout.removeAllViews()
            val q = query.trim().lowercase()
            val filtered = if (q.isEmpty()) allApps else allApps.filter {
                it.label.lowercase().contains(q) || it.pkg.lowercase().contains(q)
            }
            for (app in filtered) {
                val cb = CheckBox(this).apply {
                    text = "${app.label}\n${app.pkg}"
                    setTextColor(Fg.ink)
                    buttonTintList = ColorStateList.valueOf(Fg.ink)
                    textSize = 13.5f
                    isChecked = selectedSet.any { it.equals(app.pkg, ignoreCase = true) }
                    setPadding(dp(6), dp(8), dp(6), dp(8))
                    setOnCheckedChangeListener { _, checked ->
                        selectedSet.removeAll { it.equals(app.pkg, ignoreCase = true) }
                        if (checked) selectedSet.add(app.pkg)
                    }
                }
                listLayout.addView(cb)
            }
        }

        renderFiltered("")
        searchBox.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            override fun afterTextChanged(s: Editable?) {
                renderFiltered(s?.toString() ?: "")
            }
        })

        AlertDialog.Builder(this, android.R.style.Theme_Material_Light_Dialog_Alert)
            .setTitle("止めるアプリを選ぶ")
            .setView(container)
            .setPositiveButton("保存して同期") { _, _ ->
                if (selectedSet.isEmpty()) {
                    toast("最低 1 つ以上のアプリを選択してください")
                    return@setPositiveButton
                }
                val next = repo.getState().deepCopy()
                next.browsers.android = selectedSet.toMutableList()
                applyScheduleChangeWithLv2Check(next, "Lv.2 の間に止めるアプリを外すには、下の言葉を手で入力してください：")
            }
            .setNegativeButton("やめる", null)
            .showStyled()
    }

    private fun renderSchedules(st: GuardState) {
        val sig = st.schedules.joinToString("|") { "${it.id},${it.name},${it.enabled},${it.startTime},${it.endTime},${it.level},${it.target},${it.days.joinToString("")}" }
        if (sig == scheduleSignature) return
        scheduleSignature = sig
        scheduleContainer.removeAllViews()
        if (st.schedules.isEmpty()) {
            scheduleContainer.addView(makeSub("まだありません。夜や作業時間など、毎日決まった時間に止めたいときに追加してください。", 13f).apply {
                setPadding(0, 0, 0, dp(10))
            })
            return
        }
        for (rule in st.schedules) {
            val card = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                background = Fg.rounded(Fg.paper, dp(14).toFloat())
                setPadding(dp(14), dp(12), dp(10), dp(12))
                layoutParams = lp(bottom = dp(10))
            }
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
            }
            val daysStr = if (rule.days.size == 7) "毎日"
            else rule.days.sorted().filter { it in 0..6 }.joinToString("・") { dayLabels[it] }
            val targetStr = when (rule.target) {
                "pc" -> "PCだけ"
                "android" -> "スマホだけ"
                else -> "両方"
            }
            val info = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
            }
            info.addView(TextView(this).apply {
                text = rule.name
                setTextColor(if (rule.enabled) Fg.ink else Fg.sub)
                textSize = 15f
                setTypeface(null, Typeface.BOLD)
            })
            info.addView(makeSub("$daysStr ${rule.startTime}〜${rule.endTime}、${targetStr}を Lv.${rule.level} で", 12f))
            row.addView(info)

            val sw = SwitchCompat(this).apply {
                isChecked = rule.enabled
                contentDescription = "${rule.name}を有効にする"
                thumbTintList = ColorStateList.valueOf(Fg.paper)
                trackTintList = ColorStateList(
                    arrayOf(intArrayOf(android.R.attr.state_checked), intArrayOf()),
                    intArrayOf(Fg.ink, Fg.line)
                )
                setOnClickListener {
                    val next = repo.getState().deepCopy()
                    next.schedules.find { it.id == rule.id }?.let { it.enabled = !it.enabled }
                    scheduleSignature = null
                    applyScheduleChangeWithLv2Check(next, "動いている Lv.2 のスケジュールをオフにするには、下の言葉を手で入力してください：")
                }
            }
            row.addView(sw)
            row.addView(TextView(this).apply {
                text = "削除"
                textSize = 13f
                setTextColor(Fg.sub)
                gravity = Gravity.CENTER
                background = Fg.rounded(Color.TRANSPARENT, dp(22).toFloat(), dp(1), Fg.line)
                layoutParams = LinearLayout.LayoutParams(dp(56), dp(44)).apply { marginStart = dp(8) }
                isClickable = true
                isFocusable = true
                contentDescription = "${rule.name}を削除"
                setOnClickListener {
                    AlertDialog.Builder(this@MainActivity, android.R.style.Theme_Material_Light_Dialog_Alert)
                        .setTitle("スケジュールの削除")
                        .setMessage("「${rule.name}」を削除しますか？")
                        .setPositiveButton("削除") { _, _ ->
                            val next = repo.getState().deepCopy()
                            next.schedules.removeAll { it.id == rule.id }
                            applyScheduleChangeWithLv2Check(next, "動いている Lv.2 のスケジュールを削除するには、下の言葉を手で入力してください：")
                        }
                        .setNegativeButton("やめる", null)
                        .showStyled()
                }
            })
            card.addView(row)

            val color = if (rule.enabled) Fg.lv(rule.level) else Fg.line
            card.addView(TimelineView(this, Fg.parseMin(rule.startTime), Fg.parseMin(rule.endTime), color).apply {
                layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(8)).apply {
                    topMargin = dp(10); marginEnd = dp(4)
                }
            })
            val scale = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; layoutParams = lp(top = dp(4)) }
            val marks = arrayOf("0時", "6時", "12時", "18時", "24時")
            for ((i, m) in marks.withIndex()) {
                scale.addView(makeSub(m, 11f).apply {
                    gravity = when (i) { 0 -> Gravity.START; 4 -> Gravity.END; else -> Gravity.CENTER }
                    layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                })
            }
            card.addView(scale)
            scheduleContainer.addView(card)
        }
    }

    private fun startManualSession() {
        var mins = selectedDuration
        if (selectedLevel == 3 && mins == 0) mins = 60

        val next = repo.getState().deepCopy()
        val now = System.currentTimeMillis()
        // 選んだ端末の枠だけを書き換える（もう片方の端末のブロックはそのまま）
        next.startManual(selectedLevel, selectedTarget, now, if (mins > 0) now + mins * 60_000L else 0L)
        if (selectedTarget != "pc") repo.setSnoozeUntilMs(0L)
        pushUpdatedState(next, "ブロックを始めました")
    }

    /** Lv.3 ロック中に終了時刻を延ばす（短縮・解除はできない） */
    private fun extendHardLock(mins: Int) {
        val cur = repo.getState()
        val effPc = RuleEngine.evaluate(cur, "pc", 0L)
        val effAn = RuleEngine.evaluate(cur, "android", 0L)
        val pcLocked = effPc.isHardLocked
        val anLocked = effAn.isHardLocked
        if (!pcLocked && !anLocked) return
        if ((pcLocked && (effPc.endsAtMs ?: 0L) <= 0L) || (anLocked && (effAn.endsAtMs ?: 0L) <= 0L)) {
            toast("手動で止めるまで続くブロックなので、延ばす必要はありません")
            return
        }
        // ロック中の端末それぞれの終了時刻から延ばす（ロックしていない端末は変えない）
        val now = System.currentTimeMillis()
        val next = cur.deepCopy()
        if (pcLocked) next.setSession("pc", 3, now, maxOf(now, effPc.endsAtMs ?: 0L) + mins * 60_000L)
        if (anLocked) next.setSession("android", 3, now, maxOf(now, effAn.endsAtMs ?: 0L) + mins * 60_000L)
        pushUpdatedState(next, "${mins}分延ばしました")
    }

    private fun stopManualSession() {
        val cur = repo.getState()
        val effPc = RuleEngine.evaluate(cur, "pc", 0L)
        val effAn = RuleEngine.evaluate(cur, "android", 0L)
        // 止められるのは、手動ブロック中で Lv.3 のロックがかかっていない端末だけ
        val stopPc = RuleEngine.isLive(cur.manualPc) && !effPc.isHardLocked
        val stopAn = RuleEngine.isLive(cur.manualAndroid) && !effAn.isHardLocked
        if (!stopPc && !stopAn) {
            toast(if (effPc.isHardLocked || effAn.isHardLocked) "Lv.3 のブロック中は、終わる時刻まで止められません。" else "止められる手動のブロックはありません。")
            return
        }
        fun stopped(base: GuardState): GuardState = base.deepCopy().apply {
            if (stopPc) { manualPc.active = false; manualPc.expiresAt = 0L }
            if (stopAn) { manualAndroid.active = false; manualAndroid.expiresAt = 0L }
        }
        val needsLv2 = (stopPc && effPc.level == 2) || (stopAn && effAn.level == 2)
        if (needsLv2) {
            showLv2ChallengeDialog("Lv.2 のブロックを止めるには、下の言葉を手で入力してください：") {
                pushUpdatedState(stopped(repo.getState()), "手動のブロックを止めました", lv2ChallengePassed = true)
            }
            return
        }
        pushUpdatedState(stopped(cur), "手動のブロックを止めました", lv2ChallengePassed = false)
    }

    private fun applyScheduleChangeWithLv2Check(proposed: GuardState, msg: String, successToast: String = "スケジュールを更新しました") {
        val cur = repo.getState()
        val errNo = RuleEngine.validateTransition(cur, proposed, lv2ChallengePassed = false)
        if (errNo == null) {
            pushUpdatedState(proposed, successToast, lv2ChallengePassed = false)
            return
        }
        val errHard = RuleEngine.validateTransition(cur, proposed, lv2ChallengePassed = true)
        if (errHard != null) {
            toast(errHard)
            return
        }
        showLv2ChallengeDialog(msg) {
            pushUpdatedState(proposed, successToast, lv2ChallengePassed = true)
        }
    }

    private fun saveAllowedSites() {
        val lines = inSites.text.toString().split(Regex("[\\s,、]+")).filter { it.isNotBlank() }
        val bad = lines.filter { SiteAllowlist.normalizeSite(it) == null }
        if (bad.isNotEmpty()) {
            toast("サイトとして読めないものがあります: ${bad.joinToString(" ")}（example.com のように入力してください）")
            return
        }
        val next = repo.getState().deepCopy()
        next.allowedSites = SiteAllowlist.normalizeList(lines).toMutableList()
        inSites.clearFocus()
        applyScheduleChangeWithLv2Check(
            next,
            "Lv.2 の間に開けるサイトを増やすには、下の言葉を手で入力してください：",
            if (next.allowedSites.isEmpty()) "開けるサイトを空にしました" else "開けるサイトを保存しました（${next.allowedSites.size} 件）"
        )
    }

    private fun showLv2ChallengeDialog(message: String, onSuccess: () -> Unit) {
        val target = Quotes.pick()

        val box = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(14), dp(20), dp(8))
        }
        val lblMsg = TextView(this).apply { text = message; setPadding(0, 0, 0, dp(8)) }
        val lblPhrase = TextView(this).apply {
            text = target.text
            textSize = 16f
            typeface = Fg.serif
            setLineSpacing(0f, 1.3f)
            setTextColor(Fg.ink)
            setPadding(0, dp(4), 0, dp(2))
        }
        val lblAuthor = TextView(this).apply {
            text = "— ${target.author}"
            textSize = 12f
            gravity = Gravity.END
            setPadding(0, 0, 0, dp(8))
        }
        val lblHint = TextView(this).apply {
            text = Quotes.progress("", target)
            textSize = 12f
            setPadding(0, dp(4), 0, 0)
        }
        val input = EditText(this).apply {
            hint = "上の言葉を入力（貼り付け不可）"
            isLongClickable = false
            setTextIsSelectable(false)
            customSelectionActionModeCallback = object : android.view.ActionMode.Callback {
                override fun onCreateActionMode(mode: android.view.ActionMode?, menu: android.view.Menu?) = false
                override fun onPrepareActionMode(mode: android.view.ActionMode?, menu: android.view.Menu?) = false
                override fun onActionItemClicked(mode: android.view.ActionMode?, item: android.view.MenuItem?) = false
                override fun onDestroyActionMode(mode: android.view.ActionMode?) {}
            }
            addTextChangedListener(object : android.text.TextWatcher {
                override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
                override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
                override fun afterTextChanged(s: android.text.Editable?) {
                    lblHint.text = Quotes.progress(s?.toString(), target)
                }
            })
        }
        box.addView(lblMsg)
        box.addView(lblPhrase)
        box.addView(lblAuthor)
        box.addView(input)
        box.addView(lblHint)

        AlertDialog.Builder(this, android.R.style.Theme_Material_Light_Dialog_Alert)
            .setTitle("Lv.2 の確認")
            .setView(box)
            .setPositiveButton("確認して実行") { _, _ ->
                if (Quotes.matches(input.text.toString(), target)) {
                    onSuccess()
                } else {
                    toast("入力が一致しませんでした")
                }
            }
            .setNegativeButton("やめる", null)
            .showStyled()
    }

    private fun showAddScheduleDialog() {
        val box = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(14), dp(20), dp(8))
        }
        val inName = EditText(this).apply { setText("夜のデジタルデトックス"); hint = "ルール名" }
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
            CheckBox(this).apply { text = dayLabels[idx]; isChecked = true; buttonTintList = ColorStateList.valueOf(Fg.ink) }
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

        AlertDialog.Builder(this, android.R.style.Theme_Material_Light_Dialog_Alert)
            .setTitle("スケジュールを追加")
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
            .setNegativeButton("やめる", null)
            .showStyled()
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

    // ===== 部品 =====

    /**
     * ダイアログを出して、ボタンの文字色をアプリの色で指定する。
     * 標準のダイアログは端末の強調色を使うため、機種によってはボタンがほとんど見えない。
     */
    private fun AlertDialog.Builder.showStyled(): AlertDialog {
        val d = show()
        d.getButton(AlertDialog.BUTTON_POSITIVE)?.apply {
            setTextColor(Fg.ink)
            setTypeface(null, Typeface.BOLD)
        }
        d.getButton(AlertDialog.BUTTON_NEGATIVE)?.setTextColor(Color.parseColor("#4D5A52"))
        d.getButton(AlertDialog.BUTTON_NEUTRAL)?.setTextColor(Color.parseColor("#4D5A52"))
        return d
    }

    private fun makeCard(): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        background = Fg.rounded(Fg.paper, dp(22).toFloat())
        setPadding(dp(18), dp(18), dp(18), dp(18))
        layoutParams = lp(bottom = dp(14))
    }

    private fun makeSectionTitle(t: String): TextView = TextView(this).apply {
        text = t
        setTextColor(Fg.ink)
        textSize = 19f
        typeface = Fg.serif
        setPadding(0, 0, 0, dp(12))
    }

    private fun makeGroupTitle(t: String): TextView = TextView(this).apply {
        text = t
        setTextColor(Fg.ink)
        textSize = 13f
        setTypeface(null, Typeface.BOLD)
        setPadding(0, dp(10), 0, dp(8))
    }

    private fun makeLabel(t: String): TextView = TextView(this).apply {
        text = t
        setTextColor(Fg.sub)
        textSize = 12f
        setPadding(0, dp(10), 0, dp(4))
    }

    private fun makeSub(t: String, size: Float): TextView = TextView(this).apply {
        text = t
        setTextColor(Fg.sub)
        textSize = size
    }

    private fun makeDeviceTile(deviceTitle: String): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        background = Fg.rounded(Fg.paper, dp(14).toFloat())
        setPadding(dp(14), dp(12), dp(14), dp(12))
        layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        addView(makeSub(deviceTitle, 12f))
    }

    private fun makeDeviceState(): TextView = TextView(this).apply {
        text = "ブロックなし"
        textSize = 15f
        setTypeface(null, Typeface.BOLD)
        setTextColor(Fg.ink)
        setPadding(0, dp(2), 0, dp(2))
    }

    private fun makeLevelButton(lv: Int): LinearLayout = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        setPadding(dp(12), dp(12), dp(12), dp(10))
        isClickable = true
        isFocusable = true
        val bars = LinearLayout(this@MainActivity).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.BOTTOM
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(26))
        }
        val heights = intArrayOf(10, 18, 26)
        for (h in heights) {
            bars.addView(View(this@MainActivity).apply {
                layoutParams = LinearLayout.LayoutParams(dp(8), dp(h)).apply { marginEnd = dp(3) }
            })
        }
        addView(bars)
        addView(View(this@MainActivity).apply { layoutParams = LinearLayout.LayoutParams(1, 0, 1f) })
        addView(TextView(this@MainActivity).apply {
            text = Fg.lvName(lv)
            textSize = 14f
            setTypeface(null, Typeface.BOLD)
            setTextColor(Fg.ink)
        })
        setOnClickListener {
            selectedLevel = lv
            renderStartControls()
        }
    }

    private fun makeChip(label: String, onClick: () -> Unit): TextView = TextView(this).apply {
        text = label
        textSize = 14f
        gravity = Gravity.CENTER
        setPadding(dp(16), 0, dp(16), 0)
        isClickable = true
        isFocusable = true
        setOnClickListener { onClick() }
    }

    private fun makeInput(hintText: String, password: Boolean): EditText = EditText(this).apply {
        hint = hintText
        setHintTextColor(Fg.sub)
        setTextColor(Fg.ink)
        textSize = 15f
        if (password) {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        background = Fg.rounded(Fg.white, dp(10).toFloat(), dp(1), Fg.line)
        setPadding(dp(12), dp(12), dp(12), dp(12))
        layoutParams = lp(bottom = dp(4))
    }

    private fun makePrimaryButton(label: String, onClick: () -> Unit): Button = Button(this).apply {
        text = label
        isAllCaps = false
        setTextColor(Fg.paper)
        textSize = 15f
        setTypeface(null, Typeface.BOLD)
        stateListAnimator = null
        background = Fg.primary(Fg.sched, dp(14).toFloat(), dp(4))
        layoutParams = lp(h = dp(52), top = dp(10))
        setOnClickListener { onClick() }
    }

    private fun makeStrongButton(label: String, onClick: () -> Unit): Button = Button(this).apply {
        text = label
        isAllCaps = false
        setTextColor(Fg.ink)
        textSize = 15f
        setTypeface(null, Typeface.BOLD)
        stateListAnimator = null
        background = Fg.rounded(Fg.white, dp(14).toFloat(), dp(2), Fg.ink)
        setOnClickListener { onClick() }
    }

    private fun makeLineButton(label: String, small: Boolean = false, onClick: () -> Unit): Button = Button(this).apply {
        text = label
        isAllCaps = false
        textSize = if (small) 13f else 14f
        stateListAnimator = null
        minHeight = 0
        minimumHeight = 0
        styleLineButton(this, true)
        layoutParams = lp(h = dp(48), top = dp(10))
        setOnClickListener { onClick() }
    }

    private fun styleLineButton(b: Button, enabled: Boolean) {
        b.setTextColor(if (enabled) Fg.ink else Fg.sub)
        b.background = Fg.rounded(Color.TRANSPARENT, dp(14).toFloat(), dp(1), Fg.line, if (enabled) 0f else dp(4).toFloat())
    }

    private fun toast(msg: String) {
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
    }

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()
}
