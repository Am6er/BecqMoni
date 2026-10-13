# П142 23.09.2026 — сборка основного дерева ПОСЛЕ слияния физики 23 в `master`.
# Свои каталоги: bin\Release_p142, obj\Release_p142, probes\build_p142 (правило полосы).
# Ключ -Tag даёт КОНТРОЛЬНЫЙ каталог (например -Tag ctrl -> Release_p142ctrl / build_p142ctrl).
#   pwsh -File handover\p142-rev33\build_p142.ps1 [-Tag ''] [-SkipRestore]
param([string]$Tag = '', [switch]$SkipRestore)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art  = "$root\handover\p142-rev33"
$msb  = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
# ключ /p:GenerateManifests=false стоит в вызове msbuild ниже (T75: без него сборку рушит
# любой процесс, держащий один из BecquerelMonitor/*.sqlite на запись)
$proj = "$root\BecquerelMonitor\BecquerelMonitor.csproj"
$bin  = "bin\Release_p142$Tag\"
$obj  = "obj\Release_p142$Tag\"
$out  = "$root\tools\effmaker\probes\build_p142$Tag"
$codes = "$art\codes_p142$Tag.txt"
"=== сборка П142$Tag $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===" | Out-File $codes
Set-Location $root
if (-not $SkipRestore) {
    & $msb $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU *> "$art\build_restore_p142$Tag.log"
    "Restore code=$LASTEXITCODE" | Out-File -Append $codes
}
& $msb $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false `
    "/p:OutputPath=$bin" "/p:IntermediateOutputPath=$obj" *> "$art\build_app_p142$Tag.log"
"Release app code=$LASTEXITCODE -> $bin" | Out-File -Append $codes
& pwsh -NoLogo -NoProfile -File "$root\tools\effmaker\probes\build_all.ps1" `
    -Bin "$root\BecquerelMonitor\$($bin.TrimEnd('\'))" -Out $out *> "$art\build_probes_p142$Tag.log"
"probes build_all code=$LASTEXITCODE -> $out" | Out-File -Append $codes
$a = (Get-FileHash "$root\BecquerelMonitor\$($bin.TrimEnd('\'))\BecquerelMonitor.exe" -Algorithm SHA256).Hash
$p = (Get-FileHash "$out\BecquerelMonitor.exe" -Algorithm SHA256).Hash
"sha256 app  = $a" | Out-File -Append $codes
"sha256 probe= $p" | Out-File -Append $codes
"sha256 равны: $($a -eq $p)" | Out-File -Append $codes
"проб .exe в каталоге: $((Get-ChildItem $out -Filter *.exe).Count)" | Out-File -Append $codes
Get-Content $codes
