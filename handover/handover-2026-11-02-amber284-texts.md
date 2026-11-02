# AMBER284 — человекочитаемые фразы окна FSA (полоса `a284`, 02.11.2026)

## Постановка

Amber 02.11.2026, консоль, дословно: «Запиши задачу, пройтись по всем подобным фразам "Фон перевычтен"
и подобным и переделать их в человекочитаемый вид. Ожидаю на вход вопросник "Фраза1" и предложения
"Фраза человекочитаемая".»

Восемь вопросников и ответы Amber дословно — журнал
[handover-2026-10-29-amber270-276-new-tasks.md](handover-2026-10-29-amber270-276-new-tasks.md), раздел
«AMBER284 — человекочитаемые фразы окна FSA (02.11.2026)». Эта полоса — исполнение: 28 фраз, «сет» в
обеих строках, капсула только в отказе. Работа — в worktree от снимка `tw1113`, сдана патчем.

## Что сделано

1. **Тексты** — `FSAReportView.resx`/`.ru.resx` (ключи `FSAReport_*`), `Properties/Resources.resx`/`.ru.resx`
   (`FSA*`), обе культуры; комментарии генератора в `Resources.Designer.cs` — под новые английские
   значения (у `FSANoFit` и `FSALibraryEmptied` комментария нет — не заводились). Таблица — ниже.
2. **«Нет матрицы отклика» — в подсказку строки «Матрица отклика»** (решение Amber «Объединить со строкой
   выше … Остальное убрать в подсказку»). Отдельная строка `FSAReport_NoMatrixWarnRow`/`…Value` снята;
   новый ключ `FSAReport_MatrixNotUsedTip`, читатель `FSAReportView.MatrixNotUsedTip` (условие то же, что
   было у снятой строки: разбор с кривой и без матрицы). У строки «Матрица отклика» при этом условии —
   красное значение, как и прежде, и признак происшествия (`Warning`) у строки, который несла снятая строка.
3. **Шаг узлов под бетой (VIF, узлы, заходы) — в подсказку строк «только по бета»** (решение Amber «Убрать
   строку в подсказку строки бета-нуклидов»). Своей строки блока нет; `FSAReportView.BetaOnlyRowTip` —
   подсказка строки состава: её подпись и текст `BetaKnotCaption`. На виду остаётся только красное
   предупреждение, когда VIF выше предела и на самом редком шаге (`FSAReport_BetaKnotVifOverRow`/`…Value`,
   подсказка — тот же текст шага узлов). Значения «два захода»/«один заход» сняты.
4. **Бета пробы**: в строке — одни отсчёты («Бета-излучение пробы | 1234 отсч.»), число узлов раздела —
   подсказкой (`FSAReport_SampleBetaTip`, `FSAReportView.SampleBetaTip`), капсула — только в причине отказа
   `FSAReport_SampleBetaOtherCapsule`. Слова «выключена»/«не взята» → «выключено»/«не взято»: подпись
   строки сменила род («Бета пробы» → «Бета-излучение пробы»).
5. **«Модель выше спектра»**: z — в подсказке (`FSAReport_ImagesAboveTip`, `FSAReportView.ImagesAboveSpectrumTip`),
   как у «Фон больше спектра» полосы `fsalow`; подпись — полоса и «в N раза».
6. **ISO 11929** — из подписи строки правила в её подсказку (`FSAReport_LimitRuleTip`).
7. **Справочники распада**: в строках подсказки `Supply*` `decay_chain` → «цепочка распада»,
   `decay_radiations` → «таблица излучений», `perc` → «доля ветви в цепочке»; ENSDF оставлен.
8. **Пометка «только по бета»** — и в окне результата: `DCResult_FsaContinuumOnly` («по сплошному спектру»
   → «только по бета», англ. «beta only») и умолчание `FsaMeasurementResult.Texts.ContinuumOnly` — та же
   строка того же нуклида, иначе два окна называли бы её по-разному.
9. **Подсказки опор** (`FSAReport_AnchorTip`, `…RollbackTip`) — тем же языком, что строка («опорные пики»,
   «+ взят, - отвергнут»); `FSAReport_ResidualNoiseTip` — «уровень шума» вместо «пола».
10. **Гид**: `docs/fsa.html` (макет блока «Качество разбора» RU/EN — строки, значения, объяснения; режим
    «Без матрицы» — без отдельной строки, объяснение строки «Матрица отклика» с ценой; «уровень шума» в
    «Под капотом» и в игрушечном FSA), `docs/how-it-works.html` (одна фраза «пик вне состава» RU/EN).
11. **Пробы**: `FsaReportViewProbe` (раздел 6: сцена отката без строки шага узлов; новые сцены «бета пробы +
    VIF выше предела» и «кривая без матрицы»; сканер ПРЕЖНИХ ФРАЗ — 67 отличительных кусков 28 фраз обоих
    языков — по подписи, значению и подсказке каждой клетки, с положительным контролем; раздел полосы:
    z «модели выше спектра» — в подсказке), `FsaInputCheckProbeP195` (сцены (1)/(2): цена без матрицы —
    подсказкой красной строки «Матрица отклика»), `PureBetaProbeA274` (печать: шаг узлов — подсказкой,
    подсказка беты пробы, пометка «только по бета»).

## Таблица ключей

«снят» — ключ удалён из обеих `resx`; «—» в «было» — ключ новый. Читатели — файлы кода и оснастки, где
ключ назван (`git grep -w`, без `Resources.Designer.cs`).

