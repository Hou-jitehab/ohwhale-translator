using System;
using System.Drawing;
using System.Windows.Forms;

namespace WT
{
    /// <summary>
    /// 置顶浮层：不抢焦点、不进 Alt+Tab、跟随光标出现。
    /// </summary>
    internal class OverlayForm : Form
    {
        private readonly Label _lblSource;
        private readonly Label _lblTarget;
        private readonly Timer _autoHide;
        private bool _pinned;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(28, 28, 32);
            Padding = new Padding(14, 12, 14, 14);
            DoubleBuffered = true;
            Opacity = 0.97;

            _lblSource = new Label();
            _lblSource.AutoSize = false;
            _lblSource.AutoEllipsis = true;      // 原文只占一行，超出用省略号
            _lblSource.UseMnemonic = false;
            _lblSource.ForeColor = Color.FromArgb(150, 150, 158);
            _lblSource.BackColor = Color.Transparent;
            _lblSource.Text = "";

            _lblTarget = new Label();
            _lblTarget.AutoSize = false;
            _lblTarget.UseMnemonic = false;
            _lblTarget.ForeColor = Color.FromArgb(240, 240, 245);
            _lblTarget.BackColor = Color.Transparent;
            _lblTarget.Text = "";

            Controls.Add(_lblTarget);
            Controls.Add(_lblSource);

            _autoHide = new Timer();
            _autoHide.Interval = 1000;
            _autoHide.Tick += OnAutoHideTick;

            ApplyFonts();
        }

        private void ApplyFonts()
        {
            var s = Settings.Current;
            float size = Math.Max(9, s.OverlayFontSize);
            _lblSource.Font = new Font("Microsoft YaHei UI", Math.Max(8f, size - 4f), FontStyle.Regular, GraphicsUnit.Point);
            _lblTarget.Font = new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Point);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void ShowLoading(string source, int x, int y)
        {
            if (IsDisposed) return;
            _pinned = false;
            ApplyFonts();
            _lblSource.Text = SourceText(source, 400);
            _lblTarget.Text = "翻译中…";
            _lblTarget.ForeColor = Color.FromArgb(160, 160, 170);
            LayoutContent();
            PlaceAt(x, y);
            ShowNoActivate();
            _autoHide.Stop();
        }

        /// <summary>流式输出：只更新译文，不改变位置（除非还没显示过）。</summary>
        public void ShowPartial(string source, string partial, int x, int y)
        {
            if (IsDisposed) return;
            ApplyFonts();
            if (!Visible)
            {
                _lblSource.Text = SourceText(source, 400);
                PlaceAt(x, y);
            }
            _lblTarget.ForeColor = Color.FromArgb(240, 240, 245);
            _lblTarget.Text = partial ?? "";
            LayoutContent();
            if (!Visible) ShowNoActivate();
        }

        public void ShowResult(string source, string translated, int x, int y)
        {
            if (IsDisposed) return;
            ApplyFonts();
            _lblSource.Text = SourceText(source, 400);
            _lblTarget.Text = translated;
            _lblTarget.ForeColor = Color.FromArgb(240, 240, 245);
            LayoutContent();
            PlaceAt(x, y);
            ShowNoActivate();

            var s = Settings.Current;
            if (s.AutoHide && !_pinned)
            {
                _autoHide.Interval = Math.Max(3, s.AutoHideSeconds) * 1000;
                _autoHide.Start();
            }
        }

        public void ShowError(string source, string message, int x, int y)
        {
            if (IsDisposed) return;
            ApplyFonts();
            _lblSource.Text = SourceText(source, 200);
            _lblTarget.Text = message;
            _lblTarget.ForeColor = Color.FromArgb(255, 170, 120);
            LayoutContent();
            PlaceAt(x, y);
            ShowNoActivate();
            _autoHide.Stop();
        }

        private static string SourceText(string s, int max)
        {
            if (!Settings.Current.ShowSourceText) return "";
            return Trim(s, max);
        }

        private void ShowNoActivate()
        {
            if (!Visible) Show(); else Invalidate();
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r\n", " ").Replace("\n", " ").Trim();
            if (s.Length > max) return s.Substring(0, max) + "…";
            return s;
        }

        private void LayoutContent()
        {
            var s = Settings.Current;
            int padL = Padding.Left, padR = Padding.Right, padT = Padding.Top, padB = Padding.Bottom;
            int maxTextWidth = Math.Max(200, s.OverlayMaxWidth - padL - padR);
            bool showSrc = s.ShowSourceText && _lblSource.Text.Length > 0;

            // 原文固定一行：高度按单行算，宽度按实际文字量（超出由 AutoEllipsis 截断）
            int srcLineHeight = 0, srcWidth = 0;
            if (showSrc)
            {
                Size srcLine = TextRenderer.MeasureText("Ag", _lblSource.Font,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine);
                srcLineHeight = Math.Max(16, srcLine.Height);

                Size srcMeasured = TextRenderer.MeasureText(_lblSource.Text, _lblSource.Font,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                srcWidth = Math.Min(srcMeasured.Width, maxTextWidth);
            }

            Size dstSize = TextRenderer.MeasureText(_lblTarget.Text, _lblTarget.Font,
                new Size(maxTextWidth, int.MaxValue), TextFormatFlags.WordBreak);

            int textWidth = Math.Min(Math.Max(srcWidth, dstSize.Width), maxTextWidth);
            textWidth = Math.Max(120, textWidth);
            int width = textWidth + padL + padR;
            int srcBlock = showSrc ? srcLineHeight + 6 : 0;
            int height = padT + srcBlock + dstSize.Height + padB;

            ClientSize = new Size(width, Math.Max(50, height));

            _lblSource.Bounds = showSrc ? new Rectangle(padL, padT, textWidth, srcLineHeight) : Rectangle.Empty;
            _lblTarget.Bounds = new Rectangle(padL, padT + srcBlock, textWidth, dstSize.Height);
        }

        private void PlaceAt(int x, int y)
        {
            var screen = Screen.FromPoint(new Point(x, y));
            var wa = screen.WorkingArea;
            int margin = 14;

            int left = x + margin;
            int top = y + 20;

            if (left + Width > wa.Right) left = x - Width - margin;
            if (top + Height > wa.Bottom) top = y - Height - 12;
            if (left < wa.Left) left = wa.Left + 4;
            if (top < wa.Top) top = wa.Top + 4;

            Location = new Point(left, top);
        }

        private void OnAutoHideTick(object sender, EventArgs e)
        {
            _autoHide.Stop();
            if (!_pinned && !IsDisposed) Hide();
        }

        public void CloseOverlay()
        {
            _autoHide.Stop();
            if (!IsDisposed) Hide();
        }

        public bool IsShowing { get { return Visible; } }

        public void TogglePin()
        {
            _pinned = !_pinned;
            if (_pinned) _autoHide.Stop();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                TogglePin();
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(70, 70, 80), 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}