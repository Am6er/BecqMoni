using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace FsaStackShot
{
    /// <summary>
    /// Снимок стека FSA НАСТОЯЩИМ кодом отрисовки — чтобы смотреть глазами на
    /// то, что увидит человек, а не на пересказ.
    ///
    /// Рисует `EnergySpectrumView.ShowFsaOverlay` отражением: вид собирается
    /// без формы, поля вьюпорта выставляются вручную, готовое разложение
    /// кладётся прямо в сеанс вида (`FsaAnalysisSession`, `A145` этап 2).
    /// Своего рисования здесь нет НАРОЧНО —
    /// проба, рисующая по своим правилам, показала бы не то, что приложение.
    ///
    /// (`A145`, этап 3) Таблицы состава на графике больше нет: под панелью
    /// курсора вид печатает только короткую строку состояния
    /// (`DrawFsaStatus`), а перечень строк живёт в окне отчёта
    /// `FSAReportView`. Поэтому справа от стека в тот же PNG кладётся снимок
    /// настоящего окна отчёта (`DrawToBitmap`) на том же сеансе — стек и
    /// таблица на одной картинке, как их видит человек.
    ///
    ///   fsastackshot --spectrum=X.xml [--efficiency=Цилиндр] [--out=stack.png]
    ///                [--infer | --sample=241AM,44TI,152EU,137CS [--chain=Th-232,U-238u]]
    ///                [--no-equilibrium] [--no-matrix] [--lib-dump]
    ///                [--set=Ra-226] [--lines=Esc-I] [--select=320..380]
    ///                [--no-atomic] [--no-backscatter] [--refit-z=0]
    ///                [--refit-z-rel=0.1]
    ///                [--no-drift] [--no-anchor] [--gain-steps=N] [--offset-steps=N]
    ///                [--knots=4] [--huber=3]
    ///                [--calculating] [--spoil=manager]
    ///                [--from=200] [--to=700] [--ceiling=2000] [--width=1400]
    ///                [--scale=pow] [--pow=4] [--dump=curves.csv]
    ///                [--rates=rates.csv] [--screen] [--shield=82,74] [--tie=0.9] [--tie-lines]
    ///                [--plant-tail] [--plant-spread] [--tail-as-residual] [--tail-to-continuum] [--plant-grey-floor]
    ///                [--no-limit] [--limit-z=1000] [--matrix-layer=All|Peak|Compton|EscapeAnnihilation|
    ///                 EscapeXrayK|EscapeAnnihilationDouble|EscapeXrayL]
    ///
    /// `--matrix-layer=` (`AMBER45`, П104 18.09.2026) — положение комбо «Matrix
    /// layer» окна отчёта: «All» (умолчание — стопка как есть, снимок побитово
    /// прежний) или один канал матрицы отклика (`FsaMatrixLayer`). Ставится
    /// виду ТЕМ ЖЕ полем, каким его ставит окно через документ
    /// (`EnergySpectrumView.FsaMatrixLayer`), и окну отчёта на снимке
    /// (`FSAReportView.RequestedMatrixLayer`) — комбо на PNG стоит в том же
    /// положении. После отрисовки печатается `MATRIX_LAYER\t<просили>\t<собран>\t<слоёв
    /// нарисовано>\t<имена>`: «собран» — режим, в котором вид собрал кадр
    /// (без матрицы канал недостижим и здесь стоит `All`), и
    /// `MATRIX_LAYER_COMBO\t<enabled>\t<пункт>\t<подсказка>` — состояние комбо на
    /// снятом окне. `--dump=` в режиме канала выгружает НАРИСОВАННУЮ стопку:
    /// столбцы слоёв — только слои с каналами, значения — кривые канала, взятые
    /// у вида (`fsaStackCurves`), а не пересказ; `model`, `net`, `fit` — как
    /// были (полная модель: от неё идёт лента невязки и в этом режиме).
    ///
    /// `--no-limit` / `--limit-z=` (П63, `S171`, второе правило) — предел
    /// неизмеримого члена ряда в режиме без связки
    /// (`FsaAnalyzer.ChainLimitByExpectedZ` / `ChainLimitZ`): `--no-limit` —
    /// правило выключено (плечо А); `--limit-z=` — подменённый порог ожидаемой
    /// значимости для положительного контроля (заведомо высокий обязан сделать
    /// пределом и члены с собственной линией); без ключей порог — у отсева.
    /// После разбора печатаются приговоры по каждому судимому члену каждого
    /// круга: `LIM\t<член>\t<опорный>\t<ожид. z>\t<z>\t<порог>\t<круг>\t<limit|below|free>`
    /// (`limit` — снят в этом круге, `below` — ниже порога, но снят не он: за
    /// круг снимается один, с наименьшей ожидаемой значимостью, и остальные
    /// судятся снова после перефита);
    /// у `--rates=` последний столбец `unmeasurable_at` — опорный член, при
    /// амплитуде которого кандидат снят в предел (пусто — не этим правилом).
    ///
    /// `--tie=` (П60, `S171`) — порог привязки вырожденного члена ряда в режиме
    /// без связки (`FsaAnalyzer.ChainTieShare`; 0 — гейт выключен, плечо А).
    /// После разбора печатаются приговоры гейта по КАЖДОМУ судимому члену:
    /// `TIE\t<член>\t<партнёр>\t<доля по Шуру>\t<парная доля>\t<tied|free>` —
    /// меры от порога не зависят, зависит лишь приговор, так что развёртка по
    /// порогу читается из одного прогона; строки `ROW` несут шестым полем
    /// партнёра привязки («-» — своя амплитуда), у `--rates=` столбец `tied_to`.
    ///
    /// `--shield=` (П59) — Z элементов ЗАЩИТЫ И ОБВЯЗКИ при объявленном составе
    /// (`FsaSampleSpec.ShieldElements`, как `materials.csv` корпуса у
    /// `CorpusFsaProbe`): образ флуоресценции защиты («Xray-Pb»). В приложении
    /// этот образ появляется только по подписи «X-ray Pb» найденного пика
    /// (`FsaCompositionInference.Candidates`); при `--sample=`/`--chain=` его
    /// иначе не задать. Заведён, чтобы проверить, чей рентген 72…88 кэВ в
    /// спектре радонового фильтра.
    ///
    /// `--rates=` (П59, `AMBER27`) — ЧИСЛА разбора по компонентам в csv: скорость
    /// счёта `CountRate` (амплитуда / живое время — распадов в секунду, поскольку
    /// эффективность сидит в отклике), значимость z, порог решения и предел
    /// обнаружения (1/с), пиковые отсчёты, доля слоя, ряд связки и ряд
    /// происхождения — по `FsaResult.Components`; и то же по КАЖДОМУ кандидату
    /// `CharacteristicLimits` (обнаружен / предел). Заведено для проверки
    /// неравновесного разбора отношениями активностей членов ряда: доля слоя в
    /// процентах на экране про активность не говорит, а без чисел вердикт
    /// «корректен» — слово. ⚠ У члена ряда, объявленного через `--chain=` без
    /// равновесия, линии образа взвешены накопленной долей ветвления
    /// (`FsaSampleLibrary`, `line[1] * member.Value`), то есть `CountRate` — в
    /// единицах распадов РОДИТЕЛЯ ряда: у Tl-208 это A(Tl-208)/0.3594.
    ///
    /// `--plant-tail` (`S173`) — ПОДСАДКА только для пробы, действует вместе с
    /// `--tail-as-residual`: отвязанные хвосты матричных образов из невязки
    /// (`FsaResult.UntiedTail`) возвращаются в подложку и разносятся по
    /// слоям, как было до 14.09.2026. Без `--tail-as-residual` `UntiedTail`
    /// пуст, и подсадка — пустое действие, о чём строка `отвязанный хвост` и
    /// говорит. Числа с ключом в журнал не годятся, о чём проба говорит вслух.
    ///
    /// `--tail-as-residual` (`AMBER30`, П70) — плечо Б: правило `S173` как в П68
    /// (`FsaAnalyzer.UntiedTailAsResidual = true`) — хвост в `UntiedTail`, мимо
    /// верха стека, лентой невязки. Положительный контроль: с ключом числа
    /// обязаны совпасть с `025a65a9` (яма 56–100 кэВ на цезии в домике); фит
    /// (`--rates=`, кроме `share_pct`) — побитово в обоих плечах.
    ///
    /// `--tail-to-continuum` (`S175`, П75) — ПОДСАДКА только для пробы:
    /// отвязанные хвосты, лежащие у своих образов (`FsaComponentResult.TailCurve`,
    /// решение Amber 14.09.2026 «Чини S174 так, чтобы выглядело физически
    /// корректным»), перекладываются в подложку `Continuum` и разносятся по
    /// слоям правилом `S76` — картинка `a6f2b227` (до П68: хвост в подложке,
    /// пола разноса нет). Вместе с `--plant-grey-floor` — картинка П70
    /// (`d5c94425`: провал в слое Cs-137, серый бугор 14.2 М отсч.). Верх стека
    /// тот же, доли пересчитаны правилами результата. Положительный контроль:
    /// плечо с подсадкой обязано совпасть с тем HEAD по долям и невязке до
    /// знака, а договор `FsaDoubleCountProbe` §6 на такой подсадке — отказать.
    /// ⚠ Не бит в бит: хвост складывается в подложку после шапок сплайна, а не
    /// до, — последние разряды double, F3 их не видит.
    ///
    /// `--plant-grey-floor` (`S175`, второе решение Amber 14.09.2026 «В слои
    /// образов по S76 везде») — ПОДСАДКА только для пробы: пол разноса подложки
    /// ставится на порог доверия матрицы (`FsaResult.ContinuumSpreadFloorChannel`
    /// тем же `FsaAnalyzer.FloorChannel`), и сплайн ниже порога уходит в серый
    /// слой — п. 1 `S174` (П69), отменённый этим решением. Положительный
    /// контроль: серый слой ниже порога появляется, доля носителя падает,
    /// договор `FsaDoubleCountProbe` §7 отказывает; фит тот же.
    ///
    /// `--plant-spread` (`S174` п. 2) — ПОДСАДКА только для пробы: пол невязки
    /// снимается (`ResidualFloorChannel` = 0; пол разноса в приложении и так
    /// 0) — невязка считается по всей полосе фита, как было до решения Amber
    /// 14.09.2026 «Только в диапазоне прибора». Числа с ключом в журнал не
    /// годятся.
    ///
    /// Строка `пороги отображения` печатает оба пола каналом и кэВ, а
    /// `серый слой` — сколько отсчётов серого слоя лежит ниже пола разноса (в
    /// приложении 0) и сколько выше последней линии; в `--rates=` то же —
    /// разделом `grey` (ПОСЛЕ прежних строк, шапка та же).
    ///
    /// `--screen` — строки окна отчёта КАК ОНИ ПОКАЗАНЫ (текст ячеек XPTable и
    /// подсказка), `SCREEN\t<имя>\t<значение>\t<подсказка>`; читается с того же
    /// окна, что снято на PNG. Заведён под ~~`AMBER6`~~: свёрнутая строка
    /// «не определяются (N)» обязана называть свёрнутых в подсказке, а по
    /// картинке подсказки не видно.
    ///
    /// `--select=<от>..<до>` — выделенная область в кэВ, ровно то, что человек
    /// тянет мышью. Заведён `A27`: заливка выделения бралась у ПОЛНОГО спектра,
    /// а в режиме FSA на экране нарисован спектр за вычетом фона, — расхождение
    /// видно только картинкой, и проверять его глазами в окне значило бы каждый
    /// раз поднимать окно.
    ///
    /// `--ceiling` подрезает шкалу отсчётов: без него высокие пики забирают всё
    /// поле, и мелкая структура (сумм-пики) сливается с осью.
    ///
    /// `--scale=` — вертикальная шкала: `lin` (умолчание), `pow` (корень
    /// степени `--pow=`, ровно кнопка «POW» под графиком) или `log`. Заведён
    /// `S88`: человек смотрит на спектр в шкале POW 4, где видна вся мелочь
    /// внизу, а снимок в линейной шкале показывает почти пустое поле — по нему
    /// нельзя ни подтвердить наблюдение, ни опровергнуть.
    ///
    /// `--dump=` — кривые ПО КАНАЛАМ в csv: измерение за вычетом фона, модель,
    /// сырой сплайн (`continuum_raw`, `T103`), отвязанный хвост (`untied_tail`,
    /// `S173` — в модель и слои не входит, лежит в невязке), кривая ФИТА
    /// (`fit`, `S174` — то же измерение БЕЗ подрезки отрицательного, по ней
    /// считаны χ² и число невязки; имя столбца — как у `CorpusFsaProbe
    /// --dump-curves`, `T254`) и по колонке на каждый слой стека. Спор
    /// «модель кривая или спектр такой» картинкой не решается: на ней обе
    /// кривые в пикселе друг от друга. Показное измерение берётся у ВИДА
    /// (`fsaNetSpectrum`), а не считается заново, — выгружено то же, что
    /// нарисовано; кривая фита — у результата (`FitSpectrum`).
    ///
    /// `--set=` — тот самый выбор «Use set:» из панели поиска пиков. Без него
    /// проба берёт `ActiveSet` = null, то есть ВСЕ нуклиды, и подписи пиков
    /// расходятся с экраном человека; а подписи задают состав (`S57`), так что
    /// снимок получается не про тот спектр. Имя сверяется с именами наборов
    /// `NuclideDefinitionManager.NuclideSets`, промах — отказ, а не молчание.
    ///
    /// `--lines=` — линии ОДНОГО образа с весами и нуклидом-родителем. Заведён
    /// под `S80`: по картинке видно, что лента образа стоит не там, где ей
    /// положено, а чем именно она набрана — по картинке не видно.
    ///
    /// `--sample=` — ОБЪЯВЛЕННЫЙ состав пробы, нуклиды `nucid` через запятую
    /// («241AM,44TI,152EU,137CS», как в графе `nuclides` манифеста корпуса).
    /// Образы собирает `FsaSampleLibrary` ИЗ БАЗЫ, тем же правилом, что у
    /// корпусного прогона (`CorpusFsaProbe.SpecOf`, `--lib=sample`); поставочная
    /// `NuclideDefinition.xml` в состав не входит вовсе. Заведён 12.09.2026 по
    /// слову Amber («НЕ СМОТРЕТЬ в поставочную! Возьми нуклиды из базы! Нужен
    /// FSA Stack!»): на корпусной смеси AmTiCsEu путь `--infer` вывел из
    /// подписей Pb-210 / U-235 / Th-232 — в поставочной библиотеке Ti-44 нет, и
    /// вывод из подписей его не увидит никогда, — а путь `--set=` есть
    /// поставочная библиотека на корпусе, что запрещено (`S56`). Снимок при этом
    /// остаётся снимком ПРИЛОЖЕНИЯ: отрисовка, анализатор, матрица и отчёт — те же.
    /// (`T257`, 13.09.2026) Спецификация — общим входом `FsaSampleSpec.Declared`;
    /// поставочный `NuclideDefinition.xml` при `--sample=` НЕ ЧИТАЕТСЯ (одиночке
    /// менеджера подложен пустой список, гейт в конце — код 12, если список
    /// оказался непустым), и оснастка корпуса без этого файла пробе годится; под
    /// снимком — полоса с надписью пробы «Состав ОБЪЯВЛЕН … (nucdb: …)», потому
    /// что переключатель «Источник состава» в окне отчёта — состояние
    /// приложения, и положения «объявлен» у него нет (решение Amber 13.09.2026:
    /// «Надпись от пробы вне графика»).
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T101`) ПОСТАВОЧНАЯ ПОЛОСА — СНИМАЕТСЯ ДО ВСЕГО. Отражение её не
            // видит (статика, читаемая обоими концами в момент обращения), и
            // эталон для строки «против поставочного разбора изменено» обязан
            // родиться с нею; снятая позже, она уже могла быть уведена
            // конфигурацией. Тот же приём и тот же довод, что у `CorpusFsaProbe`
            // (`S101`, `T65`).
            // (`T243`) Само правило живёт ОДНИМ местом — довеском
            // `FsaTuningReport.cs`; здесь только два вызова.
            FsaTuningReport.Snapshot();

            string spectrumPath = null, efficiencyName = null, outPath = "stack.png";
            string setName = null, linesOf = null, selectKev = null;
            // Развязка гейтов и приборных образов — `S81`: «кто чью полку
            // забирает» иначе не разделить. `refitZ` NaN — не трогать умолчание
            // анализатора, а не «поставить ноль».
            bool atomic = true, backscatter = true;
            double refitZ = double.NaN;
            // (`A266`) Относительная доля порога отсева — ОТДЕЛЬНЫМ рычагом, а
            // не подменой умолчания: иначе её эффект не отделить от прочих
            // правок того же дня. NaN — не трогать умолчание анализатора.
            double refitZRel = double.NaN;
            // (`S171`) Порог привязки вырожденного члена; NaN — умолчание анализатора.
            double tieShare = double.NaN;
            // (`S171`) Мера гейта привязки по линиям (`--tie-lines`); умолчание анализатора — по колонкам.
            bool tieLines = false;
            // (`S171`, второе правило) Предел неизмеримого члена: `--no-limit` — выключить;
            // `--limit-z=` — подменённый порог (NaN — порог отсева, умолчание анализатора).
            bool limitRule = true;
            double limitZ = double.NaN;
            // (S69/S70) Ветка галки «состав из баз»: библиотеку собирает
            // `FsaSampleLibrary` по выведенному составу — ровно то, что видит
            // человек с включённой галкой. Без ключа остаётся прежний путь.
            bool infer = false, equilibrium = true, needMatrix = true, libDump = false;
            // Объявленный состав (`--sample=`): null — ключа не было. Пустой
            // список — отказ ниже, а не молчаливый разбор без единого образа.
            List<string> sampleNuclides = null;
            // (`AMBER22`, П56 14.09.2026) Ряды ОБЪЯВЛЕННОГО состава метками манифеста
            // (`Th-232`, `U-238u`, `Ra-226`, `U-235`…, как `FsaCascadeProbe --chain=`):
            // одними nucid ряд не задать — члены пошли бы порознь, без связки
            // равновесия, и разложение вырождалось бы (П42 `noeq`). Метка,
            // которой `FsaSampleChain.FromLabel` не знает, — отказ, не пропуск.
            List<string> sampleChains = null;
            // Порча для положительного контроля гейта `AMBER19` (как
            // `CorpusFsaProbe --spoil=manager`): пустышку одиночке НЕ
            // подкладывать. При поставочном файле рядом список поднимется
            // настоящим, и гейт в конце обязан дать код 12; без файла — упасть
            // до счёта, как падала проба до `T257` (3).
            bool spoilManager = false;
            int gainSteps = 0, offsetSteps = 0;
            // (`AMBER17`) Плечо A/B: привязка шкалы по пикам выключена.
            bool anchor = true;
            double knots = double.NaN;
            // (`AMBER22`, П29) Порог Хубера в сигмах, как `CorpusFsaProbe --huber=`:
            // 0 выключает перевзвешивание; NaN — не трогать умолчание анализатора.
            double huberM = double.NaN;
            bool showCalculating = false;
            double fromKev = 0.0, toKev = 0.0, ceiling = 0.0;
            int width = 1400, height = 700;
            string scale = "lin", dumpPath = null;
            // (П59, `AMBER27`) Числа разбора по компонентам и строки окна отчёта
            // как показаны — см. шапку.
            string ratesPath = null;
            bool plantTail = false;
            bool plantSpread = false;
            bool tailToContinuum = false;
            bool plantGreyFloor = false;
            // (`AMBER30`, П70) Плечо Б: хвост лентой невязки, как в П68.
            bool tailAsResidual = false;
            bool screenRows = false;
            var shieldZ = new List<int>();
            double pownum = 4.0;
            // (`AMBER45`) Слой матрицы отклика на снимке; умолчание — «All», стопка как есть.
            FsaMatrixLayer matrixLayer = FsaMatrixLayer.All;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--matrix-layer=", StringComparison.Ordinal))
                {
                    string name = a.Substring(15);
                    if (!Enum.TryParse(name, true, out matrixLayer) || !Enum.IsDefined(typeof(FsaMatrixLayer), matrixLayer))
                    {
                        Console.Error.WriteLine("--matrix-layer={0}: неизвестный слой; известные: {1}", name,
                                                string.Join(", ", Enum.GetNames(typeof(FsaMatrixLayer))));
                        return 2;
                    }
                }
                else if (a.StartsWith("--efficiency=", StringComparison.Ordinal)) efficiencyName = a.Substring(13);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--from=", StringComparison.Ordinal)) fromKev = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--to=", StringComparison.Ordinal)) toKev = double.Parse(a.Substring(5), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--ceiling=", StringComparison.Ordinal)) ceiling = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a == "--infer") infer = true;
                else if (a.StartsWith("--sample=", StringComparison.Ordinal))
                {
                    sampleNuclides = new List<string>();
                    foreach (string nucid in a.Substring(9).Split(','))
                    {
                        if (nucid.Trim().Length > 0) sampleNuclides.Add(nucid.Trim().ToUpperInvariant());
                    }
                }
                else if (a.StartsWith("--chain=", StringComparison.Ordinal))
                {
                    sampleChains = new List<string>();
                    foreach (string label in a.Substring(8).Split(','))
                    {
                        if (label.Trim().Length > 0) sampleChains.Add(label.Trim());
                    }
                    if (sampleNuclides == null) sampleNuclides = new List<string>();
                }
                else if (a == "--spoil=manager") spoilManager = true;
                else if (a == "--no-matrix") needMatrix = false;
                else if (a == "--lib-dump") libDump = true;
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--lines=", StringComparison.Ordinal)) linesOf = a.Substring(8);
                else if (a == "--no-atomic") atomic = false;
                else if (a == "--no-backscatter") backscatter = false;
                else if (a.StartsWith("--refit-z-rel=", StringComparison.Ordinal))
                    refitZRel = double.Parse(a.Substring(14), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--refit-z=", StringComparison.Ordinal))
                    refitZ = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a == "--no-equilibrium") equilibrium = false;
                else if (a.StartsWith("--tie=", StringComparison.Ordinal))
                    tieShare = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a == "--tie-lines") tieLines = true;
                else if (a == "--no-limit") limitRule = false;
                else if (a.StartsWith("--limit-z=", StringComparison.Ordinal))
                    limitZ = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--scale=", StringComparison.Ordinal)) scale = a.Substring(8);
                else if (a.StartsWith("--pow=", StringComparison.Ordinal)) pownum = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--dump=", StringComparison.Ordinal)) dumpPath = a.Substring(7);
                else if (a.StartsWith("--rates=", StringComparison.Ordinal)) ratesPath = a.Substring(8);
                else if (a == "--screen") screenRows = true;
                else if (a == "--plant-tail") plantTail = true;
                else if (a == "--plant-spread") plantSpread = true;
                else if (a == "--tail-as-residual") tailAsResidual = true;
                else if (a == "--tail-to-continuum") tailToContinuum = true;
                else if (a == "--plant-grey-floor") plantGreyFloor = true;
                else if (a.StartsWith("--shield=", StringComparison.Ordinal))
                {
                    foreach (string z in a.Substring(9).Split(','))
                    {
                        if (z.Trim().Length > 0) shieldZ.Add(int.Parse(z.Trim(), CultureInfo.InvariantCulture));
                    }
                }
                else if (a.StartsWith("--select=", StringComparison.Ordinal)) selectKev = a.Substring(9);
                else if (a.StartsWith("--gain-steps=", StringComparison.Ordinal))
                    gainSteps = int.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--offset-steps=", StringComparison.Ordinal))
                    offsetSteps = int.Parse(a.Substring(15), CultureInfo.InvariantCulture);
                else if (a == "--no-drift") { gainSteps = 1; offsetSteps = 1; }
                else if (a == "--no-anchor") anchor = false;
                else if (a == "--calculating") showCalculating = true;
                else if (a.StartsWith("--knots=", StringComparison.Ordinal))
                    knots = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--huber=", StringComparison.Ordinal))
                    huberM = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--width=", StringComparison.Ordinal)) width = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--height=", StringComparison.Ordinal)) height = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            if (sampleNuclides != null && sampleNuclides.Count == 0 && (sampleChains == null || sampleChains.Count == 0))
            {
                Console.Error.WriteLine("--sample= пуст: нужны nucid через запятую, например --sample=241AM,137CS (и/или --chain=Th-232)");
                return 2;
            }

            var chainSpecs = new List<FsaSampleChain>();
            if (sampleChains != null)
            {
                foreach (string label in sampleChains)
                {
                    FsaSampleChain chain = FsaSampleChain.FromLabel(label);
                    if (chain == null)
                    {
                        Console.Error.WriteLine("--chain={0}: неизвестный ряд; известные: {1}", label,
                                                string.Join(", ", FsaSampleChain.KnownLabels));
                        return 2;
                    }

                    chainSpecs.Add(chain);
                }
            }

            // Два источника состава разом — не «объединить», а отказ: иначе не
            // сказать, чей состав на снимке.
            if (sampleNuclides != null && (infer || setName != null))
            {
                Console.Error.WriteLine("--sample= не сочетается с --infer и --set=: состав берётся из одного места");
                return 2;
            }

            // (`T257` (3), `AMBER19`) При ОБЪЯВЛЕННОМ составе поставочный список
            // не нужен вовсе — и подниматься не должен: в оснастке корпуса файла
            // `config\NuclideDefinition.xml` нет по правилу, и безусловный
            // `GetInstance()` здесь ронял пробу до счёта (П14 12.09.2026). Но
            // код ПОКАЗА приложения зовёт менеджер сам — конструктор
            // `EnergySpectrumView` (исключение глотает) и отпечаток сеанса
            // `FsaAnalysisSession.BuildStamp` → `NuclideSetStamp` (не глотает),
            // а без отпечатка окно отчёта заказало бы свой счёт поверх
            // подложенного. Поэтому при `--sample=` одиночке подкладывается
            // ПУСТОЙ список (ноль определений, ноль наборов) — то же, что
            // `EnergySpectrumView.CreateNuclideDefinitionManagerFallback` делает
            // для вида: файл не открывается, а состав снимка из него взять
            // неоткуда. Гейт в конце (`SuppliedLibraryGate`) это и проверяет.
            if (sampleNuclides != null && !spoilManager)
            {
                PrimeEmptySuppliedLibrary();
            }
            else if (spoilManager)
            {
                Console.WriteLine("--spoil=manager: пустышка одиночке НЕ подложена — контроль гейта AMBER19");
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = sampleNuclides != null
                ? null
                : NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            if (efficiencyName != null && !AttachEfficiency(rd, efficiencyName))
            {
                return 2;
            }

            // Набор — ДО поиска пиков: им подписываются пики, а подписи задают
            // выведенный состав (`S57`). Прочитанный после, он не изменил бы
            // ничего и врал бы молча.
            if (setName != null && !SelectSet(nuclides, setName))
            {
                return 2;
            }

            // (S82) Настройки, которыми проба СЕЙЧАС ищет пики, — строкой, до
            // всякого счёта. Вопрос «то же ли самое видит человек» иначе не
            // задать: панель поиска пиков показывает SNR, допуск и диапазон, а
            // проба до 19.08.2026 брала их молча и не называла.
            FWHMPeakDetectionMethodConfig used = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            Console.WriteLine("SETUP\tSNR={0}\tдопуск={1}\tдиапазон={2}…{3} кэВ\tмёртвое={4} с",
                              used != null ? used.Min_SNR.ToString("G", CultureInfo.InvariantCulture) : "?",
                              used != null ? used.Tolerance.ToString("G", CultureInfo.InvariantCulture) : "?",
                              used != null ? used.Min_Range.ToString("G", CultureInfo.InvariantCulture) : "?",
                              used != null ? used.Max_Range.ToString("G", CultureInfo.InvariantCulture) : "?",
                              rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null
                                  ? rd.DeviceConfig.InputDeviceConfig.DeadTime().ToString("G4", CultureInfo.InvariantCulture)
                                  : "?");

            // (`T257` (3)) Поиск пиков с подписями поставочного списка — только
            // там, где состав идёт от подписей. При `--sample=` этот проход
            // всё равно выбрасывался (ниже пики ищутся заново с подписями из
            // базы), а менеджера для него нет.
            List<Peak> peaks = null;
            if (sampleNuclides == null)
            {
                peaks = new PeakDetector().DetectPeak(
                    rd, BackgroundMode.Invisible, SmoothingMethod.None,
                    nuclides.ActiveSet, nuclides.NuclideDefinitions);
                Console.WriteLine("SETUP\tнайдено пиков: {0}", peaks.Count);
            }

            List<FsaComponent> library;
            // (`T257` (1)) Надпись ПРОБЫ под снимком — откуда состав. Решение
            // Amber 13.09.2026, вопросником: «Надпись от пробы вне графика»
            // (третьего положения переключателя в приложении не заводить).
            string caption = null;
            if (infer)
            {
                FsaCompositionInference.Report report;
                FsaSampleSpec spec = FsaCompositionInference.Infer(peaks, rd, out report);
                spec.Equilibrium = equilibrium;
                spec.AtomicXray = atomic;
                library = FsaSampleLibrary.Build(spec);
                Console.WriteLine("состав: " + report);
                peaks = new PeakDetector().DetectPeak(
                    rd, BackgroundMode.Invisible, SmoothingMethod.None,
                    null, FsaSampleLibrary.AsDefinitions(library));
            }
            else if (sampleNuclides != null)
            {
                // Объявленный состав — порядок тот же, что у корпусного прогона
                // (`S56`, первый постулат): сперва библиотека ИЗ БАЗЫ, потом
                // поиск пиков с подписями из неё же. Подписи задают то, что
                // человек прочтёт над пиками, и брать их из поставочной, когда
                // состав объявлен, значило бы подписать Ti-44 чужим именем.
                // (`T257` (2)) Спецификация — ОБЩИМ входом приложения
                // (`FsaSampleSpec.Declared`, П11 12.09.2026), а не своей копией:
                // их было пять, и они уже расходились.
                FsaSampleLibrary.Report built;
                FsaSampleSpec spec = FsaSampleSpec.Declared(rd, chainSpecs, sampleNuclides, equilibrium, atomic);
                // (П59) Защита — ключом `--shield=`, как из `materials.csv` у
                // корпусной пробы; без ключа список пуст, как и было.
                foreach (int z in shieldZ)
                {
                    if (!spec.ShieldElements.Contains(z)) spec.ShieldElements.Add(z);
                }

                library = FsaSampleLibrary.Build(spec, out built);
                Console.WriteLine("состав объявлен: {0}; ряды: {1}; {2}", string.Join(", ", sampleNuclides),
                                  sampleChains == null ? "нет" : string.Join(", ", sampleChains), built);
                peaks = new PeakDetector().DetectPeak(
                    rd, BackgroundMode.Invisible, SmoothingMethod.None,
                    null, FsaSampleLibrary.AsDefinitions(library));
                Console.WriteLine("SETUP\tнайдено пиков: {0} (подписи из базы)", peaks.Count);

                // Окно отчёта на снимке показывает переключатель «Источник
                // состава» ПРИЛОЖЕНИЯ, у которого положения «объявлен» нет, —
                // и при `--sample=` оно врало бы о механизме. Надпись — от
                // пробы, ВНЕ графика (полосой под снимком), приложение не
                // трогается. Список — образы распада, как их собрала база.
                var decays = new List<string>();
                foreach (FsaComponent c in library)
                {
                    if (c.Kind == FsaComponentKind.Single || c.Kind == FsaComponentKind.Chain)
                    {
                        decays.Add(c.Name);
                    }
                }

                caption = "Состав ОБЪЯВЛЕН ключом --sample= (nucdb: " + string.Join(", ", sampleNuclides)
                          + (sampleChains == null ? "" : "; ряды --chain= " + string.Join(", ", sampleChains))
                          + " → " + string.Join(", ", decays) + "); поставочная библиотека не читалась.\n"
                          + "Переключатель «Источник состава» в окне отчёта — состояние приложения, "
                          + "положения «объявлен» у него нет; к этому снимку он не относится.";
            }
            else
            {
                library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);
            }
            // Состав библиотеки построчно — мерка приёмки `S70`: связка
            // равновесия не смеет убирать ни одного компонента, кроме
            // слияния членов ряда, и проверяется это сравнением ДВУХ таких
            // распечаток, а не рассуждением.
            if (libDump)
            {
                foreach (FsaComponent c in library)
                {
                    Console.WriteLine("LIB	{0}	{1}	{2}", c.Name, c.Kind, c.Lines.Count);
                }
            }

            // (S80) Линии образа — с весом и нуклидом-родителем. Печатается ДО
            // разбора: вопрос «чем набрана эта лента» относится к ОБРАЗУ, а не
            // к тому, что из него вышло после фита.
            if (linesOf != null)
            {
                bool found = false;
                foreach (FsaComponent c in library)
                {
                    if (!string.Equals(c.Name, linesOf, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    found = true;
                    Console.WriteLine("линии образа {0} ({1}, всего {2}):", c.Name, c.Kind, c.Lines.Count);
                    foreach (FsaLine line in c.Lines)
                    {
                        Console.WriteLine("LINE\t{0}\t{1}\t{2}",
                                          line.Energy.ToString("F3", CultureInfo.InvariantCulture),
                                          line.Intensity.ToString("E4", CultureInfo.InvariantCulture),
                                          line.Nuclide);
                    }
                }

                if (!found)
                {
                    Console.Error.WriteLine("образа «{0}» в библиотеке нет", linesOf);
                }
            }

            if (library.Count == 0)
            {
                Console.Error.WriteLine("библиотека пуста");
                return 1;
            }

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(
                rd.Efficiency != null ? rd.Efficiency.Guid : null, out refusal, out fileFormat);
            if (matrix == null || rd.Efficiency == null || !rd.Efficiency.HasGeometry
                || !matrix.IsValidFor(rd.Efficiency.Geometry))
            {
                // ⛔ Молчать об этом нельзя: снимок без матрицы — ДРУГОЕ
                // разложение, и выдавать его за настоящее уже случалось. Ключ
                // `--no-matrix` заведён затем, чтобы смотреть на ЛЕГЕНДУ там,
                // где кривой у спектра нет вовсе, и отказ остаётся отказом,
                // пока о нём не попросили вслух.
                if (needMatrix)
                {
                    // ⛔ ОТКАЗ ОБЯЗАН НАЗЫВАТЬ ПРИЧИНУ (`A35`). «Матрицы нет или
                    // отпечаток не сошёлся» — три разных случая под одной
                    // фразой, и разбирать их приходилось догадками: файла нет /
                    // кривая у спектра без геометрии / файл есть, а геометрия
                    // разошлась с той, под которую он считан.
                    if (matrix == null)
                    {
                        // (`A50`) «файла нет» и «файл ЕСТЬ, но прежнего формата» —
                        // два разных случая с двумя разными лечениями, и до
                        // 03.09.2026 они печатались одной фразой. Теперь отказ
                        // называет себя сам (`ResponseMatrix.MatrixRefusal`).
                        string path = rd.Efficiency != null
                            ? ResponseMatrixStore.PathOf(rd.Efficiency.Guid)
                            : "(кривой у спектра нет)";
                        if (refusal == MatrixRefusal.OldFormat)
                        {
                            Console.Error.WriteLine(
                                "МАТРИЦА ПРЕЖНЕГО ФОРМАТА: в файле формат {0}, приложение читает {1} — пересчитать",
                                fileFormat, ResponseMatrix.FormatVersion);
                            Console.Error.WriteLine("  {0}", path);
                        }
                        else
                        {
                            Console.Error.WriteLine("матрицы нет ({0}): файла {1} не существует или он не читается",
                                                    refusal, path);
                        }
                    }
                    else if (rd.Efficiency == null || !rd.Efficiency.HasGeometry)
                    {
                        Console.Error.WriteLine("матрица есть, но у кривой спектра НЕТ ГЕОМЕТРИИ — сверять не с чем");
                    }
                    else
                    {
                        Console.Error.WriteLine("ОТПЕЧАТОК НЕ СОШЁЛСЯ:");
                        Console.Error.WriteLine("  у матрицы : {0}", matrix.Stamp);
                        Console.Error.WriteLine("  у геометрии спектра: {0}",
                                                ResponseMatrix.ComputeStamp(rd.Efficiency.Geometry,
                                                                            matrix.Options));
                    }

                    return 1;
                }

                Console.WriteLine("⚠ БЕЗ МАТРИЦЫ (--no-matrix): разложение другое, числа не сравнивать");
                matrix = null;
            }

            // Фон подаётся ТОТ ЖЕ, что в окне приложения. До 15.08.2026 здесь
            // стоял null, и снимок показывал разбор без вычитания фона, выдавая
            // его за настоящий: у `G1S_K40_Denta` это χ²/ndf 3.62 против 1.72.
            // (ключ снят по B6 15.08.2026, тот же спектр — `G1S24_K40_Denta120`)
            var analyzer = new FsaAnalyzer();
            // ⛔ Матрица кладётся ТЕМ ЖЕ кодом, что и в приложении
            // (`FsaMatrixBinding`, `AMBER12`): с нею едут вещество кристалла
            // (`S20`, иначе сумм-пики считаются не по свету) и признак защиты
            // (образ обратного рассеяния поверх матрицы). Пока проба ставила
            // это сама, признак защиты до неё не доехал, и снимки «до» и
            // «после» вышли побитово одинаковыми — правку было нечем принять.
            FsaMatrixBinding.Bind(analyzer, rd.Efficiency != null ? rd.Efficiency.Geometry : null,
                                  matrix);

            // Окно совпадения — мёртвое время прибора (`S27`), диапазон — тот
            // же, что у поиска пиков. Обе строки повторяют `FsaAnalysisSession`.
            if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            // (`A36`) Сетка дрейфа — ключами: подгонка усиления и сдвига
            // двигает МОДЕЛЬ по шкале, и отделить «модель не та» от
            // «шкалу подвинули» можно только заморозив её (`--no-drift`).
            // Шаг узлов сплайна континуума в ПШПВ: гуще узлы — гибче подложка.
            if (!double.IsNaN(knots))
            {
                analyzer.ContinuumKnotFwhm = knots;
            }

            // (`AMBER22`, П29) Ключ обязан доехать до анализатора (как `--refit-z-rel=`
            // выше); читатель — строка «HuberM 3 → …» у `FsaTuningReport.Print`.
            if (!double.IsNaN(huberM))
            {
                analyzer.HuberM = huberM;
            }

            analyzer.AnchorScale = anchor;
            if (gainSteps > 0)
            {
                analyzer.GainSteps = gainSteps;
            }

            if (offsetSteps > 0)
            {
                analyzer.OffsetSteps = offsetSteps;
            }

            analyzer.Backscatter = backscatter;
            if (!double.IsNaN(refitZ))
            {
                analyzer.RefitZ = refitZ;
            }

            // (`A266`) Ключ обязан ДОЕХАТЬ до анализатора, иначе он мёртв и
            // молчит: заведённый в разборе и не применённый здесь, он дал бы
            // плечо, побитово совпадающее с поставочным. Читатель у него —
            // `FsaTuningReport.Print` (строка «RefitZRelative 0 → …», довесок
            // `FsaTuningReport.cs`) и `PrintRefitZ` (фактический порог, ниже по
            // тексту).
            if (!double.IsNaN(refitZRel))
            {
                analyzer.RefitZRelative = refitZRel;
            }

            // (`S171`) Ключ обязан доехать до анализатора; читатель — строка
            // `FsaTuningReport.Print` («ChainTieShare … → …») и строки `TIE` ниже.
            if (!double.IsNaN(tieShare))
            {
                analyzer.ChainTieShare = tieShare;
            }

            if (tieLines)
            {
                analyzer.ChainTieByLines = true;
            }

            // (`S171`, второе правило) Ключи обязаны доехать до анализатора;
            // читатель — `FsaTuningReport.Print` («ChainLimitByExpectedZ … → …»,
            // «ChainLimitZ … → …») и строки `LIM` ниже.
            if (!limitRule)
            {
                analyzer.ChainLimitByExpectedZ = false;
            }

            if (!double.IsNaN(limitZ))
            {
                analyzer.ChainLimitZ = limitZ;
            }

            // (`T101`) ЧЕМ СНЯТ СНИМОК — ДО СЧЁТА И ВСЛУХ. Это ровно та проба,
            // которой ловят расхождение стенда с экраном (~~`S82`~~), и до
            // 06.09.2026 она о своих настройках не говорила НИ СТРОКИ: прогоны
            // `--no-backscatter`, `--refit-z=`, `--knots=` выглядели в выводе
            // в точности как поставочный. Печатается ПЕРЕД `Analyze` нарочно:
            // после разбора у анализатора появляется состояние прогона
            // (`RefitZState`), и эталон `new FsaAnalyzer()` разошёлся бы с ним
            // не по настройкам, а по исходу.
            // (`AMBER30`, П70) Ключ обязан доехать до анализатора; читатель — строка
            // `FsaTuningReport.Print` («UntiedTailAsResidual False → True») и строка
            // `отвязанный хвост` ниже.
            if (tailAsResidual)
            {
                analyzer.UntiedTailAsResidual = true;
            }

            FsaTuningReport.Print(analyzer);
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration,
                                                library, FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                // (`A312`) Отказ называет причину и её числа — тем же признаком,
                // что читает сеанс приложения (`FsaAnalyzer.Refusal`): до
                // 21.09.2026 «разложение не получилось» стояло на семь разных
                // бед, и какая из них сработала на `Am-241` Amber, было нечем
                // измерить.
                Console.Error.WriteLine("разложение не получилось: {0}{1}", analyzer.Refusal,
                                        analyzer.RefusalNote != null ? " — " + analyzer.RefusalNote : "");
                return 1;
            }

            // (`S173`) ПОДСАДКА «хвост снова в слой» — положительный контроль
            // приёмки: картинка, доли и невязка ДО решения Amber 14.09.2026.
            // Фит не трогается — только представление; читатель — сверка
            // плеч А (HEAD) и Б, где плечо Б с подсадкой обязано совпасть с А.
            if (plantTail)
            {
                PlantTailBack(result);
                Console.WriteLine("⛔ ПОДСАДКА --plant-tail (S173): отвязанный хвост возвращён в подложку и разнесён по слоям —"
                                  + " картинка ДО S173, числа этого прогона в журнал НЕ ГОДЯТСЯ");
            }

            // (`S175`) ПОДСАДКИ — положительный контроль приёмки; фит не
            // трогается, только адресат хвоста / пол разноса и доли, теми же
            // правилами результата. Порядок: сперва хвост в подложку, потом пол
            // — так их сумма даёт ровно картинку П70.
            if (tailToContinuum)
            {
                PlantTailToContinuum(result);
                Console.WriteLine("⛔ ПОДСАДКА --tail-to-continuum (S175): хвосты образов переложены в подложку и разнесены по S76 —"
                                  + " картинка a6f2b227 (до П68), числа этого прогона в журнал НЕ ГОДЯТСЯ");
            }

            if (plantGreyFloor)
            {
                PlantGreyFloor(result, analyzer.ResponseContinuumTrustFloorKev, rd.EnergySpectrum.EnergyCalibration);
                Console.WriteLine("⛔ ПОДСАДКА --plant-grey-floor (S175): пол разноса на пороге доверия, сплайн ниже порога серым (п. 1 S174, П69) —"
                                  + " картинка {0}, числа этого прогона в журнал НЕ ГОДЯТСЯ", tailToContinuum ? "П70 (d5c94425)" : "П75 до второго решения");
            }

            // (`S174`) ПОДСАДКА «разнести подложку по слоям как прежде и считать
            // невязку по всей полосе» — положительный контроль приёмки: оба пола
            // отображения сняты, доли и невязка пересчитаны ТЕМИ ЖЕ правилами
            // результата. Фит не трогается.
            if (plantSpread)
            {
                PlantSpreadBack(result, rd.EnergySpectrum.Spectrum);
                Console.WriteLine("⛔ ПОДСАДКА --plant-spread (S174): полы отображения сняты — сплайн разнесён по слоям и ниже"
                                  + " порога доверия, невязка по всей полосе фита; картинка ДО S174, числа в журнал НЕ ГОДЯТСЯ");
            }

            // (`S174`) Оба пола отображения — каналом и кэВ, чтобы плечи
            // сверялись числом, а не картинкой.
            Console.WriteLine("пороги отображения (S174/S175): разнос подложки от канала {0} ({1} кэВ{2}), невязка от канала {3} ({4} кэВ) при полосе фита {5}..{6}",
                              result.ContinuumSpreadFloorChannel,
                              result.ContinuumSpreadFloorKev.ToString("G", CultureInfo.InvariantCulture),
                              result.ContinuumSpreadFloorKev > 0.0 ? " — ПОДСАДКА" : " — пола нет, S76 по всей шкале (S175)",
                              result.ResidualFloorChannel,
                              result.ResidualFloorKev.ToString("G", CultureInfo.InvariantCulture),
                              result.FirstChannel, result.LastChannel);

            // (`A36`) ДРЕЙФ ШКАЛЫ — числом, а не догадкой по картинке: подгонка
            // усиления и сдвига двигает МОДЕЛЬ относительно измерения, и на
            // дальнем конце шкалы это видно глазами, а величину сдвига видно
            // только здесь.
            Console.WriteLine("шкала: усиление {0:F6}, сдвиг {1:F3} канала{2}",
                              result.Gain, result.OffsetChannels,
                              result.DriftOnGridEdge ? " — КРАЙ СЕТКИ" : "");
            Console.WriteLine("chi2/ndf {0:F3}, невязка модели {1:F1} %, суммирование {2}",
                              result.Chi2Ndf, result.ModelResidual * 100.0,
                              result.CascadeSummingUsed ? "да" : "нет");
            PrintRefitZ(analyzer);

            // Состав и подавленные — ТЕКСТОМ рядом с картинкой (`S81`). Доля
            // берётся у слоёв, а не у компонентов: у приборных образов
            // `SharePercent` компонента считается по пиковым окнам, а печатает
            // экран долю СЛОЯ (`S76`), и сравнивать надо с тем, что видно.
            List<FsaStackLayer> shot = result.BuildStackedLayers(FsaResult.DefaultMaxNamedLayers);
            foreach (FsaStackLayer layer in shot)
            {
                Console.WriteLine("ROW\t{0}\t{1}\t{2}\t{3}\t{4}", layer.Name, layer.Kind,
                                  layer.SharePercent.ToString("F3", CultureInfo.InvariantCulture),
                                  // (`S72`) Ряд, связкой которого закреплена
                                  // амплитуда строки; «-» — амплитуда своя.
                                  // Мерка строки — сравнение СПИСКА строк с
                                  // галкой и без, и различать связанное от
                                  // свободного надо машинно, а не по картинке.
                                  string.IsNullOrEmpty(layer.ChainRoot) ? "-" : layer.ChainRoot,
                                  // (`S171`) Партнёр привязки вырожденного
                                  // члена; «-» — не привязан.
                                  string.IsNullOrEmpty(layer.TiedTo) ? "-" : layer.TiedTo);
            }

            // (`S174`) Серый слой — двух родов, и оба названы числом.
            double greyBelow, greyAbove;
            GreySplit(result, shot, out greyBelow, out greyAbove);
            Console.WriteLine("серый слой: ниже пола разноса {0} отсч. (в приложении пола нет — S175), выше последней линии {1} отсч.; всего {2} отсч. ({3} % стека)",
                              greyBelow.ToString("F1", CultureInfo.InvariantCulture),
                              greyAbove.ToString("F1", CultureInfo.InvariantCulture),
                              (greyBelow + greyAbove).ToString("F1", CultureInfo.InvariantCulture),
                              (result.StackTotal > 0.0 ? 100.0 * (greyBelow + greyAbove) / result.StackTotal : 0.0)
                                  .ToString("F3", CultureInfo.InvariantCulture));

            // (`S174`) Невязка в отсчётах ПО ПОЛОСАМ — на кривой фита, по тому же
            // правилу, что у результата, но раздельно: полоса фита ниже
            // `Min_Range` (лента есть, в проценте нет) и от `Min_Range` (то,
            // что печатает строка отчёта). Число строки обязано равняться
            // второй полосе — это и есть проверка «от Min_Range».
            PrintResidualBands(result, rd.EnergySpectrum.Spectrum);

            // (`S171`) Приговоры гейта привязки — по каждому судимому члену.
            if (analyzer.ChainTieJudgements != null)
            {
                foreach (FsaTie tie in analyzer.ChainTieJudgements)
                {
                    Console.WriteLine("TIE\t{0}\t{1}\t{2}\t{3}\t{4}", tie.Member, tie.Partner,
                                      tie.Share.ToString("F4", CultureInfo.InvariantCulture),
                                      tie.PairShare.ToString("F4", CultureInfo.InvariantCulture),
                                      tie.Tied ? "tied" : "free");
                }
            }

            Console.WriteLine("привязка: {0}", analyzer.ChainTieNote ?? "гейт не судил");

            // (`S171`, второе правило) Приговоры предела — по каждому судимому члену каждого круга.
            if (analyzer.ChainLimitJudgements != null)
            {
                foreach (FsaChainLimit verdict in analyzer.ChainLimitJudgements)
                {
                    Console.WriteLine("LIM\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}", verdict.Member, verdict.Reference,
                                      verdict.ExpectedZ.ToString("F3", CultureInfo.InvariantCulture),
                                      verdict.Z.ToString("F2", CultureInfo.InvariantCulture),
                                      verdict.Threshold.ToString("F3", CultureInfo.InvariantCulture),
                                      verdict.Round.ToString(CultureInfo.InvariantCulture),
                                      verdict.Limited ? "limit" : (verdict.Below ? "below" : "free"));
                }
            }

            Console.WriteLine("предел: {0}", analyzer.ChainLimitNote ?? "правило не судило");

            // (`S172`) Приборные образы, снятые гейтами ДО фита (в `CUT` их
            // нет — фиту они не предъявлялись): рентген кристалла (`AMBER4`) и
            // K-вылет кристалла (`S172`). Без матрицы оба нуля — по построению.
            Console.WriteLine("гейты при матрице: рентген кристалла снят {0}, вылет кристалла снят {1}",
                              analyzer.CrystalXrayDropped, analyzer.CrystalEscapeDropped);

            // (`S173`, `S175`) Отвязанные хвосты матричных образов — чей,
            // сколько отсчётов и ГДЕ (в слое своего образа — умолчание S175; в
            // подложке — образа в составе нет; в невязке — --tail-as-residual):
            // без этой строки «доля Cs-137 выросла на 6 %» и «серый бугор
            // исчез» читатель связал бы только по картинке.
            Console.WriteLine("отвязанный хвост (S175; layer = в слое и доле своего образа, continuum = в подложке → серый слой, residual = в невязке): {0}",
                              TailNote(result));
            Console.WriteLine("хвост в слоях (S175): {0}", TailInLayersNote(result));

            // (`S175`, мера п. 7 — не правка) СКОЛЬКО СПЛАЙНА ДОСТАЛОСЬ КАЖДОМУ
            // СЛОЮ правилом `S76` ниже и выше порога доверия матрицы: лента
            // слоя минус положительная часть образа минус хвост — то, что
            // добавил разнос. Число для решения Amber, физичен ли `S76` при
            // матрице выше порога (там подложка — сплайн, а образы уже несут
            // свой комптон); ниже порога — мера второго решения «В слои образов
            // по S76 везде».
            PrintSpreadByFloor(result, FsaAnalyzer.FloorChannel(rd.EnergySpectrum.EnergyCalibration,
                                                               analyzer.ResponseContinuumTrustFloorKev,
                                                               result.FirstChannel, result.LastChannel),
                               analyzer.ResponseContinuumTrustFloorKev);

            foreach (FsaSuppressedImage cut in result.SuppressedImages)
            {
                Console.WriteLine("CUT\t{0}\t{1}\tz={2}", cut.Name, cut.Kind,
                                  cut.Z.ToString("F2", CultureInfo.InvariantCulture));
            }

            if (ratesPath != null)
            {
                DumpRates(ratesPath, result);
                Console.WriteLine("{0}: компонентов {1}, кандидатов {2}", ratesPath,
                                  result.Components != null ? result.Components.Count : 0,
                                  result.CharacteristicLimits != null ? result.CharacteristicLimits.Count : 0);
            }

            EnergySpectrum spectrum = rd.EnergySpectrum;
            EnergyCalibration calibration = spectrum.EnergyCalibration;
            if (toKev <= fromKev)
            {
                fromKev = 0.0;
                toKev = calibration.ChannelToEnergy(spectrum.NumberOfChannels - 1);
            }

            // Потолок шкалы: без него высокие пики забирают поле целиком.
            if (!(ceiling > 0.0))
            {
                ceiling = 0.0;
                for (int i = 0; i < result.Model.Length; i++)
                {
                    if (result.Model[i] > ceiling)
                    {
                        ceiling = result.Model[i];
                    }
                }
            }

            const int left = 1;
            using (var view = new EnergySpectrumView())
            using (var image = new Bitmap(width, height))
            {
                Set(view, "energySpectrum", spectrum);

                // (`A45`) Активные данные: у них вид спрашивает настройку центроида.
                // В окне это поле занято всегда, в пробе его надо поставить руками —
                // иначе расчёт выделения падает на пустой ссылке.
                Set(view, "activeResultData", rd);

                // Фон: разбор выделения считает по нему подложку и «нетто», и без
                // этих полей падает там же. Калибровка фона своя — как в окне.
                if (rd.BackgroundEnergySpectrum != null)
                {
                    Set(view, "backgroundEnergySpectrum", rd.BackgroundEnergySpectrum);
                    Set(view, "backgroundEnergyCalibration",
                        rd.BackgroundEnergySpectrum.EnergyCalibration);
                    Set(view, "backgroundNumberOfChannels",
                        rd.BackgroundEnergySpectrum.NumberOfChannels);
                }
                Set(view, "energyCalibration", calibration);
                Set(view, "numberOfChannels", spectrum.NumberOfChannels);
                Set(view, "backgroundMode", BackgroundMode.ShowFSA);
                Set(view, "horizontalUnit", HorizontalUnit.Energy);
                Set(view, "verticalUnit", VerticalUnit.Counts);

                // Шкала: те же три поля, что считает `RecalcChartParameters`
                // для выбранного вида. Низ шкалы — ноль, поэтому `totalMin*`
                // обращаются в ноль по определению самих `Pow`/`Log10` вида
                // (у них x ≤ 0 даёт 0), и остаётся выставить размах.
                Set(view, "verticalScaleType",
                    scale == "pow" ? VerticalScaleType.PowerScale
                    : scale == "log" ? VerticalScaleType.LogarithmicScale
                    : VerticalScaleType.LinearScale);
                Set(view, "pownum", pownum);
                Set(view, "totalMinValuePow", 0.0);
                Set(view, "valueRangePow", Math.Pow(ceiling, 1.0 / pownum));
                Set(view, "totalMinValueLog", 0.0);
                Set(view, "valueRangeLog", Math.Log10(ceiling));
                Set(view, "height", height);
                Set(view, "width", width - left);
                Set(view, "left", left);
                Set(view, "scrollX", 0);
                Set(view, "scrollY", 0);
                Set(view, "scrollBaseY", 0.0);
                Set(view, "verticalScale", 1.0);
                Set(view, "horizontalScale", 1.0);
                Set(view, "totalMinValue", 0.0);
                Set(view, "valueRange", ceiling);
                Set(view, "energyViewOffset", fromKev);
                Set(view, "pixelPerEnergy", (width - left) / (toKev - fromKev));
                Set(view, "dirty", false);

                // (`AMBER45`) Слой матрицы — ТЕМ ЖЕ полем, каким его ставит
                // окно отчёта через документ (`EnergySpectrumView.FsaMatrixLayer`);
                // ставится ДО первого кадра, чтобы стопка собралась в нём сразу.
                Set(view, "fsaMatrixLayer", matrixLayer);

                // Готовое разложение — прямо в сеанс вида: считать его второй
                // раз фоновым потоком пробе незачем. Вид без документа заводит
                // сеанс сам при первом обращении к `FsaSession`.
                object overlay = typeof(EnergySpectrumView).GetProperty(
                    "FsaSession", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view, null);
                Field(overlay.GetType(), "result").SetValue(overlay, result);

                // (`A32`) Признак «идёт пересчёт» — тем же полем, каким его
                // взводит сам расчёт. Поймать это состояние в живом окне
                // нельзя: разбор считается доли секунды, и снимок всегда
                // опаздывает; а признак, у которого нет читателя, — ровно та
                // ошибка, которой этот проект уже болел.
                if (showCalculating)
                {
                    Field(overlay.GetType(), "running").SetValue(overlay, true);
                    Field(overlay.GetType(), "status").SetValue(overlay,
                          BecquerelMonitor.Properties.Resources.FSACalculating);
                }

                // (`A27`) Выделенная область — тем же кодом, что в окне.
                // Каналы берутся у калибровки спектра, порядок вызовов повторяет
                // `DrawChart`: подсветка полосы ДО стека, «нетто» ПОСЛЕ него.
                int selectFrom = -1, selectTo = -1;
                if (selectKev != null)
                {
                    // Данные отрисовки готовит `PrepareViewData` у окна; проба
                    // собирает вид без формы, и без этой строки `DrawingSpectrum`
                    // остаётся null — старый код заливки выделения падал на ней
                    // NullReference, то есть сравнение «до / после» не состоялось
                    // бы вовсе. Сглаживание не применяется (`SmoothingMethod.None`
                    // выше), поэтому это ровно измеренные отсчёты.
                    if (spectrum.DrawingSpectrum == null)
                    {
                        var drawing = new double[spectrum.Spectrum.Length];
                        for (int i = 0; i < drawing.Length; i++)
                        {
                            drawing[i] = spectrum.Spectrum[i];
                        }

                        spectrum.DrawingSpectrum = drawing;
                    }

                    string[] ends = selectKev.Split(new[] { ".." }, StringSplitOptions.RemoveEmptyEntries);
                    if (ends.Length != 2)
                    {
                        Console.Error.WriteLine("--select=<от>..<до> в кэВ");
                        return 2;
                    }

                    selectFrom = (int)calibration.EnergyToChannel(
                        double.Parse(ends[0], CultureInfo.InvariantCulture),
                        maxChannels: spectrum.NumberOfChannels);
                    selectTo = (int)calibration.EnergyToChannel(
                        double.Parse(ends[1], CultureInfo.InvariantCulture),
                        maxChannels: spectrum.NumberOfChannels);
                    Set(view, "selectionStart", selectFrom);
                    Set(view, "selectionEnd", selectTo);
                    Set(view, "baseEnergyCalibration", calibration);
                    Set(view, "chartType", ChartType.BarChart);
                    Console.WriteLine("выделение: {0}…{1} кэВ = каналы {2}…{3}",
                                      ends[0], ends[1], selectFrom, selectTo);
                }

                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(Color.Black);
                    if (selectFrom >= 0)
                    {
                        Invoke(view, "ShowSelectionPart1", g);
                    }

                    MethodInfo show = typeof(EnergySpectrumView).GetMethod(
                        "ShowFsaOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (show == null)
                    {
                        throw new InvalidOperationException("нет EnergySpectrumView.ShowFsaOverlay");
                    }

                    object drawn = show.Invoke(view, new object[] { g });
                    Console.WriteLine("отрисовано: {0}", drawn);

                    // (`AMBER45`) Чем собран кадр: просили — собран — сколько
                    // слоёв нарисовано и какие. «Собран» читается у вида
                    // (`fsaFrameLayer`): без матрицы канал недостижим, и здесь
                    // обязано стоять `All`, что бы ни просили.
                    var stackLayers = Field(typeof(EnergySpectrumView), "fsaStack").GetValue(view) as List<FsaStackLayer>;
                    var names = new List<string>();
                    if (stackLayers != null)
                    {
                        foreach (FsaStackLayer layer in stackLayers)
                        {
                            names.Add(layer.Name);
                        }
                    }

                    Console.WriteLine("MATRIX_LAYER\t{0}\t{1}\t{2}\t{3}", matrixLayer,
                                      Field(typeof(EnergySpectrumView), "fsaFrameLayer").GetValue(view),
                                      names.Count, string.Join(", ", names.ToArray()));

                    if (selectFrom >= 0)
                    {
                        Invoke(view, "ShowSelectionPart2", g);

                        // (`A45`) Линии полуширины — и по какому спектру они легли.
                        Invoke(view, "DrawFWHM", g);
                        ReportFwhm(view, spectrum);
                    }

                    // Строка состояния — единственное, что вид говорит о
                    // разложении словами (`A145`, этап 3). ⚠ С 07.09.2026 при
                    // ГОТОВОМ результате она пуста ВСЕГДА, в том числе под
                    // `--calculating`: панель «идёт пересчёт» с графика снята
                    // решением Amber (отмена `A32`, признак остался в окне
                    // отчёта). Ключ оставлен живым — им и проверяется, что на
                    // графике при пересчёте действительно НИЧЕГО не появляется.
                    MethodInfo status = typeof(EnergySpectrumView).GetMethod(
                        "DrawFsaStatus", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (status == null)
                    {
                        throw new InvalidOperationException("нет EnergySpectrumView.DrawFsaStatus");
                    }

                    status.Invoke(view, new object[] { g, 20, 20, 260 });
                    Console.WriteLine("строка состояния на графике: «{0}»",
                                      EnergySpectrumView.FsaStatusText((FsaAnalysisSession)overlay) ?? "");
                }

                // Отчёт — настоящим окном на том же сеансе, справа от стека;
                // надпись пробы (если есть) — полосой ПОД обоими.
                using (Bitmap combined = WithReport(image, (FsaAnalysisSession)overlay, rd, infer, caption,
                                                    screenRows, equilibrium, atomic, matrixLayer))
                {
                    combined.Save(outPath, ImageFormat.Png);
                }
                Console.WriteLine("{0}: {1}–{2:F0} кэВ, потолок {3:F0}, шкала {4}",
                                  outPath, fromKev, toKev, ceiling, scale);
                if (caption != null)
                {
                    foreach (string line in caption.Split('\n'))
                    {
                        Console.WriteLine("НАДПИСЬ\t{0}", line);
                    }
                }

                if (dumpPath != null)
                {
                    int columns = Dump(dumpPath, view, spectrum, calibration, result, shot);
                    Console.WriteLine("{0}: {1} каналов, колонок слоёв {2}",
                                      dumpPath, spectrum.NumberOfChannels, columns);
                }
            }

            return SuppliedLibraryGate(sampleNuclides != null, 0);
        }

        /// <summary>
        /// (`T257` (3), `AMBER19`) Подложить одиночке `NuclideDefinitionManager`
        /// ПУСТОЙ список и пометить его загруженным: `GetInstance()` после этого
        /// файл не открывает и не бросает, а кто бы его ни спросил, получит
        /// ноль определений и ноль наборов. Ровно то, что делает для себя
        /// `EnergySpectrumView.CreateNuclideDefinitionManagerFallback`, — только
        /// для одиночки, которую зовёт код показа приложения
        /// (`FsaAnalysisSession.NuclideSetStamp`), и которую пробе иначе не
        /// обойти, не трогая приложение. Зовётся ДО первого обращения к
        /// менеджеру; поставочный файл при этом не читается ни при каком пути.
        /// </summary>
        static void PrimeEmptySuppliedLibrary()
        {
            var stub = new NuclideDefinitionManager { NuclideDefinitionFile = new NuclideDefinitionFile() };
            Field(typeof(NuclideDefinitionManager), "isLoaded").SetValue(stub, true);
            FieldInfo instance = typeof(NuclideDefinitionManager).GetField(
                "instance", BindingFlags.Static | BindingFlags.NonPublic);
            if (instance == null)
            {
                throw new InvalidOperationException("нет поля instance у NuclideDefinitionManager");
            }

            instance.SetValue(null, stub);
        }

        /// <summary>
        /// (`AMBER19`, порядок П11) Гейт «поставочный список не читался» — в
        /// конце всегда, счётчик обращений печатается всегда.
        ///
        /// ⚠ У этой пробы, в отличие от восьми проб оснастки, счётчик при
        /// `--sample=` НЕ НОЛЬ по праву: менеджер зовёт код ПОКАЗА приложения
        /// (конструктор вида, отпечаток сеанса для окна отчёта), и обойти это
        /// можно только правкой приложения. Поэтому судится не число обращений,
        /// а ЧТО они получили: одиночка обязана остаться пустышкой
        /// (<see cref="PrimeEmptySuppliedLibrary"/>) — ноль определений, ноль
        /// наборов. Список с определениями при объявленном составе — код 12:
        /// значит, поставочный файл всё же прочитан, и подписи или состав могли
        /// прийти из него. Без `--sample=` список нужен по праву (подписи,
        /// `--infer`), и гейт только печатает счётчик.
        /// </summary>
        static int SuppliedLibraryGate(bool declared, int code)
        {
            int raised = NuclideDefinitionManager.RaiseCount;
            if (!declared)
            {
                Console.WriteLine("NuclideDefinitionManager за прогон: обращений {0} (состав от подписей — поставочный список нужен по праву)",
                                  raised.ToString(CultureInfo.InvariantCulture));
                return code;
            }

            // Через одиночку, а не `GetInstance()`: тот сам увеличил бы счётчик.
            var instance = (NuclideDefinitionManager)typeof(NuclideDefinitionManager)
                .GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            int definitions = instance.NuclideDefinitionFile != null && instance.NuclideDefinitionFile.NuclideDefinitions != null
                ? instance.NuclideDefinitionFile.NuclideDefinitions.Count : -1;
            int sets = instance.NuclideDefinitionFile != null && instance.NuclideDefinitionFile.NuclideSets != null
                ? instance.NuclideDefinitionFile.NuclideSets.Count : -1;
            bool empty = definitions == 0 && sets == 0;
            Console.WriteLine("NuclideDefinitionManager за прогон: обращений {0} (код показа приложения); список у одиночки: определений {1}, наборов {2}{3}",
                              raised.ToString(CultureInfo.InvariantCulture),
                              definitions.ToString(CultureInfo.InvariantCulture),
                              sets.ToString(CultureInfo.InvariantCulture),
                              empty ? " — пустышка пробы, поставочный файл не читался" : " — НАСТОЯЩИЙ список");
            if (!empty)
            {
                Console.Error.WriteLine("⛔ AMBER19: при --sample= поставочный список поднялся с {0} определениями и {1} наборами — числа негодны",
                                        definitions.ToString(CultureInfo.InvariantCulture),
                                        sets.ToString(CultureInfo.InvariantCulture));
                return 12;
            }

            return code;
        }
        /// <summary>
        /// (`T240`) ЧТО СЛУЧИЛОСЬ С ОТСЕВОМ ПО ЗНАЧИМОСТИ — вслух, после счёта.
        ///
        /// ⛔ Зачем. Порог выше значимости ВСЕХ компонентов ОТКЛЮЧАЕТ отсев, а
        /// не ужесточает его: `keep` пуст, второго прохода не происходит, в
        /// отчёте стоит результат ДО отсева. Плечо развёртки
        /// `--refit-z=&lt;большое&gt;` читается при этом как «самый строгий
        /// отсев» — то есть НАОБОРОТ, — а ключ в шапке печатается как принятый.
        /// Ни отказа, ни предупреждения до 06.09.2026 не было.
        ///
        /// ⚠ Печатаются ВСЕ четыре исхода, а не один тревожный: «отсев
        /// применён», «выбрасывать было некого» и «порог выше всех» — три
        /// разных утверждения, и молчание вместо двух последних снова сделало
        /// бы их неразличимыми.
        /// </summary>
        static void PrintRefitZ(FsaAnalyzer analyzer)
        {
            string top = double.IsNaN(analyzer.RefitZTopZ)
                ? "судить было некого"
                : "наибольшая значимость "
                  + analyzer.RefitZTopZ.ToString("F2", CultureInfo.InvariantCulture);
            string thr = Threshold(analyzer);
            switch (analyzer.RefitZState)
            {
                case FsaAnalyzer.RefitZOutcome.NotRequested:
                    Console.WriteLine("отсев по значимости: НЕ ЗАКАЗАН (порог {0})", thr);
                    break;
                case FsaAnalyzer.RefitZOutcome.Applied:
                    Console.WriteLine("отсев по значимости: ПРИМЕНЁН, порог {0}, судимых {1}, {2}",
                                      thr,
                                      Judged(analyzer), top);
                    break;
                case FsaAnalyzer.RefitZOutcome.NothingBelow:
                    Console.WriteLine("отсев по значимости: выбрасывать было некого,"
                                      + " порог {0}, судимых {1}, {2}",
                                      thr,
                                      Judged(analyzer), top);
                    break;
                default:
                    Console.WriteLine("⚠ отсев по значимости НЕ ПРИМЕНЁН (`T240`): порог {0} выше"
                                      + " значимости ВСЕХ {1} судимых ({2}) — второго прохода НЕ БЫЛО,"
                                      + " в отчёте результат ДО отсева. Плечо «строже» здесь значит"
                                      + " «отсева нет», наоборот читать нельзя",
                                      thr,
                                      // Здесь НЕ `Judged`: отсеяно НОЛЬ, а не
                                      // всё. Второго прохода не было вовсе, и
                                      // «отсеяно 13» было бы ложью ровно того
                                      // рода, против которой заведена `T240`.
                                      analyzer.RefitZJudged.ToString(CultureInfo.InvariantCulture), top);
                    break;
            }
        }

        /// <summary>
        /// (`A266`) ПОРОГ, КОТОРЫМ СУДИЛИ, — а не тот, что заказан.
        ///
        /// ⛔ При относительной доле заказанное и применённое расходятся:
        /// заказано `RefitZ` = 3 и доля 0.1, применено 0.26. Печатать
        /// заказанный значило бы повторить механизм ~~`T240`~~ на новом рычаге —
        /// ключ в шапке стоит как принятый, а судили другим числом.
        /// </summary>
        /// <summary>
        /// (`A266`) Судимых и сколько из них ОТСЕЯНО. Одна смена исхода
        /// говорит лишь «отсев ожил», а насколько — не говорит ничем.
        /// </summary>
        static string Judged(FsaAnalyzer analyzer)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} (отсеяно {1})",
                                 analyzer.RefitZJudged,
                                 analyzer.RefitZJudged - analyzer.RefitZKept);
        }

        static string Threshold(FsaAnalyzer analyzer)
        {
            if (analyzer.RefitZRelative <= 0.0 || double.IsNaN(analyzer.RefitZUsed))
            {
                return analyzer.RefitZ.ToString("G", CultureInfo.InvariantCulture);
            }

            return string.Format(CultureInfo.InvariantCulture,
                                 "{0:F4} = min(абс {1}, доля {2} от вершины)",
                                 analyzer.RefitZUsed,
                                 analyzer.RefitZ.ToString("G", CultureInfo.InvariantCulture),
                                 analyzer.RefitZRelative.ToString("G", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Кривые по каналам в csv. Измерение берётся у ВИДА — поле
        /// `fsaNetSpectrum`, которое он и рисует линией, — а не пересчитывается
        /// пробой: вычитание фона живёт в одном месте, и второе такое же
        /// правило рядом разъехалось бы молча (та же беда, что у `S37`).
        /// </summary>
        static int Dump(string path, EnergySpectrumView view, EnergySpectrum spectrum,
                        EnergyCalibration calibration, FsaResult result, List<FsaStackLayer> layers)
        {
            double[] net = (double[])Field(typeof(EnergySpectrumView), "fsaNetSpectrum").GetValue(view);
            if (net == null)
            {
                net = result.NetSpectrum(spectrum.Spectrum);
            }

            // (`AMBER45`) В режиме канала столбцы слоёв — НАРИСОВАННАЯ стопка,
            // как её собрал вид: только слои с каналами, значения — кривые
            // канала (`fsaStackCurves`). При «All» — прежний список слоёв и их
            // ленты, столбец в столбец, как до 18.09.2026: эталоны семи пар
            // витрины держатся на этом.
            var columnNames = new List<string>();
            var columnCurves = new List<double[]>();
            var frameLayer = (FsaMatrixLayer)Field(typeof(EnergySpectrumView), "fsaFrameLayer").GetValue(view);
            if (frameLayer != FsaMatrixLayer.All)
            {
                var stack = Field(typeof(EnergySpectrumView), "fsaStack").GetValue(view) as List<FsaStackLayer>;
                var curves = Field(typeof(EnergySpectrumView), "fsaStackCurves").GetValue(view) as double[][];
                if (stack == null || curves == null || stack.Count != curves.Length)
                {
                    throw new InvalidOperationException("--dump=: у вида нет нарисованной стопки канала (fsaStack/fsaStackCurves)");
                }

                for (int k = 0; k < stack.Count; k++)
                {
                    columnNames.Add(stack[k].Name);
                    columnCurves.Add(curves[k]);
                }
            }
            else
            {
                foreach (FsaStackLayer layer in layers)
                {
                    columnNames.Add(layer.Name);
                    columnCurves.Add(layer.Curve);
                }
            }
            // (`T103`) Сырой сплайн — `continuum_raw`: слой стека «continuum»
            // (`FsaResult.ContinuumLayerName`) идёт в том же заголовке следом, и
            // `csv.DictReader` брал его вместо сырого. Повтор имени — отказ.
            // (`S173`) `untied_tail` — сумма отвязанных хвостов матричных
            // образов по каналам: в `model` (верх стека) и в слои НЕ входит,
            // лежит в невязке `net − model`; столбец даёт увидеть, какая часть
            // невязки — хвост. Нули — хвостов нет.
            // (`S174`) `fit` — кривая ФИТА (`FsaResult.FitSpectrum`, без
            // подрезки): число невязки и χ² считаны по ней, а `net` — показная.
            // (`S175`) `tail` — сумма отвязанных хвостов, лежащих У ОБРАЗОВ
            // (`FsaResult.TailCurveSum`): входит в `model` и в слои своих
            // образов; нули — хвостов в слоях нет. `continuum_raw` — ТОЛЬКО
            // сплайн (хвоста в нём нет, кроме хвоста без образа в составе).
            var head = new StringBuilder("ch,keV,net,model,continuum_raw,untied_tail,fit,tail");
            double[] fitCurve = result.FitSpectrum(spectrum.Spectrum);
            double[] tailSum = result.TailCurveSum();
            foreach (string name in columnNames)
            {
                head.Append(',').Append(name.Replace(',', ';'));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in head.ToString().Split(','))
            {
                if (!seen.Add(name))
                {
                    throw new InvalidOperationException(
                        "--dump=: имя столбца «" + name + "» повторяется в заголовке (T103)");
                }
            }

            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.WriteLine(head.ToString());
                for (int i = 0; i < spectrum.NumberOfChannels; i++)
                {
                    var line = new StringBuilder();
                    line.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(calibration.ChannelToEnergy(i).ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append(At(net, i).ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append(At(result.Model, i).ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append(At(result.Continuum, i).ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append(At(result.UntiedTail, i).ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append(At(fitCurve, i).ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append(At(tailSum, i).ToString("F3", CultureInfo.InvariantCulture));
                    foreach (double[] curve in columnCurves)
                    {
                        line.Append(',').Append(At(curve, i).ToString("F3", CultureInfo.InvariantCulture));
                    }

                    w.WriteLine(line.ToString());
                }
            }

            return columnNames.Count;
        }

        static double At(double[] a, int i)
        {
            return a != null && i < a.Length ? a[i] : 0.0;
        }

        /// <summary>
        /// Снимок окна отчёта (`FSAReportView`) на том же сеансе, приклеенный
        /// справа к снимку стека. Окно живёт в форме-носителе, показанной ради
        /// создания ручек (как `EditorShot`); ширина — обычная ширина
        /// dock-панели.
        ///
        /// (`T257` (1)) <paramref name="caption"/> — надпись ПРОБЫ, полосой под
        /// стеком и отчётом, строки через '\n'; null — полосы нет, и снимок
        /// той же высоты, что прежде. Это единственное, что проба рисует сама,
        /// и рисует ВНЕ графика: на самом стеке по-прежнему только код
        /// приложения.
        /// </summary>
        static Bitmap WithReport(Bitmap stack, FsaAnalysisSession session, ResultData rd, bool infer,
                                 string caption, bool screenRows, bool equilibrium, bool atomic,
                                 FsaMatrixLayer matrixLayer)
        {
            const int reportWidth = 320;
            string[] captionLines = caption != null ? caption.Split('\n') : new string[0];
            const int captionLineHeight = 22;
            int captionHeight = captionLines.Length > 0 ? captionLines.Length * captionLineHeight + 12 : 0;

            // Окно читает НАСТРОЙКИ из конфигурации спектра, а результат здесь
            // подложен и посчитан анализатором пробы: радиокнопка источника
            // ставится по ключу `--infer`, а в сеанс кладётся отпечаток этих же
            // настроек — иначе окно-потребитель сочло бы подложенный результат
            // устаревшим и заказало бы СВОЙ счёт поверх него.
            var cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (cfg != null)
            {
                cfg.DbLookupsForFsa = infer;
                // (П59) Галки «Равновесие ряда» и «Атомный рентген» окна — тоже
                // из конфигурации спектра, и до 14.09.2026 снимок плеча
                // `--no-equilibrium` показывал галку ВКЛЮЧЁННОЙ: конфигурация
                // прибора ключа не знала. Ставятся те же значения, что ушли в
                // `FsaSampleSpec`, — окно обязано говорить то же, что разбор.
                cfg.ChainEquilibrium = equilibrium;
                cfg.AtomicXrayForFsa = atomic;
            }

            Field(session.GetType(), "stamp").SetValue(session,
                FsaAnalysisSession.BuildStamp(rd, rd.BackgroundEnergySpectrum != null));

            using (var host = new Form())
            using (var report = new FSAReportView(null))
            {
                host.ClientSize = new Size(reportWidth, stack.Height);
                host.StartPosition = FormStartPosition.Manual;
                host.Location = new Point(-4000, -4000);
                host.ShowInTaskbar = false;
                report.TopLevel = false;
                report.Dock = DockStyle.Fill;
                host.Controls.Add(report);
                report.Show();
                host.Show();
                // (`AMBER45`) Просьба о слое — ДО источника: `SetProbeSource`
                // перечитывает окно и сам ставит комбо по просьбе и по тому,
                // жива ли матрица (без неё комбо гаснет и показывает «All»).
                report.RequestedMatrixLayer = matrixLayer;
                report.SetProbeSource(session, rd);
                Application.DoEvents();

                ComboBox layerCombo = (ComboBox)Field(typeof(FSAReportView), "matrixLayerComboBox").GetValue(report);
                Console.WriteLine("MATRIX_LAYER_COMBO\t{0}\t{1}\t{2}",
                                  layerCombo.Enabled ? "enabled" : "disabled",
                                  layerCombo.SelectedItem ?? "",
                                  report.ToolTipOf(layerCombo));

                var combined = new Bitmap(stack.Width + reportWidth, stack.Height + captionHeight);
                using (Graphics g = Graphics.FromImage(combined))
                {
                    g.Clear(Color.White);
                    g.DrawImageUnscaled(stack, 0, 0);
                    using (var shot = new Bitmap(reportWidth, stack.Height))
                    {
                        report.DrawToBitmap(shot, new Rectangle(0, 0, reportWidth, stack.Height));
                        g.DrawImageUnscaled(shot, stack.Width, 0);
                    }

                    // Полоса надписи — ниже стека и отчёта, своим цветом, чтобы
                    // её нельзя было принять за часть окна приложения.
                    if (captionHeight > 0)
                    {
                        var band = new Rectangle(0, stack.Height, combined.Width, captionHeight);
                        using (var back = new SolidBrush(Color.FromArgb(255, 250, 205)))
                        using (var font = new Font("Segoe UI", 10.0f, FontStyle.Regular, GraphicsUnit.Point))
                        using (var pen = new Pen(Color.FromArgb(180, 140, 0)))
                        {
                            g.FillRectangle(back, band);
                            g.DrawLine(pen, band.Left, band.Top, band.Right, band.Top);
                            for (int i = 0; i < captionLines.Length; i++)
                            {
                                g.DrawString(captionLines[i], font, Brushes.Black,
                                             8, band.Top + 6 + i * captionLineHeight);
                            }
                        }
                    }
                }

                Console.WriteLine("отчёт: строк в таблице {0}", report.ReportTable.TableModel.Rows.Count);
                if (screenRows)
                {
                    // (П59) Строки — С ТОГО ЖЕ окна, что снято на PNG: текст
                    // ячеек «Компонент» и «Значение» и подсказка ячейки имени.
                    // Своей сборки представления здесь нет нарочно — иначе
                    // строки могли бы разойтись с картинкой.
                    foreach (XPTable.Models.Row row in report.ReportTable.TableModel.Rows)
                    {
                        string name = row.Cells.Count > 1 && row.Cells[1] != null ? row.Cells[1].Text : "";
                        string value = row.Cells.Count > 2 && row.Cells[2] != null ? row.Cells[2].Text : "";
                        string hint = row.Cells.Count > 1 && row.Cells[1] != null ? row.Cells[1].ToolTipText : "";
                        Console.WriteLine("SCREEN\t{0}\t{1}\t{2}", name ?? "", value ?? "",
                                          string.Equals(hint, name, StringComparison.Ordinal) ? "" : (hint ?? ""));
                    }
                }

                host.Hide();
                return combined;
            }
        }

        /// <summary>
        /// (П59, `AMBER27`) Числа разбора по компонентам и кандидатам — csv,
        /// разделитель дробной части точка (инвариант). Строки `meta` — живое
        /// время, χ²/ndf, невязка, шкала; `component` — вошедшие в состав
        /// (`FsaResult.Components`); `limit` — все кандидаты с пределами
        /// (`FsaResult.CharacteristicLimits`). Пустое поле — величины у строки
        /// нет.
        /// </summary>
        static void DumpRates(string path, FsaResult result)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                // (`S171`) Столбец `tied_to` — партнёр привязки вырожденного
                // члена (пусто — своя амплитуда); (`S171`, второе правило)
                // последний столбец `unmeasurable_at` — опорный член, при
                // амплитуде которого кандидат снят в предел (пусто — не этим
                // правилом); у строк `meta` и `component` оба пусты всегда.
                var unmeasurableAt = new Dictionary<string, string>(StringComparer.Ordinal);
                if (result.ChainLimits != null)
                {
                    foreach (FsaChainLimit verdict in result.ChainLimits)
                    {
                        if (verdict.Limited)
                        {
                            unmeasurableAt[verdict.Member] = verdict.Reference;
                        }
                    }
                }

                w.WriteLine("section,name,kind,detected,count_rate,z,decision_threshold_rate,detection_limit_rate,"
                            + "peak_counts,share_pct,chain_root,decay_chain_root,total_yield_pct,limit_peak_counts,degenerate,collinearity,tied_to,unmeasurable_at");
                w.WriteLine("meta,live_time,,,{0},,,,,,,,,,,,,", R(result.LiveTime));
                w.WriteLine("meta,chi2ndf,,,{0},,,,,,,,,,,,,", R(result.Chi2Ndf));
                w.WriteLine("meta,chi2ndf_pois,,,{0},,,,,,,,,,,,,", R(result.Chi2NdfPoisson));
                w.WriteLine("meta,model_residual,,,{0},,,,,,,,,,,,,", R(result.ModelResidual));
                w.WriteLine("meta,gain,,,{0},,,,,,,,,,,,,", R(result.Gain));
                w.WriteLine("meta,offset_channels,,,{0},,,,,,,,,,,,,", R(result.OffsetChannels));
                w.WriteLine("meta,cascade_summing,,,{0},,,,,,,,,,,,,", result.CascadeSummingUsed ? "1" : "0");
                w.WriteLine("meta,response_matrix,,,{0},,,,,,,,,,,,,", result.ResponseMatrixUsed ? "1" : "0");
                if (result.Components != null)
                {
                    foreach (FsaComponentResult c in result.Components)
                    {
                        w.WriteLine("component,{0},{1},1,{2},{3},{4},{5},{6},{7},{8},{9},,,,,{10},",
                                    Q(c.Name), c.Kind, R(c.CountRate), R(c.Z),
                                    R(c.DecisionThresholdRate), R(c.DetectionLimitRate),
                                    R(c.PeakCounts), R(c.SharePercent),
                                    Q(c.ChainRoot), Q(c.DecayChainRoot), Q(c.TiedTo));
                    }
                }

                if (result.CharacteristicLimits != null)
                {
                    foreach (FsaCharacteristicLimit L in result.CharacteristicLimits)
                    {
                        string at;
                        w.WriteLine("limit,{0},{1},{2},{3},,{4},{5},,,,{6},{7},{8},{9},{10},{11},{12}",
                                    Q(L.Name), L.Kind, L.Detected ? "1" : "0", R(L.CountRate),
                                    R(L.DecisionThresholdRate), R(L.DetectionLimitRate),
                                    Q(L.DecayChainRoot), R(L.TotalYieldPercent),
                                    R(L.DetectionLimitPeakCounts), L.Degenerate ? "1" : "0",
                                    R(L.Collinearity), Q(L.TiedTo),
                                    unmeasurableAt.TryGetValue(L.Name, out at) ? Q(at) : "");
                    }
                }

                // (`S173`, `S175`) Отвязанные хвосты — своим разделом, ПОСЛЕ
                // прежних строк: отсчёты хвоста в столбце `peak_counts` (это
                // отсчёты, а не скорость), компонент — в `name`, адресат
                // (`layer`/`continuum`/`residual`) — в `kind`. Строки состава и
                // кандидатов выше не сдвинуты ни на столбец: сверка «до/после»
                // по ним остаётся построчной.
                if (result.UntiedTails != null)
                {
                    foreach (FsaUntiedTail tail in result.UntiedTails)
                    {
                        w.WriteLine("untied_tail,{0},{1},,,,,,{2},,,,,,,,,", Q(tail.Component),
                                    tail.Placement.ToString().ToLowerInvariant(), R(tail.Counts));
                    }
                }

                // (`S175`) Хвост в слоях — по компонентам, у кого он есть:
                // отсчёты в `peak_counts`.
                if (result.Components != null)
                {
                    foreach (FsaComponentResult c in result.Components)
                    {
                        if (c.TailCurve != null)
                        {
                            w.WriteLine("tail_in_layer,{0},{1},,,,,,{2},,,,,,,,,", Q(c.Name), c.Kind, R(c.TailCounts));
                        }
                    }
                }

                // (`S174`) Серый слой и полы отображения — своим разделом ПОСЛЕ
                // прежних строк: отсчёты серого слоя в `peak_counts`, доля в
                // `share_pct`; у полов кэВ в `count_rate`, канал в `peak_counts`;
                // невязка экрана (доли) в `count_rate`.
                double greyBelow, greyAbove;
                GreySplit(result, result.BuildStackedLayers(FsaResult.DefaultMaxNamedLayers), out greyBelow, out greyAbove);
                double total = result.StackTotal;
                w.WriteLine("grey,below_floor,,,,,,,{0},{1},,,,,,,,", R(greyBelow), R(total > 0.0 ? 100.0 * greyBelow / total : 0.0));
                w.WriteLine("grey,above_lines,,,,,,,{0},{1},,,,,,,,", R(greyAbove), R(total > 0.0 ? 100.0 * greyAbove / total : 0.0));
                w.WriteLine("grey,spread_floor,,,{0},,,,{1},,,,,,,,,", R(result.ContinuumSpreadFloorKev), result.ContinuumSpreadFloorChannel);
                w.WriteLine("grey,residual_floor,,,{0},,,,{1},,,,,,,,,", R(result.ResidualFloorKev), result.ResidualFloorChannel);
                w.WriteLine("grey,residual_missing,,,{0},,,,,,,,,,,,,", R(result.ResidualMissingShare));
                w.WriteLine("grey,residual_excess,,,{0},,,,,,,,,,,,,", R(result.ResidualExcessShare));
            }
        }

        /// <summary>
        /// (`S174`) Серый слой стека надвое: отсчёты ниже
        /// <see cref="FsaResult.ContinuumSpreadFloorChannel"/> (подложка ниже
        /// порога доверия матрицы) и от него и выше (хвост выше последней
        /// линии). Слоя нет — оба нуля.
        /// </summary>
        static void GreySplit(FsaResult result, List<FsaStackLayer> layers, out double below, out double above)
        {
            below = 0.0;
            above = 0.0;
            foreach (FsaStackLayer layer in layers)
            {
                if (!string.Equals(layer.Name, FsaResult.ContinuumLayerName, StringComparison.Ordinal) || layer.Curve == null)
                {
                    continue;
                }

                for (int i = 0; i < layer.Curve.Length; i++)
                {
                    if (i < result.ContinuumSpreadFloorChannel) below += layer.Curve[i];
                    else above += layer.Curve[i];
                }
            }
        }

        /// <summary>
        /// (`S174`) Невязка в отсчётах по двум полосам — ниже пола `Min_Range`
        /// и от него: измерение фита, верх стека, «не описано» (измерение выше
        /// модели), «лишнее» (модель выше измерения) — в отсчётах, и доли от
        /// измеренных отсчётов той же полосы, как считает результат.
        /// </summary>
        static void PrintResidualBands(FsaResult result, int[] raw)
        {
            double[] fit = result.FitSpectrum(raw);
            int split = Math.Max(result.FirstChannel, Math.Min(result.ResidualFloorChannel, result.LastChannel + 1));
            var bands = new[]
            {
                new { Name = "ниже Min_Range (лента, не в проценте)", Lo = result.FirstChannel, Hi = split - 1 },
                new { Name = "от Min_Range (число строки отчёта)", Lo = split, Hi = result.LastChannel }
            };
            foreach (var band in bands)
            {
                double measured = 0.0, model = 0.0, missing = 0.0, excess = 0.0;
                int channels = 0;
                for (int i = band.Lo; i <= band.Hi && i < fit.Length; i++)
                {
                    double m = At(result.Model, i);
                    measured += fit[i];
                    model += m;
                    double d = fit[i] - m;
                    if (d > 0.0) missing += d; else excess -= d;
                    channels++;
                }

                Console.WriteLine("невязка (S174) {0}: каналы {1}..{2} ({3}), измерение фита {4}, модель {5}, не описано {6} ({7} %), лишнее {8} ({9} %)",
                                  band.Name, band.Lo, band.Hi, channels,
                                  measured.ToString("F1", CultureInfo.InvariantCulture),
                                  model.ToString("F1", CultureInfo.InvariantCulture),
                                  missing.ToString("F1", CultureInfo.InvariantCulture),
                                  (measured > 0.0 ? 100.0 * missing / measured : 0.0).ToString("F2", CultureInfo.InvariantCulture),
                                  excess.ToString("F1", CultureInfo.InvariantCulture),
                                  (measured > 0.0 ? 100.0 * excess / measured : 0.0).ToString("F2", CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        /// (`S174`) ПОДСАДКА ТОЛЬКО ДЛЯ ПРОБЫ: снять оба пола отображения —
        /// сплайн подложки разносится по слоям и ниже порога доверия, невязка
        /// считается по всей полосе фита, — ровно так лежало до 14.09.2026.
        /// Доли и невязка пересчитываются ТЕМИ ЖЕ правилами результата
        /// (<see cref="FsaResult.ComputeComponentShares"/>,
        /// <see cref="FsaResult.ComputeResidualShares"/>); фит не трогается.
        /// </summary>
        static void PlantSpreadBack(FsaResult result, int[] raw)
        {
            result.ContinuumSpreadFloorChannel = 0;
            result.ContinuumSpreadFloorKev = 0.0;
            result.ResidualFloorChannel = 0;
            result.ComputeComponentShares();
            result.ComputeResidualShares(raw);
        }

        static string R(double v)
        {
            return double.IsNaN(v) ? "" : v.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// (`S173`) ПОДСАДКА ТОЛЬКО ДЛЯ ПРОБЫ: сложить отвязанные хвосты обратно в
        /// подложку и верх стека — ровно так они лежали до 14.09.2026, когда
        /// колонка хвоста складывалась в `Continuum` и разнос подложки отдавал
        /// её слоям нуклидов. Доли пересчитываются тем же
        /// <see cref="FsaResult.ComputeComponentShares"/>, что у разбора.
        /// Фит (амплитуды, z, χ²/ndf, пределы) не трогается. ⚠ Не бит в бит с
        /// HEAD: там хвосты складывались в подложку ДО шапок сплайна, здесь —
        /// после; расхождение — в последних разрядах double, F3 на экране его
        /// не видит.
        /// </summary>
        static void PlantTailBack(FsaResult result)
        {
            if (result.UntiedTail == null)
            {
                return;
            }

            for (int i = 0; i < result.UntiedTail.Length; i++)
            {
                double tail = result.UntiedTail[i];
                if (result.Continuum != null && i < result.Continuum.Length)
                {
                    result.Continuum[i] += tail;
                }

                if (result.Model != null && i < result.Model.Length)
                {
                    result.Model[i] += tail;
                }
            }

            result.UntiedTail = null;
            result.UntiedTails.Clear();
            result.ComputeComponentShares();
        }

        /// <summary>
        /// (`S173`, `S175`) Отвязанные хвосты результата одной строкой: «Pb-212
        /// 1008.0 (layer), Bi-214 12.3 (layer); всего 1020.3 отсч.» либо «нет».
        /// </summary>
        static string TailNote(FsaResult result)
        {
            if (result.UntiedTails == null || result.UntiedTails.Count == 0)
            {
                return "нет";
            }

            var parts = new List<string>();
            double total = 0.0;
            foreach (FsaUntiedTail tail in result.UntiedTails)
            {
                parts.Add(tail.Component + " " + tail.Counts.ToString("F1", CultureInfo.InvariantCulture)
                          + " (" + tail.Placement.ToString().ToLowerInvariant() + ")");
                total += tail.Counts;
            }

            return string.Join(", ", parts.ToArray()) + "; всего "
                   + total.ToString("F1", CultureInfo.InvariantCulture) + " отсч.";
        }

        /// <summary>
        /// (`S175`) Хвосты, лежащие в слоях, по компонентам: «Cs-137 13984949.0,
        /// Xray-Pb 433210.1; всего … отсч., из них ниже пола разноса …» либо «нет».
        /// У члена ряда — его доля хвоста колонки.
        /// </summary>
        static string TailInLayersNote(FsaResult result)
        {
            var parts = new List<string>();
            double total = 0.0, below = 0.0;
            foreach (FsaComponentResult c in result.Components)
            {
                if (c.TailCurve == null)
                {
                    continue;
                }

                parts.Add(c.Name + " " + c.TailCounts.ToString("F1", CultureInfo.InvariantCulture));
                total += c.TailCounts;
                for (int i = 0; i < c.TailCurve.Length && i < result.ContinuumSpreadFloorChannel; i++)
                {
                    below += c.TailCurve[i];
                }
            }

            if (parts.Count == 0)
            {
                return "нет";
            }

            return string.Join(", ", parts.ToArray()) + "; всего "
                   + total.ToString("F1", CultureInfo.InvariantCulture) + " отсч., из них ниже пола разноса "
                   + below.ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// (`S175`, мера п. 7) Разнос сплайна по слоям (`S76`) ниже и выше
        /// канала <paramref name="floor"/> (порог доверия матрицы,
        /// <paramref name="floorKev"/>): у каждого слоя, за которым стоит
        /// компонент, — отсчёты, добавленные разносом (лента − образ⁺ − хвост),
        /// в двух полосах, и доля разнесённого в ленте слоя целиком; итог — Σ по
        /// слоям против сплайна в полосе (разность — серый слой). Слои — полный
        /// стек, без свёртки «прочего». Строки `SPREAD`: слой, ниже порога,
        /// выше порога, лента целиком, доля разнесённого в ленте (%).
        /// </summary>
        static void PrintSpreadByFloor(FsaResult result, int floor, double floorKev)
        {
            var byName = new Dictionary<string, FsaComponentResult>(StringComparer.Ordinal);
            foreach (FsaComponentResult c in result.Components)
            {
                byName[c.Name] = c;
            }

            double splineBelow = 0.0, splineAbove = 0.0;
            for (int i = 0; result.Continuum != null && i < result.Continuum.Length; i++)
            {
                if (!(result.Continuum[i] > 0.0)) continue;
                if (i < floor) splineBelow += result.Continuum[i]; else splineAbove += result.Continuum[i];
            }

            double totalBelow = 0.0, totalAbove = 0.0;
            var parts = new List<string>();
            foreach (FsaStackLayer layer in result.BuildStackedLayers(int.MaxValue))
            {
                FsaComponentResult c;
                if (!byName.TryGetValue(layer.Name, out c))
                {
                    continue;
                }

                double below = 0.0, above = 0.0, whole = 0.0;
                for (int i = 0; i < layer.Curve.Length; i++)
                {
                    whole += layer.Curve[i];
                    double own = i < c.Curve.Length && c.Curve[i] > 0.0 ? c.Curve[i] : 0.0;
                    double tail = c.TailCurve != null && i < c.TailCurve.Length ? c.TailCurve[i] : 0.0;
                    double spread = layer.Curve[i] - own - tail;
                    if (i < floor) below += spread; else above += spread;
                }

                totalBelow += below;
                totalAbove += above;
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} ниже {1:F1} / выше {2:F1} ({3:F2} % ленты)",
                                        layer.Name, below, above, whole > 0.0 ? 100.0 * (below + above) / whole : 0.0));
                Console.WriteLine("SPREAD\t{0}\t{1}\t{2}\t{3}\t{4}", layer.Name,
                                  below.ToString("F1", CultureInfo.InvariantCulture),
                                  above.ToString("F1", CultureInfo.InvariantCulture),
                                  whole.ToString("F1", CultureInfo.InvariantCulture),
                                  (whole > 0.0 ? 100.0 * (below + above) / whole : 0.0).ToString("F3", CultureInfo.InvariantCulture));
            }

            Console.WriteLine("разнос сплайна (S76, мера S175 п. 7) относительно порога доверия {0} кэВ (канал {1}): сплайн ниже {2} / выше {3} отсч., разнесено по слоям ниже {4} / выше {5} отсч.: {6}",
                              floorKev.ToString("G", CultureInfo.InvariantCulture), floor,
                              splineBelow.ToString("F1", CultureInfo.InvariantCulture),
                              splineAbove.ToString("F1", CultureInfo.InvariantCulture),
                              totalBelow.ToString("F1", CultureInfo.InvariantCulture),
                              totalAbove.ToString("F1", CultureInfo.InvariantCulture),
                              parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "нет");
        }

        /// <summary>
        /// (`S175`, второе решение) ПОДСАДКА ТОЛЬКО ДЛЯ ПРОБЫ: пол разноса
        /// подложки — на порог доверия матрицы тем же правилом, что ставил
        /// П69 (`FsaAnalyzer.FloorChannel`); сплайн ниже порога уходит в серый
        /// слой, доли пересчитываются правилами результата. Фит не трогается.
        /// </summary>
        static void PlantGreyFloor(FsaResult result, double floorKev, EnergyCalibration calibration)
        {
            result.ContinuumSpreadFloorKev = floorKev;
            result.ContinuumSpreadFloorChannel = FsaAnalyzer.FloorChannel(calibration, floorKev, result.FirstChannel, result.LastChannel);
            result.ComputeComponentShares();
        }

        /// <summary>
        /// (`S175`) ПОДСАДКА ТОЛЬКО ДЛЯ ПРОБЫ: переложить хвосты образов из их
        /// слоёв в подложку — ровно так они лежали до П68 (`a6f2b227`) и у П70
        /// (`d5c94425`), когда колонка хвоста складывалась в `Continuum`.
        /// Верх стека не меняется (хвост в нём был и есть), доли
        /// пересчитываются тем же <see cref="FsaResult.ComputeComponentShares"/>,
        /// что у разбора; невязка та же. Фит (амплитуды, z, χ²/ndf, пределы) не
        /// трогается. Адресат в `UntiedTails` переписывается на `continuum`,
        /// чтобы строка пробы не врала. ⚠ Не бит в бит: там хвосты
        /// складывались в подложку ДО шапок сплайна, здесь — после;
        /// расхождение — в последних разрядах double, F3 на экране его не видит.
        /// </summary>
        static void PlantTailToContinuum(FsaResult result)
        {
            bool moved = false;
            foreach (FsaComponentResult c in result.Components)
            {
                if (c.TailCurve == null)
                {
                    continue;
                }

                for (int i = 0; i < c.TailCurve.Length && i < result.Continuum.Length; i++)
                {
                    result.Continuum[i] += c.TailCurve[i];
                }

                c.TailCurve = null;
                c.TailCounts = 0.0;
                moved = true;
            }

            if (!moved)
            {
                return;
            }

            foreach (FsaUntiedTail tail in result.UntiedTails)
            {
                if (tail.Placement == FsaTailPlacement.Layer)
                {
                    tail.Placement = FsaTailPlacement.Continuum;
                }
            }

            result.ComputeComponentShares();
        }

        static string Q(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }

            return s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }

        static FieldInfo Field(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    return field;
                }
            }

            throw new InvalidOperationException("нет поля " + name + " у " + type.Name);
        }

        /// <summary>
        /// Что вышло у расчёта полуширины выделения и по каким отсчётам (`A45`).
        ///
        /// Жёлтые линии в окне рисуются ровно по этим числам, поэтому сверять надо
        /// их: вершина и полувысота обязаны стоять в шкале спектра ЗА ВЫЧЕТОМ
        /// ФОНА — того, что нарисован. Рядом печатаются сырое и чистое значения
        /// того же канала: по какому из них считалось, видно без гадания.
        /// </summary>
        static void ReportFwhm(EnergySpectrumView view, EnergySpectrum spectrum)
        {
            object analytics = Field(typeof(EnergySpectrumView), "selectionAnalytics").GetValue(view);
            if (analytics == null)
            {
                Console.WriteLine("полуширина: разбор выделения пуст");
                return;
            }

            object result = analytics.GetType().GetProperty("FwhmResult") != null
                ? analytics.GetType().GetProperty("FwhmResult").GetValue(analytics, null)
                : null;
            if (result == null)
            {
                Console.WriteLine("полуширина: не посчиталась (пик не разрешён в окне)");
                return;
            }

            double maxChannel = (double)Member(result, "MaxChannel");
            double maxValue = (double)Member(result, "MaxValue");
            double halfValue = (double)Member(result, "HalfValue");
            double baseValue = (double)Member(result, "MaxBaseValue");
            double resolution = (double)Member(result, "Resolution");

            int channel = (int)maxChannel;
            double raw = channel >= 0 && channel < spectrum.Spectrum.Length
                ? spectrum.Spectrum[channel]
                : double.NaN;
            PropertyInfo netProperty = typeof(EnergySpectrumView).GetProperty(
                "FsaNetSpectrum", BindingFlags.Instance | BindingFlags.NonPublic);
            double[] net = netProperty != null ? (double[])netProperty.GetValue(view, null) : null;
            double clean = net != null && channel >= 0 && channel < net.Length
                ? net[channel]
                : double.NaN;

            // ⛔ `P` не применяется (`T247`): выше 1000 % он ставит разделитель
            //    разрядов, а группировки разрядов нет вовсе (решение Amber
            //    05.09.2026). Процент — множителем 100.0 и знаком в тексте.
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                              "полуширина: вершина канал {0}, значение {1:F0}; полувысота {2:F0}; подложка {3:F0}; ПШПВ {4:F2} %",
                              channel, maxValue, halfValue, baseValue, 100.0 * resolution));
            Console.WriteLine("            в этом канале: сырых {0:F0}, за вычетом фона {1:F0} — считалось по {2}",
                              raw, clean,
                              Math.Abs(maxValue - clean) < Math.Abs(maxValue - raw)
                                  ? "ЧИСТОМУ (верно)"
                                  : "СЫРОМУ (баг A45)");
        }

        static object Member(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name);
            if (property != null)
            {
                return property.GetValue(target, null);
            }

            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field != null ? field.GetValue(target) : null;
        }

        static void Set(object target, string name, object value)
        {
            Field(target.GetType(), name).SetValue(target, value);
        }

        /// <summary>Позвать закрытый метод вида — как это делает окно.</summary>
        static object Invoke(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null)
            {
                throw new InvalidOperationException("нет метода " + name + " у " + target.GetType().Name);
            }

            return method.Invoke(target, args);
        }

        /// <summary>
        /// Поставить набор нуклидов активным — то же поле, что правит панель
        /// поиска пиков (<c>NuclideDefinitionManager.ActiveSet</c>). Промах по
        /// имени — отказ со списком того, что есть: молчаливый откат на «все
        /// нуклиды» дал бы другой состав и выглядел бы как работающий прогон.
        /// </summary>
        static bool SelectSet(NuclideDefinitionManager nuclides, string name)
        {
            var have = new List<string>();
            foreach (NuclideSet set in nuclides.NuclideSets)
            {
                if (set == null)
                {
                    continue;
                }

                have.Add(set.Name);
                if (string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    nuclides.ActiveSet = set;
                    Console.WriteLine("набор: {0}", set.Name);
                    return true;
                }
            }

            Console.Error.WriteLine("набора «{0}» нет; есть: {1}", name, string.Join(", ", have.ToArray()));
            return false;
        }

        static bool AttachEfficiency(ResultData rd, string name)
        {
            foreach (DeviceConfigInfo device in DeviceConfigManager.GetInstance().DeviceConfigList)
            {
                foreach (EfficiencyConfigData curve in device.EfficiencyConfigs)
                {
                    if (string.Equals(curve.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        rd.Efficiency = curve.Copy();
                        return true;
                    }
                }
            }

            Console.Error.WriteLine("кривая «{0}» не нашлась", name);
            return false;
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            // ⛔ Прибор и его настройки поиска пиков — ОДНИМ правилом на все
            // пробы (`ProbeDeviceConfig`, строка `S82`). Прежде здесь стояла
            // своя копия правила, и она молча брала умолчания библиотеки:
            // `ResultData.DeviceConfig` заведён полем `= new DeviceConfigInfo()`,
            // то есть после чтения файла НЕ null, а пустой, и проверка «прибор
            // есть?» проходила. Проба искала пики допуском 10 в диапазоне
            // 30…2800 кэВ там, где у человека 11 и от 5 кэВ.
            Console.WriteLine("SETUP\tприбор: {0}", ProbeDeviceConfig.Attach(rd));

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(
                        cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            return rd;
        }
    }
}
