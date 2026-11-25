using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FocusGuard
{
    public class SyncService : IDisposable
    {
        private readonly string _dataDir;
        private readonly string _settingsPath;
        private readonly string _statePath;
        private readonly string _webHtmlPath;
        private readonly HttpClient _http;
        private readonly object _lock = new object();

        private TcpListener _tcpListener;
        private CancellationTokenSource _cts;
        private GuardState _state;
        private AppSettings _settings;

        public string LastSyncMessage { get; private set; }
        public DateTime? LastSyncTime { get; private set; }
        public bool LastSyncOk { get; private set; }
        public long LocalSnoozeUntilMs { get; set; }

        public event Action<GuardState> StateChanged;
        public event Action<string, bool> SyncStatusChanged;

        public SyncService()
        {
            string asmLoc = typeof(SyncService).Assembly.Location;
            string exeDir = !string.IsNullOrEmpty(asmLoc)
                ? Path.GetDirectoryName(asmLoc)
                : AppDomain.CurrentDomain.BaseDirectory;
            _dataDir = ResolveDataDir(exeDir);
            _settingsPath = Path.Combine(_dataDir, "settings.json");
            _statePath = Path.Combine(_dataDir, "state.json");

            // Look for web/index.html next to exe or in ../web/index.html
            string p1 = Path.Combine(exeDir, "web", "index.html");
            string p2 = Path.GetFullPath(Path.Combine(exeDir, "..", "web", "index.html"));
            _webHtmlPath = File.Exists(p1) ? p1 : p2;

            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(10);

            _settings = LoadSettings();
            _state = LoadState();
            LastSyncMessage = _settings.SyncMode == "supabase" ? "Supabase 同期待機中" : "ローカルモードで動作中";
            LastSyncOk = true;
        }

        /// <summary>exe に埋め込んだ Web リモコン（web/index.html が隣にないとき用）</summary>
        private static string ReadEmbeddedWebHtml()
        {
            try
            {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("FocusGuard.web.index.html"))
                {
                    if (stream != null)
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                            return reader.ReadToEnd();
                }
            }
            catch { }
            return "<html><body><h2>FocusGuard の Web リモコンが見つかりません</h2></body></html>";
        }

        private static string ResolveDataDir(string exeDir)
        {
            string preferred = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FocusGuard");
            try
            {
                Directory.CreateDirectory(preferred);
                string probe = Path.Combine(preferred, ".write_test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return preferred;
            }
            catch
            {
                string fallback = Path.Combine(exeDir, "data");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
        }

        public AppSettings GetSettings()
        {
            lock (_lock)
            {
                return JsonUtil.Deserialize<AppSettings>(JsonUtil.Serialize(_settings));
            }
        }

        public GuardState GetState()
        {
            lock (_lock)
            {
                return JsonUtil.Deserialize<GuardState>(JsonUtil.Serialize(_state));
            }
        }

        public string LocalWebUrl
        {
            get { return string.Format("http://127.0.0.1:{0}/", _settings.LocalPort); }
        }

        public void SaveSettings(AppSettings updated)
        {
            lock (_lock)
            {
                _settings = updated;
                File.WriteAllText(_settingsPath, JsonUtil.Serialize(_settings), Encoding.UTF8);
            }
            if (_settings.SyncMode == "supabase")
            {
                Task.Run(() => PullFromSupabaseAsync(true));
            }
            else
            {
                SetSyncStatus("ローカルモードで動作中", true);
            }
        }

        public static GuardState LoadStateStatic()
        {
            try
            {
                string asmLoc = typeof(SyncService).Assembly.Location;
                string exeDir = !string.IsNullOrEmpty(asmLoc)
                    ? Path.GetDirectoryName(asmLoc)
                    : AppDomain.CurrentDomain.BaseDirectory;
                string dataDir = ResolveDataDir(exeDir);
                string path = Path.Combine(dataDir, "state.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var st = JsonUtil.Deserialize<GuardState>(json);
                    if (st != null) { st.EnsureDefaults(); return st; }
                }
            }
            catch { }
            return new GuardState();
        }

        public bool UpdateState(GuardState nextState, out string errorMessage, bool lv2ChallengePassed = false)
        {
            errorMessage = null;
            if (nextState == null)
            {
                errorMessage = "無効な状態です";
                return false;
            }
            nextState.EnsureDefaults();

            lock (_lock)
            {
                string blockErr = RuleEngine.ValidateTransition(_state, nextState, lv2ChallengePassed);
                if (blockErr != null)
                {
                    errorMessage = blockErr;
                    return false;
                }

                // 累計のための記録（手動ブロックとスケジュールの変化）
                BlockStats.RecordHistory(_state, nextState, TimeUtil.NowMs());

                // 期限切れの手動ブロックを止める
                nextState.ExpireManual(TimeUtil.NowMs());

                nextState.updatedAt = TimeUtil.NowMs();
                _state = nextState;
                SaveStateToDisk(_state);
            }

            RaiseStateChanged();

            if (_settings.SyncMode == "supabase")
            {
                Task.Run(() => PushToSupabaseAsync(GetState()));
            }
            return true;
        }

        public void StartBackgroundServices()
        {
            _cts = new CancellationTokenSource();
            StartLocalHttpServer(_cts.Token);
            Task.Run(() => SyncLoopAsync(_cts.Token));
        }

        private async Task SyncLoopAsync(CancellationToken ct)
        {
            // Initial pull if configured
            if (_settings.SyncMode == "supabase")
            {
                await PullFromSupabaseAsync(true);
            }

            int tick = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    int interval = Math.Max(2, _settings.PollIntervalSeconds);
                    await Task.Delay(interval * 1000, ct);
                    tick++;

                    // Expire manual session locally if needed
                    bool expiredNow = false;
                    lock (_lock)
                    {
                        if (_state.ExpireManual(TimeUtil.NowMs()))
                        {
                            _state.updatedAt = TimeUtil.NowMs();
                            SaveStateToDisk(_state);
                            expiredNow = true;
                        }
                    }
                    if (expiredNow)
                    {
                        RaiseStateChanged();
                        if (_settings.SyncMode == "supabase")
                            await PushToSupabaseAsync(GetState());
                    }

                    if (_settings.SyncMode == "supabase")
                    {
                        await PullFromSupabaseAsync(false);
                        if (tick % 4 == 0)
                        {
                            await SendHeartbeatAsync();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SetSyncStatus("同期エラー: " + ex.Message, false);
                }
            }
        }

        public async Task<string> TestSupabaseConnectionAsync(AppSettings testSettings)
        {
            if (string.IsNullOrWhiteSpace(testSettings.SupabaseUrl) ||
                string.IsNullOrWhiteSpace(testSettings.SupabaseAnonKey) ||
                string.IsNullOrWhiteSpace(testSettings.SyncKey))
            {
                return "URL / Anon Key / Sync Key をすべて入力してください。";
            }

            try
            {
                string endpoint = testSettings.SupabaseUrl.TrimEnd('/') + "/rest/v1/rpc/focus_guard_get";
                var payload = JsonUtil.Serialize(new Dictionary<string, object> { { "p_sync_key", testSettings.SyncKey.Trim() } });
                using (var req = BuildSupabaseRequest(endpoint, testSettings.SupabaseAnonKey.Trim(), payload))
                using (var res = await _http.SendAsync(req))
                {
                    string body = await res.Content.ReadAsStringAsync();
                    if (!res.IsSuccessStatusCode)
                    {
                        return string.Format("接続失敗 (HTTP {0}): {1}", (int)res.StatusCode, body);
                    }
                    return "OK";
                }
            }
            catch (Exception ex)
            {
                return "接続例外: " + ex.Message;
            }
        }

        public async Task PullFromSupabaseAsync(bool pushIfEmpty)
        {
            AppSettings cfg = GetSettings();
            if (cfg.SyncMode != "supabase" || string.IsNullOrWhiteSpace(cfg.SupabaseUrl) ||
                string.IsNullOrWhiteSpace(cfg.SupabaseAnonKey) || string.IsNullOrWhiteSpace(cfg.SyncKey))
            {
                return;
            }

            try
            {
                string endpoint = cfg.SupabaseUrl.TrimEnd('/') + "/rest/v1/rpc/focus_guard_get";
                var payload = JsonUtil.Serialize(new Dictionary<string, object> { { "p_sync_key", cfg.SyncKey.Trim() } });

                using (var req = BuildSupabaseRequest(endpoint, cfg.SupabaseAnonKey.Trim(), payload))
                using (var res = await _http.SendAsync(req))
                {
                    string body = await res.Content.ReadAsStringAsync();
                    if (!res.IsSuccessStatusCode)
                    {
                        SetSyncStatus(string.Format("Supabase取得エラー ({0})", (int)res.StatusCode), false);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(body) || body.Trim() == "null" || body.Trim() == "[]")
                    {
                        if (pushIfEmpty)
                        {
                            await PushToSupabaseAsync(GetState());
                        }
                        else
                        {
                            SetSyncStatus("Supabase 同期済み (初期未登録)", true);
                        }
                        return;
                    }

                    // RPC returns either object { state: {...} } or array [{ state: {...} }] or directly state json
                    GuardState remoteState = ExtractStateFromRpcResponse(body);
                    if (remoteState == null)
                    {
                        if (pushIfEmpty) await PushToSupabaseAsync(GetState());
                        return;
                    }
                    remoteState.EnsureDefaults();

                    bool updatedLocal = false;
                    lock (_lock)
                    {
                        if (remoteState.updatedAt > _state.updatedAt)
                        {
                            _state = remoteState;
                            SaveStateToDisk(_state);
                            updatedLocal = true;
                        }
                    }

                    if (updatedLocal)
                    {
                        RaiseStateChanged();
                    }

                    SetSyncStatus("Supabase 同期済み (" + DateTime.Now.ToString("HH:mm:ss") + ")", true);
                }
            }
            catch (Exception ex)
            {
                SetSyncStatus("オフライン (キャッシュ稼働中): " + ex.Message, false);
            }
        }

        public async Task PushToSupabaseAsync(GuardState stateToPush)
        {
            AppSettings cfg = GetSettings();
            if (cfg.SyncMode != "supabase" || string.IsNullOrWhiteSpace(cfg.SupabaseUrl) ||
                string.IsNullOrWhiteSpace(cfg.SupabaseAnonKey) || string.IsNullOrWhiteSpace(cfg.SyncKey))
            {
                return;
            }

            try
            {
                string endpoint = cfg.SupabaseUrl.TrimEnd('/') + "/rest/v1/rpc/focus_guard_save";
                var payload = JsonUtil.Serialize(new Dictionary<string, object>
                {
                    { "p_sync_key", cfg.SyncKey.Trim() },
                    { "p_state", stateToPush }
                });

                using (var req = BuildSupabaseRequest(endpoint, cfg.SupabaseAnonKey.Trim(), payload))
                using (var res = await _http.SendAsync(req))
                {
                    if (!res.IsSuccessStatusCode)
                    {
                        string body = await res.Content.ReadAsStringAsync();
                        SetSyncStatus(string.Format("Supabase保存エラー ({0}): {1}", (int)res.StatusCode, body), false);
                        return;
                    }
                    SetSyncStatus("Supabase 送信完了 (" + DateTime.Now.ToString("HH:mm:ss") + ")", true);
                }
            }
            catch (Exception ex)
            {
                SetSyncStatus("送信エラー (オフライン): " + ex.Message, false);
            }
        }

        private async Task SendHeartbeatAsync()
        {
            AppSettings cfg = GetSettings();
            if (cfg.SyncMode != "supabase" || string.IsNullOrWhiteSpace(cfg.SupabaseUrl) ||
                string.IsNullOrWhiteSpace(cfg.SupabaseAnonKey) || string.IsNullOrWhiteSpace(cfg.SyncKey))
            {
                return;
            }

            try
            {
                var eff = RuleEngine.Evaluate(GetState(), "pc", LocalSnoozeUntilMs);
                string statusText = eff.IsActive ? (eff.IsSnoozed ? "snoozed" : "blocking") : "idle";
                string endpoint = cfg.SupabaseUrl.TrimEnd('/') + "/rest/v1/rpc/focus_guard_heartbeat";
                var payload = JsonUtil.Serialize(new Dictionary<string, object>
                {
                    { "p_sync_key", cfg.SyncKey.Trim() },
                    { "p_device", "pc" },
                    { "p_status", statusText },
                    { "p_level", eff.Level }
                });
                using (var req = BuildSupabaseRequest(endpoint, cfg.SupabaseAnonKey.Trim(), payload))
                using (var res = await _http.SendAsync(req))
                {
                    // Ignore non-fatal heartbeat failures
                }
            }
            catch { }
        }

        private HttpRequestMessage BuildSupabaseRequest(string url, string anonKey, string jsonBody)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("apikey", anonKey);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", anonKey);
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            return req;
        }

        private GuardState ExtractStateFromRpcResponse(string body)
        {
            var ser = JsonUtil.CreateSerializer();
            object parsed = ser.DeserializeObject(body);
            if (parsed == null) return null;

            var dict = parsed as Dictionary<string, object>;
            if (dict == null)
            {
                var arr = parsed as object[];
                if (arr != null && arr.Length > 0)
                {
                    dict = arr[0] as Dictionary<string, object>;
                }
            }
            if (dict == null) return null;

            if (dict.ContainsKey("state") && dict["state"] != null)
            {
                string innerJson = ser.Serialize(dict["state"]);
                return ser.Deserialize<GuardState>(innerJson);
            }
            if (dict.ContainsKey("manualSession") || dict.ContainsKey("manualSessions") || dict.ContainsKey("schedules"))
            {
                return ser.Deserialize<GuardState>(body);
            }
            return null;
        }

        private void StartLocalHttpServer(CancellationToken ct)
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Loopback, _settings.LocalPort);
                _tcpListener.Start();

                Task.Run(async () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            TcpClient client = await _tcpListener.AcceptTcpClientAsync();
                            var _ = Task.Run(() => HandleTcpClientAsync(client));
                        }
                        catch (ObjectDisposedException) { break; }
                        catch (SocketException) { break; }
                        catch { }
                    }
                }, ct);
            }
            catch
            {
                // Port might already be in use; native UI still works fine.
            }
        }

        private async Task HandleTcpClientAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] buf = new byte[65536];
                        int totalRead = 0;
                        int headerEnd = -1;

                        while (totalRead < buf.Length)
                        {
                            int n = await stream.ReadAsync(buf, totalRead, buf.Length - totalRead);
                            if (n <= 0) break;
                            totalRead += n;
                            headerEnd = FindHeaderEnd(buf, totalRead);
                            if (headerEnd >= 0) break;
                        }
                        if (headerEnd < 0) return;

                        string headerText = Encoding.ASCII.GetString(buf, 0, headerEnd);
                        string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                        if (lines.Length == 0) return;

                        string[] reqLine = lines[0].Split(' ');
                        if (reqLine.Length < 2) return;
                        string method = reqLine[0].ToUpperInvariant();
                        string rawPath = reqLine[1];
                        int qIdx = rawPath.IndexOf('?');
                        string path = (qIdx >= 0 ? rawPath.Substring(0, qIdx) : rawPath).ToLowerInvariant();

                        int contentLength = 0;
                        for (int i = 1; i < lines.Length; i++)
                        {
                            if (lines[i].StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                int.TryParse(lines[i].Substring(15).Trim(), out contentLength);
                            }
                        }

                        int bodyOffset = headerEnd + 4;
                        int bodyHave = totalRead - bodyOffset;
                        byte[] bodyBytes = new byte[Math.Max(0, contentLength)];
                        if (contentLength > 0)
                        {
                            int copyInit = Math.Min(bodyHave, contentLength);
                            if (copyInit > 0) Buffer.BlockCopy(buf, bodyOffset, bodyBytes, 0, copyInit);
                            int got = copyInit;
                            while (got < contentLength)
                            {
                                int n = await stream.ReadAsync(bodyBytes, got, contentLength - got);
                                if (n <= 0) break;
                                got += n;
                            }
                        }
                        string body = contentLength > 0 ? Encoding.UTF8.GetString(bodyBytes) : "";

                        if (method == "OPTIONS")
                        {
                            await WriteHttpResponseAsync(stream, 204, "No Content", "text/plain", "");
                            return;
                        }

                        if ((path == "/" || path == "/index.html") && method == "GET")
                        {
                            string html = File.Exists(_webHtmlPath)
                                ? File.ReadAllText(_webHtmlPath, Encoding.UTF8)
                                : ReadEmbeddedWebHtml();
                            await WriteHttpResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", html);
                            return;
                        }

                        if (path == "/api/state" && method == "GET")
                        {
                            var st = GetState();
                            var effPc = RuleEngine.Evaluate(st, "pc", LocalSnoozeUntilMs);
                            var effAndroid = RuleEngine.Evaluate(st, "android", 0);
                            var cfg = GetSettings();
                            var payload = new Dictionary<string, object>
                            {
                                { "state", st },
                                { "effectivePc", effPc },
                                { "effectiveAndroid", effAndroid },
                                { "syncMode", cfg.SyncMode },
                                { "syncMessage", LastSyncMessage },
                                { "syncOk", LastSyncOk }
                            };
                            await WriteHttpResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", JsonUtil.Serialize(payload));
                            return;
                        }

                        if (path == "/api/state" && method == "POST")
                        {
                            bool lv2Verified = rawPath.IndexOf("lv2verified=1", StringComparison.OrdinalIgnoreCase) >= 0;
                            var nextState = JsonUtil.Deserialize<GuardState>(body);
                            string err;
                            if (!UpdateState(nextState, out err, lv2Verified))
                            {
                                await WriteHttpResponseAsync(stream, 403, "Forbidden", "application/json; charset=utf-8",
                                    JsonUtil.Serialize(new Dictionary<string, object> { { "ok", false }, { "error", err } }));
                                return;
                            }
                            await WriteHttpResponseAsync(stream, 200, "OK", "application/json; charset=utf-8",
                                JsonUtil.Serialize(new Dictionary<string, object> { { "ok", true }, { "state", GetState() } }));
                            return;
                        }

                        await WriteHttpResponseAsync(stream, 404, "Not Found", "application/json; charset=utf-8", "{\"error\":\"not found\"}");
                    }
                }
                catch { }
            }
        }

        private static int FindHeaderEnd(byte[] buf, int length)
        {
            for (int i = 0; i <= length - 4; i++)
            {
                if (buf[i] == '\r' && buf[i + 1] == '\n' && buf[i + 2] == '\r' && buf[i + 3] == '\n')
                    return i;
            }
            return -1;
        }

        private static async Task WriteHttpResponseAsync(NetworkStream stream, int statusCode, string reason, string contentType, string content)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(content ?? "");
            string header = string.Format(
                "HTTP/1.1 {0} {1}\r\n" +
                "Content-Type: {2}\r\n" +
                "Content-Length: {3}\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                "Access-Control-Allow-Headers: Content-Type\r\n" +
                "Connection: close\r\n\r\n",
                statusCode, reason, contentType, bodyBytes.Length);
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            if (bodyBytes.Length > 0)
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
            await stream.FlushAsync();
        }

        private AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                    var s = JsonUtil.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        if (s.LocalPort <= 0) s.LocalPort = 8787;
                        if (s.PollIntervalSeconds < 2) s.PollIntervalSeconds = 4;
                        if (string.IsNullOrEmpty(s.SyncMode)) s.SyncMode = "local";
                        if (string.IsNullOrEmpty(s.SyncKey)) s.SyncKey = Guid.NewGuid().ToString("N").Substring(0, 16);
                        return s;
                    }
                }
            }
            catch { }
            var def = new AppSettings();
            File.WriteAllText(_settingsPath, JsonUtil.Serialize(def), Encoding.UTF8);
            return def;
        }

        private GuardState LoadState()
        {
            try
            {
                if (File.Exists(_statePath))
                {
                    string json = File.ReadAllText(_statePath, Encoding.UTF8);
                    var st = JsonUtil.Deserialize<GuardState>(json);
                    if (st != null)
                    {
                        st.EnsureDefaults();
                        return st;
                    }
                }
            }
            catch { }
            var def = new GuardState();
            SaveStateToDisk(def);
            return def;
        }

        private void SaveStateToDisk(GuardState st)
        {
            try
            {
                File.WriteAllText(_statePath, JsonUtil.Serialize(st), Encoding.UTF8);
            }
            catch { }
        }

        private void SetSyncStatus(string message, bool ok)
        {
            LastSyncMessage = message;
            LastSyncOk = ok;
            if (ok) LastSyncTime = DateTime.Now;
            var handler = SyncStatusChanged;
            if (handler != null) handler(message, ok);
        }

        private void RaiseStateChanged()
        {
            var handler = StateChanged;
            if (handler != null) handler(GetState());
        }

        public void Dispose()
        {
            try { if (_cts != null) _cts.Cancel(); } catch { }
            try { if (_tcpListener != null) _tcpListener.Stop(); } catch { }
            try { if (_http != null) _http.Dispose(); } catch { }
        }
    }
}
