# -*- coding: utf-8 -*-
u"""Сторож имён веществ: геометрия В СПЕКТРЕ против `corpus/geometries/*.in` (`A269`).

Что сверяет. У каждого корпусного спектра, несущего узел `<Efficiency>` с
геометрией, имена пяти веществ (кристалл, отражатель, обойма, стенка сосуда,
проба) обязаны совпадать с одноимёнными ключами файла `.in` той же сцены.
Плюс отдельный счёт: ни один спектр не должен нести знак замены U+FFFD.

Зачем. Склад матриц считается ИЗ `.in`, а разбор принимает матрицу, сверяя её
клеймо с геометрией ИЗ СПЕКТРА (`matrix.IsValidFor(rd.Efficiency.Geometry)`).
Имя вещества входит в отпечаток, поэтому расхождение в одной букве отнимает
матрицу целиком — молча, без единого сообщения. Ровно это и случилось: с
23.08.2026 в узлах лежало порченое имя пробы (четыре знака замены вместо
`ОИСН-06`), обе стороны портили его одинаково, и клейма сходились. Коммит
`ed398e09` (05.09.2026) починил чтение `.in` — склад стал считаться по верному
имени, узел остался с порченым, и 37 понятных спектров из 82 потеряли матрицу.
Стоило это трёх часов пересчёта склада, потраченных наполовину впустую.

Почему сторож, а не разовая правка: узел `<Efficiency>` приезжает в спектр
ЦЕЛИКОМ ИЗ GIT (шаг 2/4 пересборки, `restore_eff_nodes.py`), то есть заморожен.
Любая правка данных без правки конвейера умирает на первой же пересборке, а
любая новая порча имени вернёт ту же болезнь так же молча.

    python tools/check_geometry_names.py                 # проверить дерево
    python tools/check_geometry_names.py --corpus=X      # проверить другой корпус
    python tools/check_geometry_names.py --selftest      # доказать, что отказывает

Сторож без доказанного отказа ничего не меряет (`T69`), поэтому `--selftest`
складывает временный корпус из одного спектра с НАРОЧНО испорченным именем
вещества и требует кода 1 с именем этого спектра в выводе; тот же спектр в
целости обязан дать код 0.

Коды возврата:
  0 — расхождений нет;
  1 — имя разошлось с `.in`, либо в спектре знак замены, либо не сошёлся
      `--selftest`;
  2 — корпус не найден.
"""

import argparse
import glob
import os
import shutil
import subprocess
import sys
import tempfile

# `T137`: cp1251-консоль не должна ронять печать. Приговор кодом важнее вида.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт
        pass

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = os.path.join(ROOT, 'tools', 'CORPUS', 'scripts')
CORPUS = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus')

sys.path.insert(0, SCRIPTS)

REPLACEMENT = u'�'


def check(corpus):
    u"""Приговор по корпусу. Возвращает код возврата."""
    import geom_names

    spectra = os.path.join(corpus, 'spectra')
    geom_dir = os.path.join(corpus, 'geometries')
    if not os.path.isdir(spectra):
        print(u'ОТКАЗ: нет каталога %s' % spectra)
        return 2

    files = sorted(glob.glob(os.path.join(spectra, '*.xml')))
    if not files:
        print(u'ОТКАЗ: в %s нет ни одного спектра' % spectra)
        return 2

    checked, bad, marks, refusals = 0, [], [], []
    for path in files:
        key = os.path.splitext(os.path.basename(path))[0]
        text = geom_names.read_spectrum(path)
        if REPLACEMENT in text:
            marks.append((key, text.count(REPLACEMENT)))
        node = geom_names.node_of(text)
        if node is None:
            continue
        gname, diffs, refusal = geom_names.compare(node, geom_dir)
        if gname is None:
            continue                     # узел без геометрии — сверять нечего
        if refusal is not None:
            refusals.append((key, refusal))
            continue
        checked += 1
        for tag, have, want in diffs:
            bad.append((key, gname, tag, have, want))

    print(u'спектров всего %d, из них сверено с .in %d' % (len(files), checked))

    if refusals:
        print(u'СВЕРИТЬ НЕ УДАЛОСЬ (%d):' % len(refusals))
        for key, why in refusals:
            print(u'   %-26s %s' % (key, why))

    if bad:
        print(u'ИМЯ ВЕЩЕСТВА РАЗОШЛОСЬ С .in (%d):' % len(bad))
        for key, gname, tag, have, want in bad:
            print(u'   %-26s %-30s %-11s в спектре «%s», в .in «%s»'
                  % (key, gname, tag, have, want))

    if marks:
        print(u'ЗНАК ЗАМЕНЫ U+FFFD В СПЕКТРЕ (%d):' % len(marks))
        for key, count in marks:
            print(u'   %-26s знаков %d' % (key, count))

    if bad or marks or refusals:
        print(u'ОСТАНОВ. Чинится шагом 2/4 пересборки:')
        print(u'   python tools/CORPUS/scripts/restore_eff_nodes.py --apply')
        return 1

    print(u'расхождений нет, знаков замены нет')
    return 0


