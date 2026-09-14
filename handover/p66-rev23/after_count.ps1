# П66 14.09.2026 — шаг 3 после счёта 18 матриц: приёмка склада полосы, кривые (CorpusEffProbe по корпусу основного
# дерева), снимок уже снят (store_swap.py backup), перенос в живой склад, приёмка живого склада, сводка и check_corpus.
# Все пробы — из build_p66 (сборка 16de7feb, физика 18). Коды — codes.txt, логи рядом.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'; $env:PYTHONUTF8 = '1'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = "$root\handover\p66-rev23"
$pb = 'D:\BqMoni_Claude\p66\wt\tools\effmaker\probes\build_p66'
$store = 'D:\BqMoni_Claude\p66\store'
$log = "$art\codes.txt"
Set-Location $root
"after_count start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append $log
Push-Location $pb
.\MatrixAuditProbe.exe "--dir=$store" --phys=18 --hist=3000000 --noise=6.0 *> "$art\audit_store.log"
"audit store code=$LASTEXITCODE" | Out-File -Append $log
.\CorpusEffProbe.exe "--dir=$store" "--spectra=$root\tools\CORPUS\corpus\spectra" *> "$art\eff.log"
"eff code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
Pop-Location
& python "$art\store_swap.py" swap *> "$art\store_swap.log"
"store swap code=$LASTEXITCODE" | Out-File -Append $log
Push-Location $pb
.\MatrixAuditProbe.exe "--dir=$root\tools\CORPUS\corpus\geometries" --phys=18 --hist=3000000 --noise=6.0 *> "$art\audit_live.log"
"audit live code=$LASTEXITCODE" | Out-File -Append $log
Pop-Location
& python "$root\tools\check_matrix_keys.py" *> "$art\check_matrix_keys.log"
"check_matrix_keys code=$LASTEXITCODE" | Out-File -Append $log
& python "$root\tools\check_geometry_names.py" *> "$art\check_geometry_names.log"
"check_geometry_names code=$LASTEXITCODE" | Out-File -Append $log
& python "$root\tools\check_curve_generation.py" *> "$art\check_curve_generation.log"
"check_curve_generation code=$LASTEXITCODE" | Out-File -Append $log
& python "$root\tools\CORPUS\scripts\corpus_summary.py" *> "$art\corpus_summary.log"
"corpus_summary code=$LASTEXITCODE" | Out-File -Append $log
& python "$root\tools\CORPUS\scripts\check_corpus.py" *> "$art\check_corpus_final.log"
"check_corpus final code=$LASTEXITCODE" | Out-File -Append $log
& python "$root\tools\check_corpus_generator.py" *> "$art\check_corpus_generator.log"
"check_corpus_generator code=$LASTEXITCODE" | Out-File -Append $log
"after_count end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
