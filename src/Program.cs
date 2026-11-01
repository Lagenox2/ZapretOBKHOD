using System;
using System.Threading;
using System.Windows.Forms;

namespace ZapretOBKHOD
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool isAutostart = false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--autostart", StringComparison.OrdinalIgnoreCase)) isAutostart = true;
            }

            bool created;
            using (var mutex = new Mutex(true, "ZapretOBKHOD_SingleInstance", out created))
            {
                if (!created)
                {
                    if (!isAutostart)
                    {
                        MessageBox.Show(
                            "ZapretOBKHOD уже запущена.\r\n\r\nОкно программы можно открыть из значка в трее.",
                            "ZapretOBKHOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Logger.Write("Необработанная ошибка: " + e.Exception);
                    MessageBox.Show("Произошла ошибка:\r\n" + e.Exception.Message,
                        "ZapretOBKHOD", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };

                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Logger.Write("Критическая ошибка: " + e.ExceptionObject);
                };

                try
                {
                    Application.Run(new MainForm(args));
                }
                catch (Exception ex)
                {
                    Logger.Write("Критическая ошибка запуска: " + ex);
                    MessageBox.Show("Не удалось запустить ZapretOBKHOD:\r\n" + ex.Message,
                        "ZapretOBKHOD", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
