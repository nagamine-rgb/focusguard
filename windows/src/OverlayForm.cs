using System;
using System.Drawing;
using System.Windows.Forms;

namespace FocusGuard
{
    public class OverlayForm : Form
    {
        public int BlockLevel { get; private set; }
        public event Action UserChoseCloseBrowser;
        public event Action<int> UserChoseAllowTemporarily;

        private bool _allowClose;
        private int _remainingWaitSec;
        private readonly int _allowMinutes;
        private Quote _targetQuote;

        private Timer _countdownTimer;
        private Label _lblCountdown;
        private ProgressBar _progressBar;
        private Button _btnAllow;
        private TextBox _txtPhrase;
        private Label _lblPhraseHint;

        public OverlayForm(
            int level,
            string reason,
            string remainingText,
            string browserNames,
            int lv1WaitSec,
            int lv1AllowMin,
            int lv2AllowMin)
        {
            BlockLevel = level;
            _remainingWaitSec = Math.Max(3, lv1WaitSec);
            _allowMinutes = level == 1 ? Math.Max(1, lv1AllowMin) : Math.Max(1, lv2AllowMin);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(14, 17, 23);
            Opacity = 0.96;
            KeyPreview = true;

            BuildUi(level, reason, remainingText, browserNames);
        }

        public void AllowProgrammaticClose()
        {
            _allowClose = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            if (_countdownTimer != null)
            {
                _countdownTimer.Stop();
                _countdownTimer.Dispose();
            }
            base.OnFormClosing(e);
        }

