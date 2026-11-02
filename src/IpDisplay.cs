using System;

namespace ZiceLan
{
    public static class IpDisplay
    {
        public const string NoIpLine = "本机 IP：没有（网线未插或未获取到地址）";

        public static string Line(AdapterInfo info)
        {
            if (info == null || !IpCore.IsValidIpv4(info.Ip))
            {
                return NoIpLine;
            }
            string text = "本机 IP：" + info.Ip;
            if (info.Mask.Length > 0)
            {
                text = text + "　掩码 " + info.Mask;
            }
            if (info.Gateway.Length > 0)
            {
                text = text + "　网关 " + info.Gateway;
            }
            return text;
        }
    }
}
