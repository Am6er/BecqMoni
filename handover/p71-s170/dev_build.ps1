# П71: быстрая сборка ОДНОЙ пробы в отдельный каталог dev (копия оснастки wd_p71a + проба) — для отладки пробы.
# Итоговые числа — только из заверенного build_all + mk_appwd (A77/T226); этот каталог — черновик.
param([string]$Probe = 'SumPeakProbe')
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$wt = 'D:\BqMoni_Claude\p71\wt'
$bin = "$wt\BecquerelMonitor\bin\Release_p71a"
$wd = "$wt\tools\CORPUS\scripts\wd_p71a"
$dev = 'D:\BqMoni_Claude\p71\dev'
if (-not (Test-Path "$dev\BecquerelMonitor.exe")) {
  robocopy $wd $dev /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
  "dev: скопирована оснастка wd_p71a, robocopy $LASTEXITCODE"
}
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
$refs = @("/r:$bin\BecquerelMonitor.exe", '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll',
  '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', "/r:$bin\Microsoft.Data.Sqlite.dll",
  "/r:$bin\WeifenLuo.WinFormsUI.Docking.dll", "/r:$facades\netstandard.dll")
$src = "$wt\tools\effmaker\probes"
$extra = @("$src\_TargetFramework.cs", "$src\FsaTuningReport.cs", "$src\ProbeDeviceConfig.cs", "$src\ResidualScan.cs")
& $csc /nologo /target:exe /langversion:7.3 /d:TRACE "/out:$dev\$Probe.exe" @refs "$src\$Probe.cs" @extra
"csc code $LASTEXITCODE"
Copy-Item "$dev\FsaCascadeProbe.exe.config" "$dev\$Probe.exe.config" -Force
exit $LASTEXITCODE
