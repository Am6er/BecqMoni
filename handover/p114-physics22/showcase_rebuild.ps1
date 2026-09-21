Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$env:PYTHONIOENCODING = 'utf-8'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
"start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File D:\BqMoni_Claude\p114\art\showcase_rebuild_codes.txt
& 'tools\fsa_showcase\rebuild_store.ps1' -Force *> D:\BqMoni_Claude\p114\art\showcase_rebuild_store.log
"rebuild_store code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append D:\BqMoni_Claude\p114\art\showcase_rebuild_codes.txt
