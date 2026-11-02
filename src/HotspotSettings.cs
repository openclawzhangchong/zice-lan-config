using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ZiceLan
{
    public class HotspotState
    {
        public string ScopeAddress = string.Empty;
        public string ScopeAddressBackup = string.Empty;
        public string AdapterName = string.Empty;
        public string AdapterIp = string.Empty;
        public string AdapterGuid = string.Empty;
        public bool ServiceRunning;
        public string Error = string.Empty;

        public string Describe()
        {
            string text = "注册表 ScopeAddress=" + (ScopeAddress.Length > 0 ? ScopeAddress : "读不到")
                + "　备份=" + (ScopeAddressBackup.Length > 0 ? ScopeAddressBackup : "读不到")
                + "　ICS 服务=" + (ServiceRunning ? "运行中" : "未运行");
            if (AdapterIp.Length > 0)
            {
                text = text + "　虚拟网卡 " + AdapterName + "=" + AdapterIp;
            }
            else
            {
                text = text + "　虚拟网卡未取到地址（热点可能没开）";
            }
            if (Error.Length > 0)
            {
                text = text + "　错误：" + Error;
            }
            return text;
        }
    }

    public class HotspotWriteResult
    {
        public bool Ok;
        public string Previous = string.Empty;
        public string Error = string.Empty;
    }

    public class ChangeResult
    {
        public bool Written;
        public bool Verified;
        public string Previous = string.Empty;
        public string Detail = string.Empty;
        public HotspotState After;
    }

    public class HotspotAdvice
    {
        public List<HotspotCandidate> Picked = new List<HotspotCandidate>();
        public List<int> Responded = new List<int>();
        public List<int> Used = new List<int>();
        public int ProbedCount;
        public int CurrentOctet = -1;
        public string Note = string.Empty;
    }

    public static class HotspotSettings
    {
        public const string KeyPath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters";
        public const string DefaultGateway = "192.168.137.1";
        public const string ServiceName = "SharedAccess";

        public static HotspotState Read()
        {
            var state = new HotspotState();
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(KeyPath, false))
                {
                    if (key == null)
                    {
                        state.Error = "读不到注册表项 " + KeyPath;
                    }
                    else
                    {
                        state.ScopeAddress = ToText(key.GetValue("ScopeAddress"));
                        state.ScopeAddressBackup = ToText(key.GetValue("ScopeAddressBackup"));
                    }
                }
            }
            catch (Exception ex)
            {
                state.Error = ex.Message;
            }

            string error;
            List<AdapterInfo> adapters = Adapters.Read(out error);
            AdapterInfo byKind = null;
            AdapterInfo byIp = null;
            foreach (AdapterInfo info in adapters)
            {
                if (byKind == null && IsHotspotAdapter(info))
                {
                    byKind = info;
                }
                if (byIp == null && string.Equals(info.Ip, state.ScopeAddress, StringComparison.OrdinalIgnoreCase))
                {
                    byIp = info;
                }
            }
            AdapterInfo chosen = byKind != null ? byKind : byIp;
            if (chosen != null)
            {
                state.AdapterName = chosen.Name;
                state.AdapterIp = chosen.Ip;
                state.AdapterGuid = chosen.Guid;
            }
            state.ServiceRunning = ServiceIsRunning();
            return state;
        }

        public static bool IsHotspotAdapter(AdapterInfo info)
        {
            return info.Description.IndexOf("Wi-Fi Direct Virtual Adapter", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool ServiceIsRunning()
        {
            CmdResult result = Cmd.Run("sc.exe", "query " + ServiceName, 10000);
            if (result.TimedOut)
            {
                return false;
            }
            return (result.Output ?? string.Empty).IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static HotspotWriteResult Write(string gateway)
        {
            var result = new HotspotWriteResult();
            if (!IpCore.IsValidIpv4(gateway))
            {
                result.Error = "网关地址格式不对：" + gateway;
                return result;
            }
            if (!gateway.EndsWith(".1", StringComparison.Ordinal))
            {
                result.Error = "热点网关段必须以 .1 结尾：" + gateway;
                return result;
            }
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(KeyPath, true))
                {
                    if (key == null)
                    {
                        result.Error = "打不开注册表项（需要管理员权限）：" + KeyPath;
                        return result;
                    }
                    result.Previous = ToText(key.GetValue("ScopeAddress"));
                    key.SetValue("ScopeAddress", gateway, RegistryValueKind.String);
                    key.SetValue("ScopeAddressBackup", gateway, RegistryValueKind.String);
                    result.Ok = true;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return result;
        }

        public static string RestartIcs(out string detail)
        {
            detail = string.Empty;
            CmdResult stop = Cmd.Run("net.exe", "stop " + ServiceName + " /y", 30000);
            CmdResult start = Cmd.Run("net.exe", "start " + ServiceName, 30000);
            detail = "stop rc=" + stop.ExitCode + (stop.TimedOut ? "(超时被终止)" : "")
                + " start rc=" + start.ExitCode + (start.TimedOut ? "(超时被终止)" : "");
            if (start.ExitCode != 0)
            {
                return "ICS 服务没能重启（" + detail + "）";
            }
            if (!ServiceIsRunning())
            {
                return "ICS 服务重启命令成功但状态不是运行中（" + detail + "）";
            }
            return string.Empty;
        }

        public static string NormalizeGuid(string adapterGuid)
        {
            if (string.IsNullOrEmpty(adapterGuid))
            {
                return string.Empty;
            }
            return adapterGuid.Replace("{", string.Empty).Replace("}", string.Empty).Trim().ToLower();
        }

        public static string BuildRebindCommand(string adapterGuid)
        {
            // Disable-NetAdapter has no -InterfaceGuid parameter on this box (only Name /
            // InterfaceDescription / InputObject), and the adapter name "本地连接* 9" contains
            // a wildcard, so filter the object by its InterfaceGuid property and pipe it in.
            string guid = NormalizeGuid(adapterGuid);
            string filter = "Get-NetAdapter | Where-Object { $_.InterfaceGuid -ieq '" + guid + "' }";
            return "-NoProfile -Command \"" + filter + " | Disable-NetAdapter -Confirm:$false;"
                + " Start-Sleep -Seconds 3; " + filter + " | Enable-NetAdapter -Confirm:$false\"";
        }

        public static string RebindAdapter(string adapterGuid, out string detail)
        {
            detail = string.Empty;
            if (string.IsNullOrEmpty(adapterGuid))
            {
                return "没找到热点虚拟网卡的 GUID（热点可能没开），跳过承载网络重建";
            }
            CmdResult run = Cmd.Run("powershell.exe", BuildRebindCommand(adapterGuid), 60000);
            detail = "rc=" + run.ExitCode + (run.TimedOut ? "(超时被终止)" : "");
            if (run.TimedOut || run.ExitCode != 0)
            {
                return "重建承载网络失败（" + detail + "）" + Shorten(run.Text);
            }
            return string.Empty;
        }

        public static ChangeResult Change(string targetGateway, Action<string> log)
        {
            var result = new ChangeResult();
            HotspotState before = Read();
            string guid = before.AdapterGuid;
            Say(log, "改之前先读一次：" + before.Describe());

            HotspotWriteResult write = Write(targetGateway);
            if (!write.Ok)
            {
                result.Detail = "注册表写入失败：" + write.Error;
                return result;
            }
            result.Written = true;
            result.Previous = write.Previous;
            Say(log, "已写入 ScopeAddress / ScopeAddressBackup = " + targetGateway
                + "，原值 " + (write.Previous.Length > 0 ? write.Previous : "（注册表原本没有值）"));

            string restartDetail;
            string restartError = RestartIcs(out restartDetail);
            Say(log, restartError.Length > 0
                ? "ICS 服务重启有问题：" + restartError + "（" + restartDetail + "）"
                : "ICS 服务已重启（" + restartDetail + "）");

            string rebindDetail;
            string rebindError = RebindAdapter(guid, out rebindDetail);
            Say(log, rebindError.Length > 0
                ? "承载网络重建：" + rebindError
                : "已禁用/启用热点虚拟网卡，让承载网络按新段重建（" + rebindDetail + "）");

            System.Threading.Thread.Sleep(3000);
            HotspotState after = Read();
            result.After = after;
            bool scopeOk = string.Equals(after.ScopeAddress, targetGateway, StringComparison.OrdinalIgnoreCase);
            bool adapterTookIt = string.Equals(after.AdapterIp, targetGateway, StringComparison.OrdinalIgnoreCase);
            result.Verified = scopeOk && restartError.Length == 0;
            result.Detail = after.Describe()
                + (scopeOk ? "" : "　注册表回读不等于目标值！")
                + (adapterTookIt
                    ? "　虚拟网卡已按新段起来"
                    : "　虚拟网卡还没拿到新段：承载网络是在移动热点被打开那一刻按注册表建的，"
                      + "请在「设置 → 移动热点」里关掉再打开一次，新段就会生效");
            return result;
        }

        private static void Say(Action<string> log, string line)
        {
            if (log != null)
            {
                log(line);
            }
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            string one = text.Replace("\r", " ").Replace("\n", " ");
            return one.Length > 240 ? one.Substring(0, 240) : one;
        }

        public static HashSet<int> ProbeResponding(List<HotspotCandidate> candidates)
        {
            var responded = new HashSet<int>();
            if (candidates == null || candidates.Count == 0)
            {
                return responded;
            }
            var options = new ParallelOptions();
            options.MaxDegreeOfParallelism = 64;
            Parallel.For(0, candidates.Count, options, delegate(int i)
            {
                HotspotCandidate candidate = candidates[i];
                // Cross-segment ARP would only ever resolve the default gateway, so ping is
                // the only honest probe here: an answer means that gateway address exists
                // somewhere reachable and must not be reused as our hotspot segment.
                if (Scanner.ProbePing(candidate.Gateway, 600) == IPStatus.Success)
                {
                    lock (responded)
                    {
                        responded.Add(candidate.ThirdOctet);
                    }
                }
            });
            return responded;
        }

        public static HotspotAdvice Advise(List<AdapterInfo> adapters, string currentGateway)
        {
            var advice = new HotspotAdvice();
            HashSet<int> used = HotspotAdvisor.UsedThirdOctets(adapters);
            foreach (int octet in used)
            {
                advice.Used.Add(octet);
            }
            advice.Used.Sort();
            int current = HotspotAdvisor.OctetFromGateway(currentGateway);
            if (current < 0)
            {
                current = HotspotAdvisor.ThirdOctetOf(currentGateway);
            }
            advice.CurrentOctet = current;

            List<HotspotCandidate> all = HotspotAdvisor.Rank(used, null, current, int.MaxValue);
            advice.ProbedCount = all.Count;
            HashSet<int> responded = ProbeResponding(all);
            foreach (int octet in responded)
            {
                advice.Responded.Add(octet);
            }
            advice.Responded.Sort();
            advice.Picked = HotspotAdvisor.Rank(used, responded, current, 5);
            if (advice.Picked.Count == 0)
            {
                advice.Note = "探测完 " + all.Count + " 个候选段后一个都不剩（本机有线与热点之间没有可用段），"
                    + "这次不给建议，请人工确认网络规划。";
                return advice;
            }
            advice.Note = "已探测 " + all.Count + " 个候选段的网关位，其中 " + responded.Count
                + " 个有人应答、已剔除；剩下 " + advice.Picked.Count + " 个建议是「探测无应答」——"
                + "跨段探测要走上游路由，不回也可能是被过滤，属弱证据，落段前再确认一次。";
            return advice;
        }

        private static string ToText(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
