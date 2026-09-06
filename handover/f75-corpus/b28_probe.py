# -*- coding: utf-8 -*-
u"""B28: сверка device_guid с закреплённой личностью и опыт «файла нет».

Два плеча:
  1) штатное окружение — совпадает ли device_guid(det) с DEVICE_IDENTITY[det][0];
  2) положительный контроль — APPDATA уводится в несуществующий каталог, и
     видно, падает device_guid или нет. Комментарий build_corpus.py:116
     обещает «отсутствие файла больше не мешает».
"""
import os
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
SCRIPTS = os.environ.get('BQ_SCRIPTS') or os.path.join(ROOT, 'tools', 'CORPUS', 'scripts')


def load(appdata=None):
    for m in list(sys.modules):
        if m in ('build_corpus',):
            del sys.modules[m]
    if appdata is not None:
        os.environ['APPDATA'] = appdata
    sys.path.insert(0, SCRIPTS)
    import build_corpus
    return build_corpus


def arm(title, appdata):
    print('=' * 72)
    print(title)
    bc = load(appdata)
    print('  APPDATA_DEV =', bc.APPDATA_DEV)
    ok = bad = miss = 0
    for det in sorted(bc.COPY_DEVICE):
        src = os.path.join(*bc.COPY_DEVICE[det])
        exists = os.path.isfile(src)
        pinned = bc.DEVICE_IDENTITY[det][0]
        try:
            got = bc.device_guid(det)
            verdict = 'СОВПАЛ' if got == pinned else 'РАЗОШЁЛСЯ %s' % got
            if got == pinned:
                ok += 1
            else:
                bad += 1
        except Exception as exc:
            verdict = 'ОТКАЗ %s: %s' % (type(exc).__name__, str(exc)[:60])
            miss += 1
        print('  %-11s файл %-3s  %s' % (det, 'да' if exists else 'НЕТ', verdict))
    print('  ИТОГО: совпало %d, разошлось %d, отказов %d' % (ok, bad, miss))
    return ok, bad, miss


real = os.environ.get('APPDATA', '')
a = arm(u'ПЛЕЧО 1: штатное окружение', real)
b = arm(u'ПЛЕЧО 2: положительный контроль — APPDATA в несуществующий каталог',
        os.path.join(HERE, 'net-takogo-kataloga'))
print()
print('ПРИГОВОР: плечо 1 %r; плечо 2 %r' % (a, b))
