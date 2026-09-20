# П88: сборка проб в worktree (build_all.ps1 -> build_p88). Код возврата — код build_all.
$env:OS = 'Windows_NT'
$ErrorActionPreference = 'Continue'
$wt = 'D:\BqMoni_Claude\p88\wt'
"start probes $(Get-Date -Format 'HH:mm:ss')"
& pwsh -NoProfile -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin "$wt\BecquerelMonitor\bin\Release_p88" -Out "$wt\tools\effmaker\probes\build_p88" > "D:\BqMoni_Claude\p88\build_probes.log" 2>&1
$c3 = $LASTEXITCODE; "probes code=$c3 $(Get-Date -Format 'HH:mm:ss')"
if ($c3 -ne 0) { "PROBES FAILED"; exit 3 }
"PROBES OK"
exit 0
