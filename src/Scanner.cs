using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace ZiceLan
{
    public class ScanResult
    {
        public List<int> Free = new List<int>();
        public List<int> Used = new List<int>();
        public List<int> Unknown = new List<int>();
        public int Candidates;
        public int NotScanned;
        public long PingMs;
        public long ArpMs;
        public bool StoppedEarly;
        public string Note = string.Empty;
        public long ElapsedMs;
    }

    public static class Picker
    {
        public static int PickRandom(List<int> pool, int avoidOctet, Random rng)
        {
            if (pool == null || pool.Count == 0)
            {
                return -1;
            }
            var options = new List<int>();
            foreach (int octet in pool)
            {
                if (octet != avoidOctet)
                {
                    options.Add(octet);
                }
            }
            if (options.Count == 0)
            {
                return -1;
            }
            return options[rng.Next(options.Count)];
        }
    }

    public static class Scanner
    {
        public const int PingTimeoutMs = 250;
        public const int MaxParallel = 64;
        public const int DeadlineMs = 120000;

        private const int CodePending = 0;
        private const int CodePingSuccess = 1;
        private const int CodePingNoAnswer = 2;
        private const int CodePingLocalFailure = 3;
        private const int CodeCachedUsed = 4;
        private const int CodeSelf = 5;
        private const int CodeSkipped = 6;
        private const int CodeArpSkipped = 7;

        public static ScanResult Run(string basePrefix, int reserveFrom, int reserveTo, string sourceIp,
                                    string ownIp, string arpTableText, Action<string, int, int> progress, Func<bool> cancel)
        {
            var result = new ScanResult();
            List<int> candidates = IpCore.BuildCandidates(1, 254, reserveFrom, reserveTo);
            result.Candidates = candidates.Count;
            int total = candidates.Count;
            if (total == 0)
            {
                return result;
            }

            HashSet<string> cached = ArpProbe.ParseTable(arpTableText, basePrefix, ownIp);
            var cacheUsedOctets = new HashSet<int>();
            foreach (string ip in cached)
            {
                cacheUsedOctets.Add(IpCore.OctetOf(ip));
            }
            int selfOctet = IpCore.OctetOf(ownIp);

            var code = new int[total];
            var pingStatus = new IPStatus[total];
            var states = new HostState[total];
            int done = 0;
            int stoppedEarly = 0;
            int startTicks = Environment.TickCount;

            var options = new ParallelOptions();
            options.MaxDegreeOfParallelism = MaxParallel;
            Parallel.For(0, total, options, delegate(int i)
            {
                int octet = candidates[i];
                if (Environment.TickCount - startTicks > DeadlineMs || (cancel != null && cancel()))
                {
                    code[i] = CodeSkipped;
                    Interlocked.CompareExchange(ref stoppedEarly, 1, 0);
                }
                else if (octet == selfOctet)
                {
                    code[i] = CodeSelf;
                    states[i] = HostState.Used;
                }
                else if (cacheUsedOctets.Contains(octet))
                {
                    code[i] = CodeCachedUsed;
                    states[i] = HostState.Used;
                }
                else
                {
                    IPStatus status = ProbePing(IpCore.Combine(basePrefix, octet));
                    pingStatus[i] = status;
                    code[i] = status == IPStatus.Success
                        ? CodePingSuccess
                        : (ArpProbe.IsLocalFailure(status) ? CodePingLocalFailure : CodePingNoAnswer);
                    if (code[i] != CodePingNoAnswer)
                    {
                        states[i] = ArpProbe.Classify(status, false);
                    }
                }
                int tick = Interlocked.Increment(ref done);
                if (progress != null && (tick % 4 == 0 || tick == total))
                {
                    progress("ping 扫描", tick, total);
                }
            });
            int arpStart = Environment.TickCount;

            var arpPhase = new List<int>();
            for (int i = 0; i < total; i++)
            {
                if (code[i] == CodePingNoAnswer || code[i] == CodePingLocalFailure)
                {
                    arpPhase.Add(i);
                }
            }

            int arpDone = 0;
            int arpTotal = arpPhase.Count;
            Parallel.For(0, arpTotal, options, delegate(int k)
            {
                int i = arpPhase[k];
                if (Environment.TickCount - startTicks > DeadlineMs || (cancel != null && cancel()))
                {
                    code[i] = CodeArpSkipped;
                    Interlocked.CompareExchange(ref stoppedEarly, 1, 0);
                }
                else
                {
                    string ip = IpCore.Combine(basePrefix, candidates[i]);
                    states[i] = ArpProbe.Classify(pingStatus[i], ArpProbe.Responds(ip, sourceIp));
                }
                int tick = Interlocked.Increment(ref arpDone);
                if (progress != null && (tick % 4 == 0 || tick == arpTotal))
                {
                    progress("ARP 复核", tick, arpTotal);
                }
            });
            result.PingMs = arpStart - startTicks;
            result.ArpMs = Environment.TickCount - arpStart;

            for (int i = 0; i < total; i++)
            {
                if (code[i] == CodeSkipped || code[i] == CodeArpSkipped)
                {
                    result.NotScanned++;
                    continue;
                }
                HostState state = states[i];
                int octet = candidates[i];
                if (state == HostState.Free)
                {
                    result.Free.Add(octet);
                }
                else if (state == HostState.Used)
                {
                    result.Used.Add(octet);
                }
                else
                {
                    result.Unknown.Add(octet);
                }
            }

            result.StoppedEarly = stoppedEarly != 0;
            result.ElapsedMs = Environment.TickCount - startTicks;
            var notes = new List<string>();
            if (result.Used.Count == 0 && cacheUsedOctets.Count == 0)
            {
                notes.Add("整个网段没有任何主机响应，可能网线未插或本机不在该网段，本次空闲结论不可信。");
            }
            if (result.Unknown.Count > 0)
            {
                notes.Add("有 " + result.Unknown.Count + " 个地址探测状态不明，已从空闲列表中排除。");
            }
            if (result.StoppedEarly)
            {
                notes.Add("扫描超时提前结束，未完成的 " + result.NotScanned + " 个地址未计入结果。");
            }
            result.Note = string.Join(" ", notes.ToArray());
            result.Free.Sort();
            result.Used.Sort();
            result.Unknown.Sort();
            return result;
        }

        public static IPStatus ProbePing(string ip)
        {
            return ProbePing(ip, PingTimeoutMs);
        }

        public static IPStatus ProbePing(string ip, int timeoutMs)
        {
            try
            {
                using (var ping = new Ping())
                {
                    PingReply reply = ping.Send(ip, timeoutMs);
                    return reply.Status;
                }
            }
            catch (PingException)
            {
                return IPStatus.Unknown;
            }
            catch (Exception)
            {
                return IPStatus.Unknown;
            }
        }
    }
}
