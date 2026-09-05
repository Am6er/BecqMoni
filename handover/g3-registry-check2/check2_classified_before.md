# Проверка 2 до правки — разбор находок по целям (06.09.2026, снимок 01:42)

Источник: `run_before.txt`. Разряды: «ложная» — не ссылка на файл вовсе; «настоящая, историчная» — файл был в git и удалён (строка рассказывает историю); «настоящая, файла нет» — имя, которого в дереве не было никогда (памятка вне репозитория, чужое дерево, обещанный и не написанный файл); «на диске вне git» — лежит в игнорируемом каталоге.

## Список 1 — «нет в дереве» (печатается к глазам, в счёт НЕ входит)

| файл | строка | № | цель | разряд | причина |
|---|---|---|---|---|---|
| TODO.md | 211 | A23 | `*.ru.resx` | ложная | шаблон с `*` |
| TODO.md | 223 | A35 | `Lu176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 238 | A49 | `Th232(WT-20).xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 240 | A50 | `*.xml` | ложная | шаблон с `*` |
| TODO.md | 240 | A50 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 264 | A75 | `corpus/spectra/<ключ>.xml` | ложная | обобщение `<…>`/`…` в описании |
| TODO.md | 273 | A84 | `--map=<ключ,guid>.csv` | ложная | обобщение `<…>`/`…` в описании |
| TODO.md | 277 | A88 | `*.resx` | ложная | шаблон с `*` |
| TODO.md | 277 | A88 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 280 | A91 | `*.Designer.cs` | ложная | шаблон с `*` |
| TODO.md | 280 | A91 | `ru.resx` | ложная | хвост имени файла, не имя |
| TODO.md | 284 | A95 | `BecquerelMonitor/FullSpectrumAnalysis/FsaOverlay.cs` | настоящая, историчная | был в git, удалён: `BecquerelMonitor/FullSpectrumAnalysis/FsaOverlay.cs` |
| TODO.md | 287 | A98 | `*.cs` | ложная | шаблон с `*` |
| TODO.md | 287 | A98 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 287 | A98 | `Designer.cs` | ложная | хвост имени файла, не имя |
| TODO.md | 288 | A99 | `*.ru.resx` | ложная | шаблон с `*` |
| TODO.md | 296 | A107 | `BecquerelMonitor/**/*.resx` | ложная | шаблон с `*` |
| TODO.md | 296 | A107 | `ru.resx` | ложная | хвост имени файла, не имя |
| TODO.md | 304 | A115 | `Designer.cs` | ложная | хвост имени файла, не имя |
| TODO.md | 307 | A118 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 307 | A118 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 307 | A118 | `Foo.Designer.cs` | ложная | образец имени `Foo.*` |
| TODO.md | 307 | A118 | `Foo.cs` | ложная | образец имени `Foo.*` |
| TODO.md | 307 | A118 | `Foo.resx` | ложная | образец имени `Foo.*` |
| TODO.md | 313 | A124 | `*.Designer.cs` | ложная | шаблон с `*` |
| TODO.md | 313 | A124 | `*.resx` | ложная | шаблон с `*` |
| TODO.md | 315 | A126 | `Designer.cs` | ложная | хвост имени файла, не имя |
| TODO.md | 315 | A126 | `ru.resx` | ложная | хвост имени файла, не имя |
| TODO.md | 334 | A145 | `.cs/.Designer.cs/.resx/.ru.resx` | настоящая, историчная | был в git, удалён: `BecquerelMonitor/EffCalcMCDialog.ru.resx` |
| TODO.md | 334 | A145 | `.csproj` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 334 | A145 | `FsaOverlay.cs` | настоящая, историчная | был в git, удалён: `BecquerelMonitor/FullSpectrumAnalysis/FsaOverlay.cs` |
| TODO.md | 356 | A167 | `config/ROI/*.xml` | ложная | шаблон с `*` |
| TODO.md | 356 | A167 | `config\ROI\*.xml` | ложная | шаблон с `*` |
| TODO.md | 357 | A168 | `.csproj` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 362 | A173 | `*.cs` | ложная | шаблон с `*` |
| TODO.md | 362 | A173 | `Designer.cs` | ложная | хвост имени файла, не имя |
| TODO.md | 376 | A187 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 377 | A188 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 377 | A188 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 377 | A188 | `Designer.cs` | ложная | хвост имени файла, не имя |
| TODO.md | 378 | A189 | `check_resx*.py` | ложная | шаблон с `*` |
| TODO.md | 389 | A201 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 389 | A201 | `Resources*.resx` | ложная | шаблон с `*` |
| TODO.md | 390 | A202 | `*.resx` | ложная | шаблон с `*` |
| TODO.md | 390 | A202 | `Designer.cs` | ложная | хвост имени файла, не имя |
| TODO.md | 409 | A221 | `config/device/*.xml` | ложная | шаблон с `*` |
| TODO.md | 417 | A229 | `*.resx` | ложная | шаблон с `*` |
| TODO.md | 417 | A229 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 429 | A244 | `.Designer.cs` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 429 | A244 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 429 | A244 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 429 | A244 | `DC*.cs` | ложная | шаблон с `*` |
| TODO.md | 429 | A244 | `FSAReportView*.cs` | ложная | шаблон с `*` |
| TODO.md | 429 | A244 | `Properties/Resources*.resx` | ложная | шаблон с `*` |
| TODO.md | 429 | A244 | `ROI*.cs` | ложная | шаблон с `*` |
| TODO.md | 429 | A244 | `XPTable/*.cs` | ложная | шаблон с `*` |
| TODO.md | 429 | A244 | `tools/check_resx*.py` | ложная | шаблон с `*` |
| TODO.md | 432 | A247 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 432 | A247 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 433 | A248 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 433 | A248 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 434 | A249 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 465 | S3 | `FullSpectrumAnalysis/*.cs` | ложная | шаблон с `*` |
| TODO.md | 474 | S44 | `BecquerelMonitor/**/*.cs` | ложная | шаблон с `*` |
| TODO.md | 474 | S44 | `check_resx*.py` | ложная | шаблон с `*` |
| TODO.md | 475 | E19 | `!AS80x80\Lu-176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 475 | E19 | `!ASN16\Lu176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 477 | S52 | `!AS80x80\Lu-176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 482 | S109 | `tools/pie/out_s109/s109_peaks_m*.csv` | ложная | шаблон с `*` |
| TODO.md | 554 | W10 | `BecquerelMonitor/**/*.cs` | ложная | шаблон с `*` |
| TODO.md | 564 | E17 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 580 | R1 | `BecquerelMonitor/LibraryPeakFitter.cs` | настоящая, историчная | был в git, удалён: `BecquerelMonitor/LibraryPeakFitter.cs` |
| TODO.md | 580 | R1 | `LineSetBuilder.cs` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 589 | C2 | `*.resx` | ложная | шаблон с `*` |
| TODO.md | 589 | C2 | `BecquerelMonitor/**/*.cs` | ложная | шаблон с `*` |
| TODO.md | 590 | C4 | `config/ROI/*.xml` | ложная | шаблон с `*` |
| TODO.md | 606 | N17 | `xray_widths.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 606 | N17 | `xraydb.sqlite` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 616 | D7 | `xray_widths.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 624 | D35 | `BecquerelMonitor/**/*.cs` | ложная | шаблон с `*` |
| TODO.md | 657 | T43 | `data.perfView.csv` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 665 | S137 | `components.csv` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 671 | S143 | `corpus-base-out-mini.md` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 676 | T21 | `t21_co60_off.json` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 676 | T21 | `t21_sc_cyl_off.json` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 676 | T21 | `t21_sc_mar_off.json` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 677 | T63 | `*_spline_components.csv` | ложная | шаблон с `*` |
| TODO.md | 677 | T63 | `*_spline_limits.csv` | ложная | шаблон с `*` |
| TODO.md | 677 | T63 | `*_spline_runs.csv` | ложная | шаблон с `*` |
| TODO.md | 677 | T63 | `config\NuclideDefinition.OLD.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 680 | T67 | `runs.csv` | настоящая, историчная | был в git, удалён: `tools/LibraryFitLab/scripts/probe_runs.csv` |
| TODO.md | 681 | T68 | `corpus-run-with-matrix.md` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 681 | T68 | `scripts/*.ps1` | ложная | шаблон с `*` |
| TODO.md | 683 | T76 | `*.md` | ложная | шаблон с `*` |
| TODO.md | 686 | T79 | `.sqlite` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 686 | T79 | `.xml` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 687 | T80 | `Ghost.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 687 | T80 | `config\NuclideDefinition.OLD.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 687 | T80 | `config\device\*.xml` | ложная | шаблон с `*` |
| TODO.md | 688 | T81 | `*.sqlite` | ложная | шаблон с `*` |
| TODO.md | 694 | T89 | `.cs` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 694 | T89 | `tools\effmaker\*.cs` | ложная | шаблон с `*` |
| TODO.md | 694 | T89 | `tools\effmaker\probes\*.cs` | ложная | шаблон с `*` |
| TODO.md | 695 | T91 | `corpus-base-out-v5.md` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 695 | T91 | `memory/corpus-base-out-v5.md` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 699 | T97 | `*.ru.resx` | ложная | шаблон с `*` |
| TODO.md | 706 | T113 | `handover-*.md` | ложная | шаблон с `*` |
| TODO.md | 706 | T113 | `scratchpad/f10/t113_scan.py` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 711 | T119 | `.ps1` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 711 | T119 | `config\GeometryMaterials.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 718 | T128 | `corpus-base-<база>.md` | ложная | обобщение `<…>`/`…` в описании |
| TODO.md | 727 | T137 | `tools/**/*.py` | ложная | шаблон с `*` |
| TODO.md | 729 | T139 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 729 | T139 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 735 | T145 | `*.sqlite` | ложная | шаблон с `*` |
| TODO.md | 739 | T149 | `config\ROI\*.xml` | ложная | шаблон с `*` |
| TODO.md | 739 | T149 | `config\device\*.xml` | ложная | шаблон с `*` |
| TODO.md | 745 | T155 | `.ps1` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 745 | T155 | `.py` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 746 | T156 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 746 | T156 | `Foo.resx` | ложная | образец имени `Foo.*` |
| TODO.md | 746 | T156 | `check_resx*.py` | ложная | шаблон с `*` |
| TODO.md | 747 | T157 | `Designer.cs` | ложная | хвост имени файла, не имя |
| TODO.md | 747 | T157 | `ru.resx` | ложная | хвост имени файла, не имя |
| TODO.md | 748 | T158 | `*.resx` | ложная | шаблон с `*` |
| TODO.md | 748 | T158 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 748 | T158 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 756 | T166 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 764 | T174 | `config/ROI/*.xml` | ложная | шаблон с `*` |
| TODO.md | 767 | T177 | `FsaOverlay.cs` | настоящая, историчная | был в git, удалён: `BecquerelMonitor/FullSpectrumAnalysis/FsaOverlay.cs` |
| TODO.md | 772 | T182 | `.cs` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 778 | T188 | `*.md` | ложная | шаблон с `*` |
| TODO.md | 778 | T188 | `tools/**/*.ps1` | ложная | шаблон с `*` |
| TODO.md | 780 | T190 | `config\ROI\*.xml` | ложная | шаблон с `*` |
| TODO.md | 780 | T190 | `config\device\*.xml` | ложная | шаблон с `*` |
| TODO.md | 784 | T194 | `.cs` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 789 | T199 | `tools/effmaker/**/*.cs` | ложная | шаблон с `*` |
| TODO.md | 790 | T200 | `*.cs` | ложная | шаблон с `*` |
| TODO.md | 790 | T200 | `*.py` | ложная | шаблон с `*` |
| TODO.md | 790 | T200 | `tools/**/*.ps1` | ложная | шаблон с `*` |
| TODO.md | 793 | T203 | `*.resx` | ложная | шаблон с `*` |
| TODO.md | 793 | T203 | `<ключ>.xml` | ложная | обобщение `<…>`/`…` в описании |
| TODO.md | 793 | T203 | `Designer.cs` | ложная | хвост имени файла, не имя |
| TODO.md | 793 | T203 | `config/ROI/*.xml` | ложная | шаблон с `*` |
| TODO.md | 793 | T203 | `handover/*-2026-09-05-*.md` | ложная | шаблон с `*` |
| TODO.md | 799 | T209 | `*.sqlite` | ложная | шаблон с `*` |
| TODO.md | 813 | T223 | `!AS80x80\Lu-176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| TODO.md | 813 | T223 | `*.resx` | ложная | шаблон с `*` |
| TODO.md | 813 | T223 | `--map=<ключ,guid>.csv` | ложная | обобщение `<…>`/`…` в описании |
| TODO.md | 813 | T223 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 813 | T223 | `Designer.cs` | ложная | хвост имени файла, не имя |
| TODO.md | 813 | T223 | `Foo.Designer.cs` | ложная | образец имени `Foo.*` |
| TODO.md | 813 | T223 | `FsaOverlay.cs` | настоящая, историчная | был в git, удалён: `BecquerelMonitor/FullSpectrumAnalysis/FsaOverlay.cs` |
| TODO.md | 813 | T223 | `check_resx*.py` | ложная | шаблон с `*` |
| TODO.md | 813 | T223 | `config/ROI/*.xml` | ложная | шаблон с `*` |
| TODO.md | 813 | T223 | `corpus/spectra/<ключ>.xml` | ложная | обобщение `<…>`/`…` в описании |
| TODO.md | 815 | T226 | `.cs` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 815 | T226 | `.csproj` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 818 | T229 | `*.ru.resx` | ложная | шаблон с `*` |
| TODO.md | 819 | T230 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 819 | T230 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 822 | T233 | `.csproj` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 823 | T234 | `*.ru.resx` | ложная | шаблон с `*` |
| TODO.md | 823 | T234 | `Foo.Designer.cs` | ложная | образец имени `Foo.*` |
| TODO.md | 824 | T235 | `*.resx` | ложная | шаблон с `*` |
| TODO.md | 824 | T235 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| TODO.md | 827 | T238 | `*.Designer.cs` | ложная | шаблон с `*` |
| TODO.md | 827 | T238 | `*.resx` | ложная | шаблон с `*` |
| TODO.md | 827 | T238 | `add_keys*.py` | ложная | шаблон с `*` |
| TODO.md | 827 | T238 | `tools/**/*.py` | ложная | шаблон с `*` |
| DONE.md | 72 | E20 | `config\GeometryMaterials.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 72 | E20 | `tools/effmaker/probes/MaterialLibraryProbe.cs` | настоящая, историчная | был в git, удалён: `tools/effmaker/probes/MaterialLibraryProbe.cs` |
| DONE.md | 75 | B15 | `!AS80x80\Lu-176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 75 | B15 | `!AS80x80\Th232(WT-20).xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 76 | B9 | `!AS80x80\Lu-176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 76 | B9 | `!ASN16\Lu176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 86 | S81 | `Th232_29.07.2022.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 87 | S80 | `Th232_29.07.2022.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 88 | S69 | `Th232_29.07.2022.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 88 | S69 | `limits.csv` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 89 | S68 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| DONE.md | 89 | S68 | `Th232_29.07.2022.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 89 | S68 | `limits.csv` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 90 | S71 | `Th232_29.07.2022.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 91 | S70 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| DONE.md | 91 | S70 | `Th232_29.07.2022.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 94 | B12 | `.xml` | ложная | голое расширение (`.resx`, `.cs`…) |
| DONE.md | 94 | B12 | `…_Точечная-5см_5cm.xml` | ложная | обобщение `<…>`/`…` в описании |
| DONE.md | 97 | B10 | `!AS80x80\Lu-176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 97 | B10 | `!AS80x80\Th232(WT-20).xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 103 | S60 | `lines_<режим>.csv` | ложная | обобщение `<…>`/`…` в описании |
| DONE.md | 105 | S72 | `Th232_29.07.2022.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 106 | S78 | `Th232_29.07.2022.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 106 | S78 | `components.csv` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 107 | S79 | `Th232_29.07.2022.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 108 | S85 | `Th232_29.07.2022.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 130 | A10 | `ru.resx` | ложная | хвост имени файла, не имя |
| DONE.md | 131 | A11 | `.ru.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| DONE.md | 132 | A12 | `*.ru.resx` | ложная | шаблон с `*` |
| DONE.md | 132 | A12 | `PulseView.ru.resx` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 135 | A15 | `*.cs` | ложная | шаблон с `*` |
| DONE.md | 169 | S49 | `components.csv` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 170 | S47 | `!AS80x80\Lu-176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 174 | S46 | `!AS80x80\Lu-176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 174 | S46 | `Lu176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 183 | S77 | `BecquerelMonitor/FullSpectrumAnalysis/FsaOverlay.cs` | настоящая, историчная | был в git, удалён: `BecquerelMonitor/FullSpectrumAnalysis/FsaOverlay.cs` |
| DONE.md | 204 | S32 | `gainscan.py` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 204 | S32 | `xray_widths.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 211 | S51 | `runs.csv` | настоящая, историчная | был в git, удалён: `tools/LibraryFitLab/scripts/probe_runs.csv` |
| DONE.md | 214 | S67 | `BecquerelMonitor/FullSpectrumAnalysis/FsaOverlay.cs` | настоящая, историчная | был в git, удалён: `BecquerelMonitor/FullSpectrumAnalysis/FsaOverlay.cs` |
| DONE.md | 219 | S36 | `out_v5/*_spline_components.csv` | ложная | шаблон с `*` |
| DONE.md | 221 | S102 | `.ps1` | ложная | голое расширение (`.resx`, `.cs`…) |
| DONE.md | 223 | S9 | `components.csv` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 223 | S9 | `limits.csv` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 225 | S98 | `*_runs.csv` | ложная | шаблон с `*` |
| DONE.md | 225 | S98 | `runs.csv` | настоящая, историчная | был в git, удалён: `tools/LibraryFitLab/scripts/probe_runs.csv` |
| DONE.md | 226 | S99 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| DONE.md | 228 | S103 | `runs.csv` | настоящая, историчная | был в git, удалён: `tools/LibraryFitLab/scripts/probe_runs.csv` |
| DONE.md | 229 | S104 | `DC*.cs` | ложная | шаблон с `*` |
| DONE.md | 229 | S104 | `EnergySpectrumView*.cs` | ложная | шаблон с `*` |
| DONE.md | 229 | S104 | `MainForm*.cs` | ложная | шаблон с `*` |
| DONE.md | 229 | S104 | `components.csv` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 231 | S110 | `runs.csv` | настоящая, историчная | был в git, удалён: `tools/LibraryFitLab/scripts/probe_runs.csv` |
| DONE.md | 232 | S111 | `--dump=curves.csv` | ложная | ключ командной строки, не путь |
| DONE.md | 232 | S111 | `BecquerelMonitor/FWHMPeakDetector.cs` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 232 | S111 | `runs.csv` | настоящая, историчная | был в git, удалён: `tools/LibraryFitLab/scripts/probe_runs.csv` |
| DONE.md | 235 | S101 | `*_runs.csv` | ложная | шаблон с `*` |
| DONE.md | 246 | T33 | `config\device\*.xml` | ложная | шаблон с `*` |
| DONE.md | 247 | T34 | `!ASN16\Lu176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 255 | B23 | `tools/CORPUS/corpus/spectra/*.xml` | ложная | шаблон с `*` |
| DONE.md | 261 | B8 | `!AS80x80\Lu-176.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 268 | W21 | `.resx` | ложная | голое расширение (`.resx`, `.cs`…) |
| DONE.md | 315 | E31 | `tools/pie/out_v5/*_spline_runs.csv` | ложная | шаблон с `*` |
| DONE.md | 326 | R3 | `gainscan.py` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 326 | R3 | `gainscan_stub.py` | настоящая, историчная | был в git, удалён: `tools/LibraryFitLab/scripts/gainscan_stub.py` |
| DONE.md | 333 | C3 | `Designer.cs` | ложная | хвост имени файла, не имя |
| DONE.md | 349 | N15 | `xraydb.sqlite` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 384 | N18 | `*_components.csv` | ложная | шаблон с `*` |
| DONE.md | 384 | N18 | `*_limits.csv` | ложная | шаблон с `*` |
| DONE.md | 384 | N18 | `*_runs.csv` | ложная | шаблон с `*` |
| DONE.md | 384 | N18 | `runs.csv` | настоящая, историчная | был в git, удалён: `tools/LibraryFitLab/scripts/probe_runs.csv` |
| DONE.md | 404 | T57 | `*.cs` | ложная | шаблон с `*` |
| DONE.md | 429 | T38 | `runs.csv` | настоящая, историчная | был в git, удалён: `tools/LibraryFitLab/scripts/probe_runs.csv` |
| DONE.md | 432 | T40 | `.md` | ложная | голое расширение (`.resx`, `.cs`…) |
| DONE.md | 439 | T45 | `*.sqlite` | ложная | шаблон с `*` |
| DONE.md | 441 | T41 | `**/*.cs` | ложная | шаблон с `*` |
| DONE.md | 441 | T41 | `BecquerelMonitor/**/*.cs` | ложная | шаблон с `*` |
| DONE.md | 444 | T75 | `*.sqlite` | ложная | шаблон с `*` |
| DONE.md | 444 | T75 | `memory/msbuild-through-powershell-only.md` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 450 | T60 | `Nano.xml` | на диске вне git | лежит `BecquerelMonitor/bin/Debug/config/ROI/Nano.xml` (игнорируемый каталог) |
| DONE.md | 450 | T60 | `config\ROI\Nano.xml` | на диске вне git | лежит `BecquerelMonitor/bin/Debug/config/ROI/Nano.xml` (игнорируемый каталог) |
| DONE.md | 451 | T65 | `*_components.csv` | ложная | шаблон с `*` |
| DONE.md | 451 | T65 | `*_limits.csv` | ложная | шаблон с `*` |
| DONE.md | 451 | T65 | `*_runs.csv` | ложная | шаблон с `*` |
| DONE.md | 453 | T88 | `.cs` | ложная | голое расширение (`.resx`, `.cs`…) |
| DONE.md | 453 | T88 | `config\ROI\*.xml` | ложная | шаблон с `*` |
| DONE.md | 453 | T88 | `config\chuzhoy.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 459 | T72 | `bin\Debug\config\*.xml` | ложная | шаблон с `*` |
| DONE.md | 460 | T83 | `.cs` | ложная | голое расширение (`.resx`, `.cs`…) |
| DONE.md | 463 | T105 | `*.cs` | ложная | шаблон с `*` |
| DONE.md | 463 | T105 | `runs.csv` | настоящая, историчная | был в git, удалён: `tools/LibraryFitLab/scripts/probe_runs.csv` |
| DONE.md | 466 | T2 | `docs/rules.md` | настоящая, файла нет | ни в git, ни на диске в дереве |
| DONE.md | 467 | T62 | `BecquerelMonitor/EfficiencyMakerForm*.cs` | ложная | шаблон с `*` |
| DONE.md | 469 | T27 | `xray_widths.xml` | настоящая, файла нет | ни в git, ни на диске в дереве |

