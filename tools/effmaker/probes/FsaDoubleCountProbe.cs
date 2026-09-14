using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaDoubleCountProbe
{
    /// <summary>
    /// Читатель двух правил `A145` (этап 1): вылеты (`A168`) и обратное
    /// рассеяние (`A170`) НЕ дают двойного образа ни с матрицей, ни без неё,
    /// а происхождение ряда (`A169`) не зависит от связки равновесием.
    ///
    ///     fsadoublecountprobe --spectrum=<файл> --chain=Th-232 [--nuclides=40K,137CS]
    ///                         [--crystal=Cs,I] [--no-matrix]
    ///
    /// Запускать ИЗ ОСНАСТКИ (`wd_*`): матрица берётся у корешка спектра
    /// через `ResponseMatrixStore`, а тот ищет `config\device\response` от
    /// каталога исполняемого файла. Состав объявляется ключами — поставочная
    /// библиотека нуклидов к спектру не предъявляется (правило Amber
    /// 01.09.2026): пики подписываются определениями, собранными из nucdb по
    /// ОБЪЯВЛЕННОМУ составу, и из них же строится библиотека «по пикам».
    /// ⛔ С 12.09.2026 (`AMBER19`, П11) `NuclideDefinitionManager` не
    /// поднимается ВОВСЕ — в оснастке корпуса файла нет, и подъём падал
    /// броском (`S100`); метки `--chain=` — словарь манифеста
    /// (`FsaSampleChain.FromLabel`), `--sample=` — то же, что `--nuclides=`,
    /// спецификация — общим входом `FsaSampleSpec.FromManifest` (`T257`).
    /// В конце печатается счётчик обращений к менеджеру; не ноль — код 12.
    ///
    /// ЧТО МЕРИТСЯ, числом:
    ///
    ///   1. `A168`, четыре клетки {матрица есть/нет} × {вылеты вкл/выкл} на
    ///      библиотеке «по пикам» (только она строит SE/DE): число SE/DE и
    ///      `Ann-511`, прошедших в разбор (состав + подавленные), χ²/ndf и
    ///      невязка. Двойной образ = матрица применена И SE/DE прошли — ни в
    ///      одной клетке его нет; без матрицы при «выкл» вылетов нет, при «вкл»
    ///      есть.
    ///      ⛔ Положительный контроль: гейт `EscapeGate` снят при живой матрице
    ///      — ловушка ОБЯЗАНА сработать, иначе проба ничего не ловит.
    ///   2. `A170`: `BackscatterWithMatrix`, поднятый напрямую при матрице,
    ///      возвращает образ обратного рассеяния поверх неё — ловушка обязана
    ///      сработать; через фасад `FsaCalculationOptions` ключ не поднимается.
    ///   3. `A169`, библиотека из баз, равновесие вкл/выкл: число членов ряда с
    ///      непустым `DecayChainRoot` в РЕЗУЛЬТАТЕ — по объединению состава,
    ///      пределов (`CharacteristicLimits`) и подавленных образов — одинаково
    ///      при обоих положениях и равно числу членов ряда в библиотеке.
    ///      ⚠ Считать по одному составу нельзя, и это измерено (05.09.2026, B4b):
    ///      без равновесия свободный член без значимых линий (Po-216, Rn-220,
    ///      Th-228 у ряда Th-228) в состав не входит вовсе и живёт в результате
    ///      только строкой предела либо подавленного образа — строка состава
    ///      «на каждого члена» есть ТОЛЬКО при связке, где колонка ряда
    ///      раскладывается на всех. С непустым `ChainRoot` — при равновесии
    ///      столько же, сколько членов, без него ноль; `ParentGroupingAllowed` —
    ///      да/нет соответственно; при равновесии сумма кривых членов ряда
    ///      равна остатку модели за вычетом всего прочего с машинным допуском
    ///      (разложение колонки без потерь); доли слоёв линейны по кривым с
    ///      общим знаменателем, и то же тождество покрывает их.
    ///      Положительный контроль: спектр без ряда (`--chain=` не задан) —
    ///      `DecayChainRoot` пуст у всех и родительский режим недопустим.
    ///   4. `S141`, библиотека ИЗ БАЗ (`FsaSampleLibrary.Build`, ею идёт весь
    ///      корпус): SE/DE в НЕЙ построены, без матрицы проходят в разбор, с
    ///      матрицей их снимает гейт, выключенный ключ снимает и без матрицы.
    ///      ⛔ Положительный контроль двойной: сперва «SE/DE в библиотеке есть»
    ///      (на сборке без правки `S141` эта проверка ОТКАЗЫВАЕТ, и клетка «с
    ///      матрицей 0» перестаёт быть доказательством), потом снятый гейт при
    ///      живой матрице — двойной образ обязан вернуться.
    ///   5. `S172` (решение Amber 14.09.2026 «При матрице вылет не класть»),
    ///      библиотека ИЗ БАЗ: образ K-вылета КРИСТАЛЛА `Esc-<вещество>` (флаг
    ///      `CrystalEscape`) при живой матрице снят тем же гейтом `EscapeGate`
    ///      (счётчик `CrystalEscapeDropped` = числу образов), без матрицы
    ///      проходит в разбор. Рядом числом — судьба образа рентгена кристалла
    ///      (`FromCrystal`, гейт `AMBER4`). ⛔ Положительный контроль: гейт
    ///      снят при матрице — образ обязан вернуться, счётчик — молчать.
    ///   6. `S173` → `AMBER30` → `S175` (решение Amber 14.09.2026 «Чини S174
    ///      так, чтобы выглядело физически корректным»), при живой матрице.
    ///      Умолчание (плечо А, `FsaAnalyzer.UntiedTailAsResidual` опущен):
    ///      отвязанный хвост образа лежит В ЛЕНТЕ И ДОЛЕ СВОЕГО ОБРАЗА
    ///      (`FsaComponentResult.TailCurve`; у ряда со связкой — у членов),
    ///      серым ниже пола разноса остаётся один сплайн; `UntiedTail` пуст,
    ///      `UntiedTails` называет хвосты с адресатом `Layer`. Плечо Б (ключ
    ///      поднят — правило `S173` как в П68): хвост в `UntiedTail`, ниже
    ///      порога доверия, в верх стека не входит, `FitModel` = верх + хвост.
    ///      Договор умолчания меряется ПАРОЙ плеч: фит один (χ²/ndf, усиление,
    ///      сдвиг); `Continuum`(А) = `Continuum`(Б) по каналам (хвост не в
    ///      подложке); Σ `TailCurve`(А) = `UntiedTail`(Б) по каналам (весь
    ///      хвост в слоях); `Model`(А) = `Model`(Б) + хвост(Б); лента каждого
    ///      слоя А = образ⁺ + хвост + доля сплайна ≥ 0, и Σ долей по слоям =
    ///      сплайн − серый канал в канал (второе решение Amber «В слои
    ///      образов по S76 везде»: хвост входит в веса разноса, слои двух
    ///      плеч расходятся и на перераспределённый сплайн — потому
    ///      проверяется разбиение, а не «А = Б + хвост»); «не описано» в А не
    ///      больше, чем в Б; Σ слоёв = `Model`.
    ///      ⛔ Положительный контроль двойной: плечо Б, выданное за умолчание
    ///      (яма снова на экране), — договор отказывает; подсадка «хвост в
    ///      подложку» на плече А (картинка до П68 / П70) — договор отказывает,
    ///      хвост уходит из слоёв в подложку, χ²/ndf тот же. И прежняя
    ///      подсадка «хвост снова в подложку» на плече Б — договор `S173`
    ///      отказывает.
    ///   7. `S174` п. 2 («Только в диапазоне прибора») и `S175`, второе
    ///      решение Amber 14.09.2026 («В слои образов по S76 везде»; п. 1
    ///      `S174` отменён), при живой матрице: пол разноса подложки = 0 —
    ///      сплайн разносится по слоям по всей шкале, серый слой есть только
    ///      там, где от канала и выше нет ни одного слоя образов (хвост
    ///      подложки выше последней линии), и там равен сплайну; пол невязки
    ///      стоит на `Min_Range` прибора, и число «не описано»/«лишнее» равно
    ///      независимому счёту пробы от этого пола; Σ слоёв (с серым) =
    ///      `Model`. ⛔ Положительные контроли: подсадка «сплайн ниже порога
    ///      серым» (пол разноса на пороге доверия тем же `FloorChannel` —
    ///      картинка П69/П70) — договор отказывает, серый слой ниже порога =
    ///      сплайну там, доля носителя падает, χ²/ndf тот же; подсадка
    ///      «невязка по всей полосе» (пол невязки 0) — договор отказывает,
    ///      невязка равна независимому счёту по всей полосе.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0. Всё, что мерится, печатается строками
    /// `CELL`/`SDE`/`ESC`/`CHAIN`/`TAIL`/`GREY`, чтобы числа можно было положить в журнал.
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей: полоса это статика,
            // отражение её не видит, и снятая позже она уже могла быть уведена.
            FsaTuningReport.Snapshot();

            string spectrumPath = null;
            var chains = new List<string>();
            var nuclides = new List<string>();
            var crystal = new List<string>();
            bool wantMatrix = true;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--chain=", StringComparison.Ordinal)) chains.AddRange(a.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--nuclides=", StringComparison.Ordinal)) nuclides.AddRange(a.Substring(11).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--sample=", StringComparison.Ordinal)) nuclides.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--crystal=", StringComparison.Ordinal)) crystal.AddRange(a.Substring(10).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a == "--no-matrix") wantMatrix = false;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            if (chains.Count == 0 && nuclides.Count == 0)
            {
                Console.Error.WriteLine("нужен состав: --chain=Th-232 и/или --nuclides=40K");
                return 2;
            }

            // Метки рядов проверяются ДО чтения спектра — тем же словарём, каким
            // их читает манифест корпуса (`FsaSampleChain.FromLabel`).
            foreach (string label in chains)
            {
                if (FsaSampleChain.FromLabel(label) == null)
                {
                    Console.Error.WriteLine("--chain={0}: неизвестный ряд; известные: {1}",
                                            label, string.Join(", ", FsaSampleChain.KnownLabels));
                    return 2;
                }
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            // ⛔ `NuclideDefinitionManager` здесь НЕ поднимается (`AMBER19`, П11).

            ResultData rd = Load(spectrumPath);
            if (rd == null)
            {
                return 2;
            }

            Console.WriteLine("спектр  : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор  : {0}", ProbeDeviceConfig.Attach(rd));

            // (П65, 14.09.2026) Калибровка ПШПВ — как у приложения и `FsaStackShot`:
            // у спектра без своей кривой берётся умолчание прибора
            // (`FwhmCalibration.DefaultCalibration`, тот же путь, что
            // `DocumentManager` при открытии файла). Без этого поиск пиков
            // (`PeakFilter`) падал `NullReferenceException` на полевом спектре
            // ASN16 «Радон деревня» — молчаливый отказ без причины, и плечо
            // HEAD падало так же.
            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig fwhmConfig)
            {
                if (fwhmConfig.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    fwhmConfig.FwhmCalibration = FwhmCalibration.DefaultCalibration(
                        fwhmConfig, rd.EnergySpectrum.EnergyCalibration);
                }

                if (fwhmConfig.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = fwhmConfig.FwhmCalibration.Clone();
                    Console.WriteLine("ПШПВ    : у спектра своей калибровки нет — взято умолчание прибора");
                }
            }

            if (rd.FwhmCalibration == null)
            {
                Console.Error.WriteLine("у спектра нет калибровки ПШПВ и прибор её не даёт — поиск пиков невозможен");
                return 2;
            }

            // Матрица — тем же путём, что приложение и корпусная проба.
            ResponseMatrix matrix = null;
            string material = null;
            if (wantMatrix && rd.Efficiency != null && rd.Efficiency.HasGeometry
                && rd.Efficiency.UseResponseMatrix)
            {
                ResponseMatrix loaded = ResponseMatrixStore.Load(rd.Efficiency.Guid);
                if (loaded != null && loaded.IsValidFor(rd.Efficiency.Geometry))
                {
                    matrix = loaded;
                    material = EfficiencySimulator.ScintillatorNameOf(rd.Efficiency.Geometry);
                }
            }

            // ⛔ Отказ обязан НАЗЫВАТЬ причину: «матрица НЕТ» без разбора —
            // молчаливый отказ, и на нём уже потерян час (06.09.2026, полоса
            // П5): половина приёмки `S141` меряется только при живой матрице,
            // а склад корпуса стоит на прежней версии физики.
            if (matrix == null)
            {
                string why;
                if (!wantMatrix) why = "запрещена ключом --no-matrix";
                else if (rd.Efficiency == null) why = "у спектра нет кривой";
                else if (!rd.Efficiency.HasGeometry) why = "у кривой нет геометрии";
                else if (!rd.Efficiency.UseResponseMatrix) why = "матрица выключена в кривой";
                else
                {
                    ResponseMatrix loaded = ResponseMatrixStore.Load(rd.Efficiency.Guid);
                    why = loaded == null
                        ? "в складе нет файла на Guid " + rd.Efficiency.Guid
                        : "файл есть, но НЕ ГОДЕН для этой геометрии (клеймо: версия физики, формат или сама геометрия)";
                }

                Console.WriteLine("матрица : НЕТ — {0}", why);
            }
            else
            {
                Console.WriteLine("матрица : есть, {0}", material);
            }

            // Спецификация состава — как `CorpusFsaProbe.SpecOf`, без manifest.
            FsaSampleSpec spec = SpecOf(rd, chains, nuclides, crystal);
            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);

            // ------------------------------------------------------------------
            // A168 + A170: библиотека ПО ПИКАМ — единственная, что строит SE/DE.
            // ------------------------------------------------------------------
            List<FsaComponent> sample = FsaSampleLibrary.Build(spec);
            List<NuclideDefinition> definitions = FsaSampleLibrary.AsDefinitions(sample);
            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None, null, definitions);
            List<FsaComponent> byPeaks = FsaLibrary.BuildFromPeaks(
                peaks, definitions, spec.CrystalFractions.Count > 0 ? spec.CrystalFractions : null);
            Console.WriteLine("пиков {0}, библиотека по пикам {1} образов: SE/DE {2}, Ann-511 {3}",
                              peaks.Count, byPeaks.Count, CountEscape(byPeaks), CountAnnihilation(byPeaks));

            // ⛔ Образ вылета — МЕШАЮЩИЙ, и нуклидным компонентом стать не
            // может (`S141`). Признак нуклидного имени — массовое число, а в
            // «SE-2615» цифры есть: подпись вылета, попав в состав, забрала бы
            // долю активности и сама стала бы родителем вылета. Поймано
            // 06.09.2026, когда корпусная библиотека начала строить SE/DE и её
            // же подписи поехали в финдер.
            int escapeAsNuclide = 0;
            foreach (FsaComponent component in byPeaks)
            {
                if (FsaLibrary.IsEscapeImage(component.Name)
                    && component.Kind != FsaComponentKind.Nuisance)
                {
                    Console.WriteLine("⛔ образ вылета «{0}» встал в состав видом {1}",
                                      component.Name, component.Kind);
                    escapeAsNuclide++;
                }
            }

            Same("по пикам: образов вылета среди нуклидных компонентов нет", 0, escapeAsNuclide);

            if (CountEscape(byPeaks) == 0)
            {
                Console.WriteLine("⛔ в библиотеке нет ни одного SE/DE — состав без линии выше 1022 кэВ, клетки A168 мерить нечем");
                bad++;
            }

            Console.WriteLine();
            Console.WriteLine("=== A168: {матрица есть/нет} × {вылеты вкл/выкл}, библиотека по пикам ===");
            Console.WriteLine("CELL\tматрица\tвылеты\tSE/DE\tAnn-511\tchi2/ndf\tневязка_%\tдвойной_образ");

            var cells = new List<bool[]>();
            if (matrix != null)
            {
                cells.Add(new[] { true, true });
                cells.Add(new[] { true, false });
            }

            cells.Add(new[] { false, true });
            cells.Add(new[] { false, false });

            foreach (bool[] cell in cells)
            {
                bool withMatrix = cell[0], escapeOn = cell[1];
                FsaAnalyzer analyzer = NewAnalyzer(rd, withMatrix ? matrix : null, material);
                var options = new FsaCalculationOptions { EscapeAndAnnihilation = escapeOn };
                options.ApplyTo(analyzer);

                // (`T243`) ЧЕМ СЧИТАЛИ ЭТО ПЛЕЧО — ДО СЧЁТА И ВСЛУХ. Плечи
                // A/B здесь настраивают анализатор ИЗ КЛЮЧЕЙ (`--no-matrix`,
                // `--crystal=`), и до 06.09.2026 отличить их в выводе по
                // настройкам было нечем: болезнь ~~`S82`~~ внутри одного
                // прогона. Анализатор у каждого плеча СВОЙ (`NewAnalyzer`),
                // поэтому состояние прошлого разбора в отчёт не попадает.
                FsaTuningReport.Print(analyzer, Cell(withMatrix, escapeOn));
                FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                    rd.FwhmCalibration, byPeaks, efficiency);
                if (result == null)
                {
                    Console.WriteLine("CELL\t{0}\t{1}\tразложение не получилось", withMatrix, escapeOn);
                    bad++;
                    continue;
                }

                int escapes = PassedEscape(result);
                int annihilation = PassedAnnihilation(result);
                bool doubled = DoubleEscape(result);
                Console.WriteLine("CELL\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}",
                                  withMatrix ? "есть" : "нет", escapeOn ? "вкл" : "выкл",
                                  escapes, annihilation,
                                  result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture),
                                  (100.0 * result.ModelResidual).ToString("F1", CultureInfo.InvariantCulture),
                                  doubled ? "ДА" : "нет");

                Same(Cell(withMatrix, escapeOn) + ": двойного образа нет", false, doubled);
                if (withMatrix)
                {
                    Same(Cell(withMatrix, escapeOn) + ": матрица применена", true, result.ResponseMatrixUsed);
                    Same(Cell(withMatrix, escapeOn) + ": SE/DE отдельными образами нет", 0, escapes);
                }
                else
                {
                    Same(Cell(withMatrix, escapeOn) + ": SE/DE отдельными образами " + (escapeOn ? "есть" : "нет"),
                         escapeOn, escapes > 0);
                }

                // (`AMBER7`, 08.09.2026) `Ann-511` не предъявляется фиту, когда у
                // состава есть СВОЯ линия в окне 511 (у Th-ряда — Tl-208
                // 510.8 кэВ): гейт столкновения снимает образ, и ожидание «есть»
                // при столкновении было ложным отказом с 08.09 (найдено П11
                // 12.09.2026 на `G1S16_Th228_P5`, воспроизведено HEAD-сборкой
                // пробы с поставочным файлом — дефект ожидания, не разбора).
                bool collides = !string.IsNullOrEmpty(analyzer.AnnihilationCollides);
                bool expectAnnihilation = escapeOn && !collides;
                Same(Cell(withMatrix, escapeOn) + ": Ann-511 "
                     + (expectAnnihilation ? "есть" : collides ? "нет (столкновение с " + analyzer.AnnihilationCollides + ")" : "нет"),
                     expectAnnihilation, annihilation > 0);
            }

            if (matrix != null)
            {
                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ A168: старый двойной счёт вернуть
                // нарочно (гейт снят при живой матрице) — ловушка обязана
                // сработать. Не сработала — проба слепа, и все «нет» выше
                // ничего не значат.
                FsaAnalyzer analyzer = NewAnalyzer(rd, matrix, material);
                new FsaCalculationOptions().ApplyTo(analyzer);
                analyzer.EscapeGate = false;
                FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                    rd.FwhmCalibration, byPeaks, efficiency);
                bool caught = result != null && DoubleEscape(result);
                Console.WriteLine("CELL\tесть\tвкл, гейт СНЯТ\t{0}\t{1}\t{2}\t{3}\t{4}",
                                  result != null ? PassedEscape(result) : -1,
                                  result != null ? PassedAnnihilation(result) : -1,
                                  result != null ? result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture) : "-",
                                  result != null ? (100.0 * result.ModelResidual).ToString("F1", CultureInfo.InvariantCulture) : "-",
                                  caught ? "ДА (ловушка сработала)" : "НЕ ПОЙМАН");
                Same("положительный контроль A168: двойной образ ПОЙМАН", true, caught);

                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ A170: `BackscatterWithMatrix`, поднятый
                // напрямую, возвращает образ рассеяния поверх матрицы.
                Console.WriteLine();
                Console.WriteLine("=== A170: обратное рассеяние при матрице ===");
                analyzer = NewAnalyzer(rd, matrix, material);
                new FsaCalculationOptions { Backscatter = true }.ApplyTo(analyzer);
                Same("фасад: BackscatterWithMatrix остаётся false", false, analyzer.BackscatterWithMatrix);
                result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                          rd.FwhmCalibration, byPeaks, efficiency);
                int viaFacade = result != null ? PassedBackscatter(result) : -1;
                Console.WriteLine("BS\tчерез фасад\tобразов рассеяния {0}", viaFacade);
                Same("через фасад при матрице образа рассеяния нет", 0, viaFacade);

                analyzer = NewAnalyzer(rd, matrix, material);
                new FsaCalculationOptions { Backscatter = true }.ApplyTo(analyzer);
                analyzer.BackscatterWithMatrix = true;
                result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                          rd.FwhmCalibration, byPeaks, efficiency);
                int direct = result != null ? PassedBackscatter(result) : -1;
                bool bsCaught = result != null && result.ResponseMatrixUsed && direct > 0;
                Console.WriteLine("BS\tнапрямую =true\tобразов рассеяния {0}\t{1}", direct,
                                  bsCaught ? "ДВОЙНОЙ СЧЁТ (ловушка сработала)" : "НЕ ПОЙМАН");
                Same("положительный контроль A170: двойной счёт рассеяния ПОЙМАН", true, bsCaught);
            }
            else
            {
                Console.WriteLine("(матрицы нет — клетки «есть» и оба положительных контроля на этом спектре не меряются)");
            }

            // ------------------------------------------------------------------
            // S141: библиотека ИЗ БАЗ обязана строить SE/DE тем же правилом,
            // что путь по подписям пиков. До 06.09.2026 не строила вовсе, и
            // весь корпус (`--lib=sample`) шёл без образов вылета.
            //
            // ⛔ Гейт «с матрицей SE/DE нет» сам по себе НИЧЕГО не меряет: он
            // проходит и тогда, когда образов не построено вовсе. Поэтому
            // первой стоит проверка «в библиотеке из баз SE/DE ЕСТЬ» — на
            // сборке без правки она и есть отрицательное плечо контроля, — а
            // при матрице снятый гейт обязан вернуть двойной образ.
            // ------------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("=== S141: SE/DE на библиотеке ИЗ БАЗ ===");
            int sampleEscapes = CountEscape(sample);
            Console.WriteLine("библиотека из баз: образов {0}, SE/DE {1}, Ann-511 {2}",
                              sample.Count, sampleEscapes, CountAnnihilation(sample));
            foreach (FsaComponent component in sample)
            {
                if (FsaLibrary.IsEscapeImage(component.Name) && component.Lines.Count > 0)
                {
                    Console.WriteLine("      {0}\t{1} кэВ", component.Name,
                                      component.Lines[0].Energy.ToString("F2", CultureInfo.InvariantCulture));
                }
            }

            Same("библиотека из баз: SE/DE построены", true, sampleEscapes > 0);

            Console.WriteLine("SDE\tматрица\tвылеты\tSE/DE\tAnn-511\tchi2/ndf\tдвойной_образ");
            var sampleCells = new List<bool[]>();
            if (matrix != null)
            {
                sampleCells.Add(new[] { true, true });
            }

            sampleCells.Add(new[] { false, true });
            sampleCells.Add(new[] { false, false });
            foreach (bool[] cell in sampleCells)
            {
                bool withMatrix = cell[0], escapeOn = cell[1];
                FsaAnalyzer analyzer = NewAnalyzer(rd, withMatrix ? matrix : null, material);
                new FsaCalculationOptions { EscapeAndAnnihilation = escapeOn }.ApplyTo(analyzer);
                FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                    rd.FwhmCalibration, sample, efficiency);
                if (result == null)
                {
                    Console.WriteLine("SDE\t{0}\t{1}\tразложение не получилось", withMatrix, escapeOn);
                    bad++;
                    continue;
                }

                int escapes = PassedEscape(result);
                Console.WriteLine("SDE\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}",
                                  withMatrix ? "есть" : "нет", escapeOn ? "вкл" : "выкл",
                                  escapes, PassedAnnihilation(result),
                                  result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture),
                                  DoubleEscape(result) ? "ДА" : "нет");
                Same("из баз, " + Cell(withMatrix, escapeOn) + ": двойного образа нет",
                     false, DoubleEscape(result));
                if (withMatrix)
                {
                    Same("из баз, " + Cell(withMatrix, escapeOn) + ": матрица применена",
                         true, result.ResponseMatrixUsed);
                    Same("из баз, " + Cell(withMatrix, escapeOn) + ": SE/DE отдельными образами нет",
                         0, escapes);
                }
                else
                {
                    Same("из баз, " + Cell(withMatrix, escapeOn) + ": SE/DE "
                         + (escapeOn ? "есть" : "нет"), escapeOn, escapes > 0);
                }
            }

            if (matrix != null)
            {
                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ S141: гейт снят при живой матрице —
                // образы обязаны пройти и дать двойной счёт. Не прошли — значит
                // клетка «с матрицей 0» выше была пустой, а не запертой.
                FsaAnalyzer analyzer = NewAnalyzer(rd, matrix, material);
                new FsaCalculationOptions().ApplyTo(analyzer);
                analyzer.EscapeGate = false;
                FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                    rd.FwhmCalibration, sample, efficiency);
                bool caught = result != null && DoubleEscape(result);
                Console.WriteLine("SDE\tесть\tвкл, гейт СНЯТ\t{0}\t{1}\t{2}\t{3}",
                                  result != null ? PassedEscape(result) : -1,
                                  result != null ? PassedAnnihilation(result) : -1,
                                  result != null ? result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture) : "-",
                                  caught ? "ДА (ловушка сработала)" : "НЕ ПОЙМАН");
                Same("положительный контроль S141: гейт снят — двойной образ ПОЙМАН", true, caught);
            }

            // ------------------------------------------------------------------
            // S172: K-вылет КРИСТАЛЛА (`Esc-<вещество>`, флаг `CrystalEscape`)
            // при живой матрице — тем же гейтом `EscapeGate`, что SE/DE.
            // Решение Amber 14.09.2026: «При матрице вылет не класть».
            //
            // Образ узнаётся по ФЛАГУ библиотеки, а не по приставке имени:
            // имена берутся у флагованных компонентов и ищутся в результате
            // (состав ∪ подавленные — `PassedNamed`). Рядом — судьба образа
            // собственного РЕНТГЕНА кристалла (`FromCrystal`, гейт `AMBER4`):
            // числом, чтобы вопрос «не второй ли счёт и он» отвечался
            // замером, а не памятью.
            //
            // ⛔ Положительный контроль двойной: (1) образ вылета в библиотеке
            // ЕСТЬ и без матрицы проходит в разбор (иначе «при матрице нет» —
            // пустая клетка); (2) гейт снят при живой матрице — образ обязан
            // вернуться в разбор, счётчик обязан молчать.
            // ------------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("=== S172: K-вылет кристалла (Esc-*) при матрице ===");
            var crystalEscapeNames = new List<string>();
            var crystalXrayNames = new List<string>();
            foreach (FsaComponent component in sample)
            {
                if (component.CrystalEscape) crystalEscapeNames.Add(component.Name);
                if (component.FromCrystal) crystalXrayNames.Add(component.Name);
            }

            Console.WriteLine("библиотека из баз: образов вылета кристалла {0} ({1}), рентгена кристалла {2} ({3})",
                              crystalEscapeNames.Count, string.Join(", ", crystalEscapeNames),
                              crystalXrayNames.Count, string.Join(", ", crystalXrayNames));
            if (crystalEscapeNames.Count == 0)
            {
                Console.WriteLine("⛔ образа вылета кристалла в библиотеке нет — кристалл не назван или родительских линий ниже {0} кэВ нет; клетки S172 мерить нечем",
                                  spec.EscapeParentMaxKev.ToString("F0", CultureInfo.InvariantCulture));
                bad++;
            }
            else
            {
                Console.WriteLine("ESC\tматрица\tгейт\tEsc_в_разборе\tснято_гейтом\tEsc_доля_%\tEsc_z\tXray_в_разборе\tXray_доля_%\tXray_z\tchi2/ndf");
                var escapeCells = new List<bool[]>();
                if (matrix != null)
                {
                    escapeCells.Add(new[] { true, true });
                    escapeCells.Add(new[] { true, false });
                }

                escapeCells.Add(new[] { false, true });
                foreach (bool[] cell in escapeCells)
                {
                    bool withMatrix = cell[0], gateOn = cell[1];
                    FsaAnalyzer analyzer = NewAnalyzer(rd, withMatrix ? matrix : null, material);
                    new FsaCalculationOptions().ApplyTo(analyzer);
                    analyzer.EscapeGate = gateOn;
                    string arm = "матрица " + (withMatrix ? "есть" : "нет") + ", гейт " + (gateOn ? "вкл" : "СНЯТ");
                    FsaTuningReport.Print(analyzer, arm);
                    FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                        rd.FwhmCalibration, sample, efficiency);
                    if (result == null)
                    {
                        Console.WriteLine("ESC\t{0}\t{1}\tразложение не получилось", withMatrix, gateOn);
                        bad++;
                        continue;
                    }

                    double escShare, escZ, xrayShare, xrayZ;
                    int escPassed = PassedNamed(result, crystalEscapeNames, out escShare, out escZ);
                    int xrayPassed = PassedNamed(result, crystalXrayNames, out xrayShare, out xrayZ);
                    Console.WriteLine("ESC\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}",
                                      withMatrix ? "есть" : "нет", gateOn ? "вкл" : "СНЯТ",
                                      escPassed, analyzer.CrystalEscapeDropped,
                                      escShare.ToString("F3", CultureInfo.InvariantCulture),
                                      escZ.ToString("F2", CultureInfo.InvariantCulture),
                                      xrayPassed,
                                      xrayShare.ToString("F3", CultureInfo.InvariantCulture),
                                      xrayZ.ToString("F2", CultureInfo.InvariantCulture),
                                      result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture));

                    if (withMatrix && gateOn)
                    {
                        Same("S172, матрица есть, гейт вкл: матрица применена", true, result.ResponseMatrixUsed);
                        Same("S172, матрица есть, гейт вкл: образа вылета кристалла в разборе нет", 0, escPassed);
                        Same("S172, матрица есть, гейт вкл: счётчик снятых = числу образов",
                             crystalEscapeNames.Count, analyzer.CrystalEscapeDropped);
                    }
                    else if (withMatrix)
                    {
                        // Подсадка «правило ВЫКЛ»: образ обязан ВЕРНУТЬСЯ.
                        Same("положительный контроль S172: гейт снят при матрице — образ вылета кристалла в разборе ЕСТЬ (ловушка сработала)",
                             true, escPassed > 0);
                        Same("положительный контроль S172: гейт снят — счётчик молчит", 0, analyzer.CrystalEscapeDropped);
                    }
                    else
                    {
                        Same("S172, матрицы нет: образ вылета кристалла в разборе есть (единственное выражение вылета)",
                             true, escPassed > 0);
                        Same("S172, матрицы нет: гейт не срабатывал", 0, analyzer.CrystalEscapeDropped);
                    }
                }

                if (matrix == null)
                {
                    Console.WriteLine("(матрицы нет — клетки «есть» и положительный контроль S172 на этом спектре не меряются)");
                }
            }

            // ------------------------------------------------------------------
            // S173: отвязанный хвост матричного образа — невязка, не слой.
            // ------------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("=== S173 → AMBER30 → S175: отвязанный хвост (ниже порога доверия матрицы) — в слое и доле своего образа; плечо Б (--tail-as-residual) — невязка ===");
            if (matrix == null)
            {
                Console.WriteLine("(матрицы нет — отвязки нет по построению; клетки S173 на этом спектре не меряются)");
            }
            else
            {
                CheckUntiedTail(rd, matrix, material, sample, efficiency);
            }

            // ------------------------------------------------------------------
            // S174: серый слой ниже порога доверия, невязка от Min_Range.
            // ------------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("=== S174 п. 2 / S175: сплайн — в слои образов по S76 везде (пола разноса нет), серый только где нет образов; невязка — от Min_Range ===");
            if (matrix == null)
            {
                Console.WriteLine("(матрицы нет — порога доверия нет по построению; клетки S174 на этом спектре не меряются)");
            }
            else
            {
                CheckGreyFloor(rd, matrix, material, sample, efficiency);
            }

            // ------------------------------------------------------------------
            // A169: библиотека ИЗ БАЗ, равновесие вкл/выкл.
            // ------------------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("=== A169: происхождение ряда против связки равновесием ===");
            Console.WriteLine("CHAIN\tравновесие\tчленов_в_библиотеке\tDecayChainRoot≠∅\tChainRoot≠∅\tродители\tпричина");
            int membersEq = -1, membersFree = -1;
            List<string> namesEq = null, namesFree = null;
            foreach (bool equilibrium in new[] { true, false })
            {
                spec.Equilibrium = equilibrium;
                List<FsaComponent> library = FsaSampleLibrary.Build(spec);
                int inLibrary = 0;
                foreach (FsaComponent c in library)
                {
                    if (!string.IsNullOrEmpty(c.DecayChainRoot))
                    {
                        // Колонка ряда несёт всех членов разом — считаем по
                        // нуклидам линий, как раскладывает их анализатор.
                        if (c.Kind == FsaComponentKind.Chain)
                        {
                            var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (FsaLine line in c.Lines)
                            {
                                members.Add(string.IsNullOrEmpty(line.Nuclide) ? c.Name : line.Nuclide);
                            }

                            inLibrary += members.Count;
                        }
                        else
                        {
                            inLibrary++;
                        }
                    }
                }

                FsaAnalyzer analyzer = NewAnalyzer(rd, matrix, material);
                new FsaCalculationOptions { DbLookups = true, ChainEquilibrium = equilibrium }.ApplyTo(analyzer);
                FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                    rd.FwhmCalibration, library, efficiency);
                if (result == null)
                {
                    Console.WriteLine("CHAIN\t{0}\tразложение не получилось", equilibrium);
                    bad++;
                    continue;
                }

                // Происхождение — по ОБЪЕДИНЕНИЮ трёх списков результата: у
                // вошедшего члена оно в строке состава, у не вошедшего — в
                // строке предела и/или подавленного образа. Имя считается раз.
                int inComposition = 0, withLink = 0;
                var origin = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (FsaComponentResult c in result.Components)
                {
                    if (!string.IsNullOrEmpty(c.DecayChainRoot))
                    {
                        inComposition++;
                        origin[c.Name] = "состав";
                    }

                    if (!string.IsNullOrEmpty(c.ChainRoot))
                    {
                        withLink++;
                    }
                }

                foreach (FsaCharacteristicLimit limit in result.CharacteristicLimits)
                {
                    if (!string.IsNullOrEmpty(limit.DecayChainRoot) && !origin.ContainsKey(limit.Name))
                    {
                        origin[limit.Name] = limit.Detected ? "предел (обнаружен)" : "предел";
                    }
                }

                foreach (FsaSuppressedImage image in result.SuppressedImages)
                {
                    if (!string.IsNullOrEmpty(image.DecayChainRoot) && !origin.ContainsKey(image.Name))
                    {
                        origin[image.Name] = "подавлен";
                    }
                }

                int withOrigin = origin.Count;
                var names = new List<string>(origin.Keys);
                Console.WriteLine("CHAIN\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}",
                                  equilibrium ? "вкл" : "выкл", inLibrary, withOrigin, withLink,
                                  result.ParentGroupingAllowed ? "допустимы" : "НЕДОПУСТИМЫ",
                                  result.ParentGroupingRefusal ?? "-");
                var where = new List<string>();
                foreach (KeyValuePair<string, string> pair in origin)
                {
                    where.Add(pair.Key + " (" + pair.Value + ")");
                }

                Console.WriteLine("      в составе {0} из {1}; члены: {2}", inComposition, withOrigin,
                                  string.Join(", ", where));
                Same((equilibrium ? "равновесие" : "без равновесия")
                     + ": DecayChainRoot у всех членов ряда библиотеки (состав ∪ пределы ∪ подавленные)",
                     inLibrary, withOrigin);

                if (equilibrium)
                {
                    membersEq = withOrigin;
                    namesEq = names;
                    Same("равновесие: ChainRoot у всех членов ряда", withOrigin, withLink);
                    Same("равновесие: каждый член ряда — строкой состава", withOrigin, inComposition);
                    Same("равновесие: родительский режим допустим", chains.Count > 0, result.ParentGroupingAllowed);
                    if (withOrigin > 0)
                    {
                        // Разложение колонки ряда без потерь: сумма кривых членов
                        // одного корня равна модели за вычетом всего прочего.
                        double worst = SplitMismatch(result);
                        Console.WriteLine("      |Σ членов − (модель − прочее)| / max(модель) = {0}",
                                          worst.ToString("E2", CultureInfo.InvariantCulture));
                        Same("равновесие: сумма кривых членов = кривой ряда (машинный допуск)",
                             true, worst < 1e-9);
                    }
                }
                else
                {
                    membersFree = withOrigin;
                    namesFree = names;
                    Same("без равновесия: ChainRoot пуст у всех", 0, withLink);
                    Same("без равновесия: родительский режим недопустим", false, result.ParentGroupingAllowed);
                }
            }

            if (membersEq >= 0 && membersFree >= 0)
            {
                Same("DecayChainRoot: одинаковое число членов при равновесии и без", membersEq, membersFree);
                Same("DecayChainRoot: те же имена членов", string.Join(",", namesEq), string.Join(",", namesFree));
                Same("DecayChainRoot: членов " + (chains.Count > 0 ? "больше нуля" : "ноль (ряда нет)"),
                     chains.Count > 0, membersEq > 0);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return SuppliedLibraryGate(bad == 0 ? 0 : 1);
        }

        /// <summary>
        /// (`AMBER19`, П11) Вторая дверь гейта «состав только из базы» — та же,
        /// что у `CorpusFsaProbe.RefuseIfManagerRaised`: счётчик обращений к
        /// поставочному менеджеру печатается ВСЕГДА, и не ноль — код 12 поверх
        /// любого итога (по `isLoaded` подъёма не видно: без файла он бросает, `S100`).
        /// </summary>
        static int SuppliedLibraryGate(int code)
        {
            int raised = NuclideDefinitionManager.RaiseCount;
            Console.WriteLine("NuclideDefinitionManager за прогон: обращений {0}", raised);
            if (raised > 0)
            {
                Console.Error.WriteLine("⛔ AMBER19: поставочную библиотеку поднимали {0} раз(а) — числа негодны", raised);
                return 12;
            }

            return code;
        }

        static string Cell(bool withMatrix, bool escapeOn)
        {
            return "матрица " + (withMatrix ? "есть" : "нет") + ", вылеты " + (escapeOn ? "вкл" : "выкл");
        }

        /// <summary>
        /// (`S173` → `AMBER30` → `S175`, решение Amber 14.09.2026 «Чини S174
        /// так, чтобы выглядело физически корректным») ДОГОВОР УМОЛЧАНИЯ парой
        /// плеч и договор правила `S173` на плече Б — см. пункт 6 шапки.
        ///
        /// Плечо А (умолчание): хвост в ленте и доле своего образа. Плечо Б
        /// (ключ <c>FsaAnalyzer.UntiedTailAsResidual</c>): правило `S173`
        /// целиком — у результата есть `UntiedTail`/`UntiedTails` с
        /// положительной суммой, ниже порога доверия, `Model` без хвоста,
        /// `FitModel` = `Model` + `UntiedTail`, Σ слоёв = `Model`, «не
        /// описано» считано против `Model` (хвост в ней).
        ///
        /// ⛔ Положительные контроли: (1) пара (Б, Б), выданная за умолчание, —
        /// договор `S175` отказывает (яма); (2) подсадка «хвост в серый слой»
        /// на плече А — договор `S175` отказывает (провал П70); (3) подсадка
        /// «хвост снова в подложку» на плече Б — договор `S173` отказывает.
        /// Фит (амплитуды, z, χ²/ndf) везде тот же. Строки `TAIL` — числа в
        /// журнал.
        /// </summary>
        static void CheckUntiedTail(ResultData rd, ResponseMatrix matrix, string material,
                                    List<FsaComponent> sample, FsaEfficiency efficiency)
        {
            FsaAnalyzer analyzerA = NewAnalyzer(rd, matrix, material);
            new FsaCalculationOptions().ApplyTo(analyzerA);
            FsaTuningReport.Print(analyzerA, "S175, плечо А: хвост в слое своего образа (умолчание)");
            FsaResult resultA = analyzerA.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                  rd.FwhmCalibration, sample, efficiency);
            if (resultA == null)
            {
                Console.WriteLine("S175, плечо А: разложение не получилось");
                bad++;
                return;
            }

            FsaAnalyzer analyzer = NewAnalyzer(rd, matrix, material);
            new FsaCalculationOptions().ApplyTo(analyzer);
            analyzer.UntiedTailAsResidual = true;
            FsaTuningReport.Print(analyzer, "S173, матрица есть (плечо Б: --tail-as-residual)");
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, sample, efficiency);
            if (result == null)
            {
                Console.WriteLine("S173: разложение не получилось");
                bad++;
                return;
            }

            Same("S173: матрица применена", true, result.ResponseMatrixUsed);
            Same("S175, А/Б: фит один (χ²/ndf)", resultA.Chi2Ndf, result.Chi2Ndf);
            Same("S175, А/Б: фит один (усиление)", resultA.Gain, result.Gain);
            Same("S175, А/Б: фит один (сдвиг)", resultA.OffsetChannels, result.OffsetChannels);
            Same("S175, плечо А (умолчание): UntiedTail пуст", true, resultA.UntiedTail == null);

            double tailTotal = 0.0;
            foreach (FsaUntiedTail tail in result.UntiedTails)
            {
                tailTotal += tail.Counts;
                Console.WriteLine("TAIL\tхвост (Б)\t{0}\t{1}", tail.Component,
                                  tail.Counts.ToString("F1", CultureInfo.InvariantCulture));
            }

            foreach (FsaUntiedTail tail in resultA.UntiedTails)
            {
                Console.WriteLine("TAIL\tхвост (А)\t{0}\t{1}\t{2}", tail.Component,
                                  tail.Counts.ToString("F1", CultureInfo.InvariantCulture),
                                  tail.Placement.ToString().ToLowerInvariant());
            }

            foreach (FsaComponentResult c in resultA.Components)
            {
                if (c.TailCurve != null)
                {
                    Console.WriteLine("TAIL\tв слое (А)\t{0}\t{1}", c.Name,
                                      c.TailCounts.ToString("F1", CultureInfo.InvariantCulture));
                }
            }

            Console.WriteLine("TAIL\tвсего\t{0}\tотсч.; χ²/ndf {1}; не описано А {2} %, Б {3} %",
                              tailTotal.ToString("F1", CultureInfo.InvariantCulture),
                              result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture),
                              (100.0 * resultA.ResidualMissingShare).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * result.ResidualMissingShare).ToString("F2", CultureInfo.InvariantCulture));
            if (!(tailTotal > 0.0))
            {
                // Законный исход, а не отказ: колонки хвоста фиту предъявлены, но
                // NNLS дал всем ноль (`AS80_Th232Medal`: сплайн и пики описали
                // низ шкалы сами). Это и есть «спектр, где ниже порога всё
                // привязано»: слои и доли обязаны быть побитово теми же в обоих
                // плечах — здесь мерится только это.
                Console.WriteLine("(отвязанных хвостов у результата нет — решатель дал им ноль; клетки S173/S175 и подсадки на этом спектре не меряются, слои побитово прежние)");
                Same("S173, хвостов нет: Model = Continuum + Σ кривых (хвоста в верхе стека нет)", 0,
                     TailContract(result, analyzer.ResponseContinuumTrustFloorKev, rd, true).Count);
                Same("S175, хвостов нет: у компонентов TailCurve пуст", 0, TailCarriers(resultA).Count);
                Same("S175, хвостов нет: слои А = слоям Б по каналам", true,
                     LayersGap(resultA, result, null) <= 1e-9 * Math.Max(1.0, MaxOf(resultA.Model)));
                return;
            }

            // Договор `S175` — парой плеч.
            List<string> carriers = TailCarriers(resultA);
            Console.WriteLine("TAIL\tносители (А)\t{0}", string.Join(", ", carriers.ToArray()));
            List<string> refusalsA = TailInLayerContract(resultA, result, rd, analyzerA.ResponseContinuumTrustFloorKev);
            foreach (string refusal in refusalsA)
            {
                Console.WriteLine("  ⛔ договор S175: {0}", refusal);
            }

            Same("S175: договор умолчания (хвост в слое и доле своего образа, серый = сплайн, Σ слоёв = Model, «не описано» не больше плеча Б) выполнен",
                 0, refusalsA.Count);
            double greyBelowA, greyAboveA, greyBelowB, greyAboveB;
            GreySplit(resultA, out greyBelowA, out greyAboveA);
            GreySplit(result, out greyBelowB, out greyAboveB);
            string carrier = carriers.Count > 0 ? carriers[0] : "-";
            Console.WriteLine("TAIL\tA/B\tсерый слой ниже пола разноса: А {0}, Б {1} отсч.; «не описано» А {2} %, Б {3} %; доля носителя {4}: А {5} %, Б {6} %",
                              greyBelowA.ToString("F1", CultureInfo.InvariantCulture),
                              greyBelowB.ToString("F1", CultureInfo.InvariantCulture),
                              (100.0 * resultA.ResidualMissingShare).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * result.ResidualMissingShare).ToString("F2", CultureInfo.InvariantCulture),
                              carrier,
                              ShareOf(resultA, carrier).ToString("F3", CultureInfo.InvariantCulture),
                              ShareOf(result, carrier).ToString("F3", CultureInfo.InvariantCulture));

            // Положительный контроль 1: плечо Б, выданное за умолчание, — яма
            // на экране (хвост в невязке) — договор ОБЯЗАН отказать.
            List<string> hole = TailInLayerContract(result, result, rd, analyzer.ResponseContinuumTrustFloorKev);
            Same("положительный контроль S175: --tail-as-residual (яма AMBER30, хвост в невязке) — договор ОТКАЗЫВАЕТ",
                 true, hole.Count > 0);

            // Положительный контроль 2: подсадка «хвост в подложку» (картинка
            // до П68 / П70) на копии плеча А — договор ОБЯЗАН отказать: хвост
            // ушёл из слоёв, подложка выросла на хвост, фит тот же.
            FsaResult planted = analyzerA.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                  rd.FwhmCalibration, sample, efficiency);
            double[] tailSumA = resultA.TailCurveSum();
            double tailTotalA = tailSumA != null ? Sum(tailSumA) : 0.0;
            double shareBefore = ShareOf(planted, carrier);
            double chi2Before = planted.Chi2Ndf;
            double continuumBefore = Sum(planted.Continuum);
            PlantTailToContinuum(planted);
            Console.WriteLine("TAIL\tподсадка «в подложку»\t{0}\tдоля {1} → {2} %; подложка {3} → {4} отсч. (хвост {5})",
                              carrier,
                              shareBefore.ToString("F3", CultureInfo.InvariantCulture),
                              ShareOf(planted, carrier).ToString("F3", CultureInfo.InvariantCulture),
                              continuumBefore.ToString("F1", CultureInfo.InvariantCulture),
                              Sum(planted.Continuum).ToString("F1", CultureInfo.InvariantCulture),
                              tailTotalA.ToString("F1", CultureInfo.InvariantCulture));
            List<string> grey = TailInLayerContract(planted, result, rd, analyzerA.ResponseContinuumTrustFloorKev);
            Same("положительный контроль S175: подсадка «хвост в подложку» (картинка до П68 / П70) — договор ОТКАЗЫВАЕТ",
                 true, grey.Count > 0);
            Same("положительный контроль S175: с подсадкой подложка выросла ровно на хвост",
                 true, Math.Abs((Sum(planted.Continuum) - continuumBefore) - tailTotalA) <= 1e-6 * Math.Max(1.0, tailTotalA));
            Same("положительный контроль S175: с подсадкой у компонентов хвостов нет", 0, TailCarriers(planted).Count);
            Same("положительный контроль S175: фит подсадкой не тронут (χ²/ndf)", chi2Before, planted.Chi2Ndf);
            Same("положительный контроль S175: тождество стека держится и с подсадкой (Σ слоёв = модель)",
                 true, StackGap(planted) <= StackTolerance(planted));

            // Плечо Б — договор `S173` целиком, и прежняя подсадка на нём.
            double missingBefore = result.ResidualMissingShare;
            double shareBeforeB = ShareOf(result, carrier);
            double chi2BeforeB = result.Chi2Ndf;
            double greyBelowBefore, greyAboveBefore;
            GreySplit(result, out greyBelowBefore, out greyAboveBefore);
            List<string> refusals = TailContract(result, analyzer.ResponseContinuumTrustFloorKev, rd, false);
            foreach (string refusal in refusals)
            {
                Console.WriteLine("  ⛔ договор S173: {0}", refusal);
            }

            Same("S173 (плечо Б): договор результата (хвост есть, ниже порога, не в верхе стека, стек = модель) выполнен", 0, refusals.Count);

            // Подсадка «хвост снова в подложку» на плече Б — картинка П70.
            for (int i = 0; i < result.UntiedTail.Length; i++)
            {
                double tail = result.UntiedTail[i];
                if (i < result.Continuum.Length) result.Continuum[i] += tail;
                if (i < result.Model.Length) result.Model[i] += tail;
            }

            double[] plantedTail = result.UntiedTail;
            result.UntiedTail = null;
            result.UntiedTails.Clear();
            result.ComputeComponentShares();
            result.ComputeResidualShares(rd.EnergySpectrum.Spectrum);
            double missingAfter = result.ResidualMissingShare;
            Console.WriteLine("TAIL\tподсадка (Б → подложка)\t{0}\tдоля {1} → {2} %; не описано {3} → {4} %; хвост {5} отсч.",
                              carrier,
                              shareBeforeB.ToString("F2", CultureInfo.InvariantCulture),
                              ShareOf(result, carrier).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * missingBefore).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * missingAfter).ToString("F2", CultureInfo.InvariantCulture),
                              Sum(plantedTail).ToString("F1", CultureInfo.InvariantCulture));
            List<string> plantedB = TailContract(result, analyzer.ResponseContinuumTrustFloorKev, rd, false);
            Same("положительный контроль S173: подсадка «хвост снова в подложку» — договор ОТКАЗЫВАЕТ (ловушка сработала)",
                 true, plantedB.Count > 0);
            // (`S175`, второе решение) Пола разноса нет: подсаженный хвост из
            // подложки разносится по слоям (`S76`), серый слой не растёт.
            double greyBelowAfter, greyAboveAfter;
            GreySplit(result, out greyBelowAfter, out greyAboveAfter);
            Same("положительный контроль S173 (с S175): хвост из подложки разнесён по слоям — серый слой не вырос",
                 true, Math.Abs(greyBelowAfter + greyAboveAfter - greyBelowBefore - greyAboveBefore) <= StackTolerance(result));
            Same("положительный контроль S173 (с S175): с хвостом в подложке «не описано» не выросло", true, missingAfter <= missingBefore);
            Same("положительный контроль S173: фит подсадкой не тронут (χ²/ndf)", chi2BeforeB, result.Chi2Ndf);
            Same("положительный контроль S173: тождество стека держится и с хвостом в подложке (Σ слоёв = модель)",
                 true, StackGap(result) <= StackTolerance(result));
        }

        /// <summary>(`S175`) Имена компонентов, у которых есть хвост в слое (`TailCurve`), в порядке состава.</summary>
        static List<string> TailCarriers(FsaResult result)
        {
            var names = new List<string>();
            foreach (FsaComponentResult c in result.Components)
            {
                if (c.TailCurve != null && c.TailCounts > 0.0)
                {
                    names.Add(c.Name);
                }
            }

            return names;
        }

        /// <summary>
        /// (`S175`) ПОДСАДКА ТОЛЬКО ДЛЯ ПРОБЫ (та же, что `FsaStackShot
        /// --tail-to-continuum`): хвосты образов из их слоёв — в подложку,
        /// откуда разнос `S76` раздаёт их по слоям (картинка до П68); верх
        /// стека тот же, доли пересчитаны правилами результата.
        /// </summary>
        static void PlantTailToContinuum(FsaResult result)
        {
            foreach (FsaComponentResult c in result.Components)
            {
                if (c.TailCurve == null) continue;
                for (int i = 0; i < c.TailCurve.Length && i < result.Continuum.Length; i++)
                {
                    result.Continuum[i] += c.TailCurve[i];
                }

                c.TailCurve = null;
                c.TailCounts = 0.0;
            }

            foreach (FsaUntiedTail tail in result.UntiedTails)
            {
                if (tail.Placement == FsaTailPlacement.Layer) tail.Placement = FsaTailPlacement.Continuum;
            }

            result.ComputeComponentShares();
        }

        /// <summary>
        /// (`S175`) Наибольший по каналам зазор между слоями двух плеч по
        /// именам: у слоёв из <paramref name="carriers"/> ожидается
        /// А = Б + <paramref name="tail"/> (сумма по носителям), у прочих — А = Б;
        /// слой, которого нет в одном плече (убран по доле, `S87`), считается
        /// нулём. <paramref name="carriers"/> null — все слои сравниваются как
        /// «А = Б».
        /// </summary>
        static double LayersGap(FsaResult a, FsaResult b, List<string> carriers)
        {
            return LayersGap(a, b, carriers, null);
        }

        static double LayersGap(FsaResult a, FsaResult b, List<string> carriers, double[] tail)
        {
            var la = new Dictionary<string, double[]>(StringComparer.Ordinal);
            var lb = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (FsaStackLayer layer in a.BuildStackedLayers(int.MaxValue)) la[layer.Name] = layer.Curve;
            foreach (FsaStackLayer layer in b.BuildStackedLayers(int.MaxValue)) lb[layer.Name] = layer.Curve;
            int channels = a.Model.Length;
            double[] carrierA = new double[channels], carrierB = new double[channels];
            double gap = 0.0;
            var names = new HashSet<string>(la.Keys, StringComparer.Ordinal);
            names.UnionWith(lb.Keys);
            foreach (string name in names)
            {
                double[] ca, cb;
                la.TryGetValue(name, out ca);
                lb.TryGetValue(name, out cb);
                bool carrier = carriers != null && carriers.Contains(name);
                for (int i = 0; i < channels; i++)
                {
                    double va = ca != null && i < ca.Length ? ca[i] : 0.0;
                    double vb = cb != null && i < cb.Length ? cb[i] : 0.0;
                    if (carrier)
                    {
                        carrierA[i] += va;
                        carrierB[i] += vb;
                    }
                    else
                    {
                        gap = Math.Max(gap, Math.Abs(va - vb));
                    }
                }
            }

            if (carriers != null)
            {
                for (int i = 0; i < channels; i++)
                {
                    double t = tail != null && i < tail.Length ? tail[i] : 0.0;
                    gap = Math.Max(gap, Math.Abs(carrierA[i] - (carrierB[i] + t)));
                }
            }

            return gap;
        }

        /// <summary>
        /// (`S175`) Список нарушений договора умолчания «хвост в слое и доле
        /// своего образа» для пары плеч: <paramref name="a"/> — умолчание,
        /// <paramref name="b"/> — плечо Б (хвост в `UntiedTail`). Пусто —
        /// выполнен. Вызванный на паре (Б, Б) обязан отказать (яма); на паре
        /// (А с подсадкой «хвост в подложку», Б) — тоже.
        ///
        /// Лента слоя у умолчания — образ⁺ + хвост + доля сплайна
        /// (<see cref="FsaResult.SpreadContinuum"/>): доля неотрицательна у
        /// каждого слоя, и Σ долей по слоям = сплайн − серый слой канал в
        /// канал (разнос — разбиение, ничего не потеряно и не удвоено). Это и
        /// проверяется, а не «слой А = слой Б + хвост»: со вторым решением
        /// Amber («В слои образов по S76 везде») хвост входит в веса разноса,
        /// и слои двух плеч расходятся ещё и на перераспределённый сплайн.
        /// </summary>
        static List<string> TailInLayerContract(FsaResult a, FsaResult b, ResultData rd, double floorKev)
        {
            var refusals = new List<string>();
            int channels = Math.Min(a.Model.Length, b.Model.Length);
            double scale = Math.Max(1.0, MaxOf(a.Model));
            double[] tailB = b.UntiedTail;
            double[] tailA = a.TailCurveSum();
            if (tailB == null || !(Sum(tailB) > 0.0))
            {
                refusals.Add("у плеча Б хвоста в невязке нет — договор мерить нечем");
                return refusals;
            }

            if (a.UntiedTail != null)
            {
                refusals.Add("у умолчания хвост лежит в невязке (UntiedTail не пуст) — яма на экране");
            }

            if (tailA == null || !(Sum(tailA) > 0.0))
            {
                refusals.Add("у компонентов умолчания хвоста нет (TailCurve пуст) — хвост не в слое своего образа");
            }

            foreach (FsaUntiedTail tail in a.UntiedTails)
            {
                if (tail.Placement != FsaTailPlacement.Layer)
                {
                    refusals.Add("хвост " + tail.Component + " положен не в слой: " + tail.Placement);
                }
            }

            double listedA = 0.0, listedB = 0.0;
            foreach (FsaUntiedTail tail in a.UntiedTails) listedA += tail.Counts;
            foreach (FsaUntiedTail tail in b.UntiedTails) listedB += tail.Counts;
            if (Math.Abs(listedA - listedB) > 1e-6 * Math.Max(1.0, listedB))
            {
                refusals.Add("Σ хвостов по колонкам А " + listedA.ToString("F3", CultureInfo.InvariantCulture)
                             + " ≠ Б " + listedB.ToString("F3", CultureInfo.InvariantCulture));
            }

            // Хвост целиком в слоях: Σ TailCurve(А) = UntiedTail(Б) по каналам;
            // подложка та же: Continuum(А) = Continuum(Б); верх стека: Model(А)
            // = Model(Б) + хвост(Б); Model(А) = Continuum + Σ(Curve + TailCurve).
            double gapTail = 0.0, gapContinuum = 0.0, gapModel = 0.0, gapSum = 0.0;
            for (int i = 0; i < channels; i++)
            {
                double ta = tailA != null && i < tailA.Length ? tailA[i] : 0.0;
                double tb = i < tailB.Length ? tailB[i] : 0.0;
                gapTail = Math.Max(gapTail, Math.Abs(ta - tb));
                gapContinuum = Math.Max(gapContinuum, Math.Abs(a.Continuum[i] - b.Continuum[i]));
                gapModel = Math.Max(gapModel, Math.Abs(a.Model[i] - (b.Model[i] + tb)));
                double sum = a.Continuum[i];
                foreach (FsaComponentResult c in a.Components)
                {
                    sum += c.Curve[i];
                    if (c.TailCurve != null) sum += c.TailCurve[i];
                }

                gapSum = Math.Max(gapSum, Math.Abs(sum - a.Model[i]));
            }

            if (gapTail > 1e-9 * scale) refusals.Add("Σ TailCurve(А) ≠ UntiedTail(Б) по каналам, зазор " + gapTail.ToString("E2", CultureInfo.InvariantCulture));
            if (gapContinuum > 1e-9 * scale) refusals.Add("Continuum(А) ≠ Continuum(Б): хвост попал в подложку, зазор " + gapContinuum.ToString("E2", CultureInfo.InvariantCulture));
            if (gapModel > 1e-9 * scale) refusals.Add("Model(А) ≠ Model(Б) + хвост(Б), зазор " + gapModel.ToString("E2", CultureInfo.InvariantCulture));
            if (gapSum > 1e-6 * scale) refusals.Add("Model(А) ≠ Continuum + Σ (Curve + TailCurve), зазор " + gapSum.ToString("E2", CultureInfo.InvariantCulture));

            // Хвост — ниже порога доверия (плечо уширения не выше порога + 3 ПШПВ).
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            double above = 0.0, total = tailA != null ? Sum(tailA) : 0.0;
            for (int i = 0; tailA != null && i < tailA.Length; i++)
            {
                double e = calibration.ChannelToEnergy(i);
                double fwhmCh = rd.FwhmCalibration.ChannelToFwhm(i);
                double fwhmKev = calibration.ChannelToEnergy(i + fwhmCh / 2.0) - calibration.ChannelToEnergy(i - fwhmCh / 2.0);
                if (e > floorKev + 3.0 * Math.Max(0.0, fwhmKev)) above += tailA[i];
            }

            if (above > 1e-6 * Math.Max(1.0, total))
            {
                refusals.Add("хвост в слоях выше порога доверия: " + above.ToString("F3", CultureInfo.InvariantCulture) + " отсч. из " + total.ToString("F1", CultureInfo.InvariantCulture));
            }

            // Слои умолчания: лента = образ⁺ + хвост + доля сплайна ≥ 0; Σ долей
            // по слоям = сплайн − серый слой канал в канал; у носителя хвост
            // лежит в его ленте, а не в чужой (TailCurve слоя = TailCurve
            // компонента).
            var byName = new Dictionary<string, FsaComponentResult>(StringComparer.Ordinal);
            foreach (FsaComponentResult c in a.Components) byName[c.Name] = c;
            double[] spreadSum = new double[channels];
            double[] grey = new double[channels];
            double negative = 0.0, tailGap = 0.0;
            foreach (FsaStackLayer layer in a.BuildStackedLayers(int.MaxValue))
            {
                if (string.Equals(layer.Name, FsaResult.ContinuumLayerName, StringComparison.Ordinal))
                {
                    for (int i = 0; i < channels && i < layer.Curve.Length; i++) grey[i] += layer.Curve[i];
                    continue;
                }

                FsaComponentResult c;
                if (!byName.TryGetValue(layer.Name, out c)) continue;
                for (int i = 0; i < channels && i < layer.Curve.Length; i++)
                {
                    double own = i < c.Curve.Length && c.Curve[i] > 0.0 ? c.Curve[i] : 0.0;
                    double tail = c.TailCurve != null && i < c.TailCurve.Length ? c.TailCurve[i] : 0.0;
                    double layerTail = layer.TailCurve != null && i < layer.TailCurve.Length ? layer.TailCurve[i] : 0.0;
                    tailGap = Math.Max(tailGap, Math.Abs(layerTail - tail));
                    double spread = layer.Curve[i] - own - tail;
                    if (spread < negative) negative = spread;
                    spreadSum[i] += spread;
                }
            }

            double partition = 0.0;
            for (int i = 0; i < channels; i++)
            {
                double spline = a.Continuum[i] > 0.0 ? a.Continuum[i] : 0.0;
                partition = Math.Max(partition, Math.Abs(spreadSum[i] - (spline - grey[i])));
            }

            if (tailGap > 1e-9 * scale) refusals.Add("хвост слоя ≠ хвосту его компонента, зазор " + tailGap.ToString("E2", CultureInfo.InvariantCulture));
            if (-negative > 1e-9 * scale) refusals.Add("доля сплайна у слоя отрицательна: " + negative.ToString("E2", CultureInfo.InvariantCulture) + " — лента меньше образа с хвостом");
            if (partition > StackTolerance(a)) refusals.Add("Σ долей сплайна по слоям ≠ сплайн − серый, зазор " + partition.ToString("E2", CultureInfo.InvariantCulture) + " при допуске " + StackTolerance(a).ToString("E2", CultureInfo.InvariantCulture));

            if (a.ResidualMissingShare > b.ResidualMissingShare + 1e-12)
            {
                refusals.Add("«не описано» умолчания больше, чем у плеча Б: "
                             + (100.0 * a.ResidualMissingShare).ToString("F3", CultureInfo.InvariantCulture) + " против "
                             + (100.0 * b.ResidualMissingShare).ToString("F3", CultureInfo.InvariantCulture) + " %");
            }

            double stackGap = StackGap(a);
            if (stackGap > StackTolerance(a))
            {
                refusals.Add("Σ слоёв ≠ Model, зазор " + stackGap.ToString("E2", CultureInfo.InvariantCulture));
            }

            return refusals;
        }

        /// <summary>
        /// (`S174` п. 2 и `S175`, второе решение Amber 14.09.2026 «В слои
        /// образов по S76 везде») Договор результата при живой матрице, числом:
        ///
        ///   * пол разноса подложки <c>ContinuumSpreadFloorChannel</c> = 0 и
        ///     <c>ContinuumSpreadFloorKev</c> = 0: сплайн разносится по слоям
        ///     (`S76`) по всей шкале, п. 1 `S174` отменён;
        ///   * серый слой `continuum` не нуль только там, где нет ни одного
        ///     слоя образов от этого канала и выше (хвост подложки выше
        ///     последней линии), и там он равен сплайну канал в канал; где слои
        ///     образов есть — серый слой нуль;
        ///   * пол невязки <c>ResidualFloorChannel</c> — канал `Min_Range`
        ///     (энергия не ниже, предыдущего — ниже); «не описано»/«лишнее»
        ///     равны независимому счёту пробы от этого пола до конца полосы;
        ///   * Σ слоёв стека (с серым) = `Model`.
        ///
        /// ⛔ Положительные контроли: (1) ПОДСАДКА «сплайн ниже порога доверия
        /// серым» — пол разноса на пороге доверия тем же `FsaAnalyzer.FloorChannel`
        /// (п. 1 `S174`, П69; картинка П70/П75 до второго решения) — договор
        /// обязан ОТКАЗАТЬ, серый слой ниже порога — появиться и равняться
        /// сплайну ниже порога, доля носителя — упасть, χ²/ndf — тот же;
        /// (2) ПОДСАДКА «невязка по всей полосе» (пол невязки 0) — договор
        /// отказывает, невязка совпадает с независимым счётом по всей полосе.
        /// Строки `GREY` — числа в журнал.
        /// </summary>
        static void CheckGreyFloor(ResultData rd, ResponseMatrix matrix, string material,
                                   List<FsaComponent> sample, FsaEfficiency efficiency)
        {
            FsaAnalyzer analyzer = NewAnalyzer(rd, matrix, material);
            new FsaCalculationOptions().ApplyTo(analyzer);
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, sample, efficiency);
            if (result == null)
            {
                Console.WriteLine("S174/S175: разложение не получилось");
                bad++;
                return;
            }

            Same("S174/S175: матрица применена", true, result.ResponseMatrixUsed);
            int[] raw = rd.EnergySpectrum.Spectrum;
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            int trustFloor = FsaAnalyzer.FloorChannel(calibration, analyzer.ResponseContinuumTrustFloorKev,
                                                      result.FirstChannel, result.LastChannel);
            double greyBelow, greyAbove;
            GreySplitAt(result, trustFloor, out greyBelow, out greyAbove);
            double splineBelow = 0.0;
            for (int i = result.FirstChannel; i < trustFloor && i <= result.LastChannel && i < result.Continuum.Length; i++)
            {
                if (result.Continuum[i] > 0.0) splineBelow += result.Continuum[i];
            }

            Console.WriteLine("GREY\tполы\tразнос от канала {0} ({1} кэВ; порог доверия — канал {2}), невязка от канала {3} ({4} кэВ), полоса фита {5}..{6}",
                              result.ContinuumSpreadFloorChannel,
                              result.ContinuumSpreadFloorKev.ToString("G", CultureInfo.InvariantCulture),
                              trustFloor,
                              result.ResidualFloorChannel,
                              result.ResidualFloorKev.ToString("G", CultureInfo.InvariantCulture),
                              result.FirstChannel, result.LastChannel);
            Console.WriteLine("GREY\tслой\tниже порога доверия {0} отсч. (сплайн там {1}), выше {2} отсч.; χ²/ndf {3}; не описано {4} %, приписано {5} %",
                              greyBelow.ToString("F1", CultureInfo.InvariantCulture),
                              splineBelow.ToString("F1", CultureInfo.InvariantCulture),
                              greyAbove.ToString("F1", CultureInfo.InvariantCulture),
                              result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture),
                              (100.0 * result.ResidualMissingShare).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * result.ResidualExcessShare).ToString("F2", CultureInfo.InvariantCulture));

            List<string> refusals = GreyContract(result, analyzer, rd);
            foreach (string refusal in refusals)
            {
                Console.WriteLine("  ⛔ договор S174/S175: {0}", refusal);
            }

            Same("S175: договор результата (пол разноса 0 — S76 везде; серый только где нет образов; невязка от Min_Range; стек = модель) выполнен",
                 0, refusals.Count);

            // Носитель — нуклид с наибольшей долей: ему разнос ниже порога
            // отдаёт больше всех, и подсадка «серым» у него же отнимет.
            string carrier = null;
            double carrierShare = -1.0;
            foreach (FsaComponentResult c in result.Components)
            {
                if (c.Kind != FsaComponentKind.Nuisance && c.SharePercent > carrierShare)
                {
                    carrier = c.Name;
                    carrierShare = c.SharePercent;
                }
            }

            double chi2Before = result.Chi2Ndf;
            double missingBefore = result.ResidualMissingShare;
            double excessBefore = result.ResidualExcessShare;
            bool floorCuts = result.ResidualFloorChannel > result.FirstChannel;

            // Подсадка 1: сплайн ниже порога доверия серым (п. 1 S174).
            result.ContinuumSpreadFloorKev = analyzer.ResponseContinuumTrustFloorKev;
            result.ContinuumSpreadFloorChannel = trustFloor;
            result.ComputeComponentShares();
            double greyBelowPlanted, greyAbovePlanted;
            GreySplitAt(result, trustFloor, out greyBelowPlanted, out greyAbovePlanted);
            double shareAfter = carrier != null ? ShareOf(result, carrier) : 0.0;
            Console.WriteLine("GREY\tподсадка «серым»\t{0}\tдоля {1} → {2} %; серый слой ниже порога {3} → {4} отсч. (сплайн ниже порога {5})",
                              carrier ?? "-",
                              carrierShare.ToString("F2", CultureInfo.InvariantCulture),
                              shareAfter.ToString("F2", CultureInfo.InvariantCulture),
                              greyBelow.ToString("F1", CultureInfo.InvariantCulture),
                              greyBelowPlanted.ToString("F1", CultureInfo.InvariantCulture),
                              splineBelow.ToString("F1", CultureInfo.InvariantCulture));
            List<string> planted = GreyContract(result, analyzer, rd);
            Same("положительный контроль S175: подсадка «сплайн ниже порога серым» (п. 1 S174) — договор ОТКАЗЫВАЕТ (ловушка сработала)",
                 true, planted.Count > 0);
            Same("положительный контроль S175: с подсадкой серый слой ниже порога = сплайну ниже порога",
                 true, Math.Abs(greyBelowPlanted - splineBelow) <= 1e-9 * Math.Max(1.0, MaxOf(result.Model)));
            if (splineBelow > 0.0)
            {
                Same("положительный контроль S175: с подсадкой доля носителя меньше", true, shareAfter < carrierShare);
            }
            else
            {
                Console.WriteLine("(сплайна ниже порога доверия у этого спектра нет — сдвиг доли носителя не меряется)");
            }

            Same("положительный контроль S175: фит подсадкой не тронут (χ²/ndf)", chi2Before, result.Chi2Ndf);
            Same("положительный контроль S175: тождество стека держится и с подсадкой (Σ слоёв = модель)",
                 true, StackGap(result) <= StackTolerance(result));

            // Подсадка 2: невязка по всей полосе (пол невязки 0) — п. 2 S174.
            result.ContinuumSpreadFloorKev = 0.0;
            result.ContinuumSpreadFloorChannel = 0;
            result.ResidualFloorChannel = 0;
            result.ComputeComponentShares();
            result.ComputeResidualShares(raw);
            Console.WriteLine("GREY\tподсадка «невязка по всей полосе»\tне описано {0} → {1} %; приписано {2} → {3} %",
                              (100.0 * missingBefore).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * result.ResidualMissingShare).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * excessBefore).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * result.ResidualExcessShare).ToString("F2", CultureInfo.InvariantCulture));
            double missingWhole, excessWhole;
            ResidualByProbe(result, raw, result.FirstChannel, out missingWhole, out excessWhole);
            Same("положительный контроль S174 п. 2: с подсадкой невязка = независимому счёту по всей полосе (не описано)",
                 true, Math.Abs(result.ResidualMissingShare - missingWhole) <= 1e-12);
            Same("положительный контроль S174 п. 2: с подсадкой невязка = независимому счёту по всей полосе (лишнее)",
                 true, Math.Abs(result.ResidualExcessShare - excessWhole) <= 1e-12);
            if (floorCuts)
            {
                List<string> plantedResidual = GreyContract(result, analyzer, rd);
                Same("положительный контроль S174 п. 2: подсадка «невязка по всей полосе» — договор ОТКАЗЫВАЕТ",
                     true, plantedResidual.Count > 0);
            }
            else
            {
                Console.WriteLine("(пол Min_Range не режет полосу фита у этого спектра — сдвиг невязки не меряется)");
            }

            Same("положительный контроль S174 п. 2: фит подсадкой не тронут (χ²/ndf)", chi2Before, result.Chi2Ndf);
        }

        /// <summary>Список нарушений договора S174 п. 2 / S175; пусто — договор выполнен.</summary>
        static List<string> GreyContract(FsaResult result, FsaAnalyzer analyzer, ResultData rd)
        {
            var refusals = new List<string>();
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            int[] raw = rd.EnergySpectrum.Spectrum;
            if (result.ContinuumSpreadFloorChannel != 0 || result.ContinuumSpreadFloorKev != 0.0)
            {
                refusals.Add("пол разноса подложки " + result.ContinuumSpreadFloorChannel + " (" + result.ContinuumSpreadFloorKev.ToString("G", CultureInfo.InvariantCulture)
                             + " кэВ) ≠ 0 — сплайн ниже него серым, а не в слоях образов (п. 1 S174 отменён S175)");
            }

            int rfloor = result.ResidualFloorChannel;
            double minRange = analyzer.MinEnergy;
            if (Math.Abs(result.ResidualFloorKev - minRange) > 1e-9)
            {
                refusals.Add("пол невязки " + result.ResidualFloorKev.ToString("G", CultureInfo.InvariantCulture)
                             + " кэВ ≠ Min_Range " + minRange.ToString("G", CultureInfo.InvariantCulture));
            }

            // (П70) Пол — ПЕРВЫЙ КАНАЛ ПОЛОСЫ ФИТА, чья энергия не ниже порога
            // (`FsaAnalyzer.FloorChannel`: счёт от `chLo`): порог ниже полосы
            // даёт пол = первый канал полосы, и «канал − 1 ещё ниже порога» там
            // неверно по построению (Cs-137 в домике у Amber: Min_Range 5 кэВ при
            // полосе от канала 35 ≈ 7 кэВ). Договор: энергия пола ≥ порога, и
            // либо канал − 1 ниже порога, либо пол — первый канал полосы.
            if (minRange > 0.0 && (rfloor <= 0 || calibration.ChannelToEnergy(rfloor) < minRange
                                   || (rfloor > result.FirstChannel && calibration.ChannelToEnergy(rfloor - 1) >= minRange)))
            {
                refusals.Add("канал пола невязки " + rfloor + " не на Min_Range " + minRange.ToString("G", CultureInfo.InvariantCulture) + " кэВ (и не первый канал полосы " + result.FirstChannel + ")");
            }

            // Серый слой — только там, где от канала и выше нет ни одного слоя
            // образов; там он равен сплайну; где образы есть — нуль.
            List<FsaStackLayer> layers = result.BuildStackedLayers(int.MaxValue);
            int channels = result.Model.Length;
            double[] grey = new double[channels];
            double[] imagesAbove = new double[channels];
            double running = 0.0;
            for (int i = channels - 1; i >= 0; i--)
            {
                foreach (FsaStackLayer layer in layers)
                {
                    double v = i < layer.Curve.Length ? layer.Curve[i] : 0.0;
                    if (string.Equals(layer.Name, FsaResult.ContinuumLayerName, StringComparison.Ordinal)) grey[i] += v;
                    else running += v;
                }

                imagesAbove[i] = running;
            }

            double top = Math.Max(1.0, MaxOf(result.Model));
            double greyWhereImages = 0.0, greyGap = 0.0;
            for (int i = result.FirstChannel; i <= result.LastChannel && i < channels; i++)
            {
                if (imagesAbove[i] > 0.0)
                {
                    greyWhereImages = Math.Max(greyWhereImages, grey[i]);
                }
                else
                {
                    double spline = i < result.Continuum.Length && result.Continuum[i] > 0.0 ? result.Continuum[i] : 0.0;
                    greyGap = Math.Max(greyGap, Math.Abs(grey[i] - spline));
                }
            }

            if (greyWhereImages > 1e-9 * top)
            {
                refusals.Add("серый слой там, где есть слои образов: до " + greyWhereImages.ToString("E2", CultureInfo.InvariantCulture) + " отсч./канал — сплайн не разнесён по S76");
            }

            if (greyGap > 1e-9 * top)
            {
                refusals.Add("выше последней линии серый слой ≠ сплайн, зазор " + greyGap.ToString("E2", CultureInfo.InvariantCulture));
            }

            double missing, excess;
            ResidualByProbe(result, raw, Math.Max(result.FirstChannel, rfloor), out missing, out excess);
            if (Math.Abs(missing - result.ResidualMissingShare) > 1e-12 || Math.Abs(excess - result.ResidualExcessShare) > 1e-12)
            {
                refusals.Add("невязка результата " + (100.0 * result.ResidualMissingShare).ToString("F4", CultureInfo.InvariantCulture)
                             + "/" + (100.0 * result.ResidualExcessShare).ToString("F4", CultureInfo.InvariantCulture)
                             + " % ≠ счёту пробы от пола " + (100.0 * missing).ToString("F4", CultureInfo.InvariantCulture)
                             + "/" + (100.0 * excess).ToString("F4", CultureInfo.InvariantCulture) + " %");
            }

            double stackGap = StackGap(result);
            if (stackGap > StackTolerance(result))
            {
                refusals.Add("Σ слоёв ≠ Model, зазор " + stackGap.ToString("E2", CultureInfo.InvariantCulture)
                             + " при допуске " + StackTolerance(result).ToString("E2", CultureInfo.InvariantCulture));
            }

            return refusals;
        }

        /// <summary>(`S175`) Серый слой стека надвое относительно канала <paramref name="floor"/>: ниже и от него и выше.</summary>
        static void GreySplitAt(FsaResult result, int floor, out double below, out double above)
        {
            below = 0.0;
            above = 0.0;
            foreach (FsaStackLayer layer in result.BuildStackedLayers(int.MaxValue))
            {
                if (!string.Equals(layer.Name, FsaResult.ContinuumLayerName, StringComparison.Ordinal)) continue;
                for (int i = 0; i < layer.Curve.Length; i++)
                {
                    if (i < floor) below += layer.Curve[i];
                    else above += layer.Curve[i];
                }
            }
        }

        /// <summary>
        /// (`S174`) НЕЗАВИСИМЫЙ счёт невязки в отсчётах от канала
        /// <paramref name="from"/> до конца полосы: измерение фита минус верх
        /// стека, положительная половина — «не описано», отрицательная —
        /// «лишнее», обе долей от измеренных отсчётов той же полосы. Это
        /// контроль правила результата, а не его замена.
        /// </summary>
        static void ResidualByProbe(FsaResult result, int[] raw, int from, out double missing, out double excess)
        {
            double[] net = result.FitSpectrum(raw);
            double m = 0.0, e = 0.0, measured = 0.0;
            for (int i = from; i <= result.LastChannel && i < net.Length; i++)
            {
                measured += net[i];
                double d = net[i] - result.Model[i];
                if (d > 0.0) m += d; else e -= d;
            }

            missing = measured > 0.0 ? m / measured : 0.0;
            excess = measured > 0.0 ? e / measured : 0.0;
        }

        /// <summary>(`S174`) Серый слой стека надвое: ниже пола разноса и от него и выше.</summary>
        static void GreySplit(FsaResult result, out double below, out double above)
        {
            below = 0.0;
            above = 0.0;
            foreach (FsaStackLayer layer in result.BuildStackedLayers(int.MaxValue))
            {
                if (!string.Equals(layer.Name, FsaResult.ContinuumLayerName, StringComparison.Ordinal)) continue;
                for (int i = 0; i < layer.Curve.Length; i++)
                {
                    if (i < result.ContinuumSpreadFloorChannel) below += layer.Curve[i];
                    else above += layer.Curve[i];
                }
            }
        }

        /// <summary>
        /// Допуск тождества «Σ слоёв стека = Model». Слои с долей ниже
        /// <see cref="FsaResult.MinShownSharePercent"/> из стека УБРАНЫ ПО
        /// ПОСТРОЕНИЮ (`S87`, указание Amber 19.08.2026), и верх стека проседает
        /// на их сумму — меньше 0.005 % знаменателя на слой; плюс отрицательная
        /// часть кривых подрезана (`PositivePart`). Допуск — ровно эта величина
        /// на число возможных слоёв, а не «машинный ноль».
        /// </summary>
        static double StackTolerance(FsaResult result)
        {
            int layers = result.Components != null ? result.Components.Count + 1 : 1;
            return layers * FsaResult.MinShownSharePercent / 100.0 * Math.Max(1.0, result.StackTotal)
                   + 1e-9 * Math.Max(1.0, MaxOf(result.Model));
        }

        /// <summary>Кривая слоя стека по имени (полный стек, без свёртки мелких); null — слоя нет.</summary>
        static double[] LayerCurve(FsaResult result, string name)
        {
            foreach (FsaStackLayer layer in result.BuildStackedLayers(int.MaxValue))
            {
                if (string.Equals(layer.Name, name, StringComparison.Ordinal))
                {
                    return layer.Curve;
                }
            }

            return null;
        }

        /// <summary>
        /// Список нарушений договора S173; пусто — договор выполнен.
        /// <paramref name="allowEmpty"/> — хвостов может не быть (решатель дал
        /// им ноль): тогда проверяется только верх стека.
        /// </summary>
        static List<string> TailContract(FsaResult result, double floorKev, ResultData rd, bool allowEmpty)
        {
            var refusals = new List<string>();
            if (result.UntiedTail == null || result.UntiedTails == null || result.UntiedTails.Count == 0)
            {
                if (!allowEmpty)
                {
                    refusals.Add("хвостов нет (UntiedTail/UntiedTails пусты)");
                    return refusals;
                }

                double topEmpty = MaxOf(result.Model);
                double gapEmpty = 0.0;
                for (int i = result.FirstChannel; i <= result.LastChannel; i++)
                {
                    double sum = result.Continuum[i];
                    foreach (FsaComponentResult c in result.Components)
                    {
                        sum += c.Curve[i] + (c.TailCurve != null ? c.TailCurve[i] : 0.0);
                    }

                    gapEmpty = Math.Max(gapEmpty, Math.Abs(sum - result.Model[i]));
                }

                if (gapEmpty > 1e-6 * Math.Max(1.0, topEmpty))
                {
                    refusals.Add("Model ≠ Continuum + Σ кривых, зазор " + gapEmpty.ToString("E2", CultureInfo.InvariantCulture));
                }

                return refusals;
            }

            double total = Sum(result.UntiedTail);
            if (!(total > 0.0))
            {
                refusals.Add("сумма хвоста не положительна");
            }

            double listed = 0.0;
            foreach (FsaUntiedTail tail in result.UntiedTails) listed += tail.Counts;
            if (Math.Abs(listed - total) > 1e-6 * Math.Max(1.0, total))
            {
                refusals.Add("Σ по компонентам " + listed.ToString("F3", CultureInfo.InvariantCulture)
                             + " ≠ Σ кривой " + total.ToString("F3", CultureInfo.InvariantCulture));
            }

            // Хвост — ниже порога доверия: выше порога + 3 ПШПВ его быть не может.
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            double above = 0.0;
            for (int i = 0; i < result.UntiedTail.Length; i++)
            {
                double e = calibration.ChannelToEnergy(i);
                double fwhmCh = rd.FwhmCalibration.ChannelToFwhm(i);
                double fwhmKev = calibration.ChannelToEnergy(i + fwhmCh / 2.0) - calibration.ChannelToEnergy(i - fwhmCh / 2.0);
                if (e > floorKev + 3.0 * Math.Max(0.0, fwhmKev))
                {
                    above += result.UntiedTail[i];
                }
            }

            if (above > 1e-6 * total)
            {
                refusals.Add("хвост выше порога доверия: " + above.ToString("F3", CultureInfo.InvariantCulture)
                             + " отсч. из " + total.ToString("F1", CultureInfo.InvariantCulture));
            }

            // Верх стека — без хвоста: Model = Continuum + Σ кривых; хвостов у
            // образов (`S175`) на плече Б быть не должно.
            foreach (FsaComponentResult c in result.Components)
            {
                if (c.TailCurve != null)
                {
                    refusals.Add("у компонента " + c.Name + " хвост лежит в слое (TailCurve) при ключе «хвост в невязке»");
                }
            }

            double top = MaxOf(result.Model);
            double gap = 0.0;
            for (int i = result.FirstChannel; i <= result.LastChannel; i++)
            {
                double sum = result.Continuum[i];
                foreach (FsaComponentResult c in result.Components) sum += c.Curve[i];
                gap = Math.Max(gap, Math.Abs(sum - result.Model[i]));
            }

            if (gap > 1e-6 * Math.Max(1.0, top))
            {
                refusals.Add("Model ≠ Continuum + Σ кривых, зазор " + gap.ToString("E2", CultureInfo.InvariantCulture));
            }

            // Модель фита — с хвостом.
            double[] fit = result.FitModel();
            double fitGap = 0.0;
            for (int i = result.FirstChannel; i <= result.LastChannel; i++)
            {
                fitGap = Math.Max(fitGap, Math.Abs(fit[i] - result.Model[i] - result.UntiedTail[i]));
            }

            if (fitGap > 1e-9 * Math.Max(1.0, top))
            {
                refusals.Add("FitModel ≠ Model + UntiedTail, зазор " + fitGap.ToString("E2", CultureInfo.InvariantCulture));
            }

            // Тождество стека: Σ слоёв (с остатком подложки) = Model — с допуском
            // на слои, убранные из стека по доле (`S87`), см. StackTolerance.
            double stackGap = StackGap(result);
            if (stackGap > StackTolerance(result))
            {
                refusals.Add("Σ слоёв ≠ Model, зазор " + stackGap.ToString("E2", CultureInfo.InvariantCulture)
                             + " при допуске " + StackTolerance(result).ToString("E2", CultureInfo.InvariantCulture));
            }

            return refusals;
        }

        /// <summary>Худший по каналам |Σ слоёв стека − Model| (слои без отсева, лимит бесконечный).</summary>
        static double StackGap(FsaResult result)
        {
            List<FsaStackLayer> layers = result.BuildStackedLayers(int.MaxValue);
            double worst = 0.0;
            for (int i = result.FirstChannel; i <= result.LastChannel; i++)
            {
                double sum = 0.0;
                foreach (FsaStackLayer layer in layers)
                {
                    if (i < layer.Curve.Length) sum += layer.Curve[i];
                }

                worst = Math.Max(worst, Math.Abs(sum - result.Model[i]));
            }

            return worst;
        }

        static double ShareOf(FsaResult result, string name)
        {
            foreach (FsaComponentResult c in result.Components)
            {
                if (string.Equals(c.Name, name, StringComparison.Ordinal)) return c.SharePercent;
            }

            return 0.0;
        }

        static double Sum(double[] a)
        {
            double s = 0.0;
            if (a != null) foreach (double v in a) s += v;
            return s;
        }

        static double MaxOf(double[] a)
        {
            double m = 0.0;
            if (a != null) foreach (double v in a) if (v > m) m = v;
            return m;
        }

        static FsaAnalyzer NewAnalyzer(ResultData rd, ResponseMatrix matrix, string material)
        {
            var analyzer = new FsaAnalyzer();
            if (matrix != null)
            {
                analyzer.ResponseMatrix = matrix;
                analyzer.ScintillatorMaterial = material;
            }

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

            return analyzer;
        }

        /// <summary>
        /// Спецификация состава — ОБЩИМ ВХОДОМ приложения
        /// (`FsaSampleSpec.FromManifest`, `T257` хвост (2), 12.09.2026): метки
        /// рядов словами манифеста, нуклиды nucid, элементы кристалла из
        /// `--crystal=` — поверх (данные ключа, не приложения). До того здесь
        /// лежала своя копия сборки — без элементов пробы из геометрии и с
        /// `NucidOf` на метках рядов.
        /// </summary>
        static FsaSampleSpec SpecOf(ResultData rd, List<string> chains, List<string> nuclides, List<string> crystal)
        {
            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, nuclides, true, true);
            foreach (string symbol in crystal)
            {
                int z = MaterialDatabase.ZOf(symbol);
                if (z > 0 && !spec.CrystalElements.Contains(z))
                {
                    spec.CrystalElements.Add(z);
                }
            }

            return spec;
        }

        static int CountEscape(List<FsaComponent> library)
        {
            int n = 0;
            foreach (FsaComponent c in library)
            {
                if (FsaLibrary.IsEscapeImage(c.Name))
                {
                    n++;
                }
            }

            return n;
        }

        static int CountAnnihilation(List<FsaComponent> library)
        {
            int n = 0;
            foreach (FsaComponent c in library)
            {
                if (FsaResult.IsAnnihilationImage(c.Name))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// Образы, ПРОШЕДШИЕ в разбор: вошедшие в состав плюс построенные и
        /// подавленные отсевом (`S78`). Отсев по значимости — не фильтр
        /// библиотеки, и судить «был ли образ предъявлен фиту» надо по обоим
        /// спискам.
        /// </summary>
        static int PassedEscape(FsaResult result)
        {
            int n = 0;
            foreach (FsaComponentResult c in result.Components)
            {
                if (FsaLibrary.IsEscapeImage(c.Name)) n++;
            }

            foreach (FsaSuppressedImage c in result.SuppressedImages)
            {
                if (FsaLibrary.IsEscapeImage(c.Name)) n++;
            }

            return n;
        }

        /// <summary>
        /// (`S172`) Сколько образов из списка имён ПРОШЛО в разбор (состав ∪
        /// подавленные), и доля/z первого вошедшего в СОСТАВ (у подавленного
        /// доли нет — печатается 0 и его z). Имена — у флагованных компонентов
        /// библиотеки, чтобы проба читала флаг, а не приставку.
        /// </summary>
        static int PassedNamed(FsaResult result, List<string> names, out double share, out double z)
        {
            int n = 0;
            share = 0.0;
            z = 0.0;
            var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            foreach (FsaComponentResult c in result.Components)
            {
                if (wanted.Contains(c.Name))
                {
                    if (n == 0)
                    {
                        share = c.SharePercent;
                        z = c.Z;
                    }

                    n++;
                }
            }

            foreach (FsaSuppressedImage c in result.SuppressedImages)
            {
                if (wanted.Contains(c.Name))
                {
                    if (n == 0 && !double.IsNaN(c.Z))
                    {
                        z = c.Z;
                    }

                    n++;
                }
            }

            return n;
        }

        static int PassedAnnihilation(FsaResult result)
        {
            int n = 0;
            foreach (FsaComponentResult c in result.Components)
            {
                if (FsaResult.IsAnnihilationImage(c.Name)) n++;
            }

            foreach (FsaSuppressedImage c in result.SuppressedImages)
            {
                if (FsaResult.IsAnnihilationImage(c.Name)) n++;
            }

            return n;
        }

        static int PassedBackscatter(FsaResult result)
        {
            int n = 0;
            foreach (FsaComponentResult c in result.Components)
            {
                if (IsBackscatter(c.Name)) n++;
            }

            foreach (FsaSuppressedImage c in result.SuppressedImages)
            {
                if (IsBackscatter(c.Name)) n++;
            }

            return n;
        }

        static bool IsBackscatter(string name)
        {
            return !string.IsNullOrEmpty(name)
                   && name.StartsWith(FsaResult.BackscatterLayerName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Двойной образ вылета: матрица применена И свободные SE/DE прошли в разбор.</summary>
        static bool DoubleEscape(FsaResult result)
        {
            return result.ResponseMatrixUsed && PassedEscape(result) > 0;
        }

        /// <summary>
        /// Наибольшее по корням |Σ кривых членов − (модель − континуум − всё
        /// прочее)|, отнесённое к максимуму модели. Ноль с точностью
        /// округления — колонка ряда разложена на членов без потерь.
        /// </summary>
        static double SplitMismatch(FsaResult result)
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FsaComponentResult c in result.Components)
            {
                if (!string.IsNullOrEmpty(c.ChainRoot))
                {
                    roots.Add(c.ChainRoot);
                }
            }

            double top = 0.0;
            foreach (double v in result.Model)
            {
                if (v > top) top = v;
            }

            double worst = 0.0;
            foreach (string root in roots)
            {
                for (int i = result.FirstChannel; i <= result.LastChannel; i++)
                {
                    double members = 0.0, others = result.Continuum[i];
                    foreach (FsaComponentResult c in result.Components)
                    {
                        // (`S175`) Хвост члена — в его ленте: Σ (Curve + TailCurve).
                        double v = c.Curve[i] + (c.TailCurve != null ? c.TailCurve[i] : 0.0);
                        if (string.Equals(c.ChainRoot, root, StringComparison.OrdinalIgnoreCase))
                        {
                            members += v;
                        }
                        else
                        {
                            others += v;
                        }
                    }

                    double gap = Math.Abs(members - (result.Model[i] - others));
                    if (gap > worst) worst = gap;
                }
            }

            return top > 0.0 ? worst / top : worst;
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0,-70} {1} {2}{3}", what, ok ? "=" : "!!", got,
                              ok ? "" : string.Format(" вместо {0}", expected));
            if (!ok)
            {
                bad++;
            }
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            if (file.ResultDataList == null || file.ResultDataList.Count == 0)
            {
                Console.Error.WriteLine("в файле нет ни одного результата");
                return null;
            }

            return file.ResultDataList[0];
        }
    }
}
