using System;
using System.IO;

namespace ZiceLan
{
    public static class AppPaths
    {
        // 壁纸固定存 D 盘：exe 常被放在桌面上，图跟在 exe 旁边就落了桌面。
        public const string WallpaperDriveRoot = "D:";
        public const string WallpaperFolderName = "智策壁纸";

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

        // 壁纸落盘优先级：D:\智策壁纸 → exe 目录\wallpapers → %LOCALAPPDATA%\…\wallpapers。
        // 纯函数，供闸门断言链路形状；真正挑目录时按顺序试建，谁可写用谁。
        // 注意 Path.Combine("D:", x) 会给出盘符相对路径 "D:x"（不带反斜杠），必须自己补分隔符。
        public static string[] WallpaperDirCandidates()
        {
            string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            return new string[]
            {
                WallpaperDriveRoot + Path.DirectorySeparatorChar + WallpaperFolderName,
                Path.Combine(exeDir, "wallpapers"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Product.Title, "wallpapers")
            };
        }

        public static string EnsureWallpaperDir(out string error)
        {
            error = string.Empty;
            foreach (string root in WallpaperDirCandidates())
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
