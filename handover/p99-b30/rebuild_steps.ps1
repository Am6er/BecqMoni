# П99 — шаги пересборки корпуса в worktree ПО ОТДЕЛЬНОСТИ (те же четыре скрипта, что зовёт rebuild_corpus.py,
# но приёмка check_corpus.py — ПОСЛЕ матриц и кривых: новые понятные спектры без узла <Efficiency> до CorpusEffProbe
# приёмку не пройдут по построению, а restore_eff_nodes ПОСЛЕ CorpusEffProbe вернул бы узлы rev28 из git).
#   & D:\BqMoni_Claude\p99\rebuild_steps.ps1 -Tag _2
param([string]$Tag = '')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'
$wt = 'D:\BqMoni_Claude\p99\wt'
$art = 'D:\BqMoni_Claude\p99\art'
Set-Location $wt
$t0 = Get-Date
python tools\CORPUS\scripts\build_corpus.py --from-library *> "$art\build_corpus$Tag.log"
"build_corpus code=$LASTEXITCODE $([int]((Get-Date)-$t0).TotalSeconds) s" | Tee-Object -Append "$art\rebuild_codes.txt"
python tools\CORPUS\scripts\restore_eff_nodes.py --apply *> "$art\restore_eff_nodes$Tag.log"
"restore_eff_nodes code=$LASTEXITCODE" | Tee-Object -Append "$art\rebuild_codes.txt"
python tools\CORPUS\scripts\res_apply.py --mode=power-node --apply *> "$art\res_apply$Tag.log"
"res_apply code=$LASTEXITCODE" | Tee-Object -Append "$art\rebuild_codes.txt"
python tools\CORPUS\scripts\split_corpus.py *> "$art\split_corpus$Tag.log"
"split_corpus code=$LASTEXITCODE" | Tee-Object -Append "$art\rebuild_codes.txt"
Get-Content "$art\build_corpus$Tag.log" | Select-String 'девятка|ПОЕХАЛ|НОВОЕ|ПРОПАЛО|записано новых|клеймо генератора' | ForEach-Object { $_.Line }
Get-Content "$art\split_corpus$Tag.log" | Select-String 'known|unknown|excluded|СОШЛОСЬ|РАЗОШ' | ForEach-Object { $_.Line }
