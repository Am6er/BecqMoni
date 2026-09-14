# -*- coding: utf-8 -*-
"""П59 (AMBER27): сцены ватного диска Ø40×20 мм на ASN16 — из шаблона ASN16_lu_side.in.

Две стенки (решение Amber 14.09.2026 «Торец И широкая грань») и три плотности ваты
(0.15 — решение «взять 0.15 г/см³»; 0.10 и 0.25 — чувствительность).
Вещество пробы — целлюлоза C6H10O5 (в matdb чистой целлюлозы нет, состав задан явно):
массовые доли H 0.062164, C 0.444462, O 0.493374 (A: H 1.008, C 12.011, O 15.999).
Сосуда нет (стенки 0), зазор 0 — вплотную.

    python handover/p59-amber27/mk_scenes.py <каталог склада полосы>

Пишет <ключ>.in (cp1251, CRLF — как шаблон) и index.csv для CorpusEffProbe.
"""
import io, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
TEMPLATE = os.path.join(REPO, 'tools', 'CORPUS', 'corpus', 'geometries', 'ASN16_lu_side.in')

def set_key(text, key, value):
    pat = re.compile(r'^(' + re.escape(key) + r'\s*=\s*)(.*?)(\r?\n)', re.M)
    m = pat.search(text)
    if not m:
        raise SystemExit('нет ключа ' + key)
    return text[:m.start(2)] + value + text[m.end(2):]

def replace_block(text, start_marker, end_marker, new_block):
    i = text.index(start_marker)
    j = text.index(end_marker, i)
    return text[:i] + new_block + text[j:]

SOURCE_BLOCK = (
    "//Source \r\n"
    "SC_nSourceElements = 3\r\n"
    "SC_RoSource = {rho}\r\n"
    "SC_ZSource[0] = 1\r\n"
    "SC_FractionsSource[0] = 0.062164\r\n"
    "SC_ZSource[1] = 6\r\n"
    "SC_FractionsSource[1] = 0.444462\r\n"
    "SC_ZSource[2] = 8\r\n"
    "SC_FractionsSource[2] = 0.493374\r\n"
    "SC_FractionTypeSource = MASS\r\n"
    "M_SC_Source.MName = Cellulose (cotton)\r\n"
    "M_SC_Source.Nmaterials = 1\r\n"
    "M_SC_Source.Name[0] = Cellulose (cotton)                       \r\n"
    "M_SC_Source.MatRelWeight[0] = 1\r\n"
    "\r\n"
    "\r\n"
)

def scene(template, facing, rho):
    t = template
    t = set_key(t, 'DS_Facing', facing)
    t = set_key(t, 'SC_BeakerToDetectorFrontDistance', '0 cm')
    t = set_key(t, 'SC_BeakerDiameter', '4 cm')
    t = set_key(t, 'SC_BeakerHeight', '2 cm')
    t = set_key(t, 'SC_BeakerSideWallThickness', '0 cm')
    t = set_key(t, 'SC_BeakerEndWallThickness', '0 cm')
    t = set_key(t, 'SC_SourceHeight', '2 cm')
    t = replace_block(t, '//Source \r\nSC_nSourceElements', '// Empty space \r\nSC_nEmptySpace',
                      SOURCE_BLOCK.format(rho=rho))
    return t

def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    template = io.open(TEMPLATE, 'rb').read().decode('cp1251')
    assert 'DS_Facing = SIDE' in template
    rows = ['geometry,spectrum,preset,vessel']
    for facing, tag, human in (('FRONT', 'front', 'ВПРИТЫК к торцу 15×18'),
                               ('SIDE', 'side', 'ВПРИТЫК к широкой грани 18×60')):
        for rho, rtag in (('0.15', ''), ('0.10', '_r010'), ('0.25', '_r025')):
            key = 'ASN16_rn_' + tag + rtag
            text = scene(template, facing, rho)
            with io.open(os.path.join(out, key + '.in'), 'wb') as fh:
                fh.write(text.encode('cp1251'))
            for n in ('1', '2'):
                rows.append('%s,radon%s_%s%s,Atom Spectra Nano 16,"ватный диск Ø40×20 мм ρ %s г/см³ (целлюлоза), %s"'
                            % (key, n, tag, rtag, rho, human))
            print(key)
    with io.open(os.path.join(out, 'index.csv'), 'w', encoding='utf-8', newline='') as fh:
        fh.write('\n'.join(rows) + '\n')

if __name__ == '__main__':
    main()
