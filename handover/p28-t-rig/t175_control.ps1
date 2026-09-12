# П28 12.09.2026, `T175` — положительный контроль: config\ROI в оснастке корпуса.
# Плечо «до»: оснастка wd_p28t без config\ROI (собрана до правки плана) — проба с
# DocEnergySpectrum (FsaReportViewProbe) печатает в stderr «Не удалось загрузить
# конфигурационный файл ROI». Плечо «после»: mk_appwd -Force по новому плану кладёт
# 12 поставочных ROI, та же проба — без строки. Лог — t175_control.log.
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$log  = "$repo\handover\p28-t-rig\t175_control.log"
$scripts = "$repo\tools\CORPUS\scripts"
$wd = "$scripts\wd_p28t"
$sp = "$repo\tools\CORPUS\corpus\spectra"
function L($s) { $s | Out-File -Append -LiteralPath $log; Write-Host $s }
function Probe($tag) {
    Push-Location $wd
    $o = & "$wd\FsaReportViewProbe.exe" "--spectrum=$sp\ASN16_Th232.xml" "--control=$sp\ASN16_Cs137.xml" "--out=$repo\handover\p28-t-rig\t175_$tag" *>&1 | Out-String
    $script:code = $LASTEXITCODE
    Pop-Location
    $o | Out-File -LiteralPath "$repo\handover\p28-t-rig\t175_probe_$tag.log"
    $o
}
"start $(Get-Date -Format 'HH:mm:ss')" | Out-File -LiteralPath $log
Set-Location $repo

L "=== до: оснастка без config\ROI ==="
L ("config\ROI есть: " + (Test-Path "$wd\config\ROI"))
$o = Probe 'before'
$roi = @($o -split "`n" | Where-Object { $_ -match 'ROI' })
L ("код пробы = $code; строк со словом ROI в выводе: {0}" -f $roi.Count)
foreach ($x in $roi) { L ("   " + $x.Trim()) }

L "=== пересборка оснастки по новому плану (mk_appwd -Force: T41 соседей) ==="
$o = & "$scripts\mk_appwd.ps1" -Bin "$repo\BecquerelMonitor\bin\Release_P28t" -Wd $wd -ProbeBuild "$repo\tools\effmaker\probes\build_p28t" -Force *>&1 | Out-String
L (($o -split "`n" | Where-Object { $_ -match 'положено|ПРОЩЕНО|САМОПРОВЕРКА|сверено' }) -join "`n"); L ("код mk = $LASTEXITCODE")
L ("config\ROI есть: " + (Test-Path "$wd\config\ROI") + ", файлов: " + @(Get-ChildItem "$wd\config\ROI\*.xml" -File -Force -ErrorAction SilentlyContinue).Count)
L ("отметка есть: " + (Test-Path "$wd\.appwd.json"))

L "=== после: та же проба ==="
$o = Probe 'after'
$roi = @($o -split "`n" | Where-Object { $_ -match 'ROI' })
L ("код пробы = $code; строк со словом ROI в выводе: {0}" -f $roi.Count)
foreach ($x in $roi) { L ("   " + $x.Trim()) }

L "=== сторож отдельной командой: check_appwd (число находок; ROI в плане сверяется) ==="
$o = & "$scripts\check_appwd.ps1" -Wd $wd *>&1 | Out-String
L (($o -split "`n" | Where-Object { $_ -match 'сверено|^\s+\d+\. |СВЕЖАЯ|ROI' }) -join "`n"); L ("код = $LASTEXITCODE")
L "done $(Get-Date -Format 'HH:mm:ss')"
