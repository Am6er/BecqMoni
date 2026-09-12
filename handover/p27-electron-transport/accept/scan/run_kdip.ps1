# П27 12.09.2026: 59.541 кэВ с раздельным каскадом (--kdip=1 — умолчание склада с 12.09.2026:
# фотоэлектрон e − E_связи, релаксация электронами EADL) — оба плеча (--etr=0/1), три сцены;
# контроль — RC103 1461 с --kdip=1 (каскад там почти ничего не решает).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p27"
$scenes = "$root\handover\p27-electron-transport\scenes"
$out = "$root\handover\p27-electron-transport\accept"
$runs = @(
    @{ g = 'RC103_bare_gap5'; e = 59.541; n = 10000000 },
    @{ g = 'OBS_bare_gap5';   e = 59.541; n = 10000000 },
    @{ g = 'AS80_bare_gap5';  e = 59.541; n = 4000000 },
    @{ g = 'RC103_bare_gap5'; e = 1461;   n = 10000000 }
)
Push-Location $bin
foreach ($etr in 0, 1) {
    foreach ($r in $runs) {
        $tag = "$($r.g)_$($r.e)_etr${etr}_kdip1"
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & "$bin\G4RawProbe.exe" "--geometry=$scenes\$($r.g).in" --no-light --bin=1 "--energy=$($r.e)" "--n=$($r.n)" `
            "--etr=$etr" --kdip=1 "--out=$out\ours_$tag.csv" *> "$out\ours_$tag.txt"
        "ours $tag code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
    }
}
Pop-Location
"done kdip $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
