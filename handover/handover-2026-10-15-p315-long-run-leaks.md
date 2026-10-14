# П315 (ночь 14→15.10.2026): утечки ресурсов при долгой работе — замером

Полоса распорядителя ночи без Amber. Задача: человек оставляет набор на часы и сутки, окно
перерисовывается по такту; утечка объектов GDI/USER (предел процесса — 10 000 каждого) или памяти
кончается падением с потерей набора. Искать в отрисовке, коллекциях, подписках, таймерах;
доказательство — замером с положительным контролем.

## Чем мерено

Проба `tools/effmaker/probes/P315GdiLeakProbe.cs` (новая). Счётчики процесса после трёх полных
сборок и финализаторов: `GetGuiResources` GDI и USER, `Process.HandleCount` (ядро), управляемая
память, частная память, число потоков. Рост судится по ВТОРОЙ половине прогона: кэш, наполненный в
начале, встаёт на полку, утечка растёт дальше тем же темпом. Пороги — в шапке `Judge`.

Режимы:

* `paint` — настоящий `DocEnergySpectrum` в скрытом окне; кадр — путь такта графика
  `MainForm.RefreshDocumentChart` (RecalcChartParameters, PrepareViewData, RecalcScrollBar,
  Invalidate) и отрисовка вида; девять сцен по кругу (линейная/степенная/логарифмическая шкала,
  линии/столбики, каналы/энергия, отсчёты/cps, фон, вычитание, континуум, SMA/WMA, курсор с
  панелью и выделение, подсветка пиков, слои FSA); между сценами спектр «набирается» (300
  отсчётов на кадр). ⚠ Окно за краем экрана WM_PAINT НЕ получает — мерено: 0 кадров из 180
  дошли до `OnPaint` (признак — `scrollBaseY`, который `DrawChart` пишет первым делом). Поэтому
  кадр рисуется через WM_PRINT (`DrawToBitmap`) — тот же `OnPaintBackground` + `OnPaint` вида;
  признак «дошло» — 2160 из 2160.
* `tick` — настоящий `MainForm(string[])` со всеми окнами-панелями, скрытно; документ открыт его
  же `OpenExistingDocument`; набор изображён (Recording, время, отсчёты); кадр — настоящий
  `MainForm.OnTimer` (100 мс) и разбор очереди окна.
* `docs` — тот же `MainForm`: открыть документ, 30 тактов, закрыть — N раз; живые после сборки
  документы и виды (`WeakReference`).
* `layout` — смена раскладки пунктами меню N раз: живы ли прежние окна-панели и кто их держит.
* развилки: `session` (только сеанс FSA, без окон), `bginvoke` (только `BeginInvoke` из пула),
  `plain` (простая форма без кода приложения — контроль среды).

Кто держит — обход графа объектов в ширину от статических полей всех типов приложения, WinForms,
DockPanelSuite, System, System.Drawing, mscorlib (и от `MainForm`) по полям, массивам и целям
делегатов (`PathTo`); что растёт — перепись достижимого по типам в конце двух прогонов разной
длины (`--census-out` / `--census-base`; посреди прогона нельзя — чтение статических полей
запускает статические конструкторы).

Положительный контроль (`--sabotage=`): `gdi` — `CreatePen` без `DeleteObject`, `user` —
`CreateMenu` без `DestroyMenu`, `managed` — 4 КБ в статический список, `docref` — документ в
статический список; `pen` — `new Pen` без `Dispose` (объект GDI+, не GDI: ждём «роста нет»).

Спектр — `tools/CORPUS/corpus/spectra/AS80_Th232WT20.xml` (8192 канала, с фоном), копия в
`D:\BqMoni_Claude\p315\work` (такт с автосохранением пишет файл документа).

## Находки

### 1. Отрисовка графика по такту — утечки НЕТ (замер)

