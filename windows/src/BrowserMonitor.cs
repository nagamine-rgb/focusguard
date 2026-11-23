using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace FocusGuard
{
    public class BrowserMonitor : IDisposable
    {
        private readonly SyncService _sync;
        private readonly Timer _timer;
        private OverlayForm _activeOverlay;
        private HardBlockBannerForm _activeBanner;
        private DateTime _lastHardBannerAt = DateTime.MinValue;
        private bool _isPromptOpen;

        public event Action<EffectiveBlockState> TickEvaluated;

        public BrowserMonitor(SyncService sync)
        {
            _sync = sync;
            _timer = new Timer();
            _timer.Interval = 1200; // Check every 1.2s
            _timer.Tick += OnTick;
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            CloseOverlayIfAny();
        }

        private void OnTick(object sender, EventArgs e)
        {
            GuardState state = _sync.GetState();
            EffectiveBlockState eff = RuleEngine.Evaluate(state, "pc", _sync.LocalSnoozeUntilMs);

            var handler = TickEvaluated;
            if (handler != null) handler(eff);

            if (!eff.IsActive || eff.IsSnoozed)
            {
                CloseOverlayIfAny();
                return;
            }

            List<Process> runningBrowsers = FindRunningBrowsers(state.browsers.windows);
            if (runningBrowsers.Count == 0)
            {
                // User closed browsers on their own
                return;
            }

            string detectedNames = string.Join(", ",
                runningBrowsers.Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Take(3));

            if (eff.Level >= 3)
            {
                // Lv.3: Hard block - immediately terminate browser processes
                CloseOverlayIfAny();
                KillProcesses(runningBrowsers, true);
                ShowHardBlockBanner(eff, detectedNames);
            }
            else if (eff.Level == 2 || eff.Level == 1)
            {
                // Lv.1 / Lv.2: Show friction overlay if not already open
                if (_isPromptOpen && _activeOverlay != null && !_activeOverlay.IsDisposed)
                {
                    if (_activeOverlay.BlockLevel != eff.Level)
                    {
                        CloseOverlayIfAny();
                    }
                    else
                    {
                        _activeOverlay.BringToFront();
                        return;
                    }
                }

                ShowFrictionOverlay(state, eff, detectedNames);
            }
        }

        private void ShowFrictionOverlay(GuardState state, EffectiveBlockState eff, string browserNames)
        {
            _isPromptOpen = true;
            _activeOverlay = new OverlayForm(
                eff.Level,
                eff.Reason,
                eff.RemainingText,
                browserNames,
                state.lv1WaitSeconds,
                state.lv1AllowMinutes,
                state.lv2AllowMinutes);

            _activeOverlay.UserChoseCloseBrowser += () =>
            {
                var targets = FindRunningBrowsers(_sync.GetState().browsers.windows);
                KillProcesses(targets, false);
                CloseOverlayIfAny();
            };

            _activeOverlay.UserChoseAllowTemporarily += (minutes) =>
            {
                _sync.LocalSnoozeUntilMs = TimeUtil.NowMs() + (long)minutes * 60 * 1000;
                CloseOverlayIfAny();
            };

            _activeOverlay.FormClosed += (s, args) =>
            {
                _isPromptOpen = false;
                _activeOverlay = null;
            };

            _activeOverlay.Show();
            _activeOverlay.Activate();
        }

        private void ShowHardBlockBanner(EffectiveBlockState eff, string browserNames)
        {
            if ((DateTime.Now - _lastHardBannerAt).TotalSeconds < 2.5) return;
            _lastHardBannerAt = DateTime.Now;

            try
            {
                if (_activeBanner != null && !_activeBanner.IsDisposed)
                {
                    _activeBanner.Close();
                }
                _activeBanner = new HardBlockBannerForm(eff.Reason, eff.RemainingText, browserNames);
                _activeBanner.Show();
            }
            catch { }
        }

        private void CloseOverlayIfAny()
        {
            if (_activeOverlay != null && !_activeOverlay.IsDisposed)
            {
                try { _activeOverlay.AllowProgrammaticClose(); _activeOverlay.Close(); } catch { }
            }
            _activeOverlay = null;
            _isPromptOpen = false;
        }

        private List<Process> FindRunningBrowsers(List<string> targetNames)
        {
            var found = new List<Process>();
            if (targetNames == null || targetNames.Count == 0) return found;

            var nameSet = new HashSet<string>(
                targetNames.Where(n => !string.IsNullOrWhiteSpace(n))
                           .Select(n => n.Trim().ToLowerInvariant().Replace(".exe", "")),
                StringComparer.OrdinalIgnoreCase);

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (nameSet.Contains(proc.ProcessName.ToLowerInvariant()))
                    {
                        // Avoid blocking headless background helpers with no window when possible?
                        // Actually Chrome/Edge often keep background processes; check MainWindowHandle for Lv1/Lv2
                        // so overlay only fires when an actual browser window is open!
                        if (proc.MainWindowHandle != IntPtr.Zero)
                        {
                            found.Add(proc);
                        }
                    }
                }
                catch { }
            }
            return found;
        }

        private void KillProcesses(List<Process> visibleTargets, bool forceImmediate)
        {
            // Collect all process names to kill cleanly (including child/tab processes)
            var names = new HashSet<string>(
                visibleTargets.Select(p => p.ProcessName),
                StringComparer.OrdinalIgnoreCase);

            foreach (var p in visibleTargets)
            {
                try
                {
                    if (!forceImmediate)
                    {
                        p.CloseMainWindow();
                    }
                    else
                    {
                        p.Kill();
                    }
                }
                catch { }
            }

            if (forceImmediate)
            {
                foreach (var name in names)
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
            }
            else
            {
                // Gentle close first; if main window still alive after 1.5s, force close those main processes
                var delayed = new Timer { Interval = 1500 };
                delayed.Tick += (s, e) =>
                {
                    delayed.Stop();
                    delayed.Dispose();
                    foreach (var name in names)
                    {
                        foreach (var p in Process.GetProcessesByName(name))
                        {
                            try
                            {
                                if (p.MainWindowHandle != IntPtr.Zero)
                                    p.Kill();
                            }
                            catch { }
                        }
                    }
                };
                delayed.Start();
            }
        }

        public void Dispose()
        {
            Stop();
            _timer.Dispose();
        }
    }
}
