# П164 — в копии корпуса заменить узел <Efficiency>…</Efficiency> каждого спектра на узел из ДРУГОГО источника
# (каталог спектров или коммит git). Работа в байтах: переводы строк и BOM не трогаются.
# python swap_eff.py <target_spectra_dir> --from-dir=<dir> | --from-git=<repo>@<commit>:<relpath_spectra>
import os, re, subprocess, sys

tgt = sys.argv[1]
src_dir = None
git = None
for a in sys.argv[2:]:
    if a.startswith('--from-dir='):
        src_dir = a[len('--from-dir='):]
    if a.startswith('--from-git='):
        git = a[len('--from-git='):]
OPEN = b'<Efficiency>'
CLOSE = b'</Efficiency>'


def outer_nodes(buf):
    """Внешние узлы кривой: начинаются с <Efficiency><Guid>, конец — по глубине вложенных <Efficiency>."""
    out = []
    pos = 0
    while True:
        i = buf.find(OPEN + b'<Guid>', pos)
        if i < 0:
            return out
        depth = 0
        j = i
        while True:
            o = buf.find(OPEN, j)
            c = buf.find(CLOSE, j)
            if c < 0:
                raise ValueError('незакрытый узел')
            if 0 <= o < c:
                depth += 1
                j = o + len(OPEN)
            else:
                depth -= 1
                j = c + len(CLOSE)
                if depth == 0:
                    out.append((i, j))
                    pos = j
                    break
n_sw = n_same = n_none = 0
for name in sorted(os.listdir(tgt)):
    if not name.endswith('.xml'):
        continue
    p = os.path.join(tgt, name)
    with open(p, 'rb') as fh:
        body = fh.read()
    if src_dir:
        sp = os.path.join(src_dir, name)
        if not os.path.exists(sp):
            continue
        with open(sp, 'rb') as fh:
            src = fh.read()
    else:
        repo, rest = git.split('@', 1)
        commit, rel = rest.split(':', 1)
        r = subprocess.run(['git', '-C', repo, 'show', f'{commit}:{rel}/{name}'], capture_output=True)
        if r.returncode != 0:
            continue
        src = r.stdout
    st = outer_nodes(body)
    ss = outer_nodes(src)
    mt = [body[a:b] for a, b in st]
    ms = [src[a:b] for a, b in ss]
    if len(mt) != len(ms):
        print(f'!! {name}: узлов в цели {len(mt)}, в источнике {len(ms)} — пропущен')
        continue
    if not mt:
        n_none += 1
        continue
    if mt == ms:
        n_same += 1
        continue
    parts = []
    last = 0
    for (a, b), rep in zip(st, ms):
        parts.append(body[last:a])
        parts.append(rep)
        last = b
    parts.append(body[last:])
    body2 = b''.join(parts)
    with open(p, 'wb') as fh:
        fh.write(body2)
    n_sw += 1
print(f'заменено {n_sw}, уже равны {n_same}, без узла {n_none}')
