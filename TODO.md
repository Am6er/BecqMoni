# TODO — корень всех доработок проекта

**Это список ВСЕХ задач и ВСЕХ известных проблем — единственная точка входа в
недоделанное.**

## 🔨 Нашёл проблему — сразу строку сюда. Без исключений.

Где угодно, в чём угодно, каким угодно способом: попутно в чужом коде, в журнале,
в комментарии, в конфиге, в данных поставки, в чужой области; названную вскользь;
починенную наполовину; и ту, которую решено НЕ чинить — она идёт в таблицу «чего
делать НЕ надо» с причиной.

Проблема, которая живёт только в журнале, только в комментарии, только в переписке
или только в голове, **не существует** и будет потеряна.

Здесь только задачи: что сделать, насколько срочно и где лежат детали.
Подробности, числа и обоснования — в документах по ссылкам, сюда их дублировать
НЕ НАДО. Закончив ревизию — проверить покрытие машинно, а не глазами.

Приоритеты: **P0** — блокер, зависящее делать нельзя · **P1** — ближайшее ·
**P2** — плановое · **P3** — когда дойдут руки.

Закрывая задачу, вычеркните строку здесь и допишите результат в её документ.

---

## P0 — блокеры

| # | задача | детали |
|---|---|---|
| B1 | Реорганизовать корпус: задать геометрии, построить матрицу на каждую, разделить на понятные и непонятные спектры. До этого ВСЕ прогоны по корпусу остановлены | [tools/CORPUS/README.md](tools/CORPUS/README.md), блок «⛔ БЛОКЕР»; [scheme.md](database/scheme.md) §9а C-5 |
| B2 | Выбрать формат снапшота — без него этап 5 рефакторинга не оценить, а от него зависят этапы 2, 7, 8 | [arch/review-arch-notes.md](arch/review-arch-notes.md), §2.4 |
| B3 | Завести конфигурацию «RC-103 (282)» и починить ПШПВ-калибровку RC-103 (только руками Amber) | [tools/effmaker/README.md](tools/effmaker/README.md), «Открытые пункты» |

## P0 — продукт не работает из коробки

Задевает ВСЕХ пользователей прямо сейчас, у всех новых — с первого запуска.
Разбор и точные ссылки на строки — [arch/review-arch-notes.md](arch/review-arch-notes.md),
таблица «Восемь правок».

| # | задача | детали |
|---|---|---|
| **G1** | В поставочном `config/NuclideDefinition.xml` **ноль тегов `Sets`, ноль `IsAnchor`, ноль `Intencity`** — гейт `LibraryPeakFitter.cs:479-486` срабатывает всегда, библиотечный фит не стартует НИКОГДА | [arch/review-arch-notes.md](arch/review-arch-notes.md); [scheme.md](database/scheme.md) §9а F-1 |
| **G2** | `EfficencyROIGuid` не проставлен ни в одном из девяти поставочных device-конфигов — `DocEnergySpectrum.cs:449-460` всегда уходит в fallback `ROIConfigList[0]`, и активность считается по произвольной ЧУЖОЙ кривой | [arch/review-arch-notes.md](arch/review-arch-notes.md) |

## P1 — дефекты продукта

