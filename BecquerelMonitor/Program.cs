using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace BecquerelMonitor
{

    static class Program
    {

        [STAThread]
        static void Main(string[] args)
        {
            Process currentProcess = Process.GetCurrentProcess();
            Process[] processes = Process.GetProcessesByName(currentProcess.ProcessName);

            foreach (Process process in processes) {
                if (process.Id != currentProcess.Id && process.MainModule.FileName.Equals(currentProcess.MainModule.FileName)) {
                    MessageBox.Show(Properties.Resources.ERRAppAllreadyRunning, 
                        Properties.Resources.ErrorExclamation, 
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }
}
