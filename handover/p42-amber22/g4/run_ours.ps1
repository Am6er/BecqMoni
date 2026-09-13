# П42 13.09.2026, `AMBER22` — наша сторона ФИЗИКИ 17: сырой отклик G4RawProbe (шкала энерговыделения --no-light,
# бин 1 кэВ, умолчания = умолчания склада физики 17, П38/П40) на сцене ториевого диска AS80_th_disk (копия .in П26)
# для сверки континуума с теми же логами Geant4 П26 (handover/p26-amber22/g4/g4_*.log — арбитр не перегонялся).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = 'C:\Users\moroz\bqp42\tools\effmaker\probes\build_p42'
$scenes = "$root\handover\p26-amber22\scenes"
$out = "$root\handover\p42-amber22\g4"
$runs = @(
    @{ g = 'AS80_th_disk';   e = 238.632;  n = 8000000 },
    @{ g = 'AS80_th_disk';   e = 583.187;  n = 8000000 },
    @{ g = 'AS80_th_disk';   e = 911.204;  n = 8000000 },
    @{ g = 'AS80_th_disk';   e = 2614.511; n = 8000000 }
)
Push-Location $bin
foreach ($r in $runs) {
    $tag = "$($r.g)_$($r.e)"
    & "$bin\G4RawProbe.exe" "--geometry=$scenes\$($r.g).in" --no-light --bin=1 "--energy=$($r.e)" "--n=$($r.n)" `
        "--out=$out\ours17_$tag.csv" "--bands=0-100,100-500,500-1000,1000-2000,2000-2600" *> "$out\ours17_$tag.txt"
    "ours17 $tag code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
}
Pop-Location
"done ours17 $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