| # | задача | детали |
|---|---|---|
| G3 | `config/ROI/Obsidian Marinelli 0.5.xml:60`: точка `Energy=20, Efficiency=1471.85, ErrorPercent=554` — ε > 1 физически невозможна, сплайн утаскивает левый край | [arch/review-arch-notes.md](arch/review-arch-notes.md) |
| G4 | `SetExporter.BuildRoiConfig` не пишет `BecquerelCoefficient` — любой набор из мастера даёт 0 Бк | [arch/review-arch-notes.md](arch/review-arch-notes.md) |
| G5 | `DoseRateManager.cs:32` — жёсткий каст к `PolynomialEnergyCalibration`; `NonlinearEnergyCalibration` ей сестра, не наследник → `InvalidCastException` | [arch/review-arch-notes.md](arch/review-arch-notes.md) |
| G6 | Доза зависит от галки отображения фона (`DoseRateManager.cs:23-31` ← `MainForm.cs:639-641`) — показание меняется от режима графика | [arch/review-arch-notes.md](arch/review-arch-notes.md) |
| G7 | Тихий нуль K вместо статуса (`ROIConfigForm.cs:951,955` → `MeasurementResultManager.cs:48`) — 0 Бк неотличим от «не посчитано» | [arch/review-arch-notes.md](arch/review-arch-notes.md) |
| G8 | `EnergySpectrumView.cs:2879` — `break` на `OutofChannelException`: одна плохая область гасит отрисовку всех последующих | [arch/review-arch-notes.md](arch/review-arch-notes.md) |
| G9 | Стабильные индексы линий nucdb — закрывает класс поломок при пересборке базы | [arch/review-arch-notes.md](arch/review-arch-notes.md), «Волна 1» |
| G10 | Санитария ссылок: резолвы по имени (`ROIReferenceData`, `SelectROIDialog`) и по индексу комбобокса ломаются чаще Guid | [arch/review-arch-notes.md](arch/review-arch-notes.md), этап 0 |

## P1 — физика модели детектора

| # | задача | детали |
|---|---|---|
| F1 | Каскадное суммирование: разыгрывать распад по схеме уровней, складывать кванты одного распада, сверить CF с `TCCFCALC.dll` | [handover-response-matrix.md](tools/effmaker/handover-response-matrix.md), §10 п. 0б |
| F2 | Малоугловой комптон в ближних слоях — последнее физическое расхождение с ЛСРМ | [tools/tccfcalc/README.md](tools/tccfcalc/README.md), §6 п. 1 |
| F3 | Регрессия маринелли с мелким кристаллом (+25…50 % против ЛСРМ) | [handover-2026-08-05.md](tools/effmaker/handover-2026-08-05.md), §6, строка R |
| F4 | Ввести разрешение в `GeometryModel` — от него зависит допуск поправки на рассеяние | [handover-2026-08-05.md](tools/effmaker/handover-2026-08-05.md), §6, «A1 вскрыл ограничение»; [scheme.md](database/scheme.md) §9а C-4 |
| F5 | Формы ПШПВ GADRAS и `√(A0+A1E+A2/E)` | [interspec/handover-2026-08-05.md](tools/interspec/handover-2026-08-05.md), §10 п. 1 |
| F6 | Доля K-оболочки по энергии, а не константой | [handover-2026-08-05.md](tools/effmaker/handover-2026-08-05.md), §6, строка A3 |
| F7 | Разобрать остаток на 2600 кэВ (канал рождения пар) | [tools/tccfcalc/README.md](tools/tccfcalc/README.md), §6 п. 2 |
| F8 | Проверить L-флуоресценцию тяжёлых кристаллов (LaBr₃, CeBr₃, GSO) | [tools/tccfcalc/README.md](tools/tccfcalc/README.md), §6 п. 5 |

## P1 — полноспектральный разбор

| # | задача | детали |
|---|---|---|
| S1 | Замер по всему корпусу: фантомы и Σχ²/ndf. Блокируется B1 | [handover-response-matrix.md](tools/effmaker/handover-response-matrix.md), §10 п. 2 |
| S2 | Пометка в легенде, каким способом получен образ — с матрицей или без | [handover-response-matrix.md](tools/effmaker/handover-response-matrix.md), §10 п. 4 |
| S3 | Отрисовка каналов отклика отдельными слоями (сделана и откачена; данные готовы) | [handover-response-matrix.md](tools/effmaker/handover-response-matrix.md), §2а |
| S4 | Образы из измеренных эталонов систематически (`--standard`) | [tools/pie/README.md](tools/pie/README.md), «Что дальше» п. 1 |
| S5 | Кривые эффективности для AS80x80 | [tools/pie/README.md](tools/pie/README.md), «Что дальше» п. 2 |
| S6 | Сетка дрейфа шире ±3 кэВ плюс предупреждение о границе | [tools/pie/README.md](tools/pie/README.md), «Что дальше» п. 3 |
| S7 | Совместная обработка группы спектров одного образца | [tools/pie/README.md](tools/pie/README.md), «Что дальше» п. 5 |
| S8 | «Пирог» состава в `DCResultView` | [tools/pie/README.md](tools/pie/README.md), «Что дальше» п. 6 |

