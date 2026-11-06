using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WT
{
    internal class SettingsForm : Form
    {
        private readonly NumericUpDown _numRounds;
        private readonly NumericUpDown _numFont;
        private readonly NumericUpDown _numHideSec;
        private readonly NumericUpDown _numInterval;
        private readonly CheckBox _chkShowEngine;
        private readonly CheckBox _chkAutoHide;
        private readonly CheckBox _chkShowSource;
        private readonly TextBox _txtPrompt;

        public SettingsForm()
        {
            Text = "哦鲸鲸翻译 - 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 460);
            Font = new Font("Microsoft YaHei UI", 9f);

            var s = Settings.Current;

            int y = 16;
            AddLabel("翻译轮数重置（每 N 轮自动开新对话）", 16, y);
            _numRounds = AddNumber(s.MaxRoundsBeforeReset, 1, 200, 330, y - 3);
            y += 40;

            AddLabel("两次请求最小间隔（毫秒，防限流）", 16, y);
            _numInterval = AddNumber(s.MinIntervalMs, 100, 5000, 330, y - 3);
            y += 40;

            AddLabel("译文字号", 16, y);
            _numFont = AddNumber(s.OverlayFontSize, 8, 40, 330, y - 3);
            y += 40;

            AddLabel("自动隐藏（秒）", 16, y);
            _chkAutoHide = new CheckBox();
            _chkAutoHide.Text = "启用";
            _chkAutoHide.Checked = s.AutoHide;
            _chkAutoHide.Location = new Point(330, y);
            _chkAutoHide.AutoSize = true;
            Controls.Add(_chkAutoHide);
            _numHideSec = AddNumber(s.AutoHideSeconds, 3, 600, 400, y - 3);
            y += 40;

            _chkShowSource = new CheckBox();
            _chkShowSource.Text = "在浮层里显示原文（只占一行，超出用省略号）";
            _chkShowSource.Checked = s.ShowSourceText;
            _chkShowSource.Location = new Point(16, y);
            _chkShowSource.AutoSize = true;
            Controls.Add(_chkShowSource);
            y += 32;

            _chkShowEngine = new CheckBox();
            _chkShowEngine.Text = "显示 DeepSeek 窗口（调试用，登录时也会自动弹出）";
            _chkShowEngine.Checked = s.ShowEngineWindow;
            _chkShowEngine.Location = new Point(16, y);
            _chkShowEngine.AutoSize = true;
            Controls.Add(_chkShowEngine);
            y += 32;

            AddLabel("发送给网页版的前缀（换行后接原文）", 16, y);
            y += 24;
            _txtPrompt = new TextBox();
            _txtPrompt.Multiline = true;
            _txtPrompt.ScrollBars = ScrollBars.Vertical;
            _txtPrompt.Location = new Point(16, y);
            _txtPrompt.Size = new Size(488, 56);
            _txtPrompt.Text = s.PromptPrefix;
            Controls.Add(_txtPrompt);
            y += 70;

            var btnSave = new Button();
            btnSave.Text = "保存";
            btnSave.Location = new Point(330, y);
            btnSave.Size = new Size(80, 30);
            btnSave.Click += OnSave;
            Controls.Add(btnSave);

            var btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Location = new Point(420, y);
            btnCancel.Size = new Size(80, 30);
            btnCancel.Click += delegate { Close(); };
            Controls.Add(btnCancel);

            var btnLogs = new Button();
            btnLogs.Text = "打开日志目录";
            btnLogs.Location = new Point(16, y);
            btnLogs.Size = new Size(110, 30);
            btnLogs.Click += delegate { OpenFolder(Log.Directory); };
            Controls.Add(btnLogs);

            var btnData = new Button();
            btnData.Text = "数据目录";
            btnData.Location = new Point(134, y);
            btnData.Size = new Size(90, 30);
            btnData.Click += delegate { OpenFolder(Settings.RootDir); };
            Controls.Add(btnData);
        }

        private void AddLabel(string text, int x, int y)
        {
            var lbl = new Label();
            lbl.Text = text;
            lbl.Location = new Point(x, y);
            lbl.AutoSize = true;
            Controls.Add(lbl);
        }

        private NumericUpDown AddNumber(int value, int min, int max, int x, int y)
        {
            var n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Value = Math.Min(max, Math.Max(min, value));
            n.Location = new Point(x, y);
            n.Size = new Size(80, 24);
            Controls.Add(n);
            return n;
        }

        private void OnSave(object sender, EventArgs e)
        {
            var s = Settings.Current;
            s.MaxRoundsBeforeReset = (int)_numRounds.Value;
            s.MinIntervalMs = (int)_numInterval.Value;
            s.OverlayFontSize = (int)_numFont.Value;
            s.AutoHide = _chkAutoHide.Checked;
            s.AutoHideSeconds = (int)_numHideSec.Value;
            s.ShowEngineWindow = _chkShowEngine.Checked;
            s.ShowSourceText = _chkShowSource.Checked;
            string p = _txtPrompt.Text;
            if (!string.IsNullOrEmpty(p)) s.PromptPrefix = p.TrimEnd();
            s.Save();
            Close();
        }

        private static void OpenFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开目录失败：" + ex.Message);
            }
        }
    }
}