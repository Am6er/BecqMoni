# Оценщик одного состояния дерева ОДНИМ сторожем. Живёт отдельным процессом
# нарочно: старый и новый сторож объявляют функции С ОДНИМИ ИМЕНАМИ, и в одном
# сеансе второй молча перекрыл бы первого — замер мерил бы один сторож дважды.
# Печатает машинно разбираемые строки; код возврата не несёт смысла.
param(
    [Parameter(Mandatory)][string]$Guard,
    [Parameter(Mandatory)][string]$Repo,
    [Parameter(Mandatory)][string]$Bin,
    [Parameter(Mandatory)][string]$ProbeBuild,
    [switch]$Certifying,
    [switch]$Fingerprint
)
$ErrorActionPreference = 'Stop'
. $Guard
$plan = Get-AppWdPlan -Repo $Repo -Bin $Bin -Wd $ProbeBuild -ProbeBuild $ProbeBuild -ProbeCatalog

if ($Fingerprint) {
    # Отпечатки берутся у САМОГО сторожа: если их считать здесь своей копией
    # правила, замер проверял бы мою копию, а не сторожа (`T61`).
    $rec = Get-AppWdSourceRecord -Repo $Repo
    'FP_APP=' + $rec.App.Fp
    'FP_PROBES=' + $rec.Probes.Fp
    'FP_COMP=' + $rec.Comp.Fp
    'N_APP=' + $rec.App.N
    'N_PROBES=' + $rec.Probes.N
    'N_LOOSE=' + @($rec.App.Loose).Count
    foreach ($k in (@($rec.Each.Keys) | Sort-Object)) { 'FP_EACH=' + $k + '=' + $rec.Each[$k].Fp }
    return
}

$r = if ($Certifying) { Test-AppWdBuild -Plan $plan -Certifying } else { Test-AppWdBuild -Plan $plan }
$bad = @($r.Bad)
'COUNT=' + $bad.Count
foreach ($b in $bad) { 'BAD=' + (($b -split "`n")[0]).Trim() }
if ($r.PSObject.Properties['Note']) { foreach ($n in @($r.Note)) { 'NOTE=' + (($n -split "`n")[0]).Trim() } }
