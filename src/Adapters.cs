using System;
using System.Collections.Generic;
using System.Management;

namespace ZiceLan
{
    public class AdapterInfo
    {
        public int Index;
        public int InterfaceIndex;
        public string Name = string.Empty;
        public string Guid = string.Empty;
        public string Description = string.Empty;
        public string Ip = string.Empty;
        public string Mask = string.Empty;
        public string Gateway = string.Empty;
        public List<string> Dns = new List<string>();
        public bool DhcpEnabled;
        public bool NetEnabled;
        public bool PhysicalAdapter;

        public bool UsableForNetsh
        {
            get { return !string.IsNullOrEmpty(Name) && IpCore.IsValidIpv4(Ip); }
        }

        public bool InPrefix(string basePrefix)
        {
            return IpCore.IsValidIpv4(Ip) && IpCore.InPrefix(basePrefix, Ip);
        }

        // 有格式合法且不是 169.254 APIPA 的 IPv4。APIPA 说明 DHCP 没拿到地址，
        // 拿它当「所在局域网」只会扫出一个不存在的段，所以自动填充一律排除。
        public bool HasRoutableIp
        {
            get
            {
                return IpCore.IsValidIpv4(Ip) && !Ip.StartsWith("169.254.", StringComparison.Ordinal);
            }
        }

        public string Label
        {
            get
            {
                string who = Name.Length == 0 ? Description : Name;
                string ipPart = IpCore.IsValidIpv4(Ip) ? Ip : "无 IPv4";
                if (IpCore.IsValidIpv4(Ip) && Mask.Length > 0)
                {
                    ipPart = ipPart + "/" + Mask;
                }
                return who + "  [" + ipPart + "]";
            }
        }
    }

    public static class Adapters
    {
        public static List<AdapterInfo> Read(out string error)
        {
            error = string.Empty;
            var byIndex = new Dictionary<int, AdapterInfo>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\cimv2",
                    "SELECT Index, InterfaceIndex, SettingID, Description, IPAddress, IPSubnet, DefaultIPGateway, DNSServerSearchOrder, DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=TRUE"))
                using (ManagementObjectCollection found = searcher.Get())
                {
                    foreach (ManagementObject obj in found)
                    {
                        AdapterInfo info = ReadConfiguration(obj);
                        if (info != null)
                        {
                            byIndex[info.Index] = info;
                        }
                        obj.Dispose();
                    }
                }

