# П50 13.09.2026 — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ (б): две сцены склада УМОЛЧАНИЯМИ нового кода (ecomp=1,
# bpath=2, физика 18), рецепт склада --threads=10 --target=0, В СКЛАД WORKTREE
# (D:\BqMoni_Claude\p50\wt\tools\CORPUS\corpus\geometries — там .in из git и ни одного .rmx).
# Сцены: AS80_point0 (лёгкая, 318…353 с у физики 17) и AS80_th_disk (тяжёлая по П44 — ecomp ×1.13,
# 697 с у физики 17). Что проверяется ДО многочасового счёта (A77): клеймо phys=18 и хвосты
# ECMP=1/BPTH=2 (rmx_tails.py), время на сцену против физики 17 (оценка на 45), числа разумны —
# MatrixDiffProbe и rmx_nodes.py против живого склада физики 17.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'D:\BqMoni_Claude\p50\wt'
$bin = "$wt\tools\effmaker\probes\build_p50"
$art = 'D:\BqMoni_Claude\p50\art'
$store = "$wt\tools\CORPUS\corpus\geometries"
$scenes = 'AS80_point0', 'AS80_th_disk'
"ctrl_b start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'); rmx in store before: $((Get-ChildItem $store -Filter *.rmx).Count)" | Out-File -Append "$art\codes.txt"
Push-Location $bin
& "$bin\CorpusMatrixProbe.exe" "--dir=$store" "--only=$($scenes -join ',')" --threads=10 --target=0 --force > "$art\ctrl_b_matrix.log" 2>&1
"ctrl_b matrix code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
foreach ($scene in $scenes) {
    & "$bin\MatrixDiffProbe.exe" "--a=$root\tools\CORPUS\corpus\geometries\$scene.rmx" "--b=$store\$scene.rmx" > "$art\ctrl_b_diff_$scene.log" 2>&1
    "ctrl_b diff $scene code=$LASTEXITCODE" | Out-File -Append "$art\codes.txt"
}
Pop-Location
$env:PYTHONIOENCODING = 'utf-8'
$args_ = @()
foreach ($scene in $scenes) { $args_ += "$store\$scene.rmx"; $args_ += "$root\tools\CORPUS\corpus\geometries\$scene.rmx" }
python "$root\handover\p50-physics18\rmx_tails.py" @args_ > "$art\ctrl_b_tails.txt" 2>&1
"ctrl_b tails code=$LASTEXITCODE" | Out-File -Append "$art\codes.txt"
foreach ($scene in $scenes) {
    python "$root\handover\p50-physics18\rmx_nodes.py" "$root\tools\CORPUS\corpus\geometries\$scene.rmx" "$store\$scene.rmx" '31.5,60,120,239,583,662,911,1461,2614' > "$art\ctrl_b_nodes_$scene.txt" 2>&1
    "ctrl_b nodes $scene code=$LASTEXITCODE" | Out-File -Append "$art\codes.txt"
}
"ctrl_b end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
