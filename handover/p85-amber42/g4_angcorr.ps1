# П85 (AMBER42): прямая мерка A22/A44 пар в Geant4 (режим angcorr сборки D:\BqMoni_Claude\p85\g4build) —
# те же пары, что печатает наш AngularProbe по схемам базы. Логи: D:\BqMoni_Claude\p85\g4\ang_<нуклид>_<E1>_<E2>_<on|off>.log
param([long]$Decays = 5000000)
$bat = 'D:\BqMoni_Claude\p85\run_g4cf_p85.bat'
$out = 'D:\BqMoni_Claude\p85\g4'
Set-Location $out
$pairs = @(
  @('co60', 27, 60, 1173.2, 1332.5, 'on'), @('co60', 27, 60, 1173.2, 1332.5, 'off'),
  @('y88', 39, 88, 898.0, 1836.1, 'on'), @('y88', 39, 88, 898.0, 1836.1, 'off'),
  @('cs134', 55, 134, 604.7, 795.9, 'on'), @('cs134', 55, 134, 569.3, 795.9, 'on'), @('cs134', 55, 134, 563.2, 604.7, 'on'),
  @('eu152', 63, 152, 1408.0, 121.8, 'on'), @('eu152', 63, 152, 1112.1, 121.8, 'on'), @('eu152', 63, 152, 964.1, 121.8, 'on'),
  @('eu152', 63, 152, 778.9, 344.3, 'on'), @('eu152', 63, 152, 411.1, 344.3, 'on')
)
"start $(Get-Date -Format HH:mm:ss)" | Out-File -Append "$out\ang_status.txt"
foreach ($p in $pairs) {
  $n, $z, $a, $e1, $e2, $mode = $p
  $key = if ($mode -eq 'on') { 'corr ' } else { '' }
  $log = "$out\ang_${n}_${e1}_${e2}_$mode.log"
  $t = Get-Date
  cmd /c "`"$bat`" ${key}angcorr $z $a $Decays $e1 $e2" > $log 2> "$out\ang_${n}_${e1}_${e2}_$mode.err"
  $line = (Select-String -Path $log -Pattern '^ANGCORR' | Select-Object -First 1).Line
  "$n $e1+$e2 $mode rc=$LASTEXITCODE за $(((Get-Date)-$t).TotalMinutes.ToString('F1')) мин: $line" | Out-File -Append "$out\ang_status.txt"
}
"done $(Get-Date -Format HH:mm:ss)" | Out-File -Append "$out\ang_status.txt"
