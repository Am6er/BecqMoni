# Тот же опыт, что `f40_controls.ps1`, но на НАСТОЯЩЕМ МАСШТАБЕ: исходники
# копируются из рабочего дерева (548 файлов приложения, 125 проб), сборка и
# каталог проб берутся настоящие (`bin\Debug_F40`, `probes\build_f40`).
# Копия нарочно: рабочее дерево при волне полос правят соседи, и трогать его
# ради замера нельзя. `Copy-Item` переносит `LastWriteTime` — здесь это кстати.
#   pwsh handover\f40-stamp\f40_real_scale.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$bin  = Join-Path $repo 'BecquerelMonitor\bin\Debug_F40'
$out  = Join-Path $repo 'tools\effmaker\probes\build_f40'
. (Join-Path $repo 'tools\CORPUS\scripts\appwd_plan.ps1')

$stand = Join-Path ([IO.Path]::GetTempPath()) ("f40_scale_" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
try {
    New-Item -ItemType Directory -Force (Join-Path $stand 'tools\effmaker\probes') | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'BecquerelMonitor') -Destination (Join-Path $stand 'BecquerelMonitor') -Recurse -Force `
        -Exclude @('bin', 'obj')
    foreach ($d in @('bin', 'obj')) {
        $x = Join-Path $stand "BecquerelMonitor\$d"
        if (Test-Path -LiteralPath $x) { Remove-Item -LiteralPath $x -Recurse -Force }
    }
    Copy-Item (Join-Path $repo 'tools\effmaker\*.cs')        (Join-Path $stand 'tools\effmaker') -Force
    Copy-Item (Join-Path $repo 'tools\effmaker\probes\*.cs') (Join-Path $stand 'tools\effmaker\probes') -Force

    $plan = Get-AppWdPlan -Repo $stand -Bin $bin -Wd $out -ProbeBuild $out -ProbeCatalog
    $rec  = Get-AppWdSourceRecord -Repo $stand
    "исходников: приложение {0} (из {1}), пробы {2}, довесков {3}" -f $rec.App.N, $rec.App.From, $rec.Probes.N, $rec.Comp.N
    ".cs в BecquerelMonitor\ ВНЕ проекта: {0}" -f @($rec.App.Loose).Count
    ""

    # Заверение стенда: отметка ложится в каталог проб, оттуда сторож её и читает.
    $keep = Join-Path ([IO.Path]::GetTempPath()) ('appwd_keep_' + [Guid]::NewGuid().ToString('N').Substring(0, 6) + '.json')
    $stampFile = Join-Path $out '.appwd.json'
    Copy-Item -LiteralPath $stampFile -Destination $keep -Force
    Write-AppWdStamp -Plan $plan -Files 1

    $m = {
        param($What, $Want)
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $r = Test-AppWdBuild -Plan $plan
        $sw.Stop()
        $n = @($r.Bad).Count
        "{0,-62} находок {1,-3} ({2:F2} с)  ждали {3}  {4}" -f $What, $n, $sw.Elapsed.TotalSeconds, $Want,
            $(if (($Want -eq '0' -and $n -eq 0) -or ($Want -eq '>=1' -and $n -ge 1)) { 'СОШЛОСЬ' } else { '⛔ НЕ СОШЛОСЬ' })
    }
    & $m 'дерево не тронуто с заверения' '0'

    $f = Join-Path $stand 'BecquerelMonitor\Peak.cs'
    $t = (Get-Item -LiteralPath $f).LastWriteTime
    (Get-Item -LiteralPath $f).LastWriteTime = Get-Date
    & $m 'Peak.cs ТРОНУТ (время СЕЙЧАС), содержимое то же' '0'
    (Get-Item -LiteralPath $f).LastWriteTime = $t

    $novaya = Join-Path $stand 'tools\effmaker\probes\ChuzhayaProbaF40.cs'
    Set-Content -LiteralPath $novaya -Encoding ascii -Value 'class Ch { static int Main() { return 0; } }'
    & $m 'чужая НОВАЯ проба .cs без собранного exe, время СЕЙЧАС' '0'
    Remove-Item -LiteralPath $novaya -Force

    $vne = Join-Path $stand 'BecquerelMonitor\VneProektaF40.cs'
    Set-Content -LiteralPath $vne -Encoding ascii -Value 'class VneProektaF40 {}'
    & $m '.cs в BecquerelMonitor\ ВНЕ проекта, время СЕЙЧАС' '0'
    Remove-Item -LiteralPath $vne -Force

    $b = [IO.File]::ReadAllBytes($f)
    [IO.File]::WriteAllBytes($f, ($b + [byte[]][char[]]"`n// f40"))
    (Get-Item -LiteralPath $f).LastWriteTime = $t     # время СОХРАНЕНО — по времени не видно
    & $m 'Peak.cs подменён, LastWriteTime СОХРАНЁН (T233)' '>=1'
    [IO.File]::WriteAllBytes($f, $b)
    (Get-Item -LiteralPath $f).LastWriteTime = $t
    & $m 'подмена откачена' '0'

    Copy-Item -LiteralPath $keep -Destination $stampFile -Force
    Remove-Item -LiteralPath $keep -Force
    ""
    "отметка каталога возвращена на прежнюю"
} finally {
    Remove-Item -LiteralPath $stand -Recurse -Force -ErrorAction SilentlyContinue
}
