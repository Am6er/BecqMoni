# П37 13.09.2026 — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (б): первые ДВЕ сцены склада (порядок пробы —
# StringComparer.Ordinal по имени: AS80_lu_front, AS80_point0) УМОЛЧАНИЯМИ нового кода (все семь
# ключей ВКЛ, физика 17), рецепт склада --threads=10 --target=0, В СКЛАД WORKTREE
# (C:\Users\moroz\bqp37\tools\CORPUS\corpus\geometries — там .in из git и ни одного .rmx).
# Что проверяется ДО восьмичасового счёта (A77): клеймо phys=17 и хвосты семи ключей
# (rmx_tails.py), время на сцену (оценка на 45), числа разумны — MatrixDiffProbe против живого
# склада физики 16 (пик/сумма/форма, доля пика).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp37'
$bin = "$wt\tools\effmaker\probes\build_p37"
$art = "$root\handover\p37-store"
$store = "$wt\tools\CORPUS\corpus\geometries"
"ctrl_b start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'); rmx in store before: $((Get-ChildItem $store -Filter *.rmx).Count)" | Out-File -Append "$art\codes.txt"
Push-Location $bin
& "$bin\CorpusMatrixProbe.exe" "--dir=$store" --only=AS80_lu_front,AS80_point0 --threads=10 --target=0 --force > "$art\ctrl_b_matrix.log" 2>&1
"ctrl_b matrix code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
foreach ($scene in 'AS80_lu_front', 'AS80_point0') {
    & "$bin\MatrixDiffProbe.exe" "--a=$root\tools\CORPUS\corpus\geometries\$scene.rmx" "--b=$store\$scene.rmx" > "$art\ctrl_b_diff_$scene.log" 2>&1
    "ctrl_b diff $scene code=$LASTEXITCODE" | Out-File -Append "$art\codes.txt"
}
Pop-Location
$env:PYTHONIOENCODING = 'utf-8'
python "$art\rmx_tails.py" "$store\AS80_lu_front.rmx" "$store\AS80_point0.rmx" "$root\tools\CORPUS\corpus\geometries\AS80_lu_front.rmx" "$root\tools\CORPUS\corpus\geometries\AS80_point0.rmx" > "$art\ctrl_b_tails.txt" 2>&1
"ctrl_b tails code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
