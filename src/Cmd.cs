using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace ZiceLan
{
    public class CmdResult
    {
        public bool TimedOut;
        public int ExitCode = -1;
        public string Output = string.Empty;
        public string Error = string.Empty;

        public string Text
        {
            get
            {
                string combined = (Output ?? string.Empty).Trim();
                string err = (Error ?? string.Empty).Trim();
                if (err.Length == 0)
                {
                    return combined;
                }
                return combined.Length == 0 ? err : combined + " | " + err;
            }
        }
    }

    public static class Cmd
    {
        public static CmdResult Run(string fileName, string arguments, int timeoutMs)
        {
            var result = new CmdResult();
            var startInfo = new ProcessStartInfo(fileName, arguments);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = Encoding.Default;
            startInfo.StandardErrorEncoding = Encoding.Default;
            Process process = null;
            try
            {
                process = Process.Start(startInfo);
                Task<string> outTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errTask = process.StandardError.ReadToEndAsync();
                if (process.WaitForExit(timeoutMs))
                {
                    result.ExitCode = process.ExitCode;
                }
                else
                {
                    result.TimedOut = true;
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {
                    }
                }
                outTask.Wait(2000);
                errTask.Wait(2000);
                result.Output = outTask.IsCompleted ? outTask.Result : string.Empty;
                result.Error = errTask.IsCompleted ? errTask.Result : string.Empty;
                if (result.TimedOut)
                {
                    result.Error = string.Empty;
                    result.Output = string.Empty;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.Output = string.Empty;
            }
            finally
            {
                if (process != null)
                {
                    process.Dispose();
                }
            }
            return result;
        }
    }
}
