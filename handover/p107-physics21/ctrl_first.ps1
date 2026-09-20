# П107 — контроли ДО полного счёта (A77), сборка build_p107 (физика 21, lbrem ВКЛ умолчанием). Образец — П103 ctrl_first.ps1.
#  1. A77 на ПЕРВЫХ ДВУХ единицах: --nodes=2 --emin=30 --emax=31 --n=400000 --jnodes=0 на RC103_point0 и
#     ASN16_point10_house в store_check (быстро; код 1 = «шумные» на 400 k — ожидаемо).
#  2. Положительный контроль подъёма версии: --lbrem=0 рецептом склада на RC103_point0 и AS80_point0 в
#     store_off — тело ОБЯЗАНО побитово совпасть с живым складом rev30 (физика 20); клеймо отличается только phys=.
#  3. Контроль ВКЛ: RC103_point0 рецептом склада в store_ctrl — тело ОБЯЗАНО отличаться от живого;
#     позже полный счёт даст ту же сцену в store — тела обязаны совпасть (детерминизм, без --force).
# Пишет только в D:\BqMoni_Claude\p107\ (живому складу --dir не даётся).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$probes = 'D:\BqMoni_Claude\p107\wt\tools\effmaker\probes\build_p107'
$live = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$root = 'D:\BqMoni_Claude\p107'
$art = "$root\art"
$codes = "$art\ctrl_codes.txt"
foreach ($d in 'store_check', 'store_off', 'store_ctrl') {
    New-Item -ItemType Directory -Force "$root\$d" | Out-Null
    Copy-Item "$live\*.in" "$root\$d\" -Force
    Copy-Item "$live\index.csv" "$root\$d\" -Force
}
Push-Location $probes
"start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append $codes
& .\CorpusMatrixProbe.exe "--dir=$root\store_check" --only=RC103_point0,ASN16_point10_house --nodes=2 --emin=30 --emax=31 --n=400000 --threads=10 --target=0 --jnodes=0 --force "--dump=$art\dumps\a77.csv" *> "$art\check_a77.log"
"a77 2 nodes code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
& .\CorpusMatrixProbe.exe "--dir=$root\store_off" --only=RC103_point0 --lbrem=0 --threads=10 --target=0 --n=3000000 "--dump=$art\dumps\off_rc103.csv" *> "$art\ctrl_off_rc103_matrix.log"
"matrix off RC103_point0 code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
& .\MatrixDiffProbe.exe "--a=$live\RC103_point0.rmx" "--b=$root\store_off\RC103_point0.rmx" *> "$art\diff_off_RC103_point0.log"
"diff off RC103_point0 code=$LASTEXITCODE" | Out-File -Append $codes
& .\CorpusMatrixProbe.exe "--dir=$root\store_ctrl" --only=RC103_point0 --threads=10 --target=0 --n=3000000 "--dump=$art\dumps\ctrl_rc103.csv" *> "$art\ctrl_on_rc103_matrix.log"
"matrix on RC103_point0 code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
& .\MatrixDiffProbe.exe "--a=$live\RC103_point0.rmx" "--b=$root\store_ctrl\RC103_point0.rmx" *> "$art\diff_on_RC103_point0.log"
"diff on RC103_point0 code=$LASTEXITCODE" | Out-File -Append $codes
& .\CorpusMatrixProbe.exe "--dir=$root\store_off" --only=AS80_point0 --lbrem=0 --threads=10 --target=0 --n=3000000 "--dump=$art\dumps\off_as80.csv" *> "$art\ctrl_off_as80_matrix.log"
"matrix off AS80_point0 code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
& .\MatrixDiffProbe.exe "--a=$live\AS80_point0.rmx" "--b=$root\store_off\AS80_point0.rmx" *> "$art\diff_off_AS80_point0.log"
"diff off AS80_point0 code=$LASTEXITCODE" | Out-File -Append $codes
Pop-Location
"end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
Get-Content $codes
