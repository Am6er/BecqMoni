# Приёмка трёх строк (T150, T143, T155): каждая команда — вывод в файл, код возврата в сводку.
$ErrorActionPreference = 'Continue'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$s = $PSScriptRoot
$out = Join-Path $s 'runs'
New-Item -ItemType Directory -Force $out | Out-Null
$full = Join-Path $s 'scene_full'
$bare = Join-Path $s 'scene_noconf'
$summary = New-Object System.Collections.Generic.List[string]

function Run {
    param([string]$Tag, [string]$Cwd, [string]$Exe, [string[]]$ArgList)
    Push-Location $Cwd
    try {
        $log = Join-Path $out "$Tag.txt"
        & $Exe @ArgList *> $log
        $code = $LASTEXITCODE
    } finally { Pop-Location }
    $line = "{0,-28} код {1,-12} cwd={2}" -f $Tag, $code, $Cwd
    $summary.Add($line); Write-Host $line
}

# ---------- T150: RefusalWordsProbe --arm=fsa ----------
Run 't150_before_full_fsa'   $full "$full\RefusalWordsProbe_before.exe" @('--arm=fsa')
Run 't150_before_noconf_fsa' $bare "$bare\RefusalWordsProbe_before.exe" @('--arm=fsa')
Run 't150_after_full_fsa'    $full "$full\RefusalWordsProbe.exe"        @('--arm=fsa')
Run 't150_after_noconf_fsa'  $bare "$bare\RefusalWordsProbe.exe"        @('--arm=fsa')
Run 't150_after_full_ctrl'   $full "$full\RefusalWordsProbe.exe"        @('--arm=fsa', '--control=empty')
Run 't150_after_full_ctrlbad' $full "$full\RefusalWordsProbe.exe"       @('--arm=clone', '--control=empty')
Run 't150_after_full_clone'  $full "$full\RefusalWordsProbe.exe"        @('--arm=clone')
Run 't150_after_full_all'    $full "$full\RefusalWordsProbe.exe"        @()
Run 't150_before_full_clone' $full "$full\RefusalWordsProbe_before.exe" @('--arm=clone')

# ---------- T143: CorpusGeomProbe ----------
$tmpOut = Join-Path $s 'geom_out'
if (Test-Path "$full\config\device") { Remove-Item -Recurse -Force "$full\config\device" }
if (Test-Path $tmpOut) { Remove-Item -Recurse -Force $tmpOut }
$foreign = Join-Path $s 'foreign_cwd'
if (Test-Path $foreign) { Remove-Item -Recurse -Force $foreign }
New-Item -ItemType Directory -Force $foreign | Out-Null
$b1 = Join-Path $repo 'tools\effmaker\probes\build_b1'
Run 't143_before_foreign_dry'  $foreign "$full\CorpusGeomProbe_before.exe" @('--dry')
Run 't143_before_repo_dry'     $repo    "$full\CorpusGeomProbe_before.exe" @('--dry')
"после before(foreign, --dry): файлов в foreign_cwd $((Get-ChildItem $foreign -Recurse -File -ErrorAction SilentlyContinue).Count)" | Tee-Object -Variable l; $summary.Add($l)
Run 't143_before_foreign_write' $foreign "$full\CorpusGeomProbe_before.exe" @()
"после before(foreign, запись): файлов в foreign_cwd $((Get-ChildItem $foreign -Recurse -File -ErrorAction SilentlyContinue).Count): $((Get-ChildItem $foreign -Recurse -File | Select-Object -First 3 | % FullName) -join '; ')" | Tee-Object -Variable l; $summary.Add($l)
Remove-Item -Recurse -Force (Join-Path $foreign 'tools')
Run 't143_after_foreign_dry'   $foreign "$b1\CorpusGeomProbe.exe"        @('--dry')
Run 't143_after_foreign_rel'   $foreign "$b1\CorpusGeomProbe.exe"        @('--out=geom_rel', '--dry')
Run 't143_after_foreign_abs'   $foreign "$b1\CorpusGeomProbe.exe"        @("--out=$tmpOut")
Run 't143_after_repo_dry'      $repo    "$b1\CorpusGeomProbe.exe"        @('--dry')
"файлов в geom_out: $((Get-ChildItem $tmpOut -File -ErrorAction SilentlyContinue).Count); в foreign_cwd (должно быть 0 кроме ничего): $((Get-ChildItem $foreign -Recurse -File -ErrorAction SilentlyContinue).Count)" | Tee-Object -Variable l; $summary.Add($l)

# ---------- T155: EfficiencyConfigProbe ----------
$model = Join-Path $repo 'tools\effmaker\models\Nano16Pro_Marinelli.in'
$curve = Join-Path $repo 'LSRM Geometries\Exported Curves\Nano 16 - marinelli.txt'
$wd = Join-Path $s 'effwd'
"config\device в scene_full ДО: $(Test-Path "$full\config\device")" | Tee-Object -Variable l; $summary.Add($l)
Run 't155_before_3args' $full "$full\EfficiencyConfigProbe_before.exe" @($model, $curve, $wd)
"config\device в scene_full ПОСЛЕ before(3 довода): $(Test-Path "$full\config\device")" | Tee-Object -Variable l; $summary.Add($l)
if (Test-Path "$full\config\device") { Remove-Item -Recurse -Force "$full\config\device" }
Run 't155_before_2args' $full "$full\EfficiencyConfigProbe_before.exe" @($model, $curve)
Run 't155_after_3args'  $full "$full\EfficiencyConfigProbe.exe"        @($model, $curve, $wd)
Run 't155_after_2args'  $full "$full\EfficiencyConfigProbe.exe"        @($model, $curve)
"config\device в scene_full ПОСЛЕ after(2 довода): $(Test-Path "$full\config\device")" | Tee-Object -Variable l; $summary.Add($l)
Run 't155_after_1arg'   $full "$full\EfficiencyConfigProbe.exe"        @($model)
Run 't155_after_2args_noconf' $bare "$bare\EfficiencyConfigProbe.exe"  @($model, $curve)
"config\device в scene_noconf ПОСЛЕ after(2 довода): $(Test-Path "$bare\config\device")" | Tee-Object -Variable l; $summary.Add($l)

$summary | Set-Content (Join-Path $out '_summary.txt') -Encoding UTF8
