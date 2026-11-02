using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;

namespace ZiceLan
{
    public class ApplyReport
    {
        public bool Success;
        public string Message = string.Empty;
        public AdapterInfo After;
        public int GatewayPingOk;
        public int GatewayPingTotal;
    }

    public static class NetworkApplier
    {
        private const int NetshTimeoutMs = 20000;

        public static ApplyReport Apply(AdapterInfo target, string newIp, string mask, string gateway,
                                        IList<string> dnsServers, Action<string> log)
        {
            var report = new ApplyReport();
            string ownIp = target.Ip;

            if (ArpProbe.Responds(newIp, ownIp) || ArpProbe.Responds(newIp, ownIp))
            {
                report.Message = "写入前复测：新地址已有设备应答，已中止（本机配置未改动）。请重新扫描。";
                return report;
            }
            log("写入前复测通过：" + newIp + " 仍无设备应答（ARP 两次采样）");

            List<string> script = NetshArgs.BuildApplyScript(target.Name, newIp, mask, gateway, dnsServers);
            foreach (string arguments in script)
            {
                log("netsh " + arguments);
                CmdResult result = Cmd.Run(NetshArgs.Exe, arguments, NetshTimeoutMs);
                if (result.TimedOut)
                {
                    report.Message = "netsh 超过 " + NetshTimeoutMs + "ms 未返回，已强制结束进程。命令：" + arguments;
                    return report;
                }
                if (result.ExitCode != 0)
                {
                    report.Message = "netsh 退出码 " + result.ExitCode + "。输出：" + Shorten(result.Text);
                    return report;
                }
            }

            report.After = WaitForAddress(target.Index, newIp, ownIp, 15000, log);
            if (report.After == null)
            {
                report.Message = "写入后读不到该网卡，请检查网线后用「恢复自动获取」回退。";
                return report;
            }
            if (!string.Equals(report.After.Ip, newIp, StringComparison.OrdinalIgnoreCase))
            {
                report.Message = "写入后实际生效地址是 " + Describe(report.After) + "，与预期 " + newIp + " 不一致。";
                return report;
            }

            if (!string.IsNullOrEmpty(gateway))
            {
                report.GatewayPingTotal = 3;
                for (int i = 0; i < 3; i++)
                {
                    if (Scanner.ProbePing(gateway, 1000) == IPStatus.Success)
                    {
                        report.GatewayPingOk++;
                    }
                    Thread.Sleep(200);
                }
                log("网关 " + gateway + " 连通性：" + report.GatewayPingOk + "/" + report.GatewayPingTotal + " 次成功");
            }

            report.Success = report.GatewayPingTotal == 0 || report.GatewayPingOk > 0;
            report.Message = report.Success
                ? "已生效：" + Describe(report.After) + (report.GatewayPingTotal > 0 ? "，网关可达 " + report.GatewayPingOk + "/3" : "")
                : "地址已写入但网关 " + gateway + " 三次都不通，请检查网关地址或点「恢复自动获取」回退。";
            return report;
        }

        public static ApplyReport RestoreDhcp(AdapterInfo target, Action<string> log)
        {
            var report = new ApplyReport();
            foreach (string arguments in NetshArgs.BuildDhcpScript(target.Name))
            {
                log("netsh " + arguments);
                CmdResult result = Cmd.Run(NetshArgs.Exe, arguments, NetshTimeoutMs);
                if (result.TimedOut)
                {
                    report.Message = "netsh 超时被强制结束。命令：" + arguments;
                    return report;
                }
                if (result.ExitCode != 0)
                {
                    report.Message = "netsh 退出码 " + result.ExitCode + "。输出：" + Shorten(result.Text);
                    return report;
                }
            }

            string previousIp = target.Ip;
            int start = Environment.TickCount;
            AdapterInfo latest = null;
            while (Environment.TickCount - start < 20000)
            {
                latest = ReadOne(target.Index, previousIp);
                if (latest != null && latest.DhcpEnabled && IpCore.IsValidIpv4(latest.Ip)
                    && !string.Equals(latest.Ip, previousIp, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                Thread.Sleep(700);
            }
            report.After = latest;
            if (latest == null)
            {
                report.Message = "已切回自动获取，但暂时读不到网卡信息。";
                return report;
            }
            report.Success = latest.DhcpEnabled && IpCore.IsValidIpv4(latest.Ip);
            report.Message = report.Success
                ? "已恢复自动获取：" + Describe(latest)
                : "命令执行完成，当前地址 " + Describe(latest) + "，DHCP 标志=" + (latest.DhcpEnabled ? "开" : "关") + "。";
            return report;
        }

        private static AdapterInfo WaitForAddress(int index, string expectedIp, string preferOldIp, int timeoutMs, Action<string> log)
        {
            int start = Environment.TickCount;
            AdapterInfo latest = null;
            while (Environment.TickCount - start < timeoutMs)
            {
                latest = ReadOne(index, preferOldIp);
                if (latest != null && string.Equals(latest.Ip, expectedIp, StringComparison.OrdinalIgnoreCase))
                {
                    return latest;
                }
                Thread.Sleep(500);
            }
            log("等待 " + timeoutMs + "ms 后仍未读到预期地址，最后一次结果：" + Describe(latest));
            return latest;
        }

        private static AdapterInfo ReadOne(int index, string preferIp)
        {
            string error;
            List<AdapterInfo> list = Adapters.Read(out error);
            foreach (AdapterInfo info in list)
            {
                if (info.Index == index)
                {
                    return info;
                }
            }
            foreach (AdapterInfo info in list)
            {
                if (string.Equals(info.Ip, preferIp, StringComparison.OrdinalIgnoreCase))
                {
                    return info;
                }
            }
            return null;
        }

        private static string Describe(AdapterInfo info)
        {
            if (info == null)
            {
                return "无";
            }
            return info.Name + " " + info.Ip + "/" + info.Mask + " 网关 " + info.Gateway + " DNS " + string.Join(",", info.Dns.ToArray());
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "（无输出）";
            }
            string one = text.Replace("\r", " ").Replace("\n", " ");
            return one.Length > 300 ? one.Substring(0, 300) : one;
        }
    }
}
