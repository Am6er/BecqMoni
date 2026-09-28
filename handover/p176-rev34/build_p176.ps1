# П176 28.09.2026 — сборка основного дерева ПОСЛЕ слияния физики 24 в `master` (образец — П142 build_p142.ps1).
# Свои каталоги Release: bin\Release_p176, obj\Release_p176, probes\build_p176; плюс ШТАТНЫЕ bin\Debug_Codex и
# probes\build (сторожа check_all читают их, П142 §9.1). Коды — handover\p176-rev34\codes_p176.txt (A77: смотреть КОД).
#   pwsh -File handover\p176-rev34\build_p176.ps1 [-SkipRestore] [-SkipStaple]
param([switch]$SkipRestore, [switch]$SkipStaple)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$art  = "$root\handover\p176-rev34"
$logs = 'D:\BqMoni_Claude\p176\art'
$msb  = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'  # вызовы ниже — с /p:GenerateManifests=false (T75)
$proj = "$root\BecquerelMonitor\BecquerelMonitor.csproj"
$bin  = "bin\Release_p176\"
$obj  = "obj\Release_p176\"
$out  = "$root\tools\effmaker\probes\build_p176"
$codes = "$art\codes_p176.txt"
New-Item -ItemType Directory -Force $logs | Out-Null
"=== сборка П176 $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') head=$(git -C $root rev-parse --short HEAD) merge_head=$(git -C $root rev-parse --short MERGE_HEAD) ===" | Out-File -Append $codes
Set-Location $root
if (-not $SkipRestore) {
    & $msb $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false *> "$logs\build_restore_p176.log"
    "Restore code=$LASTEXITCODE" | Out-File -Append $codes
}
& $msb $proj /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false `
    "/p:OutputPath=$bin" "/p:IntermediateOutputPath=$obj" *> "$logs\build_app_p176.log"
"Release app code=$LASTEXITCODE -> $bin" | Out-File -Append $codes
& pwsh -NoLogo -NoProfile -File "$root\tools\effmaker\probes\build_all.ps1" `
    -Bin "$root\BecquerelMonitor\$($bin.TrimEnd('\'))" -Out $out *> "$logs\build_probes_p176.log"
"probes build_all code=$LASTEXITCODE -> $out" | Out-File -Append $codes
$a = (Get-FileHash "$root\BecquerelMonitor\$($bin.TrimEnd('\'))\BecquerelMonitor.exe" -Algorithm SHA256).Hash.ToLower()
$p = (Get-FileHash "$out\BecquerelMonitor.exe" -Algorithm SHA256).Hash.ToLower()
"sha256 app  = $a" | Out-File -Append $codes
"sha256 probe= $p ; равны: $($a -eq $p)" | Out-File -Append $codes
"проб .exe в каталоге: $((Get-ChildItem $out -Filter *.exe).Count)" | Out-File -Append $codes
if (-not $SkipStaple) {
    & $msb $proj /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false `
        '/p:OutputPath=bin\Debug_Codex\' *> "$logs\build_debug_codex_p176.log"
    "Debug_Codex code=$LASTEXITCODE" | Out-File -Append $codes
    & pwsh -NoLogo -NoProfile -File "$root\tools\effmaker\probes\build_all.ps1" `
        -Bin "$root\BecquerelMonitor\bin\Debug_Codex" -Out "$root\tools\effmaker\probes\build" *> "$logs\build_probes_staple_p176.log"
    "probes build (штатный) code=$LASTEXITCODE" | Out-File -Append $codes
}
"=== конец $(Get-Date -Format 'HH:mm:ss') ===" | Out-File -Append $codes
Get-Content $codes
