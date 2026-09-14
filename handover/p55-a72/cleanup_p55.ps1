# П55: снятие полосы ПОСЛЕ приёмки (правило Amber 13.09.2026: свой каталог полоса снимает после приёмки).
# Снимает worktree D:\BqMoni_Claude\p55\wt (сборки Release_p55_*, obj, пробы build_p55_* лежат внутри него),
# копию арбитра, прогоны. Артефакты уже скопированы в handover/p55-a72/ (pack_logs.py).
$ErrorActionPreference = 'Continue'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
git -C $repo worktree remove --force 'D:\BqMoni_Claude\p55\wt'
git -C $repo worktree prune
Remove-Item -Recurse -Force 'D:\BqMoni_Claude\p55' -ErrorAction SilentlyContinue
"снято: $(-not (Test-Path 'D:\BqMoni_Claude\p55'))"
git -C $repo worktree list
