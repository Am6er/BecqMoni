# П46 13.09.2026 (S106): одно плечо прогона МАЛОЙ БАЗЫ в основном дереве своей оснасткой wd_p46; выход — tools\pie\<Arm>.
#   pwsh -NoProfile -Command "& '<repo>\handover\p46-s106\p46_run.ps1' -Arm out_p46_mc_th228 -Extra '--only=G1S16_Th228_P25','--limits-mc=100','--refit-z=0'"
# ⛔ Массив -Extra доезжает целым только через `&` (T84): из Bash — pwsh -NoProfile -Command "& '<файл>' …".
param(
    [Parameter(Mandatory)][string]$Arm,
    [string[]]$Extra = @(),
    [switch]$Force
)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin  = "$root\BecquerelMonitor\bin\Release_p46"
$prb  = "$root\tools\effmaker\probes\build_p46"
$wd   = "$root\tools\CORPUS\scripts\wd_p46"
$store = "$root\tools\CORPUS\corpus\geometries"
$out  = "$root\tools\pie\$Arm"
$here = "$root\handover\p46-s106"
$logf = "$here\$Arm.log"
[System.IO.Directory]::SetCurrentDirectory($root)
Set-Location $root
$t0 = Get-Date
$extra = @() + $Extra
$pass = @{ Out = $out; Wd = $wd; Bin = $bin; ProbeBuild = $prb; Store = $store; Extra = $extra; SkipScore = $true }
if ($Force) { $pass['Force'] = $true }
& "$root\tools\CORPUS\scripts\run_mini.ps1" @pass *> $logf
$rc = $LASTEXITCODE
$line = "run {0} exit={1} ({2} s), ключи: {3} [малая база]{4}" -f $Arm, $rc, [int]((Get-Date)-$t0).TotalSeconds, ($Extra -join ' '), $(if ($Force) { ' [-Force]' } else { '' })
Write-Output $line
Add-Content -Encoding utf8 "$here\run.status.txt" ("{0} {1}" -f (Get-Date -Format 'HH:mm:ss'), $line)
Get-Content $logf | Select-String -Pattern 'ложных|МК-дамп|семья:|#-1:|#[0-9]+:|член ряда|пределов нет|ОСНАСТКА|изменено:|ОТКАЗ|ошибк' | ForEach-Object { $_.Line }
