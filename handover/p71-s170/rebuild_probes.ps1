# П71: пересборка заверенного каталога проб build_p71a (с новой SumPeakProbe.cs), затем оснастка wd_p71a
# и SumPeakProbe на трёх Co-60 (плечо A — живой склад), затем плечо Δ = 7 мм (run_d7.ps1).
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$wt = 'D:\BqMoni_Claude\p71\wt'
$out = 'D:\BqMoni_Claude\p71\logs'
$sw = [Diagnostics.Stopwatch]::StartNew()
Set-Location $wt
& pwsh -NoProfile -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p71a" -Out "$wt\tools\effmaker\probes\build_p71a" *> "$out\build_all_2.log"
"build_all code $LASTEXITCODE ($([int]$sw.Elapsed.TotalSeconds) s)"
if ($LASTEXITCODE -ne 0) { Get-Content "$out\build_all_2.log" | Select-String 'FAIL|error' | Select-Object -First 8; exit 1 }
& pwsh -NoProfile -File D:\BqMoni_Claude\p71\mk_wd.ps1 -Arm a *> "$out\mk_wd_a2.log"
"mk_wd code $LASTEXITCODE"
Get-Content "$out\mk_wd_a2.log" | Select-String 'ОСНАСТКА|⛔|ПРОТУХ' | Select-Object -Last 2
Set-Location "$wt\tools\CORPUS\scripts\wd_p71a"
$sp = "$wt\tools\CORPUS\corpus\spectra"
$codes = @()
foreach ($s in 'G1S16_Co60_P5','G1S24_Co60_P5','G1S16_Co60_P25') {
  .\SumPeakProbe.exe "--spectrum=$sp\$s.xml" --sample=Co-60 *> "$out\sp_${s}_a.log"
  $codes += "$s=$LASTEXITCODE"
}
"коды плеча A: " + ($codes -join ' ')
& pwsh -NoProfile -File D:\BqMoni_Claude\p71\run_d7.ps1
"run_d7 code $LASTEXITCODE ($([int]$sw.Elapsed.TotalSeconds) s)"
"DONE"