def selftest():
    u"""Положительный контроль: испорченное имя обязано быть НАЙДЕНО.

    Собирается временный корпус из ОДНОГО спектра, у которого есть геометрия, и
    всех файлов `.in` (они малы). Спектр проверяется дважды: в целости — ждём
    код 0, и с испорченным именем вещества — ждём код 1 и имя спектра в выводе.
    """
    import geom_names

    donor = None
    for path in sorted(glob.glob(os.path.join(CORPUS, 'spectra', '*.xml'))):
        text = geom_names.read_spectrum(path)
        node = geom_names.node_of(text)
        if node is None:
            continue
        gname, _diffs, refusal = geom_names.compare(node, os.path.join(CORPUS, 'geometries'))
        if gname is not None and refusal is None:
            donor = (path, text, node, gname)
            break
    if donor is None:
        print(u'ОТКАЗ САМОПРОВЕРКИ: в корпусе нет ни одного спектра с геометрией')
        return 1
    path, text, node, gname = donor
    key = os.path.splitext(os.path.basename(path))[0]
    print(u'самопроверка на спектре %s (сцена %s)' % (key, gname))

    tmp = tempfile.mkdtemp(prefix='chk_geom_names_')
    try:
        os.makedirs(os.path.join(tmp, 'spectra'))
        os.makedirs(os.path.join(tmp, 'geometries'))
        for src in glob.glob(os.path.join(CORPUS, 'geometries', '*.in')):
            shutil.copy2(src, os.path.join(tmp, 'geometries', os.path.basename(src)))
        clean = os.path.join(tmp, 'spectra', key + '.xml')
        geom_names.write_spectrum(clean, text, geom_names.has_bom(path))

        code_clean, out_clean = run(tmp)
        print(u'   в целости: код %d' % code_clean)
        if code_clean != 0:
            print(out_clean)
            print(u'ОТКАЗ САМОПРОВЕРКИ: целый спектр обязан давать код 0')
            return 1

        # Порча — та же, что была настоящей: имя вещества пробы знаками замены.
        spoiled_node = node
        for tag, ikey in geom_names.slots_of(node):
            import re
            m = re.search(r'(<%s><Name>)([^<]+)(</Name>)' % tag, node)
            if m is None:
                continue
            spoiled_node = node.replace(m.group(0),
                                        m.group(1) + REPLACEMENT * len(m.group(2))
                                        + m.group(3), 1)
            break
        if spoiled_node == node:
            print(u'ОТКАЗ САМОПРОВЕРКИ: в узле не нашлось имени вещества, чтобы испортить')
            return 1
        geom_names.write_spectrum(clean, text.replace(node, spoiled_node, 1),
                                  geom_names.has_bom(path))

        code_bad, out_bad = run(tmp)
        print(u'   с испорченным именем вещества: код %d' % code_bad)
        if code_bad == 0:
            print(out_bad)
            print(u'ОТКАЗ САМОПРОВЕРКИ: порча не найдена, сторож ничего не меряет')
            return 1
        if key not in out_bad:
            print(out_bad)
            print(u'ОТКАЗ САМОПРОВЕРКИ: отказ есть, а спектр %s не назван' % key)
            return 1
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    print(u'самопроверка сошлась: целое дерево 0, порча найдена и названа')
    return 0


def run(corpus):
    u"""Позвать себя же на другом корпусе — отдельным процессом, чтобы код
    возврата был настоящим, а не выведенным из внутреннего вызова."""
    env = dict(os.environ)
    env['PYTHONIOENCODING'] = 'utf-8'
    env['PYTHONUTF8'] = '1'
    proc = subprocess.run([sys.executable, os.path.abspath(__file__),
                           '--corpus=' + corpus],
                          capture_output=True, env=env)
    return proc.returncode, proc.stdout.decode('utf-8', 'replace')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--corpus', default=CORPUS)
    ap.add_argument('--selftest', action='store_true')
    args = ap.parse_args()
    if args.selftest:
        return selftest()
    return check(args.corpus)


if __name__ == '__main__':
    sys.exit(main())
