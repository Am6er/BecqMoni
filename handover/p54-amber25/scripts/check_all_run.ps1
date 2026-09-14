$env:OS = 'Windows_NT'
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONUTF8 = '1'
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$sw = [Diagnostics.Stopwatch]::StartNew()
python tools\check_all.py *> 'D:\BqMoni_Claude\p54\check_all.log'
"check_all exit=$LASTEXITCODE  $([int]$sw.Elapsed.TotalSeconds) s"
Get-Content 'D:\BqMoni_Claude\p54\check_all.log' -Tail 12
