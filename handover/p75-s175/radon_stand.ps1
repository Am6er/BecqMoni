# П75 (S175): стенд радонового фильтра ASN16 (спектр 2) — как у П68/П69/П70: копии спектров, склад сцены
# ASN16_rn_side (копия handover/p59-amber27/scenes), index.csv, матрица физикой 18 умолчаниями
# (CorpusMatrixProbe --threads=10 --target=0 из свежего build_p75 основного дерева — A77), кривая и guid
# в копии спектров (CorpusEffProbe). Склад — D:\BqMoni_Claude\p75\radon\store (свой, живой не трогается).
#   pwsh -File D:\BqMoni_Claude\p75\radon_stand.ps1
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$build = "$repo\tools\effmaker\probes\build_p75"
$d = 'D:\BqMoni_Claude\p75\radon'
New-Item -ItemType Directory -Force "$d\spectra", "$d\store", "$d\logs" | Out-Null
Copy-Item "D:\BqMoni_Claude\p75\spectra\radon1_side.xml" "$d\spectra\radon1_side.xml" -Force
Copy-Item "D:\BqMoni_Claude\p75\spectra\radon2_side.xml" "$d\spectra\radon2_side.xml" -Force
"sha256 radon2 копии: " + (Get-FileHash "$d\spectra\radon2_side.xml" -Algorithm SHA256).Hash.Substring(0, 16)
Copy-Item "$repo\handover\p59-amber27\scenes\ASN16_rn_side.in" "$d\store\" -Force
"sha256 сцены: " + (Get-FileHash "$d\store\ASN16_rn_side.in" -Algorithm SHA256).Hash.Substring(0, 16)
$index = @(
    'geometry,spectrum,preset,vessel',
    'ASN16_rn_side,radon1_side,Atom Spectra Nano 16,"ватный диск Ø40×20 мм ρ 0.15 г/см³ (целлюлоза), ВПРИТЫК к широкой грани 18×60"',
    'ASN16_rn_side,radon2_side,Atom Spectra Nano 16,"ватный диск Ø40×20 мм ρ 0.15 г/см³ (целлюлоза), ВПРИТЫК к широкой грани 18×60"'
)
[IO.File]::WriteAllLines("$d\store\index.csv", $index, (New-Object System.Text.UTF8Encoding($false)))
$sw = [Diagnostics.Stopwatch]::StartNew()
Set-Location $build
& .\CorpusMatrixProbe.exe --dir=$d\store --threads=10 --target=0 *> "$d\logs\count_ASN16_rn_side.log"
$c = $LASTEXITCODE
"count code $c  ($([int]$sw.Elapsed.TotalSeconds) s)"
Get-Content "$d\logs\count_ASN16_rn_side.log" -Encoding UTF8 | Select-String 'клейм|СОШЛИСЬ|ШУМН|мкс|phys=' | Select-Object -Last 4
if ($c -ne 0) { exit $c }
& .\CorpusEffProbe.exe --dir=$d\store --spectra=$d\spectra *> "$d\logs\eff_ASN16_rn_side.log"
$e = $LASTEXITCODE
"eff code $e  ($([int]$sw.Elapsed.TotalSeconds) s)"
Get-Content "$d\logs\eff_ASN16_rn_side.log" -Encoding UTF8 | Select-String 'guid|узл|медиан|Efficiency|код' | Select-Object -Last 5
Get-ChildItem "$d\store\response\*.rmx" | ForEach-Object { "  {0}  {1}" -f $_.Name, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 16) }
exit $e
