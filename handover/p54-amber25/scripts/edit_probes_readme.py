# -*- coding: utf-8 -*-
"""tools/effmaker/probes/README.md: снятые пробы фита, MakerSaveProbe, GeometryTemplateProbe, FsaStackShot."""
import io
import re

p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\README.md'
raw = open(p, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
t = raw.decode('utf-8-sig')
# файл в основном CRLF (1915) с 20 LF — работаем по строкам, сохраняя концы как есть
nl = '\r\n'


def section(text, heading_start):
    """Границы раздела `## <heading_start>...` до следующего `## `."""
    m = re.search(r'^## ' + re.escape(heading_start) + r'.*?$', text, re.M)
    assert m, heading_start
    start = m.start()
    n = re.search(r'^## ', text[m.end():], re.M)
    end = m.end() + n.start() if n else len(text)
    return start, end


def replace_section(text, heading_start, new_body):
    a, b = section(text, heading_start)
    return text[:a] + new_body + text[b:]


removed = {
    '`ScoutProbe.cs`': ('`SpectrumScout` и выбиратель запасного прибора для фита по спектрам — '
                        'единственным потребителем `SpectrumScout` был `EfficiencyMakerForm.AskFallbackDevice`; '
                        'вместе с фитом сняты и класс, и проба'),
    '`SetProbe.cs`': ('состав выпадающего списка наборов нуклидов вкладки фита (`EfficiencyLibrary.BuildChains`) '
                      'и подсказки к её полям — ни списка, ни полей больше нет'),
    '`PackGeometryProbe.cs`': ('`EfficiencyMakerForm.PackGeometryComplaints` (E6, пачка спектров одной ли съёмки) — '
                               'метод снят вместе с пачкой спектров'),
}
for name, why in removed.items():
    body = ('## %s — СНЯТА 13.09.2026' % name + nl + nl
            + 'Проба удалена решением Amber (`AMBER25`, полоса П54): она мерила %s. ' % why
            + 'Эмпирическое восстановление кривой по спектрам (вкладка «Fit to measured spectra», '
            + '`EfficiencyFitter`) убрано целиком — «Его не должно остаться»; что проба проверяла и как '
            + 'была устроена — в истории git (коммит снятия) и в '
            + '[handover/handover-2026-09-13-p54-amber25-remove-fit.md](../../../handover/handover-2026-09-13-p54-amber25-remove-fit.md).'
            + nl + nl)
    t = replace_section(t, name, body)

# OrderProbe упоминается в шапке как история — оставить; добавить пометку рядом.
old = ('пропадали `BqCoeffProbe` (кривая ушла из ROI-конфигов) и `OrderProbe` вместе с' + nl
       + 'самим харнессом `tools/effmaker` (файловый вход кривой сочли мёртвым по' + nl
       + 'обрезанному поиску). Собираются все `tools\\effmaker\\probes\\*.cs` и' + nl
       + '`tools\\effmaker\\*.cs`.')
assert t.count(old) == 1, 'шапка OrderProbe'
new = ('пропадали `BqCoeffProbe` (кривая ушла из ROI-конфигов) и `OrderProbe` вместе с' + nl
       + 'самим харнессом `tools/effmaker` (файловый вход кривой сочли мёртвым по' + nl
       + 'обрезанному поиску; 13.09.2026 `OrderProbe` и харнесс фита сняты уже нарочно,' + nl
       + '`AMBER25`). Собираются все `tools\\effmaker\\probes\\*.cs` и' + nl
       + '`tools\\effmaker\\*.cs`.')
t = t.replace(old, new)

# MakerSaveProbe: фраза про подгонку
old = ('Проверяется ещё, что кривая конфигурации становится ИСХОДНОЙ при привязке (по' + nl
       + 'ней подгонка берёт абсолютный уровень; поле пустовало всё время, пока кривую' + nl
       + 'выбирали ROI-файлом) и что сама привязка не объявляет конфигурацию изменённой.')
assert t.count(old) == 1, 'MakerSave'
new = ('Проверяется ещё, что кривая конфигурации становится ИСХОДНОЙ при привязке' + nl
       + '(график рисует её пунктиром рядом с посчитанной; до 13.09.2026 по ней ещё и' + nl
       + 'фит брал абсолютный уровень — фит снят, `AMBER25`; поле пустовало всё время,' + nl
       + 'пока кривую выбирали ROI-файлом) и что сама привязка не объявляет конфигурацию' + nl
       + 'изменённой.')
t = t.replace(old, new)

# FsaStackShot: строка вызова и ключи
a, b = section(t, '`FsaStackShot.cs`')
sec = t[a:b]
old_usage_start = sec.index('    fsastackshot --spectrum=X.xml')
old_usage_end = sec.index('`--ceiling` подрезает шкалу')
new_usage = ('    fsastackshot --spectrum=X.xml [--efficiency=Цилиндр] [--out=stack.png]' + nl
             + '                 [--infer | --sample=241AM,44TI,152EU,137CS]' + nl
             + '                 [--no-equilibrium] [--no-matrix] [--lib-dump]' + nl
             + '                 [--set=Ra-226] [--lines=Esc-I] [--select=320..380]' + nl
             + '                 [--no-atomic] [--no-backscatter] [--refit-z=0] [--refit-z-rel=0.1]' + nl
             + '                 [--no-drift] [--no-anchor] [--gain-steps=N] [--offset-steps=N]' + nl
             + '                 [--knots=4] [--huber=3] [--calculating] [--spoil=manager]' + nl
             + '                 [--from=180] [--to=700] [--ceiling=1500] [--width=1400]' + nl
             + '                 [--scale=pow] [--pow=4] [--dump=curves.csv]' + nl + nl
             + '(Список ключей сверен с шапкой пробы 13.09.2026, П54: прежний список здесь' + nl
             + 'не знал `--sample=`, `--scale`, `--select`, `--huber`, `--spoil=manager` и' + nl
             + 'называл снятый `--no-room` — замечание П48.)' + nl + nl
             + '`--sample=` — ОБЪЯВЛЕННЫЙ состав пробы нуклидами `nucid` из базы (как в графе' + nl
             + '`nuclides` манифеста корпуса): образы собирает `FsaSampleLibrary` тем же правилом,' + nl
             + 'что корпусный прогон (`--lib=sample`), поставочная `NuclideDefinition.xml` при' + nl
             + 'этом НЕ ЧИТАЕТСЯ (одиночке менеджера подложен пустой список, гейт в конце —' + nl
             + 'код 12, если список оказался непустым; положительный контроль гейта —' + nl
             + '`--spoil=manager`). Под снимком — полоса с надписью пробы «Состав ОБЪЯВЛЕН …»,' + nl
             + 'потому что переключатель «Источник состава» окна отчёта — состояние приложения' + nl
             + '(решение Amber 13.09.2026 «Надпись от пробы вне графика», `T257`, П48).' + nl + nl
             + '`--scale=lin|pow|log` (`--pow=4`) — вертикальная шкала, ровно кнопка «POW» под' + nl
             + 'графиком (`S88`): человек смотрит на спектр в POW 4, и снимок в линейной шкале' + nl
             + 'показывал почти пустое поле. `--select=<от>..<до>` — выделенная область в кэВ,' + nl
             + 'как её тянут мышью (`A27`). `--huber=` — порог хуберовских проходов решателя,' + nl
             + '`--knots=` — число узлов сплайна, `--no-drift`/`--no-anchor`/`--gain-steps`/' + nl
             + '`--offset-steps` — сетка дрейфа и привязка шкалы (`A36`). `--dump=` — кривые ПО' + nl
             + 'КАНАЛАМ в csv: измерение за вычетом фона, модель, сырой сплайн и по колонке на' + nl
             + 'каждый слой стека (`T103`).' + nl + nl)
sec = sec[:old_usage_start] + new_usage + sec[old_usage_end:]
old = ('`--no-atomic` / `--no-room` / `--no-backscatter` / `--refit-z=` разводят' + nl
       + 'приборные образы и гейты порознь — мерка `S81`: «кто чью полку забирает» иначе' + nl
       + 'не разделить. Состав и подавленные печатаются строками `ROW` и `CUT`.')
assert sec.count(old) == 1, 'no-room'
new = ('`--no-atomic` / `--no-backscatter` / `--refit-z=` / `--refit-z-rel=` разводят' + nl
       + 'приборные образы и гейты порознь — мерка `S81`: «кто чью полку забирает» иначе' + nl
       + 'не разделить (ключа `--no-room` больше нет — «комната» снята). Состав и' + nl
       + 'подавленные печатаются строками `ROW` и `CUT`.')
sec = sec.replace(old, new)
t = t[:a] + sec + t[b:]

# GeometryTemplateProbe — строка рядом с GeometryLayoutProbe (если раздела нет — в конец).
if '`GeometryTemplateProbe.cs`' not in t:
    add = ('## `GeometryTemplateProbe.cs` — свои шаблоны детектора в редакторе геометрий (`AMBER24`, П53)' + nl + nl
           + '    geometrytemplateprobe' + nl + nl
           + 'Ожидание: «ВСЕ СОШЛИСЬ», 90 проверок, код 0, с положительным контролем. Сторожит' + nl
           + '`GeometryTemplateStore` (`config\\GeometryTemplates.xml` по образцу' + nl
           + '`GeometryMaterialStore`, `PathOverride` для проб, `Replace`/`Remove` по имени) и' + nl
           + 'кнопки «Сохранить / Клонировать… / Удалить» под списком `presetCombo`: список' + nl
           + 'помнит выбранный, свои помечены «(свой)», вшитые `GeometryPresets` не правятся,' + nl
           + 'отказы по имени идут через `AppUi.Report`. Детекторная часть шаблона = поля' + nl
           + '`GeometryPresets.Build` (кристалл 6, обвязка 7, вещества 4, ПШПВ). Заведена' + nl
           + '13.09.2026 (П53, коммит `cb1e7cf5`).' + nl + nl)
    t = t.rstrip('\r\n') + nl + nl + add.rstrip('\r\n') + nl

out = t.encode('utf-8')
if bom:
    out = b'\xef\xbb\xbf' + out
open(p, 'wb').write(out)
print('probes README ok')
