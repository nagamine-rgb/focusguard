using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace FocusGuard
{
    /// <summary>
    /// ホワイトリスト（ブロック中でも開けるサイト）。
    /// Chrome / Edge / Brave には、ブラウザ公式の管理用設定（URLBlocklist と URLAllowlist）を
    /// ユーザー単位のレジストリに書き込んで、リスト以外のサイトをブラウザ自身に止めさせる。
    /// </summary>
    public static class SiteAllowlist
    {
        /// <summary>ポリシーでサイト単位に止められるブラウザのプロセス名</summary>
        public static readonly string[] PolicyBrowsers = { "chrome", "msedge", "brave" };

        private static readonly string[] PolicyKeys =
        {
            @"Software\Policies\Google\Chrome",
            @"Software\Policies\Microsoft\Edge",
            @"Software\Policies\BraveSoftware\Brave"
        };

        private const string MarkerKey = @"Software\FocusGuard";
        private const string MarkerValue = "BrowserPoliciesApplied";
        private static string _appliedSignature;

        /// <summary>入力を「example.com」の形にそろえる。使えない入力は null</summary>
        public static string NormalizeSite(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim().ToLowerInvariant();
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) s = s.Substring(scheme + 3);
            int cut = s.IndexOfAny(new[] { '/', '?', '#' });
            if (cut >= 0) s = s.Substring(0, cut);
            int at = s.LastIndexOf('@');
            if (at >= 0) s = s.Substring(at + 1);
            int colon = s.IndexOf(':');
            if (colon >= 0) s = s.Substring(0, colon);
            s = s.TrimStart('*', '.');
            if (s.StartsWith("www.")) s = s.Substring(4);
            s = s.TrimEnd('.');
            if (s.Length == 0 || s.Length > 253) return null;
            if (s != "localhost" && s.IndexOf('.') < 0) return null;
            foreach (char c in s)
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '.')) return null;
            return s;
        }

        public static List<string> NormalizeList(IEnumerable<string> raw)
        {
            var result = new List<string>();
            if (raw == null) return result;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in raw)
            {
                string n = NormalizeSite(r);
                if (n != null && seen.Add(n)) result.Add(n);
            }
            return result;
        }

        /// <summary>サイトが増えたか（増やすことは「ブロックを弱める」変更）</summary>
        public static bool AddedAnySite(GuardState current, GuardState proposed)
        {
            var cur = new HashSet<string>(NormalizeList(current != null ? current.allowedSites : null), StringComparer.OrdinalIgnoreCase);
            foreach (var s in NormalizeList(proposed != null ? proposed.allowedSites : null))
                if (!cur.Contains(s)) return true;
            return false;
        }

        public static bool IsPolicyBrowser(string processName)
        {
            return PolicyBrowsers.Contains((processName ?? "").ToLowerInvariant());
        }

        /// <summary>ブロック中: リスト以外のサイトを止める設定を書き込む（消されていたら書き直す）</summary>
        public static void Apply(List<string> sites)
        {
            string sig = string.Join(",", sites.ToArray());
            if (sig == _appliedSignature && PoliciesPresent()) return;
            foreach (var key in PolicyKeys)
            {
                try
                {
                    using (var root = Registry.CurrentUser.CreateSubKey(key))
                    {
                        if (root == null) continue;
                        root.DeleteSubKeyTree("URLBlocklist", false);
                        root.DeleteSubKeyTree("URLAllowlist", false);
                        using (var block = root.CreateSubKey("URLBlocklist"))
                            block.SetValue("1", "*", RegistryValueKind.String);
                        using (var allow = root.CreateSubKey("URLAllowlist"))
                        {
                            for (int i = 0; i < sites.Count; i++)
                                allow.SetValue((i + 1).ToString(), sites[i], RegistryValueKind.String);
                        }
                    }
                }
                catch { }
            }
            try
            {
                using (var m = Registry.CurrentUser.CreateSubKey(MarkerKey))
                    m.SetValue(MarkerValue, 1, RegistryValueKind.DWord);
            }
            catch { }
            _appliedSignature = sig;
        }

        /// <summary>ブロックしていないとき: 書き込んだ設定を消す</summary>
        public static void Clear()
        {
            bool marked = false;
            try
            {
                using (var m = Registry.CurrentUser.OpenSubKey(MarkerKey))
                    marked = m != null && m.GetValue(MarkerValue) != null;
            }
            catch { }
            if (!marked && _appliedSignature == null) return;

            foreach (var key in PolicyKeys)
            {
                try
                {
                    using (var root = Registry.CurrentUser.OpenSubKey(key, true))
                    {
                        if (root == null) continue;
                        root.DeleteSubKeyTree("URLBlocklist", false);
                        root.DeleteSubKeyTree("URLAllowlist", false);
                    }
                }
                catch { }
            }
            try
            {
                using (var m = Registry.CurrentUser.OpenSubKey(MarkerKey, true))
                    if (m != null) m.DeleteValue(MarkerValue, false);
            }
            catch { }
            _appliedSignature = null;
        }

        private static bool PoliciesPresent()
        {
            foreach (var key in PolicyKeys)
            {
                try
                {
                    using (var block = Registry.CurrentUser.OpenSubKey(key + @"\URLBlocklist"))
                        if (block == null || (block.GetValue("1") as string) != "*") return false;
                }
                catch { return false; }
            }
            return true;
        }
    }
}
