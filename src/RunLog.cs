using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;

namespace ZiceLan
{
    public static class RunLog
    {
        private static readonly object Gate = new object();
        private static string _path;
        private static string _pathError;

        public static string FilePath
        {
            get
            {
                if (_path == null)
                {
                    ResolvePath();
                }
                return _path;
            }
        }

        public static void Write(string line)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string text = "[" + stamp + "] " + line;
            lock (Gate)
            {
                try
                {
                    string path = FilePath;
                    if (path != null)
                    {
                        File.AppendAllText(path, text + Environment.NewLine, Encoding.UTF8);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private static void ResolvePath()
        {
            string error;
            string root = AppPaths.EnsureDir("logs", out error);
            if (root == null)
            {
                _pathError = error;
                _path = null;
                return;
            }
            string stamp = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            try
            {
                _path = Path.Combine(root, "run-" + stamp + ".log");
                File.AppendAllText(_path, string.Empty, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _pathError = ex.Message;
                _path = null;
            }
        }
    }
}
