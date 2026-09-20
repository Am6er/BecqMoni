# П100: опора Geant4 (option4) по η — материалы × энергии, N=100000, лог eta_all.log (одна строка ETA на связку).
[Console]::OutputEncoding=[System.Text.Encoding]::UTF8
$log='D:\BqMoni_Claude\p100\g4eta\eta_all.log'
$codes='D:\BqMoni_Claude\p100\g4eta\codes_eta.txt'
foreach ($m in 'G4_TEFLON','G4_Al','G4_MAGNESIUM_OXIDE','G4_C','G4_Cu','G4_CESIUM_IODIDE') {
  foreach ($e in 100,500,1000,2000) {
    $t0=Get-Date
    $out = cmd /c "D:\BqMoni_Claude\p100\g4eta\g4eta_run.cmd $m $e 100000" 2>&1
    $code=$LASTEXITCODE
    ($out | Select-String -Pattern '^ETA') | ForEach-Object { $_.Line } | Out-File -Append -Encoding utf8 $log
    "$m $e code=$code dt=$([int]((Get-Date)-$t0).TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
  }
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
