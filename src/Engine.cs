using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace ZapretOBKHOD
{
    internal sealed class Engine
    {
        private const string QuicRuleName = "ZapretOBKHOD_QUIC";
        private Process _process;
        private volatile bool _expectedExit;

        public event EventHandler ExitedUnexpected;

        public bool Running
        {
            get
            {
                try
                {
                    return _process != null && !_process.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        public int ProcessId
        {
            get
            {
                try { return _process == null ? 0 : _process.Id; }
                catch { return 0; }
            }
        }

        public bool Start(string arguments, bool blockQuic, out string error)
        {
            error = null;

            if (!File.Exists(AppPaths.EngineExe))
            {
                error = "Не найден движок:\r\n" + AppPaths.EngineExe +
                        "\r\n\r\nРаспакуйте архив ZapretOBKHOD полностью, вместе с папками engine и lists.";
                return false;
            }

            if (!Admin.IsElevated())
            {
                error = "Для работы WinDivert нужны права администратора.\r\nЗапустите ZapretOBKHOD от имени администратора.";
                return false;
            }

            StopInternal();

            RemoveQuicRule();
            if (blockQuic)
            {
                string fwError;
                if (!AddQuicRule(out fwError))
                {
                    Logger.Write("Не удалось включить блокировку QUIC: " + fwError);
                }
            }

            var psi = new ProcessStartInfo();
            psi.FileName = AppPaths.EngineExe;
            psi.Arguments = arguments;
            psi.WorkingDirectory = AppPaths.EngineDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.WindowStyle = ProcessWindowStyle.Hidden;

            try
            {
                _expectedExit = false;
                _process = new Process();
                _process.StartInfo = psi;
                _process.EnableRaisingEvents = true;
                _process.OutputDataReceived += OnOutput;
                _process.ErrorDataReceived += OnOutput;
                _process.Exited += OnExited;
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                error = "Не удалось запустить движок: " + ex.Message;
                StopInternal();
                return false;
            }

            Thread.Sleep(900);

            bool exited;
            int exitCode = 0;
            try
            {
                exited = _process.HasExited;
                if (exited) exitCode = _process.ExitCode;
            }
            catch
            {
                exited = false;
            }

            if (exited)
            {
                error = "Движок завершился сразу после запуска (код " + exitCode + ").\r\n\r\n" +
                        "Проверьте журнал. Частые причины:\r\n" +
                        "• антивирус блокирует WinDivert;\r\n" +
                        "• не установлены драйверы/обновления Windows;\r\n" +
                        "• конфликт с другим запущенным обходом (zapret, GoodbyeDPI).";
                StopInternal();
                return false;
            }

            Logger.Write("Движок запущен. PID " + ProcessId + ".");
            return true;
        }

        public void Stop()
        {
            StopInternal();
        }

        private void StopInternal()
        {
            _expectedExit = true;
            RemoveQuicRule();

            Process process = _process;
            _process = null;

            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                }
                catch
                {
                }
                try
                {
                    process.OutputDataReceived -= OnOutput;
                    process.ErrorDataReceived -= OnOutput;
                    process.Exited -= OnExited;
                    process.Dispose();
                }
                catch
                {
                }
            }

            KillStaleEngine();
        }

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null && e.Data.Length > 0)
            {
                Logger.Write("движок> " + e.Data);
            }
        }

        private void OnExited(object sender, EventArgs e)
        {
            if (_expectedExit) return;
            if (sender != _process) return;

            Logger.Write("Движок неожиданно завершился.");
            EventHandler handler = ExitedUnexpected;
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); }
                catch { }
            }
        }

        private static void KillStaleEngine()
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName("winws"); }
            catch { return; }

            foreach (Process p in processes)
            {
                try
                {
                    string path = p.MainModule.FileName;
                    if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(AppPaths.EngineExe),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill();
                        p.WaitForExit(2000);
                    }
                }
                catch
                {
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }

        public static void CleanupStaleFirewallRule()
        {
            RemoveQuicRule();
        }

        private static bool AddQuicRule(out string error)
        {
            string output;
            string args = "advfirewall firewall add rule name=\"" + QuicRuleName +
                          "\" dir=out action=block protocol=UDP remoteport=443 profile=any enable=yes";
            int code = ProcessRunner.Run("netsh.exe", args, out output);
            error = output;
            if (code == 0)
            {
                Logger.Write("QUIC заблокирован: Chrome/Edge/Firefox перейдут на TCP.");
                return true;
            }
            if (string.IsNullOrEmpty(error)) error = "netsh вернул код " + code;
            return false;
        }

        private static void RemoveQuicRule()
        {
            string output;
            ProcessRunner.Run("netsh.exe",
                "advfirewall firewall delete rule name=\"" + QuicRuleName + "\"", out output);
        }
    }
}
