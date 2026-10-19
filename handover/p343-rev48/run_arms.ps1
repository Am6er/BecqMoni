# П343: прогоны корпуса — плечо А (M14 без глубины G1S16) и rev48 (M14 + глубина), полный и малый.
# Сборка одна: bin\Release_p343 + пробы tools\effmaker\probes\build_p343 (собраны после последней правки).
param([string[]]$Arms = @('A', 'rev48'))
$ErrorActionPreference = 'Continue'
$P = 'D:\BqMoni_Claude\p343'
$W = "$P\wt"
$bin = "$W\BecquerelMonitor\bin\Release_p343"
$pb = "$W\tools\effmaker\probes\build_p343"
Set-Location $W
$mini = "$W\tools\CORPUS\corpus\mini.csv"
$rows = Get-Content $mini | Where-Object { $_ -notmatch '^\s*#' -and $_ -notmatch '^spectrum,' -and $_.Trim() }
$keys = $rows | ForEach-Object { ($_ -split ',')[0] }
foreach ($arm in $Arms) {
    if ($arm -eq 'A') {
        $corpus = "$P\armA\tools\CORPUS\corpus"; $store = "$P\storeA"; $wd = "$W\tools\CORPUS\scripts\wd_p343a"
        $full = "$P\out\armA_full"; $mn = "$P\out\armA_mini"
    } else {
        $corpus = "$W\tools\CORPUS\corpus"; $store = "$W\tools\CORPUS\corpus\geometries"; $wd = "$W\tools\CORPUS\scripts\wd_p343"
        $full = "$W\tools\pie\out_rev48_full"; $mn = "$W\tools\pie\out_rev48_mini"
    }
    foreach ($o in @($full, $mn)) { if (Test-Path $o) { "ЗАНЯТО: $o"; exit 3 } }
    & "$W\tools\CORPUS\scripts\mk_appwd.ps1" -Bin $bin -Wd $wd -ProbeBuild $pb -Store $store *> "$P\logs\mk_appwd_$arm.log"
    "$arm mk_appwd exit $LASTEXITCODE"
    if ($LASTEXITCODE -ne 0) { exit 4 }
    $t = Get-Date
    & "$W\tools\CORPUS\scripts\run_appwd.ps1" -Out $full -Corpus $corpus -Wd $wd -Bin $bin -ProbeBuild $pb -Store $store *> "$P\logs\run_${arm}_full.log"
    "$arm полный exit $LASTEXITCODE за $([int]((Get-Date)-$t).TotalSeconds) с"
    $t = Get-Date
    if ($arm -eq 'A') {
        & "$W\tools\CORPUS\scripts\run_appwd.ps1" -Out $mn -Corpus $corpus -Wd $wd -Bin $bin -ProbeBuild $pb -Store $store -Extra @("--only=$($keys -join ',')") *> "$P\logs\run_${arm}_mini.log"
    } else {
        & "$W\tools\CORPUS\scripts\run_mini.ps1" -Out $mn -Wd $wd -Bin $bin -ProbeBuild $pb -Store $store *> "$P\logs\run_${arm}_mini.log"
    }
    "$arm малый exit $LASTEXITCODE за $([int]((Get-Date)-$t).TotalSeconds) с"
}
