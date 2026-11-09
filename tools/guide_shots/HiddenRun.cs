// Запускатель на СКРЫТОМ рабочем столе: CreateDesktop + CreateProcess с lpDesktop.
// Перенесён в дерево 08.11.2026 из полосы a318 (`AMBER319`, конвейер снимков гида): окна
// потомка живут на своём столе и на экран человека не выходят.
//   HiddenRun.exe <срок, с> <exe> [аргументы…]   — код возврата потомка; 124 — срок вышел (потомок снят).
//   HiddenRun.exe --self-test                    — потомок cmd /c exit 7 обязан вернуть 7.
// Потоки вывода потомка — те же, что у запускателя (перенаправление снаружи).
using System;
using System.Runtime.InteropServices;
using System.Text;

static class HiddenRun
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr sa);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int which);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
                                     IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    static string Quote(string a)
    {
        return a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\"";
    }

    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
        {
            args = new[] { "30", Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\cmd.exe"), "/c", "exit", "7" };
            int code = Run(args);
            Console.WriteLine("self-test: потомок вернул " + code + (code == 7 ? " — верно" : " — НЕВЕРНО"));
            return code == 7 ? 0 : 1;
        }

        if (args.Length < 2)
        {
            Console.Error.WriteLine("HiddenRun.exe <срок, с> <exe> [аргументы…]");
            return 2;
        }

        return Run(args);
    }

    static int Run(string[] args)
    {
        int seconds = int.Parse(args[0]);
        var cmd = new StringBuilder();
        for (int i = 1; i < args.Length; i++) cmd.Append(i > 1 ? " " : "").Append(Quote(args[i]));
        string name = "guideshot_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        IntPtr desk = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000u, IntPtr.Zero);
        if (desk == IntPtr.Zero) { Console.Error.WriteLine("CreateDesktop: " + Marshal.GetLastWin32Error()); return 3; }
        var si = new STARTUPINFO
        {
            cb = Marshal.SizeOf(typeof(STARTUPINFO)), lpDesktop = name, dwFlags = 0x100,
            hStdInput = GetStdHandle(-10), hStdOutput = GetStdHandle(-11), hStdError = GetStdHandle(-12)
        };
        PROCESS_INFORMATION pi;
        if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, true, 0, IntPtr.Zero, null, ref si, out pi))
        {
            Console.Error.WriteLine("CreateProcess: " + Marshal.GetLastWin32Error());
            CloseDesktop(desk);
            return 3;
        }

        uint wait = WaitForSingleObject(pi.hProcess, (uint)seconds * 1000u);
        uint exit;
        if (wait != 0)
        {
            TerminateProcess(pi.hProcess, 124);
            WaitForSingleObject(pi.hProcess, 10000);
            exit = 124;
        }
        else
        {
            GetExitCodeProcess(pi.hProcess, out exit);
        }

        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        CloseDesktop(desk);
        return (int)exit;
    }
}
