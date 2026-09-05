# Замер на НАСТОЯЩЕМ дереве: старый сторож против нового по каталогу проб
# полосы F40. Ничего не правит — только читает.
#   pwsh handover\f40-stamp\f40_real.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$bin  = Join-Path $repo 'BecquerelMonitor\bin\Debug_F40'
$out  = Join-Path $repo 'tools\effmaker\probes\build_f40'

$oldFile = Join-Path ([IO.Path]::GetTempPath()) ("appwd_plan_old_" + [Guid]::NewGuid().ToString('N').Substring(0, 6) + ".ps1")
Push-Location -LiteralPath $repo
try { git show "HEAD:tools/CORPUS/scripts/appwd_plan.ps1" | Set-Content -LiteralPath $oldFile -Encoding utf8 }
finally { Pop-Location }

$code = @'
param($guard, $repo, $bin, $probes)
. $guard
$p = Get-AppWdPlan -Repo $repo -Bin $bin -Wd $probes -ProbeBuild $probes -ProbeCatalog
$sw = [Diagnostics.Stopwatch]::StartNew()
$r = Test-AppWdBuild -Plan $p
$sw.Stop()
"НАХОДОК: {0}  за {1:F3} с" -f @($r.Bad).Count, $sw.Elapsed.TotalSeconds
foreach ($b in @($r.Bad)) { "  ОТКАЗ: " + ($b -split "`n")[0] }
if ($r.PSObject.Properties['Note']) { foreach ($n in @($r.Note)) { "  замечание: $n" } }
'@
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("f40_real_" + [Guid]::NewGuid().ToString('N').Substring(0, 6) + ".ps1")
Set-Content -LiteralPath $tmp -Value $code -Encoding utf8
try {
    "=== СТАРЫЙ сторож (HEAD) ==="
    & pwsh -NoProfile -File $tmp $oldFile $repo $bin $out
    ""
    "=== НОВЫЙ сторож (рабочее дерево) ==="
    & pwsh -NoProfile -File $tmp (Join-Path $repo 'tools\CORPUS\scripts\appwd_plan.ps1') $repo $bin $out
    ""
    "=== отметка каталога ==="
    $st = Get-Content -LiteralPath (Join-Path $out '.appwd.json') -Raw | ConvertFrom-Json
    "  собран   : {0}" -f $st.built
    "  наборы   : приложение {0} ({1} файлов), пробы {2} ({3}), довесков {4}" -f `
        $st.sources.app.fp.Substring(0, 16), $st.sources.app.n,
        $st.sources.probes.fp.Substring(0, 16), $st.sources.probes.n, $st.sources.comp.n
    "  проб в записи each: {0}" -f @($st.sources.each.PSObject.Properties).Count
    "  двоичные : приложение sha {0}" -f $st.sources.binaries.app.Substring(0, 16)
    "  размер отметки: {0} КБ" -f ([math]::Round((Get-Item (Join-Path $out '.appwd.json')).Length / 1KB, 1))
} finally {
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $oldFile -Force -ErrorAction SilentlyContinue
}
