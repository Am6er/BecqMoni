# П161 (24.09.2026) — ход и приёмка ночного счёта физики 24 (rev34). ТОЛЬКО ЧТЕНИЕ.
# Образец — П140 `handover/p140-night/status_p140.ps1`.
#
#   & 'D:\BqMoni_Claude\p161\status.ps1'            # где счёт, жив ли, что уже готово
#   & 'D:\BqMoni_Claude\p161\status.ps1' -Tail 20   # плюс хвосты логов шагов
#
# Что печатает:
#   * PID из `night_rev34.cmd.launch.txt` и жив ли он (⛔ «нет процесса» + нет `ALL_DONE.txt`/
#     `CHAIN_PARTIAL.txt` = счёт ОБОРВАН; смотреть `*_done.txt` последнего шага и расшифровать код
#     `tools\CORPUS\scripts\detached_run.ps1 -Decode <код>`);
#   * какие шаги закрылись и с каким кодом (`logs\<N>_*_done.txt`);
#   * сколько матриц в складе полосы из 49, какие у них клейма (phys=…), сколько кривых в `store\response`;
#   * есть ли обрубки `.tmp` (прерванная запись матрицы); появились ли `out_rev34_*` и склад витрины.
param([int]$Tail = 0)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$lane  = 'D:\BqMoni_Claude\p161'
$wt    = 'D:\BqMoni_Claude\p147\wt'
$logs  = "$lane\logs"
$store = "$lane\store"

$launch = "$lane\night_rev34.cmd.launch.txt"
if (Test-Path $launch) {
    Get-Content $launch | ForEach-Object { "  $_" }
    $m = [regex]::Match((Get-Content $launch -Raw), 'PID (\d+)')
    if ($m.Success) {
        $procId = [int]$m.Groups[1].Value
        $p = Get-Process -Id $procId -ErrorAction SilentlyContinue
        $alive = [bool]$p -and $p.ProcessName -eq 'cmd'
        "  ПРОЦЕСС {0}: {1}" -f $procId, ($alive ? 'ЖИВ' : 'НЕТ (кончился или оборван)')
        if ($alive) {
            $kids = Get-CimInstance Win32_Process -Filter "ParentProcessId=$procId" |
                    Select-Object -ExpandProperty Name
            "  дети: " + (($kids | Sort-Object -Unique) -join ', ')
        }
    }
} else { "  ⛔ нет $launch — счёт не пускался" }

''
'шаги (logs\*_done.txt):'
$steps = [ordered]@{
    '1_store_far_done.txt' = 'склад: три дальние сцены по 6 000 000'
    '2_store_done.txt'     = 'склад: остальные 46 сцен по 3 000 000'
    '3_curves_done.txt'    = 'кривые всех 49 сцен (CorpusEffProbe --force)'
    '4_show_done.txt'      = 'склад витрины FSA (rebuild_store -Force; не держит цепь)'
    '5_wd_done.txt'        = 'оснастка wd_p161 (mk_appwd + check_appwd)'
    '6_mini_done.txt'      = 'прогон корпуса МАЛОЙ базой -> out_rev34_mini'
    '7_full_done.txt'      = 'прогон ПОЛНОГО корпуса -> out_rev34_full'
}
foreach ($f in $steps.Keys) {
    $p = Join-Path $logs $f
    if (Test-Path $p) {
        "  {0,-24} {1,-58} {2}" -f $f, $steps[$f], ((Get-Content $p -Raw).Trim())
    } else {
        "  {0,-24} {1,-58} ещё не закрыт" -f $f, $steps[$f]
    }
}
foreach ($f in 'ALL_DONE.txt', 'CHAIN_PARTIAL.txt', 'CHAIN_FAILED.txt', '_start.txt', '_finish.txt') {
    $p = Join-Path $logs $f
    if (Test-Path $p) { "  {0,-24} {1}" -f $f, ((Get-Content $p -Raw).Trim()) }
}

''
'склад полосы:'
$rmx = @(Get-ChildItem "$store\*.rmx" -ErrorAction SilentlyContinue)
"  .rmx по ключам сцен : {0} из 49" -f $rmx.Count
# клеймо читается из заголовка файла: строка `phys=NN;<хеш>` в первых 4 КБ
$ph = @{}
foreach ($f in $rmx) {
    $fs = [IO.File]::OpenRead($f.FullName)
    try { $buf = New-Object byte[] 4096; $n = $fs.Read($buf, 0, 4096) } finally { $fs.Dispose() }
    $mm = [regex]::Match([Text.Encoding]::ASCII.GetString($buf, 0, $n), 'phys=\d+')
    $k = $mm.Success ? $mm.Value : '?'
    $ph[$k] = 1 + ($ph[$k] ?? 0)
}
"  клейма              : " + (($ph.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name) — $($_.Value)" }) -join '; ')
"  response\*.rmx      : {0} из 49" -f @(Get-ChildItem "$store\response\*.rmx" -ErrorAction SilentlyContinue).Count
$tmp = @(Get-ChildItem "$store\*.tmp" -ErrorAction SilentlyContinue)
"  обрубков .tmp       : {0}{1}" -f $tmp.Count, ($tmp.Count ? ' ⛔ прерванная запись' : '')
$last = $rmx | Sort-Object LastWriteTime | Select-Object -Last 1
if ($last) { "  последняя матрица   : {0} ({1:HH:mm:ss})" -f $last.Name, $last.LastWriteTime }
"  склад витрины       : " + ((Test-Path "$wt\tools\fsa_showcase\store") ? ("матриц " + @(Get-ChildItem "$wt\tools\fsa_showcase\store\*.rmx" -ErrorAction SilentlyContinue).Count) : 'нет')
foreach ($d in 'out_rev34_mini', 'out_rev34_full') {
    $p = "$wt\tools\pie\$d"
    "  {0,-20}: {1}" -f $d, ((Test-Path $p) ? ("файлов " + @(Get-ChildItem $p -File -ErrorAction SilentlyContinue).Count) : 'нет')
}

if ($Tail -gt 0) {
    foreach ($f in 'store_far.log', 'store.log', 'curves.log', 'show.log', 'wd.log', 'mini.log', 'full.log',
                   'store_far.err', 'store.err', 'curves.err') {
        $p = Join-Path $logs $f
        if (Test-Path $p) {
            ''
            "=== $f (" + (Get-Item $p).Length + " байт) ==="
            Get-Content $p -Tail $Tail
        }
    }
    $arm = "$lane\art\arm_codes.txt"
    if (Test-Path $arm) { ''; '=== art\arm_codes.txt ==='; Get-Content $arm }
}
