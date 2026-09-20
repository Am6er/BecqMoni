# П100 (M13, 18.09.2026): исполнитель плана плеч — тот же, что у П92, пути своей полосы.
# Одна сторона за вызов (-Side ours|g4), план — CSV `plan_*.csv` со строками  side;geo;energy;n;tag;keys
# (keys — через пробел).
#   ours: G4RawProbe из build_p100 (сырой отклик, --no-light --bin=1, умолчания склада физики 19,
#         зерно штатное 20260902), геометрия D:\BqMoni_Claude\p100\geo\<geo>.in → ours\ours_<geo>_<E>_<tag>.csv
#   g4:   копия арбитра D:\BqMoni_Claude\p92\g4 (killesc/killcarry/fullcarry/CUTS), vacuum у всех,
#         сцена scenes\<geo>.scene, hist <E> <n> 1 → g4out\g4_<geo>_<E>_<tag>.log
# Код возврата КАЖДОГО прогона — в codes_<side>.txt (правило A77: смотреть код, а не факт запуска).
# ⛔ Кодировку консоли не трогать (П20/П26): g4run.cmd ставит 1251 сам; наш вывод — utf8 файлом.
# -Probe — другой exe (сборка ДО правки для контроля), -Threads — --threads= пробе (замер цены: 1).
param([Parameter(Mandatory)][string]$Side, [Parameter(Mandatory)][string]$Plan,
      [string]$Probe = 'D:\BqMoni_Claude\p100\wt\tools\effmaker\probes\build_p100\G4RawProbe.exe',
      [string]$OursDir = 'D:\BqMoni_Claude\p100\ours')
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'D:\BqMoni_Claude\p100'
$g4 = "D:\BqMoni_Claude\p92\g4\g4run.cmd"
$codes = "$root\codes_$Side.txt"
$rows = Get-Content $Plan | Where-Object { $_ -and -not $_.StartsWith('#') }
foreach ($row in $rows) {
    $c = $row.Split(';')
    if ($c[0] -ne $Side) { continue }
    $geo = $c[1]; $e = $c[2]; $n = $c[3]; $tag = $c[4]
    $keys = @()
    if ($c.Length -gt 5 -and $c[5].Trim() -ne '') { $keys = $c[5].Trim().Split(' ') }
    $name = "${geo}_${e}_${tag}"
    $t0 = Get-Date
    if ($Side -eq 'ours') {
        $out = "$OursDir\ours_$name.csv"
        if (Test-Path $out) { "ours $name SKIP (есть) $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes; continue }
        & $Probe "--geometry=$root\geo\$geo.in" "--energy=$e" "--n=$n" --no-light --bin=1 @keys "--out=$out" 2>&1 |
            Out-File -Encoding utf8 "$OursDir\ours_$name.txt"
        $code = $LASTEXITCODE
    } else {
        $out = "$root\g4out\g4_$name.log"
        if (Test-Path $out) { "g4 $name SKIP (есть) $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes; continue }
        & $g4 @keys vacuum scene "$root\scenes\$geo.scene" hist $e $n 1 2>&1 | Out-File -Encoding utf8 $out
        $code = $LASTEXITCODE
    }
    "$Side $name code=$code n=$n keys=[$($keys -join ' ')] probe=$(Split-Path -Leaf (Split-Path $Probe)) $(Get-Date -Format 'HH:mm:ss') dt=$([int]((Get-Date)-$t0).TotalSeconds)s" | Out-File -Append $codes
}
"done $Side $Plan $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
