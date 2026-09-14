# -*- coding: utf-8 -*-
"""bqp15_check.py — контроль: raw в дампе П12 = отсчёты сырого спектра корпуса (первый <Spectrum> в XML)."""
import sys, io, csv, os, re
import xml.etree.ElementTree as ET
sys.stdout.reconfigure(encoding='utf-8')
REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
SP = os.path.join(REPO, r'tools\CORPUS\corpus\spectra')
DUMP = r'C:\Users\moroz\bqp12_out\a_dump'
for key in ['G1S16_Co60_P5', 'AS80_Cs137_0cm', 'ASN16_Cs137', 'G1S24_Na22_P5']:
    root = ET.parse(os.path.join(SP, key + '.xml')).getroot()
    specs = root.findall('.//Spectrum')
    pts = [[int(p.text) for p in s.findall('DataPoint')] for s in specs]
    chi = list(csv.DictReader(io.open(os.path.join(DUMP, key + '_chi.csv'), encoding='utf-8-sig')))
    raw = [int(r['raw']) for r in chi]
    bg = [float(r['bg']) for r in chi]
    same = [i for i, s in enumerate(pts) if s == raw]
    # фон: bg = α·bgcounts? оценим α по сумме
    for i, s in enumerate(pts):
        if i in same: continue
        sb = sum(s); sbg = sum(bg)
        print('  %s: блок %d: Σ=%d, Σbg дампа=%.1f, α=%.4f' % (key, i, sb, sbg, sbg / sb if sb else 0))
    print('%s: блоков <Spectrum> %d, длины %s; raw дампа = блок %s; Σraw=%d' % (key, len(pts), [len(p) for p in pts], same, sum(raw)))
