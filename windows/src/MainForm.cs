using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FocusGuard
{
    public class MainForm : Form
    {
        private readonly SyncService _sync;
        private readonly Action _requestQuit;

        // Status header
        private Label _lblStatusBadge;
        private Label _lblStatusDetail;
        private Label _lblSyncStatus;
        private Button _btnEndSnooze;

        // Remote tab controls
        private RadioButton _rbLv1, _rbLv2, _rbLv3;
        private ComboBox _cmbTarget;
        private ComboBox _cmbDuration;
        private Button _btnStartManual;
        private Button _btnStopManual;
        private Label _lblHardLockNote;

        // Schedule tab controls
        private ListView _lvSchedules;
        private TextBox _txtSchedName;
        private TextBox _txtSchedStart;
        private TextBox _txtSchedEnd;
        private ComboBox _cmbSchedLevel;
        private ComboBox _cmbSchedTarget;
        private CheckBox[] _chkDays;
        private Button _btnSchedAdd;
        private Button _btnSchedToggle;
        private Button _btnSchedDelete;

        // Settings tab controls
        private RadioButton _rbModeLocal, _rbModeSupabase;
        private TextBox _txtSupabaseUrl, _txtSupabaseKey, _txtSyncKey;
        private Button _btnTestSupabase, _btnSaveSettings, _btnCopyMobileLink, _btnOpenWebUi;
        private TextBox _txtBrowsers;
        private NumericUpDown _numLv1Wait, _numLv1Allow, _numLv2Allow;
        private CheckBox _chkStartup;

        private bool _forceExit;

        private readonly Color _bgDark = Color.FromArgb(18, 22, 30);
        private readonly Color _cardBg = Color.FromArgb(26, 32, 44);
        private readonly Color _textMain = Color.FromArgb(238, 242, 248);
        private readonly Color _textSub = Color.FromArgb(156, 168, 190);

        public MainForm(SyncService sync, Action requestQuit)
        {
            _sync = sync;
            _requestQuit = requestQuit;

            Text = "FocusGuard — デジタルデトックス リモコン (PC / Android)";
            Size = new Size(700, 620);
            MinimumSize = new Size(660, 580);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = _bgDark;
            ForeColor = _textMain;
            Font = new Font("Meiryo UI", 9.5f);

            BuildLayout();
            LoadSettingsToUi();
            RefreshStateUi(_sync.GetState());

            _sync.StateChanged += (st) => BeginInvokeSafe(() => RefreshStateUi(st));
            _sync.SyncStatusChanged += (msg, ok) => BeginInvokeSafe(() => UpdateSyncLabel(msg, ok));
        }

        public void AllowExit()
        {
            _forceExit = true;
        }

        public void UpdateLiveStatus(EffectiveBlockState eff)
        {
            BeginInvokeSafe(() =>
            {
                if (!eff.IsActive)
                {
                    _lblStatusBadge.Text = "  待機中 (ブロックOFF)  ";
                    _lblStatusBadge.BackColor = Color.FromArgb(26, 68, 52);
                    _lblStatusBadge.ForeColor = Color.FromArgb(110, 230, 175);
                    _lblStatusDetail.Text = "ブラウザは通常どおり利用できます。";
                    _btnEndSnooze.Visible = false;
                }
                else if (eff.IsSnoozed)
                {
                    _lblStatusBadge.Text = string.Format("  一時許可中 (残り {0} 秒)  ", eff.SnoozeRemainingSeconds);
                    _lblStatusBadge.BackColor = Color.FromArgb(40, 62, 92);
                    _lblStatusBadge.ForeColor = Color.FromArgb(145, 200, 255);
                    _lblStatusDetail.Text = string.Format("元のルール: Lv.{0} ({1})", eff.Level, eff.Reason);
                    _btnEndSnooze.Visible = true;
                }
                else
                {
                    _lblStatusBadge.Text = string.Format("  Lv.{0} ブロック稼働中  ", eff.Level);
                    if (eff.Level == 1)
                    {
                        _lblStatusBadge.BackColor = Color.FromArgb(78, 60, 18);
                        _lblStatusBadge.ForeColor = Color.FromArgb(255, 215, 102);
                    }
                    else if (eff.Level == 2)
                    {
                        _lblStatusBadge.BackColor = Color.FromArgb(88, 40, 18);
                        _lblStatusBadge.ForeColor = Color.FromArgb(255, 155, 105);
                    }
                    else
                    {
                        _lblStatusBadge.BackColor = Color.FromArgb(92, 24, 34);
                        _lblStatusBadge.ForeColor = Color.FromArgb(255, 125, 138);
                    }
                    _lblStatusDetail.Text = string.Format("{0}  |  残り: {1}", eff.Reason, eff.RemainingText);
                    _btnEndSnooze.Visible = false;
                }

                bool locked = eff.IsHardLocked;
                _btnStopManual.Enabled = !locked;
                _lblHardLockNote.Visible = locked;
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_forceExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        private void BuildLayout()
        {
            // Top Status Card
            var topCard = new Panel
            {
                Dock = DockStyle.Top,
                Height = 96,
                BackColor = _cardBg,
                Padding = new Padding(16, 12, 16, 10)
            };
            Controls.Add(topCard);

            _lblStatusBadge = new Label
            {
                Text = "  待機中  ",
                Font = new Font("Meiryo UI", 10.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 14),
                Padding = new Padding(4, 3, 4, 3)
            };
            topCard.Controls.Add(_lblStatusBadge);

            _btnEndSnooze = new Button
            {
                Text = "一時許可を今すぐ終了",
                Location = new Point(470, 12),
                Size = new Size(185, 28),
                BackColor = Color.FromArgb(62, 74, 96),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Visible = false
            };
            _btnEndSnooze.FlatAppearance.BorderSize = 0;
            _btnEndSnooze.Click += (s, e) => { _sync.LocalSnoozeUntilMs = 0; };
            topCard.Controls.Add(_btnEndSnooze);

            _lblStatusDetail = new Label
            {
                Text = "読み込み中...",
                ForeColor = _textMain,
                Location = new Point(16, 46),
                Size = new Size(640, 22)
            };
            topCard.Controls.Add(_lblStatusDetail);

            _lblSyncStatus = new Label
            {
                Text = _sync.LastSyncMessage,
                ForeColor = _textSub,
                Font = new Font("Meiryo UI", 8.5f),
                Location = new Point(16, 70),
                Size = new Size(640, 20)
            };
            topCard.Controls.Add(_lblSyncStatus);

            // Tabs
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(14, 6)
            };
            Controls.Add(tabs);
            tabs.BringToFront();

            var tabRemote = new TabPage("  リモコン  ") { BackColor = _bgDark, ForeColor = _textMain };
            var tabSched = new TabPage("  時間帯スケジュール  ") { BackColor = _bgDark, ForeColor = _textMain };
            var tabSettings = new TabPage("  同期・設定  ") { BackColor = _bgDark, ForeColor = _textMain };

            tabs.TabPages.Add(tabRemote);
            tabs.TabPages.Add(tabSched);
            tabs.TabPages.Add(tabSettings);

            BuildRemoteTab(tabRemote);
            BuildScheduleTab(tabSched);
            BuildSettingsTab(tabSettings);
        }

        private void BuildRemoteTab(TabPage tab)
        {
            var panel = new Panel
            {
                Location = new Point(16, 16),
                Size = new Size(635, 390),
                BackColor = _cardBg
            };
            tab.Controls.Add(panel);

            AddSectionHeader(panel, "今すぐブラウザブロックを開始（PC / スマホへ一括指示）", 18, 16);

            // Level radios
            var lblLv = new Label { Text = "強度レベル:", ForeColor = _textSub, Location = new Point(20, 52), AutoSize = true };
            panel.Controls.Add(lblLv);

            _rbLv1 = new RadioButton
            {
                Text = "Lv.1  気づき（15秒待機 → 5分許可可 / タブ維持）",
                Location = new Point(24, 78),
                Size = new Size(580, 26),
                ForeColor = Color.FromArgb(255, 220, 125)
            };
            _rbLv2 = new RadioButton
            {
                Text = "Lv.2  ひと手間（ランダムフレーズ手入力で10分許可可）",
                Location = new Point(24, 108),
                Size = new Size(580, 26),
                ForeColor = Color.FromArgb(255, 165, 115),
                Checked = true
            };
            _rbLv3 = new RadioButton
            {
                Text = "Lv.3  強制ブロック（即終了・時間終了まで解除ロック）",
                Location = new Point(24, 138),
                Size = new Size(580, 26),
                ForeColor = Color.FromArgb(255, 125, 138)
            };
            panel.Controls.AddRange(new Control[] { _rbLv1, _rbLv2, _rbLv3 });

            // Target & Duration
            var lblTarget = new Label { Text = "対象端末:", ForeColor = _textSub, Location = new Point(20, 182), AutoSize = true };
            _cmbTarget = new ComboBox
            {
                Location = new Point(20, 206),
                Size = new Size(275, 28),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbTarget.Items.AddRange(new object[] { "PC + スマホ 両方 (all)", "Windows PC のみ (pc)", "Android スマホのみ (android)" });
            _cmbTarget.SelectedIndex = 0;

            var lblDur = new Label { Text = "継続時間:", ForeColor = _textSub, Location = new Point(320, 182), AutoSize = true };
            _cmbDuration = new ComboBox
            {
                Location = new Point(320, 206),
                Size = new Size(275, 28),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbDuration.Items.AddRange(new object[] { "15分", "30分", "60分（1時間）", "120分（2時間）", "手動解除まで（Lv.1/2推奨）" });
            _cmbDuration.SelectedIndex = 1;

            panel.Controls.AddRange(new Control[] { lblTarget, _cmbTarget, lblDur, _cmbDuration });

            // Buttons
            _btnStartManual = new Button
            {
                Text = "この内容でブロック開始",
                Location = new Point(20, 256),
                Size = new Size(280, 44),
                BackColor = Color.FromArgb(52, 116, 235),
                ForeColor = Color.White,
                Font = new Font("Meiryo UI", 10.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnStartManual.FlatAppearance.BorderSize = 0;
            _btnStartManual.Click += OnStartManualClick;

            _btnStopManual = new Button
            {
                Text = "手動ブロックを停止",
                Location = new Point(316, 256),
                Size = new Size(280, 44),
                BackColor = Color.FromArgb(62, 70, 88),
                ForeColor = Color.White,
                Font = new Font("Meiryo UI", 10f),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnStopManual.FlatAppearance.BorderSize = 0;
            _btnStopManual.Click += OnStopManualClick;

            panel.Controls.AddRange(new Control[] { _btnStartManual, _btnStopManual });

            _lblHardLockNote = new Label
            {
                Text = "Lv.3 ハードロック中のため、終了時刻まで停止ボタンはロックされています。",
                ForeColor = Color.FromArgb(255, 130, 140),
                Location = new Point(20, 312),
                Size = new Size(590, 24),
                Visible = false
            };
            panel.Controls.Add(_lblHardLockNote);

            var lblTip = new Label
            {
                Text = "ヒント: ウィンドウを×で閉じてもタスクトレイに常駐して監視を続けます。\r\nスマホやWebブラウザからも同じ画面で両端末を操作できます（同期・設定タブを参照）。",
                ForeColor = _textSub,
                Font = new Font("Meiryo UI", 8.5f),
                Location = new Point(20, 342),
                Size = new Size(590, 38)
            };
            panel.Controls.Add(lblTip);
        }

        private void BuildScheduleTab(TabPage tab)
        {
            _lvSchedules = new ListView
            {
                Location = new Point(16, 14),
                Size = new Size(635, 170),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = _cardBg,
                ForeColor = _textMain
            };
            _lvSchedules.Columns.Add("状態", 56);
            _lvSchedules.Columns.Add("ルール名", 160);
            _lvSchedules.Columns.Add("時間帯", 110);
            _lvSchedules.Columns.Add("曜日", 135);
            _lvSchedules.Columns.Add("強度", 65);
            _lvSchedules.Columns.Add("対象", 95);
            tab.Controls.Add(_lvSchedules);

            _btnSchedToggle = new Button
            {
                Text = "選択ルールのON/OFF切替",
                Location = new Point(16, 192),
                Size = new Size(200, 32),
                BackColor = Color.FromArgb(52, 66, 90),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSchedToggle.FlatAppearance.BorderSize = 0;
            _btnSchedToggle.Click += OnScheduleToggle;

            _btnSchedDelete = new Button
            {
                Text = "選択ルールを削除",
                Location = new Point(226, 192),
                Size = new Size(160, 32),
                BackColor = Color.FromArgb(112, 42, 48),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSchedDelete.FlatAppearance.BorderSize = 0;
            _btnSchedDelete.Click += OnScheduleDelete;
            tab.Controls.AddRange(new Control[] { _btnSchedToggle, _btnSchedDelete });

            // Add new schedule card
            var addCard = new Panel
            {
                Location = new Point(16, 236),
                Size = new Size(635, 190),
                BackColor = _cardBg
            };
            tab.Controls.Add(addCard);

            AddSectionHeader(addCard, "新しい時間帯スケジュールを追加（日跨ぎ例: 23:00 〜 07:00 対応）", 16, 12);

            addCard.Controls.Add(new Label { Text = "ルール名:", ForeColor = _textSub, Location = new Point(16, 44), AutoSize = true });
            _txtSchedName = new TextBox { Text = "夜の集中・就寝前", Location = new Point(16, 64), Size = new Size(180, 26) };

            addCard.Controls.Add(new Label { Text = "開始 (HH:mm):", ForeColor = _textSub, Location = new Point(208, 44), AutoSize = true });
            _txtSchedStart = new TextBox { Text = "23:00", Location = new Point(208, 64), Size = new Size(85, 26) };

            addCard.Controls.Add(new Label { Text = "終了 (HH:mm):", ForeColor = _textSub, Location = new Point(304, 44), AutoSize = true });
            _txtSchedEnd = new TextBox { Text = "07:00", Location = new Point(304, 64), Size = new Size(85, 26) };

            addCard.Controls.Add(new Label { Text = "強度:", ForeColor = _textSub, Location = new Point(402, 44), AutoSize = true });
            _cmbSchedLevel = new ComboBox { Location = new Point(402, 64), Size = new Size(95, 26), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbSchedLevel.Items.AddRange(new object[] { "Lv.1", "Lv.2", "Lv.3" });
            _cmbSchedLevel.SelectedIndex = 1;

            addCard.Controls.Add(new Label { Text = "対象:", ForeColor = _textSub, Location = new Point(508, 44), AutoSize = true });
            _cmbSchedTarget = new ComboBox { Location = new Point(508, 64), Size = new Size(110, 26), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbSchedTarget.Items.AddRange(new object[] { "両方", "PCのみ", "スマホのみ" });
            _cmbSchedTarget.SelectedIndex = 0;

            addCard.Controls.AddRange(new Control[] { _txtSchedName, _txtSchedStart, _txtSchedEnd, _cmbSchedLevel, _cmbSchedTarget });

            string[] dayLabels = { "日", "月", "火", "水", "木", "金", "土" };
            _chkDays = new CheckBox[7];
            for (int i = 0; i < 7; i++)
            {
                _chkDays[i] = new CheckBox
                {
                    Text = dayLabels[i],
                    Checked = true,
                    ForeColor = _textMain,
                    Location = new Point(18 + i * 54, 102),
                    Size = new Size(48, 24)
                };
                addCard.Controls.Add(_chkDays[i]);
            }

            _btnSchedAdd = new Button
            {
                Text = "スケジュールを追加",
                Location = new Point(16, 138),
                Size = new Size(210, 36),
                BackColor = Color.FromArgb(40, 142, 92),
                ForeColor = Color.White,
                Font = new Font("Meiryo UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            _btnSchedAdd.FlatAppearance.BorderSize = 0;
            _btnSchedAdd.Click += OnScheduleAdd;
            addCard.Controls.Add(_btnSchedAdd);
        }

        private void BuildSettingsTab(TabPage tab)
        {
            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = _bgDark
            };
            tab.Controls.Add(scroll);

            // Sync Card
            var syncCard = new Panel { Location = new Point(16, 12), Size = new Size(620, 224), BackColor = _cardBg };
            scroll.Controls.Add(syncCard);

            AddSectionHeader(syncCard, "クラウド同期設定（Supabase で PC とスマホを同期）", 16, 12);

            _rbModeLocal = new RadioButton { Text = "ローカルのみ（テスト・PC単体）", Location = new Point(18, 42), Size = new Size(240, 24), ForeColor = _textMain };
            _rbModeSupabase = new RadioButton { Text = "Supabase クラウド同期（外出先OK）", Location = new Point(268, 42), Size = new Size(280, 24), ForeColor = Color.FromArgb(120, 225, 175) };
            syncCard.Controls.AddRange(new Control[] { _rbModeLocal, _rbModeSupabase });

            syncCard.Controls.Add(new Label { Text = "Supabase URL:", ForeColor = _textSub, Location = new Point(18, 76), Size = new Size(125, 22) });
            _txtSupabaseUrl = new TextBox { Location = new Point(146, 73), Size = new Size(455, 24) };

            syncCard.Controls.Add(new Label { Text = "Anon Key:", ForeColor = _textSub, Location = new Point(18, 108), Size = new Size(125, 22) });
            _txtSupabaseKey = new TextBox { Location = new Point(146, 105), Size = new Size(455, 24) };

            syncCard.Controls.Add(new Label { Text = "Sync Key (秘密鍵):", ForeColor = _textSub, Location = new Point(18, 140), Size = new Size(125, 22) });
            _txtSyncKey = new TextBox { Location = new Point(146, 137), Size = new Size(340, 24) };

            var btnGenKey = new Button
            {
                Text = "ランダム生成",
                Location = new Point(494, 136),
                Size = new Size(107, 26),
                BackColor = Color.FromArgb(52, 64, 84),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnGenKey.FlatAppearance.BorderSize = 0;
            btnGenKey.Click += (s, e) => { _txtSyncKey.Text = Guid.NewGuid().ToString("N").Substring(0, 16); };

            _btnTestSupabase = new Button
            {
                Text = "Supabase 接続テスト",
                Location = new Point(18, 178),
                Size = new Size(175, 32),
                BackColor = Color.FromArgb(54, 72, 102),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnTestSupabase.FlatAppearance.BorderSize = 0;
            _btnTestSupabase.Click += OnTestSupabase;

            _btnCopyMobileLink = new Button
            {
                Text = "スマホ設定用ハッシュをコピー",
                Location = new Point(204, 178),
                Size = new Size(215, 32),
                BackColor = Color.FromArgb(46, 98, 86),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnCopyMobileLink.FlatAppearance.BorderSize = 0;
            _btnCopyMobileLink.Click += OnCopyMobileSetup;

            _btnOpenWebUi = new Button
            {
                Text = "ローカルWeb UIを開く",
                Location = new Point(430, 178),
                Size = new Size(171, 32),
                BackColor = Color.FromArgb(62, 68, 84),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnOpenWebUi.FlatAppearance.BorderSize = 0;
            _btnOpenWebUi.Click += (s, e) =>
            {
                try { Process.Start(_sync.LocalWebUrl); } catch { }
            };

            syncCard.Controls.AddRange(new Control[]
            {
                _txtSupabaseUrl, _txtSupabaseKey, _txtSyncKey, btnGenKey,
                _btnTestSupabase, _btnCopyMobileLink, _btnOpenWebUi
            });

            // Behavior Card
            var behCard = new Panel { Location = new Point(16, 248), Size = new Size(620, 190), BackColor = _cardBg };
            scroll.Controls.Add(behCard);

            AddSectionHeader(behCard, "監視対象ブラウザ・摩擦パラメータ・自動起動", 16, 12);

            behCard.Controls.Add(new Label { Text = "対象プロセス (カンマ区切り):", ForeColor = _textSub, Location = new Point(18, 44), AutoSize = true });
            _txtBrowsers = new TextBox { Location = new Point(18, 64), Size = new Size(582, 24) };
            behCard.Controls.Add(_txtBrowsers);

            behCard.Controls.Add(new Label { Text = "Lv.1 待機(秒):", ForeColor = _textSub, Location = new Point(18, 102), AutoSize = true });
            _numLv1Wait = new NumericUpDown { Minimum = 3, Maximum = 120, Value = 15, Location = new Point(122, 99), Size = new Size(65, 24) };

            behCard.Controls.Add(new Label { Text = "Lv.1 許可(分):", ForeColor = _textSub, Location = new Point(206, 102), AutoSize = true });
            _numLv1Allow = new NumericUpDown { Minimum = 1, Maximum = 60, Value = 5, Location = new Point(310, 99), Size = new Size(65, 24) };

            behCard.Controls.Add(new Label { Text = "Lv.2 許可(分):", ForeColor = _textSub, Location = new Point(394, 102), AutoSize = true });
            _numLv2Allow = new NumericUpDown { Minimum = 1, Maximum = 60, Value = 10, Location = new Point(498, 99), Size = new Size(65, 24) };

            _chkStartup = new CheckBox
            {
                Text = "Windows 起動時に FocusGuard を自動起動する",
                Location = new Point(18, 138),
                Size = new Size(340, 26),
                ForeColor = _textMain
            };

            _btnSaveSettings = new Button
            {
                Text = "設定をすべて保存",
                Location = new Point(420, 134),
                Size = new Size(180, 36),
                BackColor = Color.FromArgb(52, 116, 235),
                ForeColor = Color.White,
                Font = new Font("Meiryo UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            _btnSaveSettings.FlatAppearance.BorderSize = 0;
            _btnSaveSettings.Click += OnSaveSettings;

            behCard.Controls.AddRange(new Control[]
            {
                _numLv1Wait, _numLv1Allow, _numLv2Allow, _chkStartup, _btnSaveSettings
            });
        }

        private void AddSectionHeader(Panel parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                Font = new Font("Meiryo UI", 10f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(x, y),
                AutoSize = true
            });
        }

        private void LoadSettingsToUi()
        {
            var cfg = _sync.GetSettings();
            _rbModeLocal.Checked = cfg.SyncMode != "supabase";
            _rbModeSupabase.Checked = cfg.SyncMode == "supabase";
            _txtSupabaseUrl.Text = cfg.SupabaseUrl ?? "";
            _txtSupabaseKey.Text = cfg.SupabaseAnonKey ?? "";
            _txtSyncKey.Text = cfg.SyncKey ?? "";
            _chkStartup.Checked = cfg.StartWithWindows;
        }

        private void RefreshStateUi(GuardState st)
        {
            if (st == null) return;

            _lvSchedules.Items.Clear();
            string[] dayNames = { "日", "月", "火", "水", "木", "金", "土" };
            foreach (var r in st.schedules)
            {
                string daysStr = r.days != null && r.days.Count == 7
                    ? "毎日"
                    : string.Join("", (r.days ?? new List<int>()).OrderBy(d => d).Where(d => d >= 0 && d <= 6).Select(d => dayNames[d]));
                string targetStr = r.target == "pc" ? "PCのみ" : (r.target == "android" ? "スマホのみ" : "両方");

                var item = new ListViewItem(r.enabled ? "ON" : "OFF");
                item.Tag = r.id;
                item.SubItems.Add(r.name ?? "");
                item.SubItems.Add(string.Format("{0} - {1}", r.startTime, r.endTime));
                item.SubItems.Add(daysStr);
                item.SubItems.Add("Lv." + r.level);
                item.SubItems.Add(targetStr);
                item.ForeColor = r.enabled ? Color.White : Color.FromArgb(120, 130, 148);
                _lvSchedules.Items.Add(item);
            }

            _txtBrowsers.Text = string.Join(", ", st.browsers.windows);
            _numLv1Wait.Value = Math.Max(_numLv1Wait.Minimum, Math.Min(_numLv1Wait.Maximum, st.lv1WaitSeconds));
            _numLv1Allow.Value = Math.Max(_numLv1Allow.Minimum, Math.Min(_numLv1Allow.Maximum, st.lv1AllowMinutes));
            _numLv2Allow.Value = Math.Max(_numLv2Allow.Minimum, Math.Min(_numLv2Allow.Maximum, st.lv2AllowMinutes));

            UpdateLiveStatus(RuleEngine.Evaluate(st, "pc", _sync.LocalSnoozeUntilMs));
        }

        private void OnStartManualClick(object sender, EventArgs e)
        {
            int level = _rbLv1.Checked ? 1 : (_rbLv2.Checked ? 2 : 3);
            string target = _cmbTarget.SelectedIndex == 1 ? "pc" : (_cmbTarget.SelectedIndex == 2 ? "android" : "all");
            int[] minutesMap = { 15, 30, 60, 120, 0 };
            int mins = minutesMap[_cmbDuration.SelectedIndex];

            if (level == 3 && mins == 0)
            {
                var confirm = MessageBox.Show(
                    "Lv.3（強制ブロック）で「手動解除まで」を選ぶと、解除ロックがかかります。\r\n安全のため 60分タイマー に切り替えて開始しますか？（「いいえ」でキャンセル）",
                    "Lv.3 安全確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
                mins = 60;
            }

            var st = _sync.GetState();
            long now = TimeUtil.NowMs();
            st.manualSession = new ManualSession
            {
                active = true,
                level = level,
                target = target,
                startedAt = now,
                expiresAt = mins > 0 ? now + (long)mins * 60 * 1000 : 0
            };
            _sync.LocalSnoozeUntilMs = 0;

            string err;
            if (!_sync.UpdateState(st, out err))
            {
                MessageBox.Show(err, "変更できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnStopManualClick(object sender, EventArgs e)
        {
            var st = _sync.GetState();
            st.manualSession.active = false;
            st.manualSession.expiresAt = 0;
            string err;
            if (!_sync.UpdateState(st, out err))
            {
                MessageBox.Show(err, "停止できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnScheduleAdd(object sender, EventArgs e)
        {
            var days = new List<int>();
            for (int i = 0; i < 7; i++)
                if (_chkDays[i].Checked) days.Add(i);

            if (days.Count == 0)
            {
                MessageBox.Show("曜日を1つ以上選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var rule = new ScheduleRule
            {
                name = string.IsNullOrWhiteSpace(_txtSchedName.Text) ? "集中スケジュール" : _txtSchedName.Text.Trim(),
                enabled = true,
                days = days,
                startTime = _txtSchedStart.Text.Trim(),
                endTime = _txtSchedEnd.Text.Trim(),
                level = _cmbSchedLevel.SelectedIndex + 1,
                target = _cmbSchedTarget.SelectedIndex == 1 ? "pc" : (_cmbSchedTarget.SelectedIndex == 2 ? "android" : "all")
            };

            DateTime dummy;
            // Basic time format check
            TimeSpan ts1, ts2;
            if (!TimeSpan.TryParse(rule.startTime, out ts1) || !TimeSpan.TryParse(rule.endTime, out ts2))
            {
                MessageBox.Show("時刻は 23:00 のように HH:mm 形式で入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            dummy = DateTime.Now;
            RuleEngine.IsScheduleActiveNow(rule, dummy, out dummy);

            var st = _sync.GetState();
            st.schedules.Add(rule);
            string err;
            if (!_sync.UpdateState(st, out err))
                MessageBox.Show(err, "追加エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnScheduleToggle(object sender, EventArgs e)
        {
            if (_lvSchedules.SelectedItems.Count == 0) return;
            string id = _lvSchedules.SelectedItems[0].Tag as string;
            var st = _sync.GetState();
            var target = st.schedules.FirstOrDefault(s => s.id == id);
            if (target == null) return;
            target.enabled = !target.enabled;

            string err;
            if (!_sync.UpdateState(st, out err))
                MessageBox.Show(err, "変更できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnScheduleDelete(object sender, EventArgs e)
        {
            if (_lvSchedules.SelectedItems.Count == 0) return;
            string id = _lvSchedules.SelectedItems[0].Tag as string;
            var st = _sync.GetState();
            st.schedules.RemoveAll(s => s.id == id);

            string err;
            if (!_sync.UpdateState(st, out err))
                MessageBox.Show(err, "削除できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private async void OnTestSupabase(object sender, EventArgs e)
        {
            _btnTestSupabase.Enabled = false;
            _btnTestSupabase.Text = "テスト中...";
            var testCfg = new AppSettings
            {
                SyncMode = "supabase",
                SupabaseUrl = _txtSupabaseUrl.Text.Trim(),
                SupabaseAnonKey = _txtSupabaseKey.Text.Trim(),
                SyncKey = _txtSyncKey.Text.Trim()
            };
            string res = await _sync.TestSupabaseConnectionAsync(testCfg);
            _btnTestSupabase.Enabled = true;
            _btnTestSupabase.Text = "Supabase 接続テスト";

            if (res == "OK")
                MessageBox.Show("Supabase RPC への接続に成功しました！", "接続成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(res, "接続テスト結果", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnCopyMobileSetup(object sender, EventArgs e)
        {
            string url = _txtSupabaseUrl.Text.Trim();
            string key = _txtSupabaseKey.Text.Trim();
            string syncKey = _txtSyncKey.Text.Trim();
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(syncKey))
            {
                MessageBox.Show("先に Supabase URL / Anon Key / Sync Key を入力してください。", "情報不足", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string hash = string.Format("#url={0}&key={1}&sync={2}",
                Uri.EscapeDataString(url), Uri.EscapeDataString(key), Uri.EscapeDataString(syncKey));
            Clipboard.SetText(hash);
            MessageBox.Show(
                "スマホ設定用ハッシュをクリップボードにコピーしました！\r\nホスティングした Web リモコンURL の末尾にそのまま貼って開くと、スマホ側が一瞬で設定完了します。",
                "コピー完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnSaveSettings(object sender, EventArgs e)
        {
            var cfg = _sync.GetSettings();
            cfg.SyncMode = _rbModeSupabase.Checked ? "supabase" : "local";
            cfg.SupabaseUrl = _txtSupabaseUrl.Text.Trim();
            cfg.SupabaseAnonKey = _txtSupabaseKey.Text.Trim();
            cfg.SyncKey = _txtSyncKey.Text.Trim();
            cfg.StartWithWindows = _chkStartup.Checked;
            _sync.SaveSettings(cfg);
            ApplyWindowsStartup(cfg.StartWithWindows);

            var st = _sync.GetState();
            var browserList = _txtBrowsers.Text
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim().ToLowerInvariant().Replace(".exe", ""))
                .Where(b => !string.IsNullOrEmpty(b))
                .Distinct()
                .ToList();
            if (browserList.Count > 0) st.browsers.windows = browserList;
            st.lv1WaitSeconds = (int)_numLv1Wait.Value;
            st.lv1AllowMinutes = (int)_numLv1Allow.Value;
            st.lv2AllowMinutes = (int)_numLv2Allow.Value;

            string err;
            if (!_sync.UpdateState(st, out err))
            {
                MessageBox.Show(err, "一部保存できませんでした", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            MessageBox.Show("設定を保存しました。", "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ApplyWindowsStartup(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (enable)
                        key.SetValue("FocusGuard", "\"" + Application.ExecutablePath + "\" --minimized");
                    else
                        key.DeleteValue("FocusGuard", false);
                }
            }
            catch { }
        }

        private void UpdateSyncLabel(string msg, bool ok)
        {
            _lblSyncStatus.Text = "同期ステータス: " + msg;
            _lblSyncStatus.ForeColor = ok ? Color.FromArgb(130, 210, 175) : Color.FromArgb(255, 160, 120);
        }

        private void BeginInvokeSafe(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch { }
        }
    }
}
