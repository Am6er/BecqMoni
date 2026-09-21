# П116 (T262) — печатает, в каком Job Object сидит ВЫЗЫВАЮЩИЙ процесс и с какими пределами.
# Запуск: pwsh -NoProfile -File D:\BqMoni_Claude\p116\jobinfo.ps1 [метка]
# Печатает одну строку: метка | pid | родитель | inJob | LimitFlags (hex + имена) | процессов в job | PID-ы job.
param([string]$Tag = '')
$ErrorActionPreference = 'Stop'
Add-Type -Namespace P116 -Name Job -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool IsProcessInJob(IntPtr hProcess, IntPtr hJob, out bool result);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool QueryInformationJobObject(IntPtr hJob, int infoClass, byte[] info, int len, out int retLen);
[DllImport("kernel32.dll")]
public static extern IntPtr GetCurrentProcess();
'@
$inJob = $false
[void][P116.Job]::IsProcessInJob([P116.Job]::GetCurrentProcess(), [IntPtr]::Zero, [ref]$inJob)
$me = Get-CimInstance Win32_Process -Filter "ProcessId=$pid"
$flagsTxt = '-'
$count = '-'
$pids = '-'
if ($inJob) {
    # JobObjectExtendedLimitInformation = 9; LimitFlags — DWORD со смещением 16 в BasicLimitInformation
    $buf = New-Object byte[] 144
    $ret = 0
    if ([P116.Job]::QueryInformationJobObject([IntPtr]::Zero, 9, $buf, 144, [ref]$ret)) {
        $flags = [BitConverter]::ToUInt32($buf, 16)
        $names = @()
        if ($flags -band 0x400)  { $names += 'DIE_ON_UNHANDLED_EXCEPTION' }
        if ($flags -band 0x800)  { $names += 'BREAKAWAY_OK' }
        if ($flags -band 0x1000) { $names += 'SILENT_BREAKAWAY_OK' }
        if ($flags -band 0x2000) { $names += 'KILL_ON_JOB_CLOSE' }
        $flagsTxt = ('0x{0:x} [{1}]' -f $flags, ($names -join ','))
    } else {
        $flagsTxt = ('QueryInformationJobObject(9) отказ ' + [Runtime.InteropServices.Marshal]::GetLastWin32Error())
    }
    # JobObjectBasicProcessIdList = 3: NumberOfAssignedProcesses, NumberOfProcessIdsInList, ProcessIdList[]
    $buf2 = New-Object byte[] 8200
    if ([P116.Job]::QueryInformationJobObject([IntPtr]::Zero, 3, $buf2, $buf2.Length, [ref]$ret)) {
        $n = [BitConverter]::ToUInt32($buf2, 0)
        $m = [BitConverter]::ToUInt32($buf2, 4)
        $count = "$n"
        $list = @()
        for ($i = 0; $i -lt $m; $i++) {
            $p = [BitConverter]::ToUInt64($buf2, 8 + 8 * $i)
            $nm = (Get-Process -Id $p -ErrorAction SilentlyContinue).ProcessName
            $list += ('{0}:{1}' -f $p, ($nm ?? '?'))
        }
        $pids = $list -join ' '
    } else {
        $count = ('QueryInformationJobObject(3) отказ ' + [Runtime.InteropServices.Marshal]::GetLastWin32Error())
    }
}
'{0} | pid {1} ({2}) | parent {3} | inJob {4} | flags {5} | procs {6} | {7}' -f $Tag, $pid, $me.Name, $me.ParentProcessId, $inJob, $flagsTxt, $count, $pids
