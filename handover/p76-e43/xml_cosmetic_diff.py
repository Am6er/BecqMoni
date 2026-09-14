# -*- coding: utf-8 -*-
r"""П76 — что именно изменила пересборка корпуса в `corpus/spectra/*.xml` против git HEAD: по модулю BOM и
объявления XML (`<?xml version='1.0' …?>` ElementTree'а против `"1.0"` XmlDocument'а — П66 §9) содержимое
обязано быть тем же у всех, кроме спектров, которым полоса писала новый узел `<Efficiency>`.

    python handover/p76-e43/xml_cosmetic_diff.py [<ключ> …]     (без ключей — все изменённые по git status)
Печатает по файлу: «косметика» (равны по модулю BOM/объявления) или «СОДЕРЖАНИЕ» с числом отличающихся строк.
"""
import io
import os
import re
import subprocess
import sys

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
SPECTRA = 'tools/CORPUS/corpus/spectra/'
DECL = re.compile(br'^\xef?\xbb?\xbf?<\?xml[^>]*\?>\s*')


def norm(b):
    b = b.replace(b'\r\n', b'\n')
    if b.startswith(b'\xef\xbb\xbf'):
        b = b[3:]
    return DECL.sub(b'', b, count=1)


def git_show(path):
    p = subprocess.run(['git', '-C', ROOT, 'show', 'HEAD:' + path], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    return p.stdout if p.returncode == 0 else None


def main():
    keys = sys.argv[1:]
    if not keys:
        st = subprocess.run(['git', '-C', ROOT, '-c', 'core.quotepath=false', 'status', '--porcelain', '--', SPECTRA],
                            stdout=subprocess.PIPE).stdout.decode('utf-8')
        keys = [l[3:].strip().split('/')[-1][:-4] for l in st.splitlines() if l.strip() and l[3:].strip().endswith('.xml')]
    cosmetic, content = [], []
    for k in sorted(keys):
        path = SPECTRA + k + '.xml'
        a = git_show(path)
        with open(os.path.join(ROOT, *path.split('/')), 'rb') as fh:
            b = fh.read()
        if a is None:
            content.append((k, 'нет в HEAD')); continue
        na, nb = norm(a), norm(b)
        if na == nb:
            cosmetic.append(k)
        else:
            la, lb = na.split(b'\n'), nb.split(b'\n')
            diff = sum(1 for x, y in zip(la, lb) if x != y) + abs(len(la) - len(lb))
            tag = ''
            if b'<Efficiency>' in nb and (b'<Efficiency>' not in na or
                                          re.search(br'<Efficiency>.*?</Efficiency>', na, re.S).group(0)
                                          != re.search(br'<Efficiency>.*?</Efficiency>', nb, re.S).group(0)):
                tag = ' (узел <Efficiency> другой)'
            content.append((k, u'%d строк%s' % (diff, tag)))
    print(u'изменённых спектров: %d; косметика (BOM/объявление XML): %d; СОДЕРЖАНИЕ: %d' % (len(keys), len(cosmetic), len(content)))
    for k, what in content:
        print(u'  СОДЕРЖАНИЕ %-26s %s' % (k, what))
    return 0


if __name__ == '__main__':
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding='utf-8', errors='replace')
        except Exception:
            pass
    sys.exit(main())
