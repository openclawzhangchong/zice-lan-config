using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ZiceLan
{
    public enum HostState
    {
        Unknown = 0,
        Free = 1,
        Used = 2
    }

    public static class ArpProbe
    {
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(int destIp, int srcIp, byte[] mac, ref int macLen);

        private const int MacLength = 6;

        private static readonly Regex EntryRx = new Regex(
            @"(\d{1,3}(?:\.\d{1,3}){3})\s+((?:[0-9A-Fa-f]{2}[-:]){5}[0-9A-Fa-f]{2})",
            RegexOptions.Compiled);

        public static bool Responds(string ip, string sourceIp)
        {
            var mac = new byte[8];
            int macLen = MacLength;
            int dest = unchecked((int)IpCore.ToNetworkOrder(ip));
            int src = 0;
            if (IpCore.IsValidIpv4(sourceIp))
            {
                src = unchecked((int)IpCore.ToNetworkOrder(sourceIp));
            }
            int ret;
            try
            {
                ret = SendARP(dest, src, mac, ref macLen);
            }
            catch (Exception)
            {
                return false;
            }
            if (ret != 0 || macLen < MacLength)
            {
                return false;
            }
            string text = FormatMac(mac);
            if (text == "00:00:00:00:00:00")
            {
                return false;
            }
            return true;
        }

        public static string FormatMac(byte[] mac)
        {
            var sb = new StringBuilder(17);
            for (int i = 0; i < MacLength; i++)
            {
                if (i > 0)
                {
                    sb.Append(':');
                }
                sb.Append(mac[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static HostState Classify(IPStatus pingStatus, bool arpReplied)
        {
            if (pingStatus == IPStatus.Success)
            {
                return HostState.Used;
            }
            if (arpReplied)
            {
                return HostState.Used;
            }
            if (IsLocalFailure(pingStatus))
            {
                return HostState.Unknown;
            }
            return HostState.Free;
        }

        private static readonly HashSet<IPStatus> LocalFailure = new HashSet<IPStatus>(new IPStatus[]
        {
            IPStatus.NoResources,
            IPStatus.BadRoute,
            IPStatus.HardwareError,
            IPStatus.BadDestination,
            IPStatus.BadOption,
            IPStatus.PacketTooBig,
            IPStatus.SourceQuench,
            IPStatus.ParameterProblem,
            IPStatus.DestinationNetworkUnreachable,
            IPStatus.BadHeader,
            IPStatus.UnrecognizedNextHeader,
            IPStatus.IcmpError,
            IPStatus.DestinationScopeMismatch,
            IPStatus.TtlReassemblyTimeExceeded,
            IPStatus.TimeExceeded,
            IPStatus.Unknown
        });

        public static bool IsLocalFailure(IPStatus status)
        {
            return LocalFailure.Contains(status);
        }

        public static HashSet<string> ParseTable(string arpOutput, string basePrefix, string ownIp)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(arpOutput))
            {
                return used;
            }
            string[] lines = arpOutput.Replace('\r', '\n').Split('\n');
            foreach (string line in lines)
            {
                Match match = EntryRx.Match(line);
                if (!match.Success)
                {
                    continue;
                }
                string ip = match.Groups[1].Value;
                string mac = match.Groups[2].Value.Replace('-', ':').ToUpper(CultureInfo.InvariantCulture);
                if (mac == "FF:FF:FF:FF:FF:FF" || mac == "01:00:5E:00:00:00")
                {
                    continue;
                }
                if (!IpCore.InPrefix(basePrefix, ip))
                {
                    continue;
                }
                if (string.Equals(ip, ownIp, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                int octet = IpCore.OctetOf(ip);
                if (octet < 1 || octet > 254)
                {
                    continue;
                }
                used.Add(ip);
            }
            return used;
        }
    }
}
