# Нагрузка: N занятых процессов pwsh на ~90 с, поверх неё — сторож. Воспроизведение гонки П107.
param([int]$N = 16, [string]$Log = 'D:\BqMoni_Claude\p108\loaded_run.log')
$procs = @()
for ($i = 0; $i -lt $N; $i++) {
    $procs += Start-Process -PassThru -WindowStyle Hidden pwsh -ArgumentList '-NoProfile','-Command','$t=[Diagnostics.Stopwatch]::StartNew(); while($t.Elapsed.TotalSeconds -lt 120){ [math]::Sqrt(12345.678) | Out-Null }'
}
Start-Sleep -Seconds 3
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$env:PYTHONIOENCODING = 'utf-8'; $env:PYTHONUTF8 = '1'
python tools/check_fsa_report_view.py --keep *> $Log
$code = $LASTEXITCODE
$procs | ForEach-Object { try { Stop-Process -Id $_.Id -Force -ErrorAction Stop } catch {} }
"код сторожа под нагрузкой: $code"
exit $code
