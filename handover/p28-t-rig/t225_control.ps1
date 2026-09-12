# П28 12.09.2026, `T225` — положительный контроль спора за место в плане.
# Плечо «до» — HEAD-копия appwd_plan.ps1 (байтами), «после» — живой файл; каждое
# плечо в СВОЁМ дочернем pwsh (dot-source двух версий в один сеанс — перекрытие функций).
$ErrorActionPreference = 'Continue'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$log  = "$repo\handover\p28-t-rig\t225_control.log"
function L($s) { $s | Out-File -Append -LiteralPath $log; Write-Host $s }
"start $(Get-Date -Format 'HH:mm:ss')" | Out-File -LiteralPath $log
$bin = "$repo\BecquerelMonitor\bin\Release_P28t"; $pb = "$repo\tools\effmaker\probes\build_p28t"; $wd = "$repo\tools\CORPUS\scripts\wd_p28t"

# Счёт споров тем же способом на обоих плечах: группы Dst с >1 РАЗНЫМ Src, sha различны
$count = @'
$g = $plan.Pairs | Group-Object { $_.Dst.ToLowerInvariant() } | Where-Object { @($_.Group | Select-Object -ExpandProperty Src -Unique).Count -gt 1 }
$n = 0
foreach ($x in $g) { $h = @($x.Group | ForEach-Object { (Get-FileHash -LiteralPath $_.Src -Algorithm SHA256).Hash } | Select-Object -Unique); if ($h.Count -gt 1) { $n++; "  спор: " + $x.Group[0].Dst; foreach ($p in $x.Group) { "     <- [" + $p.Why + "] " + $p.Src + "  sha " + (Get-FileHash -LiteralPath $p.Src -Algorithm SHA256).Hash.Substring(0,12) } } }
"споров (Dst с разными sha источников): $n"
"пар всего: " + @($plan.Pairs).Count + "; по родам: " + (($plan.Pairs | Group-Object Why | Sort-Object Name | ForEach-Object { $_.Name + '=' + $_.Count }) -join ', ')
'@

L "=== 1. ДО (HEAD appwd_plan.ps1): план -ProbeCatalog на живом дереве ==="
$o = pwsh -NoProfile -Command ". '$repo\handover\p28-t-rig\appwd_plan_HEAD.ps1'; `$plan = Get-AppWdPlan -Repo '$repo' -Bin '$bin' -Wd '$pb' -ProbeBuild '$pb' -ProbeCatalog; $count" 2>&1 | Out-String
L $o.Trim()

L "=== 2. ПОСЛЕ (живой appwd_plan.ps1): план -ProbeCatalog — ждём 0 споров и ни одной пары корпуса ==="
$o = pwsh -NoProfile -Command ". '$repo\tools\CORPUS\scripts\appwd_plan.ps1'; `$plan = Get-AppWdPlan -Repo '$repo' -Bin '$bin' -Wd '$pb' -ProbeBuild '$pb' -ProbeCatalog; $count" 2>&1 | Out-String
L $o.Trim()

L "=== 3. ПОСЛЕ: план оснастки КОРПУСА (wd_p28t) — ждём 0 споров, приборы и матрицы на месте ==="
$o = pwsh -NoProfile -Command ". '$repo\tools\CORPUS\scripts\appwd_plan.ps1'; `$plan = Get-AppWdPlan -Repo '$repo' -Bin '$bin' -Wd '$wd' -ProbeBuild '$pb'; $count" 2>&1 | Out-String
L $o.Trim()

L "=== 4. Test-AppWdPairClash на подложенных парах ==="
$tmp = Join-Path $env:TEMP ('p28_t225_' + [guid]::NewGuid().ToString('N').Substring(0,8)); New-Item -ItemType Directory -Force $tmp | Out-Null
'AAA' | Set-Content "$tmp\a.txt"; 'BBB' | Set-Content "$tmp\b.txt"; 'AAA' | Set-Content "$tmp\c.txt"
$o = pwsh -NoProfile -Command @"
. '$repo\tools\CORPUS\scripts\appwd_plan.ps1'
function P(`$s, `$w) { [pscustomobject]@{ Src = `$s; Dst = 'X:\wd\config\x.xml'; Why = `$w } }
'a->X, b->X (разное содержимое): ' + @(Test-AppWdPairClash -Pairs @((P '$tmp\a.txt' 'один'), (P '$tmp\b.txt' 'другой'))).Count + ' (ждём 1)'
'a->X, c->X (одинаковое содержимое): ' + @(Test-AppWdPairClash -Pairs @((P '$tmp\a.txt' 'один'), (P '$tmp\c.txt' 'другой'))).Count + ' (ждём 0)'
'a->X, a->X (тот же файл дважды): ' + @(Test-AppWdPairClash -Pairs @((P '$tmp\a.txt' 'один'), (P '$tmp\a.txt' 'другой'))).Count + ' (ждём 0)'
'a->X, нет файла->X: ' + @(Test-AppWdPairClash -Pairs @((P '$tmp\a.txt' 'один'), (P '$tmp\zzz.txt' 'другой'))).Count + ' (ждём 0: о пропавшем кричит Test-AppWdPlan)'
'a->X, b->Y (разные места): ' + @(Test-AppWdPairClash -Pairs @((P '$tmp\a.txt' 'один'), [pscustomobject]@{ Src = '$tmp\b.txt'; Dst = 'X:\wd\y.xml'; Why = 'другой' })).Count + ' (ждём 0)'
'текст находки:'; Test-AppWdPairClash -Pairs @((P '$tmp\a.txt' 'один'), (P '$tmp\b.txt' 'другой'))
"@ 2>&1 | Out-String
L $o.Trim()
Remove-Item $tmp -Recurse -Force

L "=== 5. проводка: подложенный спор -> New-AppWdPlanOrDie обязан выйти кодом 6 ==="
$o = pwsh -NoProfile -Command @"
. '$repo\tools\CORPUS\scripts\appwd_plan.ps1'
function Test-AppWdPairClash { param([array]`$Pairs) @('X:\wd\config\x.xml  <- подложенный спор') }
`$p = New-AppWdPlanOrDie -Repo '$repo' -Bin '$bin' -Wd '$wd' -ProbeBuild '$pb'
'НЕ ДОЛЖНО ПЕЧАТАТЬСЯ: план построился'
"@ 2>&1 | Out-String
L $o.Trim(); L ("код = $LASTEXITCODE (ждём 6)")

L "=== 6. живая проводка: mk_appwd -Force собирает оснастку по плану с проверкой спора (ждём: отметка, код 0) ==="
$o = & "$repo\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $pb -Force *>&1 | Out-String
L (($o -split "`n" | Where-Object { $_ -match 'положено|ПРОТИВОРЕЧИВ|ПРОЩЕНО|САМОПРОВЕРКА' }) -join "`n"); L ("код = $LASTEXITCODE; отметка: " + (Test-Path "$wd\.appwd.json"))
L "done $(Get-Date -Format 'HH:mm:ss')"
