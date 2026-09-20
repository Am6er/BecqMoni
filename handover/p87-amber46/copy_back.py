# -*- coding: utf-8 -*-
r"""Перенос правок из worktree p87 в основное дерево — sha-снимок (П87, 16.09.2026; образец — П50 copy_back.py).

Протокол: файл копируется ТОЛЬКО если его версия в основном дереве равна версии `8b164a98`
(sha256 нормализованных байтов рабочей копии = sha256 блоба `git show 8b164a98:<путь>`);
отличается — чужая правка, НЕ копировать и назвать (распорядитель разведёт). Переводы строк: у
каждого файла сохраняется соглашение ОСНОВНОГО дерева (часть файлов там LF-целиком, worktree — CRLF),
сверка — байтами. `TODO.md`/`DONE.md`, `.rmx`, `.qk` не переносятся.

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
WT = r'D:\BqMoni_Claude\p87\wt'
BASE = '8b164a98'
FILES = [
    'BecquerelMonitor/EfficiencyMaker/EfficiencySimulator.cs',
    'BecquerelMonitor/EfficiencyMaker/ResponseMatrix.cs',
    'BecquerelMonitor/EfficiencyMaker/ResponseMatrixBuilder.cs',
    'BecquerelMonitor/FullSpectrumAnalysis/AngularCorrelation.cs',
    'BecquerelMonitor/FullSpectrumAnalysis/FsaAnalyzer.cs',
    'BecquerelMonitor/FullSpectrumAnalysis/FsaCascadeSummer.cs',
    'BecquerelMonitor/FullSpectrumAnalysis/FsaMatrixBinding.cs',
    'tools/CORPUS/scripts/appwd_plan.ps1',
    'tools/check_fsa_showcase.py',
    'tools/effmaker/probes/AngularQkProbe.cs',
    'tools/effmaker/probes/CorpusFsaProbe.cs',
    'tools/effmaker/probes/CorpusMatrixProbe.cs',
    'tools/effmaker/probes/FsaCascadeProbe.cs',
    'tools/effmaker/probes/MatrixDiffProbe.cs',
    'tools/effmaker/probes/SumPeakProbe.cs',
]


def sha(b):
    return hashlib.sha256(b).hexdigest()


def endings(b):
    crlf = b.count(b'\r\n')
    lf = b.count(b'\n') - crlf
    return crlf, lf


def blob(rel):
    return subprocess.run(['git', 'show', BASE + ':' + rel], cwd=ROOT, capture_output=True).stdout


def norm(b):
    return b.replace(b'\r\n', b'\n')


def main(argv):
    apply = '--apply' in argv
    copied, refused, same = [], [], []
    for rel in FILES:
        src = os.path.join(WT, *rel.split('/'))
        dst = os.path.join(ROOT, *rel.split('/'))
        base = blob(rel)
        sb = open(src, 'rb').read()
        db = open(dst, 'rb').read()
        clean = sha(norm(db)) == sha(norm(base))
        changed_wt = sha(norm(sb)) != sha(norm(base))
        crlf_d, lf_d = endings(db)
        crlf_s, lf_s = endings(sb)
        if crlf_d == 0 and lf_d > 0:
            out = norm(sb)
            conv = 'CRLF->LF'
        elif lf_d == 0 and crlf_d > 0:
            out = norm(sb).replace(b'\n', b'\r\n')
            conv = 'CRLF'
        else:
            out = sb
            conv = 'как есть (смесь в дереве: crlf %d, lf %d)' % (crlf_d, lf_d)
        if not clean:
            refused.append(rel)
            status = 'ОТЛИЧАЕТСЯ ОТ %s (чужая правка) — НЕ КОПИРУЮ' % BASE
            action = 'нет'
        elif not changed_wt:
            same.append(rel)
            status = 'чист'
            action = 'worktree = основа, копировать нечего'
        else:
            status = 'чист'
            if apply:
                io.open(dst, 'wb').write(out)
                ok = sha(norm(open(dst, 'rb').read())) == sha(norm(sb))
                action = 'скопирован (%s)%s' % (conv, '' if ok else ' ⛔ СВЕРКА НЕ СОШЛАСЬ')
            else:
                action = 'скопировался бы (%s)' % conv
            copied.append(rel)
        print('%-62s %-48s %s' % (rel, status, action))
    print()
    print('перенесено %d, отказано (чужая правка) %d, без изменений %d' % (len(copied), len(refused), len(same)))
    for r in refused:
        print('  ⛔ НЕ ПЕРЕНЕСЁН: %s' % r)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
