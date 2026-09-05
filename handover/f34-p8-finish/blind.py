# -*- coding: utf-8 -*-
"""F34: слепые пятна сканера по файлам доли П8 — каждое ОТДЕЛЬНЫМ сплошным
поиском по вырезанному коду (литералы и комментарии не считаются).

   StringBuilder.Append(число) · string.Join над числами · string.Concat над
   object[] · интерполяция · Parse/TryParse/Convert.To* без культуры.
"""
import io, os, re, sys
# ⛔ `scan_culture.py` читает `sys.argv` на верхнем уровне — импорт модуля целиком
#    падает. Берём из него ТОЛЬКО функцию, исполнив её текст.
_sc = io.open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                           '..', 'o17-decimal-dot', 'scan_culture.py'),
              encoding='utf-8').read()
_ns = {}
exec(_sc[_sc.index('def strip_code'):_sc.index('\ndef ', _sc.index('def strip_code'))], _ns)
strip_code = _ns['strip_code']  # тот же вырезатель, что у сканера

ROOT = 'C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/'
FILES = """DoseRate.cs NucBase/NucBase.cs NucBase/Decay.cs NucBase/DecayRad.cs N42/Util.cs
NuclideDefinitionForm.cs PercentageProgressBar.cs FWHMPeakDetector/PeakFinder.cs
AudioInputDeviceController.cs ChanNumberChangeDialog.cs EnergySpectrum.cs GeometryEditorPanel.cs
GeometryMaterialEditorForm.cs GlobalConfigManager.cs NuclideDefinition.cs NuclideSetForm.cs
ObsidianDeviceController.cs PowerFwhmCalibration.cs RadiaCodeDeviceController.cs
SimpleSqrtFwhmCalibration.cs SpectrumCutOffDialog.cs SqrtFwhmCalibration.cs
WinMM/NativeMethods.cs WinMM/WaveIn.cs WinMM/WaveOut.cs DocumentManager.cs
PolynomialEnergyCalibration.cs PulseView.cs Utils/BecquerelCoefficient.cs""".split()

PATTERNS = [
    ('StringBuilder.Append', re.compile(r'\.Append(?:Line|Format)?\s*\(')),
    ('string.Join',          re.compile(r'\bstring\.Join\s*\(', re.I)),
    ('string.Concat',        re.compile(r'\bstring\.Concat\s*\(', re.I)),
    ('интерполяция',         re.compile(r'\$"')),
    ('Parse/TryParse',       re.compile(r'\b(?:double|float|decimal|int|long|short|byte|uint|ulong|ushort|sbyte|Double|Single|Decimal|Int32|Int64|Int16|Byte|UInt32|UInt64|UInt16|SByte)\.(?:Try)?Parse\s*\(')),
    ('Convert.To*',          re.compile(r'\bConvert\.To(?:Double|Single|Decimal|Int32|Int64|Int16|Byte|UInt32|UInt64|UInt16|SByte|String)\s*\(')),
]
HAS_CULTURE = re.compile(r'CultureInfo\.InvariantCulture|InvariantInfo|NumberFormatInfo\.Invariant|XmlConvert\.')

total = {}
for rel in FILES:
    path = ROOT + 'BecquerelMonitor/' + rel
    src = io.open(path, encoding='utf-8-sig', newline='').read()
    code = strip_code(src)
    lines_code = code.split('\n')
    lines_src = src.split('\n')
    for name, rx in PATTERNS:
        for i, cl in enumerate(lines_code):
            if not rx.search(cl):
                continue
            # окно из трёх строк: вызов часто перенесён
            win = '\n'.join(lines_code[i:i + 3])
            ok = bool(HAS_CULTURE.search(win))
            total.setdefault(name, []).append(
                (ok, 'BecquerelMonitor/%s:%d  %s' % (rel, i + 1, lines_src[i].strip()[:180])))

for name, _ in PATTERNS:
    rows = total.get(name, [])
    bad = [r for r in rows if not r[0]]
    print('== %-22s всего %3d, БЕЗ явной культуры рядом: %d' % (name, len(rows), len(bad)))
    for _, s in bad:
        print('     ' + s)
    print()
