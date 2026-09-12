# П28 12.09.2026, `T117` — положительный контроль: постороннее в оснастке НАЗЫВАЕТСЯ,
# отказ только на исполняемом, ничего не удаляется (решение Amber 27.08.2026, T88).
# База сравнения — ЧИСЛО находок (код check_appwd.ps1 = число находок): нулевой базы
# нет, потому что соседи (П26/П27) правят исходники приложения и проб каждую минуту,
# и T41 («сборка старше исходников») красен у всех. Лог — t117_control.log.
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$log  = "$repo\handover\p28-t-rig\t117_control.log"
$scripts = "$repo\tools\CORPUS\scripts"
$wd = "$scripts\wd_p28t"
$xml = "$wd\config\chuzhoy_p28.xml"
$dll = "$wd\Chuzhoy_p28.dll"
function L($s) { $s | Out-File -Append -LiteralPath $log; Write-Host $s }
function Chk { $o = & "$scripts\check_appwd.ps1" -Wd $wd *>&1 | Out-String; $script:code = $LASTEXITCODE; $o }
function Mk  { $o = & "$scripts\mk_appwd.ps1" -Bin "$repo\BecquerelMonitor\bin\Release_P28t" -Wd $wd -ProbeBuild "$repo\tools\effmaker\probes\build_p28t" -Force *>&1 | Out-String; $script:code = $LASTEXITCODE; $o }
"start $(Get-Date -Format 'HH:mm:ss')" | Out-File -LiteralPath $log
Set-Location $repo
Remove-Item $xml, $dll -Force -ErrorAction SilentlyContinue

L "=== 0. база: check_appwd на wd_p28t (код = число находок; T41 от правок соседей) ==="
$o = Chk; $n0 = $code
L (($o -split "`n" | Where-Object { $_ -match '^\s+\d+\. |СВЕЖАЯ|ПОСТОРОННЕЕ|ИСПОЛНЯЕМОЕ' }) -join "`n"); L ("код = $n0")

L "=== 1. чужой config\chuzhoy_p28.xml: ждём тот же код $n0, строку «ПОСТОРОННЕЕ ЗАГРУЖАЕМОЕ», файл на месте ==="
'<xml/>' | Set-Content -LiteralPath $xml
$o = Chk
L (($o -split "`n" | Where-Object { $_ -match 'chuzhoy_p28|ПОСТОРОННЕЕ|ИСПОЛНЯЕМОЕ' }) -join "`n"); L ("код = $code (было $n0)"); L ("xml на месте: " + (Test-Path $xml))

L "=== 2. чужой Chuzhoy_p28.dll: ждём код $($n0 + 1), строку «ИСПОЛНЯЕМОЕ ВНЕ ПЛАНА», файл на месте ==="
[IO.File]::WriteAllBytes($dll, [byte[]](1..16))
$o = Chk
L (($o -split "`n" | Where-Object { $_ -match 'Chuzhoy_p28|ИСПОЛНЯЕМОЕ|Сторож не удаляет' }) -join "`n"); L ("код = $code (было $n0)"); L ("dll на месте: " + (Test-Path $dll))
Remove-Item $dll -Force

L "=== 3. mk_appwd -Force при чужом xml: ждём код 0, xml НЕ вынесен, назван, отметка есть ==="
$o = Mk
L (($o -split "`n" | Where-Object { $_ -match 'chuzhoy_p28|постороннее|вынесено|ПРОЩЕНО|САМОПРОВЕРКА' }) -join "`n"); L ("код = $code"); L ("xml на месте: " + (Test-Path $xml)); L ("отметка есть: " + (Test-Path "$wd\.appwd.json"))

L "=== 4. mk_appwd -Force при чужом dll: ждём код 4, dll НЕ вынесен, назван, отметки НЕТ ==="
[IO.File]::WriteAllBytes($dll, [byte[]](1..16))
$o = Mk
L (($o -split "`n" | Where-Object { $_ -match 'Chuzhoy_p28|постороннее|вынесено|САМОПРОВЕРКА|Force прощает' }) -join "`n"); L ("код = $code"); L ("dll на месте: " + (Test-Path $dll)); L ("отметка есть: " + (Test-Path "$wd\.appwd.json"))

L "=== 5. уборка своих подкладок и пересборка оснастки ==="
Remove-Item $xml, $dll -Force
$o = Mk
L ("код = $code"); L ("отметка есть: " + (Test-Path "$wd\.appwd.json"))
L "done $(Get-Date -Format 'HH:mm:ss')"
exit 0
