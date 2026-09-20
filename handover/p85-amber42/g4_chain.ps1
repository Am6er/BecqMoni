# П85 (AMBER42): цепочка прогонов Geant4 (g4cf, ион-режим) на сцене G1S_point5 —
# каждый нуклид дважды: без ключа corr (изотропно, как было) и с ключом corr.
# Логи: D:\BqMoni_Claude\p85\g4\g4_ion_<нуклид>_<off|on>.log (+ .err), моно Y-88 2734 — g4_mono_2734.log.
#   & g4_chain.ps1 [-Plan 'co60:40000000,y88:60000000,cs134:20000000,eu152:20000000']
param([string]$Plan = 'co60:40000000,y88:60000000,cs134:20000000,eu152:20000000', [switch]$SkipMono)
$ErrorActionPreference = 'Continue'
$bat = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\g4cf\run_g4cf.bat'
$scene = 'D:\BqMoni_Claude\p85\g4\G1S_point5.scene'
$out = 'D:\BqMoni_Claude\p85\g4'
$nuc = @{
  co60  = @{ z = 27; a = 60;  win = '1173.2 1332.5 2505.7' }
  y88   = @{ z = 39; a = 88;  win = '898.0 1836.1 2734.1' }
  cs134 = @{ z = 55; a = 134; win = '563.2 569.3 604.7 795.9 801.9 1038.6 1168.0 1365.2 1400.6' }
  eu152 = @{ z = 63; a = 152; win = '121.8 244.7 344.3 411.1 444.0 778.9 867.4 964.1 1085.8 1112.1 1408.0 1123.2 1233.9 1529.8' }
}
Set-Location $out
"start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$out\chain_status.txt"
foreach ($e in $Plan.Split(',')) {
  $n, $d = $e.Split(':')
  $c = $nuc[$n]
  foreach ($mode in 'off', 'on') {
    $key = if ($mode -eq 'on') { 'corr ' } else { '' }
    $log = "$out\g4_ion_${n}_$mode.log"
    $t = Get-Date
    "$n $mode $d начало $(Get-Date -Format HH:mm:ss)" | Out-File -Append "$out\chain_status.txt"
    cmd /c "`"$bat`" ${key}scene $scene ion $($c.z) $($c.a) $d $($c.win)" > $log 2> "$out\g4_ion_${n}_$mode.err"
    "$n $mode rc=$LASTEXITCODE за $(((Get-Date)-$t).TotalMinutes.ToString('F1')) мин" | Out-File -Append "$out\chain_status.txt"
  }
}
if (-not $SkipMono) {
  # моно 2734.0 кэВ — вклад собственной линии Y-88 2734 в окно сумм-пика 2734.1
  cmd /c "`"$bat`" scene $scene mono 2734.0 2000000" > "$out\g4_mono_2734.log" 2> "$out\g4_mono_2734.err"
  "mono 2734 rc=$LASTEXITCODE" | Out-File -Append "$out\chain_status.txt"
}
"done $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$out\chain_status.txt"
