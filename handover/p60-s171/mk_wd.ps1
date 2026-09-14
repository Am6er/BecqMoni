# П60 (S171): изолированный рабочий каталог для снимков FSA — по образцу handover/p59-amber27/mk_wd.ps1.
#
#   & handover\p60-s171\mk_wd.ps1 -Arm a   # плечо А: build_p60a (HEAD 1b8663d4 до правки) -> D:\BqMoni_Claude\p60\wd_a
#   & handover\p60-s171\mk_wd.ps1 -Arm b   # плечо Б: build_p60  (правка S171)            -> D:\BqMoni_Claude\p60\wd_b
#
# Кладётся: копия каталога проб (приложение, пробы, базы, runtimes, ru); config\device\ — ТОЛЬКО
# корпусные ASN16 и AS80 (поставочные сняты, B6); config\device\response\ — матрица полосы
# ASN16_rn_side из D:\BqMoni_Claude\p60\store\response (CorpusEffProbe) + живая AS80_th_disk
# под её guid (копия, только чтение); config\NuclideDefinition.xml снимается (AMBER19).
param([Parameter(Mandatory)][ValidateSet('a','b')][string]$Arm)
$ErrorActionPreference = 'Stop'
$repo  = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$build = Join-Path $repo ('tools\effmaker\probes\build_p60' + $(if ($Arm -eq 'a') { 'a' } else { '' }))
$wd    = "D:\BqMoni_Claude\p60\wd_$Arm"
$store = 'D:\BqMoni_Claude\p60\store'

if (-not (Test-Path (Join-Path $build 'FsaStackShot.exe'))) { throw "нет $build\FsaStackShot.exe" }
& robocopy $build $wd /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw "robocopy код $LASTEXITCODE" }

$dev = Join-Path $wd 'config\device'
Get-ChildItem (Join-Path $dev '*.xml') -File -Force | Remove-Item -Force
Copy-Item (Join-Path $repo 'tools\CORPUS\corpus\devices\1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml') $dev
Copy-Item (Join-Path $repo 'tools\CORPUS\corpus\devices\Atom Spectra 80x80.xml') $dev
$nd = Join-Path $wd 'config\NuclideDefinition.xml'
if (Test-Path $nd) { Remove-Item $nd -Force }

$rsp = Join-Path $dev 'response'
New-Item -ItemType Directory -Force $rsp | Out-Null
Get-ChildItem (Join-Path $rsp '*.rmx') -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
if (Test-Path (Join-Path $store 'response')) { Copy-Item (Join-Path $store 'response\*.rmx') $rsp }
# AS80_th_disk: guid = StableGuid('AS80_th_disk') = c2b5212c-6b5a-50d8-7870-0b0c3104daa2 (из спектра AS80_Th232Medal)
Copy-Item (Join-Path $repo 'tools\CORPUS\corpus\geometries\response\c2b5212c-6b5a-50d8-7870-0b0c3104daa2.rmx') $rsp

"рабочий каталог: $wd (сборка $build)"
"  приборов: {0}, матриц: {1}" -f (Get-ChildItem (Join-Path $dev '*.xml') -File).Count, (Get-ChildItem (Join-Path $rsp '*.rmx') -File).Count
Get-ChildItem (Join-Path $rsp '*.rmx') -File | ForEach-Object { "  {0}  {1}" -f $_.Name, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,16) }
"  exe sha256: " + (Get-FileHash (Join-Path $wd 'BecquerelMonitor.exe') -Algorithm SHA256).Hash.Substring(0,16)
exit 0
