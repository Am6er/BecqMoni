# П34 12.09.2026, `A309` — стенд сцены Amber (повтор П26/П29/П31 mk_stand.ps1 с именами П34): копия каталога
# проб build_p34 (worktree bqp34) + конфиг П13 (build_p13th\config) + матрица склада AS80_th_disk.rmx под guid
# кривой «Th медальон» спектра Amber (4b069ea2-…) + спектр Th-232_after.xml (П26 §1).
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp34'
$sb = 'C:\Users\moroz\bqp34_amber'
if (Test-Path $sb) { Remove-Item -Recurse -Force $sb }
Copy-Item -Recurse "$wt\tools\effmaker\probes\build_p34" $sb
Remove-Item -Recurse -Force "$sb\config"
Copy-Item -Recurse "$root\tools\effmaker\probes\build_p13th\config" "$sb\config"
$rmx = "$root\tools\CORPUS\corpus\geometries\AS80_th_disk.rmx"
Copy-Item $rmx "$sb\config\device\response\4b069ea2-117b-6cae-ddb2-0b10753939fb.rmx" -Force
Copy-Item "$root\tools\effmaker\probes\build_p13th\p13\spectra\Th-232_after.xml" "$sb\Th-232_amber.xml"
"stand: $sb"
"rmx sha256: " + (Get-FileHash $rmx).Hash
"placed sha256: " + (Get-FileHash "$sb\config\device\response\4b069ea2-117b-6cae-ddb2-0b10753939fb.rmx").Hash
"spectrum sha256: " + (Get-FileHash "$sb\Th-232_amber.xml").Hash
"FsaStackShot.exe sha256: " + (Get-FileHash "$sb\FsaStackShot.exe").Hash
"BecquerelMonitor.exe sha256: " + (Get-FileHash "$sb\BecquerelMonitor.exe").Hash
