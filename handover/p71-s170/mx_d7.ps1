# П71 (S170): матрица сцены G1S_point5 со сдвигом источника Δ = +7 мм (pdistance 5.7 см) — в СВОЙ склад
# D:\BqMoni_Claude\p71\store (живой склад не трогается). Параметры — умолчания класса (физика 18) + --target=0,
# как у единого счёта склада (README корпуса §2.5). Затем — раскладка по guid внутри своего склада и сайдкар Q_k.
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$wt = 'D:\BqMoni_Claude\p71\wt'
$g = "$wt\tools\CORPUS\corpus\geometries"
$st = 'D:\BqMoni_Claude\p71\store'
New-Item -ItemType Directory -Force $st | Out-Null
(Get-Content "$g\G1S_point5.in" -Raw) -replace 'pdistance = 5 cm', 'pdistance = 5.7 cm' | Set-Content "$st\G1S_point5.in" -NoNewline
Select-String 'pdistance' "$st\G1S_point5.in"
Set-Location "$wt\tools\effmaker\probes\build_p71a"
$sw = [Diagnostics.Stopwatch]::StartNew()
.\CorpusMatrixProbe.exe "--dir=$st" --only=G1S_point5 --threads=12 --target=0
"matrix code $LASTEXITCODE ($([int]$sw.Elapsed.TotalSeconds) s)"
Set-Location $wt
python tools\CORPUS\scripts\mx_swap.py "--from=$st" "--into=$st"
"mx_swap code $LASTEXITCODE"
Set-Location "$wt\tools\effmaker\probes\build_p71a"
.\AngularQkProbe.exe "--store=$st" --scene=G1S_point5 --n=300000 --threads=12
"qk code $LASTEXITCODE ($([int]$sw.Elapsed.TotalSeconds) s)"
Get-ChildItem -Recurse $st | Select-Object FullName, Length
"DONE"
