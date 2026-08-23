# -*- coding: utf-8 -*-
u"""Подменить матрицы отклика в каталоге прогона (`T35`).

Зачем. Вопрос `T35` — «как шум матрицы переходит в невязку разбора»: порог 5 %
НАЗНАЧЕН, а не выведен, и пока не измерено, что бывает при его превышении,
резать число историй вдесятеро нельзя. Чтобы это измерить, нужен прогон корпуса
на матрицах, посчитанных ДЕШЕВЛЕ, против той же базы.

⚠ Разбор берёт матрицу НЕ из корпуса. `ResponseMatrixStore` ищет её в
`config\\device\\response` РАБОЧЕГО КАТАЛОГА (`mk_appwd.ps1` их туда и
раскладывает), и имя файла — guid кривой, а не ключ геометрии. Поэтому подмена
делается здесь, а корпус не трогается вовсе.

Соответствие «ключ геометрии → guid» берётся из самих спектров: `index.csv`
называет спектры каждой геометрии, а guid стоит в узле `<Efficiency>` спектра.
Придумывать его иначе нельзя — на этом уже обжигались (`B14`: приёмка смотрела
`geometries/*.rmx`, а разбор берёт `geometries/response/<guid>.rmx`).

    python tools/CORPUS/scripts/mx_swap.py --from=<каталог с key.rmx> --wd=<каталог прогона>
    python tools/CORPUS/scripts/mx_swap.py --from=<каталог с key.rmx> --store

⛔ **`--store` кладёт матрицы В САМ КОРПУС** (`geometries/response/<guid>.rmx`),
откуда их берёт `mk_appwd.ps1` для КАЖДОГО прогона. Ключ заведён 23.08.2026 при
`B20`: до него шага «перенести посчитанные матрицы в склад» не было ВООБЩЕ — он
делался руками, и потому не сделался. Цена этого измерена дважды: 18.08.2026
весь корпус (81 спектр понятной части) считался БЕЗ МАТРИЦЫ, потому что склад
остался от физики 11, а матрицы физики 12 лежали в `wd_p12_geom` под именами
геометрий. Отказ при этом не молчал — `matrix_note` писал «отпечаток НЕ
сошёлся», — но на него натыкались заново, пока шага не появилось.

⚠ `--wd` и `--store` НЕ взаимозаменяемы: первый правит ОДИН прогон и корпуса не
трогает (так измеряют A/B), второй меняет то, с чем поедут все следующие.
"""
import argparse
import csv
import io
import os
import shutil
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.abspath(os.path.join(HERE, os.pardir, 'corpus'))


def key_to_guid():
    u"""{ключ геометрии: guid кривой} по спектрам, на которые она ссылается."""
    out = {}
    path = os.path.join(CORPUS, 'geometries', 'index.csv')
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            key = row['geometry']
            if key in out:
                continue
            spectrum = os.path.join(CORPUS, 'spectra', row['spectrum'] + '.xml')
            if not os.path.isfile(spectrum):
                continue
            rd = ET.parse(spectrum).getroot().find('ResultDataList/ResultData')
            eff = rd.find('Efficiency') if rd is not None else None
            guid = eff.findtext('Guid') if eff is not None else None
            if guid:
                out[key] = guid
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--from', dest='src', required=True, help=u'каталог с <ключ>.rmx')
    ap.add_argument('--wd', help=u'каталог прогона (копия wd_app)')
    ap.add_argument('--store', action='store_true',
                    help=u'класть в СКЛАД КОРПУСА geometries/response, а не в прогон')
    args = ap.parse_args()

    if bool(args.wd) == bool(args.store):
        print(u'⛔ нужен РОВНО ОДИН из --wd и --store: первый правит один прогон,'
              u' второй — склад, с которым поедут все следующие')
        return 2

    if args.store:
        store = os.path.join(CORPUS, 'geometries', 'response')
        if not os.path.isdir(store):
            os.makedirs(store)
    else:
        store = os.path.join(os.path.abspath(args.wd), 'config', 'device', 'response')
        if not os.path.isdir(store):
            print(u'⛔ нет %s — это не каталог прогона' % store)
            return 1

    print(u'куда: %s' % store)

    mapping = key_to_guid()
    print(u'геометрий в index.csv с guid: %d' % len(mapping))

    done, missing = 0, []
    for key, guid in sorted(mapping.items()):
        src = os.path.join(os.path.abspath(args.src), key + '.rmx')
        if not os.path.isfile(src):
            missing.append(key)
            continue
        shutil.copyfile(src, os.path.join(store, guid + '.rmx'))
        done += 1

    print(u'подменено матриц: %d' % done)
    if missing:
        print(u'⚠ не нашлось в источнике: %d' % len(missing))
        for key in missing:
            print(u'   %s' % key)
    return 0


if __name__ == '__main__':
    sys.exit(main())
