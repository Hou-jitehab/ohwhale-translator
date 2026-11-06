using System.Text;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WT
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveWebView2;
            if (args != null && args.Length > 0 && string.Equals(args[0], "--diag", StringComparison.OrdinalIgnoreCase))
            {
                RunDiag(args.Length > 1 ? args[1] : null);
                return;
            }
            if (args != null && args.Length > 0 && string.Equals(args[0], "--overlay-demo", StringComparison.OrdinalIgnoreCase))
            {
                RunOverlayDemo();
                return;
            }
            if (args != null && args.Length > 0 && string.Equals(args[0], "--dump-js", StringComparison.OrdinalIgnoreCase))
            {
                RunDumpJs(args.Length > 1 ? args[1] : null);
                return;
            }
            if (args != null && args.Length > 0 && string.Equals(args[0], "--settings-demo", StringComparison.OrdinalIgnoreCase))
            {
                RunSettingsDemo();
                return;
            }
            if (args != null && args.Length > 0 && string.Equals(args[0], "--preempt-test", StringComparison.OrdinalIgnoreCase))
            {
                RunPreemptTest(args.Length > 1 ? args[1] : null);
                return;
            }
            if (args != null && args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                RunSelfTest(
                    args.Length > 1 ? args[1] : "The nucleoprotein of influenza A virus inhibits the innate immune response.",
                    args.Length > 2 ? args[2] : null);
                return;
            }
            Run();
        }

        /// <summary>
        /// 自查模式：WordTranslator.exe --diag &lt;报告输出路径&gt;
        /// 用来判断是环境问题（写不了文件 / 缺 WebView2）还是程序问题。
        /// </summary>
        private static void RunSettingsDemo()
        {
            var f = new SettingsForm();
            var t = new System.Windows.Forms.Timer();
            t.Interval = 5000;
            t.Tick += delegate { t.Stop(); f.Close(); };
            t.Start();
            Application.Run(f);
        }

        /// <summary>把注入页面的 JS 全部写出来，方便用 node --check 验语法。</summary>
        private static void RunDumpJs(string outDir)
        {
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Path.GetTempPath(), "wt-js");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "status.js"), PageSelectors.StatusJs, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "stop.js"), PageSelectors.StopJs, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "prepare.js"), PageSelectors.PrepareJs, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "selfcheck.js"), PageSelectors.SelfCheckJs, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "send.js"), PageSelectors.BuildSendJs("\"hello\""), new UTF8Encoding(false));
        }

        private static void RunDiag(string outPath)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                sb.AppendLine("哦鲸鲸翻译 诊断报告 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("OS: " + Environment.OSVersion.VersionString);
                sb.AppendLine("进程: " + (Environment.Is64BitProcess ? "x64" : "x86") + " / 系统: " + (Environment.Is64BitOperatingSystem ? "x64" : "x86"));
                sb.AppendLine("CLR: " + Environment.Version);
                sb.AppendLine("用户: " + Environment.UserDomainName + "\\" + Environment.UserName);
                sb.AppendLine("完整性级别: " + GetIntegrityText());
                sb.AppendLine("Exe 路径: " + System.Reflection.Assembly.GetExecutingAssembly().Location);
                sb.AppendLine();

                sb.AppendLine("--- 数据目录 ---");
                try
                {
                    sb.AppendLine("选中目录: " + Settings.RootDir);
                    sb.AppendLine("可写: 是");
                }
                catch (Exception ex)
                {
                    sb.AppendLine("可写: 否");
                    sb.AppendLine(ex.Message);
                }
                sb.AppendLine();

                sb.AppendLine("--- WebView2 ---");
                EnsureNativeLoader();
                try
                {
                    sb.AppendLine("运行时版本: " + Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString());
                }
                catch (Exception ex)
                {
                    sb.AppendLine("查询运行时失败: " + ex.Message);
                }
                sb.AppendLine();

                sb.AppendLine("--- 单实例 ---");
                bool createdNew;
                var m = new Mutex(true, "WordTranslator.SingleInstance", out createdNew);
                sb.AppendLine("可获取单实例锁: " + createdNew);
                if (createdNew) m.ReleaseMutex();
                m.Dispose();
            }
            catch (Exception ex)
            {
                sb.AppendLine("诊断过程出错: " + ex);
            }

            string path = string.IsNullOrEmpty(outPath)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diag.txt")
                : outPath;
            try
            {
                File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                var fallback = Path.Combine(Path.GetTempPath(), "WordTranslator-diag.txt");
                try
                {
                    File.WriteAllText(fallback, sb.ToString() + "\n（写 " + path + " 失败：" + ex.Message + "）", new System.Text.UTF8Encoding(false));
                    path = fallback;
                }
                catch { }
            }
            MessageBox.Show("诊断完成，报告已写到：\n" + path, "哦鲸鲸翻译 - 诊断");
        }

        private static void RunOverlayDemo()
        {
            string longText = "The nucleoprotein of influenza A virus inhibits the innate immune response by inducing mitophagy, a selective form of autophagy that targets damaged mitochondria, and this mechanism may contribute to viral pathogenesis.";
            string chinese = "甲型流感病毒的核蛋白通过诱导线粒体自噬来抑制先天免疫反应，线粒体自噬是一种选择性自噬，专门清除受损的线粒体，这一机制可能参与病毒的致病过程。";

            Settings.Current.AutoHide = false;   // 演示时不要自动消失
            var f = new OverlayForm();
            var timer = new System.Windows.Forms.Timer();
            timer.Interval = 1200;
            int n = 0;
            timer.Tick += delegate
            {
                n++;
                if (n == 1) f.ShowResult(longText, chinese, 200, 200);
                if (n >= 8) { timer.Stop(); f.CloseOverlay(); Application.Exit(); }
            };
            f.ShowLoading(longText, 200, 200);
            timer.Start();
            Application.Run();
        }

        private static void RunPreemptTest(string outPath)
        {
            EnsureNativeLoader();
            var sb = new System.Text.StringBuilder();
            string path = string.IsNullOrEmpty(outPath)
                ? Path.Combine(Path.GetTempPath(), "wt-preempt.txt")
                : outPath;
            var form = new Form();
            form.Text = "WordTranslator preempt test";
            form.WindowState = FormWindowState.Minimized;
            form.ShowInTaskbar = false;
            form.Opacity = 0;

            form.Shown += async delegate
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using (var engine = new WebViewEngine())
                    {
                        await engine.StartAsync();
                        bool ok = await engine.EnsureLoggedInAsync(90);
                        sb.AppendLine("[" + sw.ElapsedMilliseconds + "ms] loggedIn=" + ok);

                        var cts = new CancellationTokenSource();
                        var first = engine.TranslateAsync(
                            "Please write a 600 word essay about the history of tea, in English, slowly.", cts.Token);
                        sb.AppendLine("[" + sw.ElapsedMilliseconds + "ms] 第一轮已发出（长任务）");

                        await Task.Delay(1500);
                        sb.AppendLine("[" + sw.ElapsedMilliseconds + "ms] 打断第一轮");
                        cts.Cancel();

                        string second = await engine.TranslateAsync(
                            "The nucleoprotein of influenza A virus inhibits the innate immune response by inducing mitophagy.");
                        sb.AppendLine("[" + sw.ElapsedMilliseconds + "ms] 第二轮结果: " + second);

                        try
                        {
                            string r1 = await first;
                            sb.AppendLine("第一轮结果(应被丢弃): " + r1);
                        }
                        catch (OperationCanceledException) { sb.AppendLine("第一轮已取消 OK"); }
                        catch (Exception ex) { sb.AppendLine("第一轮异常: " + ex.Message); }
                    }
                }
                catch (Exception ex) { sb.AppendLine("ERROR: " + ex); }

                try
                {
                    File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
                    Log.Info("[preempt] 结果已写入 " + path);
                }
                catch (Exception ex) { Log.Exception("写打断测试结果失败", ex); }
                form.Close();
            };

            Application.Run(form);
        }

        private static void RunSelfTest(string text, string outPath)
        {
            EnsureNativeLoader();
            var sb = new System.Text.StringBuilder();
            string path = string.IsNullOrEmpty(outPath)
                ? Path.Combine(Path.GetTempPath(), "wt-selftest.txt")
                : outPath;
            var form = new Form();
            form.Text = "WordTranslator selftest";
            form.WindowState = FormWindowState.Minimized;
            form.ShowInTaskbar = false;
            form.Opacity = 0;

            form.Shown += async delegate
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string result = null;
                string error = null;
                try
                {
                    using (var engine = new WebViewEngine())
                    {
                        engine.StatusChanged += delegate(string s) { sb.AppendLine("[" + sw.ElapsedMilliseconds + "ms] status: " + s); };
                        int partials = 0;
                        string firstPartialAt = "";
                        engine.Partial += delegate(string p)
                        {
                            partials++;
                            if (partials == 1) firstPartialAt = sw.ElapsedMilliseconds + "ms";
                            if (partials <= 3 || partials % 5 == 0)
                                sb.AppendLine("[" + sw.ElapsedMilliseconds + "ms] partial#" + partials + " len=" + p.Length + " " + (p.Length > 24 ? p.Substring(0, 24) + "…" : p));
                        };
                        await engine.StartAsync();
                        bool ok = await engine.EnsureLoggedInAsync(90);
                        sb.AppendLine("[" + sw.ElapsedMilliseconds + "ms] loggedIn=" + ok);
                        if (ok)
                        {
                            var chk = await engine.CheckPageAsync();
                            sb.AppendLine("[" + sw.ElapsedMilliseconds + "ms] 自检: " + chk.Describe() + " url=" + chk.Url);
                            result = await engine.TranslateAsync(text);
                            sb.AppendLine("[" + sw.ElapsedMilliseconds + "ms] translated；增量帧=" + partials + " 首帧=" + firstPartialAt);
                        }
                    }
                }
                catch (Exception ex) { error = ex.ToString(); }

                sb.AppendLine("INPUT: " + text);
                sb.AppendLine("RESULT: " + (result == null ? "(null)" : result));
                if (error != null) sb.AppendLine("ERROR: " + error);
                try
                {
                    File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
                    Log.Info("[selftest] 结果已写入 " + path);
                }
                catch (Exception ex) { Log.Exception("写自检结果失败 " + path, ex); }
                form.Close();
            };

            Application.Run(form);
        }

        private static string GetIntegrityText()
        {
            int rid = GetIntegrityRid();
            switch (rid)
            {
                case 0x1000: return "Low（受限，写文件/全局钩子会被拦截）";
                case 0x2000: return "Medium（正常）";
                case 0x2100: return "Medium Plus";
                case 0x3000: return "High（管理员）";
                case 0x4000: return "System";
                case 0: return "读取失败";
                default: return "0x" + rid.ToString("X");
            }
        }

        private static int GetIntegrityRid()
        {
            try
            {
                IntPtr hProc = System.Diagnostics.Process.GetCurrentProcess().Handle;
                IntPtr hTok;
                if (!OpenProcessToken(hProc, 0x0008, out hTok)) return 0;
                IntPtr buf = Marshal.AllocHGlobal(256);
                int ret;
                bool ok = GetTokenInformation(hTok, 25, buf, 256, out ret); // TokenIntegrityLevel
                int rid = 0;
                if (ok)
                {
                    IntPtr sid = Marshal.ReadIntPtr(buf);
                    if (sid != IntPtr.Zero) rid = Marshal.ReadInt32(sid, 8); // SID: rev,count,auth[6] -> subauth[0]
                }
                Marshal.FreeHGlobal(buf);
                CloseHandle(hTok);
                return rid;
            }
            catch { return 0; }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int len, out int retLen);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Run()
        {
            bool createdNew;
            _mutex = new Mutex(true, "WordTranslator.SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("哦鲸鲸翻译已经在运行了（看右下角托盘）。", "哦鲸鲸翻译");
                return;
            }

            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch { try { Native.SetProcessDPIAware(); } catch { } }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                Log.Exception("UI 未处理异常", e.Exception);
                MessageBox.Show("出错了：" + e.Exception.Message + "\n\n详情见日志：" + Log.FilePath, "哦鲸鲸翻译");
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Log.Exception("进程未处理异常", e.ExceptionObject as Exception);
            };

            Log.Info("=== 启动 ===");
            EnsureNativeLoader();

            int rid = GetIntegrityRid();
            Log.Info("进程完整性级别: 0x" + rid.ToString("X"));
            if (rid == 0x1000)
            {
                Log.Warn("检测到低完整性环境（沙箱），全局钩子会被拦截");
                MessageBox.Show(
                    "程序正运行在「受限/沙箱」环境里（低完整性级别）。\n\n" +
                    "在这种环境下：\n" +
                    "  • 写不进用户数据目录\n" +
                    "  • 全局鼠标钩子会被系统拦截，Alt+划词不会有任何反应\n\n" +
                    "请先退出，然后这样启动：\n" +
                    "  1. 按 Win+R\n" +
                    "  2. 粘贴下面这行并回车：\n" +
                    AppDomain.CurrentDomain.FriendlyName.Replace("vshost.", "") + "\n\n" +
                    "（不要从聊天窗口的文件卡片启动，那条路径会套沙箱）",
                    "哦鲸鲸翻译 - 环境受限", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            AppContext ctx;
            try
            {
                ctx = new AppContext();
            }
            catch (Exception ex)
            {
                Log.Exception("初始化失败", ex);
                MessageBox.Show("启动失败：\n" + ex.Message + "\n\n日志：" + Log.FilePath,
                    "哦鲸鲸翻译", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.Run(ctx);
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        /// <summary>
        /// WebView2 的托管封装要 P/Invoke 原生 WebView2Loader.dll。
        /// 这里把它从内嵌资源释放到数据目录，并把该目录加入 DLL 搜索路径。
        /// </summary>
        private static void EnsureNativeLoader()
        {
            try
            {
                string dir = Path.Combine(Settings.RootDir, "native");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, "WebView2Loader.dll");

                if (!File.Exists(target))
                {
                    byte[] bytes = ReadEmbedded("WebView2Loader.dll");
                    if (bytes != null)
                    {
                        File.WriteAllBytes(target, bytes);
                        Log.Info("已释放 WebView2Loader.dll");
                    }
                    else
                    {
                        string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView2Loader.dll");
                        if (File.Exists(local)) { File.Copy(local, target, true); Log.Info("已从程序目录复制 WebView2Loader.dll"); }
                    }
                }

                SetDllDirectory(dir);
                IntPtr h = LoadLibrary(target);
                if (h == IntPtr.Zero) Log.Error("加载 WebView2Loader.dll 失败，错误码 " + Marshal.GetLastWin32Error());
                else Log.Info("WebView2Loader.dll 已加载");
            }
            catch (Exception ex)
            {
                Log.Exception("准备 WebView2Loader 失败", ex);
            }
        }

        private static byte[] ReadEmbedded(string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            string target = null;
            foreach (var n in asm.GetManifestResourceNames())
            {
                if (n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)) { target = n; break; }
            }
            if (target == null) return null;

            using (var stream = asm.GetManifestResourceStream(target))
            {
                if (stream == null) return null;
                var buffer = new byte[stream.Length];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = stream.Read(buffer, read, buffer.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return buffer;
            }
        }

        private static Assembly _coreAsm;
        private static Assembly _winFormsAsm;

        private static Assembly ResolveWebView2(object sender, ResolveEventArgs args)
        {
            try
            {
                string name = new AssemblyName(args.Name).Name;
                if (name == "Microsoft.Web.WebView2.Core")
                {
                    if (_coreAsm == null) _coreAsm = LoadEmbedded("Microsoft.Web.WebView2.Core.dll");
                    return _coreAsm;
                }
                if (name == "Microsoft.Web.WebView2.WinForms")
                {
                    if (_winFormsAsm == null) _winFormsAsm = LoadEmbedded("Microsoft.Web.WebView2.WinForms.dll");
                    return _winFormsAsm;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("加载内嵌 WebView2 程序集失败 " + args.Name, ex);
            }
            return null;
        }

        private static Assembly LoadEmbedded(string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            var names = asm.GetManifestResourceNames();
            string target = null;
            foreach (var n in names)
            {
                if (n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)) { target = n; break; }
            }
            if (target == null)
            {
                // 兜底：从 exe 同目录加载
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                if (File.Exists(local)) return Assembly.LoadFrom(local);
                throw new FileNotFoundException("找不到内嵌资源 " + fileName);
            }
            using (var stream = asm.GetManifestResourceStream(target))
            {
                if (stream == null) throw new FileNotFoundException("资源流为空 " + target);
                var buffer = new byte[stream.Length];
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = stream.Read(buffer, read, buffer.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return Assembly.Load(buffer);
            }
        }
    }

    internal class AppContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly OverlayForm _overlay;
        private readonly SelectionCapture _capture;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _engineWindowItem;
        private WebViewEngine _engine;
        private readonly SynchronizationContext _ui;
        private readonly object _reqLock = new object();
        private CancellationTokenSource _cts;
        private int _reqSeq;
        private bool _exiting;

        public AppContext()
        {
            Log.Info("初始化组件");

            _overlay = new OverlayForm();
            _ui = SynchronizationContext.Current;
            if (_ui == null) _ui = new WindowsFormsSynchronizationContext();
            _engine = CreateEngine();

            _statusItem = new ToolStripMenuItem("状态：启动中…");
            _statusItem.Enabled = false;

            _engineWindowItem = new ToolStripMenuItem("显示 DeepSeek 窗口（调试）");
            _engineWindowItem.Click += delegate
            {
                _engine.ShowDebugWindow(!_engineWindowItem.Checked);
            };

            var menu = new ContextMenuStrip();

            var miTranslateClip = new ToolStripMenuItem("翻译剪贴板内容（测试用）");
            miTranslateClip.Click += delegate { TranslateClipboard(); };

            var miRelogin = new ToolStripMenuItem("重新登录 DeepSeek");
            miRelogin.Click += delegate { ReLogin(); };

            var miSettings = new ToolStripMenuItem("设置…");
            miSettings.Click += delegate { ShowSettings(); };

            var miLogs = new ToolStripMenuItem("打开日志目录");
            miLogs.Click += delegate { OpenFolder(Log.Directory); };

            var miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { ExitApp(); };

            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miTranslateClip);
            menu.Items.Add(miRelogin);
            menu.Items.Add(_engineWindowItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miSettings);
            menu.Items.Add(miLogs);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExit);

            _tray = new NotifyIcon();
            _tray.Icon = SystemIcons.Application;
            _tray.Text = "哦鲸鲸翻译（Alt+划词）";
            _tray.Visible = true;
            _tray.ContextMenuStrip = menu;

            _capture = new SelectionCapture();
            _capture.SelectionFinished += OnSelectionFinished;
            _capture.EscapePressed += delegate
            {
                if (_overlay.IsShowing) _overlay.CloseOverlay();
            };
            _capture.Start();

            StartEngine();
        }

        private void SetStatus(string text)
        {
            try
            {
                if (_statusItem != null) _statusItem.Text = "状态：" + text;
                if (_tray != null && text.Length < 60) _tray.Text = "哦鲸鲸翻译 - " + text;
            }
            catch { }
        }

        private WebViewEngine CreateEngine()
        {
            var e = new WebViewEngine();
            e.StatusChanged += delegate(string s) { SetStatus(s); };
            e.VisibilityChanged += delegate(bool show)
            {
                try { if (_engineWindowItem != null) _engineWindowItem.Checked = show; }
                catch { }
            };
            e.Partial += delegate(string partial) { OnPartial(partial); };
            e.Crashed += delegate { OnEngineCrashed(); };
            return e;
        }

        private string _lastSource = "";
        private int _lastX, _lastY;

        private void OnPartial(string partial)
        {
            if (_ui != null)
                _ui.Post(delegate { try { _overlay.ShowPartial(_lastSource, partial, _lastX, _lastY); } catch { } }, null);
            else
                _overlay.ShowPartial(_lastSource, partial, _lastX, _lastY);
        }

        private void OnEngineCrashed()
        {
            if (_ui != null) _ui.Post(delegate { RestartEngineAsync(); }, null);
            else RestartEngineAsync();
        }

        private async void RestartEngineAsync()
        {
            Log.Info("[flow] 引擎崩溃，开始重建");
            SetStatus("引擎重启中…");
            try
            {
                var old = _engine;
                _engine = CreateEngine();          // 先换新的，避免空引用
                try { old.Dispose(); } catch (Exception ex) { Log.Exception("释放旧引擎失败", ex); }
                await Task.Delay(800);
                await _engine.StartAsync();
                bool ok = await _engine.EnsureLoggedInAsync(60);
                SetStatus(ok ? "就绪（已自动重启）" : "未登录");
                Log.Info("[flow] 引擎已重启 loggedIn=" + ok);
                if (ok)
                    _tray.ShowBalloonTip(4000, "翻译引擎已自动重启", "浏览器进程刚才挂了，现在已经恢复。", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Log.Exception("引擎重启失败", ex);
                SetStatus("引擎重启失败");
                _tray.ShowBalloonTip(8000, "引擎重启失败", ex.Message, ToolTipIcon.Error);
            }
        }

        private async void StartEngine()
        {
            try
            {
                await _engine.StartAsync();
                bool ok = await _engine.EnsureLoggedInAsync(4);
                if (ok)
                {
                    SetStatus("就绪");
                    var chk = await _engine.CheckPageAsync();
                    Log.Info("[flow] 启动自检：" + chk.Describe() + " url=" + chk.Url);
                    if (!chk.Ok)
                    {
                        _tray.ShowBalloonTip(10000, "页面自检未通过",
                            chk.Describe() + "。DeepSeek 可能改版了，请查看日志。", ToolTipIcon.Warning);
                    }
                }
                else
                {
                    SetStatus("未登录");
                    _engineWindowItem.Checked = true;
                    _engine.ShowDebugWindow(true);
                    _tray.ShowBalloonTip(8000, "需要登录一次",
                        "我已经打开 DeepSeek 窗口，请在里面登录。登录成功后窗口会自动收起。", ToolTipIcon.Info);
                    await WaitForLoginAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Exception("引擎启动失败", ex);
                SetStatus("引擎失败");
                _tray.ShowBalloonTip(8000, "引擎启动失败", ex.Message, ToolTipIcon.Error);
                MessageBox.Show(
                    "翻译引擎没能启动。\n\n" + ex.Message +
                    "\n\n日志：" + Log.FilePath +
                    "\n\n如果是「初始化失败 / 灾难性故障」，通常是程序跑在了受限环境里；" +
                    "请直接从资源管理器双击 exe，不要通过聊天窗口的卡片打开。",
                    "哦鲸鲸翻译", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task WaitForLoginAsync()
        {
            while (!_exiting && !_engine.LoggedIn)
            {
                bool ok = await _engine.EnsureLoggedInAsync(10);
                if (ok) break;
            }
            if (_engine.LoggedIn)
            {
                _engineWindowItem.Checked = false;
                _engine.ShowDebugWindow(false);
                SetStatus("就绪");
                _tray.ShowBalloonTip(4000, "已就绪", "登录成功。按住 Alt 划词就能翻译了。", ToolTipIcon.Info);
            }
        }

        private void ReLogin()
        {
            _engineWindowItem.Checked = true;
            _engine.ShowDebugWindow(true);
            _engineWindowItem.ToolTipText = "登录后会自动检测";
        }

        private void OnSelectionFinished(int x, int y)
        {
            Log.Info("[flow] 收到划词事件 x=" + x + " y=" + y);

            string text = null;
            try { text = TextGrabber.Grab(); }
            catch (Exception ex) { Log.Exception("取词异常", ex); }

            if (string.IsNullOrEmpty(text))
            {
                Log.Info("[flow] 没取到文本");
                _overlay.ShowError("", "没取到选中的文字", x, y);
                return;
            }
            if (!TextGrabber.LooksLikeEnglish(text))
            {
                Log.Info("[flow] 内容不像英文，忽略：" + text.Substring(0, Math.Min(40, text.Length)));
                _overlay.ShowError(Trim(text), "选中的不是英文", x, y);
                return;
            }
            Log.Info("[flow] 开始翻译，长度=" + text.Length);
            TranslateAsync(text, x, y);
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r\n", " ").Replace("\n", " ").Trim();
            return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
        }

        private void TranslateClipboard()
        {
            string text = null;
            try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); }
            catch (Exception ex) { Log.Exception("读剪贴板失败", ex); }

            if (string.IsNullOrEmpty(text))
            {
                _tray.ShowBalloonTip(3000, "剪贴板是空的", "先复制一段英文再试。", ToolTipIcon.Warning);
                return;
            }
            var p = Cursor.Position;
            TranslateAsync(text.Trim(), p.X, p.Y);
        }

        private async void TranslateAsync(string text, int x, int y)
        {
            CancellationToken token;
            bool preempt = false;
            lock (_reqLock)
            {
                _reqSeq++;
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    preempt = true;
                    try { _cts.Cancel(); } catch { }
                }
                _cts = new CancellationTokenSource();
                token = _cts.Token;
            }

            if (preempt) Log.Info("[flow] 有新划词，打断上一轮并重发");

            _lastSource = text;
            _lastX = x;
            _lastY = y;
            _overlay.ShowLoading(text, x, y);

            var eng = _engine;
            try
            {
                if (eng == null || !eng.IsReady)
                {
                    _overlay.ShowError(text, "引擎还在启动，请稍等几秒再试。", x, y);
                    return;
                }
                if (preempt) await eng.StopGenerationAsync();

                string result = await eng.TranslateAsync(text, token);
                if (token.IsCancellationRequested) return;
                _overlay.ShowResult(text, result, x, y);
            }
            catch (OperationCanceledException)
            {
                Log.Info("[flow] 本轮已被新划词打断，丢弃结果");
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;
                Log.Exception("翻译失败", ex);
                string msg = ex is EngineException ? ex.Message : "出错了：" + ex.Message;
                _overlay.ShowError(text, msg, x, y);
            }
        }

        private void ShowSettings()
        {
            using (var f = new SettingsForm())
            {
                f.ShowDialog();
            }
        }

        private static void OpenFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { Log.Exception("打开目录失败", ex); }
        }

        private void ExitApp()
        {
            if (_exiting) return;
            _exiting = true;
            Log.Info("=== 退出 ===");
            try { _capture.Dispose(); } catch { }
            try { _engine.Dispose(); } catch { }
            try { _tray.Visible = false; _tray.Dispose(); } catch { }
            try { _overlay.Dispose(); } catch { }
            ExitThread();
        }
    }
}