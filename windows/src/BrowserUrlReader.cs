using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace FocusGuard
{
    /// <summary>
    /// Windows の UI Automation（画面読み上げ用の仕組み）で、ブラウザのアドレスバーの URL を読む。
    /// Chrome / Edge / Brave / Firefox のアドレスバーは、ウィンドウの中で最初に見つかる入力欄。
    /// </summary>
    public static class BrowserUrlReader
    {
        /// <summary>アドレスバーを読めるブラウザ（プロセス名）</summary>
        public static readonly string[] SupportedBrowsers = { "chrome", "msedge", "brave", "firefox" };

        public enum ReadResult { Url, NewTab, Editing, Unreadable }

        private class Cache
        {
            public AutomationElement Edit;
            public DateTime EditingSince = DateTime.MinValue;
            public int UnreadableStreak;
        }

        private static readonly Dictionary<IntPtr, Cache> CacheByWindow = new Dictionary<IntPtr, Cache>();
        /// <summary>アドレスバーの入力中として止めずにおく最長時間</summary>
        private static readonly TimeSpan MaxEditing = TimeSpan.FromSeconds(20);

        public static bool IsSupported(string processName)
        {
            return Array.IndexOf(SupportedBrowsers, (processName ?? "").ToLowerInvariant()) >= 0;
        }

        /// <summary>
        /// ウィンドウのアドレスバーを読む。読めたら host に「example.com」の形で入る。
        /// 時間がかかりすぎたり見つからなかったりしたら Unreadable（呼び出し側はブラウザごと止める）。
        /// </summary>
        public static ReadResult Read(IntPtr hwnd, out string host, out int unreadableStreak)
        {
            var r = ReadCore(hwnd, out host);
            Cache c;
            if (CacheByWindow.TryGetValue(hwnd, out c))
            {
                c.UnreadableStreak = r == ReadResult.Unreadable ? c.UnreadableStreak + 1 : 0;
                unreadableStreak = c.UnreadableStreak;
            }
            else unreadableStreak = 99;
            return r;
        }

        private static ReadResult ReadCore(IntPtr hwnd, out string host)
        {
            host = null;
            if (hwnd == IntPtr.Zero) return ReadResult.Unreadable;

            Cache cache;
            if (!CacheByWindow.TryGetValue(hwnd, out cache))
            {
                cache = new Cache();
                CacheByWindow[hwnd] = cache;
            }

            string value = null;
            bool focused = false;
            bool ok = false;
            var task = Task.Factory.StartNew(() =>
            {
                try
                {
                    AutomationElement edit = cache.Edit;
                    if (edit != null)
                    {
                        try { focused = edit.Current.HasKeyboardFocus; }
                        catch { edit = null; }
                    }
                    if (edit == null)
                    {
                        var root = AutomationElement.FromHandle(hwnd);
                        var cond = new AndCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                            new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true));
                        edit = root.FindFirst(TreeScope.Descendants, cond);
                        if (edit == null) return;
                        cache.Edit = edit;
                        focused = edit.Current.HasKeyboardFocus;
                    }
                    var vp = (ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern);
                    value = vp.Current.Value;
                    ok = true;
                }
                catch
                {
                    cache.Edit = null;
                }
            });
            if (!task.Wait(2500) || !ok) return ReadResult.Unreadable;

            if (focused)
            {
                if (cache.EditingSince == DateTime.MinValue) cache.EditingSince = DateTime.Now;
                if (DateTime.Now - cache.EditingSince < MaxEditing) return ReadResult.Editing;
            }
            else
            {
                cache.EditingSince = DateTime.MinValue;
            }

            string v = (value ?? "").Trim();
            if (v.Length == 0) return ReadResult.NewTab;
            if (v.IndexOf(' ') >= 0) return focused ? ReadResult.Editing : ReadResult.Unreadable;
            string lower = v.ToLowerInvariant();
            if (lower.StartsWith("chrome://newtab") || lower.StartsWith("edge://newtab") || lower.StartsWith("about:newtab")
                || lower.StartsWith("about:home") || lower.StartsWith("brave://newtab") || lower == "about:blank")
                return ReadResult.NewTab;
            host = SiteAllowlist.NormalizeSite(v);
            return host == null ? ReadResult.Unreadable : ReadResult.Url;
        }

        /// <summary>閉じたウィンドウの記録を捨てる</summary>
        public static void Forget(IEnumerable<IntPtr> liveWindows)
        {
            var live = new HashSet<IntPtr>(liveWindows);
            var dead = new List<IntPtr>();
            foreach (var k in CacheByWindow.Keys) if (!live.Contains(k)) dead.Add(k);
            foreach (var k in dead) CacheByWindow.Remove(k);
        }

        // ---- ウィンドウ操作 ----

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>対応ブラウザの、見えているウィンドウをすべて集める（プロセス名つき）</summary>
        public static List<KeyValuePair<IntPtr, string>> VisibleBrowserWindows(ICollection<string> targetNames)
        {
            var pidToName = new Dictionary<uint, string>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string n = p.ProcessName.ToLowerInvariant();
                    if (IsSupported(n) && targetNames.Contains(n)) pidToName[(uint)p.Id] = n;
                }
                catch { }
            }
            var result = new List<KeyValuePair<IntPtr, string>>();
            if (pidToName.Count == 0) return result;
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h) || IsIconic(h) || GetWindowTextLength(h) == 0) return true;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                string name;
                if (pidToName.TryGetValue(pid, out name)) result.Add(new KeyValuePair<IntPtr, string>(h, name));
                return true;
            }, IntPtr.Zero);
            return result;
        }

        /// <summary>開いているタブを閉じる（Ctrl+W）。前面にできないウィンドウは最小化する</summary>
        public static void CloseCurrentTab(IntPtr hwnd)
        {
            try
            {
                if (GetForegroundWindow() != hwnd) SetForegroundWindow(hwnd);
                if (GetForegroundWindow() == hwnd)
                    System.Windows.Forms.SendKeys.SendWait("^w");
                else
                {
                    ShowWindow(hwnd, 6); // SW_MINIMIZE
                }
            }
            catch { }
        }
    }
}
