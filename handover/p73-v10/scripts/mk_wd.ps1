# П73 (V10): изолированный рабочий каталог для RoiActivityProbe / FsaStackShot (по образцу D:\BqMoni_Claude\p67\mk_wd.ps1).
#   pwsh -NoProfile -File D:\BqMoni_Claude\p73\mk_wd.ps1
# Кладётся: копия каталога проб build_p73; config\device — ТОЛЬКО корпусные RC-103.xml и ASN16;
# config\device\response\<guid>.rmx — матрицы полосы из store\response (RC103_point0 — копия живого склада);
# config\NuclideDefinition.xml снимается (состав — --sample=137CS из базы); config\ROI — свои зоны Cs-137.
$ErrorActionPreference = 'Stop'
$env:OS = 'Windows_NT'
$p     = 'D:\BqMoni_Claude\p73'
$repo  = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$build = Join-Path $p 'wt\tools\effmaker\probes\build_p73'
$wd    = Join-Path $p 'wd'
$store = Join-Path $p 'store'
if (-not (Test-Path (Join-Path $build 'FsaStackShot.exe'))) { throw "нет $build\FsaStackShot.exe" }
& robocopy $build $wd /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw "robocopy код $LASTEXITCODE" }
$dev = Join-Path $wd 'config\device'
Get-ChildItem (Join-Path $dev '*.xml') -File -Force | Remove-Item -Force
Copy-Item (Join-Path $repo 'tools\CORPUS\corpus\devices\RC-103.xml') $dev
Copy-Item (Join-Path $repo 'tools\CORPUS\corpus\devices\1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml') $dev
$nd = Join-Path $wd 'config\NuclideDefinition.xml'
if (Test-Path $nd) { Remove-Item $nd -Force }
$rsp = Join-Path $dev 'response'
New-Item -ItemType Directory -Force $rsp | Out-Null
Get-ChildItem (Join-Path $rsp '*.rmx') -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item (Join-Path $store 'response\*.rmx') $rsp
$roi = Join-Path $wd 'config\ROI'
New-Item -ItemType Directory -Force $roi | Out-Null
Get-ChildItem (Join-Path $roi '*.xml') -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item (Join-Path $p 'roi\*.xml') $roi
"рабочий каталог: $wd"
"  приборов: {0}, матриц: {1}, ROI: {2}" -f (Get-ChildItem (Join-Path $dev '*.xml') -File).Count, (Get-ChildItem (Join-Path $rsp '*.rmx') -File).Count, (Get-ChildItem (Join-Path $roi '*.xml') -File).Count
Get-ChildItem (Join-Path $rsp '*.rmx') -File | ForEach-Object { "  {0}  {1}" -f $_.Name, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,16) }
exit 0
