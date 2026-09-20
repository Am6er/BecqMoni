# П100: опора Geant4 — косое падение (45°, 70°) на PTFE и Al при 100/500/1000 кэВ, N=100000; и фольга PTFE 0.1 пробега.
[Console]::OutputEncoding=[System.Text.Encoding]::UTF8
$log='D:\BqMoni_Claude\p100\g4eta\eta_oblique.log'
$codes='D:\BqMoni_Claude\p100\g4eta\codes_oblique.txt'
foreach ($m in 'G4_TEFLON','G4_Al') {
  foreach ($e in 100,500,1000) {
    foreach ($a in 45,70) {
      $t0=Get-Date
      $out = cmd /c "D:\BqMoni_Claude\p100\g4eta\g4eta_run.cmd $m $e 100000 0.01 20260918 0 $a" 2>&1
      $code=$LASTEXITCODE
      ($out | Select-String -Pattern '^ETA') | ForEach-Object { $_.Line } | Out-File -Append -Encoding utf8 $log
      "$m $e angle=$a code=$code dt=$([int]((Get-Date)-$t0).TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
    }
  }
}
# фольга: PTFE, толщина = 0.1 пробега CSDA (100 кэВ: 0.01727 г/см² → 0.0785 мм; 500: 0.2114 → 0.961 мм; 1000: 0.5227 → 2.376 мм), шаг нашего переноса
foreach ($pair in @(@(100,0.0785),@(500,0.961),@(1000,2.376))) {
  $e=$pair[0]; $d=$pair[1]
  $t0=Get-Date
  $out = cmd /c "D:\BqMoni_Claude\p100\g4eta\g4eta_run.cmd G4_TEFLON $e 100000 0.01 20260918 $d 0" 2>&1
  $code=$LASTEXITCODE
  ($out | Select-String -Pattern '^FOIL|^ETA') | ForEach-Object { $_.Line } | Out-File -Append -Encoding utf8 $log
  "G4_TEFLON $e foil=$d code=$code dt=$([int]((Get-Date)-$t0).TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
