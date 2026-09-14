# П72 (T259): изолированные рабочие каталоги для FsaStackShot — ДВА, по одному на сборку
# (HEAD 311c98b0 = build_p72 и правка = build_p72b), с ОДНИМ складом полосы (по образцу
# handover/p59-amber27/mk_wd.ps1; вне дерева — распоряжение Amber 13.09.2026 «D:\BqMoni_Claude\ работайте тут»).
#
#   & handover\p72-t258-t259\mk_wd.ps1
#
# Что кладётся:
#   * копия каталога проб (приложение, пробы, три базы, runtimes, ru) — проба ищет конфиг ОТ СВОЕГО каталога;
#   * config\device\ — ТОЛЬКО корпусные конфигурации ASN16 (фильтр) и G1S24 поверка 2024 (уголь);
#     поставочные сняты (B6: два одинаковых GUID = модальное окно);
#   * config\device\response\<guid>.rmx — матрица полосы ASN16_rn_side (из D:\BqMoni_Claude\p72\store\response,
#     кладёт CorpusEffProbe) + живая матрица угля G1S_mar1l_coal_046_p24 под её guid (только чтение, копия);
#   * config\NuclideDefinition.xml снимается (AMBER19: состав — из базы по --chain/--sample).
$ErrorActionPreference = 'Stop'
$repo  = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$store = 'D:\BqMoni_Claude\p72\store'
$pairs = @(
    @{ build = 'D:\BqMoni_Claude\p72\wt\tools\effmaker\probes\build_p72';    wd = 'D:\BqMoni_Claude\p72\wd_head' },
    @{ build = 'D:\BqMoni_Claude\p72\wt_b\tools\effmaker\probes\build_p72b'; wd = 'D:\BqMoni_Claude\p72\wd_new' }
)
foreach ($p in $pairs) {
    $build = $p.build; $wd = $p.wd
    if (-not (Test-Path (Join-Path $build 'FsaStackShot.exe'))) { throw "нет $build\FsaStackShot.exe" }
    & robocopy $build $wd /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "robocopy код $LASTEXITCODE" }
    $dev = Join-Path $wd 'config\device'
    New-Item -ItemType Directory -Force $dev | Out-Null
    Get-ChildItem (Join-Path $dev '*.xml') -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
    Copy-Item (Join-Path $repo 'tools\CORPUS\corpus\devices\1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml') $dev
    Copy-Item (Join-Path $repo 'tools\CORPUS\corpus\devices\Gamma-1S UDS-GC 63x63 1024 (corpus, поверка 2024).xml') $dev
    $nd = Join-Path $wd 'config\NuclideDefinition.xml'
    if (Test-Path $nd) { Remove-Item $nd -Force }
    $rsp = Join-Path $dev 'response'
    New-Item -ItemType Directory -Force $rsp | Out-Null
    Get-ChildItem (Join-Path $rsp '*.rmx') -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
    Copy-Item (Join-Path $store 'response\*.rmx') $rsp
    # уголь: guid = StableGuid('G1S_mar1l_coal_046_p24') = 966a4a49-d62e-0ee4-4a4d-e62692fe159d (узел <Efficiency> спектра)
    Copy-Item (Join-Path $repo 'tools\CORPUS\corpus\geometries\response\966a4a49-d62e-0ee4-4a4d-e62692fe159d.rmx') $rsp
    "рабочий каталог: $wd"
    "  приборов: {0}, матриц: {1}" -f (Get-ChildItem (Join-Path $dev '*.xml') -File).Count, (Get-ChildItem (Join-Path $rsp '*.rmx') -File).Count
    Get-ChildItem (Join-Path $rsp '*.rmx') -File | ForEach-Object { "  {0}  {1}" -f $_.Name, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0,16) }
    "  exe sha256 {0}" -f (Get-FileHash (Join-Path $wd 'BecquerelMonitor.exe') -Algorithm SHA256).Hash.Substring(0,16)
}
