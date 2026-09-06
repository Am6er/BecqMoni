# -*- coding: utf-8 -*-
u"""СТОРОЖ ПЕРЕКЛАДКИ: склад против каталога прогона, ПО КЛЕЙМУ (`A79`).

ЗАЧЕМ. Склад пишет `corpus/geometries/<ключ>.rmx`, а разбор читает
`…/config/device/response/<guid>.rmx`; между ними стоит `mx_swap.py --store`,
которого никто не зовёт автоматически. Шаг ручной, и он уже дважды не
сделался: 18.08.2026 весь корпус посчитался БЕЗ матрицы (`B14`, `B20`), а
02.09.2026 после 3.5 часов пересчёта прогон едва не взял матрицы прошлых
суток — и отработал бы штатно, молча, на старой физике.

⛔ СВЕРКА ИДЁТ ПО КЛЕЙМУ, А НЕ ПО ВРЕМЕНИ. Время файла говорит «кто-то писал»,
а не «лежит то самое»: ровно на этом 02.09.2026 попался заход, объявивший
пересборку корпуса прошедшей по датам файлов (первый шаг переписал файлы,
остальные не запускались). Клеймо от часов не зависит, ловит «переложили из
чужого прогона» и не пугается копирования, меняющего время.

⛔ ВТОРОГО РАЗБОРЩИКА НЕТ, И ЭТО ГЛАВНОЕ В УСТРОЙСТВЕ. Ни формат `.rmx`, ни
соответствие ключ→guid здесь не разбираются заново: матрицы читает
`MatrixAuditProbe` штатным `ResponseMatrix.Load` и отдаёт клеймо колонкой CSV,
имена разрешает `audit_wd.py` через `mx_swap.key_to_guid()`. Второй разборщик
того же однажды разойдётся с первым молча (`S37`), и сторож начнёт стеречь не
то соответствие, которым перекладывают.

    python tools/CORPUS/scripts/store_vs_wd.py --probe=<...\\MatrixAuditProbe.exe>
           [--store=<corpus/geometries>] [--wd=<...\\config\\device\\response>]

Код возврата 0 — склад и каталог прогона совпали по клейму у всех сцен;
1 — есть расхождения (перечислены поимённо); 2 — ошибка самого сторожа.
"""
import csv
import io
import os
import subprocess
import sys

# T137: cp1251-консоль не роняет печать знаков вне неё (⛔, →, σ): приговор кодом важнее вида.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.abspath(os.path.join(HERE, os.pardir, 'corpus'))
sys.path.insert(0, HERE)


def fail(msg):
    sys.stderr.write(msg + u'\n')
    sys.exit(2)


