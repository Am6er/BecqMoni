# П67 (AMBER22): изолированный рабочий каталог для FsaStackShot (по образцу handover/p64-amber29/mk_wd.ps1).
#   pwsh -NoProfile -File D:\BqMoni_Claude\p67\mk_wd.ps1
# Кладётся: копия каталога проб build_p67; config\device — ТОЛЬКО корпусный «Atom Spectra 80x80.xml» (guid 33100348-…);
# config\device\response\<guid>.rmx — матрицы полосы из D:\BqMoni_Claude\p67\store\response (кладёт CorpusEffProbe);
# config\NuclideDefinition.xml снимается (состав — из базы по --chain).
$ErrorActionPreference = 'Stop'
$env:OS = 'Windows_NT'
$p     = 'D:\BqMoni_Claude\p67'
$repo  = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$build = Join-Path $p 'wt\tools\effmaker\probes\build_p67'
$wd    = Join-Path $p 'wd'
$store = Join-Path $p 'store'
if (-not (Test-Path (Join-Path $build 'FsaStackShot.exe'))) { throw "нет $build\FsaStackShot.exe" }
& robocopy $build $wd /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw "robocopy код $LASTEXITCODE" }
$dev = Join-Path $wd 'config\device'
Get-ChildItem (Join-Path $dev '*.xml') -File -Force | Remove-Item -Force
Copy-Item (Join-Path $repo 'tools\CORPUS\corpus\devices\Atom Spectra 80x80.xml') $dev
$nd = Join-Path $wd 'config\NuclideDefinition.xml'
if (Test-Path $nd) { Remove-Item $nd -Force }
$rsp = Join-Path $dev 'response'
New-Item -ItemType Directory -Force $rsp | Out-Null
Get-ChildItem (Join-Path $rsp '*.rmx') -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
if (Test-Path (Join-Path $store 'response')) { Copy-Item (Join-Path $store 'response\*.rmx') $rsp }
"рабочий каталог: $wd"
"  приборов: {0}, матриц: {1}" -f (Get-ChildItem (Join-Path $dev '*.xml') -File).Count, (Get-ChildItem (Join-Path $rsp '*.rmx') -File -ErrorAction SilentlyContinue).Count
Get-ChildItem (Join-Path $rsp '*.rmx') -File -ErrorAction SilentlyContinue | ForEach-Object { "  {0}  {1}" -f $_.Name, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,16) }
exit 0
