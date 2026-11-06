using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace WT
{
    /// <summary>
    /// 取词：先试 UI Automation 读选区（不动剪贴板），失败再用剪贴板法兜底。
    /// </summary>
    internal static class TextGrabber
    {
        public static string Grab()
        {
            string text = null;

            try
            {
                text = GrabByUia();
                if (!string.IsNullOrEmpty(text)) Log.Info("取词成功(UIA) 长度=" + text.Length);
                else Log.Info("UIA 未取到文本");
            }
            catch (Exception ex)
            {
                Log.Exception("UIA 取词异常", ex);
            }

            if (string.IsNullOrEmpty(Normalize(text)))
            {
                try
                {
                    text = GrabByClipboard();
                    if (!string.IsNullOrEmpty(text)) Log.Info("取词成功(剪贴板) 长度=" + text.Length);
                    else Log.Info("剪贴板法未取到文本");
                }
                catch (Exception ex)
                {
                    Log.Exception("剪贴板取词异常", ex);
                }
            }

            return Normalize(text);
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Replace("\r\n", "\n").Trim();
            if (s.Length == 0) return null;
            if (s.Length > 4000) s = s.Substring(0, 4000);
            return s;
        }

        private static string GrabByUia()
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return null;

            object pattern;
            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) return null;
            var tp = pattern as TextPattern;
            if (tp == null) return null;

            TextPatternRange[] ranges = tp.GetSelection();
            if (ranges == null || ranges.Length == 0) return null;

            var sb = new StringBuilder();
            foreach (var r in ranges)
            {
                var part = r.GetText(-1);
                if (!string.IsNullOrEmpty(part)) sb.Append(part);
            }
            return sb.ToString();
        }

        private static string GrabByClipboard()
        {
            IDataObject backup = null;
            try { backup = Clipboard.GetDataObject(); } catch { }

            string before = SafeGetText();
            if (before == null) before = string.Empty;

            SendCtrlC();

            string after = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 700)
            {
                Application.DoEvents();
                Thread.Sleep(30);
                after = SafeGetText();
                if (after != null && after != before) break;
            }

            // 还原剪贴板
            if (backup != null)
            {
                try { Clipboard.SetDataObject(backup, true); } catch { }
            }

            if (after == null || after == before) return null;
            return after;
        }

        private static string SafeGetText()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (Clipboard.ContainsText()) return Clipboard.GetText();
                    return null;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    Thread.Sleep(40);
                }
                catch (Exception)
                {
                    Thread.Sleep(40);
                }
            }
            return null;
        }

        private static void SendCtrlC()
        {
            Native.keybd_event((byte)Native.VK_CONTROL, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            Native.keybd_event((byte)Native.VK_C, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            Native.keybd_event((byte)Native.VK_C, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(20);
            Native.keybd_event((byte)Native.VK_CONTROL, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        /// <summary>
        /// 粗略判断是不是英文：字母占多数，且至少两个字母。
        /// </summary>
        public static bool LooksLikeEnglish(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int letters = 0, cjk = 0, other = 0;
            foreach (char c in s)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) letters++;
                else if (c >= 0x4E00 && c <= 0x9FFF) cjk++;
                else if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsDigit(c)) other++;
            }
            if (letters < 2) return false;
            return letters >= cjk * 2;
        }
    }
}