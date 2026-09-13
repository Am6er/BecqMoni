# П44 13.09.2026 — наша сторона против арбитра Geant4 (логи П26/П27/П32 не перегоняются):
# G4RawProbe --no-light --bin=1, умолчания = умолчания склада физики 17; плечи ключей П44:
#   off      — ни одного ключа (ВЫКЛ);
#   bpath1   — --bpath=1 (тормозное вдоль пути, изотропно);
#   bpath2   — --bpath=2 (вдоль пути, по электрону);
#   ecomp    — --ecomp=1 (электрон в произвольном веществе: пробег и тормозное обвязки);
#   both     — --ecomp=1 --bpath=1.
# Параметр -Set выбирает подмножество сцен (bare / disk / point0), -Arms — плечи через запятую.
param(
    [string]$Set = 'bare',
    [string]$Arms = 'off,bpath1,bpath2,ecomp,both',
    [int]$N = 8000000
)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p44"
$scenes = 'D:\BqMoni_Claude\p44\scenes'
$out = "D:\BqMoni_Claude\p44\g4\$Set"
New-Item -ItemType Directory -Force $out | Out-Null
$armKeys = @{
    off    = @();
    bpath1 = @('--bpath=1');
    bpath2 = @('--bpath=2');
    ecomp  = @('--ecomp=1');
    both   = @('--ecomp=1', '--bpath=1');
}
$sets = @{
    bare  = @(
        @{ g = 'AS80_bare_gap5';  e = 661.657;  bands = '0-165,165-330,330-495,495-660' },
        @{ g = 'AS80_bare_gap5';  e = 2614.511; bands = '0-100,100-200,200-300,300-500,500-522,522-1000,1000-1033,1033-2000,2000-2600' },
        @{ g = 'RC103_bare_gap5'; e = 661.657;  bands = '0-165,165-330,330-495,495-660' },
        @{ g = 'RC103_bare_gap5'; e = 2614.511; bands = '0-100,100-200,200-300,300-500,500-522,522-1000,1000-1033,1033-2000,2000-2600' }
    );
    disk  = @(
        @{ g = 'AS80_th_disk';    e = 2614.511; bands = '0-100,100-200,200-300,300-500,500-600,600-1000,1000-2000,2000-2600' },
        @{ g = 'AS80_th_disk';    e = 583.187;  bands = '0-50,50-100,100-150,150-200,200-400,400-580' }
    );
    point0 = @(
        @{ g = 'AS80_point0';     e = 661.657;  bands = '0-50,50-100,100-165,165-330,330-495,495-660' },
        @{ g = 'AS80_point0';     e = 1332.5;   bands = '0-100,100-200,200-333,333-666,666-1000,1000-1330' }
    );
}
Push-Location $bin
foreach ($r in $sets[$Set]) {
    foreach ($arm in $Arms.Split(',')) {
        $tag = "$($r.g)_$($r.e)_$arm"
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $keys = $armKeys[$arm]
        & "$bin\G4RawProbe.exe" "--geometry=$scenes\$($r.g).in" --no-light --bin=1 "--energy=$($r.e)" "--n=$N" `
            "--out=$out\ours_$tag.csv" "--bands=$($r.bands)" @keys *> "$out\ours_$tag.txt"
        "ours $tag code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
    }
}
Pop-Location
"done $Set $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
