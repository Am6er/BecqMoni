# П100: опора Geant4 — обвязка RC103 П55 как есть (PTFE 1 мм + Al 1 мм + пустота), 0/45/70°, 100/500/1000/2000 кэВ, N=100000.
[Console]::OutputEncoding=[System.Text.Encoding]::UTF8
$log='D:\BqMoni_Claude\p100\g4eta\eta_rc103.log'
$codes='D:\BqMoni_Claude\p100\g4eta\codes_rc103.txt'
foreach ($e in 100,500,1000,2000) {
  foreach ($a in 0,45,70) {
    $t0=Get-Date
    $out = cmd /c "D:\BqMoni_Claude\p100\g4eta\g4eta_run.cmd G4_TEFLON $e 100000 0.01 20260918 1.0 $a G4_Al 1.0" 2>&1
    $code=$LASTEXITCODE
    ($out | Select-String -Pattern '^ETA') | ForEach-Object { $_.Line } | Out-File -Append -Encoding utf8 $log
    "PTFE1+Al1 $e angle=$a code=$code dt=$([int]((Get-Date)-$t0).TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
  }
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
