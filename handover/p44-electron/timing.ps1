# П44 — цена по времени, мкс/историю: G4RawProbe одним процессом, машина свободна, 4 млн историй,
# плечи off / ecomp / bpath1 / bpath2 / both на диске (2614) и на голом CsI (2614).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p44"
$scenes = 'D:\BqMoni_Claude\p44\scenes'
$out = 'D:\BqMoni_Claude\p44\timing'
New-Item -ItemType Directory -Force $out | Out-Null
$N = 4000000
$armKeys = @{ off = @(); ecomp = @('--ecomp=1'); bpath1 = @('--bpath=1'); bpath2 = @('--bpath=2'); both = @('--ecomp=1', '--bpath=1') }
Push-Location $bin
foreach ($g in @(@{ g = 'AS80_th_disk'; e = 2614.511 }, @{ g = 'RC103_bare_gap5'; e = 2614.511 }, @{ g = 'AS80_point0'; e = 661.657 })) {
    foreach ($arm in @('off', 'ecomp', 'bpath1', 'bpath2', 'both')) {
        $keys = $armKeys[$arm]
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & "$bin\G4RawProbe.exe" "--geometry=$scenes\$($g.g).in" --no-light --bin=1 "--energy=$($g.e)" "--n=$N" @keys *> "$out\t_$($g.g)_$arm.txt"
        $s = $sw.Elapsed.TotalSeconds
        "{0} {1} code={2} t={3}s us/hist={4}" -f $g.g, $arm, $LASTEXITCODE, $s.ToString('F1', [System.Globalization.CultureInfo]::InvariantCulture), (1e6 * $s / $N).ToString('F2', [System.Globalization.CultureInfo]::InvariantCulture) | Out-File -Append "$out\timing.txt"
    }
}
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\timing.txt"
