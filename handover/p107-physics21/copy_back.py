# -*- coding: utf-8 -*-
r"""Перенос правок из worktree p107 в основное дерево — sha-снимок (П107, 19.09.2026; образец — П103 copy_back.py).

Протокол: файл копируется ТОЛЬКО если его версия в основном дереве равна ОСНОВЕ — блобу `645d4870`
(sha256 НОРМАЛИЗОВАННЫХ по переводам строк байтов рабочей копии = sha256 нормализованного блоба
`git show 645d4870:<путь>`; нормализация — ответ на грабли П100 «LF-форма индекса против CRLF дерева»);
отличается — чужая правка после старта, НЕ копировать и назвать. Переводы строк: у каждого файла сохраняется соглашение
ОСНОВНОГО дерева, сверка — байтами. `TODO.md`/`DONE.md`, `.rmx` не переносятся.

Список файлов — `git status --porcelain` worktree (изменённые + новые под tools/CORPUS/corpus/spectra).

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
WT = r'D:\BqMoni_Claude\p107\wt'
BASE = '645d4870'
# файл -> sha256 байтов рабочей копии основного дерева на старте полосы (П107: чужих застейженных правок нет)
SPECIAL_BASE = {}
SKIP = ('TODO.md', 'DONE.md')


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


def changed_in_wt():
    out = subprocess.run(['git', 'status', '--porcelain', '-z'], cwd=WT, capture_output=True).stdout.decode('utf-8')
    files = []
    for item in out.split('\0'):
        if not item.strip():
            continue
        status, rel = item[:2], item[3:]
        if status.strip() in ('M', 'A', '??', 'AM', 'MM'):
            files.append(rel)
    return sorted(files)


def main(argv):
    apply = '--apply' in argv
    copied, refused, same, created = [], [], [], []
    files = [f for f in changed_in_wt() if not f.endswith(SKIP)]
    special_raw = {}
    for rel in SPECIAL_BASE:
        p = os.path.join(r'D:\BqMoni_Claude\p97\art', 'base_sha_' + os.path.basename(rel).replace('.cs', '') + '.txt')
        special_raw[rel] = io.open(p, encoding='utf-8').read().split()[0]
    for rel in files:
        src = os.path.join(WT, *rel.split('/'))
        dst = os.path.join(ROOT, *rel.split('/'))
        if os.path.isdir(src):
            continue
        sb = open(src, 'rb').read()
        exists = os.path.isfile(dst)
        db = open(dst, 'rb').read() if exists else b''
        if rel in SPECIAL_BASE:
            clean = exists and sha(db) == special_raw[rel]
            base_desc = 'снимок старта'
            changed_wt = True
        else:
            base = blob(rel)
            if not base and not exists:
                clean = True
                base_desc = 'новый файл'
                changed_wt = True
            else:
                clean = sha(norm(db)) == sha(norm(base))
                base_desc = BASE
                changed_wt = sha(norm(sb)) != sha(norm(base))
        crlf_d, lf_d = endings(db) if exists else endings(sb)
        if crlf_d == 0 and lf_d > 0:
            out = norm(sb)
            conv = 'LF'
        elif lf_d == 0 and crlf_d > 0:
            out = norm(sb).replace(b'\n', b'\r\n')
            conv = 'CRLF'
        else:
            out = sb
            conv = 'как есть (смесь: crlf %d, lf %d)' % (crlf_d, lf_d)
        if not clean:
            refused.append(rel)
            status = 'ОТЛИЧАЕТСЯ ОТ %s (чужая правка) — НЕ КОПИРУЮ' % base_desc
            action = 'нет'
        elif not changed_wt:
            same.append(rel)
            status = 'чист'
            action = 'worktree = основа, копировать нечего'
        else:
            status = 'чист (%s)' % base_desc
            if apply:
                d = os.path.dirname(dst)
                if not os.path.isdir(d):
                    os.makedirs(d)
                io.open(dst, 'wb').write(out)
                ok = sha(norm(open(dst, 'rb').read())) == sha(norm(sb))
                action = 'скопирован (%s)%s' % (conv, '' if ok else ' ⛔ СВЕРКА НЕ СОШЛАСЬ')
            else:
                action = 'скопировался бы (%s)' % conv
            (created if not exists else copied).append(rel)
        print('%-62s %-48s %s' % (rel, status, action))
    print()
    print('перенесено %d, создано %d, отказано (чужая правка) %d, без изменений %d'
          % (len(copied), len(created), len(refused), len(same)))
    for r in refused:
        print('  ⛔ НЕ ПЕРЕНЕСЁН: %s' % r)
    return 0 if not refused else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
