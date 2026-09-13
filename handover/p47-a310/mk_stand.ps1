# П47 13.09.2026 (A310) — стенд сцены Amber (повтор mk_stand.ps1 П42 с путями П47): копия каталога проб
# build_p47 (основное дерево) + конфиг Amber (Desktop\Debug\config, ТОЛЬКО ЧТЕНИЕ, копируется целиком; копия
# П13 build_p13th снята clean_builds 13.09) + матрица AS80_th_disk.rmx живого склада (физика 17) под guid кривой
# «Th медальон» спектра Amber (4b069ea2-…) + спектр сцены П34 (sha C0BEC24A… = стенд П42).
#   pwsh -NoProfile -File mk_stand.ps1 -Tag head   -> D:\BqMoni_Claude\p47\amber_head
param([string]$Tag = 'head')
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$sb = "D:\BqMoni_Claude\p47\amber_$Tag"
$rmx = "$root\tools\CORPUS\corpus\geometries\AS80_th_disk.rmx"
$cfg = 'C:\Users\moroz\OneDrive\Desktop\Debug\config'
$spec = "$root\handover\p34-fit-floor\amber\scene\Th-232_amber.xml"
if (Test-Path $sb) { Remove-Item -Recurse -Force $sb }
Copy-Item -Recurse "$root\tools\effmaker\probes\build_p47" $sb
Remove-Item -Recurse -Force "$sb\config"
Copy-Item -Recurse $cfg "$sb\config"
Remove-Item -Force "$sb\config\NuclideDefinition.xml.bak-*" -ErrorAction SilentlyContinue
Copy-Item $rmx "$sb\config\device\response\4b069ea2-117b-6cae-ddb2-0b10753939fb.rmx" -Force
Copy-Item $spec "$sb\Th-232_amber.xml"
"stand: $sb"
"config source: $cfg (только чтение)"
"rmx source: $rmx"
"rmx sha256: " + (Get-FileHash $rmx).Hash
"placed sha256: " + (Get-FileHash "$sb\config\device\response\4b069ea2-117b-6cae-ddb2-0b10753939fb.rmx").Hash
"spectrum sha256: " + (Get-FileHash "$sb\Th-232_amber.xml").Hash
"FsaStackShot.exe sha256: " + (Get-FileHash "$sb\FsaStackShot.exe").Hash
"BecquerelMonitor.exe sha256: " + (Get-FileHash "$sb\BecquerelMonitor.exe").Hash
"NuclideDefinition.xml sha256: " + (Get-FileHash "$sb\config\NuclideDefinition.xml").Hash
"Atom Spectra 80x80.xml sha256: " + (Get-FileHash "$sb\config\device\Atom Spectra 80x80.xml").Hash
