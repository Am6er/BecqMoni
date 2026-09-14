# П70 (AMBER30): снимок FSA «Cs 137 в домике 24.11.2022» на каждой точке бисекции — ОДНИМ скриптом.
# Стенд = экран Amber: состав «из NucBase» (--infer), набор «Cs-137 + K-40» из её NuclideDefinition.xml,
# связка/рентген/образы — умолчания (все ВКЛ), матрица из её склада (config\device\response\c482e3bc….rmx).
#
#   & D:\BqMoni_Claude\p70\bisect_run.ps1 -Hashes 025a65a9,... [-Tag <суффикс>] [-Extra <доп. ключи>]
#
# Выход — out\<hash>[_tag]\: shot.png, rates.csv, curves.csv, shot.log (stdout+stderr, строки ROW/SCREEN/LINE).
param([string[]]$Hashes = @('025a65a9'), [string]$Tag = '', [string[]]$Extra = @())
$ErrorActionPreference = 'Continue'
$lane = 'D:\BqMoni_Claude\p70'
$spectrum = Join-Path $lane 'Cs 137 в домике 24.11.2022.xml'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$codes = @()
foreach ($h in $Hashes) {
    $wd = Join-Path $lane "wd_$h"
    $out = Join-Path $lane ("out\" + $h + $(if ($Tag) { "_$Tag" } else { '' }))
    New-Item -ItemType Directory -Force $out | Out-Null
    if (-not (Test-Path (Join-Path $wd 'FsaStackShot.exe'))) { "нет $wd\FsaStackShot.exe"; $codes += "$h=нет"; continue }
    Push-Location $wd
    $a = @("--spectrum=$spectrum", '--infer', '--set=Cs-137 + K-40', '--screen', '--scale=pow', '--width=1600',
           '--lines=Xray-Pb', "--out=$out\shot.png", "--rates=$out\rates.csv", "--dump=$out\curves.csv") + $Extra
    & .\FsaStackShot.exe @a > (Join-Path $out 'shot.log') 2>&1
    $code = $LASTEXITCODE
    Pop-Location
    $codes += "$h=$code"
    $log = Join-Path $out 'shot.log'
    "--- $h (код $code) ---"
    Select-String -Path $log -Pattern '^(состав|chi2/ndf|шкала|ROW|гейты при матрице|отвязанный хвост|CUT|пороги отображения|серый слой|невязка)' | ForEach-Object { $_.Line }
}
"коды: " + ($codes -join ' ')
