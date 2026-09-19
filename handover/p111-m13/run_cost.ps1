# П111 — цена ключа lbang: CorpusMatrixProbe --threads=1 --nodes=3 --n=600000 --target=0 на копии RC103_point0, парами ABAB
# (ВЫКЛ/ВКЛ/ВЫКЛ/ВКЛ), как П106 §5.1. Печатает мкс на историю из лога пробы. ⚠ Машина в этот час делится с чужим счётом
# (MatrixRecomputeProbe другой полосы) — отношение ВКЛ/ВЫКЛ читать по парам, абсолют не сравнивать с П106.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$probes = 'D:\BqMoni_Claude\p111\wt\tools\effmaker\probes\build_p111'
$live = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$root = 'D:\BqMoni_Claude\p111'
$art = "$root\bitwise"
$codes = "$art\codes_cost.txt"
New-Item -ItemType Directory -Force "$root\mx\cost" | Out-Null
Copy-Item "$live\RC103_point0.in" "$root\mx\cost\" -Force
Copy-Item "$live\index.csv" "$root\mx\cost\" -Force
Push-Location $probes
foreach ($i in 1, 2) {
    foreach ($lb in 0, 1) {
        Remove-Item "$root\mx\cost\RC103_point0.rmx" -ErrorAction SilentlyContinue
        $t0 = Get-Date
        & .\CorpusMatrixProbe.exe "--dir=$root\mx\cost" --only=RC103_point0 "--lbang=$lb" --threads=1 --nodes=3 --n=600000 --target=0 --force *> "$art\cost_${i}_lbang$lb.log"
        $dt = ((Get-Date) - $t0).TotalSeconds
        $us = (Select-String -Path "$art\cost_${i}_lbang$lb.log" -Pattern 'мкс на историю' | Select-Object -First 1).Line
        "cost pair $i lbang=$lb code=$LASTEXITCODE dt=$([int]$dt)s :: $us" | Out-File -Append $codes
    }
}
Pop-Location
Get-Content $codes
