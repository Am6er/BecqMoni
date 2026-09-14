using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace XrayLabelProbeF53
{
    /// <summary>
    /// (`A252`, полоса F53) ПРИБОРНЫЙ РЕНТГЕН ПРОТИВ НУКЛИДНОЙ ПОДПИСИ.
    ///
    /// Свинцовый рентген домика (`Pb x-ray` 72.804 и 74.969) неотличим по
    /// положению от гаммы `Am-243` 74.66, и у сцинтиллятора спор между ними
    /// положением не решается: оба стоят в списке кандидатов пика (`S64`).
    /// Побеждал америций — над спектрами калия, тория и цезия оказывался
    /// написан трансурановый нуклид.
    ///
    ///     XrayLabelProbeF53 --spectra=&lt;…\CORPUS\corpus\spectra&gt; [--csv=f53.csv]
    ///     XrayLabelProbeF53 --spectra=… --expect=&lt;спектр&gt;=&lt;подпись&gt;   (контроль)
    ///     XrayLabelProbeF53 --selftest
    ///
    /// ⛔ ПРОБА СЧИТАЕТСЯ НА ОБОИХ ПЛЕЧАХ. Правило спрашивается ОТРАЖЕНИЕМ, а не
    /// прямым вызовом: на сборке ДО правки метода `UnfalsifiableAgainstImage`
    /// нет вовсе, и прямой вызов сделал бы плечо «до» неизмеримым. Без правила
    /// исходы ПЕЧАТАЮТСЯ, но не судятся — иначе плечо «до» отказывало бы за
    /// отсутствие того, чего в нём и не должно быть.
    ///
    /// ⚠ ИМЕНА СПЕКТРОВ И ОЖИДАЕМЫЕ ПОДПИСИ ВЫПИСАНЫ ЗДЕСЬ ДОСЛОВНО, и это
    /// нарочно: таблица ниже — ПРИЁМКА строки `A252`, то есть поимённый список
    /// из самой строки реестра плюс её положительный контроль. Общая мерка
    /// подписи живёт не здесь, а в `LabelTruthProbe` + `label_score.py`, и
    /// именами она не пользуется вовсе. Спроси проба ожидание у приложения —
    /// она печатала бы «сошлось» при любом исходе.
    /// </summary>
    static class Program
    {
        /// <summary>
        /// ⛔ ТОЛЬКО ДЛЯ ПОЛОЖИТЕЛЬНОГО КОНТРОЛЯ. Подменяет ОЖИДАНИЕ одной
        /// строки приёмки, а не то, что считает приложение: прогон с заведомо
        /// неверным образцом обязан ОТКАЗАТЬ и назвать место. Без ключа
        /// ожидание берётся из таблицы и сверяется всегда.
        /// </summary>
        static string expectSpectrum, expectLabel;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            string spectraDir = null;
            string csvPath = "f53.csv";
            bool selftestOnly = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectraDir = a.Substring(10);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csvPath = a.Substring(6);
                else if (a == "--selftest") selftestOnly = true;
                else if (a.StartsWith("--expect=", StringComparison.Ordinal))
                {
                    string v = a.Substring(9);
                    int eq = v.IndexOf('=');
                    if (eq <= 0)
                    {
                        Console.Error.WriteLine("--expect= ждёт «спектр=подпись»");
                        return 2;
                    }
                    expectSpectrum = v.Substring(0, eq);
                    expectLabel = v.Substring(eq + 1);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            bool hasRule = RuleMethod() != null;
            Console.WriteLine("правило `A252` в сборке: {0}",
                              hasRule ? "есть (UnfalsifiableAgainstImage)" : "НЕТ — исходы не судятся");
            Console.WriteLine();

            int bad = SelfTest(hasRule);
            if (selftestOnly)
            {
                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ОПЫТЫ СОШЛИСЬ" : "ОПЫТОВ РАЗОШЛОСЬ: " + bad);
                return bad == 0 ? 0 : 1;
            }

            if (spectraDir == null || !Directory.Exists(spectraDir))
            {
                Console.Error.WriteLine("нет каталога спектров: {0}", spectraDir ?? "(не задан)");
                return 2;
            }

            bad += Corpus(spectraDir, csvPath, hasRule);
            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "РАСХОЖДЕНИЙ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static MethodInfo RuleMethod()
        {
            return typeof(PeakDetector).GetMethod("UnfalsifiableAgainstImage",
                                                  BindingFlags.NonPublic | BindingFlags.Instance);
        }

        // ------------------------------------------------------------------
        // ПРИЁМКА `A252` — поимённо
        // ------------------------------------------------------------------

        /// <summary>
        /// Строка приёмки: спектр, пик (кэВ) и подпись, которая обязана над ним
        /// стоять ПОСЛЕ правки. Пик ищется по ближайшему — окно ±1 кэВ, чтобы
        /// строка ловила именно тот пик, а не соседний.
        /// </summary>
        class Row
        {
            public string Spectrum;
            public double PeakKev;
            public string Expect;
            public string Why;
        }

        static readonly Row[] Acceptance =
        {
            // Семь ложных подписей, поимённо из строки `A252`: америция нет ни
            // в одной пробе (калий, торий, цезий, натрий), пик 73…75 кэВ — это
            // свинцовый рентген домика.
            R("G1S16_Na22_P25",       74.560, "(нет)", "ложная `A252` (натрий)"),
            R("G1S16_Th232_Mar_3",    74.381, "(нет)", "ложная `A252` (торий)"),
            R("G1S24_Cs137_Petri_2",  74.010, "(нет)", "ложная `A252` (цезий)"),
            R("G1S24_K40_Denta120",   73.902, "(нет)", "ложная `A252` (калий)"),
            R("G1S24_K40_Denta120_2", 74.812, "(нет)", "ложная `A252` (калий)"),
            R("G1S24_K40_Mar_2",      73.910, "(нет)", "ложная `A252` (калий)"),
            R("G1S24_Th232_Denta120", 74.656, "(нет)", "ложная `A252` (торий)"),
            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ строки `A252`: тот же род (нуклид против
            //    приборного рентгена, спор не решён положением), но америций
            //    ЕСТЬ — подпись обязана уцелеть.
            R("G1S16_Am241_P25",      60.691, "Am-241", "⛔ контроль: америций ЕСТЬ"),
            // Второй такой же контроль, найден полосой: тот же спор с
            // вольфрамом, америций объявлен в сцене.
            R("ASN16_Am241",          59.491, "Am-241", "⛔ контроль: америций ЕСТЬ"),
            // Тот же род, что и семь названных, — найдено полосой F53 сплошным
            // проходом по корпусу. В строке `A252` их нет, потому что F49
            // мерила спор в УЗКОМ окне активности, а не в окне подписи.
            R("ASN16_Granite",        74.735, "(нет)", "тот же род, найдено F53"),
            R("ASN8_Th232_3000",      73.856, "(нет)", "тот же род, найдено F53"),
            R("ASN8_Th232_4096",      74.035, "(нет)", "тот же род, найдено F53"),
            R("ASN8_Th232_8192",      73.916, "(нет)", "тот же род, найдено F53"),
            R("GS4000_Th232",         74.095, "(нет)", "тот же род, найдено F53"),
            R("LaBrBril_Th228",       74.675, "(нет)", "тот же род, найдено F53"),
            // ⚠ ОСТАТОК, И ОН НАЗВАН: у германия ПШПВ 0.8 кэВ, спор решается
            //   ПОЛОЖЕНИЕМ (рентген выпадает из списка кандидатов), и правило
            //   `A252` до этих двух подписей не достаёт по построению. Германий
            //   вне работы по решению Amber; строка стоит здесь, чтобы молчаливое
            //   изменение их исхода было видно.
            R("HPGeGEM_Ra226",        74.756, "Am-243", "⚠ остаток: германий, спор решён положением"),
            R("HPGeGEM_Th232",        74.711, "Am-243", "⚠ остаток: германий, спор решён положением")
        };

        static Row R(string spectrum, double kev, string expect, string why)
        {
            return new Row { Spectrum = spectrum, PeakKev = kev, Expect = expect, Why = why };
        }

        // ------------------------------------------------------------------
        // ПРОГОН ПО КОРПУСУ
        // ------------------------------------------------------------------

        static int Corpus(string spectraDir, string csvPath, bool hasRule)
        {
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            NuclideSet set = nuclides.ActiveSet;
            List<NuclideDefinition> lib = nuclides.NuclideDefinitions;

            Console.WriteLine("библиотека: {0} записей, набор «{1}»",
                              lib != null ? lib.Count : -1, set != null ? set.Name : "(нет)");

            // Окно подписи читается ИЗ СБОРКИ (`GetRawConstantValue`), а не
            // переписывается сюда: копия константы мерила бы старое число.
            double window = Gate("MaximumLabelMissInFwhm") ?? 1.5;

            var csv = new StringBuilder();
            csv.AppendLine("spectrum,peak_kev,fwhm_kev,nuclide,line_kev,intensity_pct,"
                           + "one_line_nuclide,image_within_window,image_name,image_kev");

            var got = new Dictionary<string, List<Tuple<double, string>>>(StringComparer.Ordinal);
            int spectra = 0, failed = 0, peaks = 0, labelled = 0, suspect = 0;

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
                                                          SmoothingMethod.None, set, lib);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("{0}: {1}", name, e.Message);
                    failed++;
                    continue;
                }
                spectra++;
                if (found == null) found = new List<Peak>();
                EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
                var mine = new List<Tuple<double, string>>();
                got[name] = mine;

                foreach (Peak p in found.OrderBy(x => x.Energy))
                {
                    peaks++;
                    NuclideDefinition nd = p.Nuclide;
                    mine.Add(Tuple.Create(p.Energy, nd == null ? "(нет)" : nd.Name));
                    if (nd == null) continue;
                    labelled++;

                    double fwhmKev = p.FwhmKev(cal);
                    bool oneLine = !nd.IsElementXray && !OtherOwnLine(lib, nd);
                    NuclideDefinition image = NearestImage(lib, p.Energy, fwhmKev * window);
                    if (oneLine && image != null) suspect++;

                    csv.AppendLine(string.Join(",",
                        name, F(p.Energy), F(fwhmKev), nd.Name.Replace(',', ';'), F(nd.Energy),
                        F(nd.Intencity), oneLine ? "1" : "0", image != null ? "1" : "0",
                        image == null ? "" : image.Name.Replace(',', ';'),
                        image == null ? "" : F(image.Energy)));
                }
            }

            File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));
            Console.WriteLine("спектров разобрано {0}, отказало {1}; пиков {2}, с подписью {3} -> {4}",
                              spectra, failed, peaks, labelled, csvPath);
            Console.WriteLine();
            // ⛔ ЗАГЛАВНОЕ ЧИСЛО ПРАВКИ. Подпись ОДНОЛИНЕЙНЫМ нуклидом там, где
            //    в то же окно подписи попадает приборный образ, — ровно тот
            //    случай, ради которого `A252` заведена: подтвердить такую
            //    подпись нечем и никогда не будет чем.
            Console.WriteLine("подписей однолинейным нуклидом под приборным образом (окно {0} ПШПВ): {1}",
                              window.ToString("G6", CultureInfo.InvariantCulture), suspect);
            Console.WriteLine();

            Console.WriteLine("ПРИЁМКА `A252` ПОИМЁННО:");
            int bad = 0;
            foreach (Row r in Acceptance)
            {
                string want = expectSpectrum != null
                              && string.Equals(expectSpectrum, r.Spectrum, StringComparison.Ordinal)
                    ? expectLabel : r.Expect;
                List<Tuple<double, string>> list;
                string label;
                double at;
                if (!got.TryGetValue(r.Spectrum, out list))
                {
                    label = "(спектра нет)";
                    at = Double.NaN;
                }
                else
                {
                    Tuple<double, string> near = list
                        .Where(t => Math.Abs(t.Item1 - r.PeakKev) <= 1.0)
                        .OrderBy(t => Math.Abs(t.Item1 - r.PeakKev))
                        .FirstOrDefault();
                    label = near == null ? "(пика нет)" : near.Item2;
                    at = near == null ? Double.NaN : near.Item1;
                }
                bool ok = string.Equals(label, want, StringComparison.Ordinal);
                if (!ok && hasRule) bad++;
                Console.WriteLine("  {0,-22} @{1,8}  ждали «{2}» получили «{3}» {4}   {5}",
                                  r.Spectrum, F(r.PeakKev), want, label,
                                  !hasRule ? "(сборка без правила `A252`)"
                                           : (ok ? "✓" : "⛔ РАСХОЖДЕНИЕ"),
                                  r.Why);
            }
            return bad;
        }

        /// <summary>Есть ли у нуклида ЕЩЁ ОДНА пригодная линия в библиотеке.</summary>
        static bool OtherOwnLine(List<NuclideDefinition> lib, NuclideDefinition nuclide)
        {
            double floor = Gate("MinimumLabelYieldPercent") ?? 0.1;
            foreach (NuclideDefinition other in lib)
            {
                if (other == null || !other.Visible || other.Energy == 0.0) continue;
                if (!string.Equals(other.Name, nuclide.Name, StringComparison.Ordinal)) continue;
                if (Math.Abs(other.Energy - nuclide.Energy) < 1e-9) continue;
                if (other.Intencity > 0.0 && other.Intencity < floor) continue;
                return true;
            }
            return false;
        }

        /// <summary>Ближайшая запись ПРИБОРНОГО ОБРАЗА в окне вокруг пика.</summary>
        static NuclideDefinition NearestImage(List<NuclideDefinition> lib, double peakKev, double window)
        {
            if (!(window > 0.0)) return null;
            NuclideDefinition best = null;
            double bestMiss = Double.MaxValue;
            foreach (NuclideDefinition d in lib)
            {
                if (d == null || !d.Visible || d.Energy == 0.0) continue;
                if (!d.IsElementXray) continue;
                double miss = Math.Abs(peakKev - d.Energy);
                if (miss > window || miss >= bestMiss) continue;
                best = d;
                bestMiss = miss;
            }
            return best;
        }

        // ------------------------------------------------------------------
        // ОПЫТ НАД ПРАВИЛОМ — подставная библиотека, прямой вызов ConfirmLabels
        // ------------------------------------------------------------------

        /// <summary>
        /// Прогон по корпусу отвечает «сколько», опыт — «ПОЧЕМУ»: он держит оба
        /// конца правила. Ложный вход обязан быть отвергнут ИМЕННО тем условием,
        /// которым должен, а честный — принят; правило, снимающее подпись у
        /// всякого соседа рентгена, отличается от полезного только этой парой.
        ///
        /// ⛔ Имена подставных линий несут МАССОВОЕ ЧИСЛО там, где линия должна
        /// быть нуклидной, и НЕ несут — там, где приборным образом:
        /// `NuclideDefinition.IsElementXrayName` зовёт рентгеном любое имя без
        /// цифры в первой лексеме, и «Америций-мнимый» молча стал бы рентгеном
        /// (эта грабля стоила полосе F49 трёх отказавших опытов разом).
        /// </summary>
        static int SelfTest(bool hasRule)
        {
            FieldInfo fDefs = typeof(PeakDetector).GetField(
                "nuclideDefinitions", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo mConfirm = typeof(PeakDetector).GetMethod(
                "ConfirmLabels", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fDefs == null || mConfirm == null)
            {
                Console.Error.WriteLine("⛔ в сборке нет ConfirmLabels/nuclideDefinitions — опыт не ставится");
                return 1;
            }

            var cases = new[]
            {
                // ⛔ ГЛАВНЫЙ СЛУЧАЙ `A252`. Линия ЯРКАЯ (67.2 %) и стоит В ПИКЕ
                //    (промах 0.76 кэВ при ПШПВ 10.7 — это 0.07 ПШПВ, много
                //    меньше и окна 0.20, и пола 1.5 кэВ), то есть ни `A197`, ни
                //    `A227` её не берут. Берёт только `A252`: второй своей
                //    линии у имени нет ВООБЩЕ, а тот же пик объясняет рентген.
                C("однолинейный нуклид под рентгеном", true,
                  new[] { Line("Ам-243", 74.66, 67.2), Line("Пб", 74.969, 100.0), Line("Пб", 72.804, 59.5) },
                  new[] { P(73.902, 10.713, 30, "Ам-243", 74.66) },
                  new[] { "(нет)" }),
                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. Тот же спор, но у имени ЕСТЬ вторая
                //    линия в библиотеке — подпись остаётся, и остаётся ДАЖЕ
                //    тогда, когда второй линии в спектре не видно и она лежит
                //    НИЖЕ полосы поиска (20 кэВ): «улику не смотрели» —
                //    ограничение измерения, а не свойство утверждения.
                C("у нуклида есть вторая линия (вне полосы)", true,
                  new[] { Line("Ам-241", 59.541, 35.9), Line("Ам-241", 15.345, 2.27),
                          Line("Вэ", 59.318, 100.0), Line("Вэ", 57.981, 57.6) },
                  new[] { P(60.691, 10.540, 140, "Ам-241", 59.541) },
                  new[] { "Ам-241@59.541" }),
                // ⛔ ВТОРОЙ КОНТРОЛЬ. Однолинейный нуклид БЕЗ рентгена рядом не
                //    судится — это доктрина `A197` (`Cs-137` 661.7, `K-40`
                //    1460.8), и `A252` её не отменяет.
                C("однолинейный нуклид, рентгена рядом НЕТ", true,
                  new[] { Line("Цэ-137", 661.7, 85.1) },
                  new[] { P(661.7, 40.0, 300, "Цэ-137", 661.7) },
                  new[] { "Цэ-137@661.7" }),
                // ⛔ ТРЕТИЙ КОНТРОЛЬ. Спор РЕШЁН положением (германий): рентген
                //    выпадает из списка кандидатов, и правило до подписи не
                //    достаёт. Тот же вход, что и в первом опыте, но ПШПВ 0.8.
                C("спор решён положением (узкий пик)", true,
                  new[] { Line("Ам-243", 74.66, 67.2), Line("Пб", 74.969, 100.0), Line("Пб", 72.804, 59.5) },
                  new[] { P(74.756, 0.844, 30, "Ам-243", 74.66) },
                  new[] { "Ам-243@74.66" }),
                // ⛔ ЧЕТВЁРТЫЙ КОНТРОЛЬ. Сам приборный образ этим правилом не
                //    судится: он и есть тот соперник, ради которого оно писано.
                C("подпись — сам приборный образ", true,
                  new[] { Line("Пб", 87.3, 8.0), Line("Вэ", 87.5, 22.0) },
                  new[] { P(87.35, 6.9, 30, "Пб", 87.3) },
                  new[] { "Пб@87.3" })
            };

            Console.WriteLine("ОПЫТ НАД ПРАВИЛОМ `A252` (подставная библиотека, ConfirmLabels):");
            int bad = 0;
            foreach (Case c in cases)
            {
                var det = new PeakDetector();
                fDefs.SetValue(det, c.Library);
                // ⛔ ПОДПИСЬ ПИКА — ЗАПИСЬ ЭТОЙ ЖЕ БИБЛИОТЕКИ, а не её двойник:
                //    правило судит по выходу и по имени, и двойник проскочил бы
                //    мимо, ничего не измерив.
                foreach (Peak p in c.Peaks)
                {
                    if (p.Nuclide == null) continue;
                    NuclideDefinition real = c.Library.FirstOrDefault(
                        d => d.Name == p.Nuclide.Name && Math.Abs(d.Energy - p.Nuclide.Energy) < 1e-9);
                    if (real == null)
                    {
                        Console.Error.WriteLine("⛔ опыт «{0}»: подписи нет в подставной библиотеке", c.Title);
                        return 1;
                    }
                    p.Nuclide = real;
                    // Список кандидатов ставится ТЕМ ЖЕ отбором, что в
                    // приложении (`MatchNuclides`), а не выписывается руками:
                    // иначе опыт мерил бы мою выписку, а не правило.
                    SetCandidates(det, p, c.Library);
                }
                var spectrum = new EnergySpectrum(1.0, 4096);
                spectrum.EnergyCalibration = new PolynomialEnergyCalibration();
                var cfg = new FWHMPeakDetectionMethodConfig();
                cfg.Min_Range = 20.0;
                cfg.Max_Range = 3000.0;
                mConfirm.Invoke(det, new object[] { c.Peaks, spectrum, cfg, null });

                string[] res = c.Peaks.Select(p => p.Nuclide == null
                        ? "(нет)"
                        : p.Nuclide.Name + "@" + p.Nuclide.Energy.ToString("G6", CultureInfo.InvariantCulture))
                    .ToArray();
                bool ok = res.Length == c.Expect.Length;
                for (int i = 0; ok && i < res.Length; i++) ok = res[i] == c.Expect[i];
                bool judged = hasRule || !c.NeedsRule;
                if (!ok && judged) bad++;
                Console.WriteLine("  {0,-42} ждали [{1}] получили [{2}] {3}",
                                  c.Title, string.Join(" ", c.Expect), string.Join(" ", res),
                                  !judged ? "(сборка без правила `A252`)"
                                          : (ok ? "✓" : "⛔ РАСХОЖДЕНИЕ"));
            }
            return bad;
        }

        static void SetCandidates(PeakDetector det, Peak peak, List<NuclideDefinition> lib)
        {
            MethodInfo mMatch = typeof(PeakDetector).GetMethod(
                "MatchNuclides", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo mSet = typeof(Peak).GetMethod(
                "SetNuclideCandidates", BindingFlags.Public | BindingFlags.Instance);
            if (mMatch == null || mSet == null) return;
            double fwhmKev = peak.FWHM;
            object list = mMatch.Invoke(det, new object[] { peak, 10.0, null, fwhmKev });
            mSet.Invoke(peak, new object[] { list });
        }

        class Case
        {
            public string Title;
            public bool NeedsRule;
            public List<NuclideDefinition> Library;
            public List<Peak> Peaks;
            public string[] Expect;
        }

        static Case C(string title, bool needsRule, NuclideDefinition[] lib, Peak[] peaks, string[] expect)
        {
            return new Case
            {
                Title = title,
                NeedsRule = needsRule,
                Library = lib.ToList(),
                Peaks = peaks.ToList(),
                Expect = expect
            };
        }

        /// <summary>
        /// Подставной пик. Калибровка опыта тождественная, поэтому канал равен
        /// энергии, а ПШПВ в каналах — ПШПВ в кэВ.
        /// </summary>
        static Peak P(double kev, double fwhm, double snr, string label, double lineKev)
        {
            return new Peak
            {
                Energy = kev,
                Channel = (int)Math.Round(kev),
                FWHM = fwhm,
                SNR = snr,
                Nuclide = label == null ? null : Line(label, lineKev, 0.0)
            };
        }

        static NuclideDefinition Line(string name, double kev, double intensity)
        {
            return new NuclideDefinition
            {
                Name = name,
                Energy = kev,
                Intencity = intensity,
                Visible = true
            };
        }

        // ------------------------------------------------------------------
        // Мелочи
        // ------------------------------------------------------------------

        static double? Gate(string field)
        {
            FieldInfo f = typeof(PeakDetector).GetField(field, BindingFlags.Public | BindingFlags.Static);
            if (f == null) return null;
            return Convert.ToDouble(f.GetRawConstantValue(), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// ⛔ Число печатается ЯВНОЙ инвариантной культурой, разделитель —
        /// точка, группировки разрядов нет (правило Amber 05.09.2026).
        /// </summary>
        static string F(double v)
        {
            return Double.IsNaN(v) ? "" : v.ToString("F3", CultureInfo.InvariantCulture);
        }

        static ResultData LoadResult(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
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

            ProbeDeviceConfig.Attach(rd);
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
    }
}
