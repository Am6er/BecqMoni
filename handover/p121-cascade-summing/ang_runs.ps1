# П121 (AMBER58): прямая мерка A22/A44 Geant4 (angcorr, 5 млн распадов) на ПРАВЛЕННОЙ поставке
# PhotonEvaporation (403 -> 304 при δ≠0) и на штатной — контроль той же сборкой.
param([long]$Decays = 5000000)
$out = 'D:\BqMoni_Claude\p121\g4'
New-Item -ItemType Directory -Force $out | Out-Null
Set-Location $out
$runs = @(
  @('eu152', 63, 152, 964.1, 121.8, 'patched'),
  @('eu152', 63, 152, 964.1, 121.8, 'std'),
  @('eu152', 63, 152, 1112.1, 121.8, 'patched'),
  @('cs134', 55, 134, 563.2, 604.7, 'patched')
)
"start $(Get-Date -Format HH:mm:ss)" | Out-File -Append "$out\ang_status.txt"
foreach ($r in $runs) {
  $n, $z, $a, $e1, $e2, $set = $r
  $bat = "D:\BqMoni_Claude\p121\run_g4cf_$set.bat"
  $log = "$out\ang_${n}_${e1}_${e2}_$set.log"
  $t = Get-Date
  cmd /c "`"$bat`" corr angcorr $z $a $Decays $e1 $e2" > $log 2> "$out\ang_${n}_${e1}_${e2}_$set.err"
  $line = (Select-String -Path $log -Pattern '^ANGCORR' | Select-Object -First 1).Line
  "$n $e1+$e2 $set rc=$LASTEXITCODE за $(((Get-Date)-$t).TotalMinutes.ToString('F1')) мин: $line" | Out-File -Append "$out\ang_status.txt"
}
"done $(Get-Date -Format HH:mm:ss)" | Out-File -Append "$out\ang_status.txt"
