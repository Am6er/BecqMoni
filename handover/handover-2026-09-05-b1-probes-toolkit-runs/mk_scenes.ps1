# Сцены для приёмки B1: копии build_b1 (сборка 05.09.2026) с before-версиями трёх проб рядом.
#   scene_full   — как build_b1 целиком (config\ с BecquerelMonitor.xml и NuclideDefinition.xml, без config\device)
#   scene_noconf — то же без config\ вовсе («голый» каталог)
$ErrorActionPreference = 'Stop'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$s = $PSScriptRoot
$src = Join-Path $repo 'tools\effmaker\probes\build_b1'
foreach ($name in 'scene_full', 'scene_noconf') {
    $d = Join-Path $s $name
    if (Test-Path $d) { Remove-Item -Recurse -Force $d }
    New-Item -ItemType Directory -Force $d | Out-Null
    # только нужное: приложение, dll, базы, runtimes, ru, config, три пробы и их exe.config
    Copy-Item (Join-Path $src '*.dll') $d
    Copy-Item (Join-Path $src '*.sqlite') $d
    Copy-Item (Join-Path $src 'BecquerelMonitor.exe') $d
    Copy-Item (Join-Path $src 'BecquerelMonitor.exe.config') $d
    Copy-Item (Join-Path $src 'runtimes') $d -Recurse
    Copy-Item (Join-Path $src 'ru') $d -Recurse
    if ($name -eq 'scene_full') { Copy-Item (Join-Path $src 'config') $d -Recurse }
    foreach ($p in 'RefusalWordsProbe', 'CorpusGeomProbe', 'EfficiencyConfigProbe') {
        Copy-Item (Join-Path $src "$p.exe") $d
        Copy-Item (Join-Path $src "$p.exe.config") $d
        Copy-Item (Join-Path $s "before_bin\${p}_before.exe") $d
        Copy-Item (Join-Path $src "$p.exe.config") (Join-Path $d "${p}_before.exe.config")
    }
    "$name : файлов $((Get-ChildItem $d -Recurse -File).Count), config\ есть: $(Test-Path (Join-Path $d 'config')), config\device есть: $(Test-Path (Join-Path $d 'config\device'))"
}
