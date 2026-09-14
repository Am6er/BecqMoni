# BecqMoni
English:

Compiled distr with autoupdate available at https://amba.cloud/becqmoni

Youtube channel for feature view available at https://www.youtube.com/@Am6er

User config files stored at %AppData%\BecqMoni

Russian:

Дистрибутив с инсталлятором доступен на https://amba.cloud/becqmoni

Youtube канал, где можно посмотреть нововведения https://www.youtube.com/@Am6er

Конфигурационные файлы сохраняются в пользовательском профиле %AppData%\BecqMoni

Using [SpecUtils binding for C#](https://github.com/Am6er/SpecUtilsCS)
For original [SpecUtils](https://github.com/sandialabs/SpecUtils) big thanks SandiaLabs!

---

## For developers / Разработчикам

**[TODO.md](TODO.md) is the root of all outstanding work and the register of every
known problem.** Found a problem — anywhere, in anything — add a row there first, then
write the details into the relevant `*.md`. A problem that lives only in a journal, a
comment or a chat message will be lost. `TODO.md` holds tasks only: what, how urgent,
and where the details are.

**[TODO.md](TODO.md) — корень всех доработок и реестр всех известных проблем.**
Нашли проблему — где угодно, в чём угодно — первым делом строку туда, подробности
в соответствующий `*.md`. Проблема, которая живёт только в журнале, комментарии или
переписке, будет потеряна. В `TODO.md` только задачи: что, насколько срочно и где
детали. Закрытое лежит рядом, в [DONE.md](DONE.md), и искать надо в обоих.

Завещания — сквозные, по итогам работы; читать первым делом свежее:

| документ | о чём |
|---|---|
| [handover-2026-08-17-matrix-speed.md](handover/handover-2026-08-17-matrix-speed.md) | модель разрешения в корпусе (перенос и степенная форма), скорость расчёта матриц, клеймо, PerfView |
| [handover-2026-08-14.md](handover/handover-2026-08-14.md) | метрология FSA (пределы), правки поставки, паспортные активности, корпус-70 |
| [handover-2026-08-13.md](handover/handover-2026-08-13.md) | полноспектральный разбор, цена счёта, подписи пиков; что ждёт решения |
| [tools/effmaker/handover-response-matrix.md](tools/effmaker/handover-response-matrix.md) | матрица отклика и всё, что на ней меряно |
| [tools/effmaker/handover-2026-08-05.md](tools/effmaker/handover-2026-08-05.md) | конструктор эффективности, сверка с чужими программами, данные |
| [tools/interspec/handover-2026-08-05.md](tools/interspec/handover-2026-08-05.md) | разбор InterSpec: совпадения, кривые GADRAS, происхождение пиков |

## Среда: систематические грабли / Known environment pitfalls

Каждая строка ниже уже стоила потерянной работы минимум однажды; даты и разборы —
в завещаниях. Проверять ДО того, как наступить.

| где | грабля | что делать |
|---|---|---|
| rtk и обёртки чтения | могут отдать снимок дерева МНОГОЧАСОВОЙ давности: 13.08 `rtk git status`/`git log` и чтение `TODO.md` показали вершину `a1c5e4e` вместо реальной `17e3af4`, закрытые в тот же день задачи выглядели несделанными | начинать сессию с ПРЯМЫХ `git log --oneline -5` и `git status --porcelain` без обёрток и сверять с тем, что показывают инструменты чтения |
| сборка → рабочие каталоги | `tools/CORPUS/scripts/wd_*` держат СВОЮ копию приложения: проба, собранная против свежего кода, исполняет старый exe, и правка «не проявляется» | пересобрал приложение — пересобери каталоги (`mk_appwd.ps1`); свежесть проверять по дате exe в каталоге прогона |
| одиночные csc-сборки | файлы-довески без `Main` (`ResidualScan.cs`, `GadrasDetector.cs`) знает только `build_all.ps1`; `mk_appwd.ps1` собирал пробу одиночным csc и молча сломался при появлении довеска (не собирался с 13.08 по 14.08) | новый довесок вносить в ОБА списка; «у проб нет проекта» — удалив «мёртвый» код, собрать ВСЕ `tools/effmaker/**/*.cs`, обрезанный поиск вызовов уже дважды ломал молча |
| MSBuild | правка файла со старым `LastWriteTime` (копирование, откат) не пересобирается — исполняется прежний код при чистом логе сборки | коснуться файла или собирать `/t:Rebuild`; при сомнении сверить дату сборки с датой правки |
| `sqlite3.connect` | промах по пути МОЛЧА СОЗДАЁТ пустой файл: отказ выглядит как «в базе нет таблицы», а в дереве остаётся файл-мусор (`database/nucdb.sqlite` 13.08, `matdb.sqlite` в корне 16.08 — дважды одно и то же) | открывать ТОЛЬКО через `file:…?mode=ro` с `uri=True` — тогда неверный путь даёт ошибку, а не новый файл; пути брать из таблицы ниже, а не по памяти |
| WebFetch и `pdftotext -layout` | оба портят числовые таблицы: пересказчик выбрасывает строки, `-layout` сдвигает колонки на строку | извлекать двумя способами и сверять с якорем из текста |
| анти-боты (HAL, ScienceDirect) | `Invoke-WebRequest` получает страницу-заглушку/403 вместо PDF — и это видно только по размеру файла | проверять сигнатуру `%PDF` и размер; путь в обход — браузер с JS и `fetch` из контекста страницы (добыча Xu-2022, 14.08) |
| параллельные сессии | два агента в одном дереве: `git add .` уносит чужие правки, номера строк TODO сталкиваются (T27, N17 — дважды за день) | `git add` только поимённо; свободный номер строки — `python tools/check_registry.py`, не глазами |
| конфиг Amber | `%AppData%\BecqMoni` — живой конфиг: читать можно, писать НЕЛЬЗЯ (правки только руками Amber); спектры корпуса read-only | прогонам собирать свой каталог (`mk_appwd.ps1`) — приложение standalone всегда, кроме ClickOnce, и конфиг берёт от рабочего каталога |
| счёт по корпусу | числа только ПО ЧАСТЯМ (понятная/непонятная); `score.py` без ключа `--members` разворачивает цепочки в фантомы (recall 100 % → 46 % на ровном месте, наступлено повторно 14.08); стоимость кода мерить ЦП-временем, не часами | `--part=… --members` всегда; в отчёте называть часть; сравнивать прогоны по `cpu_ms` |

## Где лежат базы данных / Where the databases live

Трижды агент открывал базу не по тому пути и получал пустышку вместо отказа
(`database/nucdb.sqlite` 13.08.2026, `matdb.sqlite` в корне 16.08.2026). Пути —
здесь, и других нет.

| база | о чём | путь-ИСТОЧНИК (в коммите) |
|---|---|---|
| `matdb.sqlite` | вещество и перенос: NIST XCOM, ESTAR/STAR, составы, сечения | `BecquerelMonitor/matdb.sqlite` |
| `schemedb.sqlite` | схемы уровней Geant4 (G4EMLOW, EADL/EPICS/EPDL) | `BecquerelMonitor/schemedb.sqlite` |
| `nucdb.sqlite` | нуклиды, линии, выходы, каскадные совпадения | `BecquerelMonitor/nucdb.sqlite` |

Раскладка таблиц — в [`database/scheme.md`](database/scheme.md); каталог `database/`
держит ОПИСАНИЕ, самих баз в нём нет.

Рядом со сборками и прогонами лежат **копии**, которые кладёт сборка и
`mk_appwd.ps1`; править и читать «на постоянно» их не надо:
`BecquerelMonitor/bin/<Configuration>/`, `tools/effmaker/probes/build/`,
`tools/CORPUS/scripts/wd_*/`.

⛔ **Читать питоном только так** — иначе промах по пути создаёт файл, а не ошибку:

```python
con = sqlite3.connect('file:%s?mode=ro' % path.replace('?', '%3f'), uri=True)
```

⛔ **Писать в базы — только с прямого разрешения Amber на САМУ ЗАПИСЬ** (правило
09.08.2026). Разрешение на метод им не является.