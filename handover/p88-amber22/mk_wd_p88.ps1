# П88 (AMBER22): изолированный рабочий каталог для FsaStackShot — по образцу handover/p67-amber22/mk_wd.ps1.
#   pwsh -NoProfile -File D:\BqMoni_Claude\p88\mk_wd_p88.ps1
# Кладётся: копия каталога проб build_p88; config\device — ТОЛЬКО корпусный «Atom Spectra 80x80.xml» (guid 33100348-…,
# читается из worktree — tools\CORPUS не правится); config\device\response\<guid>.rmx — матрицы полосы из store\response;
# config\NuclideDefinition.xml снимается (состав — из базы по --chain).
$ErrorActionPreference = 'Stop'
$env:OS = 'Windows_NT'
$p     = 'D:\BqMoni_Claude\p88'
$wt    = "$p\wt"
$build = Join-Path $wt 'tools\effmaker\probes\build_p88'
$wd    = Join-Path $p 'wd'
$store = Join-Path $p 'store'
if (-not (Test-Path (Join-Path $build 'FsaStackShot.exe'))) { throw "нет $build\FsaStackShot.exe" }
& robocopy $build $wd /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw "robocopy код $LASTEXITCODE" }
$dev = Join-Path $wd 'config\device'
Get-ChildItem (Join-Path $dev '*.xml') -File -Force | Remove-Item -Force
Copy-Item (Join-Path $wt 'tools\CORPUS\corpus\devices\Atom Spectra 80x80.xml') $dev
$nd = Join-Path $wd 'config\NuclideDefinition.xml'
if (Test-Path $nd) { Remove-Item $nd -Force }
$rsp = Join-Path $dev 'response'
New-Item -ItemType Directory -Force $rsp | Out-Null
Get-ChildItem (Join-Path $rsp '*.rmx') -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
if (Test-Path (Join-Path $store 'response')) { Copy-Item (Join-Path $store 'response\*.rmx') $rsp }
"рабочий каталог: $wd"
"  приборов: {0}, матриц: {1}" -f (Get-ChildItem (Join-Path $dev '*.xml') -File).Count, (Get-ChildItem (Join-Path $rsp '*.rmx') -File -ErrorAction SilentlyContinue).Count
exit 0
