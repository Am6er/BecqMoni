# П141 (23.09.2026) — ЧТО С ПРОГОНОМ: жив ли отсоединённый процесс, какие шаги закрыты,
# какими кодами, появились ли выходы и сводки. Ничего не запускает и не правит.
#
#   & 'handover\p141-corpus\status_p141.ps1'
#   & 'handover\p141-corpus\status_p141.ps1' -Tail 25    # плюс хвосты всех логов
#
# Образец — `handover\p140-night\status_p140.ps1`.
param([int]$Tail = 0, [string]$Lane = 'D:\BqMoni_Claude\p141')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$logs = "$Lane\logs"
$art  = "$Lane\art"

$launch = "$Lane\corpus_p141.cmd.launch.txt"
if (Test-Path $launch) {
    Get-Content $launch | ForEach-Object { "  $_" }
    $m = (Select-String -Path $launch -Pattern 'PID (\d+)').Matches
    if ($m.Count) {
        $procId = [int]$m[0].Groups[1].Value
        $p = Get-Process -Id $procId -ErrorAction SilentlyContinue
        if ($p) { Write-Host "ПРОЦЕСС $procId ЖИВ (старт $($p.StartTime))" -ForegroundColor Green }
        else    { Write-Host "ПРОЦЕССА $procId НЕТ — цепочка либо дошла, либо оборвана" -ForegroundColor Yellow }
    }
} else { Write-Host "нет $launch — пуска не было" -ForegroundColor Red }

''
'ШАГИ ЦЕПОЧКИ (код 0 — шаг прошёл; расшифровка кода: tools\CORPUS\scripts\detached_run.ps1 -Decode <код>)'
foreach ($n in @('1_wd', '2_mini', '3_full', '4_cmp')) {
    $f = "$logs\${n}_done.txt"
    if (Test-Path $f) { '  {0,-8} {1}' -f $n, ((Get-Content $f -Raw).Trim()) }
    else              { '  {0,-8} ещё не закрыт' -f $n }
}
foreach ($f in @('ALL_DONE.txt', 'CHAIN_FAILED.txt', '_finish.txt')) {
    if (Test-Path "$logs\$f") { '  {0,-18} {1}' -f $f, ((Get-Content "$logs\$f" -Raw).Trim()) }
}

''
'ВЫХОДЫ'
foreach ($d in @("$Lane\out_p141_mini", "$Lane\out_p141_full")) {
    if (Test-Path $d) {
        $csv = @(Get-ChildItem "$d\*_spline_runs.csv" -ErrorAction SilentlyContinue).Count
        $stamp = if (Test-Path "$d\.run.json") { 'клеймо .run.json есть' } else { 'КЛЕЙМА НЕТ (прогон не дошёл)' }
        '  {0}: групп {1}, {2}' -f (Split-Path -Leaf $d), $csv, $stamp
    } else { '  {0}: каталога нет' -f (Split-Path -Leaf $d) }
}

''
'СВОДКИ (их и читать приёмкой)'
foreach ($f in @('cmp_declared.txt', 'score_full_known.txt', 'score_full_unknown.txt',
                 'score_mini_known.txt', 'score_mini_unknown.txt',
                 'ladder_full_known.txt', 'ladder_mini_known.txt',
                 'ladder_summary_full.txt', 'ladder_summary_mini.txt')) {
    if (Test-Path "$art\$f") { '  {0,-26} {1,8} байт' -f $f, (Get-Item "$art\$f").Length }
    else                     { '  {0,-26} НЕТ' -f $f }
}

if (Test-Path "$art\arm_codes.txt") { ''; 'arm_codes.txt:'; Get-Content "$art\arm_codes.txt" | ForEach-Object { "  $_" } }

if ($Tail -gt 0) {
    foreach ($f in (Get-ChildItem "$logs\*.log" -ErrorAction SilentlyContinue)) {
        ''; "=== $($f.Name) (последние $Tail) ==="; Get-Content $f.FullName -Tail $Tail
    }
}
