# T143, второй заход: проба после сведения Build() с корпусом (B19). Пишет ТОЛЬКО во временный каталог.
$ErrorActionPreference = 'Continue'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$s = $PSScriptRoot
$out = Join-Path $s 'runs'
$b1 = Join-Path $repo 'tools\effmaker\probes\build_b1'
$tmpOut = Join-Path $s 'geom_out2'
if (Test-Path $tmpOut) { Remove-Item -Recurse -Force $tmpOut }
$foreign = Join-Path $s 'foreign_cwd'
if (Test-Path $foreign) { Remove-Item -Recurse -Force $foreign }
New-Item -ItemType Directory -Force $foreign | Out-Null

function Run {
    param([string]$Tag, [string]$Cwd, [string]$Exe, [string[]]$ArgList)
    Push-Location $Cwd
    try { & $Exe @ArgList *> (Join-Path $out "$Tag.txt"); $code = $LASTEXITCODE } finally { Pop-Location }
    "{0,-28} код {1,-12} cwd={2}" -f $Tag, $code, $Cwd
}
Run 't143b_after_foreign_dry' $foreign "$b1\CorpusGeomProbe.exe" @('--dry')
Run 't143b_after_foreign_rel' $foreign "$b1\CorpusGeomProbe.exe" @('--out=geom_rel', '--dry')
Run 't143b_after_foreign_abs' $foreign "$b1\CorpusGeomProbe.exe" @("--out=$tmpOut")
Run 't143b_after_b1_dry'      $b1      "$b1\CorpusGeomProbe.exe" @('--dry')
"файлов в foreign_cwd после всего: $((Get-ChildItem $foreign -Recurse -File -ErrorAction SilentlyContinue).Count)"
"в geom_out2: .in $((Get-ChildItem $tmpOut -Filter *.in).Count), всего $((Get-ChildItem $tmpOut -File).Count)"
# сверка с корпусом
$corp = Join-Path $repo 'tools\CORPUS\corpus\geometries'
$same = 0; $diff = @()
foreach ($f in Get-ChildItem $tmpOut -Filter *.in) {
    $c = Join-Path $corp $f.Name
    if ((Test-Path $c) -and ((Get-FileHash $c -Algorithm SHA256).Hash -eq (Get-FileHash $f.FullName -Algorithm SHA256).Hash)) { $same++ } else { $diff += $f.Name }
}
"сверка .in с корпусом: побитово равны $same, расходятся/нет в корпусе: $($diff.Count) $($diff -join ', ')"
$onlyCorpus = @(Get-ChildItem $corp -Filter *.in | Where-Object { -not (Test-Path (Join-Path $tmpOut $_.Name)) } | % Name)
"есть в корпусе, проба не построила: $($onlyCorpus.Count) $($onlyCorpus -join ', ')"
# опись: сравнение без BOM и без CR
$a = [IO.File]::ReadAllText((Join-Path $corp 'index.csv')).TrimStart([char]0xFEFF) -replace "`r", ''
$b = [IO.File]::ReadAllText((Join-Path $tmpOut 'index.csv')).TrimStart([char]0xFEFF) -replace "`r", ''
"опись: корпус BOM=$([IO.File]::ReadAllBytes((Join-Path $corp 'index.csv'))[0] -eq 0xEF), проба BOM=$([IO.File]::ReadAllBytes((Join-Path $tmpOut 'index.csv'))[0] -eq 0xEF); текст без BOM/CR $(if ($a -eq $b) {'СОВПАЛ'} else {'РАЗОШЁЛСЯ'})"
"git status корпуса геометрий: $((git -C $repo status --short tools/CORPUS/corpus/geometries | Measure-Object).Count) строк"
