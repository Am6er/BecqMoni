# Целевой замер к `T138`: «сторож сверяет оснастку со СБОРКОЙ, а сборку — НИ С
# ЧЕМ». Воспроизводится ИМЕННО тот случай, которым строка заведена: исходник
# поднимает `PhysicsVersion`, сборка и пробы остаются прежними и СОГЛАСНЫ МЕЖДУ
# СОБОЙ — до 06.09.2026 такая оснастка проходила сторожа зелёным.
# Время правки СОХРАНЯЕТСЯ: так строка `T233` и `Copy-Item` соседа, и так
# правило времени не может помочь даже случайно.
$ErrorActionPreference = 'Stop'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$tmp  = Join-Path $env:TEMP ('f46t138_' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$sh   = Join-Path $tmp 'repo'
foreach ($spec in @(
        @{ Rel = 'BecquerelMonitor';        Flt = '*.cs';     Rec = $true  },
        @{ Rel = 'BecquerelMonitor';        Flt = '*.csproj'; Rec = $false },
        @{ Rel = 'BecquerelMonitor\config'; Flt = '*.xml';    Rec = $true  },
        @{ Rel = 'config';                  Flt = '*.xml';    Rec = $true  },
        @{ Rel = 'tools\effmaker';          Flt = '*.cs';     Rec = $false },
        @{ Rel = 'tools\effmaker\probes';   Flt = '*.cs';     Rec = $false })) {
    $src = Join-Path $root $spec.Rel
    if (-not (Test-Path -LiteralPath $src)) { continue }
    $files = if ($spec.Rec) {
        Get-ChildItem -LiteralPath $src -Recurse -File -Force -Filter $spec.Flt -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    } else { Get-ChildItem (Join-Path $src $spec.Flt) -File -Force -ErrorAction SilentlyContinue }
    $pre = (Resolve-Path -LiteralPath $src).Path.TrimEnd('\') + '\'
    foreach ($f in $files) {
        $d = Join-Path (Join-Path $sh $spec.Rel) $f.FullName.Substring($pre.Length)
        New-Item -ItemType Directory -Force (Split-Path -Parent $d) | Out-Null
        Copy-Item -LiteralPath $f.FullName -Destination $d -Force
    }
}
New-Item -ItemType Directory -Force (Join-Path $sh 'tools\CORPUS\corpus\geometries\response') | Out-Null

$ev  = Join-Path $root 'handover\f46-stamp\f46_eval.ps1'
$new = Join-Path $root 'tools\CORPUS\scripts\appwd_plan.ps1'
$old = Join-Path $tmp  'appwd_plan_HEAD.ps1'
Push-Location $root
& git show HEAD:tools/CORPUS/scripts/appwd_plan.ps1 | Set-Content -LiteralPath $old -Encoding utf8
Pop-Location
$pb  = Join-Path $root 'tools\effmaker\probes\build_f46'
$bin = Join-Path $root 'BecquerelMonitor\bin\Debug_F46'
$rm  = Join-Path $sh 'BecquerelMonitor\EfficiencyMaker\ResponseMatrix.cs'

Write-Host ('ResponseMatrix.cs в тени: {0}' -f (Test-Path -LiteralPath $rm))
Write-Host ('заявленная физика: ' + (Select-String -LiteralPath $rm -Pattern 'PhysicsVersion\s*=\s*\d+' | Select-Object -First 1).Line.Trim())
Write-Host ''
Write-Host '--- 1. тень цела: сборка, пробы и исходники согласны ---'
& pwsh -NoProfile -File $ev -Guard $new -Repo $sh -Bin $bin -ProbeBuild $pb
Write-Host ''
Write-Host '--- 2. физика поднята в ИСХОДНИКЕ, время правки СОХРАНЕНО; сборка и пробы не тронуты ---'
$t = (Get-Item -LiteralPath $rm).LastWriteTime
((Get-Content -LiteralPath $rm -Raw) -replace 'PhysicsVersion\s*=\s*\d+', 'PhysicsVersion = 99') |
    Set-Content -LiteralPath $rm -NoNewline
(Get-Item -LiteralPath $rm).LastWriteTime = $t
Write-Host '  НОВЫЙ сторож:'
& pwsh -NoProfile -File $ev -Guard $new -Repo $sh -Bin $bin -ProbeBuild $pb
Write-Host '  СТАРЫЙ сторож (HEAD):'
& pwsh -NoProfile -File $ev -Guard $old -Repo $sh -Bin $bin -ProbeBuild $pb
Write-Host ''
Write-Host ('тень оставлена как есть: {0}' -f $sh)
