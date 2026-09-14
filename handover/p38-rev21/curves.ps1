# П38 13.09.2026 — кривые всех 45 сцен физикой 17 на ПЕРЕСОБРАННОМ корпусе (после rebuild_corpus.py шаг 2/4
# вернул узлы <Efficiency> физики 16 из git HEAD): `CorpusEffProbe --force` из build_p38 на живом складе и
# живых спектрах — как П37 curves.ps1, но в основном дереве. Клеймо кривой обязано нести
# phys=17; hist=200000; …; kdip=1; lys=2; etr=1; e+tr=1; e+off=1; rayl2=1 у 85 узлов.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p38"
$art = "$root\handover\p38-rev21"
$store = "$root\tools\CORPUS\corpus\geometries"
"curves start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'); rmx in store: $((Get-ChildItem $store -Filter *.rmx).Count)" | Out-File -Append "$art\codes.txt"
Push-Location $bin
& "$bin\CorpusEffProbe.exe" "--dir=$store" "--spectra=$root\tools\CORPUS\corpus\spectra" --force > "$art\curves.log" 2>&1
"curves code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
