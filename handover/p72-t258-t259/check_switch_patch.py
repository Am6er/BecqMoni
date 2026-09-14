# -*- coding: utf-8 -*-
"""П72 (T259): сверка — после `generator_switch_patch.py --apply` на КОПИИ `corpus_def.py`
(`D:\\BqMoni_Claude\\p72\\patch_test\\scripts\\`) записи угля дают те же `chains` и `why`, что
подготовленные строки манифеста `manifest_coal_rn222.csv`; и `build_corpus.sample_lines`/
`calibrate.sample_lines` из копий дают для метки `Rn-222` линии подряда без Pb-210.
"""
import csv, io, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
TEST = r'D:\BqMoni_Claude\p72\patch_test\scripts'


def coal_entries(path):
    src = io.open(path, encoding='utf-8-sig').read()
    m = re.search(r"\n_COAL_DIR = .*?\nCOAL = \[(.*?)\n\]\n", src, re.S)
    ns = {'p': lambda *a: os.path.join(*a)}
    exec("_COAL_DIR = ('G1S', 'x')\nCOAL = [" + m.group(1) + "\n]", ns)
    return ns['COAL']


def main():
    rows = list(csv.DictReader(io.open(os.path.join(HERE, 'manifest_coal_rn222.csv'), encoding='utf-8-sig', newline='')))
    ok = True
    for e, r in zip(coal_entries(os.path.join(TEST, 'corpus_def.py')), rows):
        same = e['key'] == r['key'] and ';'.join(e['chains']) == r['chains'] and e['why'] == r['why']
        print('%-26s chains %-14s why %s' % (e['key'], ';'.join(e['chains']), 'СОШЛОСЬ' if same else 'РАЗОШЛОСЬ'))
        if not same:
            ok = False
            print('  def:', e['why'][:220])
            print('  man:', r['why'][:220])
    # линии подряда через патченные читатели (импорт из копии, chains/chain_labels — из дерева)
    sys.path.insert(0, os.path.join(REPO, 'tools', 'CORPUS', 'scripts'))
    sys.path.insert(0, TEST)
    import calibrate                                              # noqa: E402 (копия)
    lines = calibrate.sample_lines({'chains': ['Rn-222'], 'extra': ''})
    names = sorted(set(n for _, _, n in lines if '(' in n))
    has_pb210 = any('Pb-210' in n for n in names)
    print('calibrate.sample_lines(Rn-222): %d линий, члены: %s; Pb-210 %s' % (
        len(lines), ', '.join(names), 'ЕСТЬ — ОШИБКА' if has_pb210 else 'нет — верно'))
    ok = ok and not has_pb210
    lines_ra = calibrate.sample_lines({'chains': ['Ra-226'], 'extra': ''})
    print('calibrate.sample_lines(Ra-226): %d линий (прежний путь через CHAINS)' % len(lines_ra))
    print('ИТОГ', 'сошлось' if ok else 'РАЗОШЛОСЬ')
    return 0 if ok else 1


if __name__ == '__main__':
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding='utf-8', errors='replace')
        except Exception:
            pass
    sys.exit(main())
