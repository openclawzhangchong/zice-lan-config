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
            string name = Product.Title;
            string stamp = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string[] roots = new string[]
            {
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "logs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name, "logs")
            };
            foreach (string root in roots)
            {
                try
                {
                    Directory.CreateDirectory(root);
                    string candidate = Path.Combine(root, "run-" + stamp + ".log");
                    File.AppendAllText(candidate, string.Empty, Encoding.UTF8);
                    _path = candidate;
                    return;
                }
                catch (Exception ex)
                {
                    _pathError = ex.Message;
                }
            }
            _path = null;
        }
    }
}