        private void BuildUi(int level, string reason, string remainingText, string browserNames)
        {
            Rectangle primary = Screen.PrimaryScreen.Bounds;
            int cardW = 540;
            int cardH = level == 1 ? 400 : 560;
            int cardX = (primary.Left - Left) + (primary.Width - cardW) / 2;
            int cardY = (primary.Top - Top) + (primary.Height - cardH) / 2;

            var card = new Panel
            {
                Size = new Size(cardW, cardH),
                Location = new Point(cardX, cardY),
                BackColor = Color.FromArgb(24, 29, 39)
            };
            card.Paint += (s, e) =>
            {
                Color accent = level == 1 ? Color.FromArgb(245, 185, 66) : Color.FromArgb(245, 118, 66);
                using (var pen = new Pen(accent, 2))
                {
                    e.Graphics.DrawRectangle(pen, 1, 1, card.Width - 3, card.Height - 3);
                }
            };
            Controls.Add(card);

            Color badgeBg = level == 1 ? Color.FromArgb(72, 56, 20) : Color.FromArgb(80, 36, 20);
            Color badgeFg = level == 1 ? Color.FromArgb(255, 210, 102) : Color.FromArgb(255, 150, 102);

            var lblBadge = new Label
            {
                Text = level == 1 ? "  Lv.1  気づきモード（ソフト）  " : "  Lv.2  ひと手間モード（ミドル）  ",
                Font = new Font("Meiryo UI", 10f, FontStyle.Bold),
                ForeColor = badgeFg,
                BackColor = badgeBg,
                AutoSize = true,
                Location = new Point(32, 28),
                Padding = new Padding(6, 4, 6, 4)
            };
            card.Controls.Add(lblBadge);

            var lblTitle = new Label
            {
                Text = level == 1
                    ? "ちょっと待って。本当に今このアプリが必要？"
                    : "集中モード中です。対象アプリの起動をブロックしました。",
                Font = new Font("Meiryo UI", 14.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(32, 68),
                Size = new Size(cardW - 64, 56)
            };
            card.Controls.Add(lblTitle);

            var lblMeta = new Label
            {
                Text = string.Format("理由: {0}  /  残り: {1}\r\n検知アプリ: {2}", reason, remainingText, browserNames),
                Font = new Font("Meiryo UI", 9.5f),
                ForeColor = Color.FromArgb(156, 168, 190),
                Location = new Point(32, 128),
                Size = new Size(cardW - 64, 42)
            };
            card.Controls.Add(lblMeta);

            if (level == 1)
            {
                BuildLv1Controls(card, cardW);
            }
            else
            {
                BuildLv2Controls(card, cardW);
            }

            // Primary action: close target app (recommended)
            var btnClose = new Button
            {
                Text = "やっぱりやめる（アプリを閉じる）",
                Font = new Font("Meiryo UI", 11f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(37, 150, 96),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(cardW - 64, 46),
                Location = new Point(32, cardH - 122),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) =>
            {
                _allowClose = true;
                var h = UserChoseCloseBrowser;
                if (h != null) h();
            };
            card.Controls.Add(btnClose);
        }

        private void BuildLv1Controls(Panel card, int cardW)
        {
            _lblCountdown = new Label
            {
                Text = string.Format("深呼吸して {0} 秒待ってください...", _remainingWaitSec),
                Font = new Font("Meiryo UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 210, 102),
                Location = new Point(32, 185),
                Size = new Size(cardW - 64, 30)
            };
            card.Controls.Add(_lblCountdown);

            int totalSec = _remainingWaitSec;
            _progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = totalSec,
                Value = 0,
                Location = new Point(32, 222),
                Size = new Size(cardW - 64, 16),
                Style = ProgressBarStyle.Continuous
            };
            card.Controls.Add(_progressBar);

            _btnAllow = new Button
            {
                Text = string.Format("あと {0} 秒で一時許可可能（{1}分間）", _remainingWaitSec, _allowMinutes),
                Font = new Font("Meiryo UI", 9.5f),
                ForeColor = Color.FromArgb(180, 190, 208),
                BackColor = Color.FromArgb(40, 48, 62),
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Size = new Size(cardW - 64, 38),
                Location = new Point(32, card.Height - 64),
                Cursor = Cursors.Hand
            };
            _btnAllow.FlatAppearance.BorderSize = 1;
            _btnAllow.FlatAppearance.BorderColor = Color.FromArgb(70, 82, 102);
            _btnAllow.Click += (s, e) =>
            {
                _allowClose = true;
                var h = UserChoseAllowTemporarily;
                if (h != null) h(_allowMinutes);
            };
            card.Controls.Add(_btnAllow);

            _countdownTimer = new Timer { Interval = 1000 };
            _countdownTimer.Tick += (s, e) =>
            {
                _remainingWaitSec--;
                int elapsed = totalSec - Math.Max(0, _remainingWaitSec);
                if (elapsed <= _progressBar.Maximum) _progressBar.Value = elapsed;

                if (_remainingWaitSec <= 0)
                {
                    _countdownTimer.Stop();
                    _lblCountdown.Text = "準備OK。本当に必要な用事だけ済ませましょう。";
                    _lblCountdown.ForeColor = Color.FromArgb(130, 220, 170);
                    _btnAllow.Enabled = true;
                    _btnAllow.Text = string.Format("{0}分だけアプリを許可する", _allowMinutes);
                    _btnAllow.ForeColor = Color.White;
                }
                else
                {
                    _lblCountdown.Text = string.Format("深呼吸して {0} 秒待ってください...", _remainingWaitSec);
                    _btnAllow.Text = string.Format("あと {0} 秒で一時許可可能（{1}分間）", _remainingWaitSec, _allowMinutes);
                }
            };
            _countdownTimer.Start();
        }

        private void BuildLv2Controls(Panel card, int cardW)
        {
            _targetQuote = Quotes.Pick();

            var lblGuide = new Label
            {
                Text = "どうしても今使うなら、下の言葉を手で入力してください（貼り付けはできません）:",
                Font = new Font("Meiryo UI", 9.5f),
                ForeColor = Color.FromArgb(220, 226, 238),
                Location = new Point(32, 180),
                Size = new Size(cardW - 64, 24)
            };
            card.Controls.Add(lblGuide);

            var lblPhraseBox = new Label
            {
                Text = _targetQuote.Text,
                Font = new Font("Meiryo UI", 13f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 190, 140),
                BackColor = Color.FromArgb(16, 20, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 6, 12, 6),
                Location = new Point(32, 208),
                Size = new Size(cardW - 64, 96)
            };
            card.Controls.Add(lblPhraseBox);

            var lblAuthor = new Label
            {
                Text = "— " + _targetQuote.Author,
                Font = new Font("Meiryo UI", 9f),
                ForeColor = Color.FromArgb(156, 168, 190),
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(32, 306),
                Size = new Size(cardW - 64, 22)
            };
            card.Controls.Add(lblAuthor);

            _txtPhrase = new TextBox
            {
                Font = new Font("Meiryo UI", 12f),
                BackColor = Color.FromArgb(32, 38, 52),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(32, 334),
                Size = new Size(cardW - 64, 32),
                ShortcutsEnabled = false,
                ImeMode = ImeMode.On
            };
            _txtPhrase.KeyDown += (s, e) =>
            {
                if ((e.Control && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert))
                {
                    e.SuppressKeyPress = true;
                }
            };
            _txtPhrase.TextChanged += (s, e) =>
            {
                bool match = Quotes.Matches(_txtPhrase.Text, _targetQuote);
                _btnAllow.Enabled = match;
                _btnAllow.ForeColor = match ? Color.White : Color.FromArgb(150, 160, 180);
                _lblPhraseHint.Text = Quotes.Progress(_txtPhrase.Text, _targetQuote);
                _lblPhraseHint.ForeColor = match ? Color.FromArgb(110, 220, 150) : Color.FromArgb(140, 150, 170);
            };
            card.Controls.Add(_txtPhrase);

            _lblPhraseHint = new Label
            {
                Text = Quotes.Progress("", _targetQuote),
                Font = new Font("Meiryo UI", 8.5f),
                ForeColor = Color.FromArgb(140, 150, 170),
                Location = new Point(32, 370),
                Size = new Size(cardW - 64, 20)
            };
            card.Controls.Add(_lblPhraseHint);

            _btnAllow = new Button
            {
                Text = string.Format("入力が一致したら {0}分だけ許可する", _allowMinutes),
                Font = new Font("Meiryo UI", 9.5f),
                ForeColor = Color.FromArgb(150, 160, 180),
                BackColor = Color.FromArgb(40, 48, 62),
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Size = new Size(cardW - 64, 38),
                Location = new Point(32, card.Height - 64),
                Cursor = Cursors.Hand
            };
            _btnAllow.FlatAppearance.BorderSize = 1;
            _btnAllow.FlatAppearance.BorderColor = Color.FromArgb(70, 82, 102);
            _btnAllow.Click += (s, e) =>
            {
                _allowClose = true;
                var h = UserChoseAllowTemporarily;
                if (h != null) h(_allowMinutes);
            };
            card.Controls.Add(_btnAllow);
        }
    }

    public class HardBlockBannerForm : Form
    {
        private readonly Timer _closeTimer;

        protected override bool ShowWithoutActivation { get { return true; } }

        public HardBlockBannerForm(string reason, string remainingText, string browserNames)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Size = new Size(370, 96);
            BackColor = Color.FromArgb(38, 18, 22);
            Opacity = 0.95;

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 20, wa.Bottom - Height - 20);

            Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(235, 72, 88), 2))
                    e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
            };

            var lblHead = new Label
            {
                Text = "Lv.3 ハードブロック中 — 対象アプリを終了しました",
                Font = new Font("Meiryo UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 130, 140),
                Location = new Point(16, 14),
                Size = new Size(338, 22)
            };
            Controls.Add(lblHead);

            var lblBody = new Label
            {
                Text = string.Format("理由: {0} / 残り: {1}\r\n対象: {2}", reason, remainingText, browserNames),
                Font = new Font("Meiryo UI", 8.8f),
                ForeColor = Color.FromArgb(230, 225, 228),
                Location = new Point(16, 40),
                Size = new Size(338, 44)
            };
            Controls.Add(lblBody);

            _closeTimer = new Timer { Interval = 3200 };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                _closeTimer.Dispose();
                Close();
            };
            _closeTimer.Start();
        }
    }
}
