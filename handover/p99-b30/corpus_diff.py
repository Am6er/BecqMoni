# -*- coding: utf-8 -*-
r"""П99 — что изменилось в копиях спектров корпуса worktree против HEAD (93dafe11), по частям файла:
узел <Efficiency> (guid/имя/клеймо), узел ПШПВ (PowerFwhmCalibration a, p → ПШПВ на 662 в каналах),
энергокалибровка (коэффициенты), данные спектра (отсчёты), прочее. Одна строка на спектр с разницей.
  python corpus_diff.py [--all]
"""
import io
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

WT = r'D:\BqMoni_Claude\p99\wt'
REL = 'tools/CORPUS/corpus/spectra'
BASE = '93dafe11'


def blob(rel):
    r = subprocess.run(['git', 'show', BASE + ':' + rel], cwd=WT, capture_output=True)
    return r.stdout if r.returncode == 0 else None


def parse(data):
    root = ET.fromstring(data)
    rd = root.find('ResultDataList/ResultData')
    out = {}
    eff = rd.find('Efficiency')
    if eff is not None:
        out['eff_guid'] = eff.findtext('Guid')
        out['eff_name'] = eff.findtext('Name')
        out['eff_stamp'] = eff.findtext('ComputeStamp')
        g = eff.find('Geometry')
        out['eff_pd'] = g.findtext('PointDistance') if g is not None else None
        out['eff_shield'] = g.findtext('InShield') if g is not None else None
        pts = eff.findall('Curve/ROIEfficiencyData')
        out['eff_n'] = len(pts)
        # значение кривой у 662 (ближайший узел)
        best = None
        for p in pts:
            e = float(p.findtext('Energy')); v = float(p.findtext('Efficiency'))
            if best is None or abs(e - 661.66) < abs(best[0] - 661.66):
                best = (e, v)
        out['eff_662'] = best
    else:
        out['eff_guid'] = None
    es = rd.find('EnergySpectrum')
    cal = es.find('EnergyCalibration')
    out['ecal'] = tuple(c.text for c in cal.findall('Coefficients/Coefficient')) if cal is not None else None
    out['nch'] = int(es.findtext('NumberOfChannels') or 1024)
    fw = None
    for tag in ('PowerFwhmCalibration', 'SqrtFwhmCalibration', 'SimpleSqrtFwhmCalibration'):
        fw = rd.find(tag)
        if fw is not None:
            break
    if fw is not None:
        out['fwhm'] = (fw.tag, tuple(c.text for c in fw.findall('Coefficients/Coefficient')))
    else:
        out['fwhm'] = None
    data_txt = es.findtext('SpectrumDataList') or es.findtext('Spectrum') or ''
    counts = [c.text for c in es.findall('SpectrumDataList/*')]
    out['counts'] = hash(tuple(counts)) if counts else hash(data_txt)
    out['live'] = es.findtext('LiveTime')
    bg = rd.find('BackgroundEnergySpectrum')
    out['bg'] = ET.tostring(bg)[:100000].__hash__() if bg is not None else None
    return out


def fwhm662(fw):
    if not fw or fw[0] != 'PowerFwhmCalibration' or len(fw[1]) < 2:
        return None
    try:
        return float(fw[1][0]), float(fw[1][1])
    except ValueError:
        return None


def ecal_at(coefs, ch):
    return sum(float(c) * ch ** i for i, c in enumerate(coefs))


def ch_of(coefs, e, nch=1024):
    lo, hi = 0.0, float(nch)
    for _ in range(80):
        mid = 0.5 * (lo + hi)
        if ecal_at(coefs, mid) < e:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def main(argv):
    allf = '--all' in argv
    names = sorted(f for f in os.listdir(os.path.join(WT, *REL.split('/'))) if f.endswith('.xml'))
    changed = 0
    print('%-26s %s' % ('спектр', 'что изменилось против HEAD'))
    for n in names:
        rel = REL + '/' + n
        new = open(os.path.join(WT, *rel.split('/')), 'rb').read()
        old = blob(rel)
        if old is None:
            print('%-26s НОВЫЙ СПЕКТР' % n[:-4]); changed += 1
            continue
        try:
            a, b = parse(old), parse(new)
        except ET.ParseError as e:
            print('%-26s XML не разобран: %s' % (n[:-4], e)); continue
        what = []
        if a['eff_guid'] != b['eff_guid'] or a.get('eff_stamp') != b.get('eff_stamp') or a.get('eff_662') != b.get('eff_662'):
            if a['eff_guid'] is None and b['eff_guid'] is not None:
                what.append('узел кривой ПОЯВИЛСЯ (%s)' % b['eff_name'])
            elif b['eff_guid'] is None:
                what.append('узел кривой ПРОПАЛ')
            else:
                e0 = a.get('eff_662'); e1 = b.get('eff_662')
                ratio = (e1[1] / e0[1]) if e0 and e1 and e0[1] else float('nan')
                what.append('кривая: %s→%s, ε(662) ×%.4f%s' % (
                    a['eff_name'], b['eff_name'], ratio,
                    '' if a['eff_guid'] == b['eff_guid'] else ' guid сменился'))
        if a['fwhm'] != b['fwhm']:
            fa, fb = fwhm662(a['fwhm']), fwhm662(b['fwhm'])
            if fa and fb:
                try:
                    ch = ch_of(b['ecal'], 661.66, b['nch'])
                    w0 = fa[0] * ch ** fa[1]; w1 = fb[0] * ch ** fb[1]
                    what.append('узел ПШПВ на 662: %.2f → %.2f кан (%+.1f %%)' % (w0, w1, (w1 / w0 - 1) * 100))
                except Exception:
                    what.append('узел ПШПВ: a %.4g→%.4g, p %.4f→%.4f' % (fa[0], fb[0], fa[1], fb[1]))
            else:
                what.append('узел ПШПВ: %s→%s' % (a['fwhm'][0] if a['fwhm'] else None, b['fwhm'][0] if b['fwhm'] else None))
        if a['ecal'] != b['ecal']:
            try:
                ch = ch_of(a['ecal'], 661.66, a['nch'])
                what.append('энергокалибровка: 662 кэВ HEAD → %.2f кэВ новой шкалой' % ecal_at(b['ecal'], ch))
            except Exception:
                what.append('энергокалибровка')
        if a['counts'] != b['counts']:
            what.append('ДАННЫЕ')
        if a['live'] != b['live']:
            what.append('живое время')
        if a['bg'] != b['bg']:
            what.append('фон')
        if what or allf:
            if what:
                changed += 1
            print('%-26s %s' % (n[:-4], '; '.join(what) if what else 'побитово по разбору (косметика XML не судится)'))
    print('\nспектров с изменениями по разбору: %d из %d' % (changed, len(names)))


if __name__ == '__main__':
    main(sys.argv[1:])
