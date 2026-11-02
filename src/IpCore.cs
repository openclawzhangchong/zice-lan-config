using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace ZiceLan
{
    public static class IpCore
    {
        public static uint ToNetworkOrder(string ip)
        {
            byte[] bytes = IPAddress.Parse(ip).GetAddressBytes();
            return BitConverter.ToUInt32(bytes, 0);
        }

        public static string FromNetworkOrder(uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}", bytes[0], bytes[1], bytes[2], bytes[3]);
        }

        public static bool IsValidIpv4(string ip)
        {
            if (string.IsNullOrEmpty(ip))
            {
                return false;
            }
            string[] parts = ip.Split('.');
            if (parts.Length != 4)
            {
                return false;
            }
            for (int i = 0; i < 4; i++)
            {
                int value;
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out value))
                {
                    return false;
                }
                if (value < 0 || value > 255)
                {
                    return false;
                }
            }
            return true;
        }

        public static string BasePrefix(string ip)
        {
            if (!IsValidIpv4(ip))
            {
                return string.Empty;
            }
            string[] parts = ip.Split('.');
            return parts[0] + "." + parts[1] + "." + parts[2];
        }

        public static bool IsValidPrefix(string prefix)
        {
            return BasePrefix(prefix + ".1").Length > 0 && IsValidIpv4(prefix + ".1");
        }

        public static string Combine(string basePrefix, int octet)
        {
            return basePrefix + "." + octet.ToString(CultureInfo.InvariantCulture);
        }

        public static int OctetOf(string ip)
        {
            string[] parts = ip.Split('.');
            if (parts.Length != 4)
            {
                return -1;
            }
            int value;
            if (!int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                return -1;
            }
            return value;
        }

        public static List<int> BuildCandidates(int from, int to, int reserveFrom, int reserveTo)
        {
            if (from < 1)
            {
                from = 1;
            }
            if (to > 254)
            {
                to = 254;
            }
            var list = new List<int>();
            for (int octet = from; octet <= to; octet++)
            {
                bool reserved = reserveFrom > 0 && octet >= reserveFrom && octet <= reserveTo;
                if (reserved)
                {
                    continue;
                }
                list.Add(octet);
            }
            return list;
        }

        public static bool InPrefix(string basePrefix, string ip)
        {
            return ip.StartsWith(basePrefix + ".", StringComparison.Ordinal);
        }
    }
}
