# П47 13.09.2026 (A310) — плечи на СЦЕНЕ AMBER (стенд mk_stand.ps1 П47): FsaStackShot --infer через обёртку
# FsaP42ArmProbe (хук FsaAnalyzer.ProbeSetup; ключ --weights=data|model добавлен П47) либо FsaNnlsDumpProbe.
#   pwsh -NoProfile -File run_amber.ps1 -Tag head -Arms def
#   pwsh -NoProfile -File run_amber.ps1 -Tag p47  -Arms def,wdata,wmodel
param([string[]]$Arms = @(), [string]$Tag = 'head')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$sb = "D:\BqMoni_Claude\p47\amber_$Tag"
$here = "$root\handover\p47-a310\amber_$Tag"
New-Item -ItemType Directory -Force $here | Out-Null
$log = "$here\arms.log"
$all = [ordered]@{
    def      = @('nnls', @(), @('--infer'))
    wdata    = @('p42', @('--weights=data'), @('--infer'))
    wmodel   = @('p42', @('--weights=model'), @('--infer'))
    wmodelh0 = @('p42', @('--weights=model'), @('--infer', '--huber=0'))
    wdatah0  = @('p42', @('--weights=data'), @('--infer', '--huber=0'))
}
if ($Arms.Count -eq 0) { $Arms = @('def') }
$Arms = @($Arms | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
Push-Location $sb
foreach ($name in $Arms) {
    if (-not $all.Contains($name)) { "нет плеча $name" | Out-File -Append $log; continue }
    $wrap = $all[$name][0]
    $pkeys = $all[$name][1]
    $keys = $all[$name][2]
    $out = "$here\$name"
    New-Item -ItemType Directory -Force $out | Out-Null
    $shot = @("--spectrum=$sb\Th-232_amber.xml", "--out=$out\stack.png", "--dump=$out\dump.csv", '--scale=pow', '--from=15', '--to=2800') + $keys
    if ($wrap -eq 'p42') {
        & "$sb\FsaP42ArmProbe.exe" @pkeys -- @shot *> "$here\probe_$name.txt"
    } else {
        & "$sb\FsaNnlsDumpProbe.exe" "--out=$out" --keep=1 @pkeys -- @shot *> "$here\probe_$name.txt"
    }
    $code = $LASTEXITCODE
    "$name code=$code $(Get-Date -Format 'HH:mm:ss') wrap=$wrap probe=[$($pkeys -join ' ')] keys=[$($keys -join ' ')]" | Out-File -Append $log
}
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $log
