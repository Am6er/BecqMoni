# П42 13.09.2026, `AMBER22` — стенд сцены Amber (повтор mk_stand.ps1 П26/П29/П31/П34 с именами П42): копия каталога
# проб build_<Tag> (worktree bq<Tag>) + конфиг П13 (build_p13th\config) + матрица AS80_th_disk.rmx под guid кривой
# «Th медальон» спектра Amber (4b069ea2-…) + спектр Th-232_after.xml (П26 §1).
#   -Tag p42    → матрица ЖИВОГО склада (физика 17, sha 82d105bc…)
#   -Tag p42r20 → матрица снимка физики 16 (C:\Users\moroz\store_phys16_backup_2026-09-13, sha 817b4821… = П26/П29/П31/П34)
param([string]$Tag = 'p42')
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = "C:\Users\moroz\bq$Tag"
$sb = "C:\Users\moroz\bq${Tag}_amber"
if ($Tag -eq 'p42r20') { $rmx = 'C:\Users\moroz\store_phys16_backup_2026-09-13\AS80_th_disk.rmx' }
else { $rmx = "$root\tools\CORPUS\corpus\geometries\AS80_th_disk.rmx" }
if (Test-Path $sb) { Remove-Item -Recurse -Force $sb }
Copy-Item -Recurse "$wt\tools\effmaker\probes\build_$Tag" $sb
Remove-Item -Recurse -Force "$sb\config"
Copy-Item -Recurse "$root\tools\effmaker\probes\build_p13th\config" "$sb\config"
Copy-Item $rmx "$sb\config\device\response\4b069ea2-117b-6cae-ddb2-0b10753939fb.rmx" -Force
Copy-Item "$root\tools\effmaker\probes\build_p13th\p13\spectra\Th-232_after.xml" "$sb\Th-232_amber.xml"
"stand: $sb"
"rmx source: $rmx"
"rmx sha256: " + (Get-FileHash $rmx).Hash
"placed sha256: " + (Get-FileHash "$sb\config\device\response\4b069ea2-117b-6cae-ddb2-0b10753939fb.rmx").Hash
"spectrum sha256: " + (Get-FileHash "$sb\Th-232_amber.xml").Hash
"FsaStackShot.exe sha256: " + (Get-FileHash "$sb\FsaStackShot.exe").Hash
"BecquerelMonitor.exe sha256: " + (Get-FileHash "$sb\BecquerelMonitor.exe").Hash
