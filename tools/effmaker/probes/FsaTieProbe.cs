using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;

namespace FsaTieProbe
{
    /// <summary>
    /// СТОРОЖ ПРИВЯЗКИ ВЫРОЖДЕННОГО ЧЛЕНА РЯДА (`S171`, решение Amber
    /// 14.09.2026 «Привязать к члену, с которым вырожден, пометить»).
    ///
    /// ЗАЧЕМ. В режиме без связки равновесия каждый член ряда получает свою
    /// свободную амплитуду, и член, чья колонка неотличима от колонки другого
    /// члена того же ряда, забирает произвольную долю общего бугра (на
    /// эталоне `AS80_Th232Medal`: Ra-224 ×9.9 от равновесного при z 31, 14 %
    /// модели на экране, — журнал П59/П60). Гейт
    /// <c>FsaAnalyzer.TieDegenerateMembers</c> такого члена привязывает к
    /// партнёру в равновесном отношении и помечает «по партнёру».
    ///
    /// СЦЕНА — искусственная, без матрицы (гейт геометрии `A277` снят: вопрос
    /// пробы — сам гейт привязки, а не отклик): ряд из четырёх свободных
    /// членов (<see cref="FsaComponentKind.Single"/> с общим
    /// <see cref="FsaComponent.DecayChainRoot"/>) — хозяин с сильной линией,
    /// ВЫРОЖДЕННЫЙ член с линией в трёх кэВ от неё (при ПШПВ ≈ 40 кэВ это
    /// один столбец), член с СОБСТВЕННОЙ линией далеко от всех и ещё один
    /// свободный с собственной линией; спектр — их сумма в равновесном
    /// отношении, уширенная той же ПШПВ, плюс ровный фон.
    ///
    /// ЧТО МЕРЯЕТСЯ:
    ///   1. ПРИВЯЗКА: вырожденный привязан к хозяину (запись в
    ///      <see cref="FsaResult.Ties"/>, партнёр в строке состава и в строке
    ///      предела), амплитуда у обоих одна; члены с собственной линией
    ///      свободны, и их доля по Шуру далеко ниже порога.
    ///   2. ЭКРАН: подпись строки — «по хозяину» / «by host» в обеих культурах
    ///      (`FsaPresentationBuilder.RowName`, ресурс `FSATiedMemberRow`), у
    ///      свободных подписи нет.
    ///   3. СВЯЗКА: при связке равновесия (одна колонка <see cref="FsaComponentKind.Chain"/>)
    ///      гейт не судит никого — судимых 0, чисел не трогает.
    ///   4. ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: гейт выключен (порог 0) — та же проверка
    ///      «вырожденный привязан» ОБЯЗАНА отказать; заведомо низкий порог —
    ///      член с собственной линией привязывается, то есть приговор следует
    ///      мере, а не имени.
    ///
    ///     fsatieprobe
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        const int Channels = 2048;
        const double LiveTime = 1000.0;
        const string Root = "Ряд-испытание";
        const string Host = "Испыт-хозяин";
        const string Twin = "Испыт-двойник";
        const string Own = "Испыт-своя";
        const string Far = "Испыт-далёкая";

        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            FsaTuningReport.Snapshot();

            Ties();
            Screen();
            Equilibrium();
            Control();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 1. ПРИВЯЗКА
        // ------------------------------------------------------------------

        static void Ties()
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. привязка: вырожденный — по хозяину, свои линии — свободны ===");
            FsaAnalyzer analyzer;
            FsaResult result = Run(double.NaN, false, "умолчание", out analyzer);
            if (result == null)
            {
                Console.WriteLine("  ⛔ разбор не получился");
                bad++;
                return;
            }

            Console.WriteLine("  {0}", analyzer.ChainTieNote ?? "(гейт не судил)");
            Same("порог гейта положителен (умолчание конструктора)", true, analyzer.ChainTieShare > 0.0);
            // Сильнейший член не судится: сильнее его в ряду никого нет.
            Same("судимых членов (все, кроме сильнейшего)", 3, analyzer.ChainTieJudged);
            Same("привязано ровно один", 1, analyzer.ChainTieTied);
            Same("привязок в результате", 1, result.Ties.Count);
            if (result.Ties.Count == 1)
            {
                Same("привязан двойник", Twin, result.Ties[0].Member);
                Same("к хозяину", Host, result.Ties[0].Partner);
                Same("доля двойника по Шуру не ниже порога", true, result.Ties[0].Share >= analyzer.ChainTieShare);
                Console.WriteLine("  двойник: шур {0}, пара {1}",
                                  result.Ties[0].Share.ToString("F3", CultureInfo.InvariantCulture),
                                  result.Ties[0].PairShare.ToString("F3", CultureInfo.InvariantCulture));
            }

