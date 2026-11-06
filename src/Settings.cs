using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WT
{
    public class Settings
    {
        // 触发
        public bool RequireAlt = true;
        public int DragThreshold = 6;

        // 引擎
        public int MaxRoundsBeforeReset = 20;
        public int MinIntervalMs = 250;
        public int FirstTokenTimeoutSec = 15;
        public int TranslateTimeoutSec = 60;
        public bool ShowEngineWindow = false;
        public string PromptPrefix = "帮我直接翻译，直接给出中文译文，不要解释、不要重复原文：";

        // 浮层
        public int OverlayFontSize = 15;
        public int OverlayMaxWidth = 560;
        public bool AutoHide = false;
        public int AutoHideSeconds = 20;
        public bool ShowSourceText = true;

        // 调试
        public bool DebugLog = true;

        private static string _rootDir;
        public static string DataDirError = "";

        public static string RootDir
        {
            get
            {
                if (_rootDir != null) return _rootDir;

                var candidates = new List<string>();
                var custom = Environment.GetEnvironmentVariable("WT_DATA_DIR");
                if (!string.IsNullOrEmpty(custom)) candidates.Add(custom);
                // 优先放在 exe 旁边（绿色版，方便排查，也不受用户目录权限影响）
                candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "哦鲸鲸翻译Data"));
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "哦鲸鲸翻译"));
                candidates.Add(Path.Combine(Path.GetTempPath(), "哦鲸鲸翻译"));

                var errors = new StringBuilder();
                foreach (var dir in candidates)
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                        var probe = Path.Combine(dir, ".write-test");
                        File.WriteAllText(probe, "ok");
                        File.Delete(probe);
                        _rootDir = dir;
                        return dir;
                    }
                    catch (Exception ex)
                    {
                        errors.AppendLine("  " + dir + " => " + ex.Message);
                    }
                }

                DataDirError = errors.ToString();
                throw new IOException("所有候选数据目录都不可写：" + Environment.NewLine + DataDirError);
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(RootDir, "settings.json"); }
        }

        public static string WebViewDir
        {
            get
            {
                var dir = Path.Combine(RootDir, "wv2");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static Settings _current;

        public static Settings Current
        {
            get
            {
                if (_current == null) _current = Load();
                return _current;
            }
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    var ser = new JavaScriptSerializer();
                    var s = ser.Deserialize<Settings>(json);
                    if (s != null) return s;
                }
            }
            catch (Exception ex)
            {
                Log.Exception("加载设置失败，使用默认值", ex);
            }
            var fresh = new Settings();
            try { fresh.Save(); } catch { }
            return fresh;
        }

        public void Save()
        {
            try
            {
                var ser = new JavaScriptSerializer();
                var json = ser.Serialize(this);
                File.WriteAllText(SettingsPath, json, new UTF8Encoding(false));
                Log.Info("设置已保存");
            }
            catch (Exception ex)
            {
                Log.Exception("保存设置失败", ex);
            }
        }
    }
}