using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZiceLan
{
    public class HotspotCandidate
    {
        public int ThirdOctet;
        public string Gateway = string.Empty;
        public int Distance;
        public string Reason = string.Empty;
    }

    public static class HotspotAdvisor
    {
        public const string FamilyPrefix = "192.168";

        // Third octets that routers, APs, printers and management ports default to.
        public static readonly int[] CommonDefaults = new int[]
        {
            0, 1, 2, 3, 4, 5, 10, 20, 30, 50, 100, 101, 110, 128, 168, 169, 172, 192, 200, 201, 253, 254
        };

        public static string GatewayFor(int octet)
        {
            return FamilyPrefix + "." + octet.ToString(CultureInfo.InvariantCulture) + ".1";
        }

        public static int ThirdOctetOf(string ip)
        {
            if (string.IsNullOrEmpty(ip))
            {
                return -1;
            }
            string[] parts = ip.Split('.');
            if (parts.Length != 4)
            {
                return -1;
            }
            int value;
            if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out value))
            {
                return -1;
            }
            return value;
        }

        public static int OctetFromGateway(string gateway)
        {
            if (!IpCore.IsValidIpv4(gateway))
            {
                return -1;
            }
            if (!IpCore.InPrefix(FamilyPrefix, gateway))
            {
                return -1;
            }
            string[] parts = gateway.Split('.');
            if (!string.Equals(parts[3], "1", StringComparison.Ordinal))
            {
                return -1;
            }
            return ThirdOctetOf(gateway);
        }

        public static HashSet<int> UsedThirdOctets(IEnumerable<AdapterInfo> adapters)
        {
            var used = new HashSet<int>();
            if (adapters == null)
            {
                return used;
            }
            foreach (AdapterInfo info in adapters)
            {
                if (!IpCore.IsValidIpv4(info.Ip))
                {
                    continue;
                }
                if (!IpCore.InPrefix(FamilyPrefix, info.Ip))
                {
                    continue;
                }
                int third = ThirdOctetOf(info.Ip);
                if (third >= 0 && third <= 255)
                {
                    used.Add(third);
                }
            }
            return used;
        }

        public static List<int> BlockedOctets(ICollection<int> usedOctets, ICollection<int> respondedOctets, int currentOctet)
        {
            var blocked = new List<int>();
            foreach (int common in CommonDefaults)
            {
                if (!blocked.Contains(common))
                {
                    blocked.Add(common);
                }
            }
            if (usedOctets != null)
            {
                foreach (int octet in usedOctets)
                {
                    if (!blocked.Contains(octet))
                    {
                        blocked.Add(octet);
                    }
                }
            }
            if (respondedOctets != null)
            {
                foreach (int octet in respondedOctets)
                {
                    if (!blocked.Contains(octet))
                    {
                        blocked.Add(octet);
                    }
                }
            }
            if (currentOctet >= 0 && !blocked.Contains(currentOctet))
            {
                blocked.Add(currentOctet);
            }
            blocked.Sort();
            return blocked;
        }

        public static List<HotspotCandidate> Rank(ICollection<int> usedOctets, ICollection<int> respondedOctets,
                                                 int currentOctet, int take)
        {
            List<int> blocked = BlockedOctets(usedOctets, respondedOctets, currentOctet);
            var candidates = new List<HotspotCandidate>();
            for (int octet = 1; octet <= 253; octet++)
            {
                if (blocked.Contains(octet))
                {
                    continue;
                }
                int distance = int.MaxValue;
                foreach (int point in blocked)
                {
                    int gap = Math.Abs(octet - point);
                    if (gap < distance)
                    {
                        distance = gap;
                    }
                }
                var candidate = new HotspotCandidate();
                candidate.ThirdOctet = octet;
                candidate.Gateway = GatewayFor(octet);
                candidate.Distance = distance;
                candidate.Reason = "本机未占用、不是常见默认段；离最近被占用的段 " + distance + " 跳";
                candidates.Add(candidate);
            }
            candidates.Sort(delegate(HotspotCandidate a, HotspotCandidate b)
            {
                if (a.Distance != b.Distance)
                {
                    return b.Distance.CompareTo(a.Distance);
                }
                return a.ThirdOctet.CompareTo(b.ThirdOctet);
            });
            var result = new List<HotspotCandidate>();
            for (int i = 0; i < candidates.Count && i < take; i++)
            {
                result.Add(candidates[i]);
            }
            return result;
        }
    }
}
