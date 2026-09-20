# П85: параллельные прогоны TCCFCALC2 — по процессу на (нуклид × angular × повтор), каждый в своей копии каталога.
#   & tccf_par.ps1 -Decays 100000 -Tag test                       # все четыре нуклида одинаково
#   & tccf_par.ps1 -Plan 'co60:40000000,y88:40000000x2,cs134:20000000,eu152:20000000' -Seed 20260915 -Tag main
# Повторы (xN) идут с зёрнами Seed, Seed+1, … и складываются читателем.
param([int]$Decays = 100000, [int]$Seed = 20260915, [string]$Tag = 'test', [string]$Nuclides = 'co60,y88,cs134,eu152', [string]$Plan = '')
$ErrorActionPreference = 'Stop'
$base = 'D:\BqMoni_Claude\p85\tccf'
$geom = 'D:\BqMoni_Claude\p85\store\G1S_point5.in'
$py = 'D:\BqMoni_Claude\p85\scripts\tccf_run.py'
$nuc = @{ co60 = @(60, 27); y88 = @(88, 39); cs134 = @(134, 55); eu152 = @(152, 63) }
$items = @()
if ($Plan) {
  foreach ($e in $Plan.Split(',')) {
    $nm, $rest = $e.Split(':')
    $parts = ($rest + 'x1').Split('x')
    $items += @{ n = $nm; d = [int]$parts[0]; r = [int]$parts[1] }
  }
} else { foreach ($n in $Nuclides.Split(',')) { $items += @{ n = $n; d = $Decays; r = 1 } } }
$procs = @()
foreach ($it in $items) {
  $n = $it.n
  for ($rep = 0; $rep -lt $it.r; $rep++) {
    foreach ($ang in 0, 1) {
      $sfx = if ($it.r -gt 1) { "_r$rep" } else { '' }
      $wd = "D:\BqMoni_Claude\p85\tccf_${n}_ang${ang}$sfx"
      if (-not (Test-Path "$wd\tccfcalc.dll")) {
        New-Item -ItemType Directory -Force $wd | Out-Null
        Copy-Item "$base\tccfcalc.dll", "$base\TccfProbe2.exe" $wd
        cmd /c mklink /J "$wd\Lib" "$base\Lib" | Out-Null   # библиотека общая, только чтение
      }
      $a = $nuc[$n][0]; $z = $nuc[$n][1]; $seedHere = $Seed + $rep
      $runTag = "${Tag}_${n}_ang${ang}$sfx"
      $argList = @($py, '--workdir', $wd, '--geometry', $geom, '--a', $a, '--z', $z, '--decays', $it.d, '--angular', $ang, '--seed', $seedHere, '--tag', $runTag)
      $p = Start-Process -FilePath 'python' -ArgumentList $argList -NoNewWindow -PassThru -RedirectStandardOutput "$wd\stdout_$Tag.txt" -RedirectStandardError "$wd\stderr_$Tag.txt"
      $procs += @{ p = $p; tag = $runTag; wd = $wd }
    }
  }
}
"запущено процессов: $($procs.Count), зерно $Seed, план: $(if ($Plan) { $Plan } else { "$Nuclides x $Decays" })"
foreach ($x in $procs) { $x.p.WaitForExit(); "  $($x.tag): rc=$($x.p.ExitCode) $((Get-Content "$($x.wd)\stdout_$Tag.txt" -Raw -ErrorAction SilentlyContinue))" }
