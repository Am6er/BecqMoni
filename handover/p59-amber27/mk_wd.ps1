# П59 (AMBER27): изолированный рабочий каталог для снимков FSA радоновых спектров.
#
#   & handover\p59-amber27\mk_wd.ps1
#
# Что кладётся и почему (по образцу `tools/CORPUS/scripts/mk_appwd.ps1`, но ВНЕ дерева —
# распоряжение Amber 13.09.2026 «D:\BqMoni_Claude\ работайте тут»):
#   * копия каталога проб `build_p59` (приложение, пробы, три базы, runtimes, ru) —
#     проба ищет конфиг ОТ СВОЕГО каталога (`Package.AppDir`);
#   * `config\device\` — ТОЛЬКО корпусные конфигурации ASN16 (радон) и AS80 (положительный
#     контроль AS80_Th232Medal); поставочные сняты (B6: два одинаковых GUID = модальное окно);
#   * `config\device\response\<guid>.rmx` — матрицы полосы из `D:\BqMoni_Claude\p59\store\response`
#     (кладёт CorpusEffProbe) + живая матрица `AS80_th_disk` под её guid (только чтение, копия);
#   * `config\NuclideDefinition.xml` снимается (AMBER19: состав — из базы по --chain/--sample).
$ErrorActionPreference = 'Stop'
$repo  = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$build = Join-Path $repo 'tools\effmaker\probes\build_p59'
$wd    = 'D:\BqMoni_Claude\p59\wd'
$store = 'D:\BqMoni_Claude\p59\store'

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
Copy-Item (Join-Path $store 'response\*.rmx') $rsp
# AS80_th_disk: guid = StableGuid('AS80_th_disk') = c2b5212c-6b5a-50d8-7870-0b0c3104daa2 (из спектра AS80_Th232Medal)
Copy-Item (Join-Path $repo 'tools\CORPUS\corpus\geometries\response\c2b5212c-6b5a-50d8-7870-0b0c3104daa2.rmx') $rsp

"рабочий каталог: $wd"
"  приборов: {0}, матриц: {1}" -f (Get-ChildItem (Join-Path $dev '*.xml') -File).Count, (Get-ChildItem (Join-Path $rsp '*.rmx') -File).Count
Get-ChildItem (Join-Path $rsp '*.rmx') -File | ForEach-Object { "  {0}  {1}" -f $_.Name, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,16) }
exit 0
