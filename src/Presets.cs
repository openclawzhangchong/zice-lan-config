using System;
using System.Collections.Generic;

namespace ZiceLan
{
    public class SubnetPreset
    {
        public string Label;
        public string Prefix;
        public string FallbackGateway;

        public SubnetPreset(string label, string prefix, string fallbackGateway)
        {
            Label = label;
            Prefix = prefix;
            FallbackGateway = fallbackGateway;
        }
    }

    public class DnsPreset
    {
        public string Name;
        public string Primary;
        public string Secondary;

        public DnsPreset(string name, string primary, string secondary)
        {
            Name = name;
            Primary = primary;
            Secondary = secondary;
        }
    }

    public static class SubnetPresets
    {
        public static List<SubnetPreset> All()
        {
            var list = new List<SubnetPreset>();
            list.Add(new SubnetPreset("192.168.168.x", "192.168.168", "192.168.168.1"));
            list.Add(new SubnetPreset("192.168.200.x", "192.168.200", "192.168.200.1"));
            return list;
        }

        public static List<string> Labels()
        {
            var labels = new List<string>();
            foreach (SubnetPreset preset in All())
            {
                labels.Add(preset.Prefix);
            }
            return labels;
        }

        public static string FallbackGatewayFor(string prefix)
        {
            foreach (SubnetPreset preset in All())
            {
                if (string.Equals(preset.Prefix, prefix, StringComparison.Ordinal))
                {
                    return preset.FallbackGateway;
                }
            }
            return IpCore.Combine(prefix, 1);
        }
    }

    public static class DnsPresets
    {
        public const string CustomLabel = "自定义";

        public static List<DnsPreset> All()
        {
            var list = new List<DnsPreset>();
            list.Add(new DnsPreset("阿里 AliDNS", "223.5.5.5", "223.6.6.6"));
            list.Add(new DnsPreset("腾讯 DNSPod", "119.29.29.29", "182.254.116.116"));
            list.Add(new DnsPreset("114DNS", "114.114.114.114", "114.114.115.115"));
            list.Add(new DnsPreset("百度 DNS", "180.76.76.76", ""));
            list.Add(new DnsPreset("360 DNS", "101.226.4.6", "123.125.81.6"));
            list.Add(new DnsPreset("火山引擎", "180.184.1.1", "180.184.2.2"));
            list.Add(new DnsPreset("CNNIC SDNS", "1.2.4.8", "210.2.4.8"));
            return list;
        }

        public static List<string> Labels()
        {
            var labels = new List<string>();
            labels.Add(CustomLabel);
            foreach (DnsPreset preset in All())
            {
                labels.Add(preset.Name);
            }
            return labels;
        }

        public static DnsPreset Find(string name)
        {
            foreach (DnsPreset preset in All())
            {
                if (string.Equals(preset.Name, name, StringComparison.Ordinal))
                {
                    return preset;
                }
            }
            return null;
        }

        public static List<string> ServersOf(DnsPreset preset)
        {
            var servers = new List<string>();
            if (preset == null)
            {
                return servers;
            }
            if (IpCore.IsValidIpv4(preset.Primary))
            {
                servers.Add(preset.Primary);
            }
            if (IpCore.IsValidIpv4(preset.Secondary) && !servers.Contains(preset.Secondary))
            {
                servers.Add(preset.Secondary);
            }
            return servers;
        }

        public static string TextFor(DnsPreset preset)
        {
            return string.Join(",", ServersOf(preset).ToArray());
        }

        public static string NameForServers(string dnsText)
        {
            foreach (DnsPreset preset in All())
            {
                if (string.Equals(TextFor(preset), dnsText, StringComparison.Ordinal))
                {
                    return preset.Name;
                }
            }
            return CustomLabel;
        }
    }
}
