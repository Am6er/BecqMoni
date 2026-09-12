# П35 (S66): чем именно отличаются 69 строк REPORT базы П30 (якорь вкл) от базы П35 (якоря нет).
# Ожидание: разница ЦЕЛИКОМ объясняется двумя подстановками —
#   «, якорь N.N кэВ» -> «»   и   «неспутываемая линия на месте» -> «доля выше порога»
# (у кандидата с подцепочкой — «доля выше порога У ПОДЦЕПОЧКИ (ряд оборван)»).
import io, re, sys
sys.stdout.reconfigure(encoding='utf-8')

def load(path):
    d = {}; cur = None
    for line in io.open(path, encoding='utf-8-sig'):
        line = line.rstrip('\n')
        if line.startswith('### '): cur = line[4:]; d[cur] = ''
        elif line.startswith('REPORT\t'): d[cur] = line
    return d

old = load(sys.argv[1]); new = load(sys.argv[2])
anchor = re.compile(r', якорь \d+\.\d кэВ')
diff = [k for k in old if old[k] != new[k]]
print('REPORT различаются у', len(diff), 'из', len(old))
unexplained = []
anchors = 0
whys = {}
for k in diff:
    o = old[k]
    anchors += len(anchor.findall(o))
    o = anchor.sub('', o)
    # слово «якорь» уходило вместе с доводом; довод у тех же кандидатов — «неспутываемая линия на месте»
    o2 = o.replace(' — неспутываемая линия на месте', ' — доля выше порога')
    if o2 != new[k]:
        # второй вариант довода — подцепочка
        o3 = o.replace(' — неспутываемая линия на месте', ' — доля выше порога У ПОДЦЕПОЧКИ (ряд оборван)')
        if o3 != new[k]:
            unexplained.append(k)
            continue
        whys['подцепочка'] = whys.get('подцепочка', 0) + 1
    else:
        whys['доля выше порога'] = whys.get('доля выше порога', 0) + 1
print('слов «якорь» снято:', anchors)
print('довод «неспутываемая линия на месте» стал:', whys)
print('НЕ объяснённых двумя подстановками:', len(unexplained))
for k in unexplained:
    print('  ', k)
    print('     было :', old[k])
    print('     стало:', new[k])
