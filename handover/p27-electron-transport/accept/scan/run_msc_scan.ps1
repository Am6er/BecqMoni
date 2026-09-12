# П27 12.09.2026, разведка: чувствительность формы уноса на 59.541 кэВ (RC103) к ширине
# многократного рассеяния — ВРЕМЕННЫЙ множитель ширины Хайленда --etr-msc= (1.5, 2, 3);
# контроль верха — RC103 1461 при том же множителе. 10 млн историй, --etr=1.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p27"
$scenes = "$root\handover\p27-electron-transport\scenes"
$out = "$root\handover\p27-electron-transport\accept"
Push-Location $bin
foreach ($k in 1.5, 2, 3) {
    foreach ($e in 59.541, 1461) {
        $tag = "RC103_bare_gap5_${e}_etr1_msc$k"
        $sw = [Diagnostics.Stopwatch]::StartNew()
        & "$bin\G4RawProbe.exe" "--geometry=$scenes\RC103_bare_gap5.in" --no-light --bin=1 "--energy=$e" --n=10000000 `
            --etr=1 "--etr-msc=$k" "--out=$out\ours_$tag.csv" *> "$out\ours_$tag.txt"
        "ours $tag code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
    }
}
Pop-Location
"done msc $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
