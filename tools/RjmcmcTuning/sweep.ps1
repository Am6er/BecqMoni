<#
  RJMCMC deconvolution parameter sweep + recall scoring helper.
  Runs RjmcmcHarness.exe over one or more spectrum XML files with a small grid of
  deconvolution parameters and saves each run's stdout so score.py can grade recall
  against the tabular Th-232 / Ra-226 gamma lines.

  Usage (from a Developer PowerShell):
    .\sweep.ps1 -Spectra 'C:\path\Th-232.xml','C:\path\Charoit.xml' -OutDir '.\out'
    python score.py .\out\*.txt

  Notes:
    * Deconvolution is OFF by default in the device config; the harness flag
      --use-deconvolution=true is required to exercise it.
    * The winning configuration is expressed entirely through the harness CLI knobs,
      so no changes to the production BecquerelMonitor code are needed.
#>
param(
    [string[]] $Spectra = @(),
    [string]   $Harness = "$PSScriptRoot\..\..\BecquerelMonitor\bin\Debug_Codex\RjmcmcHarness.exe",
    [string]   $OutDir  = "$PSScriptRoot\out"
)

if (-not (Test-Path $Harness)) { throw "Harness not found: $Harness (build BecquerelMonitor, then copy RjmcmcHarness.exe next to BecquerelMonitor.exe)" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# tag ; extra CLI args (deconvolution knobs). All perf/recall trade-offs live here.
$configs = @(
    @{ tag='finderonly'; args=@('--use-deconvolution=false') },
    @{ tag='default';    args=@('--use-deconvolution=true') },
    @{ tag='recommended';args=@('--use-deconvolution=true','--roi-radius=4','--max-extra=4') }  # best recall on the test set
)

foreach ($sp in $Spectra) {
    $name = [IO.Path]::GetFileNameWithoutExtension($sp)
    foreach ($snr in 4,6) {
        foreach ($c in $configs) {
            $file = Join-Path $OutDir ("{0}__snr{1}__{2}.txt" -f $name,$snr,$c.tag)
            $a = @("--input=$sp","--snr=$snr",'--bg=substract') + $c.args
            $sw = [Diagnostics.Stopwatch]::StartNew()
            & $Harness @a 2>&1 | Out-File $file -Encoding utf8
            $sw.Stop()
            $summary = (Get-Content $file | Select-String 'DeconvExtras')
            "{0,-40} {1}  [{2} ms]" -f "$name snr$snr $($c.tag)", $summary, $sw.ElapsedMilliseconds
        }
    }
}

Write-Host ""
Write-Host "Score with:  python `"$PSScriptRoot\score.py`" `"$OutDir\*.txt`""
