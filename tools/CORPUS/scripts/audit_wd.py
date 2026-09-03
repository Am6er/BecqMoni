# -*- coding: utf-8 -*-
u"""ПРИЁМКА КАТАЛОГА ПРОГОНА: та же проба, но имена сцен переведены в guid (`A84`).

ЗАЧЕМ. Правило §11.3 требует звать приёмку ДВАЖДЫ — по складу
(`corpus/geometries`, имена — ключи сцен) и отдельно по тому каталогу, откуда
читает разбор (`wd_app/config/device/response`, имена — guid). Во втором
каталоге поимённые ключи пробы не срабатывают НИКОГДА: `MatrixAuditProbe`
сопоставляет `Path.GetFileNameWithoutExtension` точным равенством, а там лежит
guid. Молча ломаются оба поимённых ключа:

* `--except=<сцена>:<историй>` — исключение не находит сцену, и матрица,
  посчитанная отдельным прогоном (`RC103_lu_front`, 12 млн), даёт ЛОЖНУЮ
  находку «историй не столько»;
* `--cone-on=<сцены>` — двусторонняя проверка конуса вырождается в «конус
  включён у сцены, которой нет в списке», то есть в обвинение критерия
  `SourceOutsideScene`, который на деле сработал верно.

⛔ Плохо не то, что находки лишние, а ГДЕ они возникают: именно на второй
половине приёмки, той самой, которой верят меньше всего, потому что она
«шумит». Сторож, дающий заведомо ложный отказ на главном рубеже, приучает не
читать собственный отказ (`A80` в третьей форме).

⛔ Соответствие ключ→guid НЕ ПОВТОРЯЕТСЯ здесь, а импортируется из
`mx_swap.py` — из того самого места, которым каталог прогона и наполняется.
Второй разборщик того же соответствия однажды разойдётся с первым молча
(`S37`), и сторож начнёт стеречь не то соответствие, которым перекладывают.

    python tools/CORPUS/scripts/audit_wd.py --probe=<...\\MatrixAuditProbe.exe>
           --dir=<каталог с guid-именами> [--except=КЛЮЧ:N] [--cone-on=A,B] [...]

Все прочие ключи уходят в пробу как есть. Код возврата — её собственный;
2 — ошибка самой обёртки.
"""
import io
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import mx_swap                                            # noqa: E402


def fail(msg):
    sys.stderr.write(msg + u'\n')
    sys.exit(2)


def main():
    args = sys.argv[1:]
    probe = None
    passthrough = []
    renames = []                       # (что перевели, во что) — для печати

    mapping = mx_swap.key_to_guid()
    if not mapping:
        fail(u'⛔ соответствие ключ→guid пусто: mx_swap.key_to_guid() ничего не вернул')

    def to_guid(key):
        u"""Ключ сцены → guid. Неизвестный ключ — ОТКАЗ, а не пропуск."""
        if key in mapping:
            renames.append((key, mapping[key]))
            return mapping[key]
        # guid уже подставлен вручную — пропускаем как есть
        if len(key) == 36 and key.count('-') == 4:
            return key
        fail(u'⛔ сцена «%s» не найдена в соответствии ключ→guid (%d ключей). '
             u'Опечатка в имени либо сцены нет в corpus/geometries/index.csv'
             % (key, len(mapping)))

    # ⛔ ИМЕНА НЕ ПЕРЕВОДЯТСЯ ЗДЕСЬ. Перевод делает сама проба ключом `--map=`
    # (правка `bq-eng-res-net-a9`), и делает его ДО всех проверок и до печати —
    # поэтому и `--except=`, и `--cone-on=`, и отчёт выходят на именах сцен.
    # Обёртка отвечает только за то, чтобы таблица соответствия БЫЛА и была
    # свежей: считает её `mx_swap.key_to_guid()`, ручного шага нет (`A79` про
    # то, чего стоит ручной шаг).
    for a in args:
        if a.startswith('--probe='):
            probe = a[8:]
        elif a.startswith('--map='):
            fail(u'⛔ --map= обёртка делает сама; передавать его снаружи нечего')
        else:
            if a.startswith('--except='):
                body = a[9:]
                if body.count(':') != 1:
                    fail(u'⛔ --except= ждёт <сцена>:<историй>, получено «%s»' % body)
                to_guid(body.split(':')[0])          # ранний отказ на опечатке
            elif a.startswith('--cone-on='):
                for k in a[10:].split(','):
                    if k.strip():
                        to_guid(k.strip())           # то же для списка конуса
            passthrough.append(a)

    if probe is None:
        fail(u'⛔ нужен --probe=<путь к MatrixAuditProbe.exe>')
    if not os.path.isfile(probe):
        fail(u'⛔ пробы нет: %s' % probe)

    # Таблица кладётся рядом с прочими рабочими файлами и переписывается
    # каждый раз: устаревшая карта — это ровно та ошибка, против которой всё
    # затевалось, только с другого конца.
    map_path = os.path.join(HERE, 'wd_key_guid.csv')
    with io.open(map_path, 'w', encoding='utf-8', newline='') as fh:
        fh.write(u'key,guid\n')
        for key in sorted(mapping):
            fh.write(u'%s,%s\n' % (key, mapping[key]))
    passthrough.insert(0, '--map=' + map_path)

    print(u'приёмка каталога прогона, имена разрешаются по карте (A84)')
    print(u'  соответствие ключ→guid: %d ключей, из mx_swap.key_to_guid()' % len(mapping))
    print(u'  карта: %s' % map_path)
    for key, guid in renames:
        print(u'  проверено имя: %-30s = %s' % (key, guid))
    print(u'')
    sys.stdout.flush()

    # ⛔ Вывод переводится ОБРАТНО: находка, названная guid-ом, нечитаема —
    # человек не знает, о какой сцене речь, и идёт сверять руками. Это та же
    # дыра `A84` с другого конца: сторож, чей отчёт нельзя прочесть, стоит
    # ровно столько же, сколько сторож, который молчит.
    back = dict((guid, key) for key, guid in mapping.items())
    proc = subprocess.Popen([probe] + passthrough,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    raw = proc.communicate()[0]
    text = raw.decode('utf-8', 'replace')

    out_lines = []
    for line in text.split(u'\n'):
        # В строке находки («…rmx: чего не хватило») имя стоит перед
        # двоеточием — там добивка пробелами только мешает. В табличной
        # строке имя держит ширину колонки, и без добивки съезжает всё
        # остальное; guid длиннее любого ключа, поэтому добиваем до него.
        finding = u'.rmx:' in line or u'.r:' in line
        for guid, key in back.items():
            if guid in line:
                line = line.replace(guid, key if finding else key.ljust(len(guid)))
        out_lines.append(line)

    sys.stdout.write(u'\n'.join(out_lines))
    sys.stdout.flush()
    return proc.returncode


if __name__ == '__main__':
    sys.exit(main())
