# П40 13.09.2026 — контроль (б) «прямой вызов сменился»: пробы света, зовущие симулятор напрямую.
#
# LightAnchorProbe: без ключей берёт УМОЛЧАНИЯ СИМУЛЯТОРА, с --store ставит SplitXrayShells и обе
# половины K-провала от ResponseMatrixOptions (как строитель склада). Четыре плеча, одно зерно:
#   build_p38 (HEAD 83f79e86, до П40) без ключей  — свет БЕЗ K-провала (старое умолчание поля);
#   build_p38 --store                              — свет С K-провалом (склад);       ≠ плечу 1 — положительный контроль
#   build_p40 (после П40) без ключей               — обязан РАВНЯТЬСЯ плечу 2 до знака (умолчание поля = склад);
#   build_p40 --store                              — обязан равняться плечу 3 (ключ стал пустым).
# LightScaleProbe (build_p40): --kdip=0 против --kdip=1 (абляция жива) и без ключа — проба СТАВИТ обе
# половины сама от своего kdip=0, потому «без ключа» = --kdip=0, а не умолчание поля (см. журнал §9).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = "$root\handover\p40-sim-defaults"
$geo = "$root\tools\CORPUS\corpus\geometries\AS80_point0.in"
$e = '--energies=20,30,32,33.5,34,36,40,59.5,100,661.657'
"ctrl_b start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
foreach ($arm in @(@('p38', 'plain', @()), @('p38', 'store', @('--store')), @('p40', 'plain', @()), @('p40', 'store', @('--store')))) {
    $bin = "$root\tools\effmaker\probes\build_$($arm[0])"
    $log = "$art\ctrl_b_anchor_$($arm[0])_$($arm[1]).log"
    Push-Location $bin
    & "$bin\LightAnchorProbe.exe" "--geometry=$geo" $e --n=200000 --bin=1 @($arm[2]) > $log 2>&1
    "ctrl_b anchor $($arm[0]) $($arm[1]) code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
    Pop-Location
}
$bin = "$root\tools\effmaker\probes\build_p40"
Push-Location $bin
foreach ($k in @(@('nokey', @()), @('kdip0', @('--kdip=0')), @('kdip1', @('--kdip=1')))) {
    $log = "$art\ctrl_b_scale_p40_$($k[0]).log"
    & "$bin\LightScaleProbe.exe" "--geometry=$geo" $e --n=200000 --bin=1 @($k[1]) > $log 2>&1
    "ctrl_b scale p40 $($k[0]) code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
}
Pop-Location
"ctrl_b end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
