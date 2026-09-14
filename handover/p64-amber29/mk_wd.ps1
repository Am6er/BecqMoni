# П64 (AMBER29): изолированный рабочий каталог для снимков FSA спектров угля (по образцу
# handover/p59-amber27/mk_wd.ps1; ВНЕ дерева — распоряжение Amber 13.09.2026).
#
#   & handover\p64-amber29\mk_wd.ps1
#
# Что кладётся:
#   * копия каталога проб `D:\BqMoni_Claude\p64\wt\tools\effmaker\probes\build_p64` (приложение,
#     пробы, три базы, runtimes, ru) — проба ищет конфиг ОТ СВОЕГО каталога;
#   * `config\device\` — ТОЛЬКО корпусная конфигурация G1S24 (`Gamma-1S UDS-GC 63x63 1024
#     (corpus, поверка 2024).xml`, guid 9e5a1c00-…); поставочные сняты (B6: два одинаковых GUID
#     = модальное окно);
#   * `config\device\response\<guid>.rmx` — две матрицы полосы из `D:\BqMoni_Claude\p64\store\response`
#     (кладёт CorpusEffProbe под StableGuid(<ключ сцены>));
#   * `config\NuclideDefinition.xml` снимается (AMBER19: состав — из базы по --chain).
# Спектры: `D:\BqMoni_Claude\p64\spectra_ab\coalA_*.xml` / `coalB_*.xml` — копии одних XML,
# в которые CorpusEffProbe вставил узел <Efficiency> своей сцены.
$ErrorActionPreference = 'Stop'
$p     = 'D:\BqMoni_Claude\p64'
$repo  = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$build = Join-Path $p 'wt\tools\effmaker\probes\build_p64'
$wd    = Join-Path $p 'wd'
$store = Join-Path $p 'store'

if (-not (Test-Path (Join-Path $build 'FsaStackShot.exe'))) { throw "нет $build\FsaStackShot.exe" }
& robocopy $build $wd /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw "robocopy код $LASTEXITCODE" }

$dev = Join-Path $wd 'config\device'
Get-ChildItem (Join-Path $dev '*.xml') -File -Force | Remove-Item -Force
Copy-Item (Join-Path $repo 'tools\CORPUS\corpus\devices\Gamma-1S UDS-GC 63x63 1024 (corpus, поверка 2024).xml') $dev
$nd = Join-Path $wd 'config\NuclideDefinition.xml'
if (Test-Path $nd) { Remove-Item $nd -Force }

$rsp = Join-Path $dev 'response'
New-Item -ItemType Directory -Force $rsp | Out-Null
Get-ChildItem (Join-Path $rsp '*.rmx') -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item (Join-Path $store 'response\*.rmx') $rsp

"рабочий каталог: $wd"
"  приборов: {0}, матриц: {1}" -f (Get-ChildItem (Join-Path $dev '*.xml') -File).Count, (Get-ChildItem (Join-Path $rsp '*.rmx') -File).Count
Get-ChildItem (Join-Path $rsp '*.rmx') -File | ForEach-Object { "  {0}  {1}" -f $_.Name, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,16) }
exit 0