`--mode=paint`, 8 сцен без FSA, 1920 кадров (+160 прогрева), `D:\BqMoni_Claude\p315\paint_nofsa.txt`:
GDI 45 → 45 (0 за прогон), USER 47 → 50 (+3 в первом круге, дальше 0), HND +41 в первом круге
(потоки `Parallel.For` подготовки данных: 45 → 52), дальше 0; управляемая +6 КБ (кэши первого
круга), дальше +0.4 КБ на 1700 кадров. Рост — только в первом круге и встаёт на полку.

Со сценой FSA (9 сцен, 1980 кадров, `paint_before.txt`): GDI 0, но USER +9 и HND +16 за прогон —
ровно по одному на круг. Развилка ниже (п. 2): это не отрисовка, а потоки пула.

Отрисовка FSA при НЕИЗМЕННОМ разборе (смена режима Visible → ShowFSA каждый круг, 30 кругов, 600
кадров, `paint_fsa_norecalc.txt`): GDI 0, USER 0, HND 0, управляемая +2 КБ — чисто.

Перья/кисти без `Dispose` в отрисовке (`ShowDetectedPeaks`, `ShowCalibrationPeaks` — `Dispose` в конце
без `try`; `XPTable` `Table.OnPaintEmptyTableText` — `StringFormat` без `Dispose`; `DragDropRenderer`
— перо на каждый кадр перетаскивания): это объекты GDI+, а НЕ GDI — счётчик GDI процесса их не
видит, освобождает финализатор. Контроль `--sabotage=pen` (перо без `Dispose` на каждом кадре) —
см. «Контроль».

### 2. USER и HND на пересчёт FSA — НЕ утечка: полка числа потоков пула (замер)

