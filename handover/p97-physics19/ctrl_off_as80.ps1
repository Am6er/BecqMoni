# П97 — второе плечо --eltr=0 (положительный контроль): AS80_point0 той же сборкой в store_off,
# тело обязано побитово совпасть с живым складом физики 18 (MatrixDiffProbe).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$probes = 'D:\BqMoni_Claude\p97\wt\tools\effmaker\probes\build_p97'
$off = 'D:\BqMoni_Claude\p97\store_off'
$live = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$art = 'D:\BqMoni_Claude\p97\art'
$codes = "$art\ctrl_first2_codes.txt"
Copy-Item "$live\AS80_point0.in" $off -Force
Push-Location $probes
"off AS80 start $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
& .\CorpusMatrixProbe.exe "--dir=$off" --only=AS80_point0 --eltr=0 --threads=10 --target=0 --n=3000000 "--dump=$art\dumps\off_as80.csv" *> "$art\ctrl_off_as80_matrix.log"
"matrix off AS80 code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
& .\MatrixDiffProbe.exe "--a=$live\AS80_point0.rmx" "--b=$off\AS80_point0.rmx" *> "$art\diff_off_AS80_point0.log"
"diff off AS80_point0 code=$LASTEXITCODE" | Out-File -Append $codes
Pop-Location
Get-Content $codes | Select-Object -Last 3
