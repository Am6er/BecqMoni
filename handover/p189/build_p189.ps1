# П187 (копия build_p181.ps1) — сборка worktree: Restore + Release приложения + пробы в свой каталог. Коды — codes_p189.txt
param([string]$Tag = 'base', [switch]$SkipRestore, [string]$Root = 'D:\BqMoni_Claude\p189\wt')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = $Root
$logs = 'D:\BqMoni_Claude\p189\art'
New-Item -ItemType Directory -Force $logs | Out-Null
$msb  = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'  # вызовы ниже — с /p:GenerateManifests=false (T75)
$proj = "$root\BecquerelMonitor\BecquerelMonitor.csproj"
$bin  = "bin\Release_P189_$Tag\"
$obj  = "obj\P189_$Tag\"
$out  = "D:\BqMoni_Claude\p189\build_$Tag"
$codes = "$logs\codes_p189.txt"
"=== сборка П187 [$Tag] $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') head=$(git -C $root rev-parse --short HEAD) dirty=$((git -C $root status --short | Measure-Object).Count) ===" | Out-File -Append $codes
Set-Location $root
if (-not $SkipRestore) {
    & $msb $proj /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false *> "$logs\build_restore_$Tag.log"
    "Restore code=$LASTEXITCODE" | Out-File -Append $codes
}
& $msb $proj /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false `
    "/p:OutputPath=$bin" "/p:IntermediateOutputPath=$obj" *> "$logs\build_app_$Tag.log"
"Release app code=$LASTEXITCODE -> $bin" | Out-File -Append $codes
& pwsh -NoLogo -NoProfile -File "$root\tools\effmaker\probes\build_all.ps1" `
    -Bin "$root\BecquerelMonitor\$($bin.TrimEnd('\'))" -Out $out *> "$logs\build_probes_$Tag.log"
"probes build_all code=$LASTEXITCODE -> $out" | Out-File -Append $codes
$a = (Get-FileHash "$root\BecquerelMonitor\$($bin.TrimEnd('\'))\BecquerelMonitor.exe" -Algorithm SHA256).Hash.ToLower()
$p = (Get-FileHash "$out\BecquerelMonitor.exe" -Algorithm SHA256).Hash.ToLower()
"sha256 app=$a probe=$p равны=$($a -eq $p) проб=$((Get-ChildItem $out -Filter *.exe).Count)" | Out-File -Append $codes
Get-Content $codes | Select-Object -Last 5
