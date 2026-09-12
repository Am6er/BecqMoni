# П34 12.09.2026, `A309` — плечи пола полосы фита на СЦЕНЕ AMBER (стенд mk_stand.ps1): FsaNnlsDumpProbe гонит
# FsaStackShot отражением с ключами П29/П31 (--infer, шкала pow 15…2800), дамп кривых — amber\<плечо>\dump.csv.
# Контроль: плечо `off` = П31 `infer` (χ²/ndf 3.946, невязка 43.4 %, пики −21.4/−14.1/−21.2/−23.1/−28.8).
param([string[]]$Arms = @(), [string]$Sb = 'C:\Users\moroz\bqp34_amber', [string]$Tag = '')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$here = "$root\handover\p34-fit-floor\amber$Tag"
New-Item -ItemType Directory -Force $here | Out-Null
$log = "$here\arms.log"
$all = [ordered]@{
    off    = @(@('--fit-floor=off'), @('--infer'))
    adc    = @(@('--fit-floor=adc'), @('--infer'))
    f20    = @(@('--fit-floor=20'), @('--infer'))
    f35    = @(@('--fit-floor=35'), @('--infer'))
    thr    = @(@(), @('--infer'))
    # доля уровня правила ключом `threshold:<доля>` до FsaNnlsDumpProbe (чужая, П31) не доезжает —
    # его разбор без третьего выхода теперь ОТКАЗЫВАЕТ на таком значении; плечи долей 0.8 / 0.9
    # гонятся ЧИСЛОМ, которое правило даёт на этой сцене при той доле (FsaFitFloorProfileProbe --fraction=):
    # 0.7 → 15.84 (= thr), 0.8 → 16.18, 0.9 → 17.53 кэВ
    f1618  = @(@('--fit-floor=16.18'), @('--infer'))
    f1753  = @(@('--fit-floor=17.53'), @('--infer'))
    f15    = @(@('--fit-floor=15'), @('--infer'))
    f25    = @(@('--fit-floor=25'), @('--infer'))
}
if ($Arms.Count -eq 0) { $Arms = @($all.Keys) }
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
Push-Location $Sb
foreach ($name in $Arms) {
    if (-not $all.Contains($name)) { "нет плеча $name" | Out-File -Append $log; continue }
    $pkeys = $all[$name][0]
    $keys = $all[$name][1]
    $out = "$here\$name"
    & "$Sb\FsaNnlsDumpProbe.exe" "--out=$out" @pkeys -- "--spectrum=$Sb\Th-232_amber.xml" "--out=$out\stack.png" "--dump=$out\dump.csv" `
        --scale=pow --from=15 --to=2800 @keys *> "$here\probe_$name.txt"
    "$name code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') probe=[$($pkeys -join ' ')] keys=[$($keys -join ' ')]" | Out-File -Append $log
}
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
