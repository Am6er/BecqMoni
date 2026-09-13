# П38 13.09.2026 — контроль шага 0: сборка основного дерева ПОСЛЕ переворота умолчаний полей
# EfficiencySimulator (одно место истины) считает AS80_point0 УМОЛЧАНИЯМИ (ни одного ключа физики) и
# обязана дать матрицу, ТОЖДЕСТВЕННУЮ складу worktree П37 по телу (MatrixDiffProbe 0.000/0.000/0.00,
# клеймо то же): путь склада ставит все поля явно от ResponseMatrixOptions, и умолчания полей
# симулятора его не трогают. Рецепт склада: 140 узлов, 3 млн историй, --threads=10 --target=0.
# Проба ПИШЕТ .rmx в --dir — потому копия сцены в scratch, склады только читаются.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p38"
$art = "$root\handover\p38-rev21"
$dir = 'C:\Users\moroz\AppData\Local\Temp\claude\C--Users-moroz-source-repos-BQ-Eng-res--NET-4-8\c622bbcd-f473-47e4-9f7b-f2056ac28f0a\scratchpad\ctrl0'
$wt = 'C:\Users\moroz\bqp37\tools\CORPUS\corpus\geometries'
$scene = 'AS80_point0'
New-Item -ItemType Directory -Force $dir | Out-Null
Remove-Item "$dir\*.rmx" -ErrorAction SilentlyContinue
Copy-Item "$wt\$scene.in" "$dir\$scene.in" -Force
"ctrl0 start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Push-Location $bin
& "$bin\CorpusMatrixProbe.exe" "--dir=$dir" --threads=10 --target=0 --force > "$art\ctrl0_matrix.log" 2>&1
"ctrl0 matrix code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
& "$bin\MatrixDiffProbe.exe" "--a=$wt\$scene.rmx" "--b=$dir\$scene.rmx" > "$art\ctrl0_diff.log" 2>&1
"ctrl0 diff code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
"ctrl0 sha256 wt   $((Get-FileHash "$wt\$scene.rmx").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"ctrl0 sha256 ctrl $((Get-FileHash "$dir\$scene.rmx").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
