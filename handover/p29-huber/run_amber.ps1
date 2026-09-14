# П29 12.09.2026, `AMBER22` — плечи Хубера на СЦЕНЕ AMBER (стенд mk_stand.ps1): FsaStackShot настоящим
# кодом отрисовки, дамп кривых по каналам (--dump=). Контроль `infer` = П26 §3.2 дословно
# (χ²/ndf 3.938, невязка 43.4 %, 583 −21 %, 2614 −28.5 %). Ключ --huber= — вставка П29 в FsaStackShot.cs.
# ⚠ Проходов Хубера у анализатора отдельным свойством НЕТ (FitHuber: passes = HuberM > 0 ? 3 : 1),
#   плечо «1 проход при 3σ» без правки FsaAnalyzer.cs не измеримо — не гонится.
param([string[]]$Arms = @())
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$sb = 'C:\Users\moroz\bqp29_amber'
$here = "$root\handover\p29-huber\amber"
New-Item -ItemType Directory -Force $here | Out-Null
$log = "$here\arms.log"
$all = [ordered]@{
    infer     = @('--infer')
    huber0    = @('--infer', '--huber=0')
    huber5    = @('--infer', '--huber=5')
    huber10   = @('--infer', '--huber=10')
    huber20   = @('--infer', '--huber=20')
    huber100  = @('--infer', '--huber=100')
    huber2    = @('--infer', '--huber=2')
    huber1    = @('--infer', '--huber=1')
    huber05   = @('--infer', '--huber=0.5')
    huber4    = @('--infer', '--huber=4')
    h0_sample = @('--sample=232TH', '--huber=0')   # библиотека из базы по объявленному составу, а не из подписей
    h0_noatom = @('--infer', '--huber=0', '--no-atomic')
    h0_z0     = @('--infer', '--huber=0', '--refit-z=0')
    h0_noanch = @('--infer', '--huber=0', '--no-anchor')
    sample    = @('--sample=232TH')
    h3_noanch = @('--infer', '--no-anchor')                # те же колонки (усиление 1, ноль 0), что у h0_noanch — пара для сходимости NNLS
    h05_noanch = @('--infer', '--huber=0.5', '--no-anchor')
}
if ($Arms.Count -eq 0) { $Arms = @($all.Keys) }
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
Push-Location $sb
foreach ($name in $Arms) {
    if (-not $all.Contains($name)) { "нет плеча $name" | Out-File -Append $log; continue }
    $keys = $all[$name]
    & "$sb\FsaStackShot.exe" "--spectrum=$sb\Th-232_amber.xml" "--out=$here\stack_$name.png" "--dump=$here\dump_$name.csv" `
        --scale=pow --from=15 --to=2800 @keys *> "$here\stack_$name.txt"
    "$name code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') keys=[$($keys -join ' ')]" | Out-File -Append $log
}
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
