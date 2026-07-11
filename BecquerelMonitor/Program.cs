using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace BecquerelMonitor
{

    static class Program
    {

        [STAThread]
        static void Main(string[] args)
        {
            // Single-instance check via a named mutex. The old implementation walked the
            // process list and read process.MainModule.FileName without try/catch, which
            // throws Win32Exception for same-named processes with a different integrity
            // level or bitness and crashed before the UI appeared (and was not atomic).
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string mutexName = "Local\\BecqMoni_" + exePath.Replace('\\', '_').Replace(':', '_').ToLowerInvariant();
            bool createdNew;
            using (Mutex mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(Properties.Resources.ERRAppAllreadyRunning,
                        Properties.Resources.ErrorExclamation,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                Environment.CurrentDirectory = Path.GetDirectoryName(exePath);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args));
                GC.KeepAlive(mutex);
            }
        }
    }
}
