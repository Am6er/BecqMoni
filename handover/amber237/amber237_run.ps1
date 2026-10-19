# AMBER237: база (HEAD 17d42ea0, bin\Debug_Codex) против правки (app_new); прогоны вывода состава.
$ErrorActionPreference = 'Continue'
$lane = 'D:\BqMoni_Claude\amber237'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$spectra = "$repo\tools\CORPUS\corpus\spectra"
foreach ($pair in @(@('base', "$repo\BecquerelMonitor\bin\Debug_Codex"), @('new', "$lane\app_new"))) {
    pwsh -NoProfile -File "$repo\tools\effmaker\probes\build_all.ps1" -Bin $pair[1] -Out "$lane\probes_$($pair[0])" *> "$lane\build_$($pair[0]).log"
    "build_all $($pair[0]) exit $LASTEXITCODE"
}
# спектры Amber — копии
Copy-Item -LiteralPath 'D:\YandexDisk\Спектры\!ASN16\UGlass nos 12.xml' "$lane\UGlass_nos12.xml"
Copy-Item -LiteralPath 'D:\YandexDisk\Спектры\!ASN16\Чароит в домике.xml' "$lane\Charoite.xml"
New-Item -ItemType Directory -Force "$lane\amber" | Out-Null
foreach ($side in 'base', 'new') {
    $p = "$lane\probes_$side"
    Rename-Item "$p\config" 'config_supply'
    Copy-Item "$env:APPDATA\BecqMoni\config" "$p\config" -Recurse
    Push-Location $p
    foreach ($s in 'UGlass_nos12', 'Charoite') {
        foreach ($set in 'all', 'UGlass') {
            & .\FsaInferProbeF51.exe "--spectrum=$lane\$s.xml" "--set=$set" *> "$lane\amber\$s.$set.$side.infer.txt"
        }
        & .\FsaBqProbe.exe "--spectrum=$lane\$s.xml" '--set=all' '--components' '--equilibrium' '--nucbase' *> "$lane\amber\$s.all.$side.bq.txt"
    }
    Pop-Location
    Remove-Item -Recurse -Force "$p\config" -Confirm:$false
    Rename-Item "$p\config_supply" 'config'
}
"amber done"
foreach ($side in 'base', 'new') {
    & "$lane\sweep.ps1" -Probes "$lane\probes_$side" -Out "$lane\sweep_$side"
}
"all done"
