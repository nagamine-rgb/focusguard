using System;
using System.Diagnostics;
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
            // Watchdog mode: silently protects FocusGuard during Lv.3 Hard Lock
            if (args.Length >= 1 && string.Equals(args[0], "--watchdog", StringComparison.OrdinalIgnoreCase))
            {
                int parentPid = 0;
                if (args.Length >= 2) int.TryParse(args[1], out parentPid);
                RunWatchdogLoop(parentPid);
                return;
            }

            bool createdNew;
            _mutex = new Mutex(true, "Local\\FocusGuard_SingleInstance_Mutex", out createdNew);
            if (!createdNew)
            {
                if (!args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("FocusGuard はすでに起動しています（タスクトレイを確認してください）。",
                        "FocusGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
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

            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
        }

        private static void RunWatchdogLoop(int watchedPid)
        {
            bool created;
            using (var wdMutex = new Mutex(true, "Local\\FocusGuard_Watchdog_Mutex", out created))
            {
                if (!created) return;

                string exePath = Application.ExecutablePath;
                int currentMainPid = watchedPid;

                while (true)
                {
                    Thread.Sleep(900);

                    // Check if Lv.3 Hard Lock is still active on PC
                    GuardState st = SyncService.LoadStateStatic();
                    EffectiveBlockState eff = RuleEngine.Evaluate(st, "pc", 0);
                    if (!eff.IsHardLocked)
                    {
                        // Lv.3 finished; watchdog exits cleanly
                        break;
                    }

                    bool mainAlive = IsProcessAlive(currentMainPid);
                    if (!mainAlive)
                    {
                        // Check if another main instance already started
                        var existing = FindOtherMainProcessId();
                        if (existing > 0)
                        {
                            currentMainPid = existing;
                        }
                        else
                        {
                            try
                            {
                                var psi = new ProcessStartInfo
                                {
                                    FileName = exePath,
                                    Arguments = "--minimized",
                                    UseShellExecute = false
                                };
                                var restarted = Process.Start(psi);
                                if (restarted != null) currentMainPid = restarted.Id;
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        private static bool IsProcessAlive(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                var p = Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static int FindOtherMainProcessId()
        {
            int myPid = Process.GetCurrentProcess().Id;
            string myName = Process.GetCurrentProcess().ProcessName;
            foreach (var p in Process.GetProcessesByName(myName))
            {
                if (p.Id != myPid)
                {
                    try { if (!p.HasExited) return p.Id; } catch { }
                }
            }
            return 0;
        }
    }

    public class FocusGuardTrayContext : ApplicationContext
    {
        private readonly SyncService _sync;
        private readonly BrowserMonitor _monitor;
        private readonly MainForm _mainForm;
        private readonly NotifyIcon _trayIcon;
        private int _lastIconLevel = -1;
        private Process _watchdogProc;

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

        private void OnMonitorTick(EffectiveBlockState effPc)
        {
            GuardState st = _sync.GetState();
            EffectiveBlockState effAndroid = RuleEngine.Evaluate(st, "android", 0);
            _mainForm.UpdateLiveStatus(effPc, effAndroid, st.devices != null ? st.devices.android : null);

            // Manage Lv.3 Watchdog auto-revive process
            EnsureWatchdogForHardLock(effPc.IsHardLocked);

            int visualLevel = !effPc.IsActive ? 0 : (effPc.IsSnoozed ? 10 : effPc.Level);
            if (visualLevel != _lastIconLevel)
            {
                _lastIconLevel = visualLevel;
                var old = _trayIcon.Icon;
                _trayIcon.Icon = CreateStatusIcon(visualLevel);
                if (old != null) old.Dispose();
            }

            string tip = !effPc.IsActive
                ? "FocusGuard — 待機中"
                : (effPc.IsSnoozed
                    ? string.Format("FocusGuard — 一時許可中 (残り{0}秒)", effPc.SnoozeRemainingSeconds)
                    : string.Format("FocusGuard — Lv.{0} ブロック中", effPc.Level));
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            _trayIcon.Text = tip;
        }

        private void EnsureWatchdogForHardLock(bool shouldProtect)
        {
            try
            {
                if (shouldProtect)
                {
                    bool alive = _watchdogProc != null && !_watchdogProc.HasExited;
                    if (!alive)
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = Application.ExecutablePath,
                            Arguments = "--watchdog " + Process.GetCurrentProcess().Id,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        _watchdogProc = Process.Start(psi);
                    }
                }
                else
                {
                    if (_watchdogProc != null && !_watchdogProc.HasExited)
                    {
                        try { _watchdogProc.Kill(); } catch { }
                    }
                    _watchdogProc = null;
                }
            }
            catch { }
        }

        private void QuickStart(int level, int minutes)
        {
            var st = _sync.GetState();
            long now = TimeUtil.NowMs();
            st.StartManual(level, "all", now, now + (long)minutes * 60 * 1000);
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
                    string.Format("Lv.3 ハードブロック中（残り: {0}）のため、終了はロックされています。", eff.RemainingText),
                    "FocusGuard ロック中", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (eff.IsActive && eff.Level == 2)
            {
                if (!FocusGuard.MainForm.PromptLv2Challenge("Lv.2 稼働中に FocusGuard を終了するには確認フレーズを入力してください:"))
                {
                    return;
                }
            }
            ExitApplication();
        }

        private void ExitApplication()
        {
            EnsureWatchdogForHardLock(false);
            _monitor.Stop();
            _monitor.Dispose();
            _sync.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _mainForm.AllowExit();
            _mainForm.Close();
            ExitThread();
        }

        /// <summary>アプリのアイコンと同じ文字盤。扇形の色で状態を表す</summary>
        private Icon CreateStatusIcon(int level)
        {
            Color sector = Color.FromArgb(160, 172, 156);                         // ブロックなし
            if (level == 1) sector = Color.FromArgb(217, 162, 27);                // Lv.1
            else if (level == 2) sector = Color.FromArgb(224, 122, 69);           // Lv.2
            else if (level >= 3 && level < 10) sector = Color.FromArgb(200, 57, 43); // Lv.3
            else if (level == 10) sector = Color.FromArgb(255, 255, 255);         // 一時的に開けている

            var ink = Color.FromArgb(28, 38, 32);
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var path = Theme.Rounded(new RectangleF(0, 0, 32, 32), 7f))
                using (var bg = new SolidBrush(Color.FromArgb(228, 232, 225)))
                    g.FillPath(bg, path);

                // 4本の目盛り
                using (var tick = new Pen(ink, 2.4f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                {
                    g.DrawLine(tick, 16, 2.5f, 16, 4.8f);
                    g.DrawLine(tick, 29.5f, 16, 27.2f, 16);
                    g.DrawLine(tick, 16, 29.5f, 16, 27.2f);
                    g.DrawLine(tick, 2.5f, 16, 4.8f, 16);
                }
                var ring = new RectangleF(16 - 9.5f, 16 - 9.5f, 19f, 19f);
                using (var br = new SolidBrush(sector)) g.FillPie(br, ring.X, ring.Y, ring.Width, ring.Height, -90, 120);
                using (var pen = new Pen(ink, 2f)) g.DrawEllipse(pen, ring);
                using (var hand = new Pen(ink, 2.4f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                    g.DrawLine(hand, 16, 16, 16 + 7.5f * 0.866f, 16 + 7.5f * 0.5f);
                using (var dot = new SolidBrush(ink)) g.FillEllipse(dot, 14, 14, 4, 4);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
