package com.focusguard.app

import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.os.CountDownTimer
import android.text.Editable
import android.text.InputType
import android.text.TextWatcher
import android.view.ActionMode
import android.view.Gravity
import android.view.Menu
import android.view.MenuItem
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.TextView
import androidx.activity.OnBackPressedCallback
import androidx.appcompat.app.AppCompatActivity
import kotlin.random.Random

class BlockActivity : AppCompatActivity() {

    private var timer: CountDownTimer? = null
    private lateinit var repo: SyncRepository

    private val phrases = listOf(
        "focus over impulse",
        "choose calm over scroll",
        "protect my attention",
        "pause and breathe first",
        "stay on my real goal",
        "mindful screen time"
    )

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        isShowing = true
        repo = SyncRepository.getInstance(this)

        // Pressing Back goes to Home instead of returning to the blocked browser
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                goHomeAndFinish()
            }
        })

        renderFromIntent(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        timer?.cancel()
        renderFromIntent(intent)
    }

    private fun renderFromIntent(intent: Intent) {
        val level = intent.getIntExtra("level", 1)
        val reason = intent.getStringExtra("reason") ?: "集中モード"
        val remaining = intent.getStringExtra("remaining") ?: "—"
        val browserPkg = intent.getStringExtra("browserPkg") ?: "browser"
        val lv1WaitSec = intent.getIntExtra("lv1WaitSec", 15).coerceAtLeast(3)
        val lv1AllowMin = intent.getIntExtra("lv1AllowMin", 5).coerceAtLeast(1)
        val lv2AllowMin = intent.getIntExtra("lv2AllowMin", 10).coerceAtLeast(1)
        val allowMin = if (level == 1) lv1AllowMin else lv2AllowMin

        val accentColor = if (level == 1) Color.parseColor("#F5B942") else Color.parseColor("#F57642")

        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setBackgroundColor(Color.parseColor("#0D1119"))
            setPadding(dp(20), dp(32), dp(20), dp(32))
        }

        val card = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#181F2E"))
                cornerRadius = dp(18).toFloat()
                setStroke(dp(2), accentColor)
            }
            setPadding(dp(22), dp(24), dp(22), dp(24))
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            )
        }

        val badge = TextView(this).apply {
            text = if (level == 1) "Lv.1  気づきモード（ソフト）" else "Lv.2  ひと手間モード（ミドル）"
            setTextColor(accentColor)
            textSize = 13f
            setTypeface(null, Typeface.BOLD)
            background = GradientDrawable().apply {
                setColor(if (level == 1) Color.parseColor("#3D3114") else Color.parseColor("#422114"))
                cornerRadius = dp(8).toFloat()
            }
            setPadding(dp(12), dp(6), dp(12), dp(6))
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WRAP_CONTENT,
                LinearLayout.LayoutParams.WRAP_CONTENT
            )
        }
        card.addView(badge)

        val title = TextView(this).apply {
            text = if (level == 1) {
                "ちょっと待って。\n本当に今ブラウザが必要？"
            } else {
                "集中モード中です。\n無意識のブラウザ起動を止めました。"
            }
            setTextColor(Color.WHITE)
            textSize = 20f
            setTypeface(null, Typeface.BOLD)
            setPadding(0, dp(16), 0, dp(8))
        }
        card.addView(title)

        val meta = TextView(this).apply {
            text = "理由: $reason  /  残り: $remaining\n対象アプリ: $browserPkg"
            setTextColor(Color.parseColor("#9BA8C2"))
            textSize = 13f
            setPadding(0, 0, 0, dp(18))
        }
        card.addView(meta)

        val btnAllow = Button(this).apply {
            isAllCaps = false
            setTextColor(Color.parseColor("#B5C0D4"))
            textSize = 14f
            isEnabled = false
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#263042"))
                cornerRadius = dp(12).toFloat()
                setStroke(dp(1), Color.parseColor("#3C4B66"))
            }
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                dp(48)
            ).apply { topMargin = dp(10) }
            setOnClickListener {
                repo.setSnoozeUntilMs(System.currentTimeMillis() + allowMin * 60_000L)
                finish()
            }
        }

        if (level == 1) {
            val lblCountdown = TextView(this).apply {
                text = "深呼吸して $lv1WaitSec 秒待ってください..."
                setTextColor(Color.parseColor("#FFD266"))
                textSize = 16f
                setTypeface(null, Typeface.BOLD)
                setPadding(0, 0, 0, dp(10))
            }
            card.addView(lblCountdown)

            val progress = ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal).apply {
                max = lv1WaitSec * 10
                this.progress = 0
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    dp(14)
                ).apply { bottomMargin = dp(18) }
            }
            card.addView(progress)

            btnAllow.text = "あと ${lv1WaitSec} 秒で一時許可可能（${allowMin}分間）"

            val totalMs = lv1WaitSec * 1000L
            timer = object : CountDownTimer(totalMs, 100L) {
                override fun onTick(millisUntilFinished: Long) {
                    val sec = ((millisUntilFinished + 999L) / 1000L).toInt()
                    val elapsedSteps = ((totalMs - millisUntilFinished) / 100L).toInt()
                    progress.progress = elapsedSteps.coerceIn(0, progress.max)
                    lblCountdown.text = "深呼吸して $sec 秒待ってください..."
                    btnAllow.text = "あと $sec 秒で一時許可可能（${allowMin}分間）"
                }

                override fun onFinish() {
                    progress.progress = progress.max
                    lblCountdown.text = "準備OK。必要な用事だけ済ませましょう。"
                    lblCountdown.setTextColor(Color.parseColor("#7AE3A8"))
                    btnAllow.isEnabled = true
                    btnAllow.setTextColor(Color.WHITE)
                    btnAllow.text = "${allowMin}分だけブラウザを許可する"
                }
            }.start()
        } else {
            val targetPhrase = "${phrases[Random.nextInt(phrases.size)]} ${Random.nextInt(100, 999)}"

            val lblGuide = TextView(this).apply {
                text = "一時的に使うには、下のフレーズを手入力してください（貼付不可）："
                setTextColor(Color.parseColor("#D9E2F2"))
                textSize = 13f
                setPadding(0, 0, 0, dp(8))
            }
            card.addView(lblGuide)

            val lblPhrase = TextView(this).apply {
                text = targetPhrase
                setTextColor(Color.parseColor("#FFAA6E"))
                textSize = 18f
                typeface = Typeface.MONOSPACE
                gravity = Gravity.CENTER
                background = GradientDrawable().apply {
                    setColor(Color.parseColor("#101520"))
                    cornerRadius = dp(10).toFloat()
                }
                setPadding(dp(12), dp(14), dp(12), dp(14))
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                ).apply { bottomMargin = dp(10) }
            }
            card.addView(lblPhrase)

            val lblHint = TextView(this).apply {
                text = "正確に入力してください"
                setTextColor(Color.parseColor("#9BA8C2"))
                textSize = 12f
                setPadding(0, dp(4), 0, dp(14))
            }

            val input = EditText(this).apply {
                hint = "ここに入力..."
                setHintTextColor(Color.parseColor("#6B7791"))
                setTextColor(Color.WHITE)
                textSize = 16f
                typeface = Typeface.MONOSPACE
                inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
                isLongClickable = false
                setTextIsSelectable(false)
                customSelectionActionModeCallback = object : ActionMode.Callback {
                    override fun onCreateActionMode(mode: ActionMode?, menu: Menu?) = false
                    override fun onPrepareActionMode(mode: ActionMode?, menu: Menu?) = false
                    override fun onActionItemClicked(mode: ActionMode?, item: MenuItem?) = false
                    override fun onDestroyActionMode(mode: ActionMode?) {}
                }
                background = GradientDrawable().apply {
                    setColor(Color.parseColor("#20283A"))
                    cornerRadius = dp(10).toFloat()
                    setStroke(dp(1), Color.parseColor("#3B4B6B"))
                }
                setPadding(dp(14), dp(12), dp(14), dp(12))
                addTextChangedListener(object : TextWatcher {
                    override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
                    override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
                    override fun afterTextChanged(s: Editable?) {
                        val ok = s?.toString()?.trim() == targetPhrase
                        btnAllow.isEnabled = ok
                        btnAllow.setTextColor(if (ok) Color.WHITE else Color.parseColor("#8D99B2"))
                        lblHint.text = if (ok) "一致しました！一時許可できます。" else "正確に入力してください"
                        lblHint.setTextColor(if (ok) Color.parseColor("#7AE3A8") else Color.parseColor("#9BA8C2"))
                    }
                })
            }
            card.addView(input)
            card.addView(lblHint)

            btnAllow.text = "フレーズを入力して ${allowMin}分だけ許可"
        }

        // Primary recommended action: close browser (go home)
        val btnHome = Button(this).apply {
            text = "やっぱりやめる（ホームへ戻る）"
            isAllCaps = false
            setTextColor(Color.WHITE)
            textSize = 16f
            setTypeface(null, Typeface.BOLD)
            background = GradientDrawable().apply {
                setColor(Color.parseColor("#22995D"))
                cornerRadius = dp(12).toFloat()
            }
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                dp(52)
            )
            setOnClickListener { goHomeAndFinish() }
        }
        card.addView(btnHome)
        card.addView(btnAllow)

        root.addView(card)
        setContentView(root)
    }

    private fun goHomeAndFinish() {
        val homeIntent = Intent(Intent.ACTION_MAIN).apply {
            addCategory(Intent.CATEGORY_HOME)
            flags = Intent.FLAG_ACTIVITY_NEW_TASK
        }
        startActivity(homeIntent)
        finish()
    }

    override fun onDestroy() {
        timer?.cancel()
        isShowing = false
        super.onDestroy()
    }

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()

    companion object {
        @Volatile
        var isShowing: Boolean = false
    }
}