Развилка по шагам (каждый — 30 пересчётов, `D:\BqMoni_Claude\p315\`):

| прогон | USER за 30 | HND за 30 |
|---|---|---|
| сцена FSA с пересчётом каждый круг (`paint_fsa.txt`) | +19 | +37 |
| то же, подписчики сеанса сняты оба (`paint_fsa_unsub_both.txt`) | 0 | +2 |
| снят только документ / только вид | +13 / +12 | +33 / +46 |
| только сеанс FSA, без окон (`session.txt`) | 0 | −2 |
| сеанс + подписчик-двойник: `BeginInvoke(пусто)` в скрытое окно (`session_invoke.txt`) | +19 | +38 |
| `BeginInvoke` из пула без FSA, 3000 вызовов (`bginvoke.txt`) | +1 | +8 |

Рост — от того, что подписчик зовёт `Control.BeginInvoke` (то есть `PostMessage`) С ПОТОКА СЧЁТА:
поток пула, впервые позвавший user32, становится GUI-потоком (+1 объект USER и +2 дескриптора на
поток). Число различных потоков, позвавших подписчика, совпадает с ростом USER ровно (17 потоков после
базы — +17 USER, `session_invoke2.txt`). Счёт FSA гоняет `Parallel.For`, и пересчёт кончается то на
одном потоке пула, то на другом — пока все потоки пула не перебраны. Длинный прогон, 180 пересчётов
(`session_invoke_long.txt`): потоков-вызывателей 21 → 27 → 32 → 39 → 39 → 39, USER +18 → +24 → +29 →
+37 → +36 → +36 — ПОЛКА: последние 60 пересчётов роста нет. Предел — число потоков пула (~40–50 на
этой машине), а не время набора. Чинить нечего.

### 3. ⛔ Закрытый документ жил до конца программы — ПОЧИНЕНО (`MainForm.cs`)

`--mode=docs` (настоящий `MainForm`, `OpenExistingDocument` → 30 тактов `OnTimer` → `CloseDocument`),
10 циклов, `docs_before.txt`: живы после сборки **10 из 10** закрытых документов (и их видов),
на документ **+1.34 МБ** управляемой памяти, **+4 GDI**, **+2 USER**. Путь удержания (обход графа
объектов от статических корней, `PathTo` пробы):

`MainForm.dockPanel1 → DockPanel.m_dockPanelTheme → VS2015BlueTheme._stripBefore → [ContextMenuStrip]
→ ContextMenuStrip.events → CancelEventHandler._target = DocEnergySpectrum`

Тема DockPanelSuite (`ThemeBase.ApplyTo(ToolStrip)`, зовёт `DockContent.IContextMenuStripHost.
ApplyTheme` для `Form.ContextMenuStrip` каждого окна панели) запоминает каждое крашеное меню в
словаре `_stripBefore` (прежний отрисовщик — для `CleanUp`) и никогда не отпускает. Меню документа
держит обработчики `Opening`/`Closed` и пункты, а через них — документ: вид, спектр, фон, сеанс FSA.
Человек, открывающий за день десятки спектров, копил их все. Открытого API «забыть одно меню» у темы
нет (`CleanUp` снимает тему со всех окон).

Правка: `MainForm.InitializeDockPanelTheme` подписывает `dockPanel1.ContentRemoved` (ровно одна
подписка — метод зовут оба конструктора и `Load`); обработчик `ForgetDisposedThemedStrips` убирает из
`_stripBefore` отражением меню, которые УЖЕ РАЗОБРАНЫ (`ToolStrip.IsDisposed`): закрытое окно
разбирает своё меню раньше, чем DockPanelSuite убирает окно из панели
(`Form_Disposed → DockContentHandler.Dispose → DockPanel.RemoveContent → ContentRemoved`). Поля нет
(другая версия библиотеки) — ничего не делается. Пиксели не меняются: живые меню тема красит как
прежде.

### 4. ⛔ Смена раскладки держала прежние панели — ПОЧИНЕНО (`DCControlPanel.cs`, то же `MainForm.cs`)

`--mode=layout` (пункты меню «User layout»/«Expert layout» — `MainForm.InitializeToolViews` ставит
новые окна-панели), `layout_before.txt`: после 8 смен живы **все 8** прежних `DCControlPanel` и **все
8** прежних «Peak detection» (`DCPeakDetectionView`); на смену **+12 GDI**, **+4 USER**, +2…4 МБ.
Пути:

* `DeviceConfigManager::instance → DeviceConfigListChanged → DCControlPanel` — панель подписывалась на
  одиночку в конструкторе и не отписывалась; на событие (сохранение конфигурации прибора) звался
  обработчик каждой закрытой панели (броска при замере нет: при пустом активном документе обработчик
  выходит сразу). Правка: `DCControlPanel` отписывается в своём `Disposed`.
* `VS2015BlueTheme._stripBefore → ContextMenuStrip → DCPeakDetectionView` — то же, что п. 3; чинит та
  же чистка темы (`DCPeakDetectionView.cs` не тронут).

### 5. Последний закрытый документ держало окно отчёта FSA — ПОЧИНЕНО (`FSAReportView.cs`)

После правки п. 3 (с заслоном доступности, см. п. 6) из 10 закрытых жив один — последний; путь
`MainForm.dcFsaReportView → FSAReportView.pushedView → EnergySpectrumView.ActionEvent → документ`.
Поле (П290) нужно только для сравнения «тот же ли вид»; сильная ссылка держала закрытый документ до
следующего щелчка в отчёте. Правка: `WeakReference<EnergySpectrumView>`; сравнение — через
`TryGetTarget`, поведение то же (собранный вид не равен живому).

### 6. Внешний клиент доступности держит любое опрошенное окно — свойство СРЕДЫ, не приложения (замер)

После правки п. 3 документы всё ещё жили (10 из 10, `docs_after.txt`), но пути от статических корней
не было (обход: приложение, WinForms, DockPanel, System, System.Drawing, mscorlib — граф пройден
целиком). Контроль среды `--mode=plain` — простая форма БЕЗ кода приложения (кнопка, поле, панель
инструментов, контекстное меню), 6 раз показать и закрыть: живы **6 из 6** / **4 из 6** (`plain.txt`),
пути тоже нет; окно опрошено **67** раз сообщением `WM_GETOBJECT` (`QuietHost.WndProc`). На машине
работает клиент MSAA/UIA (инструменты экрана сессии), он берёт COM-ссылку на `AccessibleObject`
каждого нового окна и держит её — корень, которого обход полей не видит. Заслон
`--experiment=noacc` (внешний подкласс `NativeWindow`, на `WM_GETOBJECT` отвечает 0; ставится до
разбора очереди): простые формы — жив **1 из 6** (последняя), отклонено 42 запроса
(`plain_noacc.txt`). Все замеры «живых» в `docs`/`layout` ниже — с заслоном, и правка проверена
положительным контролем `nofix` (снимает обе правки отражением в живом приложении).

У человека за экраном такой клиент — экранный диктор, лупа, средства ввода, программы
автоматизации — устроит то же самое и без правок П315; это за пределами приложения.

### 7. ⛔ Кэш квадратуры Клейна — Нишины мощности дозы растёт без предела — НЕ МОЙ ФАЙЛ (`DoseRate.cs`)

`--mode=tick` (настоящий `MainForm.OnTimer`, набор изображён), 12 000 тактов = 20 мин набора
(`tick_cache.txt`): GDI 158 → 158, USER +4 и полка, HND −1, но управляемая память **+8.25 МБ**
и во второй половине тем же темпом (**≈ 700 Б на такт** ≈ 25 МБ в час ≈ 0.6 ГБ в сутки набора).
Перепись достижимого (`--census-out/--census-base`, разность прогонов 600 и 6600 тактов) назвала
источник: `Dictionary<double, double[]>` и +56 976 массивов `double[2]` —
`DoseRateCoefficients.comptonCache` (`BecquerelMonitor/DoseRate.cs:1134`, пишется в
`KleinNishina`, `DoseRate.cs:1078–1081`). Ключ — энергия кэВ; мощность дозы (`MainForm.ShowDoseRate`,
раз в 200 мс при наборе) спрашивает `DoseRatePerFluenceRate` на ПРЕДСТАВИТЕЛЬНЫХ энергиях диапазонов
(`DoseRateManager.cs:1918, 2282`, `RepresentativeKev`), которые плывут вместе со спектром, — каждый
вызов кладёт новые ключи: записей 28 530 → 47 523 → 66 519 → 85 509 → 104 505 → 123 496 через каждые
2000 тактов (≈ 9.5 на такт, ≈ 95 в секунду набора). Предела нет; словарь растёт удвоением (большие
массивы).

Положительный контроль правки без правки чужого файла — `--experiment=capcache` (кэш очищается при
4096 записях, отражением после такта): та же сцена, управляемая память **+18 КБ за 20 мин** (полка,
`tick_capcache.txt`), остальное без изменений. Источник роста при наборе один — этот кэш.

Предлагаемая правка (одно поведение: кэш — запоминание детерминированной функции, числа побитово те
же; квадратура 4096 шагов стоит десятки микросекунд, ~20 вызовов на 200 мс — без кэша < 1 мс):

```csharp
        // в KleinNishina, вместо записи без предела
        lock (comptonCache)
        {
            if (comptonCache.Count >= ComptonCacheLimit)
            {
                comptonCache.Clear();
            }
            comptonCache[energyKev] = new double[] { sigma, sigmaTransfer };
        }
        ...
        const int ComptonCacheLimit = 4096;
```

### 8. Падение такта в XPTable — свойство ПРОБЫ (контекст синхронизации), не приложения

Первые прогоны `tick` падали `NullReferenceException` в `XPTable.Models.Table.RowRect` /
`Row.get_Height` из `DCPeakDetectionView.RefreshTable` (такт 5653). Сторож потока на `RowAdded`
таблицы «Peak detection»: **4006** строк добавлено НЕ с потока окон; стек —
`DCPeakDetectionView.UpdatePeakDetectionResult` (продолжение `await Task.Run`). Причина в пробе: без
`Application.Run` каждый `Application.DoEvents` снимает `WindowsFormsSynchronizationContext`, и
продолжения `await` уходят в пул. В приложении цикл сообщений держит контекст. Проба ставит контекст
сама (`AutoInstall = false` + свой `WindowsFormsSynchronizationContext`): строк с чужого потока **0**,
падений нет (`tick_cache.txt`, `tick_capcache.txt`). ⚠ Тот же изъян пробы объясняет единичный отказ
«Dispose() cannot be called while doing CreateHandle()» в раннем прогоне `docs` — после правки
контекста не повторялся. Другим пробам, гоняющим `DoEvents` с `async`-кодом приложения, —
то же предупреждение.

### 9. Проверено — утечки нет (замером или по устройству, с доводом)

* **Такт набора без мощности дозы** — `--mode=tick`, 12 000 тактов (20 мин набора), с предельным
  кэшем дозы (`tick_capcache.txt`): GDI 158 → 158, USER +6 (полка с первых 2000 тактов — потоки
  пула, п. 2), HND +1, управляемая +18 КБ, частная +72 КБ — роста нет. Путь такта: `OnTimer` →
  `ShowPeakDetectionResult`, `UpdateCalibrationPeak`, `ShowCountsRate`, `ShowDetectorFeature`,
  `ShowMeasurementResult` (окно результата и FSA), `ShowDoseRate`, `RefreshDocumentChart`,
  `ShowRecordingStatus`.
* **История скорости счёта** (`CountsRateManager`) — предел `UpperWindow = 120` снимков, старший
  снимается (`CountsRateManager.cs:89–92`); такт выше это подтверждает.
* **Таймеры.** `MainForm.timer` живёт с окном программы; у документа, вида, окна отчёта, окна
  результата и панелей своих таймеров нет; `XPTable` `DoubleCellEditor.timer` останавливается и
  разбирается (`DoubleCellEditor.cs:476–480`).
* **Кэши отрисовки:** образцы строк отчёта FSA (`FSAReportView.swatches`, ключ — вид|цвет, предел —
  палитра), глиф якоря (`EnergySpectrumView.fsaAnchorGlyphCache`, один на процесс), буферы точек
  кадра (`barChartRun`, `peakBand*`, `fsaCurveBuffer` — очищаются каждый кадр) — `paint` чист.
* **Окно «Peak detection» держит последний закрытый документ строками таблицы**
  (`DCSpectrumListView.tableModel1.rows`, путь из `docs_fix_settle.txt`) — не больше одного документа,
  заменяется следующим; не мой файл; строкой не заводится (это не рост).
* **Перья и кисти GDI+ без `using`** вне моих файлов: `PulseView`, `StandardPulseView`,
  `CalibrationGraph`, `FWHMCalibrationGraph` — разбираются вручную в конце отрисовки; `ColorComboBox`
  (`ColorComboBox.cs:191, 274–275, 437`) — кисти/перья без `Dispose` в окне выбора цвета: объекты
  GDI+, их освобождает финализатор (контроль `pen`), окно не долгоживущее.
* **Журналы:** `AppLog` пишет в файл с переносом по 2 МБ (`AppLog.MaxBytes`); статические списки
  `EfficiencySimulator.TraceLog` (предел `TraceLimit`) и `FsaCascadeSummer.TripleLog` (только под
  флагом проб `LogTriples`) в работе приложения не растут.
* **Накопление импульсов звукового входа в памяти** (`PulseCollection`) — в таблице «Чего делать НЕ
  надо» `TODO.md` (решение Amber 05.10.2026: «Так и задумано») — не замерялось.

### 10. Мелочи гигиены в моих файлах (пиксели те же)

* `EnergySpectrumView.ShowCalibrationPeaks` — снято перо `Color.Red`, которое создавалось и
  разбиралось каждый кадр, ничего не рисуя.
* `XPTable` `Table.OnPaintEmptyTableText` — `StringFormat` в `using` (был без `Dispose` на каждую
  отрисовку пустой таблицы); `DragDropRenderer.PaintDragDrop` — перо в `using`.

## Контроль (итоговые прогоны на сборке с правками, `D:\BqMoni_Claude\p315\final\`)

Положительные контроли пробы — подброшенная утечка обязана быть названа:

| прогон | подброс | итог |
|---|---|---|
| `paint_sab_gdi` | `CreatePen` без `DeleteObject` на кадр | GDI **1.0000 на кадр** (900 за 900 кадров) — пойман |
| `paint_sab_user` | `CreateMenu` на кадр | USER **1.0056 на кадр** — пойман |
| `paint_sab_managed` | 4 КБ в статический список на кадр | управляемая **4159 Б на кадр** — пойман |
| `paint_sab_pen` | `new Pen` без `Dispose` на кадр | GDI 0, USER 0, управляемая +23 КБ на 900 кадров — роста нет: объект GDI+, его освобождает финализатор |
| `docs_sab_docref` | документ в статический список пробы | живы **7 из 7** — пойман |

Правки — до/после в ОДНОЙ сборке: `nofix` снимает обе правки отражением в живом приложении
(`MainForm.DockPanel_ContentRemoved` из `dockPanel1.ContentRemoved`, `DCControlPanel_Disposed` из
`Disposed` каждой новой панели); заслон доступности (`noacc`) — в обоих плечах:

| сцена | без правок (`nofix`) | с правками |
|---|---|---|
| 13 открытий-закрытий документа (`docs_*`), живых после сборки и покоя 40 с | **13 из 13**, путь через `VS2015BlueTheme._stripBefore` | **1 из 13** (последний; держит `DCSpectrumListView` строками — не рост) |
| то же, на документ: GDI / USER / управляемая (вторая половина) | **+4.0 / +2.3 / +1.34 МБ** | 0 / +0.8 (потоки пула, п. 2) / +11.6 КБ |
| 10 смен раскладки (`layout_*`): прежних `DCControlPanel` / «Peak detection» | **10 / 10** | **0** / 1 (без пути от корней) |
| то же, на смену: GDI / USER / управляемая | **+12 / +3.75 / +2.3…6 МБ** | 0 / +0.75 (потоки пула) / полка |
| контроль среды (`plain_*`): простых форм живо | без заслона 6 из 6 (86 запросов `WM_GETOBJECT`) | с заслоном 1 из 6 (42 отклонено) |

Отрисовка (`paint`, 1980 кадров, 9 сцен): GDI 45 → 45, управляемая +9 КБ; USER +11 / HND +20 — по
одному на круг сцен = на пересчёт FSA, то есть новые GUI-потоки пула (п. 2; итоговая проба судит
USER/HND за вычетом GUI-потоков, `GetGUIThreadInfo`).

Подтверждающий `paint` на итоговой пробе (`D:\BqMoni_Claude\p315\confirm\paint.txt`, счёт GUI-потоков
`GetGUIThreadInfo`): круги 1–8 — USER +1 и GUI-потоков +1 на круг (ровно п. 2); на круге 9 разовая
ступень GDI +3, USER +10, HND +32 при GUI-потоках +2 — перепись окон процесса называет три новых
окна служб ввода Windows: `CicMarshalWndClass`, `MSCTFIME UI` (TSF/IME), `SystemUserAdapterWindowClass`
— система заводит их один раз, когда включает службу текстового ввода в процессе; кода приложения в
этом нет. После ступени — снова только потоки.

Остаток `docs` после правок (`confirm\docs3.txt` против `confirm\docs13.txt`, итоговая проба): живых
закрытых документов 1 из 14 (последний, `DCSpectrumListView`); GDI 0 и USER 0 за вычетом GUI-потоков
пула; управляемая ≈ +14 КБ на документ во второй половине. Перепись достижимого (разность прогонов в
3 и 13 циклов) типов приложения в росте не называет: +40 `WeakReference` и +20
`ClientUtils.WeakRefCollection.WeakRefObject` (учёт WinForms слабыми ссылками, сам себя
подчищает), +10 КБ массива `Int64[]`. Строкой не заводится: 1000 открытых документов — ~14 МБ, и
источник не в коде приложения.

Длинный `paint` на итоговой пробе (`confirm\paint_long.txt`, 4320 кадров, 24 круга сцен, 23 пересчёта
FSA): GDI 45 → 45; USER +19 при новых GUI-потоках +19 — ровно; HND за вычетом потоков 0; управляемая
1.9 Б на кадр во второй половине — **роста нет, код 0**. Такт (`confirm\tick_capcache.txt`, 12 000
тактов с изображением правки кэша дозы) — код 0; смена раскладки (`confirm\layout_fix.txt`) — код 0.

## Правки и пробы полосы (для коммита)

* `BecquerelMonitor/MainForm.cs` — `InitializeDockPanelTheme` подписывает `dockPanel1.ContentRemoved`;
  `DockPanel_ContentRemoved` → `ForgetDisposedThemedStrips(ThemeBase)` (п. 3, 4).
* `BecquerelMonitor/DCControlPanel.cs` — отписка от `DeviceConfigManager.DeviceConfigListChanged` в
  `Disposed` (п. 4).
* `BecquerelMonitor/FSAReportView.cs` — `pushedView` слабой ссылкой (п. 5).
* `BecquerelMonitor/EnergySpectrumView.cs`, `BecquerelMonitor/XPTable/Models/Table.cs`,
  `BecquerelMonitor/XPTable/Renderers/DragDropRenderer.cs` — гигиена GDI+ (п. 10).
* `tools/effmaker/probes/P315GdiLeakProbe.cs` — новая проба (режимы, опыты, контроли — в её шапке).

Ни строки ресурсов, ни поставочных конфигов полоса не трогала. Отрисовка FSA (слои, доли, невязка,
подписи) не менялась — правки вне неё; витрина — в разделе «Приёмка».

Свидетельства — только текст, на диске вне git: `D:\BqMoni_Claude\p315\*.txt` (развилки),
`D:\BqMoni_Claude\p315\final\*.txt` (итоговые прогоны с контролями), `D:\BqMoni_Claude\p315\confirm\*.txt`
и `census_docs3.tsv` (подтверждение на итоговой пробе), сборочные журналы `build_*.log`,
`check_all.log`. Копии спектра и разборка IL DockPanelSuite сняты.

## Приёмка

* Сборка приложения Debug в `bin\p315` / `obj\p315` — код 0; `build_all.ps1 -Out build_p315` — код 0
  (сверено 384 файла по sha256, приложение рядом с пробами сошлось со сборкой).
* `python tools/check_all.py` с переменными `BQ_FSA_SHOWCASE_PROBES`, `BQ_FSA_REPORT_VIEW_PROBES`,
  `BQ_CORPUS_SCENES_PROBES`, `BQ_SUMMING_PROBES` → `build_p315`, `BQ_EFFCOVER_EXE` → его
  `EffCoverProbe.exe` (ключа `--probes` у `check_all` нет): **46 из 46 зелёных, код 0**; витрина FSA
  (`check_fsa_showcase.py`) — **код 0**, эталон не переобъявлялся.
* Сборочные каталоги полосы (`bin\p315`, `obj\p315`, `tools\effmaker\probes\build_p315`) сняты после
  приёмки.

## Остаток для распорядителя

* Правка `DoseRate.cs` (п. 7) — не файл полосы; готовый текст правки там же, строка реестра — в
  отчёте полосы.
