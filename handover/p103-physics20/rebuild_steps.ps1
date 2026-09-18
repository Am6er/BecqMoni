# П103 (B31) — шаги пересборки корпуса в worktree ПО ОТДЕЛЬНОСТИ (те же четыре скрипта, что зовёт rebuild_corpus.py,
# но приёмка check_corpus.py — ПОСЛЕ матриц и кривых: понятные спектры без узла <Efficiency> физики 20 до CorpusEffProbe
# приёмку не пройдут по построению, а restore_eff_nodes ПОСЛЕ CorpusEffProbe вернул бы узлы rev29 из git).
# Образец — П99 rebuild_steps.ps1; добавлен дамп точек модели разрешения (--res-points=, диагностика B31).
#   pwsh -File D:\BqMoni_Claude\p103\scripts\rebuild_steps.ps1 -Tag _2
param([string]$Tag = '')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'
$wt = 'D:\BqMoni_Claude\p103\wt'
$art = 'D:\BqMoni_Claude\p103\art'
Set-Location $wt
$t0 = Get-Date
python tools\CORPUS\scripts\build_corpus.py --from-library "--res-points=$art\res_points$Tag.csv" *> "$art\build_corpus$Tag.log"
"build_corpus code=$LASTEXITCODE $([int]((Get-Date)-$t0).TotalSeconds) s" | Tee-Object -Append "$art\rebuild_codes.txt"
python tools\CORPUS\scripts\restore_eff_nodes.py --apply *> "$art\restore_eff_nodes$Tag.log"
"restore_eff_nodes code=$LASTEXITCODE" | Tee-Object -Append "$art\rebuild_codes.txt"
python tools\CORPUS\scripts\res_apply.py --mode=power-node --apply *> "$art\res_apply$Tag.log"
"res_apply code=$LASTEXITCODE" | Tee-Object -Append "$art\rebuild_codes.txt"
python tools\CORPUS\scripts\split_corpus.py *> "$art\split_corpus$Tag.log"
"split_corpus code=$LASTEXITCODE" | Tee-Object -Append "$art\rebuild_codes.txt"
Get-Content "$art\build_corpus$Tag.log" | Select-String 'девятка|ПОЕХАЛ|НОВОЕ|ПРОПАЛО|записано новых|клеймо генератора|B31' | ForEach-Object { $_.Line }
Get-Content "$art\split_corpus$Tag.log" | Select-String 'known|unknown|excluded|СОШЛОСЬ|РАЗОШ' | ForEach-Object { $_.Line }
