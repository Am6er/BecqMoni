# П28 12.09.2026 — попытка чистого прохода: приложение (4 с) -> build_all.ps1 (относительные
# пути, каталог процесса C:\Users\moroz) -> mk_appwd.ps1 без -Force. Окно уязвимо к правкам
# соседей (T41), поэтому отдельный лог: build_chain.log.
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$log = "$repo\handover\p28-t-rig\build_chain.log"
function L($s) { $s | Out-File -Append -LiteralPath $log; Write-Host $s }
"start $(Get-Date -Format 'HH:mm:ss')" | Out-File -LiteralPath $log
[Environment]::CurrentDirectory = 'C:\Users\moroz'
Set-Location $repo
& pwsh -NoProfile -File "$repo\handover\p28-t-rig\build_app.ps1" | Out-Null
L ("приложение: код $LASTEXITCODE, exe " + (Get-Item "$repo\BecquerelMonitor\bin\Release_P28t\BecquerelMonitor.exe").LastWriteTime.ToString('HH:mm:ss'))
[Environment]::CurrentDirectory = 'C:\Users\moroz'
$o = & "$repo\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_P28t' -Out 'tools\effmaker\probes\build_p28t' *>&1 | Out-String
$c = $LASTEXITCODE
$o | Out-File -LiteralPath "$repo\handover\p28-t-rig\build_all_p28t.log"
L (($o -split "`n" | Where-Object { $_ -match '^\s+-Bin|^\s+-Out|ОТКАЗ|T41|сверено рядом|ЗАВЕРЕН|собрались|СОБРАЛИСЬ|пробы: ' } | Select-Object -First 12) -join "`n")
L ("build_all: код $c")
if ($c -eq 0) {
    $o = & "$repo\tools\CORPUS\scripts\mk_appwd.ps1" -Bin 'BecquerelMonitor\bin\Release_P28t' -Wd 'tools\CORPUS\scripts\wd_p28t' -ProbeBuild 'tools\effmaker\probes\build_p28t' *>&1 | Out-String
    $c2 = $LASTEXITCODE
    $o | Out-File -LiteralPath "$repo\handover\p28-t-rig\mk_appwd_p28t.log"
    L (($o -split "`n" | Where-Object { $_ -match 'положено|СВЕЖАЯ|ОТКАЗ|ПРОЩЕНО|сверено' }) -join "`n")
    L ("mk_appwd без -Force: код $c2; отметка " + (Test-Path "$repo\tools\CORPUS\scripts\wd_p28t\.appwd.json"))
    $o = & "$repo\tools\CORPUS\scripts\check_appwd.ps1" -Wd 'tools\CORPUS\scripts\wd_p28t' *>&1 | Out-String
    L ("check_appwd: код $LASTEXITCODE; " + (($o -split "`n" | Where-Object { $_ -match 'СВЕЖАЯ|находок' }) -join ' | '))
}
L "done $(Get-Date -Format 'HH:mm:ss')"
exit $c
