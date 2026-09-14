# П56 14.09.2026, `AMBER22` — плечи FsaStackShot на КОРПУСНОМ спектре диска (AS80_Th232Medal.xml: кривая AS80_th_disk
# физики 18 и фон встроены) из оснастки корпуса wd_p56 (mk_appwd.ps1, склад — копия живого). Состав ОБЪЯВЛЯЕТСЯ рядами
# (--chain=, ключ П56) — контроль `th` = состав корпусного прогона; плечи с Ra-226 / U-238u / U-235 / K-40.
#   pwsh -NoProfile -File D:\BqMoni_Claude\p56\run_arms.ps1 [-Arms th,thra,...] [-Spectrum <файл>] [-Tag disk]
param([string[]]$Arms = @(), [string]$Spectrum = '', [string]$Tag = 'disk')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$wt = 'D:\BqMoni_Claude\p56\wt'
$wd = "$wt\tools\CORPUS\scripts\wd_p56"
if ($Spectrum -eq '') { $Spectrum = "$wt\tools\CORPUS\corpus\spectra\AS80_Th232Medal.xml" }
$here = "D:\BqMoni_Claude\p56\fsa_$Tag"
New-Item -ItemType Directory -Force $here | Out-Null
$log = "$here\arms.log"
$all = [ordered]@{
    th      = @('--chain=Th-232')
    thk     = @('--chain=Th-232', '--sample=40K')
    thra    = @('--chain=Th-232,Ra-226')
    thrak   = @('--chain=Th-232,Ra-226', '--sample=40K')
    thu     = @('--chain=Th-232,U-238u,U-235')
    thrau   = @('--chain=Th-232,Ra-226,U-238u,U-235')
    thrauk  = @('--chain=Th-232,Ra-226,U-238u,U-235', '--sample=40K')
    thu8    = @('--chain=Th-232,U-238')            # U-238 весь ряд (с Ra-226 в связке)
    thu8k   = @('--chain=Th-232,U-238', '--sample=40K')
    thra_kf2 = @('--chain=Th-232,Ra-226', '--knots=2')
    th_kf2  = @('--chain=Th-232', '--knots=2')
    thrauk_kf2 = @('--chain=Th-232,Ra-226,U-238u,U-235', '--sample=40K', '--knots=2')
}
if ($Arms.Count -eq 0) { $Arms = @($all.Keys) }
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
Push-Location $wd
foreach ($name in $Arms) {
    if (-not $all.Contains($name)) { "нет плеча $name" | Out-File -Append $log; continue }
    $out = "$here\$name"
    New-Item -ItemType Directory -Force $out | Out-Null
    $keys = @("--spectrum=$Spectrum", "--out=$out\stack.png", "--dump=$out\dump.csv", '--scale=pow', '--from=15', '--to=2800') + $all[$name]
    $t0 = Get-Date
    & "$wd\FsaStackShot.exe" @keys *> "$here\probe_$name.txt"
    $code = $LASTEXITCODE
    "$name code=$code $(Get-Date -Format 'HH:mm:ss') ($([int]((Get-Date)-$t0).TotalSeconds) s) keys=[$($all[$name] -join ' ')] spectrum=$Spectrum" | Out-File -Append $log
}
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
