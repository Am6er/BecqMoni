# П29 12.09.2026, `AMBER22` — стенд сцены Amber (повтор П26 §1 / mk_stand.ps1): копия каталога проб
# build_p29 (worktree bqp29) + конфиг П13 (tools\effmaker\probes\build_p13th\config: BecquerelMonitor.xml,
# device\*.xml, NuclideDefinition.xml) + матрица склада AS80_th_disk.rmx под guid кривой «Th медальон»
# спектра Amber (4b069ea2-117b-6cae-ddb2-0b10753939fb) в config\device\response\.
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp29'
$sb = 'C:\Users\moroz\bqp29_amber'
if (Test-Path $sb) { Remove-Item -Recurse -Force $sb }
Copy-Item -Recurse "$wt\tools\effmaker\probes\build_p29" $sb
# в каталоге проб лежит СВОЙ config (из сборки приложения) — снести, ставится конфиг П13 целиком
Remove-Item -Recurse -Force "$sb\config"
Copy-Item -Recurse "$root\tools\effmaker\probes\build_p13th\config" "$sb\config"
$rmx = "$root\tools\CORPUS\corpus\geometries\AS80_th_disk.rmx"
Copy-Item $rmx "$sb\config\device\response\4b069ea2-117b-6cae-ddb2-0b10753939fb.rmx" -Force
# ⚠ спектр — p13\spectra\Th-232_after.xml (П26 §1: узел кривой «Th медальон» с геометрией AS80_ThDisk_thglass
# под guid 4b069ea2; у p13\Th-232_amber.xml guid 04cb27d4 — матрица к нему не сходится по отпечатку)
Copy-Item "$root\tools\effmaker\probes\build_p13th\p13\spectra\Th-232_after.xml" "$sb\Th-232_amber.xml"
"stand: $sb"
"rmx sha256: " + (Get-FileHash $rmx).Hash
"placed sha256: " + (Get-FileHash "$sb\config\device\response\4b069ea2-117b-6cae-ddb2-0b10753939fb.rmx").Hash
"spectrum sha256: " + (Get-FileHash "$sb\Th-232_amber.xml").Hash
"FsaStackShot.exe sha256: " + (Get-FileHash "$sb\FsaStackShot.exe").Hash
