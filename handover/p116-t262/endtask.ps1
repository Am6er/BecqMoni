# П116 — F4: завершить консольный процесс ТАК, как это делает Windows при выходе из системы / выключении
# (winlogon → csrss → EndTask по окну консоли, как «Снять задачу» диспетчера). Ищет окно ConsoleWindowClass,
# приписанное КЛИЕНТУ консоли (cmd; GetWindowThreadProcessId отдаёт PID клиента, не conhost — измерено 21.09.2026), и зовёт user32!EndTask(hwnd, FALSE, TRUE).
param([Parameter(Mandatory = $true)][int]$ClientPid)
$ErrorActionPreference = 'Stop'
Add-Type -Namespace P116 -Name U32 -MemberDefinition @'
public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
[DllImport("user32.dll", SetLastError = true)] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder sb, int max);
[DllImport("user32.dll", SetLastError = true)] public static extern bool EndTask(IntPtr hWnd, bool fShutDown, bool fForce);
'@
$found = [System.Collections.Generic.List[IntPtr]]::new()
$cb = [P116.U32+EnumProc]{
    param($h, $l)
    $p = 0
    [void][P116.U32]::GetWindowThreadProcessId($h, [ref]$p)
    if ($p -eq $ClientPid) {
        $sb = New-Object System.Text.StringBuilder 256
        [void][P116.U32]::GetClassName($h, $sb, 256)
        if ($sb.ToString() -eq 'ConsoleWindowClass') { $found.Add($h) }
    }
    return $true
}
[void][P116.U32]::EnumWindows($cb, [IntPtr]::Zero)
if ($found.Count -eq 0) { "окно консоли процесса $ClientPid не найдено"; exit 2 }
foreach ($h in $found) {
    $ok = [P116.U32]::EndTask($h, $false, $true)
    'EndTask(hwnd 0x{0:x}, fShutDown=0, fForce=1) -> {1}, GetLastError {2}' -f $h.ToInt64(), $ok, [Runtime.InteropServices.Marshal]::GetLastWin32Error()
}
