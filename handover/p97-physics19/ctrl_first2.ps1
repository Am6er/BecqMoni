# П97 — контроль на ПЕРВЫХ ДВУХ сценах (A77) физикой 19 (ключ eltr ВКЛ умолчанием) рецептом склада
# --threads=10 --target=0 --n=3000000 (S140: рецепт, не умолчание): G1S_point5 (23 спектра корпуса) и
# RC103_point0 (1 см³ CsI в обвязке — сцена, где ключ двигает больше всего). Пишет в СВОЙ склад
# D:\BqMoni_Claude\p97\store (живому складу --dir не даётся).
# Затем положительный контроль: RC103_point0 той же сборкой с --eltr=0 в ОТДЕЛЬНЫЙ каталог store_off —
# тело обязано побитово совпасть с живым складом физики 18 (кроме ключа ничего не сдвинулось).
# Потом MatrixDiffProbe: новые тела против живых (обязаны ОТЛИЧАТЬСЯ), плечо --eltr=0 против живого (побитово).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$probes = 'D:\BqMoni_Claude\p97\wt\tools\effmaker\probes\build_p97'
$store = 'D:\BqMoni_Claude\p97\store'
$off = 'D:\BqMoni_Claude\p97\store_off'
$live = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$art = 'D:\BqMoni_Claude\p97\art'
$codes = "$art\ctrl_first2_codes.txt"
New-Item -ItemType Directory -Force "$art\dumps" | Out-Null
Push-Location $probes
"start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append $codes
& .\CorpusMatrixProbe.exe "--dir=$store" --only=G1S_point5,RC103_point0 --threads=10 --target=0 --n=3000000 "--dump=$art\dumps\first2.csv" *> "$art\ctrl_first2_matrix.log"
"matrix on code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
& .\CorpusMatrixProbe.exe "--dir=$off" --only=RC103_point0 --eltr=0 --threads=10 --target=0 --n=3000000 "--dump=$art\dumps\off_rc103.csv" *> "$art\ctrl_off_rc103_matrix.log"
"matrix off code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
foreach ($s in 'G1S_point5', 'RC103_point0') {
    & .\MatrixDiffProbe.exe "--a=$live\$s.rmx" "--b=$store\$s.rmx" *> "$art\diff_on_$s.log"
    "diff on $s code=$LASTEXITCODE" | Out-File -Append $codes
}
& .\MatrixDiffProbe.exe "--a=$live\RC103_point0.rmx" "--b=$off\RC103_point0.rmx" *> "$art\diff_off_RC103_point0.log"
"diff off RC103_point0 code=$LASTEXITCODE" | Out-File -Append $codes
Pop-Location
"end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
Get-Content $codes
