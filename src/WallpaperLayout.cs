using System;
using System.Collections.Generic;

namespace ZiceLan
{
    public class ColorPreset
    {
        public string Name;
        public int R;
        public int G;
        public int B;

        public ColorPreset(string name, int r, int g, int b)
        {
            Name = name;
            R = r;
            G = g;
            B = b;
        }

        public string Hex
        {
            get { return string.Format("#{0:X2}{1:X2}{2:X2}", R, G, B); }
        }
    }

    public static class WallpaperLayout
    {
        public const string CaptionPrefix = "当前 IP：";
        public const int ReferenceWidth = 1920;
        public const double MinScale = 0.5;
        public const double MaxScale = 3.0;
        public const int BaseFontSizeAtReference = 48;
        public const int MinFontSize = 20;
        public const int MaxFontSize = 160;
        public const int MinMarginPixels = 16;
        public const double MarginRatio = 0.03;

        public static string Caption(string ip)
        {
            return CaptionPrefix + ip;
        }

        public static double ScaleFor(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return 1.0;
            }
            double scale = (double)width / ReferenceWidth;
            if (scale < MinScale)
            {
                scale = MinScale;
            }
            if (scale > MaxScale)
            {
                scale = MaxScale;
            }
            return scale;
        }

        public static int BaseFontSize(int width, int height)
        {
            int size = (int)Math.Round(BaseFontSizeAtReference * ScaleFor(width, height), MidpointRounding.AwayFromZero);
            if (size < MinFontSize)
            {
                size = MinFontSize;
            }
            if (size > MaxFontSize)
            {
                size = MaxFontSize;
            }
            return size;
        }

        public static int MarginFor(int pixels)
        {
            int margin = (int)Math.Round(pixels * MarginRatio, MidpointRounding.AwayFromZero);
            return margin < MinMarginPixels ? MinMarginPixels : margin;
        }

        public static int MaxTextWidth(int width)
        {
            int usable = width - MarginFor(width) * 2;
            return usable < 1 ? 1 : usable;
        }

        public delegate int MeasureWidth(string text, int fontSize);

        public static int FitFontSize(string text, int startSize, int maxWidth, MeasureWidth measure)
        {
            if (measure == null)
            {
                return startSize;
            }
            int size = startSize;
            while (size > MinFontSize && measure(text, size) > maxWidth)
            {
                size = (int)Math.Round(size * 0.9, MidpointRounding.AwayFromZero);
                if (size < MinFontSize)
                {
                    size = MinFontSize;
                }
            }
            return size;
        }

        public static List<ColorPreset> Colors()
        {
            var list = new List<ColorPreset>();
            list.Add(new ColorPreset("黑", 0, 0, 0));
            list.Add(new ColorPreset("白", 255, 255, 255));
            list.Add(new ColorPreset("灰", 128, 128, 128));
            list.Add(new ColorPreset("深蓝", 11, 37, 69));
            list.Add(new ColorPreset("草绿", 46, 125, 50));
            list.Add(new ColorPreset("亮黄", 255, 214, 0));
            list.Add(new ColorPreset("红", 198, 40, 40));
            list.Add(new ColorPreset("青", 0, 172, 193));
            return list;
        }

        public static List<string> ColorNames()
        {
            var names = new List<string>();
            foreach (ColorPreset color in Colors())
            {
                names.Add(color.Name);
            }
            return names;
        }

        public static ColorPreset FindColor(string name)
        {
            foreach (ColorPreset color in Colors())
            {
                if (string.Equals(color.Name, name, StringComparison.Ordinal))
                {
                    return color;
                }
            }
            return null;
        }

        public static ColorPreset BackgroundOrDefault(string name)
        {
            ColorPreset found = FindColor(name);
            return found != null ? found : FindColor("黑");
        }

        public static ColorPreset TextOrDefault(string name)
        {
            ColorPreset found = FindColor(name);
            return found != null ? found : FindColor("白");
        }

        public static string FileNameFor(string ip)
        {
            string safe = (ip ?? string.Empty).Replace(':', '_').Replace('/', '_').Replace('\\', '_');
            if (safe.Length == 0)
            {
                safe = "unknown";
            }
            return "ip-" + safe + ".png";
        }
    }
}
