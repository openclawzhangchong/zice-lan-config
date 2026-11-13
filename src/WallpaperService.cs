using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZiceLan
{
    public class ScreenSize
    {
        public int Width;
        public int Height;
        public string Source = string.Empty;
    }

    public class WallpaperReport
    {
        public bool Generated;
        public bool Applied;
        public string FilePath = string.Empty;
        public string Detail = string.Empty;
    }

    public static class WallpaperService
    {
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        public static ScreenSize ResolveScreen()
        {
            var size = new ScreenSize();
            try
            {
                var bounds = Screen.PrimaryScreen.Bounds;
                size.Width = bounds.Width;
                size.Height = bounds.Height;
                size.Source = "winforms";
            }
            catch (Exception)
            {
            }
            if (size.Width < 160 || size.Height < 120)
            {
                int cx = 0;
                int cy = 0;
                try
                {
                    cx = GetSystemMetrics(SM_CXSCREEN);
                    cy = GetSystemMetrics(SM_CYSCREEN);
                }
                catch (Exception)
                {
                }
                if (cx >= 160 && cy >= 120)
                {
                    size.Width = cx;
                    size.Height = cy;
                    size.Source = "gdi";
                }
                else
                {
                    size.Source = "none";
                }
            }
            return size;
        }

        public static int PrimaryScreenWidth
        {
            get { return ResolveScreen().Width; }
        }

        public static int PrimaryScreenHeight
        {
            get { return ResolveScreen().Height; }
        }

        public static WallpaperReport Apply(string ip, string backgroundName, string textName,
                                           int width, int height, bool setToDesktop, Action<string> log)
        {
            var report = new WallpaperReport();
            ColorPreset background = WallpaperLayout.BackgroundOrDefault(backgroundName);
            ColorPreset foreground = WallpaperLayout.TextOrDefault(textName);

            string pathError;
            string directory = AppPaths.EnsureWallpaperDir(out pathError);
            RenderResult render = WallpaperRenderer.RenderToFile(ip, background, foreground, width, height, directory);
            if (!render.Ok)
            {
                report.Detail = "壁纸生成失败：" + render.Error;
                if (log != null)
                {
                    log(report.Detail);
                }
                return report;
            }
            report.Generated = true;
            report.FilePath = render.FilePath;
            if (log != null)
            {
                log("壁纸已生成：" + render.FilePath + "（" + width + "x" + height + "，"
                    + background.Name + "底 " + foreground.Name + "字，字号 " + render.Meta.FontSize
                    + "px，边距 " + WallpaperLayout.MarginFor(width) + "px，字体 " + render.Meta.FontFamily
                    + (render.Error.Length > 0 ? "，" + render.Error : "") + "）");
            }

            if (!setToDesktop)
            {
                report.Detail = "只生成，未设置桌面";
                return report;
            }

            string setError;
            if (!WallpaperSetter.Set(render.FilePath, out setError))
            {
                report.Detail = "壁纸文件已生成在 " + render.FilePath + "，但设置桌面失败：" + setError;
                if (log != null)
                {
                    log(report.Detail);
                }
                return report;
            }
            string reported;
            bool matched = WallpaperSetter.Matches(render.FilePath, out reported);
            report.Applied = true;
            report.Detail = matched
                ? "桌面壁纸已切换，系统回报路径与生成文件一致"
                : "桌面壁纸已提交，系统回报路径是 " + reported + "（可能由系统转码存放，不影响显示）";
            if (log != null)
            {
                log(report.Detail);
            }
            return report;
        }
    }
}
