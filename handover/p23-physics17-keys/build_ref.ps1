# П23 12.09.2026 — ЭТАЛОН «ДО ПРАВКИ»: worktree на HEAD (fb86bb45) без правок полосы,
# та же сборка Release, тот же build_all — для побитового контроля MatrixDiffProbe
# (ключи ВЫКЛ обязаны дать матрицу, равную эталону до бита). Правило T209: worktree
# снимается в конце полосы (`git worktree remove --force C:\Users\moroz\bqp23`).
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$wt = 'C:\Users\moroz\bqp23'
$out = "$root\handover\p23-physics17-keys"
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
Set-Location $root
if (-not (Test-Path $wt)) {
    & git worktree add $wt HEAD *> "$out\ref_worktree.log"
    "worktree code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes_ref.txt"
}
Set-Location $wt
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Restore /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false /nologo /v:m *> "$out\ref_restore.log"
"restore code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes_ref.txt"
& $msbuild "$wt\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_P23ref\' `
    /p:IntermediateOutputPath='obj\Release_P23ref\' /nologo /v:m *> "$out\ref_build_app.log"
"app code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes_ref.txt"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& pwsh -File "$wt\tools\effmaker\probes\build_all.ps1" -Bin 'BecquerelMonitor\bin\Release_P23ref' -Out 'tools\effmaker\probes\build_p23ref' *> "$out\ref_build_probes.log"
"probes code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes_ref.txt"
exit $LASTEXITCODE
