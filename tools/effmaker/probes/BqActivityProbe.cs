using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BqActivityProbe
{
    /// <summary>
    /// (`S96`) БЕККЕРЕЛИ ПОКУПАЮТСЯ ПОДПИСЬЮ. Проба меряет три вещи, и ни одна
    /// из них не проходит «всегда»:
    ///
    /// 1. ЧИСЛО К ЧИСЛУ. Активность, посчитанная приложением по выделению,
    ///    сверяется с ручной арифметикой `A = (N/t)·100/(ε(E)·I%)`. Считает не
    ///    копия формулы, а САМА ветка вида: `EnergySpectrumView` поднимается
    ///    без окна (`FormatterServices.GetUninitializedObject`), поля ставятся
    ///    отражением, зовётся приватный `EnsureSelectionAnalytics`. Своя копия
    ///    ветки была бы вторым ответом на тот же вопрос.
    ///
    /// 2. ЦЕНА ДЕФЕКТА. Тому же пику подставляется ДРУГАЯ подпись — та, что
    ///    даёт другой выход, — и печатается, во сколько раз разъезжается
    ///    показанное число. Пара берётся не из головы: библиотека прочёсывается
    ///    на пары ВИДИМЫХ линий НАСТОЯЩИХ нуклидов (не рентген, обе с выходом
    ///    не ниже порога `MinimumActivityYieldPercent`), которые ложатся в один
    ///    пик по разрешению прибора. ⛔ Именно такая пара и нужна: на паре, где
    ///    победитель — рентген, приложение теперь ОТКАЗЫВАЕТ, и мерка «сравнить
    ///    показанные беккерели» измерила бы пустоту (разбор `S96` от 28.08.2026).
    ///
    /// 3. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. Заведомо плохие входы, на которых проверка
    ///    ОБЯЗАНА отказать: выход 0, отсутствующая кривая, энергия за краем
    ///    кривой, пик без подписи, ДВА пика в выделении, рентген, выход ниже
    ///    порога, отсутствующий фоновый спектр. По каждому печатается, что
    ///    именно случилось — ОТКАЗ с текстом, пустое место или МОЛЧАЛИВОЕ ЧИСЛО.
    ///    Молчаливое число — находка, а не «прошло».
    ///
    ///    ⛔ (05.09.2026) МОЛЧАЛИВОЕ ОТСУТСТВИЕ — тоже отказ пробы, а не
    ///    «прошло»: подпись поставлена (значит, ветка начата), а ни числа, ни
    ///    причины — на панели это неотличимо от «кривой нет». Первый прогон
    ///    05.09.2026 нашёл таких два (энергия за краем кривой снизу и сверху),
    ///    третьим было «фонового спектра нет» — там ветка не начиналась вовсе.
    ///    Для четырёх плеч — «вне кривой снизу», «вне кривой сверху», «нет ε»,
    ///    «нет фона» — ожидается ОТКАЗ СЛОВАМИ, и текст сверяется с ресурсом
    ///    сборки на ОБОИХ языках (`Thread.CurrentUICulture` en и ru); русский
    ///    текст, совпавший с английским, — признак, что сателлит `ru\` не
    ///    доехал, и это тоже отказ. На сборке ДО правки все четыре плеча
    ///    отказывают — это и есть положительный контроль самой проверки.
    ///
    ///     BqActivityProbe [--spectra=&lt;…\CORPUS\corpus\spectra&gt;]
    ///                     [--res=7] [--top=8] [--csv=bqactivity.csv]
    ///
    /// Без `--spectra` корпусная часть пропускается и об этом говорится вслух.
    /// Порог выхода и правило рентгена читаются ИЗ СБОРКИ, а не переписываются
    /// сюда: своя копия продолжала бы мерить старое число после смены порога.
    /// </summary>
    static class Program
    {
        const string LibraryPath = "config\\NuclideDefinition.xml";

        static double threshold;
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            string spectraDir = null;
            string csvPath = "bqactivity.csv";
            double res662 = 7.0;
            int top = 8;

            foreach (string a in args)
            {
                if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectraDir = a.Substring(10);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csvPath = a.Substring(6);
                else if (a.StartsWith("--res=", StringComparison.Ordinal))
                    res662 = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--top=", StringComparison.Ordinal))
                    top = int.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            // ⛔ Порог — ИЗ СБОРКИ (`public const`, поэтому только
            //    GetRawConstantValue: обычное обращение вкомпилировало бы
            //    значение в пробу намертво).
            FieldInfo fThr = typeof(EnergySpectrumView).GetField(
                "MinimumActivityYieldPercent", BindingFlags.Public | BindingFlags.Static);
            if (fThr == null)
            {
                Console.Error.WriteLine("⛔ в сборке нет MinimumActivityYieldPercent — мерить нечем");
                return 3;
            }
            threshold = Convert.ToDouble(fThr.GetRawConstantValue(), CultureInfo.InvariantCulture);

            Console.WriteLine("=== чем мерено ===");
            Console.WriteLine("порог выхода ИЗ СБОРКИ: {0} %", N(threshold));
            Console.WriteLine("сборка приложения: {0}", typeof(EnergySpectrumView).Assembly.Location);
            NuclideDefinitionManager nuclides = ReportLibrary();
            Console.WriteLine();

            Section0_Units(nuclides, spectraDir, csvPath);
            Section0b_WindowUnits();
            Section1_Formula();
            Section2_Price(nuclides, res662, top);
            Section3_PositiveControl();
            Section4_Corpus(nuclides, spectraDir, csvPath, top);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("НЕ СОШЛОСЬ: {0}", bad));
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        //  Чем мерено
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ Мерка ОБЯЗАНА называть файл библиотеки: в поставочной
        /// `BecquerelMonitor/config/NuclideDefinition.xml` и в корневой
        /// `config/NuclideDefinition.xml` вокруг 59 кэВ стоят РАЗНЫЕ линии, и
        /// подпись выходит разная. Без имени файла и его отпечатка число не
        /// повторяется.
        /// </summary>
        static NuclideDefinitionManager ReportLibrary()
        {
            string full = Path.GetFullPath(LibraryPath);
            Console.WriteLine("библиотека: {0}", full);
            if (File.Exists(full))
            {
                var info = new FileInfo(full);
                using (var sha = SHA256.Create())
                using (var s = File.OpenRead(full))
                {
                    string hash = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
                    Console.WriteLine("            {0} байт, sha256 {1}", info.Length, hash.Substring(0, 16));
                }
            }
            else
            {
                Console.WriteLine("            ⛔ ФАЙЛА НЕТ — подписи будут не те");
                bad++;
            }

            NuclideDefinitionManager m = NuclideDefinitionManager.GetInstance();
            NuclideSet set = m.ActiveSet;
            Console.WriteLine("            записей {0}, активный набор «{1}»",
                              m.NuclideDefinitions == null ? -1 : m.NuclideDefinitions.Count,
                              set != null ? set.Name : "(нет)");
            return m;
        }

        // ------------------------------------------------------------------
        //  0. ЕДИНИЦЫ ШИРИНЫ
        // ------------------------------------------------------------------

        /// <summary>
        /// (`S96`, встречная проверка 05.09.2026) В ЧЁМ МЕРИТСЯ <c>Peak.FWHM</c>.
        ///
        /// ⛔ Вопрос не праздный: этой пробой окно спора соседей задавалось как
        /// <c>0.5·peak.FWHM</c> и подставлялось в <c>SelectionFWHMinkev</c> —
        /// то есть КАК КЭВ. Если ширина канальная, окно растянуто или сжато в
        /// «кэВ-на-канал» раз, и все числа §4 посчитаны не тем окном.
        ///
        /// Мерка НЕ читает ни комментариев, ни калибровки ПШПВ: ширина того же
        /// пика измеряется ПО ОТСЧЁТАМ спектра тем же кодом, которым её меряет
        /// выделение человека (<c>EnergyResolutionCalculator.CalculateFWHM</c>,
        /// полувысота над линейной подложкой). Он возвращает СРАЗУ ОБА числа —
        /// ширину в каналах (<c>RightChannel−LeftChannel</c>) и в кэВ
        /// (<c>ResolutionInkeV</c>), — и <c>Peak.FWHM</c> сравнивается с каждым.
        ///
        /// ⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ВСТРОЕН В МЕРКУ. Там, где кэВ-на-канал
        /// близок к единице, оба ответа совпадают, и проверка не меряет НИЧЕГО:
        /// такие спектры помечаются «СЛЕПОЙ» и в счёт вердикта не идут. Вердикт
        /// выносят только спектры, на которых ответы РАЗЛИЧАЮТСЯ (германий —
        /// 0.36 кэВ на канал, то есть плечи разъезжаются в 2.8 раза). Если
        /// различающих спектров нет вовсе — так и сказано, и это отказ пробы.
        /// </summary>
        static void Section0_Units(NuclideDefinitionManager nuclides, string spectraDir, string csvPath)
        {
            Console.WriteLine("=== 0. ЕДИНИЦЫ: в чём меряется Peak.FWHM ===");
            if (string.IsNullOrEmpty(spectraDir))
            {
                Console.WriteLine("  ключ --spectra не задан — часть про единицы ПРОПУЩЕНА, и это сказано вслух.");
                Console.WriteLine();
                return;
            }
            if (!Directory.Exists(spectraDir))
            {
                Console.WriteLine("  ⛔ нет каталога {0}", spectraDir);
                bad++;
                Console.WriteLine();
                return;
            }

            Console.WriteLine("  ширина того же пика меряется по ОТСЧЁТАМ (EnergyResolutionCalculator —");
            Console.WriteLine("  тот же код, что у выделения человека) и даёт два числа: W_кан и W_кэВ.");
            Console.WriteLine("  Спектры с кэВ/кан ≈ 1 РАЗЛИЧИТЬ НЕ МОГУТ и помечены СЛЕПОЙ.");
            Console.WriteLine();

            // ⛔ Список пиков обязан совпасть с §4 до пика: правило близнецов
            //    (`isNewPeak`) зависит от ПОДПИСЕЙ, поэтому набор и библиотека
            //    берутся те же самые, а не null.
            NuclideSet uSet = nuclides.ActiveSet;
            List<NuclideDefinition> uDefs = nuclides.NuclideDefinitions;

            var csv = new StringBuilder();
            csv.AppendLine("spectrum,channels,kev_per_ch,peak_kev,peak_ch,fwhm_field,"
                           + "w_ch_empirical,w_kev_empirical,ratio_to_ch,ratio_to_kev,fwhmcal_at_ch");
            Console.WriteLine("  {0,-22} {1,6} {2,9} {3,5} {4,11} {5,11}  вердикт",
                              "спектр", "кан", "кэВ/кан", "пиков", "FWHM/W_кан", "FWHM/W_кэВ");

            int channelsVerdict = 0, kevVerdict = 0, blind = 0, noPeaks = 0;
            var lines = new List<string>();
            var dissent = new List<string>();
            // Общий котёл ПИКОВ различающих спектров: вердикт выносится по нему,
            // а не голосованием спектров. У спектра из двух пиков медиана —
            // среднее двух чисел, и один промах мерки решает его «вердикт».
            var poolCh = new List<double>();
            var poolKev = new List<double>();
            var poolCal = new List<double>();

            foreach (string file in Directory.GetFiles(spectraDir, "*.xml")
                                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                ResultData rd;
                List<Peak> found;
                try
                {
                    rd = LoadResult(file);
                    found = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible,
                                                          SmoothingMethod.None, uSet, uDefs);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("{0}: {1}", name, e.Message);
                    continue;
                }
                if (found == null) found = new List<Peak>();

                EnergySpectrum spec = rd.EnergySpectrum;
                EnergyCalibration cal = spec.EnergyCalibration;
                int n = spec.NumberOfChannels;
                double[] counts = new double[spec.Spectrum.Length];
                for (int i = 0; i < counts.Length; i++) counts[i] = spec.Spectrum[i];

                var rCh = new List<double>();
                var rKev = new List<double>();
                var rCal = new List<double>();
                var disp = new List<double>();

                foreach (Peak peak in found)
                {
                    if (!(peak.FWHM > 0.0) || Double.IsNaN(peak.FWHM)) continue;
                    int ch = peak.Channel;
                    if (ch < 2 || ch >= n - 2) continue;

                    double kevPerCh = Math.Abs(cal.ChannelToEnergy(ch + 0.5) - cal.ChannelToEnergy(ch - 0.5));
                    if (!(kevPerCh > 0.0)) continue;

                    // Окно поиска долин берётся ЩЕДРЫМ и покрывает ОБА
                    // прочтения сразу (ширина как каналы и как кэВ), поэтому
                    // выбор окна ответа не предрешает: сам ответ даёт
                    // полувысота, найденная по отсчётам внутри окна.
                    double spanCh = 3.0 * Math.Max(peak.FWHM, peak.FWHM / kevPerCh);
                    int span = (int)Math.Ceiling(Math.Min(Math.Max(spanCh, 6.0), n / 4.0));

                    int left = Valley(counts, ch, -1, span);
                    int right = Valley(counts, ch, +1, span);
                    if (right - left < 4) continue;

                    EnergyResolutionResult res =
                        EnergyResolutionCalculator.CalculateFWHM(counts, n, cal, left, right);
                    if (res == null) continue;
                    double wCh = res.RightChannel - res.LeftChannel;
                    double wKev = res.ResolutionInkeV;
                    if (!(wCh > 0.0) || !(wKev > 0.0)) continue;
                    // Найденная вершина обязана быть ТЕМ ЖЕ пиком, иначе меряем
                    // соседа: центр полувысоты не дальше половины окна.
                    if (Math.Abs(res.MaxChannel - ch) > 0.5 * span) continue;

                    double fcal = rd.FwhmCalibration != null ? rd.FwhmCalibration.ChannelToFwhm(ch) : double.NaN;

                    rCh.Add(peak.FWHM / wCh);
                    rKev.Add(peak.FWHM / wKev);
                    if (fcal > 0.0 && !Double.IsNaN(fcal)) rCal.Add(peak.FWHM / fcal);
                    disp.Add(kevPerCh);

                    csv.AppendLine(string.Join(",", name, n.ToString(CultureInfo.InvariantCulture),
                        F(kevPerCh, "G6"), F(peak.Energy, "F3"), ch.ToString(CultureInfo.InvariantCulture),
                        F(peak.FWHM, "F4"), F(wCh, "F4"), F(wKev, "F4"),
                        F(peak.FWHM / wCh, "F4"), F(peak.FWHM / wKev, "F4"), F(fcal, "F4")));
                }

                if (rCh.Count == 0) { noPeaks++; continue; }

                double mDisp = Median(disp);
                double mCh = Median(rCh);
                double mKev = Median(rKev);
                string verdict;
                // Различает ли этот спектр вообще: во сколько раз разъезжаются
                // два прочтения. Порог 1.5 — и он ЗАДРАН НАРОЧНО: при 1.25 в
                // «различающие» попал `ASN8_Th232_4096` (0.76 кэВ/кан), где оба
                // прочтения лежат в 0.71 и 0.92 от измеренной ширины, то есть
                // спектр на деле слеп. Считать его голосом — считать шум.
                double spread = mDisp > 1.0 ? mDisp : 1.0 / mDisp;
                if (spread < 1.5 || rCh.Count < 4) { verdict = "СЛЕПОЙ"; blind++; }
                else if (Math.Abs(Math.Log(mCh)) < Math.Abs(Math.Log(mKev)))
                {
                    verdict = "КАНАЛЫ"; channelsVerdict++;
                    poolCh.AddRange(rCh); poolKev.AddRange(rKev); poolCal.AddRange(rCal);
                }
                else
                {
                    verdict = "КЭВ"; kevVerdict++;
                    poolCh.AddRange(rCh); poolKev.AddRange(rKev); poolCal.AddRange(rCal);
                    dissent.Add(string.Format(CultureInfo.InvariantCulture,
                        "    {0}: кэВ/кан {1}, пиков {2}, FWHM/W_кан {3}, FWHM/W_кэВ {4}",
                        name, F(mDisp, "F4"), rCh.Count, F(mCh, "F3"), F(mKev, "F3")));
                }

                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-22} {1,6} {2,9} {3,5} {4,11} {5,11}  {6}",
                    Cut(name, 22), n, F(mDisp, "F4"), rCh.Count, F(mCh, "F3"), F(mKev, "F3"),
                    verdict + (spread < 1.5 && rCh.Count >= 4 ? " (кэВ/кан≈1)" : rCh.Count < 4 ? " (пиков мало)" : "")));
            }

            // Печатаем сперва РАЗЛИЧАЮЩИЕ спектры — на них и стоит вердикт.
            foreach (string s in lines) if (s.IndexOf("СЛЕПОЙ", StringComparison.Ordinal) < 0) Console.WriteLine(s);
            Console.WriteLine("  --- ниже спектры, которые РАЗЛИЧИТЬ НЕ МОГУТ (проверка на них слепа) ---");
            foreach (string s in lines) if (s.IndexOf("СЛЕПОЙ", StringComparison.Ordinal) >= 0) Console.WriteLine(s);

            string unitsCsv = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(csvPath)) ?? ".",
                                           Path.GetFileNameWithoutExtension(csvPath) + "_units.csv");
            File.WriteAllText(unitsCsv, csv.ToString(), new UTF8Encoding(false));

            Console.WriteLine();
            Console.WriteLine("  различающих спектров {0} (кэВ/кан отличается от 1 не меньше чем в 1.5 раза,",
                              channelsVerdict + kevVerdict);
            Console.WriteLine("  и пиков в спектре не меньше четырёх): вердикт КАНАЛЫ у {0}, КЭВ у {1}; слепых {2}; без пиков {3}",
                              channelsVerdict, kevVerdict, blind, noPeaks);
            if (dissent.Count > 0)
            {
                Console.WriteLine("  спектры, ушедшие в меньшинство — поимённо:");
                foreach (string d in dissent) Console.WriteLine(d);
            }

            double gCh = Median(poolCh), gKev = Median(poolKev), gCal = Median(poolCal);
            Console.WriteLine("  ПО ВСЕМ ПИКАМ различающих спектров ({0} шт.): FWHM/W_кан = {1}, FWHM/W_кэВ = {2}",
                              poolCh.Count, F(gCh, "F3"), F(gKev, "F3"));
            Console.WriteLine("  второй якорь, независимый от моей мерки: FWHM / FwhmCalibration.ChannelToFwhm(канал) = {0}",
                              F(gCal, "F3"));
            Console.WriteLine("  (финдер принимает пик только при отношении внутри [Min_FWHM_Tol, Max_FWHM_Tol],");
            Console.WriteLine("   то есть ширина у пика и у КАНАЛЬНОЙ калибровки ПШПВ — одна и та же величина)");

            if (channelsVerdict + kevVerdict == 0)
            {
                Console.WriteLine("  ⛔ РАЗЛИЧАЮЩИХ ВХОДОВ НЕТ — проверка ничего не измерила");
                bad++;
            }
            else if (Double.IsNaN(gCh) || Double.IsNaN(gKev) ||
                     Math.Abs(Math.Log(gCh)) >= Math.Abs(Math.Log(gKev)))
            {
                Console.WriteLine("  ⛔ ВЫВОД: Peak.FWHM меряется В КЭВ — прочтение «каналы» ОПРОВЕРГНУТО");
                bad++;
            }
            else
            {
                Console.WriteLine("  ВЫВОД: Peak.FWHM меряется В КАНАЛАХ.");
                double minority = (double)kevVerdict / (channelsVerdict + kevVerdict);
                if (minority > 0.1)
                {
                    Console.WriteLine("  ⛔ но меньшинство больше десятой части ({0:P1}) — единица не одна", minority);
                    bad++;
                }
            }
            Console.WriteLine("  -> {0}", unitsCsv);
            Console.WriteLine();
        }

        /// <summary>
        /// (`S96`, встречная проверка 05.09.2026) В ЧЁМ МЕРИТ ОКНО СПОРА САМО
        /// ПРИЛОЖЕНИЕ. Вопрос отдельный от предыдущего: `Peak.FWHM` может быть
        /// в каналах, а окно `ScanActivityRivals` при этом — в кэВ, и наоборот.
        /// Смешение каналов и кэВ ВНУТРИ ПРИЛОЖЕНИЯ было бы дефектом, который
        /// человек видит предупреждением «подпись может быть другой» там, где
        /// его быть не должно.
        ///
        /// Мерка в двух шагах, оба через НАСТОЯЩУЮ ветку вида:
        ///
        /// 1. `SelectionFWHMinkev`, посчитанная приложением на сцене, где
        ///    кэВ-на-канал заведомо НЕ ЕДИНИЦА, сверяется с истинной шириной
        ///    гауссианы в кэВ и с ней же в каналах. Сцена 1 кэВ = 1 канал
        ///    гоняется рядом и НАЗВАНА СЛЕПОЙ: на ней оба ответа совпадают.
        /// 2. `ScanActivityRivals` зовётся с подставными линиями, разложенными
        ///    по обе стороны от края окна В КЭВ: 0.4 и 0.6 полуширины. Соперник
        ///    обязан найтись ровно один. Если окно на деле канальное, при
        ///    0.25 кэВ/канал оно вчетверо шире и заберёт обе линии.
        /// </summary>
        static void Section0b_WindowUnits()
        {
            Console.WriteLine("=== 0б. ЕДИНИЦЫ ОКНА СПОРА в самом приложении ===");

            Console.WriteLine("  {0,10} {1,12} {2,12} {3,12}  {4}",
                              "кэВ/кан", "SelFWHM,кэВ", "истина,кэВ", "истина,кан", "вердикт");
            double[] disps = { 1.0, 0.25, 4.0 };
            foreach (double d in disps)
            {
                Scene sc = Scene.At(662.0, d);
                sc.SetLabel("Cs-137", 661.657, 85.1);
                Scene.Answer a = sc.Run();
                double trueKev = sc.TrueFwhmKev;
                double trueCh = trueKev / d;
                string verdict;
                if (Math.Abs(d - 1.0) < 1e-9) verdict = "СЛЕПАЯ СЦЕНА (кэВ = канал)";
                else if (Math.Abs(a.SelFwhmKev - trueKev) < 0.15 * trueKev) verdict = "КЭВ";
                else if (Math.Abs(a.SelFwhmKev - trueCh) < 0.15 * trueCh) verdict = "КАНАЛЫ ⛔";
                else verdict = "ни то ни другое ⛔";
                Console.WriteLine("  {0,10} {1,12} {2,12} {3,12}  {4}",
                                  N(d), F(a.SelFwhmKev, "F3"), F(trueKev, "F3"), F(trueCh, "F3"), verdict);
                if (verdict.IndexOf('⛔') >= 0) bad++;
            }

            // --- шаг 2: окно спора подставными линиями ---
            const double kevPerCh = 0.25;
            Scene sc2 = Scene.At(662.0, kevPerCh);
            sc2.SetLabel("Cs-137", 661.657, 85.1);
            Scene.Answer a2 = sc2.Run();
            double halfWindowKev = 0.5 * a2.SelFwhmKev;

            // ⛔ Линия «ЗА КРАЕМ» отстоит на 1.5 окна В КЭВ, но всего на 0.375
            //    окна, если бы окно читалось как КАНАЛЫ (при 0.25 кэВ/канал оно
            //    было бы вчетверо шире). То есть вход РАЗЛИЧАЕТ два прочтения:
            //    в кэВ соперник один, в каналах — два.
            var defs = new List<NuclideDefinition>
            {
                Line("ВНУТРИ", 661.657 + 0.5 * halfWindowKev, 10.0),
                Line("ЗА КРАЕМ", 661.657 + 1.5 * halfWindowKev, 10.0),
            };
            int rivals = CountRivals(sc2, defs);
            Console.WriteLine();
            Console.WriteLine("  окно спора при кэВ/кан = {0}: полуширина {1} кэВ (= {2} каналов)",
                              N(kevPerCh), F(halfWindowKev, "F3"), F(halfWindowKev / kevPerCh, "F1"));
            Console.WriteLine("  линии на 0.5 и 1.5 окна В КЭВ (вторая была бы ВНУТРИ канального окна): соперников {0}", rivals);
            if (rivals == 1)
            {
                Console.WriteLine("  ВЫВОД: окно `ScanActivityRivals` — В КЭВ, и с `SelectionFWHMinkev` сходится.");
            }
            else
            {
                Console.WriteLine("  ⛔ ждали ровно одного: окно не в кэВ либо считает не то");
                bad++;
            }

            // Положительный контроль самой мерки: обе линии внутри окна —
            // соперников обязано стать два. Проверка, не умеющая дать другого
            // ответа, ничего не меряет.
            var defs2 = new List<NuclideDefinition>
            {
                Line("ВНУТРИ", 661.657 + 0.5 * halfWindowKev, 10.0),
                Line("ТОЖЕ ВНУТРИ", 661.657 - 0.3 * halfWindowKev, 10.0),
            };
            int rivals2 = CountRivals(sc2, defs2);
            Console.WriteLine("  положительный контроль мерки: обе линии внутри окна -> соперников {0} (ждали 2)", rivals2);
            if (rivals2 != 2) bad++;

            // И обратный: обе за краем -> ноль. Иначе «один» мог бы значить
            // «считает всегда одного».
            var defs3 = new List<NuclideDefinition>
            {
                Line("ЗА КРАЕМ", 661.657 + 1.5 * halfWindowKev, 10.0),
                Line("ДАЛЕКО", 661.657 - 3.0 * halfWindowKev, 10.0),
            };
            int rivals3 = CountRivals(sc2, defs3);
            Console.WriteLine("  обратный контроль: обе линии за краем -> соперников {0} (ждали 0)", rivals3);
            if (rivals3 != 0) bad++;
            Console.WriteLine();
        }

        static NuclideDefinition Line(string name, double kev, double intensity)
        {
            return new NuclideDefinition
            {
                Name = name, Energy = kev, Intencity = intensity,
                Visible = true, Sets = new HashSet<Guid>(),
            };
        }

        /// <summary>Зовёт приватный `ScanActivityRivals` с подставным списком линий.</summary>
        static int CountRivals(Scene sc, List<NuclideDefinition> defs)
        {
            Type tView = typeof(EnergySpectrumView);
            Type tAn = tView.GetNestedType("SelectionAnalytics", BindingFlags.NonPublic);
            MethodInfo mScan = tView.GetMethod("ScanActivityRivals", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo fMgr = tView.GetField("nuclideManager", BindingFlags.NonPublic | BindingFlags.Instance);

            var mgr = new NuclideDefinitionManager();
            mgr.NuclideDefinitionFile = new NuclideDefinitionFile
            {
                NuclideDefinitions = defs,
                NuclideSets = new List<NuclideSet>(),
            };
            mgr.ActiveSet = null;

            object view = FormatterServices.GetUninitializedObject(tView);
            fMgr.SetValue(view, mgr);
            object an = Activator.CreateInstance(tAn, true);
            tAn.GetProperty("SelectionFWHMinkev").SetValue(an, sc.Run().SelFwhmKev, null);
            mScan.Invoke(view, new object[] { sc.LabelledPeak, an });
            return (int)tAn.GetProperty("ActivityRivals").GetValue(an, null);
        }

        /// <summary>Дно ямы рядом с вершиной: сглаженный по трём минимум внутри окна.</summary>
        static int Valley(double[] c, int apex, int dir, int span)
        {
            int best = apex + dir;
            double bestV = double.MaxValue;
            for (int k = 1; k <= span; k++)
            {
                int i = apex + dir * k;
                if (i < 1 || i >= c.Length - 1) break;
                double v = (c[i - 1] + c[i] + c[i + 1]) / 3.0;
                if (v < bestV) { bestV = v; best = i; }
                else if (k > 3 && v > 1.5 * bestV + 1.0) break;
            }
            return best;
        }

        static double Median(List<double> v)
        {
            if (v == null || v.Count == 0) return double.NaN;
            var a = new List<double>(v);
            a.Sort();
            int m = a.Count / 2;
            return a.Count % 2 == 1 ? a[m] : 0.5 * (a[m - 1] + a[m]);
        }

        /// <summary>
        /// (`S96`, 05.09.2026) ШИРИНА ПИКА В КЭВ — тем же выражением, что в
        /// панели поиска пиков и в отборе подписи (<c>PeakDetector.FwhmKev</c>).
        /// ⛔ Не «умножить на кэВ-на-канал»: калибровка нелинейна, и половинки
        /// растягиваются по-разному.
        /// </summary>
        static double FwhmKev(Peak peak, EnergyCalibration calibration)
        {
            if (calibration == null || !(peak.FWHM > 0.0) || Double.IsNaN(peak.FWHM))
            {
                return 0.0;
            }
            return Math.Abs(calibration.ChannelToEnergy(peak.Channel + peak.FWHM / 2.0)
                            - calibration.ChannelToEnergy(peak.Channel - peak.FWHM / 2.0));
        }

        // ------------------------------------------------------------------
        //  1. Число к числу
        // ------------------------------------------------------------------

        static void Section1_Formula()
        {
            Console.WriteLine("=== 1. формула: число приложения против ручной арифметики ===");

            Scene sc = Scene.Standard();
            sc.SetLabel("Cs-137", 661.657, 85.1);
            Scene.Answer a = sc.Run();

            if (!a.Ok)
            {
                Console.WriteLine("!! ветка активности не сработала вовсе: {0}", a.Why);
                bad++;
                return;
            }

            // Ручная арифметика, записанная ЗДЕСЬ и ни с чем не общая.
            double net = sc.FgCounts - sc.BgCounts * sc.FgTime / sc.BgTime;
            double eps = sc.EpsAt(sc.PeakEnergy);
            double byHand = (net / sc.FgTime) * 100.0 / (eps * 85.1);

            Console.WriteLine("  выделение {0}-{1} кан.; N(gross) = {2}, N(фон) = {3}, t = {4} с, t(фон) = {5} с",
                              sc.StartChannel, sc.EndChannel, N(sc.FgCounts), N(sc.BgCounts), N(sc.FgTime), N(sc.BgTime));
            Console.WriteLine("  N(net) = {0}; ε({1} кэВ) = {2}; I = 85.1 %",
                              N(net), N(sc.PeakEnergy), N(eps));
            Console.WriteLine("  приложение: A = {0} Бк   вручную (N/t)·100/(ε·I) = {1} Бк   расхождение {2}",
                              N(a.Activity), N(byHand), Rel(a.Activity, byHand));
            Near("A совпала с ручной", byHand, a.Activity, 1e-9);

            // ⛔ Якорь строкой: число обязано быть ТЕМ ЖЕ до и после любой
            //    правки ветки (05.09.2026 ветка переписана двумя помощниками).
            //    «G6» — как в журнале, «n2» — как рисует панель
            //    (`EnergySpectrumView`, floatFormat = "n2"; культура здесь
            //    инвариантная, у человека — своя, отличается разделителями).
            string panel = a.Activity.ToString("n2", CultureInfo.InvariantCulture);
            Console.WriteLine("  строкой: G6 = {0}; панель (n2) = {1}", N(a.Activity), panel);
            Same("A строкой G6", "1.41755E+06", N(a.Activity));
            // Якорь снят с ПРЕЖНЕЙ сборки (bin\Debug_B9_old, 05.09.2026), а не
            // посчитан: ε в узле 662 кэВ у лог-лог интерполятора не ровно 1e-3.
            Same("A строкой панели n2", "1,417,553.47", panel);

            // ⚠ Заголовок строки `S96` пишет `A = N·100/(ε·I%)`, БЕЗ времени.
            //    На деле в `ROIAriphmetics.CalculateActivity` стоит cps, то есть
            //    N/t. Разница — ровно множитель t, и на этой сцене она видна.
            double asRowSays = net * 100.0 / (eps * 85.1);
            Console.WriteLine("  ⚠ по записи из строки S96 (без деления на t) вышло бы {0} Бк — в {1} раз больше",
                              N(asRowSays), N(sc.FgTime));

            // Коэффициент отдельно: он и есть то, что покупается подписью.
            double k, dk;
            bool ok = BecquerelCoefficient.TryForLine(sc.PeakEnergy, 85.1, sc.Curve, out k, out dk);
            Console.WriteLine("  K = {0} (ожидали {1}), dK = {2}", N(k), N(100.0 / (eps * 85.1)), N(dk));
            Same("TryForLine отдала коэффициент", true, ok);
            Near("K = 100/(ε·I)", 100.0 / (eps * 85.1), k, 1e-9);
            Near("A = cps·K", (net / sc.FgTime) * k, a.Activity, 1e-9);
            Console.WriteLine();
        }

        // ------------------------------------------------------------------
        //  2. Цена дефекта
        // ------------------------------------------------------------------

        static void Section2_Price(NuclideDefinitionManager nuclides, double res662, int top)
        {
            Console.WriteLine("=== 2. цена подмены подписи ===");
            Console.WriteLine("  ⛔ пара берётся такая, где ОБЕ подписи — настоящие нуклиды: на паре с");
            Console.WriteLine("     рентгеном приложение отказывает, и мерить было бы нечего.");
            Console.WriteLine("  разрешение модели: {0} % на 662 кэВ, ПШПВ(E) = {0}%·662·sqrt(E/662);", N(res662));
            Console.WriteLine("  соперником считается линия ближе половины ПШПВ — то же правило, что в ScanActivityRivals.");

            List<NuclideDefinition> defs = nuclides.NuclideDefinitions;
            NuclideSet set = nuclides.ActiveSet;
            var live = new List<NuclideDefinition>();
            foreach (NuclideDefinition d in defs)
            {
                if (d == null || !d.Visible || d.Energy <= 0.0 || !(d.Intencity > 0.0)) continue;
                if (set != null && (d.Sets == null || !d.Sets.Contains(set.Id))) continue;
                if (NuclideDefinition.IsElementXrayName(d.Name)) continue;   // отказ, мерить нечего
                if (d.Intencity < threshold) continue;                      // отказ, мерить нечего
                live.Add(d);
            }
            live.Sort((x, y) => x.Energy.CompareTo(y.Energy));
            Console.WriteLine("  линий, по которым активность ВООБЩЕ показывается: {0} из {1}",
                              live.Count, defs.Count);

            var pairs = new List<Tuple<double, NuclideDefinition, NuclideDefinition>>();
            for (int i = 0; i < live.Count; i++)
            {
                for (int j = i + 1; j < live.Count; j++)
                {
                    double mid = 0.5 * (live[i].Energy + live[j].Energy);
                    double window = 0.5 * Fwhm(mid, res662);
                    if (live[j].Energy - live[i].Energy > window) break;
                    if (live[i].Name == live[j].Name) continue;   // одна и та же подпись — цены нет
                    double f = live[i].Intencity / live[j].Intencity;
                    if (f < 1.0) f = 1.0 / f;
                    pairs.Add(Tuple.Create(f, live[i], live[j]));
                }
            }

            Console.WriteLine("  пар «два настоящих нуклида в одном пике»: {0}", pairs.Count);
            if (pairs.Count == 0)
            {
                Console.WriteLine("  !! таких пар нет — цена дефекта на этой библиотеке НЕ ИЗМЕРИМА");
                bad++;
                Console.WriteLine();
                return;
            }

            pairs.Sort((x, y) => y.Item1.CompareTo(x.Item1));
            Console.WriteLine();
            Console.WriteLine("  {0,-22} {1,10} {2,9}   {3,-22} {4,10} {5,9}  {6,8}",
                              "подпись A", "кэВ", "I,%", "подпись Б", "кэВ", "I,%", "раз");
            foreach (var p in pairs.Take(top))
            {
                Console.WriteLine("  {0,-22} {1,10} {2,9}   {3,-22} {4,10} {5,9}  {6,8}",
                                  Cut(p.Item2.Name, 22), N(p.Item2.Energy), N(p.Item2.Intencity),
                                  Cut(p.Item3.Name, 22), N(p.Item3.Energy), N(p.Item3.Intencity),
                                  p.Item1.ToString("F2", CultureInfo.InvariantCulture));
            }

            // ⛔ И теперь — ЧИСЛОМ НА ЭКРАНЕ, а не отношением выходов: та же
            //    сцена, тот же пик, две подписи по очереди через настоящую
            //    ветку вида.
            Console.WriteLine();
            Console.WriteLine("  та же пара, прогнанная сценой ЧЕРЕЗ ВЕТКУ ВИДА (число, которое увидит человек):");
            var worst = pairs[0];
            PriceOnScene(worst.Item2, worst.Item3, worst.Item1);

            // Отдельно — пара на энергии, где кривая корпусных приборов живёт
            // уверенно: у худшей пары энергия бывает у самого края.
            var midband = pairs.FirstOrDefault(p => p.Item2.Energy > 200.0 && p.Item2.Energy < 1600.0);
            if (midband != null && !ReferenceEquals(midband, worst))
            {
                Console.WriteLine();
                Console.WriteLine("  худшая пара В СЕРЕДИНЕ ШКАЛЫ (200-1600 кэВ):");
                PriceOnScene(midband.Item2, midband.Item3, midband.Item1);
            }
            Console.WriteLine();
        }

        static void PriceOnScene(NuclideDefinition a, NuclideDefinition b, double expected)
        {
            double energy = 0.5 * (a.Energy + b.Energy);
            Scene sc = Scene.At(energy);

            sc.SetLabel(a.Name, a.Energy, a.Intencity);
            Scene.Answer ra = sc.Run();
            sc.SetLabel(b.Name, b.Energy, b.Intencity);
            Scene.Answer rb = sc.Run();

            if (!ra.Ok || !rb.Ok)
            {
                Console.WriteLine("  !! на сцене {0} кэВ активность не показалась: {1} / {2}",
                                  N(energy), ra.Why ?? "показана", rb.Why ?? "показана");
                bad++;
                return;
            }

            double ratio = ra.Activity > rb.Activity ? ra.Activity / rb.Activity : rb.Activity / ra.Activity;
            Console.WriteLine("    пик {0} кэВ, один и тот же счёт N(net) = {1} за {2} с, ε = {3}",
                              N(energy), N(sc.NetCounts), N(sc.FgTime), N(sc.EpsAt(energy)));
            Console.WriteLine("    подпись «{0}» {1} кэВ, I = {2} %  ->  A = {3} Бк",
                              a.Name, N(a.Energy), N(a.Intencity), N(ra.Activity));
            Console.WriteLine("    подпись «{0}» {1} кэВ, I = {2} %  ->  A = {3} Бк",
                              b.Name, N(b.Energy), N(b.Intencity), N(rb.Activity));
            Console.WriteLine("    ⛔ ЦЕНА ПОДМЕНЫ: {0}× ; спор соседей приложение {1}",
                              ratio.ToString("F2", CultureInfo.InvariantCulture),
                              ra.Rivals > 0 ? string.Format("НАЗВАЛО ({0} соперн., до {1}×)",
                                                            ra.Rivals, ra.RivalFactor.ToString("F2", CultureInfo.InvariantCulture))
                                            : "ПРОМОЛЧАЛО — голое число");
            Near("цена сошлась с отношением выходов", expected, ratio, 1e-6);
        }

        // ------------------------------------------------------------------
        //  3. Положительный контроль
        // ------------------------------------------------------------------

        static void Section3_PositiveControl()
        {
            Console.WriteLine("=== 3. положительный контроль: заведомо плохие входы ===");
            Console.WriteLine("  ожидание — ОТКАЗ или пустое место; молчаливое число есть НАХОДКА.");
            Console.WriteLine();
            Console.WriteLine("  {0,-34} {1,-46} {2,-14} {3}", "плохой вход", "что случилось", "A, Бк", "видно на панели");

            // (а) выход 0 — ветка не начинается вовсе
            Scene s = Scene.Standard(); s.SetLabel("Cs-137", 661.657, 0.0);
            Verdict("выход I = 0", s.Run(), mustBeSilentNumber: false);

            // (б) выход отрицательный — то же условие Intencity > 0
            s = Scene.Standard(); s.SetLabel("Cs-137", 661.657, -5.0);
            Verdict("выход I < 0", s.Run(), mustBeSilentNumber: false);

            // (в) кривой нет
            s = Scene.Standard(); s.SetLabel("Cs-137", 661.657, 85.1); s.Result.Efficiency = null;
            Verdict("кривой эффективности нет", s.Run(), mustBeSilentNumber: false);

            // (г) энергия за краем кривой — ОТКАЗ СЛОВАМИ на обоих языках.
            //     Кривая сцены 50…2000 кэВ; края берутся ИЗ НЕЁ, а не из
            //     головы, и в ожидаемый текст подставляются те же числа.
            Refusal("энергия ниже первой точки кривой",
                    () => { Scene x = Scene.At(30.0); x.SetLabel("Cs-137", 30.0, 85.1); return x; },
                    "ActivityOutOfCurveRefused", 30.0, Scene.CurveMin, Scene.CurveMax);
            Refusal("энергия выше последней точки",
                    () => { Scene x = Scene.At(2600.0); x.SetLabel("Cs-137", 2600.0, 85.1); return x; },
                    "ActivityOutOfCurveRefused", 2600.0, Scene.CurveMin, Scene.CurveMax);

            // (г2) кривая есть, но ε не даёт: все точки с нулевой
            //      эффективностью — интерполятор их отбрасывает, кривая пуста.
            Refusal("кривая без годных точек (нет ε)",
                    () => { Scene x = Scene.Standard(); x.SetLabel("Cs-137", 661.657, 85.1); x.DropEpsilon(); return x; },
                    "ActivityNoEpsilonRefused", 662.0);

            // (д) пик без подписи
            s = Scene.Standard(); s.ClearLabel();
            Verdict("пик без подписи", s.Run(), mustBeSilentNumber: false);

            // (е) ДВА пика в выделении
            s = Scene.Standard(); s.SetLabel("Cs-137", 661.657, 85.1); s.AddSecondPeak();
            Verdict("два пика в выделении", s.Run(), mustBeSilentNumber: false);

            // (ж) рентген
            s = Scene.Standard(); s.SetLabel("W x-ray", 59.318, 100.0);
            Verdict("подпись — рентген элемента", s.Run(), mustBeSilentNumber: false);

            // (з) выход ниже порога
            s = Scene.Standard(); s.SetLabel("Pu-238", 661.657, 0.0009);
            Verdict("выход ниже порога", s.Run(), mustBeSilentNumber: false);

            // (и) фонового спектра нет — ОТКАЗ СЛОВАМИ (05.09.2026). Само
            //     поведение прежнее: без фона беккерели не считаются; новое —
            //     что об этом сказано. Считать ли их без фона — решение Amber.
            Refusal("фонового спектра нет",
                    () => { Scene x = Scene.Standard(); x.SetLabel("Cs-137", 661.657, 85.1); x.DropBackground(); return x; },
                    "ActivityNoBackgroundRefused");

            // (к) спектр не видим
            s = Scene.Standard(); s.SetLabel("Cs-137", 661.657, 85.1); s.Result.Visible = false;
            Verdict("спектр снят с показа", s.Run(), mustBeSilentNumber: false);

            // (л) пики выключены
            s = Scene.Standard(); s.SetLabel("Cs-137", 661.657, 85.1); s.PeakMode = PeakMode.Invisible;
            Verdict("режим пиков Invisible", s.Run(), mustBeSilentNumber: false);

            Console.WriteLine();
            Console.WriteLine("  ⚠ отрицательный контроль (входы ХОРОШИЕ — число обязано быть):");
            s = Scene.Standard(); s.SetLabel("Cs-137", 661.657, 85.1);
            Scene.Answer good = s.Run();
            Verdict("всё на месте", good, mustBeSilentNumber: true);
            if (!good.Ok)
            {
                Console.WriteLine("  !! проверка не мерит ничего: отказывает и на ХОРОШЕМ входе");
                bad++;
            }
            Console.WriteLine();
            Console.WriteLine("  итог: молчаливых чисел 0 (иначе выше стояло бы «НАХОДКА»);"
                              + " молчаливых ОТСУТСТВИЙ {0}{1}", silentAbsences,
                              silentAbsences > 0 ? " — ОТКАЗ ПРОБЫ" : "");
            Console.WriteLine();
        }

        /// <summary>
        /// Плечо с ОЖИДАЕМЫМ отказом: сцена собирается заново для каждого
        /// языка, текст отказа сверяется с ресурсом ЭТОЙ СБОРКИ (ключ читается
        /// из неё же — на сборке без ключа плечо отказывает вслух, а не
        /// «проходит», потому что ждать нечего).
        /// </summary>
        static void Refusal(string what, Func<Scene> make, string key, params object[] args)
        {
            var got = new Dictionary<string, string>();
            foreach (string culture in new[] { "en", "ru" })
            {
                CultureInfo prev = Thread.CurrentThread.CurrentUICulture;
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                try
                {
                    Scene.Answer a = make().Run();
                    Verdict(what + " [" + culture + "]", a, mustBeSilentNumber: false);

                    string template = AppResources.GetString(key, CultureInfo.GetCultureInfo(culture));
                    if (template == null)
                    {
                        Console.WriteLine("     ⛔ в сборке нет строки ресурса «{0}» — ждать нечего", key);
                        bad++;
                        continue;
                    }
                    string expected = string.Format(CultureInfo.CurrentCulture, template, args);
                    if (a.Refusal != expected)
                    {
                        Console.WriteLine("     !! ждали «{0}»", expected);
                        Console.WriteLine("        получили «{0}»", a.Refusal ?? "(ничего)");
                        bad++;
                    }
                    else
                    {
                        Console.WriteLine("     отказ словами: «{0}»", a.Refusal);
                        got[culture] = a.Refusal;
                    }
                }
                finally
                {
                    Thread.CurrentThread.CurrentUICulture = prev;
                }
            }

            // Русский, совпавший с английским, — сателлит `ru\` не доехал, и
            // сверка выше прошла бы на английском тексте под русской культурой.
            if (got.Count == 2 && got["en"] == got["ru"])
            {
                Console.WriteLine("     ⛔ русский текст совпал с английским — сателлита ru нет рядом с пробой");
                bad++;
            }
        }

        /// <summary>
        /// Ресурсы ПРИЛОЖЕНИЯ, по имени — класс `Properties.Resources` у сборки
        /// внутренний, отсюда его свойства не видны.
        /// </summary>
        static readonly ResourceManager AppResources =
            new ResourceManager("BecquerelMonitor.Properties.Resources", typeof(EnergySpectrumView).Assembly);

        static void Verdict(string what, Scene.Answer a, bool mustBeSilentNumber)
        {
            string outcome;
            if (a.Refusal != null) outcome = "ОТКАЗ вслух: " + Cut(a.Refusal, 34);
            else if (a.Ok && a.Label == null) outcome = "⛔ ЧИСЛО БЕЗ ПОДПИСИ";
            else if (a.Ok) outcome = a.Rivals > 0 ? "число со спором" : "⚠ ГОЛОЕ ЧИСЛО";
            else if (a.Label != null) outcome = "⚠ МОЛЧА НИЧЕГО (ветка начата)";
            else outcome = "ничего (ветка не начата)";

            Console.WriteLine("  {0,-34} {1,-46} {2,-14} {3}", what, outcome,
                              a.Ok ? N(a.Activity) : "-",
                              a.Shown ? "да" : "нет");

            if (!mustBeSilentNumber && a.Ok && a.Refusal == null)
            {
                Console.WriteLine("     ⛔ НАХОДКА: на заведомо плохом входе показано число");
                bad++;
            }

            // ⛔ Ветка НАЧАЛАСЬ (подпись поставлена, значит и кривая была, и пик
            //    один), но числа нет и отказа нет: на панели не появится ничего
            //    — ровно то же, что при «кривой нет вовсе». Отличить эти два
            //    состояния человеку нечем. Считаем это находкой, а не «прошло».
            if (!a.Ok && a.Label != null && a.Refusal == null)
            {
                Console.WriteLine("     ⛔ НАХОДКА: подпись есть, числа нет, причина НЕ НАЗВАНА —");
                Console.WriteLine("        от «кривой нет» такое состояние на панели неотличимо");
                silentAbsences++;
                bad++;   // (05.09.2026) находка = отказ пробы, а не примечание
            }
        }

        static int silentAbsences;

        // ------------------------------------------------------------------
        //  4. Корпус: часто ли пара «два настоящих нуклида» встречается вживую
        // ------------------------------------------------------------------

        static void Section4_Corpus(NuclideDefinitionManager nuclides, string spectraDir, string csvPath, int top)
        {
            Console.WriteLine("=== 4. корпус: сколько таких пиков вживую ===");
            if (string.IsNullOrEmpty(spectraDir))
            {
                Console.WriteLine("  ключ --spectra не задан — корпусная часть ПРОПУЩЕНА, и это сказано вслух.");
                Console.WriteLine();
                return;
            }
            if (!Directory.Exists(spectraDir))
            {
                Console.WriteLine("  ⛔ нет каталога {0}", spectraDir);
                bad++;
                Console.WriteLine();
                return;
            }

            Type tView = typeof(EnergySpectrumView);
            Type tAn = tView.GetNestedType("SelectionAnalytics", BindingFlags.NonPublic);
            MethodInfo mScan = tView.GetMethod("ScanActivityRivals", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo fMgr = tView.GetField("nuclideManager", BindingFlags.NonPublic | BindingFlags.Instance);
            if (tAn == null || mScan == null || fMgr == null)
            {
                Console.WriteLine("  ⛔ путь активности отражением не найден");
                bad++;
                return;
            }
            object view = FormatterServices.GetUninitializedObject(tView);
            fMgr.SetValue(view, nuclides);
            PropertyInfo pFwhmKev = tAn.GetProperty("SelectionFWHMinkev");
            PropertyInfo pRivals = tAn.GetProperty("ActivityRivals");
            PropertyInfo pFactor = tAn.GetProperty("ActivityRivalFactor");

            NuclideSet set = nuclides.ActiveSet;
            List<NuclideDefinition> defs = nuclides.NuclideDefinitions;

            var csv = new StringBuilder();
            csv.AppendLine("spectrum,peak_kev,fwhm_kev,label,line_kev,intensity_pct,verdict,"
                           + "rivals,rival_factor,real_rivals,real_factor,best_rival,best_rival_i,"
                           + "matcher_alts,matcher_factor,near_miss,near_factor,"
                           + "fwhm_channels,rivals_old_window,rival_factor_old,"
                           + "real_rivals_old_window,real_factor_old");

            int spectra = 0, failed = 0, peaks = 0, shown = 0, withRealRival = 0, silent = 0;
            int matcherOnly = 0, nearBand = 0;
            double nearBandWorst = 1.0;
            double worst = 1.0; string worstWhere = "";
            var cases = new List<Tuple<double, string>>();

            // ⛔ ПЛЕЧО «ОТОЗВАННОЕ»: то же самое, посчитанное окном, которое
            //    подставляло `Peak.FWHM` (КАНАЛЫ) прямо в `SelectionFWHMinkev`.
            //    Считается РЯДОМ, в одном проходе и по одному и тому же списку
            //    пиков, чтобы «было -> стало» отличалось РОВНО одним: шириной.
            int silentOld = 0, withRivalOld = 0, withRealRivalOld = 0, nearBandOld = 0;
            int onlyRefusableOld = 0, inflatedOld = 0;
            double nearBandWorstOld = 1.0;
            // Разряды до ветки активности — чтобы доля голых считалась от 716
            // здесь же, а не бралась из соседней пробы.
            int reach = 0, refusedXray = 0, refusedYield = 0, noCoeff = 0;
            // Спор, опирающийся на отказные линии (§6 разбора C2).
            int onlyRefusable = 0, inflated = 0, inflatedReal = 0, inflatedRealOld = 0;
            double inflatedWorst = 1.0; string inflatedWhere = "";

            foreach (string file in Directory.GetFiles(spectraDir, "*.xml")
                                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                ResultData rd;
                List<Peak> found;
                double tol;
                try
                {
                    rd = LoadResult(file);
                    var cfg = (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig;
                    tol = cfg.Tolerance;
                    found = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible,
                                                          SmoothingMethod.None, set, defs);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("{0}: {1}", name, e.Message);
                    failed++;
                    continue;
                }

                spectra++;
                if (found == null) found = new List<Peak>();
                bool hasCurve = rd.Efficiency != null;

                foreach (Peak peak in found)
                {
                    peaks++;
                    NuclideDefinition nd = peak.Nuclide;
                    if (nd == null || !(nd.Intencity > 0.0) || !hasCurve || !(peak.FWHM > 0.0)) continue;
                    reach++;
                    if (NuclideDefinition.IsElementXrayName(nd.Name)) { refusedXray++; continue; }
                    if (nd.Intencity < threshold) { refusedYield++; continue; }

                    double k, dk;
                    if (!BecquerelCoefficient.TryForLine(peak.Energy, nd.Intencity, rd.Efficiency, out k, out dk))
                    {
                        noCoeff++;
                        continue;
                    }
                    shown++;

                    // ⛔ ШИРИНА В КЭВ, а не поле `Peak.FWHM`: оно В КАНАЛАХ
                    //    (измерено §0). Считается ТЕМ ЖЕ выражением, что в
                    //    панели поиска пиков, а не умножением на кэВ-на-канал.
                    double fwhmKev = FwhmKev(peak, rd.EnergySpectrum.EnergyCalibration);
                    if (!(fwhmKev > 0.0)) continue;

                    object an = Activator.CreateInstance(tAn, true);
                    pFwhmKev.SetValue(an, fwhmKev, null);
                    mScan.Invoke(view, new object[] { peak, an });
                    int rivals = (int)pRivals.GetValue(an, null);
                    double factor = (double)pFactor.GetValue(an, null);

                    // Отозванное плечо — то же приложение, окно из КАНАЛОВ.
                    object anOld = Activator.CreateInstance(tAn, true);
                    pFwhmKev.SetValue(anOld, peak.FWHM, null);
                    mScan.Invoke(view, new object[] { peak, anOld });
                    int rivalsOld = (int)pRivals.GetValue(anOld, null);

                    // Соперники, которые сами показались бы ЧИСЛОМ: не рентген и
                    // не ниже порога. Только они и меняют показанное число.
                    int realRivals = 0; double realFactor = 1.0;
                    NuclideDefinition best = null;
                    double window = 0.5 * fwhmKev;
                    double windowOld = 0.5 * peak.FWHM;
                    int realRivalsOld = 0; double realFactorOld = 1.0;
                    int nearMissOld = 0; double nearFactorOld = 1.0;
                    // Кандидаты, до которых дотягивается САМ ПОДБОР подписи:
                    // у него окно в ПРОЦЕНТАХ (tol), а не в разрешении.
                    // ⚠ tol у корпусных приборов 10 %, то есть ±146 кэВ на
                    //    калии: одно это число доводом быть не может, оно лишь
                    //    очерчивает, из чего подпись ВЫБИРАЛАСЬ. Рядом считается
                    //    узкая полоса (0.5…1.0 ПШПВ) — линии, которые в пик
                    //    попадают краем и спутать их правдоподобно, а спор про
                    //    них молчит, потому что окно спора ровно 0.5 ПШПВ.
                    int matcherAlts = 0; double matcherFactor = 1.0;
                    int nearMiss = 0; double nearFactor = 1.0;
                    foreach (NuclideDefinition d in defs)
                    {
                        if (d == null || !d.Visible || d.Energy == 0.0) continue;
                        if (set != null && (d.Sets == null || !d.Sets.Contains(set.Id))) continue;
                        if (!(d.Intencity > 0.0)) continue;
                        if (d.Name == nd.Name && Math.Abs(d.Energy - nd.Energy) < 1e-9) continue;
                        if (NuclideDefinition.IsElementXrayName(d.Name)) continue;
                        if (d.Intencity < threshold) continue;

                        double f = nd.Intencity / d.Intencity;
                        if (f < 1.0) f = 1.0 / f;

                        double miss = Math.Abs(d.Energy - peak.Energy);
                        if (miss <= window)
                        {
                            realRivals++;
                            if (f > realFactor) { realFactor = f; best = d; }
                        }
                        else
                        {
                            if (miss <= 2.0 * window)
                            {
                                nearMiss++;
                                if (f > nearFactor) nearFactor = f;
                            }
                            if (Math.Abs((peak.Energy - d.Energy) / d.Energy) < tol / 100.0)
                            {
                                matcherAlts++;
                                if (f > matcherFactor) matcherFactor = f;
                            }
                        }

                        if (miss <= windowOld)
                        {
                            realRivalsOld++;
                            if (f > realFactorOld) realFactorOld = f;
                        }
                        else if (miss <= 2.0 * windowOld)
                        {
                            nearMissOld++;
                            if (f > nearFactorOld) nearFactorOld = f;
                        }
                    }

                    if (realRivals > 0)
                    {
                        withRealRival++;
                        if (realFactor > worst)
                        {
                            worst = realFactor;
                            worstWhere = string.Format("{0} @ {1} кэВ: «{2}» I={3} против «{4}» I={5}",
                                                       name, N(peak.Energy), nd.Name, N(nd.Intencity),
                                                       best.Name, N(best.Intencity));
                        }
                        cases.Add(Tuple.Create(realFactor,
                            string.Format("{0,-22} {1,9} кэВ  «{2}» I={3} -> «{4}» I={5}  {6}×",
                                          Cut(name, 22), N(peak.Energy), nd.Name, N(nd.Intencity),
                                          best.Name, N(best.Intencity),
                                          realFactor.ToString("F2", CultureInfo.InvariantCulture))));
                    }
                    if (rivals == 0) silent++;
                    if (rivals == 0 && matcherAlts > 0) matcherOnly++;
                    if (rivals == 0 && nearMiss > 0)
                    {
                        nearBand++;
                        if (nearFactor > nearBandWorst) nearBandWorst = nearFactor;
                    }
                    // Спор есть, а настоящих соперников нет: предупреждение
                    // висит там, где неверного числа быть не может.
                    if (rivals > 0 && realRivals == 0) onlyRefusable++;
                    // Показанный множитель ЗАВЫШЕН против настоящих соперников.
                    if (rivals > 0 && factor > realFactor * (1.0 + 1e-9))
                    {
                        inflated++;
                        if (realRivals > 0) inflatedReal++;
                        double ratio = factor / realFactor;
                        if (ratio > inflatedWorst)
                        {
                            inflatedWorst = ratio;
                            inflatedWhere = string.Format(CultureInfo.InvariantCulture,
                                "{0} @ {1} кэВ, подпись «{2}»: панель «до ×{3}», по настоящим ×{4}",
                                name, N(peak.Energy), nd.Name,
                                factor.ToString("F2", CultureInfo.InvariantCulture),
                                realFactor.ToString("F2", CultureInfo.InvariantCulture));
                        }
                    }

                    if (rivalsOld == 0) silentOld++; else withRivalOld++;
                    if (realRivalsOld > 0) withRealRivalOld++;
                    if (rivalsOld == 0 && nearMissOld > 0)
                    {
                        nearBandOld++;
                        if (nearFactorOld > nearBandWorstOld) nearBandWorstOld = nearFactorOld;
                    }
                    if (rivalsOld > 0 && realRivalsOld == 0) onlyRefusableOld++;
                    if (rivalsOld > 0 && (double)pFactor.GetValue(anOld, null) > realFactorOld * (1.0 + 1e-9))
                    {
                        inflatedOld++;
                        if (realRivalsOld > 0) inflatedRealOld++;
                    }

                    csv.AppendLine(string.Join(",", name, F(peak.Energy, "F3"), F(fwhmKev, "F3"),
                        nd.Name.Replace(',', ';'), F(nd.Energy, "F3"), F(nd.Intencity, "G6"),
                        "ПОКАЗАНО", rivals.ToString(CultureInfo.InvariantCulture), F(factor, "F3"),
                        realRivals.ToString(CultureInfo.InvariantCulture), F(realFactor, "F3"),
                        best == null ? "" : best.Name.Replace(',', ';'),
                        best == null ? "" : F(best.Intencity, "G6"),
                        matcherAlts.ToString(CultureInfo.InvariantCulture), F(matcherFactor, "F3"),
                        nearMiss.ToString(CultureInfo.InvariantCulture), F(nearFactor, "F3"),
                        F(peak.FWHM, "F3"), rivalsOld.ToString(CultureInfo.InvariantCulture),
                        F((double)pFactor.GetValue(anOld, null), "F3"),
                        realRivalsOld.ToString(CultureInfo.InvariantCulture),
                        F(realFactorOld, "F3")));
                }
            }

            File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));
            Console.WriteLine("  спектров {0} (отказало {1}), пиков {2}", spectra, failed, peaks);
            Console.WriteLine("  до ветки активности доходят {0}; ОТКАЗ рентген {1}, ОТКАЗ выход ниже порога {2},"
                              + " коэффициент не получен {3}; ПОКАЗАНО числом {4}",
                              reach, refusedXray, refusedYield, noCoeff, shown);
            Console.WriteLine();
            Console.WriteLine("  ⛔ БЫЛО -> СТАЛО: то же приложение, тот же список пиков, разница РОВНО В ОКНЕ.");
            Console.WriteLine("     «было» — окно 0.5·Peak.FWHM, подставленное как кэВ (а это КАНАЛЫ, §0);");
            Console.WriteLine("     «стало» — окно 0.5·ПШПВ В КЭВ, тем же выражением, что в панели поиска пиков.");
            Console.WriteLine("     {0,-52} {1,8} {2,8}", "величина", "БЫЛО", "СТАЛО");
            Console.WriteLine("     {0,-52} {1,8} {2,8}", "показано СО СПОРОМ", withRivalOld, shown - silent);
            Console.WriteLine("     {0,-52} {1,8} {2,8}", "показано ГОЛЫМ ЧИСЛОМ", silentOld, silent);
            Console.WriteLine("     {0,-52} {1,8} {2,8}", "  доля голых от дошедших до ветки, %",
                              (100.0 * silentOld / Math.Max(1, reach)).ToString("F1", CultureInfo.InvariantCulture),
                              (100.0 * silent / Math.Max(1, reach)).ToString("F1", CultureInfo.InvariantCulture));
            Console.WriteLine("     {0,-52} {1,8} {2,8}", "с соперником-НУКЛИДОМ (не рентген, не ниже порога)",
                              withRealRivalOld, withRealRival);
            Console.WriteLine("     {0,-52} {1,8} {2,8}", "спор ТОЛЬКО по отказным линиям", onlyRefusableOld, onlyRefusable);
            Console.WriteLine("     {0,-52} {1,8} {2,8}", "множитель на панели ЗАВЫШЕН, всего", inflatedOld, inflated);
            Console.WriteLine("     {0,-52} {1,8} {2,8}", "  из них там, где НАСТОЯЩИЕ соперники ЕСТЬ",
                              inflatedRealOld, inflatedReal);
            Console.WriteLine("     {0,-52} {1,8} {2,8}", "голых с кандидатом в полосе 0.5…1.0 ПШПВ", nearBandOld, nearBand);
            Console.WriteLine("     {0,-52} {1,8} {2,8}", "  худшее отношение выходов в этой полосе, ×",
                              nearBandWorstOld.ToString("F2", CultureInfo.InvariantCulture),
                              nearBandWorst.ToString("F2", CultureInfo.InvariantCulture));
            if (inflatedWhere.Length > 0)
            {
                Console.WriteLine("     худшее завышение множителя: {0} — в {1} раз",
                                  inflatedWhere, inflatedWorst.ToString("F1", CultureInfo.InvariantCulture));
            }
            Console.WriteLine();
            Console.WriteLine("     ⚠ и {0} имеют кандидата в окне САМОГО ПОДБОРА (tol в процентах, у корпусных",
                              matcherOnly);
            Console.WriteLine("       приборов 10 % — это ±146 кэВ на калии; число очерчивает выбор, а не цену)");
            if (worstWhere.Length > 0)
            {
                Console.WriteLine("  худший случай: {0} — {1}×", worstWhere,
                                  worst.ToString("F2", CultureInfo.InvariantCulture));
            }
            cases.Sort((x, y) => y.Item1.CompareTo(x.Item1));
            foreach (var c in cases.Take(top)) Console.WriteLine("    " + c.Item2);
            Console.WriteLine("  -> {0}", Path.GetFullPath(csvPath));
        }

        /// <summary>Чтение спектра корпуса — тем же порядком, что у `S109ActivityProbe`.</summary>
        static ResultData LoadResult(string path)
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }
            ResultData rd = file.ResultDataList.First();
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }
            var pcal = s != null ? s.EnergyCalibration as PolynomialEnergyCalibration : null;
            if (pcal != null) pcal.CheckCalibration(s.NumberOfChannels);

            string note = ProbeDeviceConfig.Attach(rd);
            if (note.IndexOf("НЕТ", StringComparison.Ordinal) >= 0)
            {
                Console.Error.WriteLine("⚠ " + Path.GetFileNameWithoutExtension(path) + ": " + note);
            }
            if (!(rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig))
            {
                throw new InvalidDataException("нет настроек поиска пиков ни в спектре, ни в приборе");
            }
            if (rd.FwhmCalibration == null)
            {
                var cfg = (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig;
                rd.FwhmCalibration = cfg.FwhmCalibration
                    ?? FwhmCalibration.DefaultCalibration(cfg, s.EnergyCalibration);
            }
            return rd;
        }

        // ------------------------------------------------------------------
        //  Сцена: вид БЕЗ окна, поля отражением, ветка настоящая
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ Окно `BecqMoni` не поднимается: `EnergySpectrumView` создаётся
        /// БЕЗ КОНСТРУКТОРА, поля ставятся отражением, и зовётся приватный
        /// `EnsureSelectionAnalytics` — та самая ветка, что рисует панель.
        /// Своя копия ветки мерила бы копию, а не приложение.
        /// </summary>
        class Scene
        {
            static readonly Type TView = typeof(EnergySpectrumView);
            static readonly Type TAn = TView.GetNestedType("SelectionAnalytics", BindingFlags.NonPublic);
            static readonly MethodInfo MEnsure = TView.GetMethod("EnsureSelectionAnalytics",
                BindingFlags.NonPublic | BindingFlags.Instance);

            public EfficiencyConfigData Curve;
            public ResultData Result;
            public double PeakEnergy;
            public double FgTime = 1000.0, BgTime = 2000.0;
            public double FgCounts, BgCounts, NetCounts;
            public int StartChannel, EndChannel;
            public PeakMode PeakMode = PeakMode.Visible;

            EnergySpectrum fg, bg;
            PolynomialEnergyCalibration cal;
            Peak peak;

            public class Answer
            {
                public bool Ok;
                public double Activity, RivalFactor, Lc;
                /// <summary>Ширина выделения, как её посчитало САМО приложение: доля и кэВ.</summary>
                public double SelFwhmRel, SelFwhmKev;
                public int Rivals;
                public string Label, Refusal, Why;
                public double Intensity;

                /// <summary>
                /// Появится ли на панели ХОТЬ ЧТО-ТО про активность. Правило
                /// переписано с самой отрисовки (`EnergySpectrumView.cs`,
                /// `activityLabelShown`): подпись непустая И (число есть при
                /// Lc > 0 ИЛИ есть отказ). Без этого «пусто» в таблице ниже
                /// читалось бы как «проверка сработала», хотя человек видит
                /// ровно то же, что при отсутствующей кривой. Lc вошёл в
                /// правило 05.09.2026: без фона Lc = 0, и отказ «фона нет»
                /// рисуется теперь и там.
                /// </summary>
                public bool Shown;
            }

            /// <summary>Края кривой сцены, кэВ — в ожидаемый текст отказа идут они же.</summary>
            public const double CurveMin = 50.0, CurveMax = 2000.0;

            /// <summary>Сколько кэВ в канале сцены — по умолчанию единица.</summary>
            public double KevPerChannel = 1.0;

            /// <summary>Истинная ПШПВ пика сцены, кэВ (по построению гауссианы).</summary>
            public double TrueFwhmKev;

            public static Scene Standard() { return At(662.0); }

            public static Scene At(double energyKev) { return At(energyKev, 1.0); }

            /// <summary>
            /// ⛔ `kevPerChannel` заведён 05.09.2026 встречной проверкой (`S96`).
            /// Прежняя сцена была ровно 1 кэВ = 1 канал, то есть НА НЕЙ КАНАЛЫ И
            /// КЭВ НЕРАЗЛИЧИМЫ: любой вопрос про единицы ширины она отвечала бы
            /// «сошлось» при обоих ответах. Проверка, у которой нет входа с
            /// РАЗНЫМ ответом, не меряет ничего.
            /// </summary>
            public static Scene At(double energyKev, double kevPerChannel)
            {
                var sc = new Scene();
                sc.PeakEnergy = energyKev;
                sc.KevPerChannel = kevPerChannel;

                sc.cal = new PolynomialEnergyCalibration();
                sc.cal.PolynomialOrder = 1;
                sc.cal.Coefficients = new double[] { 0.0, kevPerChannel };

                int channels = 4096;
                int centre = (int)Math.Round(energyKev / kevPerChannel);
                double sigmaKev = 12.0;
                double sigma = sigmaKev / kevPerChannel;          // сигма В КАНАЛАХ
                sc.TrueFwhmKev = 2.354820045 * sigmaKev;

                int[] fgArr = new int[channels];
                int[] bgArr = new int[channels];
                for (int i = 0; i < channels; i++)
                {
                    double gauss = 40000.0 * Math.Exp(-0.5 * Math.Pow((i - centre) / sigma, 2.0));
                    fgArr[i] = 200 + (int)Math.Round(gauss);
                    bgArr[i] = 300;    // фон вдвое дольше, значит на fgTime придётся 150
                }

                sc.fg = MakeSpectrum(fgArr, sc.cal, sc.FgTime);
                sc.bg = MakeSpectrum(bgArr, sc.cal, sc.BgTime);

                // Выделение ±40 кэВ вокруг пика — в кэВ, а не в каналах, иначе
                // при мелком канале оно ужалось бы внутрь самого пика.
                int half = (int)Math.Round(40.0 / kevPerChannel);
                sc.StartChannel = Math.Max(0, centre - half);
                sc.EndChannel = Math.Min(channels - 1, centre + half);
                for (int i = sc.StartChannel; i <= sc.EndChannel; i++)
                {
                    sc.FgCounts += fgArr[i];
                    sc.BgCounts += bgArr[i];
                }
                sc.NetCounts = sc.FgCounts - sc.BgCounts * sc.FgTime / sc.BgTime;

                sc.Curve = new EfficiencyConfigData("проба");
                sc.Curve.Curve = new List<ROIEfficiencyData>
                {
                    new ROIEfficiencyData { Energy = CurveMin, Efficiency = 2.0e-2, ErrorPercent = 2.0 },
                    new ROIEfficiencyData { Energy = 662.0,    Efficiency = 1.0e-3, ErrorPercent = 5.0 },
                    new ROIEfficiencyData { Energy = CurveMax, Efficiency = 1.0e-4, ErrorPercent = 8.0 },
                };

                sc.peak = new Peak
                {
                    Energy = energyKev,
                    Channel = centre,
                    Count = 40000,
                    FWHM = 2.354820045 * sigma,
                    SNR = 100.0,
                };

                sc.Result = new ResultData
                {
                    EnergySpectrum = sc.fg,
                    BackgroundEnergySpectrum = sc.bg,
                    Visible = true,
                    Efficiency = sc.Curve,
                };
                sc.Result.SampleInfo.Weight = 1.0;
                sc.Result.SampleInfo.Volume = 1.0;
                sc.Result.DetectedPeaks.Add(sc.peak);
                return sc;
            }

            static EnergySpectrum MakeSpectrum(int[] data, EnergyCalibration cal, double time)
            {
                var s = new EnergySpectrum();
                s.NumberOfChannels = data.Length;
                s.Spectrum = data;
                s.EnergyCalibration = cal;
                s.MeasurementTime = time;
                long total = 0;
                for (int i = 0; i < data.Length; i++) total += data[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
                return s;
            }

            public double EpsAt(double energy)
            {
                FsaEfficiency c = FsaEfficiency.FromConfig(this.Curve);
                double eps, err;
                return c != null && c.TryEval(energy, out eps, out err) ? eps : double.NaN;
            }

            public void SetLabel(string name, double lineKev, double intensity)
            {
                this.peak.Nuclide = new NuclideDefinition
                {
                    Name = name,
                    Energy = lineKev,
                    Intencity = intensity,
                    Visible = true,
                    Sets = new HashSet<Guid>(),
                };
            }

            public void ClearLabel() { this.peak.Nuclide = null; }

            /// <summary>Подписанный пик сцены — нужен мерке окна спора.</summary>
            public Peak LabelledPeak { get { return this.peak; } }

            public void DropBackground()
            {
                this.Result.BackgroundEnergySpectrum = null;
                this.bg = null;
            }

            /// <summary>
            /// Кривая остаётся (ссылка непустая), но ни одной годной точки:
            /// нулевую эффективность интерполятор отбрасывает, и значения нет.
            /// </summary>
            public void DropEpsilon()
            {
                foreach (ROIEfficiencyData p in this.Curve.Curve) p.Efficiency = 0.0;
            }

            /// <summary>Второй подписанный пик внутри того же выделения.</summary>
            public void AddSecondPeak()
            {
                this.Result.DetectedPeaks.Add(new Peak
                {
                    Energy = this.PeakEnergy + 10.0,
                    Channel = (int)Math.Round((this.PeakEnergy + 10.0) / this.KevPerChannel),
                    Count = 1000,
                    FWHM = this.peak.FWHM,
                    Nuclide = new NuclideDefinition
                    {
                        Name = "K-40", Energy = this.PeakEnergy + 10.0,
                        Intencity = 10.66, Visible = true, Sets = new HashSet<Guid>(),
                    },
                });
            }

            public Answer Run()
            {
                object view = FormatterServices.GetUninitializedObject(TView);
                Set(view, "energySpectrum", this.fg);
                Set(view, "backgroundEnergySpectrum", this.bg);
                Set(view, "substractedEnergySpectrum", null);
                Set(view, "normByEffEnergySpectrum", null);
                Set(view, "energyCalibration", this.cal);
                Set(view, "baseEnergyCalibration", this.cal);
                Set(view, "backgroundEnergyCalibration", this.cal);
                Set(view, "backgroundNumberOfChannels", this.bg == null ? 0 : this.bg.NumberOfChannels);
                Set(view, "selectionStart", this.StartChannel);
                Set(view, "selectionEnd", this.EndChannel);
                Set(view, "peakMode", this.PeakMode);
                Set(view, "backgroundMode", BackgroundMode.Invisible);
                Set(view, "activeResultData", this.Result);
                Set(view, "globalConfigManager", Config());
                Set(view, "nuclideManager", NuclideDefinitionManager.GetInstance());
                Set(view, "selectionAnalyticsDirty", true);
                Set(view, "selectionAnalytics", null);
                Set(view, "selectionFWHM", 0.0);

                MEnsure.Invoke(view, null);

                object an = TView.GetField("selectionAnalytics",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
                var a = new Answer();
                if (an == null) { a.Why = "аналитика не построена"; return a; }

                a.Activity = (double)Get(an, "Activity");
                a.Label = (string)Get(an, "ActivityLabel");
                a.Refusal = (string)Get(an, "ActivityRefusal");
                a.Rivals = (int)Get(an, "ActivityRivals");
                a.RivalFactor = (double)Get(an, "ActivityRivalFactor");
                a.Intensity = (double)Get(an, "ActivityIntensity");
                a.Lc = (double)Get(an, "Lc");
                a.SelFwhmRel = (double)Get(an, "SelectionFWHM");
                a.SelFwhmKev = (double)Get(an, "SelectionFWHMinkev");
                a.Ok = a.Activity > 0.0;
                a.Shown = !string.IsNullOrEmpty(a.Label)
                          && ((a.Lc > 0.0 && a.Activity > 0.0) || !string.IsNullOrEmpty(a.Refusal));
                if (!a.Ok)
                {
                    double fwhm = (double)Get(an, "SelectionFWHM");
                    a.Why = a.Refusal != null ? "отказ"
                          : fwhm <= 0.0 ? "ПШПВ выделения не измерена"
                          : a.Label == null ? "подписи нет" : "коэффициент не получен";
                }
                return a;
            }

            static GlobalConfigManager Config()
            {
                var m = new GlobalConfigManager();
                var c = new GlobalConfigInfo();
                if (c.ColorConfig != null &&
                    (c.ColorConfig.SpectrumColorList == null || c.ColorConfig.SpectrumColorList.Count == 0))
                {
                    c.ColorConfig.InitializeSpectrumColor();
                }
                m.GlobalConfig = c;
                return m;
            }

            static void Set(object target, string field, object value)
            {
                FieldInfo f = TView.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) throw new InvalidOperationException("нет поля EnergySpectrumView." + field);
                f.SetValue(target, value);
            }

            static object Get(object an, string prop)
            {
                PropertyInfo p = TAn.GetProperty(prop);
                if (p == null) throw new InvalidOperationException("нет свойства SelectionAnalytics." + prop);
                return p.GetValue(an, null);
            }
        }

        // ------------------------------------------------------------------

        static double Fwhm(double energy, double res662)
        {
            return res662 / 100.0 * 662.0 * Math.Sqrt(energy / 662.0);
        }

        static string N(double v)
        {
            return double.IsNaN(v) || double.IsInfinity(v)
                ? "-" : v.ToString("G6", CultureInfo.InvariantCulture);
        }

        static string F(double v, string fmt)
        {
            return double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString(fmt, CultureInfo.InvariantCulture);
        }

        static string Rel(double a, double b)
        {
            if (b == 0.0) return "-";
            return ((a - b) / b).ToString("E2", CultureInfo.InvariantCulture);
        }

        static string Cut(string s, int n)
        {
            if (s == null) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        static void Near(string what, double expected, double got, double tol)
        {
            if (Math.Abs(got - expected) <= tol * Math.Max(1.0, Math.Abs(expected))) return;
            Console.WriteLine("  !! {0}: ждали {1}, получили {2}", what, N(expected), N(got));
            bad++;
        }

        static void Same(string what, object expected, object got)
        {
            if (Equals(expected, got)) return;
            Console.WriteLine("  !! {0}: ждали {1}, получили {2}", what, expected ?? "null", got ?? "null");
            bad++;
        }
    }
}
