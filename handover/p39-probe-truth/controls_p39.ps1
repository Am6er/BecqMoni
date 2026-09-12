# П39, 13.09.2026: положительные контроли `T68` (2) и `T115` на оснастке wd_p39.
# ⚠ Прямые запуски `CorpusFsaProbe.exe` из оснастки здесь НАРОЧНО: это тот самый обходной путь мимо
#    `run_appwd.ps1`, который `T68` (2) закрывает читателем внутри пробы. Каталог `handover/` сторож
#    путей запуска не сканирует. Оснастка после каждой порчи восстанавливается и сверяется.
param([string]$Repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8')
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
$env:PYTHONIOENCODING = 'utf-8'
$wd     = "$Repo\tools\CORPUS\scripts\wd_p39"
$probe  = "$wd\CorpusFsaProbe.exe"
$corpus = "$Repo\tools\CORPUS\corpus"
$art    = "$Repo\handover\p39-probe-truth"
$only   = '--only=ASN16_Cs137,G1S16_Cs137_P5'
$rows = @()

function Run-Direct([string]$name, [string[]]$keys) {
    $out = "$Repo\tools\pie\out_p39_ctl_$name"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    Push-Location $wd
    try {
        $log = & $probe "--corpus=$corpus" "--out=$out" @keys 2>&1 | Out-String
        $rc = $LASTEXITCODE
    } finally { Pop-Location }
    $log | Set-Content -LiteralPath "$art\ctl_$name.log" -Encoding utf8
    $files = if (Test-Path $out) { @(Get-ChildItem $out -File).Count } else { 0 }
    [pscustomobject]@{ Name = $name; Rc = $rc; OutFiles = $files; Log = $log }
}
function Sha([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0, 12) }
function Grab([string]$log, [string]$pattern) { (($log -split "`r?`n") | Where-Object { $_ -match $pattern } | Select-Object -First 3) -join ' | ' }

# ── T115 ────────────────────────────────────────────────────────────────────
$r = Run-Direct 'honest' @($only, '--quiet')
$rows += [pscustomobject]@{ Контроль = 'T115 умолчания (честно)'; Ждём = 'rc 0, APPLIED все True/False по умолчанию'; Получено = "rc $($r.Rc), файлов $($r.OutFiles)"; Строка = (Grab $r.Log '^APPLIED') }

$r = Run-Direct 'noatomic' @($only, '--quiet', '--no-atomic', '--no-matrix', '--no-background', '--no-equilibrium', '--crystal-shield=1')
$rows += [pscustomobject]@{ Контроль = 'T115 пять ключей (честно)'; Ждём = 'rc 0, KEYS и APPLIED называют все пять'; Получено = "rc $($r.Rc), файлов $($r.OutFiles)"; Строка = ((Grab $r.Log '^KEYS') + ' || ' + (Grab $r.Log '^APPLIED')) }

$r = Run-Direct 'spoilkey' @($only, '--quiet', '--no-atomic', '--spoil=key')
$rows += [pscustomobject]@{ Контроль = 'T115 ПОРЧА --spoil=key при --no-atomic'; Ждём = 'rc 13, файлов 0, назван Atomic'; Получено = "rc $($r.Rc), файлов $($r.OutFiles)"; Строка = (Grab $r.Log 'ОТКАЗ \(T115\)|Atomic: заказано') }

# ── T68 (2) ─────────────────────────────────────────────────────────────────
$victim = "$wd\ru\BecquerelMonitor.resources.dll"
$src    = "$Repo\BecquerelMonitor\bin\Release_p39\ru\BecquerelMonitor.resources.dll"
$t0 = (Get-Item -LiteralPath $victim).LastWriteTime
$shaBefore = Sha $victim
# порча: дописать байт, вернуть время правки (так выглядит Copy-Item чужого файла поверх)
[System.IO.File]::AppendAllText($victim, 'x')
(Get-Item -LiteralPath $victim).LastWriteTime = $t0
$shaSpoiled = Sha $victim
$r = Run-Direct 't68_tamper' @($only, '--quiet')
$rows += [pscustomobject]@{ Контроль = 'T68 ПОДМЕНЁН ru\BecquerelMonitor.resources.dll (время сохранено)'; Ждём = 'rc 3, файлов 0, файл назван'; Получено = "rc $($r.Rc), файлов $($r.OutFiles); sha $shaBefore -> $shaSpoiled, время $t0 = $((Get-Item -LiteralPath $victim).LastWriteTime)"; Строка = (Grab $r.Log 'ОТКАЗ \(T68\)|ПОДМЕНЁН') }
# восстановление
Copy-Item -LiteralPath $src -Destination $victim -Force
$shaAfter = Sha $victim
$rows += [pscustomobject]@{ Контроль = 'T68 восстановление файла'; Ждём = "sha как до порчи ($shaBefore)"; Получено = "sha $shaAfter"; Строка = '' }

# порча 2: отметка без files_sha (оснастка до 13.09.2026)
$stamp = "$wd\.appwd.json"
$keep  = Get-Content -LiteralPath $stamp -Raw
$st = $keep | ConvertFrom-Json
$st.PSObject.Properties.Remove('files_sha')
($st | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $stamp -Encoding utf8
$r = Run-Direct 't68_nomanifest' @($only, '--quiet')
$rows += [pscustomobject]@{ Контроль = 'T68 отметка БЕЗ files_sha'; Ждём = 'rc 3, файлов 0'; Получено = "rc $($r.Rc), файлов $($r.OutFiles)"; Строка = (Grab $r.Log 'ОТКАЗ \(T68\)') }
& "$Repo\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd 2>&1 | Out-String | Set-Content -LiteralPath "$art\ctl_t68_nomanifest_check.log" -Encoding utf8
$rows += [pscustomobject]@{ Контроль = 'T68 check_appwd.ps1 на отметке без files_sha'; Ждём = 'rc 1, «БЕЗ МАНИФЕСТА»'; Получено = "rc $LASTEXITCODE"; Строка = (Grab (Get-Content -LiteralPath "$art\ctl_t68_nomanifest_check.log" -Raw) 'БЕЗ МАНИФЕСТА') }
$outR = "$Repo\tools\pie\out_p39_ctl_t68_nomanifest_run"
if (Test-Path $outR) { Remove-Item $outR -Recurse -Force }
& "$Repo\tools\CORPUS\scripts\run_appwd.ps1" -Out $outR -Wd $wd -Extra @($only, '--quiet') 2>&1 | Out-String | Set-Content -LiteralPath "$art\ctl_t68_nomanifest_run.log" -Encoding utf8
$rows += [pscustomobject]@{ Контроль = 'T68 run_appwd.ps1 на отметке без files_sha'; Ждём = 'rc 2, проба не запущена, файлов 0'; Получено = "rc $LASTEXITCODE, файлов $(if (Test-Path $outR) { @(Get-ChildItem $outR -File).Count } else { 0 })"; Строка = (Grab (Get-Content -LiteralPath "$art\ctl_t68_nomanifest_run.log" -Raw) 'БЕЗ МАНИФЕСТА|НЕ ЗАПУЩЕНА') }
$keep | Set-Content -LiteralPath $stamp -Encoding utf8 -NoNewline

# порча 3: отметки нет вовсе
Remove-Item -LiteralPath $stamp -Force
$r = Run-Direct 't68_nostamp' @($only, '--quiet')
$rows += [pscustomobject]@{ Контроль = 'T68 отметки нет'; Ждём = 'rc 3, файлов 0'; Получено = "rc $($r.Rc), файлов $($r.OutFiles)"; Строка = (Grab $r.Log 'ОТКАЗ \(T68\)') }
$keep | Set-Content -LiteralPath $stamp -Encoding utf8 -NoNewline

# честная оснастка после восстановления — дверь снова проходит, сторож зелёный
$r = Run-Direct 't68_restored' @($only, '--quiet')
$rows += [pscustomobject]@{ Контроль = 'T68 оснастка восстановлена'; Ждём = 'rc 0, «474 файлов … сошлись»'; Получено = "rc $($r.Rc), файлов $($r.OutFiles)"; Строка = (Grab $r.Log 'оснастка \(T68\)') }
& "$Repo\tools\CORPUS\scripts\check_appwd.ps1" -Wd $wd 2>&1 | Out-String | Set-Content -LiteralPath "$art\ctl_t68_restored_check.log" -Encoding utf8
$rows += [pscustomobject]@{ Контроль = 'T68 check_appwd.ps1 после восстановления'; Ждём = 'rc 0'; Получено = "rc $LASTEXITCODE"; Строка = (Grab (Get-Content -LiteralPath "$art\ctl_t68_restored_check.log" -Raw) 'ОСНАСТКА СВЕЖАЯ') }

$rows | Format-Table -AutoSize -Wrap | Out-String -Width 400 | Tee-Object -FilePath "$art\controls_summary.txt"
