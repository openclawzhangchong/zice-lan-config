using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ZiceLan
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
            if (mode == "--selftest")
            {
                return SelfTest.RunAndReport(Cli.OutPath(args, "selftest.txt"));
            }
            if (mode == "--scan")
            {
                return ScanCli.Run(Cli.OutPath(args, "scan.txt"), args);
            }
            if (mode == "--version")
            {
                Console.WriteLine(Product.Title + " " + Product.VersionText);
                return 0;
            }
            Application.Run(new MainForm());
            return 0;
        }
    }

    public static class Cli
    {
        public static string Value(string[] args, string flag, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return fallback;
        }

        public static string OutPath(string[] args, string defaultName)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith("--out=", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(arg.Substring(6));
                }
            }
            string explicitPath = Value(args, "--out", null);
            if (!string.IsNullOrEmpty(explicitPath))
            {
                return Path.GetFullPath(explicitPath);
            }
            string exeDir = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            return Path.Combine(exeDir, defaultName);
        }
    }

    public static class ScanCli
    {
        public static int Run(string outFile, string[] args)
        {
            var sb = new StringBuilder();
            string error;
            List<AdapterInfo> adapters = Adapters.Read(out error);
            sb.AppendLine("MODE=headless-scan");
            sb.AppendLine("PRODUCT=" + Product.Title + " " + Product.VersionText);
            sb.AppendLine("FILEVERSION=" + FileVersionInfo.GetVersionInfo(Process.GetCurrentProcess().MainModule.FileName).FileVersion);
            sb.AppendLine("ADAPTER_ERROR=" + error);
            foreach (AdapterInfo info in adapters)
            {
                sb.AppendLine("ADAPTER wmiIndex=" + info.Index + " ifIndex=" + info.InterfaceIndex + " name=" + info.Name
                    + " ip=" + info.Ip
                    + " mask=" + info.Mask + " gw=" + info.Gateway
                    + " dns=" + string.Join(",", info.Dns.ToArray()) + " dhcp=" + info.DhcpEnabled);
            }

            string prefix = Cli.Value(args, "--prefix", "192.168.168");
            AdapterInfo chosen = ChooseAdapter(adapters, prefix, Cli.Value(args, "--adapter", null));
            if (chosen == null)
            {
                sb.AppendLine("SCAN_ERROR=no usable adapter");
                File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(true));
                return 2;
            }
            int reserve = 20;
            int parsedReserve;
            if (int.TryParse(Cli.Value(args, "--reserve", "20"), out parsedReserve))
            {
                reserve = parsedReserve;
            }
            int reserveFrom = reserve >= 1 ? 1 : 0;

            string arpTable = Adapters.ReadArpTableText();
            sb.AppendLine("ARP_TABLE_LINES=" + arpTable.Replace('\r', '\n').Split('\n').Length);
            sb.AppendLine("GATEWAY_PING=" + chosen.Gateway + "=" + Scanner.ProbePing(chosen.Gateway));

            Stopwatch watch = Stopwatch.StartNew();
            ScanResult result = Scanner.Run(prefix, reserveFrom, reserve, chosen.Ip, chosen.Ip, arpTable, null, null);
            watch.Stop();
            sb.AppendLine("SCAN prefix=" + prefix + " adapter=" + chosen.Name + " candidates=" + result.Candidates
                + " free=" + result.Free.Count + " used=" + result.Used.Count + " unknown=" + result.Unknown.Count
                + " not_scanned=" + result.NotScanned + " elapsed_ms=" + result.ElapsedMs
                + " ping_ms=" + result.PingMs + " arp_ms=" + result.ArpMs
                + " parallel=" + Scanner.MaxParallel + " wall_ms=" + watch.ElapsedMilliseconds);
            sb.AppendLine("FREE=" + JoinOctets(prefix, result.Free, 400));
            sb.AppendLine("UNKNOWN=" + JoinOctets(prefix, result.Unknown, 200));
            sb.AppendLine("NOTE=" + result.Note);

            int contradictions = 0;
            for (int i = 0; i < result.Free.Count && i < 3; i++)
            {
                string ip = IpCore.Combine(prefix, result.Free[i]);
                CmdResult ping = Cmd.Run("ping.exe", "-n 2 -w 500 " + ip, 6000);
                sb.AppendLine("CROSSCHECK_SYSTEM_PING " + ip + " exit=" + ping.ExitCode + " timedout=" + ping.TimedOut);
                if (ping.ExitCode == 0)
                {
                    contradictions++;
                }
            }
            sb.AppendLine("CROSSCHECK_CONTRADICTIONS=" + contradictions);
            File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(true));
            return contradictions == 0 ? 0 : 3;
        }

        private static AdapterInfo ChooseAdapter(List<AdapterInfo> adapters, string prefix, string indexHint)
        {
            if (indexHint != null)
            {
                int wanted;
                if (int.TryParse(indexHint, out wanted))
                {
                    foreach (AdapterInfo info in adapters)
                    {
                        if (info.Index == wanted)
                        {
                            return info;
                        }
                    }
                }
            }
            foreach (AdapterInfo info in adapters)
            {
                if (info.UsableForNetsh && info.InPrefix(prefix))
                {
                    return info;
                }
            }
            foreach (AdapterInfo info in adapters)
            {
                if (info.UsableForNetsh)
                {
                    return info;
                }
            }
            return null;
        }

        private static string JoinOctets(string prefix, List<int> octets, int maxShown)
        {
            var parts = new List<string>();
            for (int i = 0; i < octets.Count && i < maxShown; i++)
            {
                parts.Add(IpCore.Combine(prefix, octets[i]));
            }
            string text = string.Join(" ", parts.ToArray());
            if (octets.Count > maxShown)
            {
                text = text + " ...(" + octets.Count + " total)";
            }
            return text;
        }
    }
}
