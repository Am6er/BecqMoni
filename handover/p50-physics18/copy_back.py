# -*- coding: utf-8 -*-
"""Перенос правок КОДА из worktree p50 в основное дерево — sha-copy-back (П50, 14.09.2026; образец — П37).

Правило распорядителя: файл копируется ТОЛЬКО если его версия в основном дереве равна
версии `1dbdb4bb` (`git diff --quiet 1dbdb4bb -- <файл>`); отличается — НЕ копировать и
назвать. `.rmx`, кривые (`corpus/spectra/*.xml`, `response/`) и `TODO.md`/`DONE.md` не
переносятся вовсе — это П51. Файлы П49 (FullSpectrumAnalysis/*.cs, CorpusFsaProbe.cs,
FsaCascadeProbe.cs, appwd_plan.ps1) в списке отсутствуют и не трогаются. Переводы строк: у каждого файла сохраняется соглашение
ОСНОВНОГО дерева (там часть файлов лежит LF-целиком, worktree — CRLF), сверка — байтами.

  python copy_back.py [--apply]      (без --apply — только проверка и печать)
"""
import hashlib
import io
import os
import subprocess
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
WT = r'D:\BqMoni_Claude\p50\wt'
BASE = '1dbdb4bb'
FILES = [
    'BecquerelMonitor/EfficiencyMaker/ResponseMatrix.cs',
    'BecquerelMonitor/EfficiencyMaker/EfficiencySimulator.cs',
    'BecquerelMonitor/EfficiencyMaker/ElectronData.cs',
    'BecquerelMonitor/EfficiencyMaker/EstarCalculator.cs',
    'BecquerelMonitor/EfficiencyMaker/BremsstrahlungData.cs',
    'tools/check_matrix_keys.py',
    'tools/effmaker/probes/CorpusMatrixProbe.cs',
    'tools/effmaker/probes/CorpusEffProbe.cs',
    'tools/effmaker/probes/G4RawProbe.cs',
    'database/scheme.md',
    'tools/effmaker/handover-response-matrix.md',
]


def sha(b):
    return hashlib.sha256(b).hexdigest()


def endings(b):
    crlf = b.count(b'\r\n')
    lf = b.count(b'\n') - crlf
    return crlf, lf


def main(argv):
    apply = '--apply' in argv
    rows = []
    for rel in FILES:
        src = os.path.join(WT, *rel.split('/'))
        dst = os.path.join(ROOT, *rel.split('/'))
        clean = subprocess.call(['git', 'diff', '--quiet', BASE, '--', rel], cwd=ROOT) == 0
        changed_wt = subprocess.call(['git', 'diff', '--quiet', BASE, '--', rel], cwd=WT) != 0
        sb = open(src, 'rb').read()
        db = open(dst, 'rb').read()
        crlf_d, lf_d = endings(db)
        crlf_s, lf_s = endings(sb)
        # соглашение основного дерева
        if crlf_d == 0 and lf_d > 0:
            out = sb.replace(b'\r\n', b'\n')
            conv = 'CRLF->LF'
        else:
            out = sb
            conv = 'как есть'
        status = 'ЧИСТ' if clean else 'ОТЛИЧАЕТСЯ ОТ %s — НЕ КОПИРУЮ' % BASE
        action = 'нет'
        if clean and changed_wt:
            if apply:
                io.open(dst, 'wb').write(out)
                action = 'скопирован'
            else:
                action = 'скопировался бы'
        elif clean and not changed_wt:
            action = 'в worktree не менялся'
        rows.append((rel, status, action, conv, sha(out)[:16], (crlf_s, lf_s), (crlf_d, lf_d)))
    for r in rows:
        print('%-58s %-40s %-22s %-9s sha16 %s  wt(CRLF,LF)=%s  main(CRLF,LF)=%s' % r)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
