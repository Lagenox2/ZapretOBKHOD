using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ZapretOBKHOD
{
    internal static class AppPaths
    {
        public static readonly string BaseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
        public static readonly string EngineDir = Path.Combine(BaseDir, "engine");
        public static readonly string EngineExe = Path.Combine(EngineDir, "winws.exe");
        public static readonly string FakeDir = Path.Combine(EngineDir, "files", "fake");
        public static readonly string ListsDir = Path.Combine(BaseDir, "lists");
        public static readonly string ExcludeList = Path.Combine(ListsDir, "exclude.txt");
        public static readonly string CustomList = Path.Combine(ListsDir, "custom.txt");
        public static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZapretOBKHOD");
        public static readonly string LogsDir = Path.Combine(DataDir, "logs");
        public static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");
        public static readonly string ActiveList = Path.Combine(DataDir, "active.txt");
        public static readonly string ActiveExcludeList = Path.Combine(DataDir, "active-exclude.txt");

        public static void EnsureDataDirs()
        {
            try
            {
                if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
                if (!Directory.Exists(LogsDir)) Directory.CreateDirectory(LogsDir);
            }
            catch
            {
            }
        }

        public static string ToEnginePath(string path)
        {
            return path.Replace('\\', '/');
        }
    }

    internal sealed class AppSettings
    {
        public string PresetId { get; set; }
        public bool Autostart { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool BlockQuic { get; set; }
        public bool ExcludeRu { get; set; }
        public bool AutoRestart { get; set; }

        // Версия настроек. Нужна, чтобы при обновлении включить новые
        // безопасные значения по умолчанию, не сбрасывая выбор пользователя.
        public int? SettingsVersion { get; set; }

        public AppSettings()
        {
            PresetId = "combo";
            Autostart = false;
            MinimizeToTray = true;
            BlockQuic = true;
            ExcludeRu = true;
            AutoRestart = true;
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    string json = File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8);
                    var serializer = new JavaScriptSerializer();
                    var loaded = serializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        if (string.IsNullOrEmpty(loaded.PresetId)) loaded.PresetId = "combo";

                        // Файл от старой версии: блокировка QUIC тогда была выключена,
                        // из-за чего браузеры и клиенты застревали на QUIC.
                        if (!loaded.SettingsVersion.HasValue)
                        {
                            loaded.BlockQuic = true;
                            loaded.SettingsVersion = 1;
                            Logger.Write("Настройки обновлены: блокировка QUIC включена по умолчанию.");
                        }

                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write("Не удалось прочитать настройки: " + ex.Message);
            }

            var fresh = new AppSettings();
            fresh.SettingsVersion = 1;
            return fresh;
        }

        public void Save()
        {
            try
            {
                if (!SettingsVersion.HasValue) SettingsVersion = 1;
                AppPaths.EnsureDataDirs();
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(this);
                File.WriteAllText(AppPaths.SettingsFile, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Logger.Write("Не удалось сохранить настройки: " + ex.Message);
            }
        }
    }

    internal static class Logger
    {
        private static readonly object Sync = new object();
        private static string _file;

        public static event Action<string> Message;

        public static string LogFile
        {
            get
            {
                if (_file == null)
                {
                    _file = Path.Combine(AppPaths.LogsDir,
                        "zapretobkhod-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                }
                return _file;
            }
        }

        public static void Init()
        {
            AppPaths.EnsureDataDirs();
            try
            {
                if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 3 * 1024 * 1024)
                {
                    File.Move(LogFile, LogFile + ".old");
                }
            }
            catch
            {
            }
        }

        public static void Write(string message)
        {
            if (message == null) return;
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
            lock (Sync)
            {
                try
                {
                    File.AppendAllText(LogFile, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch
                {
                }
            }
            Action<string> handler = Message;
            if (handler != null)
            {
                try { handler(line); }
                catch { }
            }
        }
    }

    internal static class Admin
    {
        public static bool IsElevated()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class ProcessRunner
    {
        public static int Run(string fileName, string arguments, out string output)
        {
            var sb = new StringBuilder();
            try
            {
                var psi = new ProcessStartInfo(fileName, arguments);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;

                using (var process = new Process())
                {
                    process.StartInfo = psi;
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            lock (sb) sb.AppendLine(e.Data);
                        }
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            lock (sb) sb.AppendLine(e.Data);
                        }
                    };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    lock (sb) output = sb.ToString().Trim();
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }
    }
}