                using (var names = new ManagementObjectSearcher("root\\cimv2",
                    "SELECT Index, Name, NetConnectionID, NetEnabled, PhysicalAdapter FROM Win32_NetworkAdapter"))
                using (ManagementObjectCollection found = names.Get())
                {
                    foreach (ManagementObject obj in found)
                    {
                        int index = ToIndex(obj["Index"]);
                        AdapterInfo info;
                        if (index >= 0 && byIndex.TryGetValue(index, out info))
                        {
                            string connectionId = ToText(obj["NetConnectionID"]);
                            if (connectionId.Length > 0)
                            {
                                info.Name = connectionId;
                            }
                            info.NetEnabled = ToBool(obj["NetEnabled"]);
                            info.PhysicalAdapter = ToBool(obj["PhysicalAdapter"]);
                        }
                        obj.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            var list = new List<AdapterInfo>();
            foreach (KeyValuePair<int, AdapterInfo> pair in byIndex)
            {
                AdapterInfo info = pair.Value;
                bool hasAddress = IpCore.IsValidIpv4(info.Ip);
                bool connectedWithoutAddress = !hasAddress && info.NetEnabled && info.PhysicalAdapter
                    && info.Name.Length > 0;
                if (hasAddress || connectedWithoutAddress)
                {
                    list.Add(info);
                }
            }
            list.Sort(delegate(AdapterInfo a, AdapterInfo b) { return a.Index.CompareTo(b.Index); });
            return list;
        }

        public static string FirstValidIp(List<AdapterInfo> adapters)
        {
            if (adapters == null)
            {
                return string.Empty;
            }
            foreach (AdapterInfo info in adapters)
            {
                if (IpCore.IsValidIpv4(info.Ip))
                {
                    return info.Ip;
                }
            }
            return string.Empty;
        }

        public static AdapterInfo FindByIndex(int index, string preferIp)
        {
            string error;
            List<AdapterInfo> all = Read(out error);
            foreach (AdapterInfo info in all)
            {
                if (info.Index == index)
                {
                    return info;
                }
            }
            foreach (AdapterInfo info in all)
            {
                if (string.Equals(info.Ip, preferIp, StringComparison.OrdinalIgnoreCase))
                {
                    return info;
                }
            }
            return null;
        }

        // 「最能代表本机所在局域网」的网卡排名，越小越优先：
        // 0 = 真实 IPv4 且读到了网关；1 = 真实 IPv4 没网关；2 = 只有 169.254 APIPA；
        // 3 = 已连接但没地址的物理网卡；4 = 其余。同级保持原列表顺序（稳定）。
        public static int PrimaryRank(AdapterInfo info)
        {
            if (info == null)
            {
                return int.MaxValue;
            }
            if (info.HasRoutableIp)
            {
                return info.Gateway.Length > 0 ? 0 : 1;
            }
            if (IpCore.IsValidIpv4(info.Ip))
            {
                return 2;
            }
            if (info.NetEnabled && info.PhysicalAdapter && info.Name.Length > 0)
            {
                return 3;
            }
            return 4;
        }

        // 自动检测所在局域网用：从网卡清单里挑出代表网卡。调用方在拿到 null
        // （清单为空）或 HasRoutableIp == false（只有 APIPA / 没地址）时如实报错。
        public static AdapterInfo PickPrimary(List<AdapterInfo> adapters)
        {
            if (adapters == null || adapters.Count == 0)
            {
                return null;
            }
            AdapterInfo best = adapters[0];
            int bestRank = PrimaryRank(best);
            for (int i = 1; i < adapters.Count; i++)
            {
                int rank = PrimaryRank(adapters[i]);
                if (rank < bestRank)
                {
                    best = adapters[i];
                    bestRank = rank;
                }
            }
            return best;
        }

        public static string ReadArpTableText()
        {
            CmdResult result = Cmd.Run("arp.exe", "-a", 8000);
            if (result.TimedOut || result.ExitCode != 0)
            {
                return string.Empty;
            }
            return result.Output;
        }

        private static AdapterInfo ReadConfiguration(ManagementObject obj)
        {
            int index = ToIndex(obj["Index"]);
            if (index < 0)
            {
                return null;
            }
            var info = new AdapterInfo();
            info.Index = index;
            info.InterfaceIndex = ToIndex(obj["InterfaceIndex"]);
            info.Guid = ToText(obj["SettingID"]);
            info.Description = ToText(obj["Description"]);
            info.DhcpEnabled = ToBool(obj["DHCPEnabled"]);
            info.Gateway = FirstIpv4(obj["DefaultIPGateway"] as string[]);

            string[] addresses = obj["IPAddress"] as string[];
            string[] subnets = obj["IPSubnet"] as string[];
            if (addresses != null)
            {
                int chosen = -1;
                for (int i = 0; i < addresses.Length; i++)
                {
                    if (addresses[i] == null || !IpCore.IsValidIpv4(addresses[i]))
                    {
                        continue;
                    }
                    if (chosen < 0 || (!addresses[i].StartsWith("169.254.", StringComparison.Ordinal)
                        && addresses[chosen].StartsWith("169.254.", StringComparison.Ordinal)))
                    {
                        chosen = i;
                    }
                }
                if (chosen >= 0)
                {
                    info.Ip = addresses[chosen];
                    if (subnets != null && chosen < subnets.Length && subnets[chosen] != null)
                    {
                        info.Mask = subnets[chosen];
                    }
                }
            }

            string[] servers = obj["DNSServerSearchOrder"] as string[];
            if (servers != null)
            {
                foreach (string server in servers)
                {
                    if (server != null && IpCore.IsValidIpv4(server))
                    {
                        info.Dns.Add(server);
                    }
                }
            }
            return info;
        }

        private static string FirstIpv4(string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }
            foreach (string value in values)
            {
                if (value != null && IpCore.IsValidIpv4(value))
                {
                    return value;
                }
            }
            return string.Empty;
        }

        private static int ToIndex(object value)
        {
            try
            {
                if (value == null)
                {
                    return -1;
                }
                return Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static string ToText(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            return Convert.ToString(value);
        }

        private static bool ToBool(object value)
        {
            try
            {
                return value != null && Convert.ToBoolean(value);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