def audit(probe, directory, tag):
    u"""Прогнать приёмку и вернуть {ключ сцены: строка CSV}."""
    out = os.path.join(HERE, 'store_vs_wd_%s.csv' % tag)

    # ⛔ СТАРУЮ ТАБЛИЦУ СНОСИМ ДО ЗАПУСКА, И ЭТО НЕ ПЕДАНТИЗМ.
    # Поймано 03.09.2026 на себе же: проба из `probes/build` не знала ключа
    # `--map=`, упала с кодом 2 и ничего не написала — а таблица от ПРОШЛОГО
    # запуска (по нарочно испорченной копии) осталась лежать. Сторож прочёл её,
    # увидел 43 матрицы вместо 44 и объявил РАСХОЖДЕНИЕ в исправной оснастке.
    # Работы без отказа, признаки на месте, данные чужие — ровно тот класс,
    # против которого сторож и заведён.
    if os.path.isfile(out):
        os.remove(out)

    cmd = [sys.executable, os.path.join(HERE, 'audit_wd.py'),
           '--probe=' + probe, '--dir=' + directory, '--csv=' + out, '--quiet']
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    text = proc.communicate()[0].decode('utf-8', 'replace')

    # ⛔ Код 1 у приёмки — это ЕЁ находки (шумный узел и прочее), нам они
    # безразличны: мы сверяем происхождение, а не качество. А вот всё
    # остальное — отказ самой приёмки, и путать его с расхождением нельзя:
    # «сверка не сошлась» и «сверку не удалось сделать» требуют разных действий.
    if proc.returncode not in (0, 1):
        sys.stdout.write(text)
        fail(u'⛔ приёмка каталога «%s» ОТКАЗАЛА (код %d) — сверка НЕ ВЫПОЛНЕНА.\n'
             u'   Чаще всего это старая проба: `--map=` появился 03.09.2026, '
             u'и `probes/build` может быть собран раньше.' % (tag, proc.returncode))

    if not os.path.isfile(out):
        sys.stdout.write(text)
        fail(u'⛔ приёмка не оставила таблицы для «%s»: %s' % (tag, directory))

    rows = {}
    with io.open(out, encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            key = os.path.splitext(row['file'])[0]
            rows[key] = row
    if not rows:
        fail(u'⛔ таблица «%s» пуста: %s' % (tag, out))
    return rows


def main():
    probe = None
    store = os.path.join(CORPUS, 'geometries')
    wd = os.path.join(HERE, 'wd_app', 'config', 'device', 'response')

    for a in sys.argv[1:]:
        if a.startswith('--probe='):
            probe = a[8:]
        elif a.startswith('--store='):
            store = a[8:]
        elif a.startswith('--wd='):
            wd = a[5:]
        else:
            fail(u'⛔ неизвестный ключ: %s' % a)

    if probe is None or not os.path.isfile(probe):
        fail(u'⛔ нужен --probe=<путь к MatrixAuditProbe.exe>')
    for d, what in ((store, u'склад'), (wd, u'каталог прогона')):
        if not os.path.isdir(d):
            fail(u'⛔ нет каталога (%s): %s' % (what, d))

    left = audit(probe, store, 'store')
    right = audit(probe, wd, 'wd')

    print(u'сторож перекладки: склад против каталога прогона, ПО КЛЕЙМУ (A79)')
    print(u'  склад:          %s — %d матриц' % (store, len(left)))
    print(u'  каталог прогона: %s — %d матриц' % (wd, len(right)))
    print(u'')

    findings = []
    for key in sorted(left):
        if key not in right:
            findings.append(u'%s: ЕСТЬ В СКЛАДЕ, НЕТ В КАТАЛОГЕ ПРОГОНА — перекладка не дошла' % key)
            continue
        a, b = left[key], right[key]
        if a['stamp'] != b['stamp']:
            # ⚠ Печатать «phys=14 историй=3000000 против phys=14 историй=3000000»
            # бесполезно: читатель видит одинаковое и не понимает, чем клейма
            # разошлись. Разводим два случая — видимое расхождение и
            # НЕВИДИМОЕ, когда всё названное совпало, а клейма разные. Второе
            # и есть подмена матрицей чужого прогона или чужой сцены, ровно то,
            # ради чего сторож судит по клейму, а не по времени.
            same = (a['phys'] == b['phys'] and a['histories'] == b['histories']
                    and a['nodes'] == b['nodes'])
            if same:
                findings.append(
                    u'%s: КЛЕЙМА РАЗНЫЕ ПРИ ОДИНАКОВЫХ phys=%s, историй=%s, узлов=%s — '
                    u'в каталоге прогона лежит матрица ЧУЖОГО прогона или другой сцены'
                    % (key, a['phys'], a['histories'], a['nodes']))
            else:
                findings.append(
                    u'%s: КЛЕЙМА РАЗНЫЕ — в складе phys=%s историй=%s узлов=%s, '
                    u'в прогоне phys=%s историй=%s узлов=%s'
                    % (key, a['phys'], a['histories'], a['nodes'],
                       b['phys'], b['histories'], b['nodes']))
        elif a['cone'] != b['cone']:
            # Клеймо совпало, а конус нет — значит клеймо не покрывает то, что
            # мы считаем важным. Такое стоит увидеть, а не проглотить.
            findings.append(u'%s: клеймо ТО ЖЕ, а конус разный (%s против %s)'
                            % (key, a['cone'], b['cone']))

    for key in sorted(right):
        if key not in left:
            findings.append(u'%s: ЕСТЬ В КАТАЛОГЕ ПРОГОНА, НЕТ В СКЛАДЕ — чужая матрица' % key)

    if findings:
        print(u'НАХОДОК: %d' % len(findings))
        for f in findings:
            print(u'  ' + f)
        print(u'')
        print(u'⛔ Перекладку надо повторить: python tools/CORPUS/scripts/mx_swap.py '
              u'--from=<склад> --store')
        return 1

    print(u'СОШЛОСЬ: клейма совпали у всех %d сцен, каталог прогона держит тот же склад' % len(left))
    return 0


if __name__ == '__main__':
    sys.exit(main())
