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
    ///   6. `S173` → `AMBER30` (решение Amber 14.09.2026 «В серый слой
    ///      «континуум»»), при живой матрице. Умолчание (плечо А,
    ///      `FsaAnalyzer.UntiedTailAsResidual` опущен): отвязанный хвост образа
    ///      идёт в подложку, оттуда `S174` кладёт его ниже пола разноса в
    ///      СЕРЫЙ слой; `UntiedTail`/`UntiedTails` пусты, Σ слоёв = `Model`.
    ///      Плечо Б (ключ поднят — правило `S173` как в П68): хвост в
    ///      `UntiedTail`, ниже порога доверия, в верх стека не входит,
    ///      `FitModel` = верх + хвост. Договор умолчания меряется ПАРОЙ плеч:
    ///      фит один (χ²/ndf, усиление, сдвиг), `Model`(А) = `Model`(Б) +
    ///      хвост(Б) и то же для `Continuum` по каналам; серый слой ниже пола
    ///      разноса в А больше, чем в Б, ровно на хвост ниже пола; слой
    ///      носителя ниже пола в А = в Б (образ × амплитуда, хвост не в доле
    ///      нуклида); «не описано» в А не больше, чем в Б (хвост не в невязке).
    ///      ⛔ Положительный контроль: плечо Б, выданное за умолчание (яма
    ///      снова на экране), — договор отказывает; и прежняя подсадка «хвост
    ///      снова в подложку» на плече Б — договор `S173` отказывает, χ²/ndf
    ///      тот же, серый слой растёт ровно на хвост ниже пола разноса.
    ///   7. `S174` (два решения Amber 14.09.2026 «Ниже порога не разносить —
    ///      серый слой «континуум»» и «Только в диапазоне прибора»), при живой
    ///      матрице: пол разноса подложки стоит на пороге доверия матрицы
    ///      (канал по калибровке файла), ниже него сплайн лежит серым слоем
    ///      `continuum` целиком, а слои нуклидов там — ровно образ ×
    ///      амплитуда; пол невязки стоит на `Min_Range` прибора, и число
    ///      «не описано»/«лишнее» равно независимому счёту пробы от этого
    ///      пола; Σ слоёв (с серым) = `Model`. ⛔ Положительный контроль:
    ///      подсадка «разнести как прежде и считать невязку по всей полосе»
    ///      (оба пола = 0, доли и невязка пересчитаны правилами результата) —
    ///      договор отказывает, серый слой ниже порога исчезает, доля
    ///      носителя растёт, невязка равна независимому счёту по всей полосе,
    ///      χ²/ndf тот же.
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
            Console.WriteLine("=== S173 → AMBER30: отвязанный хвост (ниже порога доверия матрицы) — серый слой; плечо Б (--tail-as-residual) — невязка ===");
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
            Console.WriteLine("=== S174: сплайн ниже порога доверия — серый слой; невязка — от Min_Range ===");
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
        /// (`S173`, решение Amber 14.09.2026 «Отвязанный хвост рисовать как
        /// невязку»; с `AMBER30` того же дня — «В серый слой «континуум»» —
        /// правило `S173` живёт только на плече Б, ключом
        /// <c>FsaAnalyzer.UntiedTailAsResidual</c>, а умолчание меряется парой
        /// плеч — см. пункт 6 шапки) Договор результата ПЛЕЧА Б при живой
        /// матрице, числом:
        ///
        ///   * у результата есть отвязанные хвосты (`UntiedTail`, `UntiedTails`)
        ///     с положительной суммой, и они лежат НИЖЕ порога доверия матрицы
        ///     (выше `ResponseContinuumTrustFloorKev` + 3 ПШПВ хвоста нет вовсе —
        ///     помечены именно колонки хвоста, а не шапки сплайна);
        ///   * верх стека `Model` = `Continuum` + Σ кривых компонентов — хвоста в
        ///     нём нет; модель фита `FitModel()` = `Model` + `UntiedTail`;
        ///   * Σ слоёв стека (с неразнесённым остатком) = `Model` по каналам —
        ///     тождество стека: слои + невязка = данные;
        ///   * невязка «не описано» считана против `Model` — то есть хвост в ней.
        ///
        /// ⛔ Положительный контроль — ПОДСАДКА «хвост снова в подложку»: тот
        /// же результат, хвост сложен обратно в подложку и верх стека, доли
        /// пересчитаны (`ComputeComponentShares`) — картинка ДО S173. Договор
        /// на ней обязан ОТКАЗАТЬ; фит (амплитуды, z, χ²/ndf) — тот же.
        /// ⚠ (`S174`) До 14.09.2026 вечера ожидалось ещё «доля носителя
        /// растёт, «не описано» падает» — с `S174` это не так ПО ПОСТРОЕНИЮ:
        /// хвост лежит ниже порога доверия, а подложка там разносится не по
        /// слоям, а в серый слой, и ниже `Min_Range` невязка в процент не
        /// входит. Поэтому меряется то, что теперь и происходит: серый слой
        /// вырос ровно на хвост ниже пола разноса, «не описано» не выросло.
        /// Строки `TAIL` — числа в журнал.
        /// </summary>
        static void CheckUntiedTail(ResultData rd, ResponseMatrix matrix, string material,
                                    List<FsaComponent> sample, FsaEfficiency efficiency)
        {
            // (`AMBER30`, П70 14.09.2026) Правило `S173` по умолчанию ОТОЗВАНО
            // (`FsaAnalyzer.UntiedTailAsResidual`): хвост снова в подложке, как до
            // П68. Договор `S173` ниже меряется на плече Б — с поднятым ключом, —
            // а плечо А (умолчание) обязано дать: хвостов у результата нет,
            // `Model`(А) = `Model`(Б) + `UntiedTail`(Б) и то же для `Continuum`
            // по каналам, фит тот же. Иначе ключ — не отображение.
            FsaAnalyzer analyzerA = NewAnalyzer(rd, matrix, material);
            new FsaCalculationOptions().ApplyTo(analyzerA);
            FsaTuningReport.Print(analyzerA, "AMBER30, плечо А: хвост в подложке (умолчание)");
            FsaResult resultA = analyzerA.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                  rd.FwhmCalibration, sample, efficiency);
            if (resultA == null)
            {
                Console.WriteLine("AMBER30, плечо А: разложение не получилось");
                bad++;
                return;
            }

            Same("AMBER30, плечо А (умолчание): UntiedTail пуст", true, resultA.UntiedTail == null);
            Same("AMBER30, плечо А (умолчание): UntiedTails пуст", 0,
                 resultA.UntiedTails != null ? resultA.UntiedTails.Count : 0);
            Same("AMBER30, плечо А (умолчание): Model = Continuum + Σ кривых (тождество стека)", 0,
                 TailContract(resultA, analyzerA.ResponseContinuumTrustFloorKev, rd, true).Count);

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
            Same("AMBER30, А/Б: фит один (χ²/ndf)", resultA.Chi2Ndf, result.Chi2Ndf);
            Same("AMBER30, А/Б: фит один (усиление)", resultA.Gain, result.Gain);
            Same("AMBER30, А/Б: фит один (сдвиг)", resultA.OffsetChannels, result.OffsetChannels);
            double gapModel = 0.0, gapContinuum = 0.0, scale = 1.0;
            for (int i = 0; i < resultA.Model.Length && i < result.Model.Length; i++)
            {
                double tail = result.UntiedTail != null && i < result.UntiedTail.Length ? result.UntiedTail[i] : 0.0;
                gapModel = Math.Max(gapModel, Math.Abs(resultA.Model[i] - (result.Model[i] + tail)));
                gapContinuum = Math.Max(gapContinuum, Math.Abs(resultA.Continuum[i] - (result.Continuum[i] + tail)));
                scale = Math.Max(scale, Math.Abs(resultA.Model[i]));
            }

            Console.WriteLine("TAIL\tА/Б\tзазор Model(А) − (Model(Б) + хвост) {0}, Continuum {1}, шкала {2}",
                              gapModel.ToString("E2", CultureInfo.InvariantCulture),
                              gapContinuum.ToString("E2", CultureInfo.InvariantCulture),
                              scale.ToString("E2", CultureInfo.InvariantCulture));
            Same("AMBER30, А/Б: Model(А) = Model(Б) + UntiedTail(Б) по каналам", true, gapModel <= 1e-9 * scale);
            Same("AMBER30, А/Б: Continuum(А) = Continuum(Б) + UntiedTail(Б) по каналам", true, gapContinuum <= 1e-9 * scale);
            double tailTotal = 0.0;
            foreach (FsaUntiedTail tail in result.UntiedTails)
            {
                tailTotal += tail.Counts;
                Console.WriteLine("TAIL\tхвост\t{0}\t{1}", tail.Component,
                                  tail.Counts.ToString("F1", CultureInfo.InvariantCulture));
            }

            Console.WriteLine("TAIL\tвсего\t{0}\tотсч.; χ²/ndf {1}; не описано {2} %, приписано {3} %",
                              tailTotal.ToString("F1", CultureInfo.InvariantCulture),
                              result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture),
                              (100.0 * result.ResidualMissingShare).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * result.ResidualExcessShare).ToString("F2", CultureInfo.InvariantCulture));
            if (!(tailTotal > 0.0))
            {
                // Законный исход, а не отказ: колонки хвоста фиту предъявлены, но
                // NNLS дал всем ноль (`AS80_Th232Medal`: сплайн и пики описали
                // низ шкалы сами). Это и есть «спектр, где ниже порога всё
                // привязано»: слои и доли обязаны быть побитово теми же, что до
                // S173 — проверяется сверкой плеч, здесь мерить нечего.
                Console.WriteLine("(отвязанных хвостов у результата нет — решатель дал им ноль; клетки S173 и подсадка на этом спектре не меряются, слои побитово прежние)");
                Same("S173, хвостов нет: Model = Continuum + Σ кривых (хвоста в верхе стека нет)", 0,
                     TailContract(result, analyzer.ResponseContinuumTrustFloorKev, rd, true).Count);
                return;
            }

            // Хвост-носитель: компонент с наибольшим хвостом — по нему и
            // сверяется доля до/после подсадки.
            string carrier = null;
            double carrierTail = 0.0;
            foreach (FsaUntiedTail tail in result.UntiedTails)
            {
                if (tail.Counts > carrierTail)
                {
                    carrier = tail.Component;
                    carrierTail = tail.Counts;
                }
            }

            double missingBefore = result.ResidualMissingShare;
            double shareBefore = ShareOf(result, carrier);
            double chi2Before = result.Chi2Ndf;
            double greyBelowBefore, greyAboveBefore;
            GreySplit(result, out greyBelowBefore, out greyAboveBefore);
            double tailBelowSpread = 0.0;
            for (int i = 0; i < result.UntiedTail.Length && i < result.ContinuumSpreadFloorChannel; i++)
            {
                tailBelowSpread += result.UntiedTail[i];
            }

            List<string> refusals = TailContract(result, analyzer.ResponseContinuumTrustFloorKev, rd, false);
            foreach (string refusal in refusals)
            {
                Console.WriteLine("  ⛔ договор S173: {0}", refusal);
            }

            Same("S173: договор результата (хвост есть, ниже порога, не в верхе стека, стек = модель) выполнен", 0, refusals.Count);

            // (`AMBER30`, решение Amber 14.09.2026 «В серый слой «континуум»»)
            // ДОГОВОР УМОЛЧАНИЯ — парой плеч: у плеча А хвост лежит в сером
            // слое ниже пола разноса (серый слой А − серый слой Б = хвост ниже
            // пола), слой носителя ниже пола тот же, что у Б (образ ×
            // амплитуда — хвост не в доле нуклида), «не описано» не больше,
            // чем у Б (хвост не в невязке). Читатель — строки `TAIL\tA/B`.
            double greyBelowA, greyAboveA;
            GreySplit(resultA, out greyBelowA, out greyAboveA);
            Console.WriteLine("TAIL\tA/B\tсерый слой ниже пола разноса: А {0}, Б {1} отсч. (хвост ниже пола {2}); «не описано» А {3} %, Б {4} %; доля носителя {5}: А {6} %, Б {7} %",
                              greyBelowA.ToString("F1", CultureInfo.InvariantCulture),
                              greyBelowBefore.ToString("F1", CultureInfo.InvariantCulture),
                              tailBelowSpread.ToString("F1", CultureInfo.InvariantCulture),
                              (100.0 * resultA.ResidualMissingShare).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * missingBefore).ToString("F2", CultureInfo.InvariantCulture),
                              carrier,
                              ShareOf(resultA, carrier).ToString("F2", CultureInfo.InvariantCulture),
                              shareBefore.ToString("F2", CultureInfo.InvariantCulture));
            List<string> greyRefusals = TailInGreyContract(resultA, result, tailBelowSpread, carrier);
            foreach (string refusal in greyRefusals)
            {
                Console.WriteLine("  ⛔ договор AMBER30: {0}", refusal);
            }

            Same("AMBER30: договор умолчания (хвост в сером слое ниже пола, слой носителя ниже пола = образ × амплитуда, «не описано» не больше плеча Б) выполнен",
                 0, greyRefusals.Count);
            // Положительный контроль: плечо Б, выданное за умолчание, — яма на
            // экране (хвост в невязке, серый слой без него) — договор ОБЯЗАН отказать.
            List<string> hole = TailInGreyContract(result, result, tailBelowSpread, carrier);
            Same("положительный контроль AMBER30: --tail-as-residual возвращает яму (хвост в невязке, не в сером слое) — договор ОТКАЗЫВАЕТ",
                 true, hole.Count > 0);

            // Подсадка «хвост снова в слой» — картинка до S173.
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

            // Невязка «не описано» после подсадки — ТЕМ ЖЕ правилом результата
            // (`S174`: `FsaResult.ComputeResidualShares`, от пола `Min_Range`);
            // своей копии правила у пробы больше нет.
            result.ComputeResidualShares(rd.EnergySpectrum.Spectrum);
            double missingAfter = result.ResidualMissingShare;
            double shareAfter = ShareOf(result, carrier);
            Console.WriteLine("TAIL\tподсадка\t{0}\tдоля {1} → {2} %; не описано {3} → {4} %; хвост {5} отсч.",
                              carrier,
                              shareBefore.ToString("F2", CultureInfo.InvariantCulture),
                              shareAfter.ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * missingBefore).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * missingAfter).ToString("F2", CultureInfo.InvariantCulture),
                              carrierTail.ToString("F1", CultureInfo.InvariantCulture));

            List<string> planted = TailContract(result, analyzer.ResponseContinuumTrustFloorKev, rd, false);
            Same("положительный контроль S173: подсадка «хвост снова в подложку» — договор ОТКАЗЫВАЕТ (ловушка сработала)",
                 true, planted.Count > 0);

            // (`S174`) Куда ушёл подсаженный хвост: ниже пола разноса — в серый
            // слой (ровно на его величину), и только его часть от пола и выше
            // (обычно ноль) — в слои нуклидов.
            double greyBelowAfter, greyAboveAfter;
            GreySplit(result, out greyBelowAfter, out greyAboveAfter);
            Console.WriteLine("TAIL\tподсадка\tсерый слой ниже порога {0} → {1} отсч. (хвост ниже пола разноса {2} отсч.)",
                              greyBelowBefore.ToString("F1", CultureInfo.InvariantCulture),
                              greyBelowAfter.ToString("F1", CultureInfo.InvariantCulture),
                              tailBelowSpread.ToString("F1", CultureInfo.InvariantCulture));
            Same("положительный контроль S173 (с S174): хвост ниже порога ушёл в серый слой — вырос ровно на хвост ниже пола разноса",
                 true, Math.Abs((greyBelowAfter - greyBelowBefore) - tailBelowSpread) <= StackTolerance(result));
            Same("положительный контроль S173 (с S174): с хвостом в подложке «не описано» не выросло", true, missingAfter <= missingBefore);
            Same("положительный контроль S173: фит подсадкой не тронут (χ²/ndf)", chi2Before, result.Chi2Ndf);

            // Хвост вернулся в верх стека целиком: Σ слоёв = Model и после.
            double worst = StackGap(result);
            Same("положительный контроль S173: тождество стека держится и с хвостом в слое (Σ слоёв = модель)",
                 true, worst <= StackTolerance(result));
            Console.WriteLine("  (подсажено {0} отсч. хвоста; худший зазор стека {1}, допуск {2})",
                              Sum(plantedTail).ToString("F1", CultureInfo.InvariantCulture),
                              worst.ToString("E2", CultureInfo.InvariantCulture),
                              StackTolerance(result).ToString("E2", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`S174`, два решения Amber 14.09.2026) Договор результата при живой
        /// матрице, числом:
        ///
        ///   * пол разноса подложки <c>ContinuumSpreadFloorChannel</c> стоит на
        ///     пороге доверия матрицы: энергия канала по калибровке файла не
        ///     ниже порога, предыдущего — ниже; <c>ContinuumSpreadFloorKev</c>
        ///     = <c>ResponseContinuumTrustFloorKev</c>;
        ///   * ниже пола серый слой `continuum` = сплайн `Continuum` канал в
        ///     канал, а Σ слоёв нуклидов и образов = Σ положительных частей
        ///     кривых компонентов — то есть разнесённого сплайна там нет;
        ///   * пол невязки <c>ResidualFloorChannel</c> — канал `Min_Range`
        ///     (энергия не ниже, предыдущего — ниже); «не описано»/«лишнее»
        ///     равны независимому счёту пробы от этого пола до конца полосы;
        ///   * Σ слоёв стека (с серым) = `Model`.
        ///
        /// ⛔ Положительный контроль — ПОДСАДКА «разнести как прежде и считать
        /// по всей полосе»: оба пола = 0, доли и невязка пересчитаны правилами
        /// результата. Договор обязан ОТКАЗАТЬ, серый слой ниже порога —
        /// исчезнуть, доля носителя (нуклид с наибольшей долей) — вырасти,
        /// невязка — совпасть с независимым счётом по всей полосе, χ²/ndf —
        /// тот же. Строки `GREY` — числа в журнал.
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
                Console.WriteLine("S174: разложение не получилось");
                bad++;
                return;
            }

            Same("S174: матрица применена", true, result.ResponseMatrixUsed);
            int[] raw = rd.EnergySpectrum.Spectrum;
            double greyBelow, greyAbove;
            GreySplit(result, out greyBelow, out greyAbove);
            Console.WriteLine("GREY	полы	разнос от канала {0} ({1} кэВ), невязка от канала {2} ({3} кэВ), полоса фита {4}..{5}",
                              result.ContinuumSpreadFloorChannel,
                              result.ContinuumSpreadFloorKev.ToString("G", CultureInfo.InvariantCulture),
                              result.ResidualFloorChannel,
                              result.ResidualFloorKev.ToString("G", CultureInfo.InvariantCulture),
                              result.FirstChannel, result.LastChannel);
            Console.WriteLine("GREY	слой	ниже порога {0} отсч., выше последней линии {1} отсч.; χ²/ndf {2}; не описано {3} %, приписано {4} %",
                              greyBelow.ToString("F1", CultureInfo.InvariantCulture),
                              greyAbove.ToString("F1", CultureInfo.InvariantCulture),
                              result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture),
                              (100.0 * result.ResidualMissingShare).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * result.ResidualExcessShare).ToString("F2", CultureInfo.InvariantCulture));

            List<string> refusals = GreyContract(result, analyzer, rd);
            foreach (string refusal in refusals)
            {
                Console.WriteLine("  ⛔ договор S174: {0}", refusal);
            }

            Same("S174: договор результата (полы на порогах, сплайн ниже порога — серый слой, невязка от Min_Range, стек = модель) выполнен",
                 0, refusals.Count);

            // Носитель — нуклид с наибольшей долей: ему разнос ниже порога
            // отдаст больше всех.
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
            bool spreadCuts = greyBelow > 0.0;

            // Подсадка: оба пола сняты, доли и невязка — правилами результата.
            result.ContinuumSpreadFloorChannel = 0;
            result.ContinuumSpreadFloorKev = 0.0;
            result.ResidualFloorChannel = 0;
            result.ComputeComponentShares();
            result.ComputeResidualShares(raw);
            double greyBelowAfter, greyAboveAfter;
            GreySplit(result, out greyBelowAfter, out greyAboveAfter);
            double shareAfter = carrier != null ? ShareOf(result, carrier) : 0.0;
            Console.WriteLine("GREY	подсадка	{0}	доля {1} → {2} %; не описано {3} → {4} %; приписано {5} → {6} %; серый слой ниже порога {7} → {8} отсч.",
                              carrier ?? "-",
                              carrierShare.ToString("F2", CultureInfo.InvariantCulture),
                              shareAfter.ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * missingBefore).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * result.ResidualMissingShare).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * excessBefore).ToString("F2", CultureInfo.InvariantCulture),
                              (100.0 * result.ResidualExcessShare).ToString("F2", CultureInfo.InvariantCulture),
                              greyBelow.ToString("F1", CultureInfo.InvariantCulture),
                              greyBelowAfter.ToString("F1", CultureInfo.InvariantCulture));

            List<string> planted = GreyContract(result, analyzer, rd);
            Same("положительный контроль S174: подсадка «разнести как прежде, невязка по всей полосе» — договор ОТКАЗЫВАЕТ (ловушка сработала)",
                 true, planted.Count > 0);
            Same("положительный контроль S174: с подсадкой серого слоя ниже порога нет", 0.0, greyBelowAfter);
            if (spreadCuts)
            {
                Same("положительный контроль S174: с подсадкой доля носителя больше", true, shareAfter > carrierShare);
            }
            else
            {
                Console.WriteLine("(серого слоя ниже порога у этого спектра нет — сдвиг доли носителя не меряется)");
            }

            double missingWhole, excessWhole;
            ResidualByProbe(result, raw, result.FirstChannel, out missingWhole, out excessWhole);
            Same("положительный контроль S174: с подсадкой невязка = независимому счёту по всей полосе (не описано)",
                 true, Math.Abs(result.ResidualMissingShare - missingWhole) <= 1e-12);
            Same("положительный контроль S174: с подсадкой невязка = независимому счёту по всей полосе (лишнее)",
                 true, Math.Abs(result.ResidualExcessShare - excessWhole) <= 1e-12);
            if (!floorCuts)
            {
                Console.WriteLine("(пол Min_Range не режет полосу фита у этого спектра — сдвиг невязки не меряется)");
            }

            Same("положительный контроль S174: фит подсадкой не тронут (χ²/ndf)", chi2Before, result.Chi2Ndf);
            double worst = StackGap(result);
            Same("положительный контроль S174: тождество стека держится и с подсадкой (Σ слоёв = модель)",
                 true, worst <= StackTolerance(result));
        }

        /// <summary>Список нарушений договора S174; пусто — договор выполнен.</summary>
        static List<string> GreyContract(FsaResult result, FsaAnalyzer analyzer, ResultData rd)
        {
            var refusals = new List<string>();
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            int[] raw = rd.EnergySpectrum.Spectrum;
            double floorKev = analyzer.ResponseContinuumTrustFloorKev;
            int spread = result.ContinuumSpreadFloorChannel;
            if (Math.Abs(result.ContinuumSpreadFloorKev - floorKev) > 1e-9)
            {
                refusals.Add("порог разноса " + result.ContinuumSpreadFloorKev.ToString("G", CultureInfo.InvariantCulture)
                             + " кэВ ≠ порог доверия " + floorKev.ToString("G", CultureInfo.InvariantCulture));
            }

            // (П70) Пол — ПЕРВЫЙ КАНАЛ ПОЛОСЫ ФИТА, чья энергия не ниже порога
            // (`FsaAnalyzer.FloorChannel`: счёт от `chLo`): порог ниже полосы
            // даёт пол = первый канал полосы, и «канал − 1 ещё ниже порога» там
            // неверно по построению (Cs-137 в домике у Amber: Min_Range 5 кэВ при
            // полосе от канала 35 ≈ 7 кэВ). Договор: энергия пола ≥ порога, и
            // либо канал − 1 ниже порога, либо пол — первый канал полосы.
            if (spread <= 0 || calibration.ChannelToEnergy(spread) < floorKev
                || (spread > result.FirstChannel && calibration.ChannelToEnergy(spread - 1) >= floorKev))
            {
                refusals.Add("канал пола разноса " + spread + " не на пороге доверия " + floorKev.ToString("G", CultureInfo.InvariantCulture) + " кэВ");
            }

            int rfloor = result.ResidualFloorChannel;
            double minRange = analyzer.MinEnergy;
            if (Math.Abs(result.ResidualFloorKev - minRange) > 1e-9)
            {
                refusals.Add("пол невязки " + result.ResidualFloorKev.ToString("G", CultureInfo.InvariantCulture)
                             + " кэВ ≠ Min_Range " + minRange.ToString("G", CultureInfo.InvariantCulture));
            }

            if (minRange > 0.0 && (rfloor <= 0 || calibration.ChannelToEnergy(rfloor) < minRange
                                   || (rfloor > result.FirstChannel && calibration.ChannelToEnergy(rfloor - 1) >= minRange)))
            {
                refusals.Add("канал пола невязки " + rfloor + " не на Min_Range " + minRange.ToString("G", CultureInfo.InvariantCulture) + " кэВ (и не первый канал полосы " + result.FirstChannel + ")");
            }

            // Ниже пола: серый слой = сплайн, слои компонентов = образы.
            List<FsaStackLayer> layers = result.BuildStackedLayers(int.MaxValue);
            double greyGap = 0.0, layerGap = 0.0, top = Math.Max(1.0, MaxOf(result.Model));
            for (int i = result.FirstChannel; i < spread && i <= result.LastChannel; i++)
            {
                double grey = 0.0, others = 0.0, images = 0.0;
                foreach (FsaStackLayer layer in layers)
                {
                    double v = i < layer.Curve.Length ? layer.Curve[i] : 0.0;
                    if (string.Equals(layer.Name, FsaResult.ContinuumLayerName, StringComparison.Ordinal)) grey += v;
                    else others += v;
                }

                foreach (FsaComponentResult c in result.Components)
                {
                    double v = i < c.Curve.Length ? c.Curve[i] : 0.0;
                    if (v > 0.0) images += v;
                }

                double continuum = i < result.Continuum.Length && result.Continuum[i] > 0.0 ? result.Continuum[i] : 0.0;
                greyGap = Math.Max(greyGap, Math.Abs(grey - continuum));
                layerGap = Math.Max(layerGap, Math.Abs(others - images));
            }

            if (greyGap > 1e-9 * top)
            {
                refusals.Add("ниже порога серый слой ≠ сплайн, зазор " + greyGap.ToString("E2", CultureInfo.InvariantCulture));
            }

            if (layerGap > StackTolerance(result))
            {
                refusals.Add("ниже порога слои компонентов ≠ образы (в них разнесён сплайн), зазор " + layerGap.ToString("E2", CultureInfo.InvariantCulture));
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

        /// <summary>
        /// (`AMBER30`) Список нарушений договора умолчания «хвост в сером
        /// слое» для пары плеч: <paramref name="a"/> — умолчание (хвост в
        /// подложке), <paramref name="b"/> — плечо Б (хвост в `UntiedTail`);
        /// <paramref name="tailBelow"/> — хвост плеча Б ниже пола разноса,
        /// <paramref name="carrier"/> — носитель хвоста. Пусто — выполнен.
        /// Вызванный на паре (Б, Б) обязан отказать — это положительный
        /// контроль: серый слой без хвоста и есть яма на экране.
        /// </summary>
        static List<string> TailInGreyContract(FsaResult a, FsaResult b, double tailBelow, string carrier)
        {
            var refusals = new List<string>();
            double greyBelowA, greyAboveA, greyBelowB, greyAboveB;
            GreySplit(a, out greyBelowA, out greyAboveA);
            GreySplit(b, out greyBelowB, out greyAboveB);
            double tolerance = StackTolerance(a);
            if (!(tailBelow > 0.0))
            {
                refusals.Add("хвоста ниже пола разноса нет — договор мерить нечем");
            }

            if (Math.Abs((greyBelowA - greyBelowB) - tailBelow) > tolerance)
            {
                refusals.Add("серый слой ниже пола разноса вырос не на хвост: А " + greyBelowA.ToString("F1", CultureInfo.InvariantCulture)
                             + ", Б " + greyBelowB.ToString("F1", CultureInfo.InvariantCulture)
                             + ", хвост " + tailBelow.ToString("F1", CultureInfo.InvariantCulture)
                             + ", допуск " + tolerance.ToString("E2", CultureInfo.InvariantCulture));
            }

            double[] layerA = LayerCurve(a, carrier), layerB = LayerCurve(b, carrier);
            if (layerA == null || layerB == null)
            {
                refusals.Add("слоя носителя «" + carrier + "» нет в одном из плеч");
            }
            else
            {
                double gap = 0.0, scale = 1.0;
                int floor = Math.Min(a.ContinuumSpreadFloorChannel, Math.Min(layerA.Length, layerB.Length));
                for (int i = 0; i < floor; i++)
                {
                    gap = Math.Max(gap, Math.Abs(layerA[i] - layerB[i]));
                    scale = Math.Max(scale, Math.Abs(layerB[i]));
                }

                if (gap > 1e-9 * scale)
                {
                    refusals.Add("слой носителя ниже пола разноса разошёлся между плечами: зазор "
                                 + gap.ToString("E2", CultureInfo.InvariantCulture) + " при шкале "
                                 + scale.ToString("E2", CultureInfo.InvariantCulture) + " — хвост попал в долю нуклида");
                }
            }

            if (a.ResidualMissingShare > b.ResidualMissingShare + 1e-12)
            {
                refusals.Add("«не описано» умолчания больше, чем у плеча Б: "
                             + (100.0 * a.ResidualMissingShare).ToString("F3", CultureInfo.InvariantCulture) + " против "
                             + (100.0 * b.ResidualMissingShare).ToString("F3", CultureInfo.InvariantCulture) + " %");
            }

            return refusals;
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
                    foreach (FsaComponentResult c in result.Components) sum += c.Curve[i];
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

            // Верх стека — без хвоста: Model = Continuum + Σ кривых.
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
                        if (string.Equals(c.ChainRoot, root, StringComparison.OrdinalIgnoreCase))
                        {
                            members += c.Curve[i];
                        }
                        else
                        {
                            others += c.Curve[i];
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
