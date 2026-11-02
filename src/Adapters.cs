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
        public string Description = string.Empty;
        public string Ip = string.Empty;
        public string Mask = string.Empty;
        public string Gateway = string.Empty;
        public List<string> Dns = new List<string>();
        public bool DhcpEnabled;

        public bool UsableForNetsh
        {
            get { return !string.IsNullOrEmpty(Name) && IpCore.IsValidIpv4(Ip); }
        }

        public bool InPrefix(string basePrefix)
        {
            return IpCore.IsValidIpv4(Ip) && IpCore.InPrefix(basePrefix, Ip);
        }

        public string Label
        {
            get
            {
                string who = Name.Length == 0 ? Description : Name;
                string ipPart = Ip.Length == 0 ? "无 IPv4" : Ip;
                if (Mask.Length > 0)
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
                    "SELECT Index, InterfaceIndex, Description, IPAddress, IPSubnet, DefaultIPGateway, DNSServerSearchOrder, DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=TRUE"))
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
                    "SELECT Index, Name, NetConnectionID FROM Win32_NetworkAdapter"))
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
                if (IpCore.IsValidIpv4(pair.Value.Ip))
                {
                    list.Add(pair.Value);
                }
            }
            list.Sort(delegate(AdapterInfo a, AdapterInfo b) { return a.Index.CompareTo(b.Index); });
            return list;
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
