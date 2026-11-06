using System;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WT
{
    internal class EngineException : Exception
    {
        public EngineException(string message) : base(message) { }
    }

    internal class PageStatus
    {
        public bool HasInput;
        public bool StopVisible;
        public int MarkdownCount;
        public string Text;
    }

    /// <summary>启动自检结果：页面结构是否还能被识别。</summary>
    internal class PageCheck
    {
        public string Url;
        public bool HasInput;
        public bool HasSend;
        public int Toggles;
        public int Nodes;
        public string Error;

        public bool Ok { get { return Error == null && HasInput && HasSend; } }

        public string Describe()
        {
            if (Error != null) return "自检异常：" + Error;
            if (!HasInput) return "找不到输入框（未登录，或页面改版）";
            if (!HasSend) return "找不到发送按钮（页面可能改版）";
            return "页面正常（开关 " + Toggles + " 个）";
        }
    }

    /// <summary>
    /// 用内嵌 WebView2 驱动 chat.deepseek.com 做翻译。
    /// 页面离屏运行，首次需要用户登录一次（登录态存在独立 profile 里）。
    /// </summary>
    internal class WebViewEngine : IDisposable
    {
        private Form _host;
        private WebView2 _web;
        private bool _ready;
        private bool _disposed;
        private bool _disposingHost;
        private int _rounds;
        private DateTime _lastRequest = DateTime.MinValue;
        private readonly object _gate = new object();
        private readonly JavaScriptSerializer _ser = new JavaScriptSerializer();

        public bool IsReady { get { return _ready; } }
        public bool LoggedIn { get; private set; }

        public event Action<string> StatusChanged;
        public event Action LoginRequired;
        public event Action<bool> VisibilityChanged;
        public event Action<string> Partial;    // 流式增量译文
        public event Action Crashed;            // 浏览器进程挂了，需要重建引擎

        private void Status(string s)
        {
            Log.Info("[engine] " + s);
            var h = StatusChanged;
            if (h != null) h(s);
        }

        // ---------- 启动 ----------

        public async Task StartAsync()
        {
            _host = new Form();
            _host.Text = "哦鲸鲸翻译 - DeepSeek";
            _host.FormBorderStyle = FormBorderStyle.Sizable;
            _host.ControlBox = true;
            _host.MinimizeBox = true;
            _host.MaximizeBox = true;
            _host.StartPosition = FormStartPosition.Manual;
            _host.ShowInTaskbar = false;
            _host.Size = new Size(1100, 860);
            _host.Location = new Point(-6000, -6000);
            _host.BackColor = Color.Black;
            _host.FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                // 关窗口 = 只是收起，不能把翻译引擎关掉
                if (_disposingHost) return;
                e.Cancel = true;
                // 关闭流程里直接改位置会被回滚，等这一轮消息处理完再收
                _host.BeginInvoke((MethodInvoker)delegate
                {
                    try { ShowDebugWindow(false); }
                    catch (Exception ex) { Log.Exception("收起调试窗口失败", ex); }
                });
            };

            _web = new WebView2();
            _web.Dock = DockStyle.Fill;
            _host.Controls.Add(_web);
            _host.Show();

            Status("正在初始化 WebView2…");
            try
            {
                // 离屏窗口容易被 Chromium 当作“被遮挡”而节流，这些开关保证后台也能正常收发
                string browserArgs =
                    "--disable-background-timer-throttling " +
                    "--disable-renderer-backgrounding " +
                    "--disable-backgrounding-occluded-windows " +
                    "--disable-features=CalculateNativeWinOcclusion";
                var debugPort = Environment.GetEnvironmentVariable("WT_DEBUG_PORT");
                if (!string.IsNullOrEmpty(debugPort))
                {
                    browserArgs += " --remote-debugging-port=" + debugPort;
                    Log.Info("[engine] 调试端口已开启: " + debugPort);
                }
                var options = new CoreWebView2EnvironmentOptions(browserArgs);
                var env = await CoreWebView2Environment.CreateAsync(null, Settings.WebViewDir, options);
                await _web.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                Log.Exception("WebView2 初始化失败", ex);
                throw new EngineException("WebView2 初始化失败（可能缺少 WebView2 运行时）：" + ex.Message);
            }

            _web.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.IsZoomControlEnabled = false;
            _web.CoreWebView2.NavigationCompleted += OnNavCompleted;
            _web.CoreWebView2.ProcessFailed += OnProcessFailed;

            Status("正在打开 chat.deepseek.com…");
            _web.CoreWebView2.Navigate(PageSelectors.PageUrl);
            _ready = true;

            TryApplyDebugWindow();
        }

        private void OnNavCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            Log.Info("[engine] 导航完成 success=" + e.IsSuccess + " status=" + e.HttpStatusCode);
            if (!e.IsSuccess) Status("页面加载失败：" + e.WebErrorStatus);
        }

        private void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            Log.Error("[engine] 浏览器进程异常：" + e.ProcessFailedKind);
            Status("浏览器进程异常：" + e.ProcessFailedKind);
            _ready = false;
            var h = Crashed;
            if (h != null)
            {
                try { h(); } catch (Exception ex) { Log.Exception("处理引擎崩溃回调失败", ex); }
            }
        }

        /// <summary>启动自检：确认页面结构还能被识别。</summary>
        public async Task<PageCheck> CheckPageAsync()
        {
            var chk = new PageCheck();
            try
            {
                var raw = await ExecRawAsync(PageSelectors.SelfCheckJs);
                var dict = _ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(raw);
                if (dict != null)
                {
                    if (dict.ContainsKey("url")) chk.Url = Convert.ToString(dict["url"]);
                    if (dict.ContainsKey("hasInput")) chk.HasInput = Convert.ToBoolean(dict["hasInput"]);
                    if (dict.ContainsKey("hasSend")) chk.HasSend = Convert.ToBoolean(dict["hasSend"]);
                    if (dict.ContainsKey("toggles")) chk.Toggles = Convert.ToInt32(dict["toggles"]);
                    if (dict.ContainsKey("nodes")) chk.Nodes = Convert.ToInt32(dict["nodes"]);
                }
            }
            catch (Exception ex) { Log.Exception("页面自检失败", ex); chk.Error = ex.Message; }
            return chk;
        }

        private void TryApplyDebugWindow()
        {
            try
            {
                if (_host == null) return;
                bool show = Settings.Current.ShowEngineWindow;
                _host.ShowInTaskbar = show;
                _host.TopMost = show;
                if (show)
                {
                    _host.Location = new Point(80, 80);
                    _host.Activate();
                }
                else
                {
                    // 只改 Location 有时不生效（句柄重建会回滚），这里直接调 SetWindowPos
                    _host.Location = new Point(-6000, -6000);
                    Native.SetWindowPos(_host.Handle, IntPtr.Zero, -6000, -6000, 0, 0,
                        Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                }
                Log.Info("[engine] 调试窗口 show=" + show + " loc=" + _host.Location.X + "," + _host.Location.Y);
                var h = VisibilityChanged;
                if (h != null) h(show);
            }
            catch (Exception ex) { Log.Exception("切换调试窗口失败", ex); }
        }

        public void ShowDebugWindow(bool show)
        {
            Settings.Current.ShowEngineWindow = show;
            Settings.Current.Save();
            TryApplyDebugWindow();
        }

        // ---------- JS 执行 ----------

        private async Task<string> ExecRawAsync(string js)
        {
            if (!_ready || _web == null || _web.CoreWebView2 == null) throw new EngineException("引擎尚未就绪");
            return await _web.ExecuteScriptAsync(js);
        }

        private static string Unquote(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            json = json.Trim();
            if (json.Length >= 2 && json[0] == '"' && json[json.Length - 1] == '"')
            {
                try
                {
                    var ser = new JavaScriptSerializer();
                    return ser.Deserialize<string>(json);
                }
                catch { return json.Trim('"'); }
            }
            return json;
        }

        private async Task<string> ExecJsAsync(string js)
        {
            return Unquote(await ExecRawAsync(js));
        }

        private async Task<PageStatus> GetStatusAsync()
        {
            var raw = await ExecRawAsync(PageSelectors.StatusJs);
            var st = new PageStatus();
            try
            {
                var dict = _ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(raw);
                if (dict != null)
                {
                    if (dict.ContainsKey("input")) st.HasInput = Convert.ToBoolean(dict["input"]);
                    if (dict.ContainsKey("stop")) st.StopVisible = Convert.ToBoolean(dict["stop"]);
                    if (dict.ContainsKey("count")) st.MarkdownCount = Convert.ToInt32(dict["count"]);
                    if (dict.ContainsKey("text")) st.Text = Convert.ToString(dict["text"]);
                }
            }
            catch (Exception ex)
            {
                Log.Exception("解析页面状态失败 raw=" + raw, ex);
            }
            return st;
        }

        // ---------- 登录 ----------

        public async Task<bool> EnsureLoggedInAsync(int timeoutSeconds)
        {
            if (LoggedIn) return true;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeoutSeconds)
            {
                PageStatus st = null;
                try { st = await GetStatusAsync(); }
                catch (Exception ex) { Log.Exception("检测登录状态失败", ex); }

                if (st == null) { await Task.Delay(1000); continue; }

                if (st.HasInput)
                {
                    LoggedIn = true;
                    Status("已登录");
                    TryApplyDebugWindow();
                    return true;
                }
                await Task.Delay(1000);
            }

            var h = LoginRequired;
            if (h != null) h();
            return false;
        }

        // ---------- 翻译 ----------

        // 点一次「停止生成」。空闲时按钮带 ds-button--disabled，点它等于什么都没做。
        public async Task<bool> StopGenerationAsync()
        {
            try
            {
                string r = await ExecJsAsync(PageSelectors.StopJs);
                Log.Info("[engine] 停止生成 " + r);
                bool stopped = r == "STOPPED" || r == "STOPPED_ICON";
                if (stopped) await Task.Delay(600);
                return stopped;
            }
            catch (Exception ex) { Log.Exception("停止生成失败", ex); return false; }
        }

        public async Task<string> TranslateAsync(string sourceText, CancellationToken ct = default(CancellationToken))
        {
            int wait;
            lock (_gate)
            {
                var since = DateTime.UtcNow - _lastRequest;
                int minMs = Settings.Current.MinIntervalMs;
                wait = since.TotalMilliseconds < minMs ? minMs - (int)since.TotalMilliseconds : 0;
            }
            if (wait > 0) await Task.Delay(wait, ct);
            lock (_gate) { _lastRequest = DateTime.UtcNow; }

            if (!_ready) throw new EngineException("引擎尚未就绪，请稍候");
            ct.ThrowIfCancellationRequested();

            var before = await GetStatusAsync();
            if (!before.HasInput)
            {
                LoggedIn = false;
                throw new EngineException("网页版未登录。请在托盘菜单里选择「显示 DeepSeek 窗口」完成登录。");
            }

            if (_rounds >= Settings.Current.MaxRoundsBeforeReset)
            {
                Status("已满 " + _rounds + " 轮，开启新对话");
                await NewChatAsync();
                _rounds = 0;
                before = await GetStatusAsync();
            }

            // 每次发送前先收拾干净：如果上一轮还在生成，先点停止，避免排队等待
            await StopGenerationAsync();
            ct.ThrowIfCancellationRequested();

            string prep = await ExecJsAsync(PageSelectors.PrepareJs);
            Log.Info("[engine] 开关状态 " + prep);
            if (prep != null && prep.StartsWith("TOGGLED")) await Task.Delay(350, ct);

            string prompt = Settings.Current.PromptPrefix + "\n" + sourceText;
            string js = PageSelectors.BuildSendJs(_ser.Serialize(prompt));
            string sendResult = await ExecJsAsync(js);
            Log.Info("[engine] 发送结果 " + sendResult);

            if (sendResult == "NO_INPUT")
            {
                LoggedIn = false;
                throw new EngineException("找不到输入框，可能登录已失效或页面结构变了。");
            }
            if (sendResult == "NO_SEND_BTN")
            {
                throw new EngineException("找不到发送按钮（页面结构可能变了）。");
            }
            if (sendResult != null && sendResult.StartsWith("INPUT_ERR"))
            {
                throw new EngineException("往输入框写文字失败：" + sendResult);
            }

            int baselineCount = before.MarkdownCount;

            // 阶段一：等待出现新的回答节点
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool started = false;
            PageStatus st = null;
            while (sw.Elapsed.TotalSeconds < Settings.Current.FirstTokenTimeoutSec)
            {
                await Task.Delay(300, ct);
                ct.ThrowIfCancellationRequested();
                st = await GetStatusAsync();
                if (st.MarkdownCount > baselineCount)
                {
                    started = true;
                    break;
                }
            }

            if (!started)
            {
                throw new EngineException("网页版没有开始响应（可能被限流或网络异常）。");
            }

            // 阶段二：等待输出结束，同时把增量文本推给浮层
            string lastText = null;
            string lastPushed = null;
            var stableSince = DateTime.UtcNow;
            sw.Restart();
            while (sw.Elapsed.TotalSeconds < Settings.Current.TranslateTimeoutSec)
            {
                await Task.Delay(250, ct);
                ct.ThrowIfCancellationRequested();
                st = await GetStatusAsync();
                string cur = st.Text == null ? "" : st.Text;

                if (cur != lastText)
                {
                    lastText = cur;
                    stableSince = DateTime.UtcNow;

                    string pv = CleanOutput(cur);
                    if (!string.IsNullOrEmpty(pv) && pv != lastPushed)
                    {
                        lastPushed = pv;
                        var ph = Partial;
                        if (ph != null)
                        {
                            try { ph(pv); } catch (Exception ex) { Log.Exception("推送增量译文失败", ex); }
                        }
                    }
                }

                bool quiet = (DateTime.UtcNow - stableSince).TotalMilliseconds > 1200;
                if (!st.StopVisible && quiet && cur.Length > 0) break;
            }

            _rounds++;
            string cleaned = CleanOutput(lastText);
            if (string.IsNullOrEmpty(cleaned))
                throw new EngineException("没有拿到译文（页面结构可能变了）。");

            Log.Info("[engine] 译文长度=" + cleaned.Length + " 轮次=" + _rounds);
            return cleaned;
        }

        public async Task NewChatAsync()
        {
            try
            {
                _web.CoreWebView2.Navigate(PageSelectors.PageUrl);
                await Task.Delay(2500);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < 10)
                {
                    var st = await GetStatusAsync();
                    if (st.HasInput && st.MarkdownCount == 0) break;
                    if (st.HasInput && sw.Elapsed.TotalSeconds > 4) break;
                    await Task.Delay(400);
                }
                Log.Info("[engine] 新对话已就绪");
            }
            catch (Exception ex) { Log.Exception("新对话失败", ex); }
        }

        private static string CleanOutput(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var sb = new StringBuilder();
            string[] lines = s.Replace("\r\n", "\n").Split('\n');
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) { sb.AppendLine(); continue; }
                if (line == "复制" || line == "重新生成" || line == "已深度思考") continue;
                if (line.StartsWith("已深度思考（用时") || line.StartsWith("已思考（用时")) continue;
                sb.AppendLine(line);
            }
            string outp = sb.ToString().Trim();

            string[] prefixes = { "译文：", "中文译文：", "翻译：", "以下是翻译：", "**译文：**", "**翻译：**" };
            foreach (var p in prefixes)
            {
                if (outp.StartsWith(p)) { outp = outp.Substring(p.Length).Trim(); break; }
            }
            return outp;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _disposingHost = true;
            try
            {
                if (_web != null) { _web.Dispose(); _web = null; }
                if (_host != null) { _host.Close(); _host.Dispose(); _host = null; }
            }
            catch (Exception ex) { Log.Exception("释放引擎异常", ex); }
        }
    }
}