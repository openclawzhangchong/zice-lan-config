using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace ZiceLan
{
    public class RenderMeta
    {
        public int FontSize;
        public string FontFamily = string.Empty;
    }

    public class RenderResult
    {
        public bool Ok;
        public string FilePath = string.Empty;
        public string Format = string.Empty;
        public string Error = string.Empty;
        public int Width;
        public int Height;
        public RenderMeta Meta = new RenderMeta();
    }

    public static class WallpaperRenderer
    {
        private static readonly string[] FontCandidates = new string[]
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Tahoma"
        };

        private static string _pickedFamily;
        private static readonly object FamilyGate = new object();

        public static string PickFontFamily()
        {
            lock (FamilyGate)
            {
                if (_pickedFamily != null)
                {
                    return _pickedFamily;
                }
                try
                {
                    using (var installed = new InstalledFontCollection())
                    {
                        foreach (string candidate in FontCandidates)
                        {
                            foreach (FontFamily family in installed.Families)
                            {
                                if (string.Equals(family.Name, candidate, StringComparison.OrdinalIgnoreCase))
                                {
                                    _pickedFamily = family.Name;
                                    return _pickedFamily;
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                }
                _pickedFamily = FontFamily.GenericSansSerif.Name;
                return _pickedFamily;
            }
        }

        public static Bitmap Render(string ip, ColorPreset background, ColorPreset text, int width, int height, RenderMeta meta)
        {
            if (meta == null)
            {
                meta = new RenderMeta();
            }
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.FromArgb(background.R, background.G, background.B));

                string caption = WallpaperLayout.Caption(ip);
                string family = PickFontFamily();
                int maxWidth = WallpaperLayout.MaxTextWidth(width);
                WallpaperLayout.MeasureWidth measure = delegate(string text2, int size)
                {
                    using (var probe = new Font(family, size, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        return (int)Math.Ceiling(g.MeasureString(text2, probe).Width);
                    }
                };
                int fontSize = WallpaperLayout.FitFontSize(
                    caption, WallpaperLayout.BaseFontSize(width, height), maxWidth, measure);

                using (var font = new Font(family, fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Color.FromArgb(text.R, text.G, text.B)))
                {
                    SizeF measured = g.MeasureString(caption, font);
                    int marginX = WallpaperLayout.MarginFor(width);
                    int marginY = WallpaperLayout.MarginFor(height);
                    float x = width - marginX - measured.Width;
                    if (x < marginX)
                    {
                        x = marginX;
                    }
                    g.DrawString(caption, font, brush, x, marginY);
                }
                meta.FontSize = fontSize;
                meta.FontFamily = family;
            }
            return bitmap;
        }

        public static RenderResult RenderToFile(string ip, ColorPreset background, ColorPreset text,
                                               int width, int height, string directory)
        {
            var result = new RenderResult();
            result.Width = width;
            result.Height = height;
            if (width < 160 || height < 120)
            {
                result.Error = "屏幕分辨率异常（" + width + "x" + height + "），不生成壁纸";
                return result;
            }
            if (string.IsNullOrEmpty(directory))
            {
                result.Error = "没有可写的壁纸目录";
                return result;
            }
            Bitmap bitmap = null;
            try
            {
                bitmap = Render(ip, background, text, width, height, result.Meta);
                string pngPath = Path.Combine(directory, WallpaperLayout.FileNameFor(ip));
                try
                {
                    bitmap.Save(pngPath, ImageFormat.Png);
                    result.FilePath = pngPath;
                    result.Format = "png";
                    result.Ok = true;
                    return result;
                }
                catch (Exception pngError)
                {
                    string bmpPath = Path.Combine(directory, "ip-" + ip + ".bmp");
                    bitmap.Save(bmpPath, ImageFormat.Bmp);
                    result.FilePath = bmpPath;
                    result.Format = "bmp";
                    result.Error = "PNG 保存失败（" + pngError.Message + "），已退回 BMP";
                    result.Ok = true;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
            finally
            {
                if (bitmap != null)
                {
                    bitmap.Dispose();
                }
            }
        }
    }
}
