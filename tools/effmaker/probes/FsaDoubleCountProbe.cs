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
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0. Всё, что мерится, печатается строками
    /// `CELL`/`CHAIN`, чтобы числа можно было положить в журнал.
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

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

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            if (rd == null)
            {
                return 2;
            }

            Console.WriteLine("спектр  : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор  : {0}", ProbeDeviceConfig.Attach(rd));

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

            Console.WriteLine("матрица : {0}", matrix != null ? "есть, " + material : "НЕТ");

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

                Same(Cell(withMatrix, escapeOn) + ": Ann-511 " + (escapeOn ? "есть" : "нет"),
                     escapeOn, annihilation > 0);
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
            return bad == 0 ? 0 : 1;
        }

        static string Cell(bool withMatrix, bool escapeOn)
        {
            return "матрица " + (withMatrix ? "есть" : "нет") + ", вылеты " + (escapeOn ? "вкл" : "выкл");
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

        static FsaSampleSpec SpecOf(ResultData rd, List<string> chains, List<string> nuclides, List<string> crystal)
        {
            var spec = new FsaSampleSpec
            {
                Efficiency = FsaEfficiency.FromConfig(rd.Efficiency),
                AdcFloorKev = FsaBand.AdcFloorOf(rd.EnergySpectrum)
            };
            foreach (string label in chains)
            {
                spec.Chains.Add(new FsaSampleChain(NucidOf(label)));
            }

            foreach (string nucid in nuclides)
            {
                spec.Nuclides.Add(nucid);
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig
                && peakConfig.Max_Range > peakConfig.Min_Range)
            {
                spec.MinEnergyKev = peakConfig.Min_Range;
                spec.MaxEnergyKev = peakConfig.Max_Range;
            }

            GeometryModel geometry = rd.Efficiency != null && rd.Efficiency.HasGeometry
                ? rd.Efficiency.Geometry : null;
            if (geometry != null)
            {
                FsaSampleLibrary.DescribeCrystal(spec, geometry.Crystal, 0.01,
                    EfficiencySimulator.ScintillatorNameOf(geometry));
            }

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

        /// <summary>«Th-232» → «232TH»: nucid, как его зовёт nucdb.</summary>
        static string NucidOf(string label)
        {
            int dash = label.IndexOf('-');
            if (dash < 0)
            {
                return label.ToUpperInvariant();
            }

            return label.Substring(dash + 1) + label.Substring(0, dash).ToUpperInvariant();
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
