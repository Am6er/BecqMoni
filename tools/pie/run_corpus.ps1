# Сборка pie.exe и прогон полноспектральной декомпозиции по ВСЕМУ корпусу:
# 69 спектров, 23 группы детекторов. Использует готовые рабочие каталоги
# tools/CORPUS/scripts/wd_* (конфиг устройства + спектры корпуса).
#
# Мерить на подмножестве нельзя (см. CLAUDE.md): корпус специально растянут
# от 0.22 % (HPGe) до 15 % (Obsidian) полуширины, и критерий, настроенный по
# середине диапазона, виден только на краях.
#
#   pwsh tools/pie/run_corpus.ps1 [-Mode snip|spline|both] [-SkipBuild]
#                                 [-Groups ASN16,RC103] [-OutDir …] [-Extra '--flag']
#
# -Groups   подмножество групп (по умолчанию все 23) — только для отладки,
#           не для чисел, которые идут в журнал.
# -OutDir   куда писать CSV (по умолчанию tools/pie/out) — так соседствуют
#           две ветки одного сравнения.
# -Extra    дополнительные ключи харнесса, передаются каждому прогону.

param(
    [ValidateSet('snip', 'spline', 'both')]
    [string]$Mode = 'both',
    [switch]$SkipBuild,
    [switch]$DumpModels,
    [string[]]$Groups,
    [string]$OutDir,
    [string[]]$Extra
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$pieDir = Join-Path $repo 'tools\pie'
$outDir = if ($OutDir) { $OutDir } else { Join-Path $pieDir 'out' }
$wdRoot = Join-Path $repo 'tools\CORPUS\scripts'
$effTable = Join-Path $repo 'tools\CORPUS\data\eff_by_spectrum_lsrm.csv'
$setMap  = Join-Path $pieDir 'component_map.csv'
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'

# Состав библиотеки по умолчанию повторяет харнесс; здесь он нужен, чтобы
# добавить группе недостающий компонент, не трогая умолчание для всех.
$defaultComponents = 'Th-232,Ra-226,U-238,U-235,K-40,Cs-137,Am-241,Co-60,I-131,Eu-152,Ba-133,Xray-W,Xray-Pb,SE-2614,DE-2614'

# группа -> рабочий каталог (+ добавка к библиотеке). Кривые эффективности
# берутся из общей таблицы: спектр, которого в ней нет, считается без кривой
# (харнесс печатает предупреждение) — так ветка одна для всех групп.
$allGroups = @(
    @{ Name = 'AS1PRO';     Wd = 'wd_AS1PRO'     },
    @{ Name = 'AS80x80';    Wd = 'wd_AS80x80'    },
    @{ Name = 'ASN16';      Wd = 'wd_ASN16'      },
    @{ Name = 'ASN3';       Wd = 'wd_ASN3'       },
    @{ Name = 'ASN8_1024';  Wd = 'wd_ASN8_1024'  },
    @{ Name = 'ASN8_2048';  Wd = 'wd_ASN8_2048'  },
    @{ Name = 'ASN8_3000';  Wd = 'wd_ASN8_3000'  },
    @{ Name = 'ASN8_4096';  Wd = 'wd_ASN8_4096'  },
    @{ Name = 'ASN8_8192';  Wd = 'wd_ASN8_8192'  },
    @{ Name = 'CZT';        Wd = 'wd_CZT'        },
    @{ Name = 'CZT_TECD';   Wd = 'wd_CZT_TECD'   },
    @{ Name = 'G1S';        Wd = 'wd_G1S'        },
    @{ Name = 'GS4000';     Wd = 'wd_GS4000';    Add = 'Lu-176' },
    @{ Name = 'HPGE';       Wd = 'wd_HPGE'       },
    @{ Name = 'HPGE_GEM';   Wd = 'wd_HPGE_GEM'   },
    @{ Name = 'HPGE_GMX';   Wd = 'wd_HPGE_GMX'   },
    @{ Name = 'LABR_BRIL';  Wd = 'wd_LABR_BRIL'  },
    @{ Name = 'LaBr3';      Wd = 'wd_LaBr3'      },
    @{ Name = 'OBS';        Wd = 'wd_OBS'        },
    @{ Name = 'RC101';      Wd = 'wd_RC101'      },
    @{ Name = 'RC103';      Wd = 'wd_RC103'      },
    @{ Name = 'RC103g';     Wd = 'wd_RC103g'     },
    @{ Name = 'SrI2';       Wd = 'wd_SrI2'       }
)

$groupList = if ($Groups) { $allGroups | Where-Object { $Groups -contains $_.Name } } else { $allGroups }
if (-not $groupList) { throw "нет таких групп: $($Groups -join ',')" }

New-Item -ItemType Directory -Force $outDir | Out-Null

if (-not $SkipBuild) {
    $refWd = Join-Path $wdRoot $allGroups[0].Wd
    $exe = Join-Path $refWd 'pie.exe'
    $bqRef = Join-Path $refWd 'BecquerelMonitor.exe'
    $src = Join-Path $pieDir 'Program.cs'
    & $csc /nologo /target:exe /platform:anycpu /langversion:7.3 `
        "/out:$exe" "/r:$bqRef" `
        /r:System.dll /r:System.Core.dll /r:System.Xml.dll `
        /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        "$src"
    if ($LASTEXITCODE -ne 0) { throw "csc failed" }
    foreach ($g in $allGroups | Select-Object -Skip 1) {
        Copy-Item $exe (Join-Path (Join-Path $wdRoot $g.Wd) 'pie.exe') -Force
    }
    Write-Host "built: $exe"
}

$modes = if ($Mode -eq 'both') { @('snip', 'spline') } else { @($Mode) }
$sw = [Diagnostics.Stopwatch]::StartNew()

foreach ($m in $modes) {
    foreach ($g in $groupList) {
        $wd = Join-Path $wdRoot $g.Wd
        $prefix = Join-Path $outDir "$($g.Name)_$m"
        $cliArgs = @(
            "--input=spectra",
            "--mode=$m",
            "--out=$prefix",
            "--eff-curve=$effTable",
            "--component-map=$setMap"
        )
        if ($g.Add) { $cliArgs += "--components=$defaultComponents,$($g.Add)" }
        if ($Extra) { $cliArgs += $Extra }
        if ($DumpModels) { $cliArgs += "--dump-model=$(Join-Path $outDir "models_$($g.Name)_$m")" }
        Write-Host "=== $($g.Name) mode=$m ===" -ForegroundColor Cyan
        Push-Location $wd
        try {
            & (Join-Path $wd 'pie.exe') @cliArgs
            if ($LASTEXITCODE -ne 0) { Write-Warning "$($g.Name)/$m exit code $LASTEXITCODE" }
        }
        finally { Pop-Location }
    }
}

Write-Host ("done in {0:n0} s -> {1}" -f $sw.Elapsed.TotalSeconds, $outDir)
