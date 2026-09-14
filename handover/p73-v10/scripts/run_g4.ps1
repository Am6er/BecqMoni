# П73 (V10): арбитр Geant4 на RC103_point50 (и контроль RC103_point0), 661.657 кэВ.
#   pwsh -NoProfile -File D:\BqMoni_Claude\p73\run_g4.ps1
# Наша сторона — G4RawProbe --no-light --bin=1 (умолчания = склада физики 18) из build_p73 + дамп сцены;
# арбитр — tools\g4cf (Geant4 11.4.2) через g4run.cmd (chcp 1251, ловушка П20/П26), мир ПУСТОТА (наша сцена воздуха
# не знает — T133; точка на 50 мм в воздухе теряет < 0.01 %), hist бин 1 кэВ.
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$p = 'D:\BqMoni_Claude\p73'
$bin = "$p\wt\tools\effmaker\probes\build_p73"
$out = "$p\g4"
New-Item -ItemType Directory -Force $out | Out-Null
$runs = @(
  @{ g = 'RC103_point50'; n = 8000000;  g4n = 20000000 },
  @{ g = 'RC103_point0';  n = 8000000;  g4n = 4000000 }
)
Push-Location $bin
foreach ($r in $runs) {
  $tag = "$($r.g)_661.657"
  $sw = [Diagnostics.Stopwatch]::StartNew()
  & "$bin\G4RawProbe.exe" "--geometry=$p\store\$($r.g).in" --no-light --bin=1 --energy=661.657 "--n=$($r.n)" `
      "--out=$out\ours_$tag.csv" "--scene=$out\$($r.g).scene" --bands=0-100,100-200,200-300,300-400,400-500,500-600,600-660 *> "$out\ours_$tag.txt"
  "ours $tag code=$LASTEXITCODE $($sw.Elapsed.TotalSeconds.ToString('F0')) s" | Tee-Object -Append "$out\codes.txt"
}
Pop-Location
foreach ($r in $runs) {
  $tag = "$($r.g)_661.657"
  $sw = [Diagnostics.Stopwatch]::StartNew()
  & cmd /c "`"$p\g4run.cmd`" vacuum scene `"$out\$($r.g).scene`" hist 661.657 $($r.g4n) 1 > `"$out\g4_${tag}_vacuum.log`" 2>&1"
  "g4 ${tag}_vacuum code=$LASTEXITCODE $($sw.Elapsed.TotalSeconds.ToString('F0')) s" | Tee-Object -Append "$out\codes.txt"
}
