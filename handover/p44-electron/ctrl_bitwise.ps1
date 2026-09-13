# П44 13.09.2026 — приёмка «побитовость ВЫКЛ»: сборка build_p44 (ключи ElectronAnyMaterial и
# BremAlongPath заведены, умолчания ВЫКЛ) считает AS80_point0 УМОЛЧАНИЯМИ пробы и обязана дать
# матрицу, ТОЖДЕСТВЕННУЮ живому складу по телу (MatrixDiffProbe 0.000/0.000/0.00, клеймо то же).
# Рецепт склада: 140 узлов, 3 млн историй, --threads=10 --target=0. Проба ПИШЕТ .rmx в --dir —
# потому копия сцены в D:\BqMoni_Claude\p44, живой склад только читается.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p44"
$art = 'D:\BqMoni_Claude\p44\bitwise'
$dir = "$art\ctrl"
$store = "$root\tools\CORPUS\corpus\geometries"
$scene = 'AS80_point0'
New-Item -ItemType Directory -Force $dir | Out-Null
Remove-Item "$dir\*.rmx" -ErrorAction SilentlyContinue
Copy-Item "$store\$scene.in" "$dir\$scene.in" -Force
"bitwise start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
"sha256 store BEFORE $((Get-FileHash "$store\$scene.rmx").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
Push-Location $bin
$sw = [Diagnostics.Stopwatch]::StartNew()
& "$bin\CorpusMatrixProbe.exe" "--dir=$dir" --threads=10 --target=0 --force > "$art\matrix.log" 2>&1
"matrix code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
& "$bin\MatrixDiffProbe.exe" "--a=$store\$scene.rmx" "--b=$dir\$scene.rmx" > "$art\diff.log" 2>&1
"diff code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
Pop-Location
"sha256 store AFTER  $((Get-FileHash "$store\$scene.rmx").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"sha256 ctrl         $((Get-FileHash "$dir\$scene.rmx").Hash.ToLower())" | Out-File -Append "$art\codes.txt"
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
