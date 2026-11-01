using System;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace ZapretOBKHOD
{
    internal static class AutostartTask
    {
        private const string TaskName = "ZapretOBKHOD";

        public static bool IsEnabled()
        {
            string output;
            int code = ProcessRunner.Run("schtasks.exe", "/Query /TN \"" + TaskName + "\"", out output);
            return code == 0;
        }

        public static bool SetEnabled(bool enabled, out string error)
        {
            error = null;
            if (!enabled)
            {
                string output;
                int code = ProcessRunner.Run("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F", out output);
                if (code != 0 && output.IndexOf("не найдена", StringComparison.OrdinalIgnoreCase) < 0 &&
                    output.IndexOf("cannot find", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    error = output;
                    return false;
                }
                return true;
            }

            try
            {
                AppPaths.EnsureDataDirs();
                string xmlPath = Path.Combine(AppPaths.DataDir, "autostart-task.xml");
                string user = WindowsIdentity.GetCurrent().Name;
                string command = Application.ExecutablePath;

                string xml =
                    "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                    "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                    "  <RegistrationInfo>\r\n" +
                    "    <Author>" + Escape(user) + "</Author>\r\n" +
                    "    <Description>ZapretOBKHOD — локальный обход DPI без VPN</Description>\r\n" +
                    "  </RegistrationInfo>\r\n" +
                    "  <Triggers>\r\n" +
                    "    <LogonTrigger>\r\n" +
                    "      <Enabled>true</Enabled>\r\n" +
                    "      <UserId>" + Escape(user) + "</UserId>\r\n" +
                    "      <Delay>PT10S</Delay>\r\n" +
                    "    </LogonTrigger>\r\n" +
                    "  </Triggers>\r\n" +
                    "  <Principals>\r\n" +
                    "    <Principal id=\"Author\">\r\n" +
                    "      <UserId>" + Escape(user) + "</UserId>\r\n" +
                    "      <LogonType>InteractiveToken</LogonType>\r\n" +
                    "      <RunLevel>HighestAvailable</RunLevel>\r\n" +
                    "    </Principal>\r\n" +
                    "  </Principals>\r\n" +
                    "  <Settings>\r\n" +
                    "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                    "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                    "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                    "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                    "    <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
                    "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
                    "    <IdleSettings>\r\n" +
                    "      <StopOnIdleEnd>false</StopOnIdleEnd>\r\n" +
                    "      <RestartOnIdle>false</RestartOnIdle>\r\n" +
                    "    </IdleSettings>\r\n" +
                    "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                    "    <Enabled>true</Enabled>\r\n" +
                    "    <Hidden>false</Hidden>\r\n" +
                    "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
                    "    <WakeToRun>false</WakeToRun>\r\n" +
                    "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                    "    <Priority>7</Priority>\r\n" +
                    "  </Settings>\r\n" +
                    "  <Actions Context=\"Author\">\r\n" +
                    "    <Exec>\r\n" +
                    "      <Command>" + Escape(command) + "</Command>\r\n" +
                    "      <Arguments>--autostart --minimized</Arguments>\r\n" +
                    "    </Exec>\r\n" +
                    "  </Actions>\r\n" +
                    "</Task>\r\n";

                File.WriteAllText(xmlPath, xml, Encoding.Unicode);

                string output;
                int code = ProcessRunner.Run("schtasks.exe",
                    "/Create /TN \"" + TaskName + "\" /XML \"" + xmlPath + "\" /F", out output);
                if (code != 0)
                {
                    error = string.IsNullOrEmpty(output)
                        ? "Не удалось создать задачу автозапуска (код " + code + ")."
                        : output;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string Escape(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                        .Replace("\"", "&quot;");
        }
    }
}
