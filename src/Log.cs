using System;
using System.IO;
using System.Text;

namespace WT
{
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static string Directory
        {
            get
            {
                try
                {
                    var dir = Path.Combine(Settings.RootDir, "logs");
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    return dir;
                }
                catch
                {
                    var fallback = Path.Combine(Path.GetTempPath(), "WordTranslator-logs");
                    if (!System.IO.Directory.Exists(fallback)) System.IO.Directory.CreateDirectory(fallback);
                    return fallback;
                }
            }
        }

        public static string FilePath
        {
            get
            {
                if (_path == null) _path = Path.Combine(Directory, "app.log");
                return _path;
            }
        }

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    var f = FilePath;
                    var info = new FileInfo(f);
                    if (info.Exists && info.Length > 2 * 1024 * 1024)
                    {
                        var bak = f + ".1";
                        if (File.Exists(bak)) File.Delete(bak);
                        File.Move(f, bak);
                    }
                    var line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}\r\n", DateTime.Now, message);
                    File.AppendAllText(f, line, Encoding.UTF8);
                }
            }
            catch
            {
                // logging must never throw
            }
        }

        public static void Info(string message) { Write("INFO  " + message); }
        public static void Warn(string message) { Write("WARN  " + message); }
        public static void Error(string message) { Write("ERROR " + message); }

        public static void Exception(string context, Exception ex)
        {
            Write("ERROR " + context + " :: " + (ex == null ? "(null)" : ex.ToString()));
        }
    }
}