# -*- coding: utf-8 -*-
# П111: перенос правок из worktree в главное дерево файлами кода. Перед копированием каждый файл главного дерева обязан быть
# = HEAD (git hash-object == git rev-parse HEAD:путь) — иначе отказ (чужая правка). Журнал перенос_log.txt.
import hashlib, os, shutil, subprocess, sys, io
WT = r'D:\BqMoni_Claude\p111\wt'
MAIN = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
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
]
log = io.open(r'D:\BqMoni_Claude\p111\transfer_log.txt', 'a', encoding='utf-8')
def sha(p):
    return hashlib.sha256(open(p, 'rb').read()).hexdigest()
ok = True
for rel in FILES:
    main_p = os.path.join(MAIN, rel.replace('/', os.sep))
    head_blob = subprocess.check_output(['git', '-C', MAIN, 'rev-parse', 'HEAD:' + rel]).decode().strip()
    main_blob = subprocess.check_output(['git', '-C', MAIN, 'hash-object', main_p]).decode().strip()
    if head_blob != main_blob:
        msg = 'ОТКАЗ: %s в главном дереве != HEAD (чужая правка)' % rel
        print(msg); log.write(msg + '\n'); ok = False
        continue
    src = os.path.join(WT, rel.replace('/', os.sep))
    shutil.copyfile(src, main_p)
    msg = 'перенесён %s  sha256 %s' % (rel, sha(main_p)[:16])
    print(msg); log.write(msg + '\n')
log.close()
sys.exit(0 if ok else 1)