## P1 — поиск пиков и опознание

| # | задача | детали |
|---|---|---|
| P1 | Германий даёт ноль пиков и не подаёт признака — восемь спектров корпуса | [interspec/handover-2026-08-05.md](tools/interspec/handover-2026-08-05.md), §10 п. 2 |
| P2 | Заменить гейт значимости z = 4: критерий по отношению правдоподобий вместо теста амплитуды | память `libraryfit-z-gate-blind`, [PR #32](https://github.com/Am6er/BecqMoni/pull/32) |
| P3 | Вето бинарно — нужен другой источник избыточности | память `gate-binary-veto-open` |
| P4 | Ужесточить признак обратного рассеяния | [interspec/handover-2026-08-05.md](tools/interspec/handover-2026-08-05.md), §10 п. 4 |
| P5 | Набор нуклидов под каждый спектр в пробе опознания | [interspec/handover-2026-08-05.md](tools/interspec/handover-2026-08-05.md), §10 п. 5 |

## P1 — открытое в измерениях

Найдено при разборах, но в планы журналов не попадало: утверждение сделано и
ограничено, а проверка ограничения не проведена.

| # | задача | детали |
|---|---|---|
| V1 | «Достаточно одной кривой на модель детектора» проверено только там, где линии набора выше 200 кэВ. Для Am-241 59.5, Ba-133 и рентгена геометрия расходится в разы — **утверждение не проверено** | [tools/CORPUS/README.md](tools/CORPUS/README.md), «Главное: геометрия не нужна» |
| V2 | Модель разрешения корпуса ниже 180 кэВ не измерена, а экстраполирована: на 59.5 кэВ десять спектров ASN16 дают от 3.1 % до 29.4 % полуширины при одном детекторе. Это не смещение, а отсутствие модели | [tools/pie/README.md](tools/pie/README.md), «Низкая точка ПШПВ» |
| V3 | Линии 511 кэВ нет в библиотеке FSA вовсе, хотя пики вылета от 2614 есть; в ториевом спектре она есть всегда | [tools/pie/README.md](tools/pie/README.md), разбор недобора |
| V4 | Превышение около 460 кэВ — происхождение не установлено | [tools/pie/README.md](tools/pie/README.md), разбор недобора |
| V5 | Вылет электрона выключен: прямолинейный CSDA завышает его неизвестно во сколько раз, обратного рассеяния электрона в тяжёлом веществе ESTAR не публикует | [tools/effmaker/README.md](tools/effmaker/README.md), про `--electron` |
| V6 | На RC103, RC101 и OBS библиотечный фит почти не запускается — 45, 20 и 10 предъявленных линий против 475 у ASN16; упирается в базу финдера | [tools/CORPUS/README.md](tools/CORPUS/README.md) |

## P1 — прочее открытое, найденное в журналах

| # | задача | детали |
|---|---|---|
| W1 | Найти, какая из правок вызвала регрессию маринелли — прогоны `out/effsim_all_2026-08-05_{on,off}.txt` есть, виновник не назван | [tools/effmaker/README.md](tools/effmaker/README.md), «Открытый пункт» |
| W2 | Парциальные сечения нужны ещё для девяти элементов кристаллов (I, Cs, Na, Bi, Ge, O…) — страницы NIST `ElemTab` публикуют только сумму | [tools/effmaker/README.md](tools/effmaker/README.md), «Чего не хватает — по пунктам» |
| W3 | Германий: не разобрано, дело в настройках финдера, в форме его ядра на узких пиках или в ПШПВ-калибровке. Связано с P1 | [tools/effmaker/probes/README.md](tools/effmaker/probes/README.md), «Германий выпал целиком» |
| W4 | Ещё четыре группы (CZT_TECD, RC101, RC103g, ASN3) пики находят, но ни одного объяснимого | [tools/effmaker/probes/README.md](tools/effmaker/probes/README.md) |
| W5 | Отношение эманации к суммированию: геометрический вклад не отделён, поэтому «встроенной проверкой равновесия» пользоваться нельзя | [tools/pie/README.md](tools/pie/README.md) |
| W6 | Калибровка выше 4-й степени: `EnrgToChannel` бросала `NotImplementedException`, шкала уезжала на 5.6 кэВ без единого сообщения — проверить, что починено везде | [tools/CORPUS/README.md](tools/CORPUS/README.md) |
| W7 | Голова `ECCBINDX.BIN` (записи по 25 doubles) не расколота — там подоболочечные энергии связи от 13.6 эВ | [database/scheme.md](database/scheme.md), §10 |
| W8 | Записи продолжения ENSDF, комментарии и структурные `S` не разбираются вовсе — там свободный текст | [database/scheme.md](database/scheme.md), §7 |
| W9 | Наборы ENSDF дублируются по нуклиду: выбирать надо по родителю и его периоду, а не по имени дочернего — потребитель этого пока не учитывает | [database/scheme.md](database/scheme.md), §7 |
| W10 | Поставка STAR старше веба: тормозная протона в воде расходится на 1.3 % с сегодняшним PSTAR | [database/scheme.md](database/scheme.md), §5 |
| W11 | Матрица отклика включается сама, выключателя нет — сейчас не мешает (1.37 с против 1.09), но управления нет | [handover-response-matrix.md](tools/effmaker/handover-response-matrix.md), §2 |

## P2 — кривые эффективности

| # | задача | детали |
|---|---|---|
| E1 | Пересчитать эталоны `EffCalcMC` по семи поставочным геометриям | [tools/tccfcalc/README.md](tools/tccfcalc/README.md), §6 п. 3 |
| E2 | Прогон `effsim --all` по корпусу после правок физики. Блокируется B1 | [tools/tccfcalc/README.md](tools/tccfcalc/README.md), §6 п. 4 |
| E3 | Привязать поправку кривой к опорной точке (форма отдельно от уровня) | [interspec/handover-2026-08-05.md](tools/interspec/handover-2026-08-05.md), §10 п. 7 |
| E4 | Сверка с `common_drfs.tsv` — 92 реальных прибора | [interspec/handover-2026-08-05.md](tools/interspec/handover-2026-08-05.md), §10 п. 8 |
| E5 | Расширить пачку RC103 корпуса. Блокируется B3 | [tools/effmaker/README.md](tools/effmaker/README.md), «Открытые пункты» |
| E6 | Проверять геометрию пачки — иначе форма молча усредняет разные геометрии | [tools/effmaker/README.md](tools/effmaker/README.md), «Открытые пункты»; [scheme.md](database/scheme.md) §9а C-6 |
| E7 | Брать площади из полноспектрального разложения, а не из локального фита | [tools/effmaker/README.md](tools/effmaker/README.md), «Открытые пункты» |

## P2 — конструктор ROI и нуклидные сеты

| # | задача | детали |
|---|---|---|
| R1 | Разделить пресет «ЕРН-фон» либо сделать якорь пер-цепочечным | память `roi-wizard-open-items` п. 2, [PR #32](https://github.com/Am6er/BecqMoni/pull/32) |
| R2 | Прогон с `kinds=('G','X')`: пустить рентген распада в рекомендованный состав | память `roi-wizard-open-items` п. 3 |
| R3 | Восстановить модуль `gainscan.py` — без него `calibrate.py` не запускается | память `roi-wizard-open-items` п. 4 |
| R4 | Разметка семейств нуклидов (NORM / MED / IND) — редактор в NucBase | [interspec/handover-2026-08-05.md](tools/interspec/handover-2026-08-05.md), §10 п. 6 |
| R5 | Перенести вертикальные линии интенсивностей в Nuclide Sets | память `efficiency-out-of-roi` |

## P2 — после переезда кривой эффективности

| # | задача | детали |
|---|---|---|
| C1 | Идентификатор родительской цепочки у `NuclideDefinition` | память `efficiency-out-of-roi` |
| C2 | Рефакторинг формы ROI с учётом уехавшей кривой | память `efficiency-out-of-roi` |
| C3 | Рефакторинг Efficiency Maker — дублирует конфиг устройства | память `efficiency-out-of-roi` |

## P1 — данные, которых не хватает расчётам

Полная ревизия — [database/scheme.md](database/scheme.md), **§9а**: что именно
отсутствует, кто в это упирается и где взять. Ниже — только задачи.

| # | задача | детали |
|---|---|---|
| N1 | Втянуть пооболочечные сечения фотоэффекта из `MDATX3` — снимет константную долю K-оболочки (+7 % на 40 кэВ) и откроет L-вылет | [scheme.md](database/scheme.md), §9а A-2 |
| N2 | Завести форм-факторы F(q,Z) и функции инкогерентного рассеяния S(q,Z) — комптон сейчас чистая Клейн — Нишина, когерентное без угла | [scheme.md](database/scheme.md), §9а A-1 |
| N3 | Заменить аппроксимацию ω_K измеренными значениями и завести ω_L | [scheme.md](database/scheme.md), §9а A-4 |
| N4 | Считать пробег и выход тормозного для произвольного состава (проба, оправа, стенка), а не только для десяти вшитых | [scheme.md](database/scheme.md), §9а B-1 |
| N5 | Угловые корреляции γ-γ — без них каскадные совпадения считаются изотропными | [scheme.md](database/scheme.md), §9а D-1 |
| N6 | Втянуть `common_drfs.tsv` — 92 прибора GADRAS как внешняя мера кривых и форм ПШПВ | [scheme.md](database/scheme.md), §9а E-2 |
| N7 | Решить, чьи плотности веществ брать: наши или NIST | [scheme.md](database/scheme.md), §9а C-2 |
| **N8** | **Проставить выходы линий в поставочном `config/NuclideDefinition.xml` из базы — сейчас поля `Intencity` там нет вовсе, и FSA работает на вшитом десятке нуклидов вместо 4377** | [scheme.md](database/scheme.md), §9а F-1 |
| N9 | Читать ESTAR/STAR из базы вместо вшитой таблицы `ElectronData` на 10 веществ — пять таблиц лежат без читателя (из 23 таблиц базы читаются 5) | [scheme.md](database/scheme.md), §9а F-2, F-3 |
| N10 | Перевести `AttenuationData.AtomicMass` на `xcom_elements` — вшитый массив был источником обеих опечаток | [scheme.md](database/scheme.md), §9а F-4 |

## P3 — база нуклидов

| # | задача | детали |
|---|---|---|
| D1 | Правило интерполяции каналов XCOM (расхождение до 13.7 %) | [database/scheme.md](database/scheme.md), §9а A-6 |
| D2 | Края поглощения ниже 1 кэВ и подоболочки выше N5 | [database/scheme.md](database/scheme.md), §9а A-3 |
| D3 | Пооболочечные коэффициенты конверсии из записей `S G` | [database/scheme.md](database/scheme.md), §9а D-4 |
| D4 | Установить год и импортёр ядерной части | [database/scheme.md](database/scheme.md), §9а D-7 |
| D5 | Сверить `ensdf_gammas.intensity` с `decay_radiations` | [database/scheme.md](database/scheme.md), §9а D-8 |
| D6 | Расшифровать `dec_type` | [database/scheme.md](database/scheme.md), §9а D-6 |
| D7 | Втянуть `xray_widths.xml` — закрывает часть D2 | [database/scheme.md](database/scheme.md), §9а E-1 |
| D8 | Добрать `ADOPTED LEVELS` и привязку 17 276 гамма к конечному уровню | [scheme.md](database/scheme.md), §9а D-2, D-3 |
| D9 | Дополнить ICC: Z = 4, 5, 7…13, оболочки выше M5, полный коэффициент | [scheme.md](database/scheme.md), §9а D-5 |
| D10 | Пересобрать совпадения с меньшей отсечкой — сейчас отброшено 196 177 пар | [scheme.md](database/scheme.md), §9а E-3 |
| D11 | Привязать 418 изомеров-родителей совпадений к нашей нумерации уровней — сейчас ищутся только по `sandia_symbol` | [scheme.md](database/scheme.md), §9а D-9 |

## P3 — приближения в модели переноса

Каждое из них — формула вместо данных. Ни одно не измерено по отдельности:
неизвестно, что они дают вместе и где перевешивают.

| # | задача | детали |
|---|---|---|
| M1 | Завести выходы и энергии оже-электронов — сейчас вся неизлучённая энергия кладётся «на электроны» одним куском | [scheme.md](database/scheme.md), §9а A-5 |
| M2 | Направление вылета электрона взято изотропным, хотя он летит вперёд по кванту | [scheme.md](database/scheme.md), §9а B-2 |
| M3 | Спектр тормозного — толстомишенное приближение `dN/dk = C/k`, квант испускается в точке рождения электрона, а не вдоль пути | [scheme.md](database/scheme.md), §9а B-3 |
| M4 | K-флуоресценция покрывает 71 элемент из 100 — у остальных нет K-края в сетке XCOM | [scheme.md](database/scheme.md), §9а A-7 |
| M5 | Измерить насыпные плотности проб (SiO₂ 1.6, CaCO₃ 1.5) — сейчас приняты, а не измерены | [scheme.md](database/scheme.md), §9а C-3 |
| M6 | Померить, что дают все приближения переноса ВМЕСТЕ — лестницей абляций, как делали для сверки с ЛСРМ | [tools/tccfcalc/README.md](tools/tccfcalc/README.md), §3.2 |

## P3 — инструментарий и гигиена

| # | задача | детали |
|---|---|---|
| T1 | Сохранять найденные пики в файл спектра (`DetectedPeaks` помечен `[XmlIgnore]`, разбор невоспроизводим). **Отложено Amber 06.08.2026** | — |
| T2 | Решить, где держать правила «для всех»: `CLAUDE.md` в `.gitignore` и другим не виден | — |
| T3 | Завести проект для проб `tools/effmaker/probes` — сейчас собираются вручную, поломка молчит | [tools/effmaker/probes/README.md](tools/effmaker/probes/README.md) |
| T4 | Обновить шапку `EfficiencySimulator`: она всё ещё утверждает, что вылет K-рентгена не моделируется и когерентное не выделено — оба сделаны | — |

## Отложенные прогоны

| # | задача | детали |
|---|---|---|
| X1 | Полная проверка источника-параллелепипеда по корпусу и поставочным геометриям | [handover-2026-08-05.md](tools/effmaker/handover-2026-08-05.md), «Отложенные прогоны» |
| X2 | Влияние правок физики на сверку с измерениями (корпус, заводские коэффициенты) | [handover-2026-08-05.md](tools/effmaker/handover-2026-08-05.md), «Отложенные прогоны» |

---

## Чего делать НЕ надо

| что | почему | детали |
|---|---|---|
| Германий в любом виде | модель не разбирает коаксиальную ветвь, собирает сплошной цилиндр | [tools/tccfcalc/README.md](tools/tccfcalc/README.md), §6 п. 6 |
| Возвращаться в поставки STAR | взято всё, проверено файл за файлом | [handover-2026-08-05.md](tools/effmaker/handover-2026-08-05.md), «Чего делать НЕ надо» |
| Брать у InterSpec `em_xs_data`, составы, SpecUtils | беднее нашего либо уже есть | [tools/interspec/README.md](tools/interspec/README.md) |
| Обратная совместимость форматов | релизов не было | память `geometry-in-millimeters` |
| Искать I для LaBr₃, CeBr₃, SrI₂, CZT, GSO, KCl | в `FCOMP` их нет, правило Брэгга из `estar_element_potential` — приемлемая замена, сверена на элементарных веществах | [scheme.md](database/scheme.md), §9а C-1 |
| Искать потребителя для `thermal_cross_sect` и `cumulative_fission` | это запас, а не дыра — 6828 строк лежат сознательно | [scheme.md](database/scheme.md), §9а F-5 |
