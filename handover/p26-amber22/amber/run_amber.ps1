# П26 12.09.2026, `AMBER22` п. 2 — плечи на СЦЕНЕ AMBER (стенд mk_stand.ps1): FsaStackShot настоящим
# кодом отрисовки, дамп кривых по каналам (--dump=). Контроль `infer` = П22 §4.4 (χ²/ndf 3.938, 43.4 %).
# Ключей Хубера/каскада у FsaStackShot нет (чужая проба, не правится) — здесь только её штатные ключи.
param([string[]]$Arms = @())
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$sb = 'C:\Users\moroz\bqp26_amber'
$here = "$root\handover\p26-amber22\amber"
$log = "$here\arms.log"
$all = [ordered]@{
    infer        = @('--infer')
    sample       = @('--sample=232TH')
    infer_nomx   = @('--infer', '--no-matrix')
    sample_nomx  = @('--sample=232TH', '--no-matrix')
    infer_noeq   = @('--infer', '--no-equilibrium')
    infer_noanch = @('--infer', '--no-anchor')
    infer_k32    = @('--infer', '--knots=32')
    infer_k512   = @('--infer', '--knots=512')
    infer_noatom = @('--infer', '--no-atomic')
    infer_nobs   = @('--infer', '--no-backscatter')
    infer_z0     = @('--infer', '--refit-z=0')
    infer_kf2    = @('--infer', '--knots=2')     # у FsaStackShot --knots= это ГУСТОЙ край шага узлов в ПШПВ (ContinuumKnotFwhm), не делитель
    infer_kf8    = @('--infer', '--knots=8')
    infer_nomx2  = @('--infer', '--no-matrix')   # матрица на время плеча УБИРАЕТСЯ из response\ (--no-matrix у FsaStackShot лишь снимает отказ)
}
if ($Arms.Count -eq 0) { $Arms = @($all.Keys) }
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
Push-Location $sb
foreach ($name in $Arms) {
    if (-not $all.Contains($name)) { "нет плеча $name" | Out-File -Append $log; continue }
    $keys = $all[$name]
    $rmx = "$sb\config\device\response\4b069ea2-117b-6cae-ddb2-0b10753939fb.rmx"
    if ($name -eq 'infer_nomx2') { Move-Item $rmx "$rmx.away" -Force }
    & "$sb\FsaStackShot.exe" "--spectrum=$sb\Th-232_amber.xml" "--out=$here\stack_$name.png" "--dump=$here\dump_$name.csv" `
        --scale=pow --from=15 --to=2800 @keys *> "$here\stack_$name.txt"
    if ($name -eq 'infer_nomx2') { Move-Item "$rmx.away" $rmx -Force }
    "$name code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') keys=[$($keys -join ' ')]" | Out-File -Append $log
}
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
