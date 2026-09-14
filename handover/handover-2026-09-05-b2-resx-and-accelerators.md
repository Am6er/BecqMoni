# B2 — формат `.resx` сведён к одному виду, сторож ускорителей меню в дереве

**Дата:** 05.09.2026 · **Ветка:** `pie` · **Строки:** `T156`, `T139`
**Артефакты:** `handover/handover-2026-09-05-b2-artefacts.txt` (все выводы, UTF-8,
двенадцать разделов; ниже ссылки вида «§N артефактов»).
**Сборка:** `bin\Debug_B2\` / `obj\B2\`, exit 0.
Коммитов не делалось, `TODO.md`/`DONE.md` не тронуты — текст для реестра в отчёте.

---

## 1. `T156` — формат `.resx`

### 1.1. Посылка строки и реальность

Строка: «`DCFwhmCalibrationView.resx` — LF; `DocEnergySpectrum.resx` — LF и без BOM;
остальные 40 — BOM и CRLF». Пересчёт байтами по ВСЕМУ дереву (§1 артефактов):

| класс | ДО | ПОСЛЕ |
|---|---|---|
| BOM + CRLF (правило) | **60** | **72** |
| BOM + LF | 3 | 2 |
| BOM + смешанные | 4 | 1 |
| без BOM + CRLF | 4 | 0 |
| без BOM + LF | 4 | 0 |
| **всего** | **75** | **75** |

Посылка была ýже реальности втрое: не 2 отщепенца из 42, а **15 из 75, в пяти
видах**. И сам `DCFwhmCalibrationView.resx` был не «LF», а **смешанным**: 3 CRLF на
5118 LF — след одной правки, вписавшей три строки чужим переводом строк.
`AtomSpectraVCPDeviceForm.ru.resx` — зеркальный случай: 156 CRLF и ОДИН LF, и этот
LF сидит **внутри значения** `label2.Text` (многострочная подпись), то есть вписан
рукой в текстовом режиме.

### 1.2. ⚠ Находка про git: перевод строк в репозитории НЕ хранится

`git ls-files --eol '*.resx'` → `i/lf` у **75 из 75** (§2 артефактов). CRLF в
рабочем дереве делает `core.autocrlf=true` из **системного**
`C:/Program Files/Git/etc/gitconfig`; `.gitattributes` в дереве нет вовсе. Следствия:

* правка перевода строк в `git diff` не видна НИКАК — `git diff --stat` после
  нормализации показал **8 файлов по одной строке**, и все восемь — добавленный
  BOM; четыре файла, где менялся только перевод строк, дают дифф **0 байт**
  (§5). «Откатится при коммите» не грозит — в индекс и так уходит LF;
* на машине с `autocrlf=false` (или Linux) свежее дерево будет целиком LF, и
  правило «CRLF» там не выполнимо ничем, кроме `.gitattributes` с
  `*.resx text eol=crlf`. Файл `.gitattributes` не в моих границах — заведено
  строкой реестра (отчёт);
* BOM — часть содержимого, git его хранит; эти восемь правок переживают любую
  машину.

### 1.3. Что сделано

**Нормализованы 12 файлов** байтами (`scratchpad/normalize_resx.py`, лог в §3):
`AtomSpectraVCPDeviceForm.ru`, `DCEnergyCalibrationView`, `DCFwhmCalibrationView`
(+`.ru`), `DocEnergySpectrum` (+`.ru`), `NuclideDefinitionForm` (+`.ru`),
`ObsidianDeviceForm` (+`.ru`), `XPTable/Models/Table`, `XPTable/Models/TableModel`.
Приёмка содержимого — внутри скрипта: байты без BOM и с LF **совпали до/после у
всех 12** (assert), то есть менялись только BOM и `\r`. Многострочных `<value>`
в них 6: пять — base64 иконки `$this.Icon` (пробелы там безразличны), одна —
та самая `label2.Text` в `AtomSpectraVCPDeviceForm.ru.resx`, где `\n` внутри
подписи стал `\r\n` — ровно то, что даёт `autocrlf` при свежем checkout, и что
`Label` рисует одинаково.

**Три файла НЕ тронуты — чужие полосы:** `DCPeakDetectionView.ru.resx`
(смешанный: 159 CRLF / 13 LF), `EfficiencyMakerForm.resx` и `.ru.resx` (LF).
Их нормализация — одна команда тем же скриптом, когда полосы освободятся
(строка реестра в отчёте).

**Сборка** в `bin\Debug_B2` после нормализации: exit 0, ошибок 0,
`obj\B2\BecquerelMonitor.DCFwhmCalibrationView.resources` (287 443 байт) и
`…DocEnergySpectrum.resources` (313 583 байт) на месте, всего 75 `.resources` (§6).

**Плечо формата у сверок.** Новый общий модуль `tools/resx_format.py`
(`describe`, `problems`; правило, счёт и причины — в его шапке). В четыре
`tools/check_resx*.py` добавлены: раздел шапки «Формат файла — плечо сторожа
(`T156`)», `import resx_format`, ключ `--no-format`, и в конце — печать
`ФОРМАТ  <файл>: чем плох` + строка `файлов чужого формата (не BOM+CRLF): N`
+ возврат 1. `check_resx`, `check_resx_letters`, `check_resx_designer` смотрят
ВСЕ `*.resx` под корнем (чтобы называть одно число), `check_resx_zorder` —
только пары `Foo.Designer.cs`/`Foo.resx`, которые читает (у него есть `--form`).
Перевод строк самих скриптов (LF) сохранён, BOM им не добавлен.

На дереве сейчас все четыре сверки по своему предмету дают 0 находок и
возвращают **1 из-за трёх чужих файлов** (§7). Это честно: они и есть
остаток `T156`.

### 1.4. Положительный контроль (§8)

Подброшено дерево `fmt_ctl/BecquerelMonitor/` с `Foo.resx` (LF, без BOM) и
`Foo.ru.resx` (BOM, CRLF), позже — `Foo.Designer.cs` для `zorder`:

* `check_resx.py` → `ФОРМАТ …/Foo.resx: без BOM; переводы строк LF, а не CRLF`,
  `РАЗОШЛОСЬ`, **exit 1**; тот же вызов с `--no-format` → `СОШЛОСЬ`, exit 0;
* `check_resx_letters.py`, `check_resx_designer.py` — та же строка `ФОРМАТ`, `РАЗОШЛОСЬ`;
* `check_resx_zorder.py --root …` → `файлов чужого формата: 1`, **exit 1**.

---

## 2. `T139` — сторож столкновений ускорителей меню

### 2.1. Что написано

`tools/check_menu_accelerators.py` — по методу `handover-2026-09-04-a103.md` §6:
дерево регэкспом из `MainForm.Designer.cs` (только узлы, достижимые от
`menuStrip1`: из 11 `AddRange` в дереве меню 10, одиннадцатый — `statusStrip1`),
подписи из `MainForm.resx` и `MainForm.ru.resx` с фолбэком на английский (как
сателлит), ускоритель — первая буква после одиночного `&` (как
`WindowsFormsUtils.GetMnemonic`), столкновения по `(узел, буква.upper())`
отдельно на язык. `--help`, `--list`, все пути подменяемы ключами.

**Подписи, заданные кодом** — разбор `MainForm.cs` на `X.Text = Resources.Ключ;`
(значение из `Properties/Resources.resx` / `.ru.resx`, отсутствие ключа —
`НЕТ КЛЮЧА`, exit 1) и `X.Text = "литерал";`. Присваивание кодом перекрывает
resx. Отдельно считаются `DropDownItems.Add/Insert/AddRange(` в `MainForm.cs` —
такие пункты разбору недоступны, и сторож на них печатает `НЕДОСТУПНО` и
возвращает 1 (сегодня их 0).

Формат входных `.resx` сторожу безразличен (читает `ElementTree`) — так ему
можно подсовывать файлы из `git show`, которые всегда LF.

### 2.2. Числа на текущем дереве (§9)

```
АНГЛИЙСКИЙ: узлов 10, пунктов 68, с ускорителем 40, столкновений 0
РУССКИЙ:    узлов 10, пунктов 68, с ускорителем 40, столкновений 0
подписей, заданных кодом (MainForm.cs): 1
      showLogToolStripMenuItem <- Resources.MenuShowLog (строка 1718): en 'Show log' / ru 'Показать журнал'
пунктов, добавляемых в меню кодом (разбору недоступны): 0
exit 0
```

`A103` всё починила: столкновений 0 на оба языка. ⚠ Русских ускорителей стало
**40, а не 29**, как в журнале `A103`: все 11 добавлены в том же коммите
`87016183` (в диффе `8d58ec52..HEAD` по `MainForm.ru.resx` 14 строк с `&amp;`:
11 новых + 3 разведённых). Это закрытие побочного счёта `A103` §5, не находка.

### 2.3. Положительные контроли — три, все exit 1

1. **Старые resx** (§10): `git show 8d58ec52:…MainForm.resx` и `.ru.resx`
   (родитель коммита `A103`) через `--resx`/`--ru-resx` — названы все три
   столкновения `A103`, знак в знак с его журналом, и русских ускорителей там
   ровно 29:
   ```
   СТОЛКНОВЕНИЕ viewTToolStripMenuItem [en] : 'L' -> Spectrum List, Language
   СТОЛКНОВЕНИЕ fileFToolStripMenuItem [en] : 'A' -> Save As..., Close All
   СТОЛКНОВЕНИЕ spectrumSToolStripMenuItem [ru] : 'Н' -> Новый, Начало измерения
   ```
2. **Сторож ВИДИТ `showLogToolStripMenuItem`** (§11): подброшен общий resx, где
   `MenuShowLog` = `&About log` / `&О журнале`, — столкновение названо через
   пункт, подписи которого в `MainForm.resx` нет вовсе:
   ```
   СТОЛКНОВЕНИЕ helpHToolStripMenuItem [en] : 'A' -> About log, About...
   СТОЛКНОВЕНИЕ helpHToolStripMenuItem [ru] : 'О' -> О журнале, О BQMoni...
   ```
3. **Ключа нет** (§12): общий resx без `MenuShowLog` →
   `НЕТ КЛЮЧА  …/MainForm.cs:1718  showLogToolStripMenuItem.Text = Resources.MenuShowLog`.

---

## 3. Тронутые файлы

* нормализованы (только BOM/переводы строк): 12 `.resx`, список в §1.3;
* `tools/check_resx.py`, `check_resx_designer.py`, `check_resx_letters.py`,
  `check_resx_zorder.py` — шапка + плечо формата (+92/−6);
* новые: `tools/resx_format.py`, `tools/check_menu_accelerators.py`,
  `handover/handover-2026-09-05-b2-artefacts.txt`, этот журнал.

Не тронуты: `MainForm.*`, `TODO.md`, `DONE.md`, чужие полосы, `.gitattributes`
(не заведён — вне границ, строка в отчёте).

## 4. Чего НЕ сделано

* Три чужих `.resx` не нормализованы (см. §1.3) — сверки на дереве красны ими.
* `.gitattributes` `*.resx text eol=crlf` не заведён — без него правило CRLF
  держится только на системном `autocrlf` этой машины.
* Сторож ускорителей знает одну форму — `MainForm`. Контекстные меню
  (`DocEnergySpectrum.resx` держит `…ToolStripMenuItem.Text` с ускорителями, см.
  `A88`) и меню других форм им штатно не смотрятся; ключи `--designer/--resx/--code`
  позволяют натравить его на другую форму, но `--menu` должен назвать её корень.
  Пробный прогон на `DocEnergySpectrum` с `--menu contextMenuStrip1`: узлов 3,
  пунктов 9, с ускорителем 9, столкновений 0 на оба языка. Четырнадцать
  выпадающих списков кнопок `toolStripSplitButton*` той же формы — отдельные
  корни, каждый надо называть своим `--menu`; не прогонялись.
