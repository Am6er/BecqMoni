# П27 12.09.2026, `A72` — ЦЕНА СЧЁТА НА УЗЕЛ: CorpusMatrixProbe ОДНИМ потоком, плоский счёт
# (--target=0), одинаковое число историй, ключ ВЫКЛ против ВКЛ, три сцены-копии:
#   RC103_bare_gap5 — голый CsI 1 см³ (худший случай: вылет возможен почти отовсюду),
#   ASN16_point0    — корпусная CsI 18.5×59 с обвязкой (типичная сцена склада),
#   AS80_point0     — NaI Ø80×80 с обвязкой (крупный).
# Машина в это время свободна (арбитр и приёмка закончены). Секунды на узел — из печати самой
# пробы (время сцены / число узлов) и по секундомеру всей сцены.
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p27"
$out = "$root\handover\p27-electron-transport\timing"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$scenes = 'RC103_bare_gap5', 'ASN16_point0', 'AS80_point0'
foreach ($arm in 'off', 'on') {
    foreach ($scene in $scenes) {
        New-Item -ItemType Directory -Force "$out\$arm\$scene" | Out-Null
        Copy-Item "$root\handover\p27-electron-transport\scenes\$scene.in" "$out\$arm\$scene\$scene.in" -Force
        Remove-Item "$out\$arm\$scene\$scene.rmx" -ErrorAction SilentlyContinue
    }
}
$common = @('--nodes=8', '--n=50000', '--target=0', '--threads=1', '--force')
Push-Location $bin
foreach ($scene in $scenes) {
    foreach ($arm in 'off', 'on') {
        $etr = if ($arm -eq 'on') { '--etr=1' } else { '--etr=0' }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & "$bin\CorpusMatrixProbe.exe" "--dir=$out\$arm\$scene" @common $etr > "$out\$arm`_$scene.txt" 2>&1
        "$arm $scene code=$LASTEXITCODE t=$([math]::Round($sw.Elapsed.TotalSeconds, 1))s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
    }
}
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
