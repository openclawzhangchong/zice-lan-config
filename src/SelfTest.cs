using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;

namespace ZiceLan
{
    public class Check
    {
        public string Key;
        public bool Ok;
        public string Detail;

        public Check(string key, bool ok, string detail)
        {
            Key = key;
            Ok = ok;
            Detail = detail;
        }
    }

    public static class SelfTest
    {
        private const string AdapterName = "以太网";

        // Fixture MACs are synthetic on purpose: the parser only cares about the
        // column shape, and this file ships to a public repository.
        private static readonly string ArpFixture = string.Join("\n", new string[]
        {
            "接口: 192.168.168.200 --- 0x7",
            "  Internet 地址      物理地址            类型",
            "  192.168.168.1      aa-bb-cc-00-00-01   动态",
            "  192.168.168.7      aa-bb-cc-00-00-02   动态",
            "  192.168.168.200    aa-bb-cc-00-00-03   静态",
            "  192.168.168.255    ff-ff-ff-ff-ff-ff   静态",
            "  192.168.1.1        aa-bb-cc-00-00-04   动态",
            "接口: 192.168.137.1 --- 0x1d",
            "  192.168.137.2      aa-bb-cc-dd-ee-01   动态"
        });

        public static List<Check> Run()
        {
            var checks = new List<Check>();

            Add(checks, "order_network_byte_value",
                IpCore.ToNetworkOrder("192.168.168.21") == 363374784u,
                "expected=363374784 got=" + IpCore.ToNetworkOrder("192.168.168.21"));

            Add(checks, "order_roundtrip",
                IpCore.FromNetworkOrder(363374784u) == "192.168.168.21",
                "expected=192.168.168.21 got=" + IpCore.FromNetworkOrder(363374784u));

            Add(checks, "base_prefix_of_own_ip",
                IpCore.BasePrefix("192.168.168.200") == "192.168.168",
                "got=" + IpCore.BasePrefix("192.168.168.200"));

            Add(checks, "combine_octet",
                IpCore.Combine("192.168.168", 21) == "192.168.168.21",
                "got=" + IpCore.Combine("192.168.168", 21));

            bool ipv4Gate = IpCore.IsValidIpv4("192.168.168.1")
                && !IpCore.IsValidIpv4("192.168.168.999")
                && !IpCore.IsValidIpv4("192.168.168")
                && !IpCore.IsValidIpv4("192.168.168.-1")
                && !IpCore.IsValidIpv4("192.168.168. 1");
            Add(checks, "ipv4_validation", ipv4Gate, "rejects 999 / short / sign / space");

            List<int> withReserve = IpCore.BuildCandidates(1, 254, 1, 20);
            Add(checks, "candidates_reserve20_count", withReserve.Count == 234,
                "expected=234 got=" + withReserve.Count);
            Add(checks, "candidates_reserve20_bounds",
                withReserve.Count > 0 && withReserve[0] == 21 && withReserve[withReserve.Count - 1] == 254,
                "first=" + withReserve[0] + " last=" + withReserve[withReserve.Count - 1]);
            bool excludesReserved = !withReserve.Contains(1) && !withReserve.Contains(20) && withReserve.Contains(21);
            Add(checks, "candidates_excludes_reserved_octets", excludesReserved, "1 and 20 must be absent, 21 present");

            List<int> noReserve = IpCore.BuildCandidates(1, 254, 0, 0);
            Add(checks, "candidates_reserve_off", noReserve.Count == 254 && noReserve[0] == 1,
                "expected=254 got=" + noReserve.Count);
            List<int> clamped = IpCore.BuildCandidates(-5, 300, 0, 0);
            Add(checks, "candidates_clamped_to_1_254", clamped.Count == 254 && !clamped.Contains(0) && !clamped.Contains(255),
                "expected=254 got=" + clamped.Count);

            HashSet<string> table = ArpProbe.ParseTable(ArpFixture, "192.168.168", "192.168.168.200");
            bool tableOk = table.Count == 2
                && table.Contains("192.168.168.1")
                && table.Contains("192.168.168.7")
                && !table.Contains("192.168.168.200")
                && !table.Contains("192.168.168.255");
            Add(checks, "arp_table_parse", tableOk,
                "expected 2 rows, own ip + broadcast + other prefix dropped; got=" + table.Count);

            Add(checks, "classify_ping_success_is_used",
                ArpProbe.Classify(IPStatus.Success, false) == HostState.Used, "Success must be Used");
            Add(checks, "classify_silent_host_is_free",
                ArpProbe.Classify(IPStatus.TimedOut, false) == HostState.Free, "TimedOut + no arp = Free");
            Add(checks, "classify_host_unreachable_is_free",
                ArpProbe.Classify(IPStatus.DestinationHostUnreachable, false) == HostState.Free,
                "local-subnet ARP failure surfaces as DestinationHostUnreachable");
            Add(checks, "classify_local_failure_is_unknown",
                ArpProbe.Classify(IPStatus.BadRoute, false) == HostState.Unknown, "BadRoute must be Unknown, never Free");
            Add(checks, "classify_local_failure_rescued_by_arp",
                ArpProbe.Classify(IPStatus.BadRoute, true) == HostState.Used,
                "ICMP blocked locally must not hide a host that answers ARP");
            Add(checks, "classify_arp_reply_beats_timeout",
                ArpProbe.Classify(IPStatus.TimedOut, true) == HostState.Used, "firewalled host found by ARP");
            Add(checks, "classify_arp_reply_beats_unreachable",
                ArpProbe.Classify(IPStatus.DestinationHostUnreachable, true) == HostState.Used,
                "an ARP answer always outranks a ping failure");

            Add(checks, "netsh_set_address_exact",
                NetshArgs.SetAddress(AdapterName, "192.168.168.21", "255.255.0.0", "192.168.168.1")
                    == "interface ip set address name=\"" + AdapterName + "\" static 192.168.168.21 255.255.0.0 192.168.168.1",
                "got=" + NetshArgs.SetAddress(AdapterName, "192.168.168.21", "255.255.0.0", "192.168.168.1"));

            Add(checks, "netsh_set_address_without_gateway",
                NetshArgs.SetAddress(AdapterName, "192.168.168.21", "255.255.0.0", "")
                    == "interface ip set address name=\"" + AdapterName + "\" static 192.168.168.21 255.255.0.0",
                "gateway left empty must not append a trailing space");

            Add(checks, "netsh_name_quote_escaped",
                NetshArgs.QuoteName("a\"b").Contains("\\\""),
                "got=" + NetshArgs.QuoteName("a\"b"));

            Add(checks, "netsh_dhcp_pair",
                NetshArgs.DhcpAddress(AdapterName) == "interface ip set address name=\"" + AdapterName + "\" dhcp"
                    && NetshArgs.DhcpDns(AdapterName) == "interface ip set dns name=\"" + AdapterName + "\" dhcp",
                "dhcp rollback commands");

            List<string> script = NetshArgs.BuildApplyScript(AdapterName, "192.168.168.21", "255.255.0.0", "192.168.168.1",
                new List<string>(new string[] { "114.114.114.114", "223.5.5.5" }));
            bool scriptOk = script.Count == 3
                && script[0] == NetshArgs.SetAddress(AdapterName, "192.168.168.21", "255.255.0.0", "192.168.168.1")
                && script[1] == "interface ip set dns name=\"" + AdapterName + "\" static 114.114.114.114"
                && script[2] == "interface ip add dns name=\"" + AdapterName + "\" 223.5.5.5 index=2";
            Add(checks, "netsh_apply_script_dns_order", scriptOk,
                "count=" + script.Count + " third=" + (script.Count > 2 ? script[2] : "-"));

            List<string> noDns = NetshArgs.BuildApplyScript(AdapterName, "192.168.168.21", "255.255.0.0", "192.168.168.1",
                new List<string>());
            Add(checks, "netsh_apply_script_without_dns", noDns.Count == 1, "count=" + noDns.Count);

            var pool = new List<int>();
            for (int octet = 21; octet <= 30; octet++)
            {
                pool.Add(octet);
            }
            var rng = new Random(20261102);
            var seen = new HashSet<int>();
            bool insidePool = true;
            for (int i = 0; i < 50; i++)
            {
                int pick = Picker.PickRandom(pool, -1, rng);
                if (!pool.Contains(pick))
                {
                    insidePool = false;
                }
                seen.Add(pick);
            }
            Add(checks, "picker_only_inside_free_pool", insidePool, "50 draws must all belong to the pool");
            Add(checks, "picker_not_always_first", seen.Count >= 2, "distinct=" + seen.Count);
            bool avoids = true;
            for (int i = 0; i < 50; i++)
            {
                if (Picker.PickRandom(pool, 22, rng) == 22)
                {
                    avoids = false;
                }
            }
            Add(checks, "picker_avoids_current_ip", avoids, "must never re-pick octet 22");
            Add(checks, "picker_empty_pool_returns_minus_one",
                Picker.PickRandom(new List<int>(), -1, rng) == -1, "empty pool");
            Add(checks, "picker_single_option_pool_avoids",
                Picker.PickRandom(pool, -1, rng) >= 21 && Picker.PickRandom(new List<int>(new int[] { 22 }), 22, rng) == -1,
                "only option equal to avoided octet must yield -1");

            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(exePath);
            Add(checks, "file_version_stamped", version.FileVersion == Product.FileVersion,
                "expected=" + Product.FileVersion + " got=" + version.FileVersion + " exe=" + Path.GetFileName(exePath));
            Add(checks, "version_text_is_v1_4", Product.VersionText == "v1.4", "got=" + Product.VersionText);
            Add(checks, "title_is_zice", Product.Title == "智策学校局域网配置软件", "got=" + Product.Title);

            List<SubnetPreset> subnets = SubnetPresets.All();
            Add(checks, "subnet_presets_count", subnets.Count == 2, "expected=2 got=" + subnets.Count);
            Add(checks, "subnet_preset_200_fallback_gateway",
                SubnetPresets.FallbackGatewayFor("192.168.200") == "192.168.200.1",
                "got=" + SubnetPresets.FallbackGatewayFor("192.168.200"));
            Add(checks, "subnet_custom_prefix_derives_gateway",
                SubnetPresets.FallbackGatewayFor("10.20.30") == "10.20.30.1",
                "got=" + SubnetPresets.FallbackGatewayFor("10.20.30"));

            List<DnsPreset> dns = DnsPresets.All();
            Add(checks, "dns_presets_count", dns.Count == 7, "expected=7 got=" + dns.Count);
            DnsPreset ali = DnsPresets.Find("阿里 AliDNS");
            Add(checks, "dns_preset_ali_values",
                ali != null && DnsPresets.TextFor(ali) == "223.5.5.5,223.6.6.6",
                ali == null ? "ali preset missing" : "got=" + DnsPresets.TextFor(ali));
            DnsPreset baidu = DnsPresets.Find("百度 DNS");
            Add(checks, "dns_preset_baidu_single_server",
                baidu != null && DnsPresets.ServersOf(baidu).Count == 1 && DnsPresets.TextFor(baidu) == "180.76.76.76",
                baidu == null ? "baidu preset missing" : "got=" + DnsPresets.TextFor(baidu));
            Add(checks, "dns_preset_unknown_name_is_null", DnsPresets.Find("不存在") == null, "must not silently fall back");
            Add(checks, "dns_text_maps_back_to_preset_name",
                DnsPresets.NameForServers("223.5.5.5,223.6.6.6") == "阿里 AliDNS"
                    && DnsPresets.NameForServers("8.8.8.8") == DnsPresets.CustomLabel,
                "got=" + DnsPresets.NameForServers("223.5.5.5,223.6.6.6") + " / " + DnsPresets.NameForServers("8.8.8.8"));
            bool everyPresetValid = true;
            foreach (DnsPreset preset in dns)
            {
                if (!IpCore.IsValidIpv4(preset.Primary) || DnsPresets.ServersOf(preset).Count < 1)
                {
                    everyPresetValid = false;
                }
                if (preset.Secondary.Length > 0 && !IpCore.IsValidIpv4(preset.Secondary))
                {
                    everyPresetValid = false;
                }
            }
            Add(checks, "dns_presets_all_valid_ipv4", everyPresetValid, "every preset must be real addresses");

            Add(checks, "browser_url_normalizes_gateway",
                Browser.NormalizeUrl("192.168.168.1") == "http://192.168.168.1"
                    && Browser.NormalizeUrl("192.168.168.999").Length == 0,
                "got=" + Browser.NormalizeUrl("192.168.168.1") + " / " + Browser.NormalizeUrl("192.168.168.999"));

            Add(checks, "wallpaper_caption_exact",
                WallpaperLayout.Caption("192.168.168.201") == "当前 IP：192.168.168.201",
                "got=" + WallpaperLayout.Caption("192.168.168.201"));
            Add(checks, "wallpaper_filename_per_ip",
                WallpaperLayout.FileNameFor("192.168.168.201") == "ip-192.168.168.201.png"
                    && WallpaperLayout.FileNameFor("") == "ip-unknown.png",
                "got=" + WallpaperLayout.FileNameFor("192.168.168.201"));

            List<ColorPreset> colors = WallpaperLayout.Colors();
            Add(checks, "wallpaper_color_presets", colors.Count == 8
                    && WallpaperLayout.FindColor("黑").R == 0 && WallpaperLayout.FindColor("黑").G == 0 && WallpaperLayout.FindColor("黑").B == 0
                    && WallpaperLayout.FindColor("白").R == 255 && WallpaperLayout.FindColor("白").B == 255
                    && WallpaperLayout.FindColor("亮黄").Hex == "#FFD600"
                    && WallpaperLayout.FindColor("不存在") == null,
                "count=" + colors.Count + " 亮黄=" + WallpaperLayout.FindColor("亮黄").Hex);
            Add(checks, "wallpaper_default_is_black_white",
                WallpaperLayout.BackgroundOrDefault("乱写的颜色").Name == "黑"
                    && WallpaperLayout.TextOrDefault(null).Name == "白",
                "unknown names must fall back to black background / white text");

            Add(checks, "wallpaper_font_scale_at_reference",
                WallpaperLayout.ScaleFor(1920, 1080) == 1.0 && WallpaperLayout.BaseFontSize(1920, 1080) == 48,
                "1920 must be scale 1.0 / font 48px");
            Add(checks, "wallpaper_font_scale_across_resolutions",
                WallpaperLayout.BaseFontSize(3840, 2160) == 96
                    && WallpaperLayout.BaseFontSize(2560, 1440) == 64
                    && WallpaperLayout.BaseFontSize(1366, 768) == 34
                    && WallpaperLayout.BaseFontSize(1080, 1920) == 27
                    && WallpaperLayout.BaseFontSize(320, 240) == 24
                    && WallpaperLayout.BaseFontSize(7680, 4320) == 144,
                "4k=" + WallpaperLayout.BaseFontSize(3840, 2160)
                    + " 2k=" + WallpaperLayout.BaseFontSize(2560, 1440)
                    + " 1366=" + WallpaperLayout.BaseFontSize(1366, 768)
                    + " portrait=" + WallpaperLayout.BaseFontSize(1080, 1920)
                    + " tiny=" + WallpaperLayout.BaseFontSize(320, 240)
                    + " 8k=" + WallpaperLayout.BaseFontSize(7680, 4320));
            Add(checks, "wallpaper_margin_three_percent_with_floor",
                WallpaperLayout.MarginFor(1920) == 58 && WallpaperLayout.MarginFor(100) == 16
                    && WallpaperLayout.MaxTextWidth(1920) == 1920 - 58 * 2,
                "1920 margin=" + WallpaperLayout.MarginFor(1920) + " maxTextWidth=" + WallpaperLayout.MaxTextWidth(1920));

            WallpaperLayout.MeasureWidth fakeMeasure = delegate(string text2, int size)
            {
                return text2.Length * size / 2;
            };
            string caption = WallpaperLayout.Caption("192.168.168.201");
            int untouched = WallpaperLayout.FitFontSize(caption, 48, 1000, fakeMeasure);
            int shrunk = WallpaperLayout.FitFontSize(caption, 48, 300, fakeMeasure);
            Add(checks, "wallpaper_fit_keeps_size_when_it_fits", untouched == 48, "got=" + untouched);
            Add(checks, "wallpaper_fit_shrinks_stepwise",
                shrunk == 26 && fakeMeasure(caption, shrunk) <= 300 && fakeMeasure(caption, shrunk + 3) > 300,
                "expected=26 got=" + shrunk + " measured=" + fakeMeasure(caption, shrunk)
                    + " captionLength=" + caption.Length);
            Add(checks, "wallpaper_fit_never_below_floor",
                WallpaperLayout.FitFontSize(caption, 48, 10, fakeMeasure) == WallpaperLayout.MinFontSize,
                "got=" + WallpaperLayout.FitFontSize(caption, 48, 10, fakeMeasure));

            RenderMeta meta = new RenderMeta();
            using (var bitmap = WallpaperRenderer.Render("192.168.168.201",
                WallpaperLayout.FindColor("黑"), WallpaperLayout.FindColor("白"), 640, 400, meta))
            {
                Color corner = bitmap.GetPixel(2, 2);
                bool foundTextPixel = false;
                for (int y = 0; y < 120 && !foundTextPixel; y++)
                {
                    for (int x = 320; x < 640 && !foundTextPixel; x++)
                    {
                        Color pixel = bitmap.GetPixel(x, y);
                        if (pixel.R > 200 && pixel.G > 200 && pixel.B > 200)
                        {
                            foundTextPixel = true;
                        }
                    }
                }
                Add(checks, "wallpaper_render_black_bg_white_text",
                    corner.R == 0 && corner.G == 0 && corner.B == 0 && foundTextPixel
                        && meta.FontSize >= WallpaperLayout.MinFontSize && meta.FontSize <= 48,
                "corner=" + corner.R + "," + corner.G + "," + corner.B
                        + " textPixel=" + foundTextPixel + " fontSize=" + meta.FontSize + " font=" + meta.FontFamily);
            }
            using (var inverted = WallpaperRenderer.Render("192.168.168.201",
                WallpaperLayout.FindColor("白"), WallpaperLayout.FindColor("黑"), 640, 400, new RenderMeta()))
            {
                Color corner = inverted.GetPixel(2, 2);
                bool foundDarkPixel = false;
                for (int y = 0; y < 120 && !foundDarkPixel; y++)
                {
                    for (int x = 320; x < 640 && !foundDarkPixel; x++)
                    {
                        Color pixel = inverted.GetPixel(x, y);
                        if (pixel.R < 60 && pixel.G < 60 && pixel.B < 60)
                        {
                            foundDarkPixel = true;
                        }
                    }
                }
                Add(checks, "wallpaper_render_honours_chosen_colors",
                    corner.R == 255 && corner.G == 255 && corner.B == 255 && foundDarkPixel,
                    "corner=" + corner.R + "," + corner.G + "," + corner.B + " darkPixel=" + foundDarkPixel);
            }
            int keepWidth = 1920;
            int keepHeight = 1080;
            bool badParse = WallpaperCli.TryParseSize("垃圾输入", ref keepWidth, ref keepHeight);
            bool emptyParse = WallpaperCli.TryParseSize("", ref keepWidth, ref keepHeight);
            bool tinyParse = WallpaperCli.TryParseSize("60x40", ref keepWidth, ref keepHeight);
            Add(checks, "cli_size_parse_preserves_screen_on_failure",
                !badParse && !emptyParse && !tinyParse && keepWidth == 1920 && keepHeight == 1080,
                "after failed parses width=" + keepWidth + " height=" + keepHeight);
            bool goodParse = WallpaperCli.TryParseSize("1366x768", ref keepWidth, ref keepHeight);
            Add(checks, "cli_size_parse_applies_valid_size",
                goodParse && keepWidth == 1366 && keepHeight == 768,
                "got=" + keepWidth + "x" + keepHeight);
            string[] sample = new string[] { "--scan", "--size=1366x768", "--bg", "深蓝", "--ip=192.168.168.201" };
            Add(checks, "cli_reads_inline_and_spaced_flags",
                Cli.Value(sample, "--size", string.Empty) == "1366x768"
                    && Cli.Value(sample, "--bg", string.Empty) == "深蓝"
                    && Cli.Value(sample, "--ip", string.Empty) == "192.168.168.201"
                    && Cli.Value(sample, "--missing", "默认值") == "默认值",
                "size=" + Cli.Value(sample, "--size", "-") + " bg=" + Cli.Value(sample, "--bg", "-"));
            return checks;
        }

        public static int RunAndReport(string outFile)
        {
            List<Check> checks = Run();
            var sb = new StringBuilder();
            int failed = 0;
            sb.AppendLine("SELFTEST_BEGIN product=" + Product.Title + " " + Product.VersionText);
            foreach (Check check in checks)
            {
                if (!check.Ok)
                {
                    failed++;
                }
                sb.AppendLine((check.Ok ? "PASS " : "FAIL ") + check.Key + " :: " + check.Detail);
            }
            sb.AppendLine("SELFTEST_RESULT total=" + checks.Count + " failed=" + failed + " rc=" + (failed == 0 ? 0 : 1));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outFile));
                File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Console.WriteLine("SELFTEST_WRITE_FAILED " + ex.Message);
            }
            Console.WriteLine(sb.ToString());
            return failed == 0 ? 0 : 1;
        }

        private static void Add(List<Check> checks, string key, bool ok, string detail)
        {
            checks.Add(new Check(key, ok, detail));
        }
    }
}
