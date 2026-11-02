using System;
using System.Collections.Generic;

namespace ZiceLan
{
    public static class NetshArgs
    {
        public const string Exe = "netsh.exe";

        public static string QuoteName(string adapter)
        {
            return "name=\"" + adapter.Replace("\"", "\\\"") + "\"";
        }

        public static string SetAddress(string adapter, string ip, string mask, string gateway)
        {
            string command = "interface ip set address " + QuoteName(adapter) + " static " + ip + " " + mask;
            if (!string.IsNullOrEmpty(gateway))
            {
                command = command + " " + gateway;
            }
            return command;
        }

        public static string SetDns(string adapter, string dns)
        {
            return "interface ip set dns " + QuoteName(adapter) + " static " + dns;
        }

        public static string AddDns(string adapter, string dns, int index)
        {
            return "interface ip add dns " + QuoteName(adapter) + " " + dns + " index=" + index;
        }

        public static string DhcpAddress(string adapter)
        {
            return "interface ip set address " + QuoteName(adapter) + " dhcp";
        }

        public static string DhcpDns(string adapter)
        {
            return "interface ip set dns " + QuoteName(adapter) + " dhcp";
        }

        public static List<string> BuildApplyScript(string adapter, string ip, string mask, string gateway, IList<string> dnsServers)
        {
            var script = new List<string>();
            script.Add(SetAddress(adapter, ip, mask, gateway));
            if (dnsServers != null)
            {
                for (int i = 0; i < dnsServers.Count; i++)
                {
                    string dns = dnsServers[i];
                    if (string.IsNullOrEmpty(dns))
                    {
                        continue;
                    }
                    if (script.Count == 1)
                    {
                        script.Add(SetDns(adapter, dns));
                    }
                    else
                    {
                        script.Add(AddDns(adapter, dns, script.Count));
                    }
                }
            }
            return script;
        }

        public static List<string> BuildDhcpScript(string adapter)
        {
            var script = new List<string>();
            script.Add(DhcpAddress(adapter));
            script.Add(DhcpDns(adapter));
            return script;
        }
    }
}