            FsaComponentResult host = Row(result, Host);
            FsaComponentResult twin = Row(result, Twin);
            FsaComponentResult own = Row(result, Own);
            Same("строка хозяина есть", true, host != null);
            Same("строка двойника есть", true, twin != null);
            Same("строка своей линии есть", true, own != null);
            if (host != null && twin != null && own != null)
            {
                Same("двойник помечен партнёром-хозяином", Host, twin.TiedTo);
                Same("хозяин свободен (партнёра нет)", null, host.TiedTo);
                Same("своя линия свободна (партнёра нет)", null, own.TiedTo);
                Same("связки ряда у двойника нет (это не равновесие ряда)", null, twin.ChainRoot);
                Same("происхождение в ряду у двойника сохранено", Root, twin.DecayChainRoot);
                Same("амплитуда двойника = амплитуде хозяина", host.CountRate, twin.CountRate);
                Same("значимость у них одна", host.Z, twin.Z);
                Same("своя линия — своя амплитуда (не равна хозяйской)", true, own.CountRate != host.CountRate);
                Same("лента двойника положительна", true, Sum(twin.Curve) > 0.0);
            }

            // Свободные члены — далеко ниже порога: у них СОБСТВЕННЫЕ линии.
            foreach (FsaTie verdict in analyzer.ChainTieJudgements ?? new List<FsaTie>())
            {
                Console.WriteLine("  приговор {0,-16} → {1,-16} шур {2} пара {3} {4}", verdict.Member, verdict.Partner,
                                  verdict.Share.ToString("F3", CultureInfo.InvariantCulture),
                                  verdict.PairShare.ToString("F3", CultureInfo.InvariantCulture),
                                  verdict.Tied ? "ПРИВЯЗАН" : "свободен");
                if (verdict.Member == Own || verdict.Member == Far)
                {
                    Same(verdict.Member + " — доля ниже половины порога", true,
                         verdict.Share < 0.5 * analyzer.ChainTieShare);
                }
            }

