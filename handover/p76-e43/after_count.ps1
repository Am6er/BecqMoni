# П76 14.09.2026 (E43) — после счёта двух матриц RC103 и пересборки корпуса: кривые двух сцен (CorpusEffProbe по
# спектрам основного дерева, склад полосы), перенос в живой склад (снимок уже снят — store_swap.py backup),
# приёмка живого склада, сторожа корпуса, сводка, check_corpus, check_corpus_generator, клейма генератора против
# живого склада (stamp_check.py П72). Все пробы — из build_p76 (сборка 97ccef9f, физика 18). Коды — codes.txt.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$env:PYTHONIOENCODING = 'utf-8'; $env:PYTHONUTF8 = '1'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = "$root\handover\p76-e43"
$p = 'D:\BqMoni_Claude\p76'
$pb = "$p\wt\tools\effmaker\probes\build_p76"
$pbb = "$p\wt_b\tools\effmaker\probes\build_p76b"
$store = "$p\store"
$log = "$art\codes.txt"
Set-Location $root
"after_count start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append $log
Push-Location $pb
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
& python "$root\tools\CORPUS\scripts\split_corpus.py" *> "$art\split_corpus.log"
"split_corpus code=$LASTEXITCODE" | Out-File -Append $log
& python "$root\tools\CORPUS\scripts\mk_materials.py" *> "$art\mk_materials.log"
"mk_materials code=$LASTEXITCODE" | Out-File -Append $log
& python "$root\tools\CORPUS\scripts\corpus_summary.py" *> "$art\corpus_summary.log"
"corpus_summary code=$LASTEXITCODE" | Out-File -Append $log
& python "$root\tools\CORPUS\scripts\check_corpus.py" *> "$art\check_corpus_final.log"
"check_corpus final code=$LASTEXITCODE" | Out-File -Append $log
& python "$root\tools\check_corpus_generator.py" *> "$art\check_corpus_generator.log"
"check_corpus_generator code=$LASTEXITCODE" | Out-File -Append $log
& python "$root\handover\p72-t258-t259\stamp_check.py" "$p\gen" $pbb "--out=$art\stamps_gen_vs_live_after.csv" *> "$art\stamp_check_after.txt"
"stamp_check after code=$LASTEXITCODE" | Out-File -Append $log
"after_count end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
