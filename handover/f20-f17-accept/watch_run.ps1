# Наблюдатель прогона пробы: пишет, поднялось ли МОДАЛЬНОЕ окно (класс #32770),
# когда именно, с каким текстом, и чем кончился прогон.
# ⛔ Окно НЕ закрывается: замер должен показать поведение как есть.
param(
    [Parameter(Mandatory=$true)][string]$Exe,
    [Parameter(Mandatory=$true)][string]$Log,
    [int]$LimitSec = 600
)

Add-Type @'
using System;using System.Text;using System.Runtime.InteropServices;using System.Collections.Generic;
public class WinList{
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc f, IntPtr l);
 [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
 delegate bool EnumProc(IntPtr h, IntPtr l);
 static List<string> res; static int target;
 static bool Cb(IntPtr h, IntPtr l){
   int pid; GetWindowThreadProcessId(h, out pid);
   if(pid==target){
     var c=new StringBuilder(256); GetClassNameW(h,c,256);
     if(c.ToString()=="#32770"){
       int tid = GetWindowThreadProcessId(h, out pid);
       var acc=new StringBuilder(); acc.Append("HWND=").Append(h.ToInt64()).Append(" поток=").Append(tid).Append(" текст=[");
       EnumChildWindows(h, delegate(IntPtr ch, IntPtr cl){
         var cc=new StringBuilder(256); GetClassNameW(ch,cc,256);
         var ct=new StringBuilder(512); GetWindowTextW(ch,ct,512);
         if(cc.ToString()=="Static") acc.Append(ct.ToString()).Append(" ");
         return true;
       }, IntPtr.Zero);
       acc.Append("]");
       res.Add(acc.ToString());
     }
   }
   return true;
 }
 public static string[] Dialogs(int pid){ res=new List<string>(); target=pid; EnumWindows(Cb, IntPtr.Zero); return res.ToArray(); }
}
'@ -ErrorAction SilentlyContinue

$err = $Log + '.err'
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $Exe -RedirectStandardOutput $Log -RedirectStandardError $err -NoNewWindow -PassThru
Write-Output ("запущен pid=" + $p.Id + " exe=" + $Exe)
$seen = @{}
while (-not $p.HasExited -and $sw.Elapsed.TotalSeconds -lt $LimitSec) {
    foreach ($d in [WinList]::Dialogs($p.Id)) {
        if (-not $seen.ContainsKey($d)) {
            $seen[$d] = $true
            Write-Output ("  +{0,6:N1} с  МОДАЛЬНОЕ ОКНО  {1}" -f $sw.Elapsed.TotalSeconds, $d)
        }
    }
    Start-Sleep -Milliseconds 500
}
if (-not $p.HasExited) {
    Write-Output ("⛔ предел {0} с исчерпан, процесс жив — убиваю" -f $LimitSec)
    $p.Kill()
    $p.WaitForExit()
    Write-Output ("итог: УБИТ, секунд=" + [math]::Round($sw.Elapsed.TotalSeconds,1))
} else {
    Write-Output ("итог: код=" + $p.ExitCode + "  секунд=" + [math]::Round($sw.Elapsed.TotalSeconds,1) + "  окон поднято=" + $seen.Count)
}
