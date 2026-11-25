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

        // 左列: 文字盤と端末の状態
        private DialControl _dial;
        private Label _legendNow, _legendSched;
        private Label _lblPcState, _lblPcSub, _lblAndroidState, _lblAndroidSub;
        private Label _lblSyncStatus;
        private PillButton _btnEndSnooze;

        // 右列: リモコン / スケジュール / 設定 の切り替え
        private PillButton[] _navButtons;
        private Control[] _pages;

        // リモコン
        private RoundPanel _lockPanel;
        private LevelButton[] _levelButtons;
        private Label _lblLvDesc, _lblLv3Warn;
        private PillButton[] _durChips;
        private PillButton[] _tgtSegs;
        private PillButton _btnStartManual;
        private PillButton _btnStopManual;
        private int _selLevel = 2;
        private int _selDuration = 30;
        private string _selTarget = "all";
        private static readonly int[] DurValues = { 15, 30, 60, 120, 0 };
        private static readonly string[] DurLabels = { "15分", "30分", "1時間", "2時間", "手動で止めるまで" };
        private static readonly string[] TgtValues = { "all", "pc", "android" };
        private static readonly string[] TgtLabels = { "両方", "PCだけ", "スマホだけ" };
        private static readonly string[] LvDescs =
        {
            "15秒待てば、5分だけ開けられます。",
            "表示された名言を手で打てば、10分だけ開けられます。",
            "すぐに閉じます。終わる時刻までだれも止められません。"
        };

        // スケジュール
        private FlowLayoutPanel _schedList;
        private TextBox _txtSchedName, _txtSchedStart, _txtSchedEnd;
        private ComboBox _cmbSchedLevel, _cmbSchedTarget;
        private PillButton[] _dayChips;
        private PillButton _btnSchedAdd;
        private string _schedSignature = null;

        // 設定
        private RadioButton _rbModeLocal, _rbModeSupabase;
        private TextBox _txtSupabaseUrl, _txtSupabaseKey, _txtSyncKey;
        private PillButton _btnTestSupabase, _btnSaveSettings, _btnCopyMobileLink, _btnOpenWebUi;
        private TextBox _txtBrowsers;
        private NumericUpDown _numLv1Wait, _numLv1Allow, _numLv2Allow;
        private CheckBox _chkStartup;
        private CheckBox _chkTopMost;

        private bool _forceExit;

        // ホワイトリスト
        private TextBox _txtSites;

        private const int RightX = 372;
        private const int PageW = 440;

        public MainForm(SyncService sync, Action requestQuit)
        {
            _sync = sync;
            _requestQuit = requestQuit;

            Text = "FocusGuard";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(RightX + PageW + 40, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Ground;
            ForeColor = Theme.Ink;
            Font = Theme.UI(9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildLayout();
            LoadSettingsToUi();
            // exe の場所が変わっても自動起動できるよう、起動のたびに今の場所で登録し直す
            if (_sync.GetSettings().StartWithWindows) ApplyWindowsStartup(true);
            RefreshStateUi(_sync.GetState());

            _sync.StateChanged += (st) => BeginInvokeSafe(() => RefreshStateUi(st));
            _sync.SyncStatusChanged += (msg, ok) => BeginInvokeSafe(() => UpdateSyncLabel(msg, ok));
        }

        public void AllowExit()
        {
            _forceExit = true;
        }

        public static bool PromptLv2Challenge(string titleMessage)
        {
            Quote quote = Quotes.Pick();

            using (var dlg = new Form())
            {
                dlg.Text = "Lv.2 確認";
                dlg.Size = new Size(500, 380);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = Theme.Ground;
                dlg.ForeColor = Theme.Ink;
                dlg.Font = Theme.UI(9.5f);

                var lblMsg = new Label
                {
                    Text = titleMessage,
                    Location = new Point(20, 16),
                    Size = new Size(444, 38),
                    ForeColor = Theme.Ink
                };
                var lblPhrase = new Label
                {
                    Text = quote.Text,
                    Font = Theme.Serif(12.5f),
                    ForeColor = Theme.Ink,
                    BackColor = Theme.Paper,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(10, 4, 10, 4),
                    Location = new Point(20, 60),
                    Size = new Size(444, 92)
                };
                var lblAuthor = new Label
                {
                    Text = "— " + quote.Author,
                    Font = Theme.UI(8.5f),
                    ForeColor = Theme.Sub,
                    TextAlign = ContentAlignment.MiddleRight,
                    Location = new Point(20, 154),
                    Size = new Size(444, 20)
                };
                var lblHint = new Label
                {
                    Text = Quotes.Progress("", quote),
                    Font = Theme.UI(8.5f),
                    ForeColor = Theme.Sub,
                    Location = new Point(20, 216),
                    Size = new Size(444, 20)
                };
                var txt = new TextBox
                {
                    Font = Theme.UI(11.5f),
                    BackColor = Theme.White,
                    ForeColor = Theme.Ink,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(20, 182),
                    Size = new Size(444, 30),
                    ShortcutsEnabled = false,
                    ImeMode = ImeMode.On
                };
                txt.KeyDown += (s, e) =>
                {
                    if ((e.Control && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert))
                        e.SuppressKeyPress = true;
                };

                var btnOk = new Button
                {
                    Text = "確認して実行",
                    Enabled = false,
                    Location = new Point(236, 252),
                    Size = new Size(126, 36),
                    BackColor = Theme.Ink,
                    ForeColor = Theme.Paper,
                    FlatStyle = FlatStyle.Flat,
                    DialogResult = DialogResult.OK
                };
                btnOk.FlatAppearance.BorderSize = 0;

                var btnCancel = new Button
                {
                    Text = "やめる",
                    Location = new Point(372, 252),
                    Size = new Size(92, 36),
                    BackColor = Theme.Paper,
                    ForeColor = Theme.Ink,
                    FlatStyle = FlatStyle.Flat,
                    DialogResult = DialogResult.Cancel
                };
                btnCancel.FlatAppearance.BorderSize = 1;
                btnCancel.FlatAppearance.BorderColor = Theme.Line;

                txt.TextChanged += (s, e) =>
                {
                    btnOk.Enabled = Quotes.Matches(txt.Text, quote);
                    lblHint.Text = Quotes.Progress(txt.Text, quote);
                    lblHint.ForeColor = btnOk.Enabled ? Theme.Ok : Theme.Sub;
                };

                dlg.Controls.AddRange(new Control[] { lblMsg, lblPhrase, lblAuthor, txt, lblHint, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;
                return dlg.ShowDialog() == DialogResult.OK;
            }
        }

        // ===== 画面の組み立て =====

        private static Label MakeLabel(string text, Font font, Color color, int x, int y, int w, int h)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = color,
                BackColor = Theme.Paper,
                Location = new Point(x, y),
                Size = new Size(w, h),
                AutoSize = false
            };
        }

        private static RoundPanel MakeCard(int height)
        {
            return new RoundPanel
            {
                Fill = Theme.Paper,
                BackColor = Theme.Ground,
                Size = new Size(PageW, height),
                Margin = new Padding(0, 0, 0, 14)
            };
        }

        private static TextBox MakeTextBox(int x, int y, int w)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(w, 30),
                Font = Theme.UI(11f),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.White,
                ForeColor = Theme.Ink
            };
        }

        private void BuildLayout()
        {
            // ---- 左列 ----
            var brand = new Label
            {
                Text = "FocusGuard",
                Font = Theme.Serif(17f),
                ForeColor = Theme.Ink,
                BackColor = Theme.Ground,
                Location = new Point(24, 18),
                AutoSize = true
            };
            Controls.Add(brand);

            _lblSyncStatus = new Label
            {
                Text = _sync.LastSyncMessage,
                Font = Theme.UI(8.5f),
                ForeColor = Theme.Sub,
                BackColor = Theme.Ground,
                Location = new Point(26, 52),
                Size = new Size(320, 20),
                AutoEllipsis = true
            };
            Controls.Add(_lblSyncStatus);

            _dial = new DialControl { Location = new Point(34, 82), Size = new Size(300, 300), BackColor = Theme.Ground };
            Controls.Add(_dial);

            _legendNow = new Label { Text = "■", Font = Theme.UI(8.5f), ForeColor = Theme.Lv(2), BackColor = Theme.Ground, Location = new Point(70, 390), Size = new Size(16, 20) };
            _legendSched = new Label { Text = "■", Font = Theme.UI(8.5f), ForeColor = Theme.Sched, BackColor = Theme.Ground, Location = new Point(186, 390), Size = new Size(16, 20) };
            Controls.Add(_legendNow);
            Controls.Add(_legendSched);
            Controls.Add(new Label { Text = "いまのブロック", Font = Theme.UI(8.5f), ForeColor = Theme.Sub, BackColor = Theme.Ground, Location = new Point(86, 390), Size = new Size(96, 20) });
            Controls.Add(new Label { Text = "今日のスケジュール", Font = Theme.UI(8.5f), ForeColor = Theme.Sub, BackColor = Theme.Ground, Location = new Point(202, 390), Size = new Size(130, 20) });

            var pcCard = new RoundPanel { Fill = Theme.Paper, BackColor = Theme.Ground, Location = new Point(24, 424), Size = new Size(320, 86), Radius = 14f };
            pcCard.Controls.Add(MakeLabel("Windows PC", Theme.UI(8.5f), Theme.Sub, 14, 10, 200, 18));
            _lblPcState = MakeLabel("ブロックなし", Theme.UI(11f, true), Theme.Ink, 14, 30, 200, 24);
            _lblPcSub = MakeLabel("いつも通り使えます", Theme.UI(8.5f), Theme.Sub, 14, 56, 290, 20);
            _btnEndSnooze = new PillButton("一時許可を終える", PillKind.Line)
            {
                Location = new Point(190, 12),
                Size = new Size(118, 34),
                BackColor = Theme.Paper,
                Font = Theme.UI(8.5f),
                Visible = false
            };
            _btnEndSnooze.Click += (s, e) => { _sync.LocalSnoozeUntilMs = 0; };
            pcCard.Controls.AddRange(new Control[] { _lblPcState, _lblPcSub, _btnEndSnooze });
            Controls.Add(pcCard);

            var anCard = new RoundPanel { Fill = Theme.Paper, BackColor = Theme.Ground, Location = new Point(24, 520), Size = new Size(320, 86), Radius = 14f };
            anCard.Controls.Add(MakeLabel("Android", Theme.UI(8.5f), Theme.Sub, 14, 10, 200, 18));
            _lblAndroidState = MakeLabel("ブロックなし", Theme.UI(11f, true), Theme.Ink, 14, 30, 290, 24);
            _lblAndroidSub = MakeLabel("いつも通り使えます", Theme.UI(8.5f), Theme.Sub, 14, 56, 290, 20);
            anCard.Controls.AddRange(new Control[] { _lblAndroidState, _lblAndroidSub });
            Controls.Add(anCard);

            Controls.Add(new Label
            {
                Text = "× で閉じてもタスクトレイで動き続けます。\r\nLv.3 の間は、強制終了されても自動で立ち上がります。",
                Font = Theme.UI(8.5f),
                ForeColor = Theme.Sub,
                BackColor = Theme.Ground,
                Location = new Point(24, 622),
                Size = new Size(320, 44)
            });

            // ---- 右列: 切り替え ----
            var nav = new RoundPanel { Fill = Theme.Well, BackColor = Theme.Ground, Location = new Point(RightX, 20), Size = new Size(PageW, 52), Radius = 12f };
            string[] navLabels = { "リモコン", "スケジュール", "設定" };
            _navButtons = new PillButton[3];
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                _navButtons[i] = new PillButton(navLabels[i], PillKind.Segment)
                {
                    Location = new Point(4 + i * 144, 4),
                    Size = new Size(140, 44),
                    BackColor = Theme.Well,
                    Font = Theme.UI(10f)
                };
                _navButtons[i].Click += (s, e) => ShowPage(idx);
                nav.Controls.Add(_navButtons[i]);
            }
            Controls.Add(nav);

            _pages = new Control[] { BuildRemotePage(), BuildSchedulePage(), BuildSettingsPage() };
            foreach (var p in _pages) Controls.Add(p);
            ShowPage(0);
            RenderStartControls();
        }

        private FlowLayoutPanel NewPage()
        {
            return new FlowLayoutPanel
            {
                Location = new Point(RightX, 88),
                Size = new Size(PageW + 28, 700 - 88),
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Theme.Ground,
                Padding = new Padding(0, 0, 0, 16)
            };
        }

        private void ShowPage(int idx)
        {
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visible = i == idx;
                _navButtons[i].Selected = i == idx;
            }
        }

        private Control BuildRemotePage()
        {
            var page = NewPage();

            _lockPanel = MakeCard(150);
            _lockPanel.Controls.Add(MakeLabel("ロック中にできること", Theme.Serif(13f), Theme.Ink, 18, 14, 404, 30));
            _lockPanel.Controls.Add(MakeLabel("延ばすことと、止めるアプリを増やすことはできます。短くする・弱める・止めることはできません。", Theme.UI(9f), Theme.Sub, 18, 48, 404, 40));
            var ext30 = new PillButton("30分延ばす", PillKind.Strong) { Location = new Point(18, 94), Size = new Size(198, 44), BackColor = Theme.Paper };
            var ext60 = new PillButton("1時間延ばす", PillKind.Strong) { Location = new Point(224, 94), Size = new Size(198, 44), BackColor = Theme.Paper };
            ext30.Click += (s, e) => ExtendHardLock(30);
            ext60.Click += (s, e) => ExtendHardLock(60);
            _lockPanel.Controls.AddRange(new Control[] { ext30, ext60 });
            _lockPanel.Visible = false;
            page.Controls.Add(_lockPanel);

            var card = MakeCard(572);
            card.Controls.Add(MakeLabel("新しくブロックする", Theme.Serif(13f), Theme.Ink, 18, 14, 404, 30));

            card.Controls.Add(MakeLabel("強さ", Theme.UI(9.5f, true), Theme.Ink, 18, 56, 200, 20));
            _levelButtons = new LevelButton[3];
            for (int i = 0; i < 3; i++)
            {
                int lv = i + 1;
                _levelButtons[i] = new LevelButton(lv) { Location = new Point(18 + i * 138, 80), Size = new Size(128, 76), BackColor = Theme.Paper };
                _levelButtons[i].Click += (s, e) => { _selLevel = lv; RenderStartControls(); };
                card.Controls.Add(_levelButtons[i]);
            }
            _lblLvDesc = MakeLabel("", Theme.UI(9f), Theme.Sub, 18, 162, 404, 40);
            card.Controls.Add(_lblLvDesc);

            card.Controls.Add(MakeLabel("長さ", Theme.UI(9.5f, true), Theme.Ink, 18, 208, 200, 20));
            _durChips = new PillButton[DurValues.Length];
            int[] chipW = { 64, 64, 78, 78, 160 };
            int cx = 18, cy = 232;
            for (int i = 0; i < DurValues.Length; i++)
            {
                if (cx + chipW[i] > 422) { cx = 18; cy += 52; }
                int v = DurValues[i];
                _durChips[i] = new PillButton(DurLabels[i], PillKind.Chip) { Location = new Point(cx, cy), Size = new Size(chipW[i], 44), BackColor = Theme.Paper };
                _durChips[i].Click += (s, e) => { _selDuration = v; RenderStartControls(); };
                card.Controls.Add(_durChips[i]);
                cx += chipW[i] + 8;
            }

            card.Controls.Add(MakeLabel("止める端末", Theme.UI(9.5f, true), Theme.Ink, 18, 336, 200, 20));
            var seg = new RoundPanel { Fill = Theme.Well, BackColor = Theme.Paper, Location = new Point(18, 360), Size = new Size(404, 52), Radius = 12f };
            _tgtSegs = new PillButton[3];
            for (int i = 0; i < 3; i++)
            {
                string v = TgtValues[i];
                _tgtSegs[i] = new PillButton(TgtLabels[i], PillKind.Segment) { Location = new Point(4 + i * 132, 4), Size = new Size(132, 44), BackColor = Theme.Well };
                _tgtSegs[i].Click += (s, e) => { _selTarget = v; RenderStartControls(); };
                seg.Controls.Add(_tgtSegs[i]);
            }
            card.Controls.Add(seg);

            _lblLv3Warn = MakeLabel("Lv.3 は途中で止められないので、手動解除ではなく1時間で始めます。", Theme.UI(8.5f), Theme.LvText(3), 18, 420, 404, 22);
            _lblLv3Warn.Visible = false;
            card.Controls.Add(_lblLv3Warn);

            _btnStartManual = new PillButton("30分ブロックする", PillKind.Primary) { Location = new Point(18, 448), Size = new Size(404, 52), BackColor = Theme.Paper };
            _btnStartManual.Click += OnStartManualClick;
            _btnStopManual = new PillButton("手動のブロックはありません", PillKind.Line) { Location = new Point(18, 508), Size = new Size(404, 46), BackColor = Theme.Paper };
            _btnStopManual.Click += OnStopManualClick;
            card.Controls.AddRange(new Control[] { _btnStartManual, _btnStopManual });
            page.Controls.Add(card);
            return page;
        }

        private void RenderStartControls()
        {
            for (int i = 0; i < 3; i++) _levelButtons[i].Selected = _levelButtons[i].Level == _selLevel;
            for (int i = 0; i < DurValues.Length; i++) _durChips[i].Selected = DurValues[i] == _selDuration;
            for (int i = 0; i < 3; i++) _tgtSegs[i].Selected = TgtValues[i] == _selTarget;
            _lblLvDesc.Text = LvDescs[_selLevel - 1];
            bool lv3NoEnd = _selLevel == 3 && _selDuration == 0;
            _lblLv3Warn.Visible = lv3NoEnd;
            if (lv3NoEnd) _btnStartManual.Text = "1時間ブロックする";
            else if (_selDuration == 0) _btnStartManual.Text = "手動で止めるまでブロックする";
            else _btnStartManual.Text = DurLabels[Array.IndexOf(DurValues, _selDuration)] + "ブロックする";
            _btnStartManual.Accent = Theme.Lv(_selLevel);
        }

        private Control BuildSchedulePage()
        {
            var page = NewPage();
            page.Controls.Add(new Label
            {
                Text = "スケジュール",
                Font = Theme.Serif(13f),
                ForeColor = Theme.Ink,
                BackColor = Theme.Ground,
                Size = new Size(PageW, 34),
                Margin = new Padding(0, 0, 0, 8)
            });

            _schedList = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Ground,
                Margin = new Padding(0, 0, 0, 4)
            };
            page.Controls.Add(_schedList);

            var add = MakeCard(372);
            add.Controls.Add(MakeLabel("スケジュールを追加", Theme.Serif(12f), Theme.Ink, 18, 14, 404, 28));
            add.Controls.Add(MakeLabel("名前", Theme.UI(8.5f), Theme.Sub, 18, 52, 200, 18));
            _txtSchedName = MakeTextBox(18, 72, 404);
            _txtSchedName.Text = "夜のデジタルデトックス";
            add.Controls.Add(MakeLabel("始まり（例 23:00）", Theme.UI(8.5f), Theme.Sub, 18, 112, 196, 18));
            add.Controls.Add(MakeLabel("終わり（翌日でもOK）", Theme.UI(8.5f), Theme.Sub, 226, 112, 196, 18));
            _txtSchedStart = MakeTextBox(18, 132, 196);
            _txtSchedStart.Text = "23:00";
            _txtSchedEnd = MakeTextBox(226, 132, 196);
            _txtSchedEnd.Text = "07:00";
            add.Controls.Add(MakeLabel("強さ", Theme.UI(8.5f), Theme.Sub, 18, 172, 196, 18));
            add.Controls.Add(MakeLabel("止める端末", Theme.UI(8.5f), Theme.Sub, 226, 172, 196, 18));
            _cmbSchedLevel = new ComboBox { Location = new Point(18, 192), Size = new Size(196, 30), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.UI(10f) };
            _cmbSchedLevel.Items.AddRange(new object[] { "Lv.1 気づき", "Lv.2 ひと手間", "Lv.3 強制" });
            _cmbSchedLevel.SelectedIndex = 1;
            _cmbSchedTarget = new ComboBox { Location = new Point(226, 192), Size = new Size(196, 30), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.UI(10f) };
            _cmbSchedTarget.Items.AddRange(new object[] { "両方", "PCだけ", "スマホだけ" });
            _cmbSchedTarget.SelectedIndex = 0;
            add.Controls.AddRange(new Control[] { _txtSchedName, _txtSchedStart, _txtSchedEnd, _cmbSchedLevel, _cmbSchedTarget });

            add.Controls.Add(MakeLabel("曜日", Theme.UI(8.5f), Theme.Sub, 18, 232, 196, 18));
            string[] dayLabels = { "日", "月", "火", "水", "木", "金", "土" };
            _dayChips = new PillButton[7];
            for (int i = 0; i < 7; i++)
            {
                var chip = new PillButton(dayLabels[i], PillKind.Chip) { Location = new Point(18 + i * 52, 254), Size = new Size(44, 44), BackColor = Theme.Paper, Selected = true };
                chip.Click += (s, e) => { chip.Selected = !chip.Selected; };
                _dayChips[i] = chip;
                add.Controls.Add(chip);
            }

            _btnSchedAdd = new PillButton("スケジュールを保存", PillKind.Primary) { Location = new Point(18, 308), Size = new Size(404, 48), BackColor = Theme.Paper };
            _btnSchedAdd.Click += OnScheduleAdd;
            add.Controls.Add(_btnSchedAdd);
            page.Controls.Add(add);
            return page;
        }

        private Control BuildSettingsPage()
        {
            var page = NewPage();

            var conn = MakeCard(380);
            conn.Controls.Add(MakeLabel("接続", Theme.Serif(12f), Theme.Ink, 18, 14, 404, 28));
            _rbModeLocal = new RadioButton { Text = "この PC だけで使う", Location = new Point(18, 52), Size = new Size(200, 26), BackColor = Theme.Paper, ForeColor = Theme.Ink, Font = Theme.UI(9.5f) };
            _rbModeSupabase = new RadioButton { Text = "Supabase で同期する", Location = new Point(224, 52), Size = new Size(200, 26), BackColor = Theme.Paper, ForeColor = Theme.Ink, Font = Theme.UI(9.5f) };
            conn.Controls.AddRange(new Control[] { _rbModeLocal, _rbModeSupabase });

            conn.Controls.Add(MakeLabel("Supabase URL", Theme.UI(8.5f), Theme.Sub, 18, 88, 300, 18));
            _txtSupabaseUrl = MakeTextBox(18, 108, 404);
            conn.Controls.Add(MakeLabel("Anon Key", Theme.UI(8.5f), Theme.Sub, 18, 146, 300, 18));
            _txtSupabaseKey = MakeTextBox(18, 166, 404);
            conn.Controls.Add(MakeLabel("Sync Key（PC とスマホで同じ合言葉）", Theme.UI(8.5f), Theme.Sub, 18, 204, 400, 18));
            _txtSyncKey = MakeTextBox(18, 224, 298);
            var btnGenKey = new PillButton("ランダム生成", PillKind.Line) { Location = new Point(324, 220), Size = new Size(98, 38), BackColor = Theme.Paper, Font = Theme.UI(9f) };
            btnGenKey.Click += (s, e) => { _txtSyncKey.Text = Guid.NewGuid().ToString("N").Substring(0, 16); };

            _btnTestSupabase = new PillButton("Supabase 接続テスト", PillKind.Line) { Location = new Point(18, 270), Size = new Size(198, 44), BackColor = Theme.Paper };
            _btnTestSupabase.Click += OnTestSupabase;
            _btnCopyMobileLink = new PillButton("スマホ用の設定をコピー", PillKind.Line) { Location = new Point(224, 270), Size = new Size(198, 44), BackColor = Theme.Paper };
            _btnCopyMobileLink.Click += OnCopyMobileSetup;
            _btnOpenWebUi = new PillButton("この PC の Web リモコンを開く", PillKind.Line) { Location = new Point(18, 322), Size = new Size(404, 44), BackColor = Theme.Paper };
            _btnOpenWebUi.Click += (s, e) =>
            {
                try { Process.Start(_sync.LocalWebUrl); } catch { }
            };
            conn.Controls.AddRange(new Control[]
            {
                _txtSupabaseUrl, _txtSupabaseKey, _txtSyncKey, btnGenKey,
                _btnTestSupabase, _btnCopyMobileLink, _btnOpenWebUi
            });
            page.Controls.Add(conn);

            var sites = MakeCard(300);
            sites.Controls.Add(MakeLabel("ブロック中でも開けるサイト", Theme.Serif(12f), Theme.Ink, 18, 14, 404, 28));
            sites.Controls.Add(MakeLabel("仕事で必要なサイトを1行に1つ（例: example.com）。どの強さのブロック中でも、Chrome・Edge・Brave・Firefox でこのサイトだけ開けます。ほかのサイトのタブは閉じられ、ほかのブラウザはアプリごと止まります。",
                Theme.UI(8.5f), Theme.Sub, 18, 46, 404, 56));
            _txtSites = MakeTextBox(18, 106, 404);
            _txtSites.Multiline = true;
            _txtSites.ScrollBars = ScrollBars.Vertical;
            _txtSites.Height = 120;
            sites.Controls.Add(_txtSites);
            var btnSaveSites = new PillButton("開けるサイトを保存", PillKind.Primary) { Location = new Point(18, 236), Size = new Size(404, 48), BackColor = Theme.Paper };
            btnSaveSites.Click += OnSaveAllowedSites;
            sites.Controls.Add(btnSaveSites);
            page.Controls.Add(sites);

            var apps = MakeCard(396);
            apps.Controls.Add(MakeLabel("止めるアプリと待ち時間", Theme.Serif(12f), Theme.Ink, 18, 14, 404, 28));
            apps.Controls.Add(MakeLabel("止めるプロセス名（カンマ区切り）", Theme.UI(8.5f), Theme.Sub, 18, 52, 400, 18));
            _txtBrowsers = MakeTextBox(18, 72, 404);
            _txtBrowsers.Multiline = true;
            _txtBrowsers.Height = 56;
            apps.Controls.Add(_txtBrowsers);

            var btnPickRunning = new PillButton("起動中のアプリから選ぶ", PillKind.Line) { Location = new Point(18, 136), Size = new Size(198, 42), BackColor = Theme.Paper, Font = Theme.UI(9f) };
            btnPickRunning.Click += OnPickRunningApps;
            var btnAddGamePreset = new PillButton("定番のゲーム・SNSを追加", PillKind.Line) { Location = new Point(224, 136), Size = new Size(198, 42), BackColor = Theme.Paper, Font = Theme.UI(9f) };
            btnAddGamePreset.Click += OnAddGamePresets;
            apps.Controls.AddRange(new Control[] { btnPickRunning, btnAddGamePreset });

            apps.Controls.Add(MakeLabel("Lv.1 の待ち時間（秒）", Theme.UI(8.5f), Theme.Sub, 18, 192, 130, 18));
            apps.Controls.Add(MakeLabel("Lv.1 で開ける（分）", Theme.UI(8.5f), Theme.Sub, 156, 192, 130, 18));
            apps.Controls.Add(MakeLabel("Lv.2 で開ける（分）", Theme.UI(8.5f), Theme.Sub, 294, 192, 130, 18));
            _numLv1Wait = new NumericUpDown { Minimum = 3, Maximum = 120, Value = 15, Location = new Point(18, 214), Size = new Size(96, 30), Font = Theme.UI(10f) };
            _numLv1Allow = new NumericUpDown { Minimum = 1, Maximum = 60, Value = 5, Location = new Point(156, 214), Size = new Size(96, 30), Font = Theme.UI(10f) };
            _numLv2Allow = new NumericUpDown { Minimum = 1, Maximum = 60, Value = 10, Location = new Point(294, 214), Size = new Size(96, 30), Font = Theme.UI(10f) };

            _chkStartup = new CheckBox
            {
                Text = "Windows の起動時に自動で起動する",
                Location = new Point(18, 256),
                Size = new Size(404, 28),
                BackColor = Theme.Paper,
                ForeColor = Theme.Ink,
                Font = Theme.UI(9.5f)
            };

            _chkTopMost = new CheckBox
            {
                Text = "このウィンドウを常に最前面に表示する",
                Location = new Point(18, 286),
                Size = new Size(404, 28),
                BackColor = Theme.Paper,
                ForeColor = Theme.Ink,
                Font = Theme.UI(9.5f)
            };
            // 保存ボタンを待たずに、すぐ反映して覚えておく
            _chkTopMost.CheckedChanged += (s, e) =>
            {
                TopMost = _chkTopMost.Checked;
                var cfgNow = _sync.GetSettings();
                if (cfgNow.AlwaysOnTop != _chkTopMost.Checked)
                {
                    cfgNow.AlwaysOnTop = _chkTopMost.Checked;
                    _sync.SaveSettings(cfgNow);
                }
            };

            _btnSaveSettings = new PillButton("設定を保存", PillKind.Primary) { Location = new Point(18, 330), Size = new Size(404, 48), BackColor = Theme.Paper };
            _btnSaveSettings.Click += OnSaveSettings;
            apps.Controls.AddRange(new Control[] { _numLv1Wait, _numLv1Allow, _numLv2Allow, _chkStartup, _chkTopMost, _btnSaveSettings });
            page.Controls.Add(apps);
            return page;
        }

        // ===== 状態の表示 =====

        private static DateTime ToLocal(long ms)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime();
        }

        private static string Clock(long ms)
        {
            var d = ToLocal(ms);
            return d.Hour + ":" + d.Minute.ToString("00");
        }

        private static int ParseMin(string s)
        {
            TimeSpan ts;
            return TimeSpan.TryParse(s ?? "", out ts) ? (int)ts.TotalMinutes : -1;
        }

        public void UpdateLiveStatus(EffectiveBlockState effPc, EffectiveBlockState effAndroid, DeviceHeartbeat androidHb)
        {
            BeginInvokeSafe(() =>
            {
                bool hardLocked = effPc.IsHardLocked || effAndroid.IsHardLocked;
                Theme.Apply(this, hardLocked);
                _lockPanel.Visible = hardLocked;

                // 端末ごとの状態
                SetDevice(_lblPcState, _lblPcSub, effPc, null);
                if (effPc.IsActive && effPc.IsSnoozed)
                {
                    _lblPcState.Text = "一時的に開けています";
                    _lblPcState.ForeColor = Theme.Ink;
                    _lblPcSub.Text = string.Format("あと {0} 秒で Lv.{1} に戻ります", effPc.SnoozeRemainingSeconds, effPc.Level);
                }
                _btnEndSnooze.Visible = effPc.IsActive && effPc.IsSnoozed;
                SetDevice(_lblAndroidState, _lblAndroidSub, effAndroid, FormatHeartbeat(androidHb));

                // 文字盤
                var st = _sync.GetState();
                var top = (effAndroid.IsActive && (!effPc.IsActive || effAndroid.Level > effPc.Level)) ? effAndroid : effPc;
                UpdateDial(st, top);

                // 止めるボタン
                long now = TimeUtil.NowMs();
                bool pcStoppable = GuardState.IsLive(st.SessionFor("pc"), now) && !effPc.IsHardLocked;
                bool anStoppable = GuardState.IsLive(st.SessionFor("android"), now) && !effAndroid.IsHardLocked;
                bool manualOn = pcStoppable || anStoppable;
                bool lv2Active = (pcStoppable && effPc.Level == 2) || (anStoppable && effAndroid.Level == 2);
                _btnStopManual.Enabled = manualOn;
                if (hardLocked && !manualOn)
                {
                    long lockEnd = Math.Max(
                        effPc.IsHardLocked && effPc.EndsAtMs.HasValue ? effPc.EndsAtMs.Value : 0,
                        effAndroid.IsHardLocked && effAndroid.EndsAtMs.HasValue ? effAndroid.EndsAtMs.Value : 0);
                    _btnStopManual.Text = lockEnd > 0 ? string.Format("ブロックを止める（{0} から）", Clock(lockEnd)) : "ブロックを止める（ロック中）";
                }
                else if (manualOn)
                {
                    string which = pcStoppable && anStoppable ? "" : (pcStoppable ? "PC の" : "スマホの");
                    _btnStopManual.Text = string.Format(lv2Active ? "{0}手動のブロックを止める（名言の入力あり）" : "{0}手動のブロックを止める", which);
                }
                else
                {
                    _btnStopManual.Text = "手動のブロックはありません";
                }
            });
        }

        private void SetDevice(Label state, Label sub, EffectiveBlockState eff, string extra)
        {
            if (eff.IsActive)
            {
                state.Text = string.Format("Lv.{0} ブロック中", eff.Level);
                state.ForeColor = Theme.LvText(eff.Level);
                sub.Text = eff.EndsAtMs.HasValue && eff.EndsAtMs.Value > 0 ? Clock(eff.EndsAtMs.Value) + " まで" : "手動で止めるまで";
            }
            else
            {
                state.Text = "ブロックなし";
                state.ForeColor = Theme.Ink;
                sub.Text = "いつも通り使えます";
            }
            if (!string.IsNullOrEmpty(extra)) sub.Text += "　" + extra;
        }

        private void UpdateDial(GuardState st, EffectiveBlockState top)
        {
            var nowLocal = DateTime.Now;
            double nowMin = nowLocal.Hour * 60 + nowLocal.Minute + nowLocal.Second / 60.0;
            int dow = (int)nowLocal.DayOfWeek, yest = (dow + 6) % 7;

            _dial.Arcs.Clear();
            foreach (var r in st.schedules)
            {
                if (!r.enabled || r.days == null) continue;
                int sm = ParseMin(r.startTime), em = ParseMin(r.endTime);
                if (sm < 0 || em < 0) continue;
                if (sm == em) { if (r.days.Contains(dow)) _dial.Arcs.Add(new DialControl.Arc(0, 1440, Theme.Sched)); continue; }
                if (sm < em) { if (r.days.Contains(dow)) _dial.Arcs.Add(new DialControl.Arc(sm, em - sm, Theme.Sched)); continue; }
                if (r.days.Contains(dow)) _dial.Arcs.Add(new DialControl.Arc(sm, 1440 - sm, Theme.Sched));
                if (r.days.Contains(yest)) _dial.Arcs.Add(new DialControl.Arc(0, em, Theme.Sched));
            }

            bool hasEnd = top.EndsAtMs.HasValue && top.EndsAtMs.Value > 0;
            if (top.IsActive)
            {
                double len = hasEnd ? (top.EndsAtMs.Value - TimeUtil.NowMs()) / 60000.0 : 1440;
                _dial.Arcs.Add(new DialControl.Arc(nowMin, Math.Min(len, 1439.5), Theme.Lv(top.Level)));
                _dial.CenterLabel = hasEnd ? "ブロック終了まで" : "ブロック中";
                if (hasEnd)
                {
                    var left = TimeSpan.FromMilliseconds(Math.Max(0, top.EndsAtMs.Value - TimeUtil.NowMs()));
                    _dial.CenterTime = left.TotalHours >= 1
                        ? string.Format("{0}:{1:00}:{2:00}", (int)left.TotalHours, left.Minutes, left.Seconds)
                        : string.Format("{0}:{1:00}", left.Minutes, left.Seconds);
                }
                else _dial.CenterTime = "手動解除まで";
                _dial.CenterTimeSmall = !hasEnd;
                _dial.CenterState = top.Level >= 3
                    ? (hasEnd ? Clock(top.EndsAtMs.Value) + " まで解除できません" : "解除できません")
                    : string.Format("Lv.{0} {1}で止めています", top.Level, Theme.LvName(top.Level));
                _dial.CenterStateColor = Theme.LvText(top.Level);
            }
            else
            {
                string nextTime = null, nextName = null;
                int nextAdd = 0;
                int nowMinInt = nowLocal.Hour * 60 + nowLocal.Minute;
                for (int add = 0; add < 8 && nextTime == null; add++)
                {
                    int d = (dow + add) % 7;
                    int best = int.MaxValue;
                    foreach (var r in st.schedules)
                    {
                        if (!r.enabled || r.days == null || !r.days.Contains(d)) continue;
                        int sm = ParseMin(r.startTime);
                        if (sm < 0 || (add == 0 && sm <= nowMinInt)) continue;
                        if (sm < best) { best = sm; nextTime = r.startTime; nextName = r.name; nextAdd = add; }
                    }
                }
                _dial.CenterLabel = "いまはブロックしていません";
                _dial.CenterTimeSmall = true;
                if (nextTime != null)
                {
                    _dial.CenterTime = (nextAdd == 0 ? "" : nextAdd == 1 ? "明日 " : nextAdd + "日後 ") + nextTime;
                    _dial.CenterState = "から「" + (string.IsNullOrEmpty(nextName) ? "スケジュール" : nextName) + "」";
                }
                else
                {
                    _dial.CenterTime = "自由時間";
                    _dial.CenterState = "スケジュールはありません";
                }
                _dial.CenterStateColor = Theme.Sub;
            }
            _legendNow.ForeColor = Theme.Lv(top.IsActive ? top.Level : _selLevel);
            _legendSched.ForeColor = Theme.Sched;
            _dial.NowMin = nowMin;
            _dial.ShowLock = top.IsActive && top.Level >= 3;
            _dial.AccessibleName = _dial.CenterLabel + " " + _dial.CenterTime + " " + _dial.CenterState;
            _dial.Invalidate();
        }

        private string FormatHeartbeat(DeviceHeartbeat hb)
        {
            if (hb == null || hb.lastSeen <= 0) return "（未接続）";
            long secAgo = Math.Max(0, (TimeUtil.NowMs() - hb.lastSeen) / 1000);
            string ago = secAgo < 60 ? (secAgo + "秒前")
                : (secAgo < 3600 ? ((secAgo / 60) + "分前") : ((secAgo / 3600) + "時間前"));
            return "（" + ago + "に通信）";
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

        private void LoadSettingsToUi()
        {
            var cfg = _sync.GetSettings();
            _rbModeLocal.Checked = cfg.SyncMode != "supabase";
            _rbModeSupabase.Checked = cfg.SyncMode == "supabase";
            _txtSupabaseUrl.Text = cfg.SupabaseUrl ?? "";
            _txtSupabaseKey.Text = cfg.SupabaseAnonKey ?? "";
            _txtSyncKey.Text = cfg.SyncKey ?? "";
            _chkStartup.Checked = cfg.StartWithWindows;
            _chkTopMost.Checked = cfg.AlwaysOnTop;
            TopMost = cfg.AlwaysOnTop;
        }

        private void RefreshStateUi(GuardState st)
        {
            if (st == null) return;

            string sig = string.Join("|", st.schedules.Select(r => string.Format("{0},{1},{2},{3},{4},{5},{6},{7}",
                r.id, r.name, r.enabled, r.startTime, r.endTime, r.level, r.target,
                string.Join("", (r.days ?? new List<int>()).Select(d => d.ToString()).ToArray()))).ToArray());
            if (sig != _schedSignature)
            {
                _schedSignature = sig;
                _schedList.SuspendLayout();
                foreach (Control c in _schedList.Controls) c.Dispose();
                _schedList.Controls.Clear();
                if (st.schedules.Count == 0)
                {
                    _schedList.Controls.Add(new Label
                    {
                        Text = "まだありません。夜や作業時間など、毎日決まった時間に止めたいときに追加してください。",
                        Font = Theme.UI(9f),
                        ForeColor = Theme.Sub,
                        BackColor = Theme.Ground,
                        Size = new Size(PageW, 44),
                        Margin = new Padding(0, 0, 0, 8)
                    });
                }
                foreach (var r in st.schedules)
                {
                    var row = new ScheduleRow(r, PageW) { BackColor = Theme.Ground };
                    row.Switch.BackColor = Theme.Paper;
                    row.Delete.BackColor = Theme.Paper;
                    string id = r.id;
                    string name = r.name ?? "スケジュール";
                    row.Switch.Click += (s, e) => OnScheduleToggle(id);
                    row.Delete.Click += (s, e) => OnScheduleDelete(id, name);
                    _schedList.Controls.Add(row);
                }
                _schedList.ResumeLayout();
            }

            _txtBrowsers.Text = string.Join(", ", st.browsers.windows);
            if (!_txtSites.Focused)
                _txtSites.Text = string.Join("\r\n", (st.allowedSites ?? new List<string>()).ToArray());
            _numLv1Wait.Value = Math.Max(_numLv1Wait.Minimum, Math.Min(_numLv1Wait.Maximum, st.lv1WaitSeconds));
            _numLv1Allow.Value = Math.Max(_numLv1Allow.Minimum, Math.Min(_numLv1Allow.Maximum, st.lv1AllowMinutes));
            _numLv2Allow.Value = Math.Max(_numLv2Allow.Minimum, Math.Min(_numLv2Allow.Maximum, st.lv2AllowMinutes));

            UpdateLiveStatus(
                RuleEngine.Evaluate(st, "pc", _sync.LocalSnoozeUntilMs),
                RuleEngine.Evaluate(st, "android", 0),
                st.devices != null ? st.devices.android : null);
        }

        // ===== 操作 =====

        private void OnStartManualClick(object sender, EventArgs e)
        {
            int level = _selLevel;
            string target = _selTarget;
            int mins = _selDuration;
            if (level == 3 && mins == 0) mins = 60;

            var st = _sync.GetState();
            long now = TimeUtil.NowMs();
            // 選んだ端末の枠だけを書き換える（もう片方の端末のブロックはそのまま）
            st.StartManual(level, target, now, mins > 0 ? now + (long)mins * 60 * 1000 : 0);
            if (target != "android") _sync.LocalSnoozeUntilMs = 0;

            string err;
            if (!_sync.UpdateState(st, out err, false))
            {
                MessageBox.Show(err, "始められません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Lv.3 ロック中に終了時刻を延ばす（短縮・解除はできない）</summary>
        private void ExtendHardLock(int mins)
        {
            var cur = _sync.GetState();
            var effPc = RuleEngine.Evaluate(cur, "pc", 0);
            var effAn = RuleEngine.Evaluate(cur, "android", 0);
            bool pcLocked = effPc.IsHardLocked, anLocked = effAn.IsHardLocked;
            if (!pcLocked && !anLocked) return;
            if ((pcLocked && !(effPc.EndsAtMs.HasValue && effPc.EndsAtMs.Value > 0)) ||
                (anLocked && !(effAn.EndsAtMs.HasValue && effAn.EndsAtMs.Value > 0)))
            {
                MessageBox.Show("手動で止めるまで続くブロックなので、延ばす必要はありません。", "延長", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // ロック中の端末それぞれの終了時刻から延ばす（ロックしていない端末は変えない）
            long now = TimeUtil.NowMs();
            if (pcLocked) cur.SetSession("pc", 3, now, Math.Max(now, effPc.EndsAtMs.Value) + (long)mins * 60 * 1000);
            if (anLocked) cur.SetSession("android", 3, now, Math.Max(now, effAn.EndsAtMs.Value) + (long)mins * 60 * 1000);
            string err;
            if (!_sync.UpdateState(cur, out err, false))
                MessageBox.Show(err, "延ばせません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnStopManualClick(object sender, EventArgs e)
        {
            var cur = _sync.GetState();
            long now = TimeUtil.NowMs();
            var effPc = RuleEngine.Evaluate(cur, "pc", 0);
            var effAn = RuleEngine.Evaluate(cur, "android", 0);
            // 止められるのは、手動ブロック中で Lv.3 のロックがかかっていない端末だけ
            bool stopPc = GuardState.IsLive(cur.SessionFor("pc"), now) && !effPc.IsHardLocked;
            bool stopAn = GuardState.IsLive(cur.SessionFor("android"), now) && !effAn.IsHardLocked;
            if (!stopPc && !stopAn)
            {
                MessageBox.Show(effPc.IsHardLocked || effAn.IsHardLocked
                        ? "Lv.3 のブロック中は、終わる時刻まで止められません。"
                        : "止められる手動のブロックはありません。",
                    "止められません", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool needsLv2Challenge = (stopPc && effPc.Level == 2) || (stopAn && effAn.Level == 2);
            bool passed = false;
            if (needsLv2Challenge)
            {
                passed = PromptLv2Challenge("Lv.2 のブロックを止めるには、下の言葉を手で入力してください（貼り付けはできません）:");
                if (!passed) return;
            }

            var st = _sync.GetState();
            if (stopPc) { st.SessionFor("pc").active = false; st.SessionFor("pc").expiresAt = 0; }
            if (stopAn) { st.SessionFor("android").active = false; st.SessionFor("android").expiresAt = 0; }
            string err;
            if (!_sync.UpdateState(st, out err, passed))
            {
                MessageBox.Show(err, "止められません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnScheduleAdd(object sender, EventArgs e)
        {
            var days = new List<int>();
            for (int i = 0; i < 7; i++)
                if (_dayChips[i].Selected) days.Add(i);

            if (days.Count == 0)
            {
                MessageBox.Show("曜日を1つ以上選んでください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            TimeSpan ts1, ts2;
            if (!TimeSpan.TryParse(rule.startTime, out ts1) || !TimeSpan.TryParse(rule.endTime, out ts2))
            {
                MessageBox.Show("時刻は 23:00 のように「時:分」で入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var st = _sync.GetState();
            st.schedules.Add(rule);
            string err;
            if (!_sync.UpdateState(st, out err, false))
                MessageBox.Show(err, "追加できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void OnScheduleToggle(string id)
        {
            var st = _sync.GetState();
            var target = st.schedules.FirstOrDefault(s => s.id == id);
            if (target == null) return;
            target.enabled = !target.enabled;

            ApplyScheduleChangeWithLv2Check(st, "動いている Lv.2 のスケジュールをオフにするには、下の言葉を手で入力してください:");
            _schedSignature = null;
            RefreshStateUi(_sync.GetState());
        }

        private void OnScheduleDelete(string id, string name)
        {
            if (MessageBox.Show(string.Format("「{0}」を削除しますか？", name), "スケジュールの削除",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var st = _sync.GetState();
            st.schedules.RemoveAll(s => s.id == id);

            ApplyScheduleChangeWithLv2Check(st, "動いている Lv.2 のスケジュールを削除するには、下の言葉を手で入力してください:");
        }

        private bool ApplyScheduleChangeWithLv2Check(GuardState proposed, string challengePrompt)
        {
            string err;
            if (_sync.UpdateState(proposed, out err, false)) return true;

            // Lv.3 ではなく Lv.2 だけが理由なら、名言の入力で変更できる
            if (!RuleEngine.WouldWeakenHardLock(_sync.GetState(), proposed))
            {
                if (PromptLv2Challenge(challengePrompt))
                {
                    if (_sync.UpdateState(proposed, out err, true)) return true;
                    MessageBox.Show(err, "変更できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return false;
            }
            MessageBox.Show(err, "変更できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void OnSaveAllowedSites(object sender, EventArgs e)
        {
            var lines = _txtSites.Text.Split(new[] { '\r', '\n', ',', ' ', '　' }, StringSplitOptions.RemoveEmptyEntries);
            var bad = lines.Where(l => SiteAllowlist.NormalizeSite(l) == null).ToList();
            if (bad.Count > 0)
            {
                MessageBox.Show("サイトとして読めないものがあります:\r\n" + string.Join("\r\n", bad.ToArray()) + "\r\n\r\nexample.com のように入力してください。",
                    "開けるサイト", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var st = _sync.GetState();
            st.allowedSites = SiteAllowlist.NormalizeList(lines);
            if (ApplyScheduleChangeWithLv2Check(st, "Lv.2 の間に開けるサイトを増やすには、下の言葉を手で入力してください:"))
            {
                _txtSites.Text = string.Join("\r\n", st.allowedSites.ToArray());
                MessageBox.Show(st.allowedSites.Count == 0
                        ? "開けるサイトを空にしました。ブロック中はブラウザごと止まります。"
                        : string.Format("{0} 件のサイトを保存しました。ブロック中も Chrome・Edge・Brave・Firefox で開けます。", st.allowedSites.Count),
                    "開けるサイト", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
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
                "スマホ設定用ハッシュをクリップボードにコピーしました！\r\nWeb リモコン URL の末尾や Android アプリの取り込み欄に貼り付けてください。",
                "コピー完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnPickRunningApps(object sender, EventArgs e)
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "focusguard", "explorer", "applicationframehost", "textinputhost",
                "systemsettings", "shellexperiencehost", "searchhost", "taskmgr"
            };

            var running = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    string name = p.ProcessName;
                    if (string.IsNullOrWhiteSpace(name) || excluded.Contains(name)) continue;
                    string title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? name : p.MainWindowTitle;
                    if (title.Length > 42) title = title.Substring(0, 42) + "...";
                    if (!running.ContainsKey(name))
                        running[name] = string.Format("{0}  ({1})", name, title);
                }
                catch { }
            }

            if (running.Count == 0)
            {
                MessageBox.Show("現在ウィンドウが開いている対象候補アプリが見つかりませんでした。\r\n追加したいゲームやアプリを一度起動してから押してください。",
                    "起動中アプリなし", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new Form())
            {
                dlg.Text = "起動中のアプリからブロック対象を追加";
                dlg.Size = new Size(460, 400);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.BackColor = Theme.Ground;
                dlg.ForeColor = Theme.Ink;
                dlg.Font = Theme.UI(9.5f);

                var lbl = new Label
                {
                    Text = "ブロック対象に追加したいアプリにチェックを入れてください:",
                    Location = new Point(16, 12),
                    Size = new Size(410, 24)
                };

                var clb = new CheckedListBox
                {
                    Location = new Point(16, 40),
                    Size = new Size(412, 265),
                    BackColor = Theme.White,
                    ForeColor = Theme.Ink,
                    CheckOnClick = true
                };

                var existingSet = new HashSet<string>(
                    _txtBrowsers.Text.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim().ToLowerInvariant()),
                    StringComparer.OrdinalIgnoreCase);

                var keys = running.Keys.OrderBy(k => k).ToList();
                foreach (var k in keys)
                {
                    int idx = clb.Items.Add(running[k]);
                    if (existingSet.Contains(k)) clb.SetItemChecked(idx, true);
                }

                var btnAdd = new Button
                {
                    Text = "選択したアプリを追加",
                    Location = new Point(190, 316),
                    Size = new Size(146, 34),
                    BackColor = Theme.Ink,
                    ForeColor = Theme.Paper,
                    FlatStyle = FlatStyle.Flat,
                    DialogResult = DialogResult.OK
                };
                btnAdd.FlatAppearance.BorderSize = 0;

                var btnCancel = new Button
                {
                    Text = "やめる",
                    Location = new Point(344, 316),
                    Size = new Size(84, 34),
                    BackColor = Theme.Paper,
                    ForeColor = Theme.Ink,
                    FlatStyle = FlatStyle.Flat,
                    DialogResult = DialogResult.Cancel
                };
                btnCancel.FlatAppearance.BorderSize = 1;
                btnCancel.FlatAppearance.BorderColor = Theme.Line;

                dlg.Controls.AddRange(new Control[] { lbl, clb, btnAdd, btnCancel });
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    for (int i = 0; i < keys.Count; i++)
                    {
                        if (clb.GetItemChecked(i))
                            existingSet.Add(keys[i].ToLowerInvariant());
                    }
                    _txtBrowsers.Text = string.Join(", ", existingSet);
                    MessageBox.Show("リストに追加しました！最後に下の「設定をすべて保存」を押すと反映・同期されます。",
                        "追加完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void OnAddGamePresets(object sender, EventArgs e)
        {
            var existing = new HashSet<string>(
                _txtBrowsers.Text.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);
            foreach (var preset in new[] { "steam", "discord", "epicgameslauncher", "riotclientux" })
            {
                existing.Add(preset);
            }
            _txtBrowsers.Text = string.Join(", ", existing);
            MessageBox.Show("Steam / Discord / Epic Games / Riot Client を追加しました。「設定を保存」で反映されます。",
                "プリセット追加", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            if (!_sync.UpdateState(st, out err, false))
            {
                if (!RuleEngine.WouldWeakenHardLock(_sync.GetState(), st))
                {
                    if (PromptLv2Challenge("Lv.2 稼働中にブロック対象アプリを外すには確認フレーズを入力してください:"))
                    {
                        if (_sync.UpdateState(st, out err, true))
                        {
                            MessageBox.Show("設定を保存しました。", "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }
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
            _lblSyncStatus.Text = msg;
            _lblSyncStatus.ForeColor = ok ? Theme.Sub : Theme.LvText(3);
        }

        private void BeginInvokeSafe(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch { }
        }
    }
}