| ключ | было RU | стало RU | стало EN | читатели |
|---|---|---|---|---|
| `FSAReport_ResidualRow` | Невязка: + лишнее / − нехватка | Расхождение модели и спектра: + / − | Difference between model and spectrum: + / − | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_ResidualClamped` | показ подрезан на {0} каналах | на графике обрезано ниже нуля: {0} каналов | clipped below zero on the chart: {0} channels | `EnergySpectrumView.Fsa.cs`, `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_Chi2Row` | χ²/ndf, качество подгонки (по статистике Пуассона) | Качество подгонки χ²/ndf | Goodness of fit χ²/ndf | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_InflationRow` | σ×, во сколько раз раздута погрешность | Поправка погрешности на качество подгонки, × | Uncertainty correction for the fit quality, × | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_LimitRuleRow` | Правило MDA (ISO 11929) | Нуклид найден, если значимость | Nuclide is found if significance | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_LimitRuleTip` | Пределы посчитаны под правило обнаружения самого FSA. Порог решения a* = k_α·σ₀ при k_α = {0} — значимость z, которой строка состава должна достичь, чтобы считаться найденной (α ≈ {1} %). Предел обнаружения a# = a* + k_β·σ(a#) при k_β = {2}: активность, равная a#, так находится во всех измерениях, кроме {3} %. Оба надуты на √(χ²/ndf), как погрешности. | Пределы посчитаны по ISO 11929 под правило обнаружения самого FSA. Порог решения a* = k_α·σ₀ при k_α = {0} — значимость z, которой строка состава должна достичь, чтобы считаться найденной (α ≈ {1} %). Предел обнаружения a# = a* + k_β·σ(a#) при k_β = {2}: активность, равная a#, так находится во всех измерениях, кроме {3} %. Оба надуты на √(χ²/ndf), как погрешности. | The limits are computed by ISO 11929 for the FSA detection rule itself. Decision threshold a* = k_α·σ₀ with k_α = {0}: the significance z a composition row must reach to be reported as found (α ≈ {1} %). Detection limit a# = a* + k_β·σ(a#) with k_β = {2}: an activity equal to a# is found this way in all but {3} % of measurements. Both are inflated by √(χ²/ndf), like the uncertainties. | `FSAReportView.cs` |
| `FSAReport_DriftRow` | Подгонка дрейфа шкалы | Дрейф шкалы | Scale drift | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_DriftEdge` | край сетки | на границе диапазона | at the range limit | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_AnchorRow` | Привязка шкалы, опор | Опорных пиков для шкалы | Reference peaks for the scale | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_AnchorGainRow` | Усиление по опорам, % | Растяжение шкалы, % | Scale stretch, % | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_AnchorZeroRow` | Ноль по опорам, кэВ | Сдвиг шкалы, кэВ | Scale shift, keV | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_AnchorTip` | Пики полного поглощения, по которым привязана шкала (+ опора, - отвергнут): компонент, центр модели кэВ, остаток кэВ, доля пика, причина | Опорные пики, по которым подстроена шкала (+ взят, - отвергнут): компонент, центр модели кэВ, остаток кэВ, доля пика, причина | Reference peaks the scale was adjusted on (+ taken, - rejected): component, model centre keV, residual keV, peak share, reason | `FSAReportView.cs` |
| `FSAReport_AnchorRollbackRow` | Откат привязки шкалы: с привязкой (опор {0}, усиление {1} %) χ²/ndf Пуассона {2} хуже, чем без неё ({3}) | Оставлена шкала файла: подстройка по {0} пикам ({1} %) ухудшила подгонку: χ²/ndf {2} против {3} | File scale kept: adjusting it on {0} peaks ({1} %) made the fit worse: χ²/ndf {2} against {3} | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_AnchorRollbackTip` | Опоры отменённого разбора (+ опора, - отвергнут): | Опорные пики отменённой подстройки (+ взят, - отвергнут): | Reference peaks of the cancelled adjustment (+ taken, - rejected): | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_SuppressedRow` | Состав пересилен образом | Счёт объясняет не нуклид, а | The counts are explained not by a nuclide but by | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_BackgroundRejectedRow` | Фон не взят | Фоновый спектр не вычтен | Background spectrum not subtracted | `FSAReportView.cs`, `FsaBackgroundMarkProbeF48.cs` |
| `FSAReport_SampleBetaRow` | Бета пробы (раздел электронов матрицы) | Бета-излучение пробы | Sample beta radiation | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_SampleBetaUsed` | в образах {0} отсч.; раздел {1} узлов; капсула источника {2} | {0} отсч. | {0} counts | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_SampleBetaKeyOff` | выключена | выключено | switched off | `FSAReportView.cs` |
| `FSAReport_SampleBetaRefused` | не взята: {0} | не взято: {0} | not used: {0} | `FSAReportView.cs` |
| `FSAReport_SupplyRow` | Поставки данных распада расходятся | Справочники распада расходятся | Decay data references disagree | `FSAReportView.cs` |
| `FSAReport_SupplyTip` | У этих нуклидов две поставки данных распада расходятся, и разбору пришлось выбрать. Что взято: | У этих нуклидов два справочника данных распада расходятся, и разбору пришлось выбрать. Что взято: | Two decay-data references disagree for these nuclides, and the analysis had to choose. What was taken: | `FSAReportView.cs` |
| `FSAReport_SupplyBetaPlusMode` | {0} → {1}: в decay_chain {2} % — доля одних позитронов, а не ветви ε+β⁺; ветвь взята остатком, {4} % | {0} → {1}: в цепочке распада {2} % — доля одних позитронов, а не ветви ε+β⁺; ветвь взята остатком, {4} % | {0} → {1}: the decay chain gives {2} % for the β⁺ mode alone, not for the ε+β⁺ branch; the branch is taken as the remainder, {4} % | `FSAReportView.cs` |
| `FSAReport_SupplyOtherMode` | {0} → {1}: доля ветви в decay_chain {2} % меньше суммы позитронов {3} % в decay_radiations; прочитана как доля другой моды, ветвь взята суммой {4} % | {0} → {1}: доля ветви в цепочке распада {2} % меньше суммы позитронов {3} % в таблице излучений; прочитана как доля другой моды, ветвь взята суммой {4} % | {0} → {1}: the branch share in the decay chain {2} % is below the positron sum {3} % of the radiation table; read as the share of the other mode, the branch is taken as {4} % | `FSAReportView.cs` |
| `FSAReport_SupplyExcess` | {0} → {1}: сумма позитронов {3} % в decay_radiations больше, чем оставляют прочие ветви ({4} %); избыток поставки, зажато | {0} → {1}: сумма позитронов {3} % в таблице излучений больше, чем оставляют прочие ветви ({4} %); избыток справочника ограничен | {0} → {1}: the positron sum {3} % of the radiation table exceeds what the other branches leave ({4} %); the excess of the reference is clamped | `FSAReportView.cs` |
| `FSAReport_SupplyClamped` | {0} → {1}: сумма позитронов {3} % в decay_radiations больше доли ветви {2} %; зажато ею | {0} → {1}: сумма позитронов {3} % в таблице излучений больше доли ветви {2} %; ограничена ею | {0} → {1}: the positron sum {3} % of the radiation table exceeds the branch share {2} %; clamped to it | `FSAReportView.cs` |
| `FSAReport_SupplyShareScaled` | {0} → {1}: доля β⁺ ветви {2} (decay_radiations) против {3} (ENSDF), {4} %; доли по уровням приведены к decay_radiations | {0} → {1}: доля β⁺ ветви {2} (таблица излучений) против {3} (ENSDF), {4} %; доли по уровням приведены к таблице излучений | {0} → {1}: β⁺ fraction of the branch {2} (radiation table) vs {3} (ENSDF), {4} %; level-by-level shares scaled to the radiation table | `FSAReportView.cs` |
| `FSAReport_SupplyShareKept` | {0} → {1}: доля β⁺ ветви {2} (decay_radiations) против {3} (ENSDF), {4} %; доля ветви упёрлась в perc, доли по уровням оставлены по ENSDF | {0} → {1}: доля β⁺ ветви {2} (таблица излучений) против {3} (ENSDF), {4} %; доля ветви упёрлась в долю ветви в цепочке, доли по уровням оставлены по ENSDF | {0} → {1}: β⁺ fraction of the branch {2} (radiation table) vs {3} (ENSDF), {4} %; the branch share has reached the branch share in the chain, level-by-level shares kept from ENSDF | `FSAReportView.cs` |
| `FSAReport_PileUpCapRow` | Колонка наложений ограничена R·τ: фит просил {0:F0} пар, мёртвое время допускает {1:F0} — избыток не наложения | Наложения импульсов ограничены загрузкой: подгонка просила {0:F0} пар, при такой загрузке возможно не больше {1:F0}; избыток — не наложения | Pulse pile-up limited by the count rate: the fit asked for {0:F0} pairs, at this rate no more than {1:F0} are possible; the excess is not pile-up | `FSAReportView.cs` |
| `FSAReport_PileUpCapValue` | ограничена | ограничены | limited | `FSAReportView.cs` |
| `FSAReport_UncoveredRow` | {0} кэВ: пик вне состава — на {1} % выше модели, z {2}; линии библиотеки рядом: {3} | {0} кэВ: пик не объяснён нуклидами разбора — на {1} % выше модели (z {2}); рядом в библиотеке: {3} | {0} keV: peak not explained by the analysis nuclides — {1} % above the model (z {2}); nearby in the library: {3} | `FSAReportView.cs` |
| `FSAReport_ImagesAboveRow` | Модель не описывает низ спектра: образы нуклидов выше всего измеренного спектра в {0}–{1} кэВ (×{2}, z {3}) | Модель выше спектра в {0}–{1} кэВ (в {2} раза): низ спектра не описан | Model above the spectrum at {0}–{1} keV ({2} times): the low part of the spectrum is not described | `FSAReportView.cs` |
| `FSAReport_ImagesAboveValue` | низ не описан | низ не описан | low end not described | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_SetOmittedRow` | {0}: есть в сете, нет в составе — {1} | {0}: есть в сете, но в разборе FSA не обнаружен — {1} | {0}: in the set, but not found by the FSA analysis — {1} | `FSAReportView.cs`, `FsaBqProbe.cs` |
| `FSAReport_SetOmittedValue` | не разложен | не обнаружен | not found | `FSAReportView.cs`, `FsaBqProbe.cs` |
| `FSAReport_ResidualNoise` | шумовой пол {0} % | уровень шума {0} % | noise level {0} % | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_ResidualNoiseTip` | Модель без единой ошибки всё равно даёт около {0} % в каждой половине: это пуассонов шум полосы (ожидаемое положительное отклонение каждого канала от идеальной модели, в сумме, от измеренных отсчётов той же полосы). Невязка около этого пола модель от идеальной не отличает; не описано только то, что сверх него. | Модель без единой ошибки всё равно даёт около {0} % в каждой половине: это пуассонов шум полосы (ожидаемое положительное отклонение каждого канала от идеальной модели, в сумме, от измеренных отсчётов той же полосы). Расхождение около этого уровня шума модель от идеальной не отличает; не описано только то, что сверх него. | A model without a single error still shows about {0} % in each half: this is the Poisson noise of the band (the expected positive deviation of every channel from a perfect model, summed, over the measured counts of the same band). A difference near this noise level does not tell the model from a perfect one; only what is above it is left undescribed. | `FSAReportView.cs` |
| `FSAReport_NoMatrixWarnRow` | Нет матрицы отклика: каскадное суммирование не учтено — каскадные нуклиды занижены на CF − 1 (корпус: −3…−11 % на 5 см, −4…−7 % в маринелли и Петри); в образах нет комптона и вылетов; шкала привязана по голым пикам. Постройте матрицу из геометрии. | снят | снят | `FSAReportView.cs` |
| `FSAReport_NoMatrixWarnValue` | до −11 % | снят | снят | — |
| `FSAReport_CurveOutsideRow` | Кривая эффективности {0}…{1} кэВ — линии вне неё исключены из образов: {2} | Линии вне кривой эффективности ({0}…{1} кэВ) не учитываются: {2} | Lines outside the efficiency curve ({0}…{1} keV) are not counted: {2} | `FSAReportView.cs` |
| `FSAReport_CurveOutsideValue` | исключены | не учтены | not counted | `FSAReportView.cs`, `FsaInputCheckProbeP195.cs` |
| `FSAReport_BetaOnlyMark` | по сплошному спектру | только по бета | beta only | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_BetaOnlyRemovedRow` | Строки по сплошному спектру сняты: {0} | Нуклиды без гамма-линий убраны: {0} | Nuclides without gamma lines removed: {0} | `FSAReportView.cs` |
| `FSAReport_BetaOnlyRemovedValue` | сняты | убраны | removed | `FSAReportView.cs` |
| `FSAReport_BetaOnlyNoMatrix` | нет матрицы отклика: образ беты строится только разделом электронов матрицы | нет матрицы отклика: бета-спектр строится только из раздела электронов матрицы | no response matrix: the beta spectrum is built only from the electron section of a matrix | `FSAReportView.cs` |
| `FSAReport_BetaOnlyKeyOff` | бета пробы выключена | бета-излучение пробы выключено | sample beta radiation is switched off | `FSAReportView.cs` |
| `FSAReport_BetaOnlyRefusal` | Строки по сплошному спектру ({0}) сняты: {1}. | Нуклиды без гамма-линий ({0}) убраны: {1}. | Nuclides without gamma lines ({0}) removed: {1}. | `FSAReportView.cs` |
| `FSAReport_BetaBranchExcessRow` | Ветви β в базе с суммой выше 100 %: {0} | В базе бета-ветви в сумме больше 100 %: {0} | Beta branches in the database sum to more than 100 %: {0} | `FSAReportView.cs` |
| `FSAReport_BetaBranchExcessValue` | образ беты завышен | бета завышена | beta overstated | `FSAReportView.cs` |
| `FSAReport_BetaKnotRow` | Подложка под строками по сплошному спектру: узлы через {0}·FWHM, VIF {1} (предел {2}) | Подложка под нуклидами без гамма-линий: узлы через {0}·FWHM, VIF {1} (предел {2}) | Baseline under the nuclides without gamma lines: knots every {0}·FWHM, VIF {1} (limit {2}) | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_BetaKnotTwoPassValue` | два захода | снят | снят | — |
| `FSAReport_BetaKnotOnePassValue` | один заход | снят | снят | — |
| `FSAReport_MatrixNotUsedTip` | — | Без матрицы отклика суммирование каскадов не учтено: нуклиды с каскадными гамма-квантами занижены — до 11 %. В модели нет комптоновской части спектра и пиков вылета, а шкала энергий подстраивается только по самим пикам. Постройте матрицу из геометрии. | Without a response matrix cascade summing is not applied: nuclides with cascade gamma rays read low, by up to 11 %. The model has no Compton part of the spectrum and no escape peaks, and the energy scale is adjusted on the peaks alone. Build the matrix from the geometry. | `FSAReportView.cs`, `FsaInputCheckProbeP195.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_SampleBetaTip` | — | Отсчёты, которые модель приписала бета-электронам из пробы: {0}. Раздел электронов в матрице отклика посчитан на {1} узлах по энергии. | Counts the model attributes to beta electrons from the sample: {0}. The electron section of the response matrix is computed on {1} energy nodes. | `FSAReportView.cs` |
| `FSAReport_ImagesAboveTip` | — | z {0}; модели нуклидов выше всего измеренного спектра в этой полосе | z {0}; the nuclide models are above the whole measured spectrum in this band | `FSAReportView.cs` |
| `FSAReport_BetaKnotVifOverRow` | — | Бета-спектр нуклидов без гамма-линий не отличить от подложки: их активность ненадёжна | The beta spectrum of the nuclides without gamma lines cannot be told from the baseline: their activity is unreliable | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSAReport_BetaKnotVifOverValue` | — | ненадёжна | unreliable | `FSAReportView.cs`, `FsaReportViewProbe.cs` |
| `FSABackgroundRejectedMark` |  (фон не взят) |  (фон не вычтен) |  (bg not subtracted) | `FsaPresentationBuilder.cs`, `FsaBackgroundMarkProbeF48.cs` |
| `FSASuppressedMark` |  · подавлен |  · преобладает приборное |  · instrument dominates | `FsaPresentationBuilder.cs`, `FsaQualityRowProbe.cs` |
| `FSANotResolvedNoShare` | не выделено | не найден | not found | `FsaPresentationBuilder.cs`, `FsaQualityRowProbe.cs` |
| `FSANotResolvedHint` | Образ предъявлен фиту и получил ноль: полосу держит континуум модели. Величина требует геометрии детектора. | Разбор искал этот нуклид и не нашёл: его область целиком объясняет подложка. Чтобы оценить предел обнаружения, нужна геометрия детектора. | The analysis looked for this nuclide and did not find it: the baseline explains its whole region. A detector geometry is needed to estimate the detection limit. | `FsaPresentationBuilder.cs` |
| `FSAUndetectedFoldedRow` | не определяются ({0}) | ниже предела обнаружения ({0}) | below the detection limit ({0}) | `FsaPresentationBuilder.cs` |
| `FSALibraryEmptied` | Полноспектральное разложение: в составе не осталось ни одной компоненты для фита — при матрице отклика собственный рентген и вылет кристалла несёт матрица; добавьте нуклид в состав | Полноспектральное разложение: в составе не осталось нуклидов — рентген и пики вылета кристалла уже учтены матрицей отклика; добавьте нуклид в состав | Full-spectrum decomposition: no nuclides are left in the composition — the crystal X-rays and escape peaks are already accounted for by the response matrix; add a nuclide to the composition | `FSAReportView.cs`, `FsaAnalysisSession.cs`, `FsaReportViewProbe.cs`, `FsaSessionStatesProbeP261.cs` |
| `FSANoFit` | Полноспектральное разложение: ни один узел сетки дрейфа не дал решения для этого спектра | Полноспектральное разложение: подгонка не сошлась — проверьте энергетическую калибровку и состав | Full-spectrum decomposition: the fit did not converge — check the energy calibration and the composition | `FsaAnalysisSession.cs` |
| `FSAAnnihilationDegenerate` | {0}: аннигиляция — единственное его измеримое излучение: прочие линии в полосе фита дают {1} % на распад при поле {2} %; свободный образ 511 и нуклид — один столбец, свободный образ снят | {0}: в спектре виден только по аннигиляции 511 кэВ (прочие линии — {1} % на распад при пороге {2} %); отдельная составляющая 511 кэВ убрана, чтобы не дублировать нуклид | {0}: visible in the spectrum only through 511 keV annihilation (other lines give {1} % per decay, threshold {2} %); the separate 511 keV component is removed so as not to duplicate the nuclide | `FsaAnalyzer.cs` |
| `DCResult_FsaContinuumOnly` | по сплошному спектру | только по бета | beta only | `DCResultView.cs` |

## Отступления и находки

* ⚠ **`FSANotResolvedHint` — посылка вопросника уже реальности.** Подсказка выбрана дословно: «Разбор
  искал этот нуклид и не нашёл…», значение «не найден». Но строка с этим значением в окне ставится
  ТОЛЬКО образу «Обратное рассеяние» (`FsaPresentationBuilder.BuildRows`, `SuppressedImages`), не нуклиду:
  на экране будет «Обратное рассеяние | не найден» с подсказкой «искал этот нуклид». Сделано дословно по
  решению; вопрос Amber — менять ли на «эту составляющую» / «не найдено» (одна строка в двух `resx`).
* Снимки гида со старыми фразами (в git, `docs/img/`) — не переснимались по заданию:
  `ru/panel-fsa.png`, `en/panel-fsa.png`, `ru/main-fsa.png`, `en/main-fsa.png`, `ru/main-fsa-nomatrix.png`,
  `en/main-fsa-nomatrix.png`. Вместе с `main-fsa-nomatrix` переснимается легенда `q.l4` («Предупреждение с
  ценой «до −11 %»» — строки больше нет) и её горячая точка 4 в `data-hot`/`data-hot-en` `docs/fsa.html`:
  легенда описывает снимок и оставлена в паре с ним.
* **Текст красного предупреждения VIF — полосы, не Amber**: она решила «на виду — только предупреждение»,
  слов не дала. Взято её же выражение из п. 19 («нуклиды без гамма-линий»): «Бета-спектр нуклидов без
  гамма-линий не отличить от подложки: их активность ненадёжна» | «ненадёжна»; подсказка — шаг узлов и VIF.
  Тем же выражением переписан текст подсказки шага узлов (`FSAReport_BetaKnotRow`).
  ⚠ Прежде строка шага узлов краснела и при ОТКАЗЕ второго захода гамма-строк (VIF в пределе). По решению
  «на виду — только предупреждение, если VIF выше предела» такой отказ теперь виден только в подсказке
  (хвост «второй заход не состоялся (…)»). Если Amber хочет и его на виду — одно условие в
  `FSAReportView.MakeQualityRows`.
* «Оставлена шкала файла: подстройка по {0} пикам» при одном пике читается «по 1 пикам» (видно на экране):
  формулировка Amber дословно, склонение по числу в `resx` не выражается — оставлено.
* Тексты, которых в 28 фразах не было, не трогались, кроме названных в п. 4, 8, 9: служебные заметки
  разбора (`AnchorNote`, «опор …» в `FsaAnalyzer`) на экран не выходят.

## Приёмка

Стенд: два worktree от `tw1113` — снимок как есть (база) и с правкой; сборки Debug в свои каталоги
(`/restore`, `/p:GenerateManifests=false`, код 0 обе), каталоги проб `build_all.ps1` (код 0 оба, 382
пробы). Данные вне git (склад матриц корпуса, склад витрины) скопированы в оба дерева одинаково.

* **`FsaReportViewProbe`** (спектр `AS80_Th232Medal`, контроль `AS80_Cs137_0cm`): база — ok 452, отказов 0;
  правка — **ok 502, отказов 0**, «ВСЕ СОШЛИСЬ». Новые проверки — RU и EN: строки шага узлов в блоке нет при
  VIF в пределе; красная строка VIF выше предела, её подсказка = `BetaKnotCaption`; бета пробы — одни
  отсчёты, узлы (40) в подсказке, капсулы нет нигде; подсказка строки «только по бета» = подпись + шаг
  узлов; без матрицы — пометок четыре, отдельной строки нет, у строки «Матрица отклика» подсказка цены и
  признак `Warning`; z «модели выше спектра» только в подсказке; сканер прежних фраз — 0 находок на пяти
  сценах. **Положительные контроли** (все отказали, как должны): прежняя подпись «χ²/ndf, качество подгонки
  (по статистике Пуассона)» в ячейке — поймана и сканером прежних фраз, и сверкой блока; прежняя строка
  «два захода»; прежняя строка беты с узлами и капсулой; блок без строки VIF; прежняя отдельная строка
  «Нет матрицы отклика … до −11 %». Сам список 67 кусков сверен с `resx`: в снимке встречаются все 67, в
  правке — ни один.
* **`check_fsa_report_view.py`** (четыре пробы окна): база — 4 из 4, report 452 / f16 49 / f21 54 / p195 27
  ok; правка — 4 из 4, report 502 / f16 49 / f21 54 / **p195 28** ok, отказов 0 (в прогоне `check_all` — со
  сторожем свежести, код 0).
* **`check_resx.py`, `check_resx_designer.py`, `check_resx_letters.py`, `check_resx_zorder.py`** — код 0 все
  четыре; файлов чужого формата (не BOM+CRLF) 0. Переводы строк правленых файлов посчитаны байтами:
  одиночных LF 0.
* **`check_all.py`**: правка — 41 зелёных, 6 красных; база в том же стенде — те же 6 красных
  (`check_corpus_coverage` 1, `check_declared_base` 2, `check_corpus_generator` 1, `check_corpus_scenes` 2,
  `check_fsa_showcase` 1, `check_bq_summing` 1 — у базы и правки те же три отказа `RC103_point0` против
  арбитра). Новых красных нет.
* **Витрина FSA** (`check_fsa_showcase.py`, 5 спектров × 9 пар): эталон снят СТЕНДОВЫЙ базовой сборкой в
  сторонний каталог (git-эталон не переобъявлялся), правка сверена с ним, допуск rel 1e-9 / abs 1e-6.
  Расхождений **69, все — строки окна (`SCREEN`)**; полос энергии, слоёв, долей, значимостей с расхождением
  **0** — числа разбора побитово те же. Таблица — ниже.
* **Экран**: своя сборка на КОПИИ конфига (опись sha256 30 файлов до и после — совпала). Окно «Отчёт FSA»
  снято RU и EN (снимки на диске полосы, не в git): новые подписи блока на месте, ничего не обрезано
  (подписи переносятся, последняя строка блока — «Оставлена шкала файла …» — видна целиком после
  прокрутки); подсказка строки «Матрица отклика | не учтена» показывает цену разбора без матрицы. Рядом на
  том же экране работало окно другой полосы с тем же заголовком — своё окно снималось `PrintWindow` по
  PID, без глобального ввода; подсказка — единственный снимок с наведением мыши.

### Витрина: спектр — режим — полоса — компонент — было — стало — Δ

Все 69 расхождений — строки окна (`SCREEN`, подпись | значение | подсказка), сведены по видам; «было» →
«стало» — подпись строки:

| спектр | режим | полоса | компонент | было | стало | Δ |
|---|---|---|---|---|---|---|
| все 9 пар | — | SCREEN | строка невязки | Невязка: + лишнее / − нехватка — шумовой пол … | Расхождение модели и спектра: + / − — уровень шума … | ≠ |
| все 9 пар | — | SCREEN | χ²/ndf | χ²/ndf, качество подгонки (по статистике Пуассона) | Качество подгонки χ²/ndf | ≠ |
| все 9 пар | — | SCREEN | множитель | σ×, во сколько раз раздута погрешность | Поправка погрешности на качество подгонки, × | ≠ |
| все 9 пар | — | SCREEN | правило | Правило MDA (ISO 11929) | Нуклид найден, если значимость | ≠ |
| 9 пар | — | SCREEN | опоры | Привязка шкалы, опор | Опорных пиков для шкалы | ≠ |
| 7 пар с опорами (без радона) | — | SCREEN | усиление, ноль | Усиление по опорам, % · Ноль по опорам, кэВ | Растяжение шкалы, % · Сдвиг шкалы, кэВ | ≠ |
| ASN16_Radon_filter2 | chain_eq, chain_noeq | SCREEN | откат | Откат привязки шкалы: с привязкой (опор …) … | Оставлена шкала файла: подстройка по … пикам … | ≠ |
| AS80_Th232_disk, G1S24_Rn222Coal_eq01 ×2 | — | SCREEN | бета пробы | Бета пробы (раздел электронов матрицы) \| в образах … | Бета-излучение пробы \| … отсч. | ≠ |
| ASN16_Radon_filter2, G1S24_Rn222Coal_eq01 | chain_noeq, sample_noeq | SCREEN | свёрнутые | не определяются (5) / (4) | ниже предела обнаружения (5) / (4) | ≠ |
| ASN16_Cs137_10cm | sample_nomatrix | SCREEN | строка 11 | Матрица отклика \| не учтена (подсказки нет) | Матрица отклика \| не учтена (подсказка — цена без матрицы) | ≠ |
| ASN16_Cs137_10cm | sample_nomatrix | SCREEN | строки 14–18 | «Нет матрицы отклика: … \| до −11 %», опоры, «Фон больше спектра» | строки сдвинулись на одну вверх: отдельной строки нет | ≠ |

Счёт по парам: Cs-137 в домике 7 + 7 + 7, ториевый диск 8, радон 6 + 7, уголь 8 + 9, Cs-137 10 см без
матрицы 10 — всего 69, полоса у всех — `SCREEN`.

## Доделки полосы `a284b` (02.11.2026)

Остаток строки после полосы `a284`. Worktree от снимка `tw1115`, сдано патчем. Решения Amber 02.11.2026
вопросником, дословно (журнал [handover-2026-10-29-amber270-276-new-tasks.md](handover-2026-10-29-amber270-276-new-tasks.md),
«Общий счёт волны: решения Amber»):

* «не найден» у строки «Обратное рассеяние» — **«"не найдено" / "искал эту составляющую" (Рекомендую)»**;
* текст красной строки VIF — **«Да, принять (Рекомендую)»**: не менялся;
* отказ второго захода гамма-строк — **«Вернуть красной строкой (Рекомендую)»**.

И по заданию распорядителя: склонение «по {0} пикам» — 1, 21, 31… — «пику», прочие — «пикам», слова Amber
не менять, EN — «peak»/«peaks»; переснять шесть снимков гида и поправить легенду `q.l4` с её точкой 4;
перепроверить окно «Отчёт FSA» экраном.

### Что сделано

1. **«не найдено»** (`Properties/Resources*.resx`): `FSANotResolvedNoShare` RU «не найден» → «не найдено» (EN «not
   found» как было); `FSANotResolvedHint` RU «Разбор искал эту составляющую и не нашёл: её область целиком
   объясняет подложка. Чтобы оценить предел обнаружения, нужна геометрия детектора.», EN «The analysis looked
   for this component and did not find it: …». Комментарий генератора в `Resources.Designer.cs` — под новое
   английское значение.
2. **Отказ второго захода — красной строкой** (`FSAReportView.MakeQualityRows`). Условие: строка беты дошла до
   фита (`BetaKnotCaption` не пуст) и `FsaResult.BetaGammaPassRefusal != null`. Строка ставится НЕЗАВИСИМО от
   VIF: при VIF выше предела рядом стоит и его предупреждение — это два разных факта (бета ненадёжна; гамма
   грубее). Новые ключи `FSAReport_BetaKnotSecondRefusedRow`/`…Value` (`FSAReportView*.resx`):
   «Нуклиды с гамма-линиями посчитаны первым, более грубым заходом: второй заход не состоялся» | «грубее»;
   «Nuclides with gamma lines are computed by the first, coarser pass: the second pass failed» | «coarser».
   Подсказка строки — тот же текст шага узлов (`BetaKnotCaption`). Причина отказа в нём прежде печаталась
   именем члена перечисления в скобках («(NoFit)»); теперь хвост `FSAReport_BetaKnotSecondRefused` без `{0}`
   («; второй заход не состоялся, гамма-строки — из первого»), а под ним своей строкой — причина словами окна:
   `FSAReportView.SecondPassRefusalText` берёт тот же текст, каким сеанс называет такой отказ всего разбора
   (`FsaAnalysisSession.RefusalText`: «Полноспектральное разложение: подгонка не сошлась — проверьте
   энергетическую калибровку и состав»); имя, которого перечисление не знает, — как есть.
3. **Склонение** (`FSAReportView.AnchorRollbackCaption`): новый ключ `FSAReport_AnchorRollbackRowOne` — та же
   фраза Amber с «по {0} пику» / «on {0} peak»; выбор — `FSAReportView.OnePeakForm`: по-русски
   `n % 10 == 1 && n % 100 != 11` (1, 21, 31, 101 — «пику»; 11, 111 — «пикам»), иначе только 1.
4. **Находка экранной приёмки, исправлена на месте — последняя строка отчёта недоступна прокрутке**
   (`XPTable/Models/Table.cs`). Замер: диск Th-232 без матрицы в окне гида — колесо на 10 щелчков сдвигало
   таблицу на одну строку, низ строки «… рядом в библиотеке: Pb-214 (Ra-226) 351.9» оставался обрезан.
   Причина: предел прокрутки (`LargeChange` полосы) считался `VisibleRowCountExact` — числом строк, влезающих
   от ВЕРХНЕЙ строки; вверху «Отчёта FSA» — короткие строки состава, внизу — строки блока качества в три-пять
   строк текста. Правка: при переносе по словам предел — меньшее из прежнего и числа строк, целиком влезающих
   С КОНЦА таблицы (`VisibleRowCountFromBottom`, не меньше одной); предел только растёт. Тот же стол у окна
   пиков и окна результата (`EnableWordWrap`): там высоты строк почти ровные, предел тот же.
5. **Шесть снимков гида** пересняты конвейером AMBER239 (копия `pipe\` — `snap.ps1`/`uia.ps1`/`crop.ps1`/`hot.py`
   с путями на worktree), своя сборка, копия гидовой раскладки и конфига (`amber239\app\config`, спектры
   витрины диска Th-232 и Cs-137 в домике), каталог `config\device` — копия склада Amber (физика 30, только
   чтение её папки), как в AMBER259. Состояние окон — как на прежних снимках: AS80 диск, кривая «Th медальон»,
   режим FSA, внизу справа «Результат измерений».
   * Без щелчков: режим FSA — умолчанием `DefaultBackgroundMode` копии, активные вкладки — полями
     `ActiveContent` раскладки копии, кривая — `WM_KEYDOWN VK_DOWN` в список «Эффективн.» сообщением; окно
     копии уводилось в низ стопки окон (за экраном работали), снималось `PrintWindow` по своему окну.
   * ⚠ **Ширина левого дока** — `DockLeftPortion` 0.169 → 0.178 (панель 305 → 321 точку). При прежней ширине
     новый блок качества (две новые строки, подписи длиннее) не влезал: либо последняя строка обрезана снизу,
     либо таблица прокручена и скрыта верхняя строка Ac-228 — тогда на снимке есть «Ac-228 — сумм-пик», а
     самого Ac-228 нет, и это сбивает с толку. На 0.178 таблица влезает целиком без прокрутки в обоих языках. Цена: вырезка
     `panel-fsa` — 328×944 вместо 312×944; главные снимки — те же 1866×1033.
   * **`main-fsa-nomatrix`**: матрицы копии на время съёмки вынесены в сторону и возвращены (12 из 12, sha256);
     точка 4 — подсказка строки «Матрица отклика» с ценой разбора без матрицы (её не видит `PrintWindow`: снят
     экран в рамке окна с наведённой мышью, правый край подсказки — по правому краю значений, ничего не
     перекрыто). RU — на ширине 0.178, без прокрутки; EN — на прежней ширине 0.169, тоже без прокрутки
     (вторая съёмка EN с подсказкой на 0.178 не удалась: окно не встало поверх рабочего окна за экраном,
     а выводить его силой во время чужой работы не стал). У EN свои точки `data-hot-en`.
   * `docs/fsa.html`: точки `panel-fsa`, `main-fsa`, `main-fsa-nomatrix` (RU и EN) пересчитаны по выгрузке
     элементов UIA и строкам снимка, проверены наложением на снимки обоих языков; легенды — `q.l4`
     («Подсказка строки «Матрица отклика» — цена разбора без матрицы: …» вместо «Предупреждение с ценой «до
     −11 %»»), `q.l1` (отдельных строк рентгена Pb и Th без матрицы больше нет: «пик вылета SE-1588 и обратное
     рассеяние («не найдено»)»), `r.l3` (под галками «Нижняя граница разбора, кэВ»), `q.mxNo` в игрушечном
     FSA («красное предупреждение «до −11 %»» → «цена — в подсказке строки «Матрица отклика»»).
   * `python tools/docs_stamp.py` — штамп `IMGV` и `?v=` страниц обновлены, `--check` — 0; ссылки, `src`,
     снимки обоих языков, число точек = пунктам легенд (`links.py` полосы AMBER254) — 0 замечаний.
6. **Пробы** (`FsaReportViewProbe`, четыре новые сцены, обе культуры, ожидания рукой):
   * отказ второго захода при VIF в пределе — красная строка, её подсказка = `BetaKnotCaption`, причина
     словами окна своей строкой, имени «NoFit» нет, признак `Warning`; контроли: блок без строки (прежнее
     поведение `a284`) — отказ; сцена без отказа — строки нет, подброшенная строка — отказ; VIF выше предела +
     отказ — обе красные строки;
   * откат по 1, 2, 3, 5, 11, 12, 21, 22, 101, 111 пику: RU «пику» ровно у 1, 21, 101; EN «peak» только у 1;
     окно с откатом по одному пику — «по 1 пику» в ячейке; контроль: прежнее «по 1 пикам» — отказ;
   * строка «Обратное рассеяние» в таблице окна — «не найдено»/«not found», подсказка полным текстом; контроли:
     прежние «не найден»/«искал этот нуклид» с ячейками не совпадают, подброшенная прежняя подсказка поймана
     сканером прежних фраз (в него добавлены «искал этот нуклид», «looked for this nuclide»);
   * прокрутка: окно 320×700, поле данных 189 точек, строки 318 (RU) / 282 (EN) — на упоре прокрутки строки от
     верхней видимой до последней 165 / 182 точки ≤ 189; контроль: прежний предел оставлял 259 / 238 > 189.

### Приёмка

* Сборка Debug (`bin\Debug_a284b`, `/restore`, `/p:GenerateManifests=false`) — код 0; `build_all.ps1` — код 0,
  383 пробы.
* **`FsaReportViewProbe`** (AS80_Th232Medal, контроль AS80_Cs137_0cm) — **ok 579, отказов 0**, «ВСЕ СОШЛИСЬ»
  (у `a284` — 502).
* `check_resx.py`, `check_resx_designer.py`, `check_resx_letters.py`, `check_resx_zorder.py` — код 0; переводы
  строк правленых файлов посчитаны байтами — одиночных LF 0, BOM как был.
* `check_fsa_docs.py` — код 0; `docs_stamp.py --check` — 0; `links.py` — 0.
* `check_all.py` — 42 зелёных из 47, красных 5, все стендовые (данных вне git нет в worktree):
  `check_corpus_coverage`, `check_declared_base`, `check_corpus_generator`, `check_fsa_showcase`,
  `check_bq_summing`; `check_corpus_scenes` зелен (склад матриц корпуса скопирован в worktree для P195).
  `check_fsa_report_view` — 4 из 4 (с пробой прокрутки).
* **Экран** — своя сборка на КОПИИ конфига Amber (опись `%AppData%\BecqMoni` до и после: 30 файлов, путь,
  длина, sha256 и время — совпали), RU и EN: сеанс копии с рядом Th-232 и откатом шкалы по одному пику — RU
  «Оставлена шкала файла: подстройка по 1 пику (+1.58 %) ухудшила подгонку …», EN «File scale kept:
  adjusting it on 1 peak …»; RU прокручен колесом до упора — последняя строка видна целиком; EN влезает без
  прокрутки. Окно гида без матрицы — «Обратное рассеяние | не найдено» / «Backscatter | not found». Прежних
  фраз на снимках нет. Снимки приёмки — на диске полосы (`D:\BqMoni_Claude\a284b\shots\`, не в git):
  `amber_ru_1/2`, `amber_en_1/2`, `ru_nomx_panel1/2` (обрезанный низ до и после правки XPTable),
  `ov2_*` (точки поверх снимков гида).

### Остаток, не сделанный в этой полосе

* `docs/result.html` и вырезка `panel-result.png` (из прежнего `main-fsa`) держат числа ПРЕЖНИХ снимков:
  `d.t2` — «На снимке без матрицы Th-232 вышел 1085.09 вместо 965.18 Бк — разница 12 %», макет `ROWS`/`ROWS2`
  (K-40 106.36 / 215.19, Th-232 965.18 / 1085.09) и `R` калькулятора. Новые снимки: с матрицей Th-232
  986.49 ± 12.68, K-40 ниже предела (ND); без матрицы Th-232 926.64 ± 7.58, K-40 143.33 — без матрицы на 6 %
  НИЖЕ, а не на 12 % выше. В шесть заданных снимков не входит; правка — `d.t2`, `ROWS`, `ROWS2`, `R` в обоих
  языках и пересъёмка `panel-result` из нового `main-fsa` (вырезка 1397, 598, 469, 410).
* Витрина FSA (`check_fsa_showcase`) не перемерялась — стенд-красный (склада витрины в worktree нет).
  Ожидаемые расхождения — только полоса `SCREEN`: «не найдено» у не выделившегося рассеяния, «по 1 пику» у
  отката по одной опоре, красная строка отказа второго захода там, где он был.
