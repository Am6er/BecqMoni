# П31 12.09.2026, `A308` — дамп решателя на СЦЕНЕ AMBER (стенд mk_stand.ps1): FsaNnlsDumpProbe гонит
# FsaStackShot отражением с теми же ключами, что П29 (run_amber.ps1), и пишет каждый вызов NnlsSolve.
# Контроль: вывод снимка плеча `infer` = П29 §1 (χ²/ndf 3.946, невязка 43.4 %), `huber0` = П29 §3.1.
param([string[]]$Arms = @(), [string]$Sb = 'C:\Users\moroz\bqp31_amber', [string]$Tag = '')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$here = "$root\handover\p31-a308-nnls\amber$Tag"
New-Item -ItemType Directory -Force $here | Out-Null
$log = "$here\arms.log"
# плечо = (ключи ПРОБЫ до `--`, ключи FsaStackShot после); `--fit-floor=` — пол полосы фита (`A302`)
$all = [ordered]@{
    infer      = @(@(), @('--infer'))
    huber0     = @(@(), @('--infer', '--huber=0'))
    h3_noanch  = @(@(), @('--infer', '--no-anchor'))
    h0_noanch  = @(@(), @('--infer', '--huber=0', '--no-anchor'))
    huber05    = @(@(), @('--infer', '--huber=0.5'))
    adc_h3     = @(@('--fit-floor=adc'), @('--infer'))
    adc_h0     = @(@('--fit-floor=adc'), @('--infer', '--huber=0'))
    adc_h3_noanch = @(@('--fit-floor=adc'), @('--infer', '--no-anchor'))
    adc_h0_noanch = @(@('--fit-floor=adc'), @('--infer', '--huber=0', '--no-anchor'))
    f20_h3     = @(@('--fit-floor=20'), @('--infer'))
    f20_h0     = @(@('--fit-floor=20'), @('--infer', '--huber=0'))
    f35_h3     = @(@('--fit-floor=35'), @('--infer'))
    f35_h0     = @(@('--fit-floor=35'), @('--infer', '--huber=0'))
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
