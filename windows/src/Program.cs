using System;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace FocusGuard
{
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static Mutex _mutex;

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            _mutex = new Mutex(true, "Local\\FocusGuard_SingleInstance_Mutex", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("FocusGuard はすでに起動しています（タスクトレイを確認してください）。",
                    "FocusGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try { SetProcessDPIAware(); } catch { }
            // Enable TLS 1.2 for Supabase HTTPS
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool startMinimized = args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

            using (var appContext = new FocusGuardTrayContext(startMinimized))
            {
                Application.Run(appContext);
            }

            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }

    public class FocusGuardTrayContext : ApplicationContext
    {
        private readonly SyncService _sync;
        private readonly BrowserMonitor _monitor;
        private readonly MainForm _mainForm;
        private readonly NotifyIcon _trayIcon;
        private int _lastIconLevel = -1;

        public FocusGuardTrayContext(bool startMinimized)
        {
            _sync = new SyncService();
            _monitor = new BrowserMonitor(_sync);
            _mainForm = new MainForm(_sync, ExitApplication);

            var menu = new ContextMenuStrip();
            menu.Items.Add("リモコンを開く", null, (s, e) => ShowMainWindow());
            menu.Items.Add("クイック開始: 30分集中 (Lv.2 / 両方)", null, (s, e) => QuickStart(2, 30));
            menu.Items.Add("クイック開始: 30分強制 (Lv.3 / 両方)", null, (s, e) => QuickStart(3, 30));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("終了", null, (s, e) => TryExitFromTray());

            _trayIcon = new NotifyIcon
            {
                Icon = CreateStatusIcon(0),
                Text = "FocusGuard — 待機中",
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => ShowMainWindow();

            _monitor.TickEvaluated += OnMonitorTick;

            _sync.StartBackgroundServices();
            _monitor.Start();

            if (!startMinimized && !_sync.GetSettings().StartMinimized)
            {
                ShowMainWindow();
            }
        }

        private void OnMonitorTick(EffectiveBlockState eff)
        {
            _mainForm.UpdateLiveStatus(eff);

            int visualLevel = !eff.IsActive ? 0 : (eff.IsSnoozed ? 10 : eff.Level);
            if (visualLevel != _lastIconLevel)
            {
                _lastIconLevel = visualLevel;
                var old = _trayIcon.Icon;
                _trayIcon.Icon = CreateStatusIcon(visualLevel);
                if (old != null) old.Dispose();
            }

            string tip = !eff.IsActive
                ? "FocusGuard — 待機中"
                : (eff.IsSnoozed
                    ? string.Format("FocusGuard — 一時許可中 (残り{0}秒)", eff.SnoozeRemainingSeconds)
                    : string.Format("FocusGuard — Lv.{0} ブロック中", eff.Level));
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            _trayIcon.Text = tip;
        }

        private void QuickStart(int level, int minutes)
        {
            var st = _sync.GetState();
            long now = TimeUtil.NowMs();
            st.manualSession = new ManualSession
            {
                active = true,
                level = level,
                target = "all",
                startedAt = now,
                expiresAt = now + (long)minutes * 60 * 1000
            };
            _sync.LocalSnoozeUntilMs = 0;
            string err;
            if (!_sync.UpdateState(st, out err))
            {
                MessageBox.Show(err, "FocusGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowMainWindow()
        {
            if (!_mainForm.Visible) _mainForm.Show();
            if (_mainForm.WindowState == FormWindowState.Minimized)
                _mainForm.WindowState = FormWindowState.Normal;
            _mainForm.Activate();
        }

        private void TryExitFromTray()
        {
            var eff = RuleEngine.Evaluate(_sync.GetState(), "pc", _sync.LocalSnoozeUntilMs);
            if (eff.IsHardLocked)
            {
                MessageBox.Show(
                    string.Format("Lv.3 ハードブロック中（残り: {0}）のため、トレイからの終了はロックされています。", eff.RemainingText),
                    "FocusGuard ロック中", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ExitApplication();
        }

        private void ExitApplication()
        {
            _monitor.Stop();
            _monitor.Dispose();
            _sync.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _mainForm.AllowExit();
            _mainForm.Close();
            ExitThread();
        }

        private Icon CreateStatusIcon(int level)
        {
            Color c = Color.FromArgb(46, 204, 138); // idle green
            if (level == 1) c = Color.FromArgb(245, 195, 66);
            else if (level == 2) c = Color.FromArgb(245, 122, 62);
            else if (level >= 3 && level < 10) c = Color.FromArgb(235, 72, 88);
            else if (level == 10) c = Color.FromArgb(92, 165, 245); // snoozed blue

            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var bg = new SolidBrush(Color.FromArgb(20, 24, 34)))
                    g.FillEllipse(bg, 1, 1, 30, 30);
                using (var br = new SolidBrush(c))
                    g.FillEllipse(br, 5, 5, 22, 22);
                using (var pen = new Pen(Color.White, 2))
                    g.DrawEllipse(pen, 5, 5, 22, 22);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
