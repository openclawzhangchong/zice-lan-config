using System;
using System.Diagnostics;
using System.Text;

namespace ZiceLan
{
    public static class Browser
    {
        public static string NormalizeUrl(string gateway)
        {
            if (!IpCore.IsValidIpv4(gateway))
            {
                return string.Empty;
            }
            return "http://" + gateway;
        }

        public static bool Open(string gateway, out string error)
        {
            error = string.Empty;
            string url = NormalizeUrl(gateway);
            if (url.Length == 0)
            {
                error = "网关地址不对，打不开管理页：" + gateway;
                return false;
            }
            try
            {
                var startInfo = new ProcessStartInfo(url);
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
