using System;
using System.IO;

namespace ZiceLan
{
    public static class AppPaths
    {
        public static string EnsureDir(string subName, out string error)
        {
            error = string.Empty;
            string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string[] roots = new string[]
            {
                Path.Combine(exeDir, subName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.Title, subName)
            };
            foreach (string root in roots)
            {
                try
                {
                    Directory.CreateDirectory(root);
                    return root;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }
            return null;
        }
    }
}
