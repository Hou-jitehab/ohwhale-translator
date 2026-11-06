using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WT
{
    /// <summary>
    /// 全局鼠标/键盘钩子：按住 Alt 拖选，抬起时触发一次取词。
    /// 同时负责浮层显示时的 Esc 关闭。
    /// </summary>
    internal class SelectionCapture : IDisposable
    {
        private Native.LowLevelProc _mouseProc;
        private Native.LowLevelProc _keyProc;
        private IntPtr _mouseHook = IntPtr.Zero;
        private IntPtr _keyHook = IntPtr.Zero;

        private bool _dragging;
        private bool _altAtDown;
        private Native.POINT _downPoint;
        private DateTime _downAt;

        public event Action<int, int> SelectionFinished; // x, y (physical px)
        public event Action EscapePressed;
        public event Action AltChanged;

        private bool _altDown;

        public void Start()
        {
            _mouseProc = MouseCallback;
            _keyProc = KeyboardCallback;

            var module = Native.GetModuleHandle(null);
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, module, 0);
            _keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyProc, module, 0);

            if (_mouseHook == IntPtr.Zero) Log.Error("鼠标钩子安装失败，错误码 " + Marshal.GetLastWin32Error());
            else Log.Info("鼠标钩子已安装");
            if (_keyHook == IntPtr.Zero) Log.Error("键盘钩子安装失败，错误码 " + Marshal.GetLastWin32Error());
            else Log.Info("键盘钩子已安装");
        }

        private void TrackAlt()
        {
            bool now = Native.IsAltDown();
            if (now != _altDown)
            {
                _altDown = now;
                var h = AltChanged;
                if (h != null) h();
            }
        }

        private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    var info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));

                    if (msg == Native.WM_LBUTTONDOWN)
                    {
                        if (!Settings.Current.RequireAlt || Native.IsAltDown())
                        {
                            _dragging = true;
                            _altAtDown = Native.IsAltDown();
                            _downPoint = info.pt;
                            _downAt = DateTime.UtcNow;
                            Log.Info("[hook] 开始拖选 alt=" + _altAtDown);
                        }
                    }
                    else if (msg == Native.WM_LBUTTONUP)
                    {
                        if (_dragging)
                        {
                            _dragging = false;
                            int dx = Math.Abs(info.pt.X - _downPoint.X);
                            int dy = Math.Abs(info.pt.Y - _downPoint.Y);
                            int threshold = Settings.Current.DragThreshold;
                            bool altOk = !Settings.Current.RequireAlt || _altAtDown || Native.IsAltDown();
                            double elapsed = (DateTime.UtcNow - _downAt).TotalMilliseconds;
                            Log.Info(string.Format("[hook] 松开 dx={0} dy={1} altOk={2} elapsed={3:0}ms", dx, dy, altOk, elapsed));
                            if (altOk && (dx + dy) >= threshold && elapsed < 60000)
                            {
                                var h = SelectionFinished;
                                if (h != null) h(info.pt.X, info.pt.Y);
                            }
                        }
                    }
                    TrackAlt();
                }
            }
            catch (Exception ex)
            {
                Log.Exception("鼠标钩子回调异常", ex);
            }
            return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    var info = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    int msg = wParam.ToInt32();
                    bool isDown = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;

                    if (isDown && info.vkCode == Native.VK_ESCAPE)
                    {
                        var h = EscapePressed;
                        if (h != null) h();
                    }
                    TrackAlt();
                }
            }
            catch (Exception ex)
            {
                Log.Exception("键盘钩子回调异常", ex);
            }
            return Native.CallNextHookEx(_keyHook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
            if (_keyHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_keyHook); _keyHook = IntPtr.Zero; }
        }
    }
}