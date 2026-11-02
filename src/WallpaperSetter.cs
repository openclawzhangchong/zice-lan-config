using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ZiceLan
{
    public static class WallpaperSetter
    {
        private const uint SPI_GETDESKWALLPAPER = 0x0073;
        private const uint SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPIF_UPDATEINIFILE = 0x0001;
        private const uint SPIF_SENDCHANGE = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfoGet(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);

        public static string Current()
        {
            var buffer = new StringBuilder(600);
            try
            {
                bool ok = SystemParametersInfoGet(SPI_GETDESKWALLPAPER, (uint)buffer.Capacity, buffer, 0);
                return ok ? buffer.ToString() : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public static bool Set(string path, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(path))
            {
                error = "没有壁纸文件路径";
                return false;
            }
            if (!File.Exists(path))
            {
                error = "壁纸文件不存在：" + path;
                return false;
            }
            try
            {
                bool ok = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                if (!ok)
                {
                    error = "SystemParametersInfo 返回 false（Win32 错误码 " + Marshal.GetLastWin32Error() + "）";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Matches(string path, out string reported)
        {
            reported = Current();
            if (reported.Length == 0 || string.IsNullOrEmpty(path))
            {
                return false;
            }
            return string.Equals(Normalize(reported), Normalize(path), StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}
