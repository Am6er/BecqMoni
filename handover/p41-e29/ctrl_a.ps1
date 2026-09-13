# П41 13.09.2026 — приёмка (а) «побитовость ВЫКЛ»: сборка build_p41 (ключ ImportanceSampling
# заведён, умолчание ВЫКЛ) считает AS80_point0 УМОЛЧАНИЯМИ пробы (ни одного ключа физики) и
# обязана дать матрицу, ТОЖДЕСТВЕННУЮ живому складу по телу (MatrixDiffProbe 0.000/0.000/0.00,
# клеймо то же). Рецепт склада: 140 узлов, 3 млн историй, --threads=10 --target=0. Проба ПИШЕТ
# .rmx в --dir — потому копия сцены в scratch, живой склад только читается.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p41"
$art = "$root\handover\p41-e29"
$dir = 'C:\Users\moroz\AppData\Local\Temp\claude\C--Users-moroz-source-repos-BQ-Eng-res--NET-4-8\c622bbcd-f473-47e4-9f7b-f2056ac28f0a\scratchpad\p41\ctrl_a'
$store = "$root\tools\CORPUS\corpus\geometries"
$scene = 'AS80_point0'
New-Item -ItemType Directory -Force $dir | Out-Null
Remove-Item "$dir\*.rmx" -ErrorAction SilentlyContinue
Copy-Item "$store\$scene.in" "$dir\$scene.in" -Force
"ctrl_a start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"ctrl_a sha256 store BEFORE $((Get-FileHash "$store\$scene.rmx").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
Push-Location $bin
& "$bin\CorpusMatrixProbe.exe" "--dir=$dir" --threads=10 --target=0 --force > "$art\ctrl_a_matrix.log" 2>&1
"ctrl_a matrix code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
& "$bin\MatrixDiffProbe.exe" "--a=$store\$scene.rmx" "--b=$dir\$scene.rmx" > "$art\ctrl_a_diff.log" 2>&1
"ctrl_a diff code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
"ctrl_a sha256 store AFTER  $((Get-FileHash "$store\$scene.rmx").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"ctrl_a sha256 ctrl         $((Get-FileHash "$dir\$scene.rmx").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
