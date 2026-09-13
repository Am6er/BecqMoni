# П41 13.09.2026 — ИТОГОВЫЙ заход приёмки на окончательных исходниках (направляющая со
# смягчением A/2π): пересборка Release_p41/build_p41 тем же движением (build_p41.ps1), затем
# контроли (а), (б), (4) по порядку. Первый заход (сборка без смягчения) — в pre-soft/.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art = "$root\handover\p41-e29"
Set-Location $root
"final start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -Append "$art\codes.txt"
pwsh -File "$art\build_p41.ps1"
$code = $LASTEXITCODE
"final build code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
if ($code -ne 0) { exit $code }
pwsh -File "$art\ctrl_a.ps1"
pwsh -File "$art\ctrl_b.ps1"
pwsh -File "$art\ctrl_c.ps1"
"final end $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$art\codes.txt"
