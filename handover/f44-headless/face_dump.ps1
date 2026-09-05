# ВНЕШНЕЕ сличение ВИДА двух сообщений одного процесса: подпись окна,
# все дочерние управляющие (класс + надпись), размер, наличие знака.
# Читает окна ИЗВНЕ через `EnumWindows`, то есть не полагается ни на что
# внутри пробы.
param(
    [Parameter(Mandatory=$true)][string]$Exe,
    [Parameter(Mandatory=$true)][string]$Log,
    [int]$WaitSec = 3
)

Add-Type @'
using System;using System.Text;using System.Runtime.InteropServices;using System.Collections.Generic;
public class WinFace{
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc f, IntPtr l);
 [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
 [StructLayout(LayoutKind.Sequential)] public struct RECT{ public int L,T,R,B; }
 delegate bool EnumProc(IntPtr h, IntPtr l);
 static List<string> res; static int target;
 static string Txt(IntPtr h){ var s=new StringBuilder(1024); GetWindowTextW(h,s,1024); return s.ToString(); }
 static string Cls(IntPtr h){ var s=new StringBuilder(256); GetClassNameW(h,s,256); return s.ToString(); }
 static bool Cb(IntPtr h, IntPtr l){
   int pid; GetWindowThreadProcessId(h, out pid);
   if(pid==target && Cls(h)=="#32770"){
     RECT r; GetClientRect(h, out r);
     var acc=new StringBuilder();
     acc.Append("ОКНО HWND=").Append(h.ToInt64())
        .Append("  класс=#32770  подпись=[").Append(Txt(h)).Append("]")
        .Append("  клиент=").Append(r.R-r.L).Append("x").Append(r.B-r.T).Append("\n");
     var kids=new List<string>();
     EnumChildWindows(h, delegate(IntPtr ch, IntPtr cl){
       kids.Add("    дочернее класс=" + Cls(ch) + "  надпись=[" + Txt(ch) + "]");
       return true;
     }, IntPtr.Zero);
     kids.Sort(StringComparer.Ordinal);
     foreach(var k in kids) acc.Append(k).Append("\n");
     res.Add(acc.ToString().TrimEnd('\n'));
   }
   return true;
 }
 public static string[] Dialogs(int pid){ res=new List<string>(); target=pid; EnumWindows(Cb, IntPtr.Zero); return res.ToArray(); }
}
'@ -ErrorAction SilentlyContinue

$err = $Log + '.err'
$p = Start-Process -FilePath $Exe -ArgumentList '--face' -RedirectStandardOutput $Log -RedirectStandardError $err -NoNewWindow -PassThru
Start-Sleep -Seconds $WaitSec
$dlgs = [WinFace]::Dialogs($p.Id)
Write-Output ("окон #32770 найдено: " + $dlgs.Count)
$i = 0
foreach ($d in $dlgs) {
    $i++
    Write-Output ("── №$i (порядок перечисления: сверху вниз по Z) ──")
    Write-Output $d
}
if (-not $p.HasExited) { $p.Kill(); $p.WaitForExit() }
Write-Output ("итог: код=" + $p.ExitCode)
if ($dlgs.Count -eq 2) {
    $a = ($dlgs[0] -split "`n" | Select-Object -Skip 1) -join "`n"
    $b = ($dlgs[1] -split "`n" | Select-Object -Skip 1) -join "`n"
    $ta = ([regex]::Match($dlgs[0],'подпись=\[(.*?)\]  клиент=(\S+)'))
    $tb = ([regex]::Match($dlgs[1],'подпись=\[(.*?)\]  клиент=(\S+)'))
    Write-Output ""
    Write-Output ("подпись окна:   «" + $ta.Groups[1].Value + "» против «" + $tb.Groups[1].Value + "» — " +
                  $(if ($ta.Groups[1].Value -eq $tb.Groups[1].Value) {'СОШЛАСЬ'} else {'РАЗОШЛАСЬ'}))
    Write-Output ("размер клиента: " + $ta.Groups[2].Value + " против " + $tb.Groups[2].Value + " — " +
                  $(if ($ta.Groups[2].Value -eq $tb.Groups[2].Value) {'СОШЁЛСЯ'} else {'РАЗОШЁЛСЯ'}))
    Write-Output ("состав дочерних (класс+надпись, по порядку): " +
                  $(if ($a -eq $b) {'СОШЁЛСЯ ПОБУКВЕННО'} else {'РАЗОШЁЛСЯ'}))
} else {
    Write-Output "⛔ ожидалось РОВНО два окна — сличение не состоялось."
}
