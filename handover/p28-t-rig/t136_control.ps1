# П28 12.09.2026, `T136` — положительный контроль достройки относительных путей.
# Каталог .NET-процесса ([Environment]::CurrentDirectory) ставится в C:\Users\moroz
# ЯВНО перед каждым вызовом, каталог PowerShell (Get-Location) — дерево: так
# GetFullPath (до правки) и GetUnresolvedProviderPathFromPSPath (после) расходятся,
# и разница видна числом. Плечо «до» — копии HEAD-версий скриптов (`git show HEAD:…`
# байтами, через Bash), положенные рядом с живыми (тем же $PSScriptRoot).
# `-Force` у mk/run: приложение собрано 20:40, а сосед П27 правит EfficiencyMaker\*.cs
# каждую минуту (T41 у build_all — см. первый прогон) — здесь мерится, КУДА ложится
# результат, а не числа прогона. Лог — t136_control.log.
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$log  = "$repo\handover\p28-t-rig\t136_control.log"
function L($s) { $s | Out-File -Append -LiteralPath $log; Write-Host $s }
function Mis { [Environment]::CurrentDirectory = 'C:\Users\moroz'; Set-Location $repo }
"start $(Get-Date -Format 'HH:mm:ss')" | Out-File -LiteralPath $log
Mis
L ("процесс : " + [Environment]::CurrentDirectory)
L ("PowerShell: " + (Get-Location).ProviderPath)

$probes  = "$repo\tools\effmaker\probes"
$scripts = "$repo\tools\CORPUS\scripts"
$headBA  = "$probes\build_all_HEAD_p28.ps1"
$headMK  = "$scripts\mk_appwd_HEAD_p28.ps1"
$headRUN = "$scripts\run_appwd_HEAD_p28.ps1"

# ── 1. build_all.ps1 ДО: относительный -Bin уезжает в каталог процесса → код 2
L "=== 1. build_all HEAD, относительные -Bin/-Out (ждём: -Bin от C:\Users\moroz, код 2) ==="
Mis
$o = & $headBA -Bin 'BecquerelMonitor\bin\Release_P28t' -Out 'tools\effmaker\probes\build_p28t_before' *>&1 | Out-String
$c = $LASTEXITCODE
L ($o.Trim()); L ("код = $c")
L ("каталог C:\Users\moroz\tools\effmaker\probes\build_p28t_before создан: " + (Test-Path 'C:\Users\moroz\tools\effmaker\probes\build_p28t_before'))

# ── 2. build_all.ps1 ПОСЛЕ — уже измерено первым прогоном (build_all_p28t.log / t136_control.stdout
#       первого захода): пути легли в дерево, 385 файлов; код 1 — T41 от правок соседа. Здесь не повторяется.

# ── 3. mk_appwd.ps1 ДО: относительный -Wd → «вне образца», код 7
L "=== 3. mk_appwd HEAD, относительный -Wd (ждём: -Wd от C:\Users\moroz, код 7) ==="
Mis
$o = & $headMK -Bin 'BecquerelMonitor\bin\Release_P28t' -Wd 'tools\CORPUS\scripts\wd_p28t' -ProbeBuild 'tools\effmaker\probes\build_p28t' *>&1 | Out-String
$c = $LASTEXITCODE
L ($o.Trim()); L ("код = $c")

# ── 4. mk_appwd.ps1 ПОСЛЕ: те же относительные пути → оснастка в дереве
L "=== 4. mk_appwd ПОСЛЕ правки, те же относительные пути, -Force (ждём: оснастка в дереве, отметка есть) ==="
Mis
$o = & "$scripts\mk_appwd.ps1" -Bin 'BecquerelMonitor\bin\Release_P28t' -Wd 'tools\CORPUS\scripts\wd_p28t' -ProbeBuild 'tools\effmaker\probes\build_p28t' -Force *>&1 | Out-String
$c = $LASTEXITCODE
$o | Out-File -LiteralPath "$repo\handover\p28-t-rig\mk_appwd_p28t.log"
L (($o -split "`n" | Where-Object { $_ -match 'рабочий каталог|положено|ОТКАЗ|ПРОЩЕНО|Force|СВЕЖАЯ|вынесено|постороннего' }) -join "`n")
L ("код = $c")
L ("отметка есть: " + (Test-Path "$scripts\wd_p28t\.appwd.json"))
if (-not (Test-Path "$scripts\wd_p28t\.appwd.json")) { L "оснастки нет — дальше не идём"; exit 11 }

# ── 5. run_appwd.ps1 ДО: абсолютный -Wd, относительный -Out → результат внутри оснастки
L "=== 5. run_appwd HEAD, относительный -Out, -Force (ждём: результат в wd_p28t\handover\..., код 0) ==="
Mis
$o = & $headRUN -Wd "$scripts\wd_p28t" -Out 'handover\p28-t-rig\out_t136_before' -Extra '--only=ASN16_Th232' -Force *>&1 | Out-String
$c = $LASTEXITCODE
$o | Out-File -LiteralPath "$repo\handover\p28-t-rig\run_before.log"
L (($o -split "`n" | Where-Object { $_ -match 'запуск:|СВЕЖАЯ|ОТКАЗ|Force' }) -join "`n")
L ("код = $c")
L ("в дереве   $repo\handover\p28-t-rig\out_t136_before : " + (Test-Path "$repo\handover\p28-t-rig\out_t136_before"))
L ("в оснастке $scripts\wd_p28t\handover\p28-t-rig\out_t136_before : " + (Test-Path "$scripts\wd_p28t\handover\p28-t-rig\out_t136_before"))

# ── 6. run_appwd.ps1 ПОСЛЕ: относительные -Wd и -Out → результат в дереве
L "=== 6. run_appwd ПОСЛЕ правки, относительные -Wd и -Out, -Force (ждём: результат в дереве, код 0) ==="
Mis
$o = & "$scripts\run_appwd.ps1" -Wd 'tools\CORPUS\scripts\wd_p28t' -Out 'handover\p28-t-rig\out_t136_after' -Extra '--only=ASN16_Th232' -Force *>&1 | Out-String
$c = $LASTEXITCODE
$o | Out-File -LiteralPath "$repo\handover\p28-t-rig\run_after.log"
L (($o -split "`n" | Where-Object { $_ -match 'запуск:|СВЕЖАЯ|ОТКАЗ|Force' }) -join "`n")
L ("код = $c")
L ("в дереве   $repo\handover\p28-t-rig\out_t136_after : " + (Test-Path "$repo\handover\p28-t-rig\out_t136_after"))
L ("в оснастке $scripts\wd_p28t\handover\p28-t-rig\out_t136_after : " + (Test-Path "$scripts\wd_p28t\handover\p28-t-rig\out_t136_after"))

# продукт плеча «до» из оснастки убираем (свой каталог, свой продукт)
if (Test-Path "$scripts\wd_p28t\handover") { Remove-Item -LiteralPath "$scripts\wd_p28t\handover" -Recurse -Force }
L "done $(Get-Date -Format 'HH:mm:ss')"
exit 0
