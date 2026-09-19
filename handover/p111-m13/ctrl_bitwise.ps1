# П111 — побитовость ВЫКЛ и положительный контроль ВКЛ ключа lbang (образец — П107 ctrl_first.ps1), сборка build_p111.
#  1. ВЫКЛ (умолчание) рецептом склада на RC103_point0 в store_off — тело ОБЯЗАНО побитово совпасть с живым складом rev31
#     (физика 21, HEAD 0aab363e): ключ ВЫКЛ не тянет ни одного случайного числа.
#  2. ВКЛ (--lbang=1) рецептом склада в store_on — тело ОБЯЗАНО отличаться; клеймо несёт lbang=1.
#  Время каждого счёта — цена ключа (машина в это время свободна от других счётов).
# Пишет только в D:\BqMoni_Claude\p111\ (живому складу --dir не даётся).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$probes = 'D:\BqMoni_Claude\p111\wt\tools\effmaker\probes\build_p111'
$live = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$root = 'D:\BqMoni_Claude\p111'
$art = "$root\bitwise"
New-Item -ItemType Directory -Force $art, "$art\dumps" | Out-Null
$codes = "$art\codes_bitwise.txt"
foreach ($d in 'store_off', 'store_on') {
    New-Item -ItemType Directory -Force "$root\mx\$d" | Out-Null
    Copy-Item "$live\*.in" "$root\mx\$d\" -Force
    Copy-Item "$live\index.csv" "$root\mx\$d\" -Force
}
Push-Location $probes
"start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append $codes
$t0 = Get-Date
& .\CorpusMatrixProbe.exe "--dir=$root\mx\store_off" --only=RC103_point0 --threads=10 --target=0 --n=3000000 "--dump=$art\dumps\off_rc103.csv" *> "$art\ctrl_off_rc103_matrix.log"
"matrix off RC103_point0 code=$LASTEXITCODE dt=$([int]((Get-Date)-$t0).TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
& .\MatrixDiffProbe.exe "--a=$live\RC103_point0.rmx" "--b=$root\mx\store_off\RC103_point0.rmx" *> "$art\diff_off_RC103_point0.log"
"diff off RC103_point0 code=$LASTEXITCODE" | Out-File -Append $codes
$t0 = Get-Date
& .\CorpusMatrixProbe.exe "--dir=$root\mx\store_on" --only=RC103_point0 --lbang=1 --threads=10 --target=0 --n=3000000 "--dump=$art\dumps\on_rc103.csv" *> "$art\ctrl_on_rc103_matrix.log"
"matrix on RC103_point0 code=$LASTEXITCODE dt=$([int]((Get-Date)-$t0).TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
& .\MatrixDiffProbe.exe "--a=$live\RC103_point0.rmx" "--b=$root\mx\store_on\RC103_point0.rmx" *> "$art\diff_on_RC103_point0.log"
"diff on RC103_point0 code=$LASTEXITCODE" | Out-File -Append $codes
Pop-Location
"end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
Get-Content $codes
