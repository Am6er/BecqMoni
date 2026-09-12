# П30 (S66): сравнение плеч развёртки «Из NucBase» по составу (SPEC-строки) и по отчёту (REPORT).
import io, sys, re
sys.stdout.reconfigure(encoding='utf-8')
def load(path):
    d={}; cur=None
    for line in io.open(path,encoding='utf-8-sig'):
        line=line.rstrip('\n')
        if line.startswith('### '): cur=line[4:]; d[cur]={'spec':[], 'rep':'', 'exit':'', 'lib':''}
        elif line.startswith('SPEC\t'): d[cur]['spec'].append(line)
        elif line.startswith('REPORT\t'): d[cur]['rep']=line
        elif line.startswith('EXIT='): d[cur]['exit']=line[5:]
        elif line.startswith('LIBSIZE='): d[cur]['lib']=line[8:]
    return d
base=load(sys.argv[1])
for armpath in sys.argv[2:]:
    arm=load(armpath)
    print('=== %s против %s ===' % (armpath.split('sweep_s66-')[-1], sys.argv[1].split('sweep_s66-')[-1]))
    print('спектров', len(base), '/', len(arm), '; коды не 0:', sum(1 for v in arm.values() if v['exit'] not in ('0','')))
    changed=[k for k in base if sorted(base[k]['spec'])!=sorted(arm.get(k,{'spec':[]})['spec'])]
    print('состав изменился у', len(changed), 'из', len(base))
    for k in changed:
        print('  ', k)
        print('     было :', ' | '.join(s.replace('SPEC\t','') for s in base[k]['spec']))
        print('     стало:', ' | '.join(s.replace('SPEC\t','') for s in arm[k]['spec']))
    repchanged=[k for k in base if base[k]['rep']!=arm.get(k,{'rep':None})['rep']]
    print('строка REPORT изменилась у', len(repchanged), 'из', len(base), '(из них состав тот же:', len([k for k in repchanged if k not in changed]),')')
    # число принятых родителей
    def npar(v): return len(v['spec']) if v['spec'] and v['spec'][0]!='SPEC\t(пусто)' else 0
    print('Σ принятых родителей: было %d, стало %d' % (sum(npar(v) for v in base.values()), sum(npar(v) for v in arm.values())))
    print()
