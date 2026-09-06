# -*- coding: utf-8 -*-
"""Положительный контроль G7: у каждого из трёх вылеченных сторожей — две копии с подброшенной
первой строкой `main()` `print("⛔ КОНТРОЛЬ G7 … ✅ ➜ σ 𝄞")`: `fixed` (блок на месте) и
`broken` (блок снят). Копии кладутся РЯДОМ с оригиналами (они импортируют соседей через
`HERE`), гонятся через run1251.cmd (cmd, cp1251, без PYTHONIOENCODING), затем переносятся в
handover/g7-utf8-three/control/ и из scripts/ удаляются. Ожидание: broken — код 1 и
UnicodeEncodeError, строки приговора нет; fixed — код приговора (0/0/2), контрольная строка и
строка приговора есть.

  python make_control.py <repo>
"""
import os, shutil, subprocess, sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
REPO = sys.argv[1]
HERE = os.path.dirname(os.path.abspath(__file__))
S = os.path.join(REPO, 'tools', 'CORPUS', 'scripts')
RUN = os.path.join(REPO, 'handover', 'g5-cp1251', 'run1251.cmd')
INJECT = u'    print(u"⛔ КОНТРОЛЬ G7 — знак вне cp1251 первой строкой main(): ✅ ➜ σ 𝄞")\n'
BLOCK_HEAD = "for _stream in (sys.stdout, sys.stderr):\n"

CASES = [
    ('check_corpus', ['--key=ASN16_Th232'], u'плохих:', 0),
    ('gate_blind_check', ['--help'], u'--no-inject', 0),
    ('gaussfit_check', ['--ref=no_such_ref.py'], u'нет прежнего gaussfit', 2),
]

rows = []
for name, args, verdict_mark, want in CASES:
    src = open(os.path.join(S, name + '.py'), encoding='utf-8').read()
    assert src.count('def main():\n') == 1, name
    fixed = src.replace('def main():\n', 'def main():\n' + INJECT)
    # снять блок: строку for … и четыре строки тела (с любым отступом)
    i = fixed.index(BLOCK_HEAD.strip())
    line_start = fixed.rfind('\n', 0, i) + 1
    j = line_start
    for _ in range(5):
        j = fixed.index('\n', j) + 1
    broken = fixed[:line_start] + fixed[j:]
    assert 'reconfigure(' not in broken, name
    for kind, text in (('fixed', fixed), ('broken', broken)):
        fn = '_g7ctrl_%s_%s.py' % (name, kind)
        path = os.path.join(S, fn)
        open(path, 'w', encoding='utf-8', newline='').write(text)
        log = os.path.join(HERE, fn[:-3] + '.log')
        with open(log, 'wb') as f:
            p = subprocess.run(['cmd', '/c', RUN, 'tools/CORPUS/scripts/' + fn] + args,
                               stdout=f, stderr=subprocess.STDOUT, cwd=REPO)
        data = open(log, 'rb').read()
        t = data.decode('utf-8', errors='replace')
        uee = b'UnicodeEncodeError' in data
        ctrl = u'КОНТРОЛЬ G7' in t and u'𝄞' in t
        verdict = verdict_mark in t
        shutil.move(path, os.path.join(HERE, fn))
        rows.append((fn, p.returncode, want, uee, ctrl, verdict))
        print(u'== %-36s код %d (ждали %s)  UnicodeEncodeError=%d  контрольная строка=%d  приговор=%d'
              % (fn, p.returncode, want if kind == 'fixed' else 1, uee, ctrl, verdict))

# следы: __pycache__ от копий
pc = os.path.join(S, '__pycache__')
if os.path.isdir(pc):
    for f in os.listdir(pc):
        if f.startswith('_g7ctrl_'):
            os.remove(os.path.join(pc, f))
left = [f for f in os.listdir(S) if f.startswith('_g7ctrl_')]
assert not left, left

with open(os.path.join(HERE, '00-codes.txt'), 'w', encoding='utf-8') as f:
    f.write(u'| копия | код | ждали | UnicodeEncodeError | контрольная строка цела | строка приговора |\n|---|---|---|---|---|---|\n')
    for fn, rc, want, uee, ctrl, verdict in rows:
        f.write(u'| `%s` | %d | %s | %s | %s | %s |\n' % (
            fn, rc, want if 'fixed' in fn else 1, u'да' if uee else u'нет',
            u'да' if ctrl else u'нет', u'да' if verdict else u'нет'))
