# -*- coding: utf-8 -*-
"""F34: сплошной поиск РАЗДЕЛИТЕЛЯ РАЗРЯДОВ по файлам доли П8.

Решение Amber 05.09.2026: группировки разрядов нет вовсе — `n…` → `f…`.
Ищутся форматы, дающие группу: `N`/`n` с цифрой, `#,#`, `C` (валюта),
`P` (проценты) — и в коде, и в обоих `Resources*.resx` (там формат стоит
в самой строке ресурса: `{0:N0}`).
"""
import io, re, sys

ROOT = 'C:/Users/moroz/AppData/Local/Temp/claude/C--Users-moroz-source-repos-BQ-Eng-res--NET-4-8/0dfa526c-1c92-45cd-b97b-70d6b9e6b585/scratchpad/head/BecquerelMonitor/'
FILES = """DoseRate.cs NucBase/NucBase.cs NucBase/Decay.cs NucBase/DecayRad.cs N42/Util.cs
NuclideDefinitionForm.cs PercentageProgressBar.cs FWHMPeakDetector/PeakFinder.cs
AudioInputDeviceController.cs ChanNumberChangeDialog.cs EnergySpectrum.cs GeometryEditorPanel.cs
GeometryMaterialEditorForm.cs GlobalConfigManager.cs NuclideDefinition.cs NuclideSetForm.cs
ObsidianDeviceController.cs PowerFwhmCalibration.cs RadiaCodeDeviceController.cs
SimpleSqrtFwhmCalibration.cs SpectrumCutOffDialog.cs SqrtFwhmCalibration.cs
WinMM/NativeMethods.cs WinMM/WaveIn.cs WinMM/WaveOut.cs DocumentManager.cs
PolynomialEnergyCalibration.cs PulseView.cs Utils/BecquerelCoefficient.cs
Properties/Resources.resx Properties/Resources.ru.resx""".split()

# в коде: ToString("N0"), ToString("n2"), "{0:N0}", "#,##0.00"
# ⛔ голый `[NnCP]\d` внутри ЛЮБОЙ строки ловит «N42» — первый заход дал 20 мест,
#    все до одного про формат файла N42. Голая форма засчитывается, только если
#    ВЕСЬ литерал и есть спецификатор.
CODE = re.compile(r'"(?:[^"\n]*(?:\{\d+(?:,-?\d+)?:[^}"\n]*[NnCP]\d?[^}"\n]*\}|#,#)[^"\n]*|[NnCP]\d?)"')
# в resx: формат стоит внутри <value>
RESX = re.compile(r'\{\d+(?:,-?\d+)?:[^}]*[NnCP]\d?[^}]*\}')

total = 0
for rel in FILES:
    src = io.open(ROOT + rel, encoding='utf-8-sig', newline='').read()
    rx = RESX if rel.endswith('.resx') else CODE
    for i, line in enumerate(src.split('\n')):
        for m in rx.finditer(line):
            total += 1
            print('%s:%d\t%s' % (rel, i + 1, line.strip()[:180]))
print('---')
print('мест с разделителем разрядов в доле П8: %d' % total)