Итого целей: 264; ложная — 180; на диске вне git — 2; настоящая, историчная — 19; настоящая, файла нет — 63

## Список 2 — «НЕТ В РЕПОЗИТОРИИ, лежат только на диске» (В СЧЁТ, красит сторож)

| файл | строка | № | цель | разряд | причина |
|---|---|---|---|---|---|
| TODO.md | 165 | S61 | `lines_spline.csv` | на диске вне git | лежит `tools/pie/out_b17base/lines_spline.csv` (игнорируемый каталог) |
| TODO.md | 165 | S61 | `tools/pie/out_s60/lines_spline.csv` | на диске вне git | лежит `tools/pie/out_b17base/lines_spline.csv` (игнорируемый каталог) |
| TODO.md | 662 | S134 | `s109_peaks_m1.0.csv` | на диске вне git | лежит `tools/pie/out_s109/s109_peaks_m1.0.csv` (игнорируемый каталог) |
| TODO.md | 662 | S134 | `tools/pie/out_s109/s109_peaks_m1.0.csv` | на диске вне git | лежит `tools/pie/out_s109/s109_peaks_m1.0.csv` (игнорируемый каталог) |
| TODO.md | 677 | T63 | `.appwd.json` | на диске вне git | лежит `tools/CORPUS/scripts/wd_a73/.appwd.json` (игнорируемый каталог) |
| TODO.md | 679 | T66 | `.appwd.json` | на диске вне git | лежит `tools/CORPUS/scripts/wd_a73/.appwd.json` (игнорируемый каталог) |
| TODO.md | 681 | T68 | `.appwd.json` | на диске вне git | лежит `tools/CORPUS/scripts/wd_a73/.appwd.json` (игнорируемый каталог) |
| TODO.md | 681 | T68 | `wd_app\.appwd.json` | на диске вне git | лежит `tools/CORPUS/scripts/wd_a73/.appwd.json` (игнорируемый каталог) |
| TODO.md | 687 | T80 | `.appwd.json` | на диске вне git | лежит `tools/CORPUS/scripts/wd_a73/.appwd.json` (игнорируемый каталог) |
| TODO.md | 690 | T84 | `.appwd.json` | на диске вне git | лежит `tools/CORPUS/scripts/wd_a73/.appwd.json` (игнорируемый каталог) |
| TODO.md | 724 | T134 | `tools/effmaker/out/a85/band.py` | на диске вне git | лежит `tools/effmaker/out/a85/band.py` (игнорируемый каталог) |
| TODO.md | 743 | T153 | `<wd>\config\device\effprobe.xml` | ложная | обобщение `<…>`/`…` в описании |
| TODO.md | 808 | T218 | `.claude/settings.local.json` | на диске вне git | лежит `.claude/settings.local.json` (игнорируемый каталог) |
| TODO.md | 813 | T223 | `.appwd.json` | на диске вне git | лежит `tools/CORPUS/scripts/wd_a73/.appwd.json` (игнорируемый каталог) |
| TODO.md | 813 | T223 | `.claude/settings.local.json` | на диске вне git | лежит `.claude/settings.local.json` (игнорируемый каталог) |

Итого целей: 15; ложная — 1; на диске вне git — 14

