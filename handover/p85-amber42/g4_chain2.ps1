# П85: добор статистики сумм-пика Co-60 в Geant4 другим зерном (ключ seed; сборка D:\BqMoni_Claude\p85\g4build).
param([long]$Decays = 40000000, [int]$Seed = 2, [string]$Nuc = 'co60')
$bat = 'D:\BqMoni_Claude\p85\run_g4cf_p85.bat'
$scene = 'D:\BqMoni_Claude\p85\g4\G1S_point5.scene'
$out = 'D:\BqMoni_Claude\p85\g4'
$table = @{ co60 = @{ z = 27; a = 60; win = '1173.2 1332.5 2505.7' }; y88 = @{ z = 39; a = 88; win = '898.0 1836.1 2734.1' }; cs134 = @{ z = 55; a = 134; win = '563.2 569.3 604.7 795.9 801.9 1038.6 1168.0 1365.2 1400.6' } }
$c = $table[$Nuc]
Set-Location $out
foreach ($mode in 'off', 'on') {
  $key = if ($mode -eq 'on') { 'corr ' } else { '' }
  $log = "$out\g4_ion_${Nuc}_${mode}_s$Seed.log"
  $t = Get-Date
  "$Nuc $mode seed=$Seed $Decays начало $(Get-Date -Format HH:mm:ss)" | Out-File -Append "$out\chain2_status.txt"
  cmd /c "`"$bat`" ${key}seed $Seed scene $scene ion $($c.z) $($c.a) $Decays $($c.win)" > $log 2> "$out\g4_ion_${Nuc}_${mode}_s$Seed.err"
  "$Nuc $mode seed=$Seed rc=$LASTEXITCODE за $(((Get-Date)-$t).TotalMinutes.ToString('F1')) мин" | Out-File -Append "$out\chain2_status.txt"
}
