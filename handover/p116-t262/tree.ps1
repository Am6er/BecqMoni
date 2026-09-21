# П116 — печатает процесс и его потомков (PID, имя, родитель, создан) — есть ли ещё нагрузка.
param([Parameter(Mandatory = $true)][int[]]$Pids)
$all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CreationDate, SessionId
function Show([int]$p, [int]$depth) {
    $w = $all | Where-Object ProcessId -eq $p
    if (-not $w) { '{0}{1} — НЕТ (мёртв)' -f ('  ' * $depth), $p; return }
    '{0}{1} {2} (родитель {3}, сеанс {4}, создан {5:HH:mm:ss})' -f ('  ' * $depth), $w.ProcessId, $w.Name, $w.ParentProcessId, $w.SessionId, $w.CreationDate
    foreach ($c in ($all | Where-Object ParentProcessId -eq $p)) { Show $c.ProcessId ($depth + 1) }
}
foreach ($p in $Pids) { Show $p 0 }
