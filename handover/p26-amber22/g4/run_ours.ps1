# П26 12.09.2026, `AMBER22` п. 1 — наша сторона: сырой отклик G4RawProbe (шкала ЭНЕРГОВЫДЕЛЕНИЯ
# --no-light, бин 1 кэВ, допуск пика ноль, умолчания физики 16 = клеймо склада phys=16) на сцене
# ториевого диска AS80_th_disk (копия в scenes\) и контроль AS80_bare_gap5 661.657 (= П20 §3.2).
# Тем же заходом — дамп сцены для арбитра (--scene=). Сборка — worktree C:\Users\moroz\bqp26 (HEAD 1adfd26e).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = 'C:\Users\moroz\bqp26\tools\effmaker\probes\build_p26'
$scenes = "$root\handover\p26-amber22\scenes"
$out = "$root\handover\p26-amber22\g4"
$runs = @(
    @{ g = 'AS80_bare_gap5'; e = 661.657;  n = 4000000 },
    @{ g = 'AS80_th_disk';   e = 238.632;  n = 8000000 },
    @{ g = 'AS80_th_disk';   e = 583.187;  n = 8000000 },
    @{ g = 'AS80_th_disk';   e = 911.204;  n = 8000000 },
    @{ g = 'AS80_th_disk';   e = 2614.511; n = 8000000 }
)
Push-Location $bin
foreach ($r in $runs) {
    $tag = "$($r.g)_$($r.e)"
    & "$bin\G4RawProbe.exe" "--geometry=$scenes\$($r.g).in" --no-light --bin=1 "--energy=$($r.e)" "--n=$($r.n)" `
        "--scene=$out\$($r.g).scene" "--out=$out\ours_$tag.csv" "--bands=0-100,100-500,500-1000,1000-2000,2000-2600" *> "$out\ours_$tag.txt"
    "ours $tag code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
}
Pop-Location
"done ours $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
