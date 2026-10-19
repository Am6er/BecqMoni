# -*- coding: utf-8 -*-
"""П343: снимок отслеживаемых файлов tools/CORPUS (sha256 + tar) и сверка двух снимков.

  python snap_corpus.py snap <корень дерева> <имя>        -> snap/<имя>.json, snap/<имя>.tar
  python snap_corpus.py diff <корень дерева> <имя-до>     -> сверка дерева со снимком
"""
import hashlib
import json
import os
import subprocess
import sys
import tarfile

HERE = os.path.dirname(os.path.abspath(__file__))
SNAP = os.path.join(HERE, 'snap')


def tracked(root, sub='tools/CORPUS'):
    out = subprocess.check_output(['git', '-C', root, 'ls-files', '-z', '--', sub])
    return [p for p in out.decode('utf-8').split('\0') if p]


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for block in iter(lambda: fh.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def snap(root, name):
    os.makedirs(SNAP, exist_ok=True)
    files = tracked(root)
    table = {}
    with tarfile.open(os.path.join(SNAP, name + '.tar'), 'w') as tar:
        for rel in files:
            full = os.path.join(root, rel)
            if not os.path.isfile(full):
                table[rel] = None
                continue
            table[rel] = sha(full)
            tar.add(full, arcname=rel)
    with open(os.path.join(SNAP, name + '.json'), 'w', encoding='utf-8') as fh:
        json.dump(table, fh, ensure_ascii=False, indent=0, sort_keys=True)
    print('снимок %s: файлов %d' % (name, len(table)))


def strip_header(data):
    """XML-заголовок и BOM прочь — чтобы отличить «только заголовок» от содержимого."""
    if data.startswith(b'\xef\xbb\xbf'):
        data = data[3:]
    if data.startswith(b'<?xml'):
        end = data.find(b'?>')
        data = data[end + 2:].lstrip(b'\r\n')
    return data


def diff(root, name):
    before = json.load(open(os.path.join(SNAP, name + '.json'), encoding='utf-8'))
    tar = tarfile.open(os.path.join(SNAP, name + '.tar'))
    now = tracked(root)
    same = 0
    header = []
    eol = []
    content = []
    gone = []
    for rel in sorted(before):
        full = os.path.join(root, rel)
        if not os.path.isfile(full):
            gone.append(rel)
            continue
        h = sha(full)
        if h == before[rel]:
            same += 1
            continue
        old = tar.extractfile(rel).read()
        new = open(full, 'rb').read()
        if strip_header(old) == strip_header(new):
            header.append(rel)
        elif old.replace(b'\r\n', b'\n') == new.replace(b'\r\n', b'\n'):
            eol.append(rel)
        else:
            content.append(rel)
    new_files = sorted(set(now) - set(before))
    print('побайтно те же: %d' % same)
    print('только XML-заголовок/BOM: %d' % len(header))
    print('только переводы строк: %d' % len(eol))
    for rel in eol:
        print('    ' + rel)
    print('ПО СОДЕРЖИМОМУ: %d' % len(content))
    for rel in content:
        print('    ' + rel)
    print('пропало: %d' % len(gone))
    for rel in gone:
        print('    ' + rel)
    print('новых отслеживаемых: %d' % len(new_files))
    with open(os.path.join(SNAP, name + '.diff.json'), 'w', encoding='utf-8') as fh:
        json.dump(dict(header=header, eol=eol, content=content, gone=gone, new=new_files), fh,
                  ensure_ascii=False, indent=1)


def restore(root, name, kind):
    """Вернуть побайтно из снимка файлы указанного рода (header / eol)."""
    d = json.load(open(os.path.join(SNAP, name + '.diff.json'), encoding='utf-8'))
    tar = tarfile.open(os.path.join(SNAP, name + '.tar'))
    n = 0
    for rel in d[kind]:
        data = tar.extractfile(rel).read()
        with open(os.path.join(root, rel), 'wb') as fh:
            fh.write(data)
        n += 1
    print('возвращено из снимка (%s): %d' % (kind, n))


if __name__ == '__main__':
    for _s in (sys.stdout, sys.stderr):
        try:
            _s.reconfigure(encoding='utf-8', errors='replace')
        except Exception:
            pass
    cmd, root, name = sys.argv[1:4]
    if cmd == 'snap':
        snap(root, name)
    elif cmd == 'diff':
        diff(root, name)
    elif cmd == 'restore':
        restore(root, name, sys.argv[4])