            // Пределы: у двойника своя строка — копия хозяйской с партнёром.
            FsaCharacteristicLimit twinLimit = Limit(result, Twin);
            FsaCharacteristicLimit hostLimit = Limit(result, Host);
            Same("строка предела у двойника есть", true, twinLimit != null);
            if (twinLimit != null && hostLimit != null)
            {
                Same("предел двойника помечен партнёром", Host, twinLimit.TiedTo);
                Same("предел хозяина партнёра не несёт", null, hostLimit.TiedTo);
                Same("двойник обнаружен (как хозяин)", hostLimit.Detected, twinLimit.Detected);
                Same("порог решения — хозяйский", hostLimit.DecisionThresholdRate, twinLimit.DecisionThresholdRate);
                Same("пиковые отсчёты на пределе — по СВОЕМУ образу (меньше хозяйских)", true,
                     !double.IsNaN(twinLimit.DetectionLimitPeakCounts)
                     && twinLimit.DetectionLimitPeakCounts < hostLimit.DetectionLimitPeakCounts);
            }
        }

        // ------------------------------------------------------------------
        // 2. ЭКРАН
        // ------------------------------------------------------------------

        static void Screen()
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. экран: подпись «по хозяину» / «by host» в обеих культурах ===");
            FsaAnalyzer analyzer;
            FsaResult result = Run(double.NaN, false, null, out analyzer);
            if (result == null)
            {
                Console.WriteLine("  ⛔ разбор не получился");
                bad++;
                return;
            }

            foreach (string lang in new[] { "en-US", "ru-RU" })
            {
                Language(lang);
                string mark = Mark("FSATiedMemberRow");
                string want = string.Format(CultureInfo.InvariantCulture, mark, Twin, Host);
                FsaPresentation presentation = FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, false);
                string twinRow = null, hostRow = null, ownRow = null;
                foreach (FsaReportRow row in presentation.Rows)
                {
                    if (row.Kind != FsaReportRowKind.Layer || row.Layer == null)
                    {
                        continue;
                    }

                    if (row.Layer.Name == Twin) twinRow = row.Name;
                    if (row.Layer.Name == Host) hostRow = row.Name;
                    if (row.Layer.Name == Own) ownRow = row.Name;
                }

                Console.WriteLine("  {0}: «{1}» | «{2}» | «{3}»", lang, twinRow, hostRow, ownRow);
                Same(lang + ": строка двойника — по образцу ресурса", want, twinRow);
                Same(lang + ": в ней есть имя хозяина", true, twinRow != null && twinRow.Contains(Host));
                Same(lang + ": строка хозяина — голое имя", Host, hostRow);
                Same(lang + ": строка своей линии — голое имя", Own, ownRow);
                Same(lang + ": слой двойника несёт партнёра", Host, LayerTiedTo(presentation, Twin));
            }

            Language("ru-RU");
            Same("русская подпись содержит «по»", true, Mark("FSATiedMemberRow").Contains("по"));
            Language("en-US");
            Same("английская подпись содержит «by»", true, Mark("FSATiedMemberRow").Contains("by"));
        }

        // ------------------------------------------------------------------
        // 3. СВЯЗКА РАВНОВЕСИЯ
        // ------------------------------------------------------------------

        static void Equilibrium()
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. связка равновесия: одна колонка ряда — гейт не судит ===");
            FsaAnalyzer analyzer;
            FsaResult result = Run(double.NaN, true, "равновесие", out analyzer);
            Same("разбор получился", true, result != null);
            Same("судимых 0", 0, analyzer.ChainTieJudged);
            Same("привязано 0", 0, analyzer.ChainTieTied);
            Same("служебной строки нет", null, analyzer.ChainTieNote);
            if (result != null)
            {
                Same("привязок в результате 0", 0, result.Ties.Count);
                FsaComponentResult twin = Row(result, Twin);
                Same("двойник — строка ряда (связка), не привязка", true,
                     twin != null && twin.ChainRoot == Root && twin.TiedTo == null);
            }
        }

        // ------------------------------------------------------------------
        // 4. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ Сторож обязан УМЕТЬ ОТКАЗАТЬ: на выключенном гейте проверка
        /// «двойник привязан» обязана дать отказ; на заведомо низком пороге
        /// привязывается и член с собственной линией — приговор следует мере.
        /// </summary>
        static void Control()
        {
            Console.WriteLine();
            Console.WriteLine("=== 4. положительный контроль: гейт выключен / порог заведомо низкий ===");
            FsaAnalyzer analyzer;
            FsaResult off = Run(0.0, false, "гейт ВЫКЛ", out analyzer);
            Same("разбор без гейта получился", true, off != null);
            if (off != null)
            {
                FsaComponentResult twin = Row(off, Twin);
                Denies("гейт выключен — «двойник привязан» СТОРОЖ ОБЯЗАН ОТВЕРГНУТЬ",
                       twin != null && twin.TiedTo == Host);
                Denies("гейт выключен — привязок в результате быть не должно", off.Ties.Count > 0);
                Same("гейт выключен — судимых 0", 0, analyzer.ChainTieJudged);
                Same("гейт выключен — двойник в составе со своей амплитудой", true,
                     twin != null && twin.TiedTo == null);
            }

            // Заведомо низкий порог — НИЖЕ меры члена с собственной линией:
            // тогда привязывается и он, то есть гейт различает по мере, а не
            // по имени. Порог берётся у самой меры (половина), а не числом.
            FsaResult mid = Run(double.NaN, false, null, out analyzer);
            double ownShare = double.NaN;
            foreach (FsaTie verdict in analyzer.ChainTieJudgements ?? new List<FsaTie>())
            {
                if (verdict.Member == Own) ownShare = verdict.Share;
            }

            Same("мера своей линии измерена и не ноль (столбцы соседствуют)", true, ownShare > 0.0);
            if (ownShare > 0.0)
            {
                double lowThreshold = 0.5 * ownShare;
                FsaResult low = Run(lowThreshold, false,
                                    "порог " + lowThreshold.ToString("G3", CultureInfo.InvariantCulture), out analyzer);
                Same("разбор с заведомо низким порогом получился", true, low != null);
                if (low != null)
                {
                    Console.WriteLine("  {0}", analyzer.ChainTieNote ?? "(гейт не судил)");
                    Same("при заведомо низком пороге привязано больше одного", true, analyzer.ChainTieTied > 1);
                    FsaComponentResult own = Row(low, Own);
                    Denies("при заведомо низком пороге член с собственной линией ОБЯЗАН привязаться (иначе гейт не по мере)",
                           own != null && own.TiedTo == null);
                }
            }

            // Сцена без хозяина в спектре: колонка хозяина отсеяна — двойник
            // не пропадает молча, а стоит подавленным либо пределом с партнёром.
            FsaResult noHost = Run(double.NaN, false, "хозяина в спектре нет", out analyzer, true);
            Same("разбор без хозяина получился", true, noHost != null);
            if (noHost != null)
            {
                bool tied = analyzer.ChainTieTied == 1;
                Same("двойник и без хозяина в спектре привязан (образы те же)", true, tied);
                FsaComponentResult twinRow = Row(noHost, Twin);
                FsaCharacteristicLimit twinLimit = Limit(noHost, Twin);
                bool suppressed = false;
                foreach (FsaSuppressedImage cut in noHost.SuppressedImages)
                {
                    if (cut.Name == Twin) suppressed = true;
                }

                Console.WriteLine("  без хозяина: строка состава {0}, предел {1}, подавлен {2}",
                                  twinRow != null ? "есть" : "нет",
                                  twinLimit != null ? (twinLimit.Detected ? "обнаружен" : "не обнаружен") : "нет",
                                  suppressed ? "да" : "нет");
                Same("двойник не пропал: строка состава, предел или подавленный образ", true,
                     twinRow != null || twinLimit != null || suppressed);
                if (twinLimit != null)
                {
                    Same("предел двойника несёт партнёра", Host, twinLimit.TiedTo);
                }
            }
        }

        // ------------------------------------------------------------------
        // Сцена
        // ------------------------------------------------------------------

        /// <summary>
        /// Разбор сцены. <paramref name="tieShare"/> NaN — умолчание
        /// конструктора; <paramref name="equilibrium"/> — члены одной колонкой
        /// ряда; <paramref name="hostAbsent"/> — в спектре хозяина (и двойника)
        /// нет, образы те же.
        /// </summary>
        static FsaResult Run(double tieShare, bool equilibrium, string report, out FsaAnalyzer analyzer,
                             bool hostAbsent = false)
        {
            var spectrum = new EnergySpectrum(1.0, Channels)
            {
                EnergyCalibration = new PolynomialEnergyCalibration
                {
                    Coefficients = new[] { 0.0, 1.0 }
                },
                LiveTime = LiveTime
            };
            Array.Copy(Spectrum(hostAbsent), spectrum.Spectrum, Channels);

            analyzer = new FsaAnalyzer
            {
                MinEnergy = 40.0,
                MaxEnergy = 1800.0,
                CascadeSumming = false,
                CascadeSumPeaks = false,
                Backscatter = false,
                PileUp = false,
                AnchorScale = false,
                RequireGeometry = false
            };
            if (!double.IsNaN(tieShare))
            {
                analyzer.ChainTieShare = tieShare;
            }

            if (report != null)
            {
                FsaTuningReport.Print(analyzer, report);
            }

            return analyzer.Analyze(spectrum, null, Fwhm(), Library(equilibrium), null);
        }

        /// <summary>
        /// Линии членов: хозяин 600 кэВ 40 %, двойник 603 кэВ 4 % (в ПШПВ
        /// ≈ 40 кэВ — тот же столбец), своя 690 кэВ 25 % (в 2.1 ПШПВ от
        /// хозяина — разрешена, но столбцы ещё соседствуют: мера не ноль, и
        /// заведомо низкий порог её ловит), далёкая 1400 кэВ 8 %.
        /// Веса — как у членов ряда без связки: уже с долей ветвления.
        /// </summary>
        static List<FsaComponent> Library(bool equilibrium)
        {
            var lines = new[]
            {
                new { Name = Host, Energy = 600.0, Intensity = 40.0 },
                new { Name = Twin, Energy = 603.0, Intensity = 4.0 },
                new { Name = Own, Energy = 690.0, Intensity = 25.0 },
                new { Name = Far, Energy = 1400.0, Intensity = 8.0 }
            };

            var library = new List<FsaComponent>();
            if (equilibrium)
            {
                var chain = new FsaComponent(Root, FsaComponentKind.Chain) { DecayChainRoot = Root };
                foreach (var l in lines)
                {
                    chain.Lines.Add(new FsaLine(l.Name, l.Energy, l.Intensity));
                }

                library.Add(chain);
                return library;
            }

            foreach (var l in lines)
            {
                var member = new FsaComponent(l.Name, FsaComponentKind.Single)
                {
                    DecayChainRoot = Root,
                    TotalYieldPercent = l.Intensity
                };
                member.Lines.Add(new FsaLine(l.Name, l.Energy, l.Intensity));
                library.Add(member);
            }

            return library;
        }

        /// <summary>
        /// Спектр: сумма линий в равновесном отношении (амплитуда ряда
        /// 2·10⁵ распадов), уширенная той же ПШПВ, плюс ровный фон 30.
        /// </summary>
        static int[] Spectrum(bool hostAbsent)
        {
            FwhmCalibration fwhm = Fwhm();
            double[] deposit = new double[Channels];
            foreach (FsaComponent member in Library(false))
            {
                if (hostAbsent && (member.Name == Host || member.Name == Twin))
                {
                    continue;
                }

                foreach (FsaLine line in member.Lines)
                {
                    deposit[(int)Math.Round(line.Energy)] += 2.0E5 * line.Intensity / 100.0;
                }
            }

            double[] broadened = Broaden(deposit, fwhm);
            int[] counts = new int[Channels];
            for (int i = 20; i < Channels; i++)
            {
                counts[i] = (int)Math.Round(broadened[i] + 30.0);
            }

            return counts;
        }

        static double[] Broaden(double[] deposit, FwhmCalibration fwhm)
        {
            double[] value = new double[Channels];
            for (int b = 0; b < Channels; b++)
            {
                if (!(deposit[b] > 0.0))
                {
                    continue;
                }

                double width = fwhm.ChannelToFwhm(b);
                int span = (int)Math.Ceiling(2.5 * width);
                double norm = 0.0;
                for (int i = -span; i <= span; i++)
                {
                    norm += PeakShapeModel.RelativeValue(i, width, fwhm);
                }

                for (int i = -span; i <= span; i++)
                {
                    int at = b + i;
                    if (at < 0 || at >= Channels)
                    {
                        continue;
                    }

                    value[at] += deposit[b] * PeakShapeModel.RelativeValue(i, width, fwhm) / norm;
                }
            }

            return value;
        }

        /// <summary>ПШПВ² = 2.7·ch: 40 кэВ на 600.</summary>
        static FwhmCalibration Fwhm()
        {
            return new SimpleSqrtFwhmCalibration
            {
                Coefficients = new[] { 0.0, 2.7 }
            };
        }

        // ------------------------------------------------------------------
        // Мелочь
        // ------------------------------------------------------------------

        static FsaComponentResult Row(FsaResult result, string name)
        {
            foreach (FsaComponentResult c in result.Components)
            {
                if (c.Name == name) return c;
            }

            return null;
        }

        static FsaCharacteristicLimit Limit(FsaResult result, string name)
        {
            foreach (FsaCharacteristicLimit L in result.CharacteristicLimits)
            {
                if (L.Name == name) return L;
            }

            return null;
        }

        static string LayerTiedTo(FsaPresentation presentation, string name)
        {
            foreach (FsaStackLayer layer in presentation.Layers)
            {
                if (layer.Name == name) return layer.TiedTo;
            }

            return "(слоя нет)";
        }

        static double Sum(double[] curve)
        {
            double s = 0.0;
            if (curve != null)
            {
                foreach (double v in curve) s += v;
            }

            return s;
        }

        static string Mark(string name)
        {
            PropertyInfo p = typeof(BecquerelMonitor.Properties.Resources).GetProperty(
                name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null)
            {
                Console.WriteLine("  ⛔ нет ресурса {0}", name);
                bad++;
                return " ";
            }

            return (string)p.GetValue(null, null);
        }

        static void Language(string name)
        {
            CultureInfo culture = new CultureInfo(name);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            // Числа — инвариантной культурой (разделитель — точка), даже когда
            // проба только что переключила культуру потока на русскую.
            Console.WriteLine("  {0,-70} {1} {2}{3}", what, ok ? "=" : "!!", Show(got),
                              ok ? "" : " вместо " + Show(expected));
            if (!ok)
            {
                bad++;
            }
        }

        static string Show(object value)
        {
            if (value == null) return "(null)";
            if (value is double) return ((double)value).ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        static void Denies(string what, bool found)
        {
            Console.WriteLine("  {0} {1,-72} {2}", found ? "⛔ " : "ok  ", what,
                              found ? "СТОРОЖ ПРОМОЛЧАЛ" : "отказал, как и должен");
            if (found) bad++;
        }
    }
}
