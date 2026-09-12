# П24: пересчёт кривых эффективности ВСЕХ сцен корпуса (решение Amber 12.09.2026: CorpusEffProbe --force по 44 сценам),
# из стенда Release_P24 — кривая и матрица одной физикой (kdip=1 в клейме). Пишет узлы <Efficiency> в corpus\spectra\*.xml
# и копирует <key>.rmx -> response\<guid>.rmx (побайтно те же). Лог — eff_force.log.
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'C:\Users\moroz\bqp24'
[System.IO.Directory]::SetCurrentDirectory($root)
Set-Location $root
$t0 = Get-Date
& "$root\tools\effmaker\probes\build_p24\CorpusEffProbe.exe" --force 2>&1 | Out-File -Encoding utf8 "$root\handover\p24-out-rev19\eff_force.log"
$rc = $LASTEXITCODE
$line = "eff --force exit={0} ({1} s)" -f $rc, [int]((Get-Date)-$t0).TotalSeconds
Write-Output $line
Add-Content -Encoding utf8 "$root\handover\p24-out-rev19\run.status.txt" ("{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $line)
exit $rc
