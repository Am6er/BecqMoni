# -*- coding: utf-8 -*-
"""
F18: обратный контроль. Откатывает ОДНУ правку в СВОЁМ файле и возвращает её
назад. Ничего, кроме названной подстроки, не трогает: правка ищется дословно и
обязана найтись РОВНО ОДИН раз, иначе отказ — так «откат» не может промахнуться
мимо места и тихо ничего не сделать.

    python control_f18.py <ключ> off | on

ключи:
    a50    — форма перестаёт называть номера формата (говорит прежнюю общую
             строку про «другое поколение переноса»)
    group  — `F0` обратно в `N0`: возвращается группировка разрядов
    thsep  — `ThousandsSeparator` поля историй обратно в `true`: разделитель
             групп снова берётся у КУЛЬТУРЫ ПОТОКА
"""
import io
import sys

FORM = 'BecquerelMonitor/ResponseMatrixForm.cs'
LAYOUT = 'BecquerelMonitor/ResponseMatrixForm.Layout.cs'

EDITS = {
    'a50': (FORM,
            'this.stateLabel.Text = string.Format(CultureInfo.InvariantCulture,\n'
            '                                                         Resources.ResponseMatrixStateOldFormat,\n'
            '                                                         fileFormat, ResponseMatrix.FormatVersion);',
            'this.stateLabel.Text = Resources.ResponseMatrixStateStaleVersions;'),
    'group': (FORM,
              '? "Посчитана {0:F0} историями на узел при штатных {1:F0}; поле поднято до штатного"\n'
              '                : "Computed with {0:F0} histories per node, nominal is {1:F0}; field raised to nominal";',
              '? "Посчитана {0:N0} историями на узел при штатных {1:N0}; поле поднято до штатного"\n'
              '                : "Computed with {0:N0} histories per node, nominal is {1:N0}; field raised to nominal";'),
    'thsep': (LAYOUT,
              '            this.nodesBox.Increment = 10;\n',
              '            this.nodesBox.Increment = 10;\n'
              '            this.historiesBox.ThousandsSeparator = true;\n'),
}


def run(key, mode):
    path, good, broken = EDITS[key]
    src = io.open(path, encoding='utf-8-sig', newline='').read()
    # ⛔ Переводы строк — по БАЙТАМ файла, а не по привычке: все четыре файла
    #    приложения CRLF, и образец с `\n` не нашёлся бы вовсе — «откат»
    #    прошёл бы молча и ничего не откатил (ровно так контроль и слепнет).
    nl = '\r\n' if '\r\n' in src else '\n'
    good = good.replace('\n', nl)
    broken = broken.replace('\n', nl)
    frm, to = (good, broken) if mode == 'off' else (broken, good)
    n = src.count(frm)
    if n != 1:
        print('ОТКАЗ: искомое место встречается %d раз, а должно один раз (%s, %s)'
              % (n, key, mode))
        return 2
    src = src.replace(frm, to)
    # BOM у файла есть — читали utf-8-sig, пишем utf-8-sig, иначе файл поедет.
    io.open(path, 'w', encoding='utf-8-sig', newline='').write(src)
    print('%s %s: сделано (%s)' % (key, mode, path))
    return 0


if __name__ == '__main__':
    sys.exit(run(sys.argv[1], sys.argv[2]))
