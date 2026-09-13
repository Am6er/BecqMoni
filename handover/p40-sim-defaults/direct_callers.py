# -*- coding: utf-8 -*-
# П40 13.09.2026 — перечень прямых вызовов `new EfficiencySimulator(`: кто ставит три поля физики 16
# сам (поведение прежнее), кто считает отклик (свет по kdip меняет положение всего ниже ~120 кэВ и
# поток случайных чисел каскада), кто читает каналы (SplitXrayShells переклады L-вылет в канал 5).
import io, re, glob, subprocess

files = sorted(glob.glob('tools/effmaker/probes/*.cs') + glob.glob('tools/effmaker/*.cs')
               + glob.glob('BecquerelMonitor/**/*.cs', recursive=True))
tracked = set(subprocess.check_output(['git', 'ls-files']).decode('utf-8').replace('\\', '/').split('\n'))


def strip(t):
    t = re.sub(r'//[^\n]*', '', t)
    return re.sub(r'/\*.*?\*/', '', t, flags=re.S)


rows = []
for f in files:
    t = strip(io.open(f, encoding='utf-8-sig', errors='replace').read())
    if 'new EfficiencySimulator(' not in t:
        continue
    sets = lambda n: bool(re.search(r'(^|[\s,{(.])' + n + r'\s*=[^=]', t, re.M))
    kdip = sets('LightSubKevCurve') or sets('LightCascadeSplit')
    xrkl = sets('SplitXrayShells')
    resp = bool(re.search(r'\.(Response|ResponseByChannel|ResponseRow|Simulate|Run|Efficiency|PeakEfficiency)\s*\(', t)) \
        or 'ResponseMatrixBuilder' in t
    bych = 'ResponseByChannel' in t or 'EscapeXrayL' in t or 'EscapeXrayK' in t
    f = f.replace('\\', '/')
    rows.append((f, kdip, xrkl, resp, bych, f in tracked))

print(u'%-58s %-8s %-8s %-8s %-8s %s' % (u'файл', u'kdip', u'xrkl', u'отклик', u'каналы', u'в git'))
for f, k, x, r, b, tr in rows:
    print(u'%-58s %-8s %-8s %-8s %-8s %s' % (f, u'ставит' if k else u'--', u'ставит' if x else u'--',
                                             u'да' if r else u'--', u'да' if b else u'--', u'да' if tr else u'НЕТ'))
print()
print(u'всего файлов с new EfficiencySimulator(: %d; не ставят половин kdip: %d; не ставят SplitXrayShells: %d; '
      u'из не ставящих kdip считают отклик: %d; из не ставящих xrkl читают каналы: %d'
      % (len(rows), sum(1 for r in rows if not r[1]), sum(1 for r in rows if not r[2]),
         sum(1 for r in rows if not r[1] and r[3]), sum(1 for r in rows if not r[2] and r[4])))
