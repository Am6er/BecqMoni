# П111 (M13): опора Geant4 по тормозному в слое — g4brem по связкам (вещество/толщина/энергия/угол).
# Пороги: e- range cut 1 мм (как у арбитра стенда), гамма 0.001 мм (порог ~1 кэВ), счёт квантов k ≥ 5 кэВ (MinKev таблиц слоя).
# Выход: D:\BqMoni_Claude\p111\g4brem\out\<name>.log, коды — codes_g4brem.txt.
param([int]$N = 100000, [string]$Only = '')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'D:\BqMoni_Claude\p111'
$out = "$root\g4brem\out"
New-Item -ItemType Directory -Force $out | Out-Null
$codes = "$root\codes_g4brem.txt"
# name; material; slab_mm (0 = толстая 2 см); layer2; layer2_mm; angles
$configs = @(
    @('ptfe20',  'G4_TEFLON', 0,   '-',     0,   @(0)),
    @('al20',    'G4_Al',     0,   '-',     0,   @(0)),
    @('ptfe1',   'G4_TEFLON', 1.0, '-',     0,   @(0, 45, 70)),
    @('ptfe03',  'G4_TEFLON', 0.3, '-',     0,   @(0)),
    @('al1',     'G4_Al',     1.0, '-',     0,   @(0)),
    @('rc103',   'G4_TEFLON', 1.0, 'G4_Al', 1.0, @(0, 45, 70))
)
$energies = @(100, 300, 500, 1000, 2000)
foreach ($c in $configs) {
    $name = $c[0]
    if ($Only -ne '' -and $name -ne $Only) { continue }
    foreach ($ang in $c[5]) {
        foreach ($e in $energies) {
            $tag = "${name}_${e}_a${ang}"
            $log = "$out\$tag.log"
            if (Test-Path $log) { "g4brem $tag SKIP" | Out-File -Append $codes; continue }
            $t0 = Get-Date
            & cmd /c "D:\BqMoni_Claude\p111\g4brem\run_g4brem.bat $($c[1]) $e $N 1.0 20260919 $($c[2]) $ang $($c[3]) $($c[4]) 0.001 5" 2>&1 | Out-File -Encoding utf8 $log
            "g4brem $tag code=$LASTEXITCODE N=$N $(Get-Date -Format 'HH:mm:ss') dt=$([int]((Get-Date)-$t0).TotalSeconds)s" | Out-File -Append $codes
        }
    }
}
"done g4brem $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
