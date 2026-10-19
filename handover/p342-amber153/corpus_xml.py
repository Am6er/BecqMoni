# -*- coding: utf-8 -*-
u"""П342: чтение и хирургическая правка корпусного XML спектра (только свои копии).

`read(path)` — словарь: каналы и калибровка спектра и фона, живые времена, геометрия.
`replace_background(text, counts, live=None, real=None)` — подменяет ТОЛЬКО массив
`DataPoint` узла `<BackgroundEnergySpectrum>` (и счётчики импульсов, и при нужде
времена) — калибровка фона, передний план и всё прочее не трогаются, как у
`tools/CORPUS/scripts/bg_from_spe.py`. Корпус в дереве этим модулем НЕ пишется.
"""
import io
import re
import xml.etree.ElementTree as ET


def _spec(node):
    cal = [float(c.text) for c in node.find('EnergyCalibration').find('Coefficients')]
    data = [float(d.text) for d in node.find('Spectrum')]
    return dict(cal=cal, counts=data, live=float(node.find('LiveTime').text),
                real=float(node.find('MeasurementTime').text),
                n=int(node.find('NumberOfChannels').text))


def energy(cal, ch):
    return sum(c * ch ** i for i, c in enumerate(cal))


def read(path):
    root = ET.parse(path).getroot()
    rd = root.find('ResultDataList').find('ResultData')
    out = dict(fg=_spec(rd.find('EnergySpectrum')))
    bg = rd.find('BackgroundEnergySpectrum')
    out['bg'] = _spec(bg) if bg is not None else None
    bf = rd.find('BackgroundSpectrumFile')
    out['bg_file'] = bf.text if bf is not None else ''
    eff = rd.find('Efficiency')
    geo = {}
    if eff is not None and eff.find('Geometry') is not None:
        g = eff.find('Geometry')
        for k in g:
            if len(k) == 0:
                geo[k.tag] = k.text
        src = g.find('Source')
        if src is not None:
            geo['SourceName'] = src.find('Name').text
            geo['SourceDensity'] = src.find('Density').text
        geo['EffName'] = eff.find('Name').text
    out['geo'] = geo
    out['start'] = rd.find('StartTime').text
    return out


def band(spec, e0, e1):
    s = 0.0
    for ch, c in enumerate(spec['counts']):
        e = energy(spec['cal'], float(ch))
        if e0 <= e < e1:
            s += c
    return s


def replace_background(text, counts, live=None, real=None):
    i = text.index('<BackgroundEnergySpectrum>')
    j = text.index('</BackgroundEnergySpectrum>', i)
    blk = text[i:j]
    n_old = len(re.findall(r'<DataPoint>', blk))
    if n_old != len(counts):
        raise ValueError('каналов фона %d, подаётся %d' % (n_old, len(counts)))
    it = iter(counts)
    blk = re.sub(r'<DataPoint>[-0-9.eE+]+</DataPoint>', lambda m: '<DataPoint>%d</DataPoint>' % next(it), blk)
    tot = int(sum(counts))
    blk = re.sub(r'<ValidPulseCount>\d+</ValidPulseCount>', '<ValidPulseCount>%d</ValidPulseCount>' % tot, blk)
    blk = re.sub(r'<TotalPulseCount>\d+</TotalPulseCount>', '<TotalPulseCount>%d</TotalPulseCount>' % tot, blk)
    if live is not None:
        blk = re.sub(r'<LiveTime>[^<]*</LiveTime>', '<LiveTime>%s</LiveTime>' % repr(float(live)), blk, count=1)
    if real is not None:
        blk = re.sub(r'<MeasurementTime>[^<]*</MeasurementTime>',
                     '<MeasurementTime>%s</MeasurementTime>' % repr(float(real)), blk, count=1)
    return text[:i] + blk + text[j:]


def load_text(path):
    raw = io.open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    return raw.decode('utf-8-sig'), bom


def save_text(path, text, bom):
    data = text.encode('utf-8')
    if bom:
        data = b'\xef\xbb\xbf' + data
    io.open(path, 'wb').write(data)
