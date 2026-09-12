# П28 12.09.2026, `T80` (1)–(3) — положительные контроли на своей оснастке wd_p28t.
# (1) junction: config\device уводится во временный каталог и подменяется junction
#     на него; за junction подкладываются Ghost_p28.xml и Ghost_p28.dll. «До» — HEAD
#     appwd_plan.ps1 в дочернем pwsh (обход без -FollowSymlink), «после» — живой.
# (2) код check_appwd: четыре чужих .dll + два T41 соседей = 6 находок; HEAD-копия
#     check_appwd.ps1 (с живым appwd_plan) выходит числом находок = 6 — неотличимо
#     от «план не строится»; живой — 1 и печатает «код 1: находок 6»; а «план не
#     строится» (-ProbeBuild в никуда) — 6 у обоих.
# (3) $LASTEXITCODE=77 перед mk_appwd → после удачной сборки 0 (у скрипта без exit — 77).
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$log  = "$repo\handover\p28-t-rig\t80_control.log"
$scripts = "$repo\tools\CORPUS\scripts"
$wd = "$scripts\wd_p28t"
$bin = "$repo\BecquerelMonitor\bin\Release_P28t"; $pb = "$repo\tools\effmaker\probes\build_p28t"
function L($s) { $s | Out-File -Append -LiteralPath $log; Write-Host $s }
"start $(Get-Date -Format 'HH:mm:ss')" | Out-File -LiteralPath $log
Set-Location $repo

# ── (1) junction
L "=== (1) junction: config\device -> сторонний каталог с Ghost_p28.xml и Ghost_p28.dll ==="
$ext = Join-Path $env:TEMP ('p28_dev_ext_' + [guid]::NewGuid().ToString('N').Substring(0,8))
Move-Item -LiteralPath "$wd\config\device" -Destination $ext
New-Item -ItemType Junction -Path "$wd\config\device" -Target $ext | Out-Null
'<x/>' | Set-Content "$ext\Ghost_p28.xml"
[IO.File]::WriteAllBytes("$ext\Ghost_p28.dll", [byte[]](1..16))
L ("junction стоит: " + ((Get-Item "$wd\config\device" -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) + "; Test-Path Ghost за ним: " + (Test-Path "$wd\config\device\Ghost_p28.dll"))
$count = @'
$p = Get-AppWdPlan -Repo $repo -Bin $bin -Wd $wd -ProbeBuild $pb
$x = @(Get-AppWdExtra -Plan $p)
"обход видит файлов вне плана: " + $x.Count + "; из них Ghost_p28: " + @($x | Where-Object { $_.Rel -match 'Ghost_p28' }).Count
$t = Test-AppWdPlan -Plan $p
"Test-AppWdPlan: отказов " + @($t.Bad).Count + ", предупреждений " + @($t.PSObject.Properties['Warn'] | ForEach-Object { $t.Warn }).Count + ", сошлось " + $t.Ok
@($t.Bad) | Where-Object { $_ -match 'Ghost_p28' } | ForEach-Object { "   отказ: " + ($_ -split "`n")[0] }
'@
foreach ($arm in @(@('ДО (HEAD appwd_plan.ps1)', "$repo\handover\p28-t-rig\appwd_plan_HEAD.ps1"), @('ПОСЛЕ (живой appwd_plan.ps1)', "$scripts\appwd_plan.ps1"))) {
    L ("--- " + $arm[0])
    $o = pwsh -NoProfile -Command "`$repo='$repo'; `$bin='$bin'; `$wd='$wd'; `$pb='$pb'; . '$($arm[1])'; $count" 2>&1 | Out-String
    L $o.Trim()
}
# вернуть как было: снять ТОЛЬКО точку разбора, каталог — назад
[IO.Directory]::Delete("$wd\config\device")
Remove-Item "$ext\Ghost_p28.xml", "$ext\Ghost_p28.dll" -Force
Move-Item -LiteralPath $ext -Destination "$wd\config\device"
L ("восстановлено: junction снят " + (-not ((Get-Item "$wd\config\device" -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) + ", приборов " + @(Get-ChildItem "$wd\config\device\*.xml" -File -Force).Count + ", матриц " + @(Get-ChildItem "$wd\config\device\response\*.rmx" -File -Force).Count)

# ── (2) код check_appwd
L "=== (2) четыре чужих .dll в корне оснастки (+2 находки T41 соседей = 6) ==="
$dlls = 1..4 | ForEach-Object { "$wd\Chuzhoy_p28_$_.dll" }
foreach ($d in $dlls) { [IO.File]::WriteAllBytes($d, [byte[]](1..16)) }
$o = & "$scripts\check_appwd_HEAD_p28.ps1" -Wd $wd *>&1 | Out-String; $c = $LASTEXITCODE
L ("HEAD check_appwd: код = $c  (число находок; неотличимо от «план не строится»)")
$o = & "$scripts\check_appwd.ps1" -Wd $wd *>&1 | Out-String; $c = $LASTEXITCODE
L ("живой check_appwd: код = $c; " + (($o -split "`n" | Where-Object { $_ -match 'код 1: находок' }) -join ''))
Remove-Item $dlls -Force
L "--- «план не строится» (-ProbeBuild в никуда): ждём 6 у обоих"
$o = & "$scripts\check_appwd_HEAD_p28.ps1" -Wd $wd -ProbeBuild "$repo\tools\effmaker\probes\net_takogo_p28" *>&1 | Out-String; L ("HEAD: код = $LASTEXITCODE")
$o = & "$scripts\check_appwd.ps1" -Wd $wd -ProbeBuild "$repo\tools\effmaker\probes\net_takogo_p28" *>&1 | Out-String; L ("живой: код = $LASTEXITCODE")
L "--- чистая оснастка (только T41 соседей): живой check_appwd"
$o = & "$scripts\check_appwd.ps1" -Wd $wd *>&1 | Out-String; L ("живой: код = $LASTEXITCODE; " + (($o -split "`n" | Where-Object { $_ -match 'код 1: находок' }) -join ''))

# ── (3) exit 0 у mk_appwd
L "=== (3) `$LASTEXITCODE = 77 перед mk_appwd -Force; после — ждём 0 ==="
$stand = Join-Path $env:TEMP 'p28_noexit.ps1'; "Write-Host 'скрипт без exit'" | Set-Content $stand
cmd /c "exit 77"; L ("стенд: скрипт БЕЗ exit — было $LASTEXITCODE, ") ; & $stand | Out-Null; L ("   после него `$LASTEXITCODE = $LASTEXITCODE (остался чужой)")
Remove-Item $stand -Force
cmd /c "exit 77"; L ("перед mk_appwd: `$LASTEXITCODE = $LASTEXITCODE")
$o = & "$scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $pb -Force *>&1 | Out-String
L ("после mk_appwd: `$LASTEXITCODE = $LASTEXITCODE; отметка: " + (Test-Path "$wd\.appwd.json") + "; " + (($o -split "`n" | Where-Object { $_ -match 'положено|ПРОЩЕНО' }) -join ' | '))
L "done $(Get-Date -Format 'HH:mm:ss')"
