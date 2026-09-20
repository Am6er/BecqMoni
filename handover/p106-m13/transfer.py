# -*- coding: utf-8 -*-
# П106: перенос правленых файлов из worktree в главное дерево по sha256 — файл копируется, ТОЛЬКО если в главном дереве он
# равен основе (чистый worktree `wt_base` коммита a7ad8933; `git show` отдаёт LF-форму индекса, рабочее дерево — CRLF).
# Печатает sha до/после. Образец — handover/p100-m13/transfer.py.
import hashlib, io, os, sys, shutil
sys.stdout.reconfigure(encoding='utf-8')
MAIN = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
WT = r'D:\BqMoni_Claude\p106\wt'
BASEWT = r'D:\BqMoni_Claude\p106\wt_base'
FILES = [
    'BecquerelMonitor/EfficiencyMaker/ElectronTransport.cs',
    'BecquerelMonitor/EfficiencyMaker/EfficiencySimulator.cs',
    'BecquerelMonitor/EfficiencyMaker/ResponseMatrix.cs',
    'BecquerelMonitor/EfficiencyMaker/ResponseMatrixBuilder.cs',
    'BecquerelMonitor/EfficiencyMaker/EfficiencyCalculation.cs',
    'tools/effmaker/probes/CorpusMatrixProbe.cs',
    'tools/effmaker/probes/G4RawProbe.cs',
    'tools/effmaker/probes/LayerReturnProbe.cs',
    'tools/check_matrix_keys.py',
    'tools/g4cf/g4cf.cc',
    'tools/effmaker/handover-response-matrix.md',
]

def sha(b):
    return hashlib.sha256(b).hexdigest()

ok = True
for rel in FILES:
    base = open(os.path.join(BASEWT, rel.replace('/', os.sep)), 'rb').read()
    main_path = os.path.join(MAIN, rel.replace('/', os.sep))
    wt_path = os.path.join(WT, rel.replace('/', os.sep))
    cur = open(main_path, 'rb').read()
    new = open(wt_path, 'rb').read()
    if sha(cur) != sha(base):
        print('⛔ %s: в главном дереве НЕ основа (sha %s… против %s…) — не копирую' % (rel, sha(cur)[:12], sha(base)[:12]))
        ok = False
        continue
    if sha(new) == sha(cur):
        print('= %s: без изменений' % rel)
        continue
    shutil.copyfile(wt_path, main_path)
    after = open(main_path, 'rb').read()
    print('✅ %s: %s… → %s… (worktree %s…) %s' % (rel, sha(cur)[:12], sha(after)[:12], sha(new)[:12], 'OK' if sha(after) == sha(new) else 'РАСХОЖДЕНИЕ'))
print('all ok' if ok else 'ЕСТЬ ОТКАЗЫ')
