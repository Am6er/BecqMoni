# П116 — E3: свой Job Object, чтобы ЭМУЛИРОВАТЬ гибель job'а приложения (Claude Desktop — пакет MSIX, все его
# процессы сидят в одном job; TerminateJobObject по нему — «Снять задачу» диспетчера / обновление пакета).
# Точечный вызов (dot-source) в канале: . .\jobctl.ps1 ; потом New-P116Job, Add-P116JobProcess, Kill-P116Job, Close-P116Job.
Add-Type -Namespace P116 -Name JobCtl -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern IntPtr CreateJobObject(IntPtr attrs, string name);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, byte[] info, int len);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool TerminateJobObject(IntPtr hJob, uint exitCode);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool CloseHandle(IntPtr h);
[DllImport("kernel32.dll", SetLastError = true)]
public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
'@
function New-P116Job([uint32]$Flags = 0x2000) {
    # JOBOBJECT_EXTENDED_LIMIT_INFORMATION — 144 байта на x64, LimitFlags со смещением 16
    $h = [P116.JobCtl]::CreateJobObject([IntPtr]::Zero, $null)
    if ($h -eq [IntPtr]::Zero) { throw ('CreateJobObject: ' + [Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
    $buf = New-Object byte[] 144
    [BitConverter]::GetBytes($Flags).CopyTo($buf, 16)
    if (-not [P116.JobCtl]::SetInformationJobObject($h, 9, $buf, 144)) { throw ('SetInformationJobObject: ' + [Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
    Write-Host ('job 0x{0:x} flags 0x{1:x}' -f $h.ToInt64(), $Flags)
    return $h
}
function Add-P116JobProcess([IntPtr]$Job, [int]$ProcId) {
    # PROCESS_SET_QUOTA (0x100) | PROCESS_TERMINATE (0x1)
    $hp = [P116.JobCtl]::OpenProcess(0x101, $false, $ProcId)
    if ($hp -eq [IntPtr]::Zero) { throw ('OpenProcess: ' + [Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
    $ok = [P116.JobCtl]::AssignProcessToJobObject($Job, $hp)
    $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    [void][P116.JobCtl]::CloseHandle($hp)
    'AssignProcessToJobObject({0}) -> {1}{2}' -f $ProcId, $ok, ($ok ? '' : " (ошибка $err)")
}
function Kill-P116Job([IntPtr]$Job, [uint32]$Code = 77) {
    'TerminateJobObject(code {0}) -> {1}' -f $Code, [P116.JobCtl]::TerminateJobObject($Job, $Code)
}
function Close-P116Job([IntPtr]$Job) {
    'CloseHandle(job) -> {0}' -f [P116.JobCtl]::CloseHandle($Job)
}
