# П71 (S170): плечо Δ = 7 мм — оснастка wd_p71d7 со СВОИМ складом (матрица G1S_point5 при pdistance 5.7 см),
# спектры-копии с PointDistance 57; SumPeakProbe на двух Co-60. Двоичные файлы — те же (build_p71a).
#   pwsh -File D:\BqMoni_Claude\p71\run_d7.ps1 [-Probe <каталог проб>]
param([string]$Probe = 'D:\BqMoni_Claude\p71\wt\tools\effmaker\probes\build_p71a', [string]$Tag = 'd7')
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$wt = 'D:\BqMoni_Claude\p71\wt'
$st = 'D:\BqMoni_Claude\p71\store'
$wd = "$wt\tools\CORPUS\scripts\wd_p71$Tag"
$out = "D:\BqMoni_Claude\p71\logs"
Set-Location $wt
& "$wt\tools\CORPUS\scripts\mk_appwd.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p71a" -Wd $wd -ProbeBuild $Probe -Store $st *> "$out\mk_wd_$Tag.log"
"mk_appwd code $LASTEXITCODE"
Get-Content "$out\mk_wd_$Tag.log" | Select-String 'ОСНАСТКА|⛔|ПРОТУХ|положено' | Select-Object -Last 4
Get-ChildItem "$wd\config\device\response" | Select-Object Name, Length
Set-Location $wd
$codes = @()
foreach ($s in 'G1S16_Co60_P5','G1S24_Co60_P5') {
  .\SumPeakProbe.exe "--spectrum=D:\BqMoni_Claude\p71\spectra_d7\$s.xml" --sample=Co-60 *> "$out\sp_${s}_$Tag.log"
  $codes += "$s=$LASTEXITCODE"
}
"коды: " + ($codes -join ' ')
