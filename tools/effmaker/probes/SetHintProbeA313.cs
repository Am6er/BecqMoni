// Проба AMBER313 (05.11.2026): подсказка подходящего сета у списка «--- All Nuclides ---». Журнал —
// handover/handover-2026-11-05-fixsets.md.
//
// Amber вопросником, дословно: «Подсказка в поиске пиков, где ComboBox -- All Nuclides -- Появляется
// сразу по итогу решения - нужен этот сет. Текст: "Мне кажется, тебе подойдет ....... сет"», затем:
// «Нажатием выбирает сет (Рекомендую) и должна быть сбоку кнопка - закрыть.»
//
// Два режима.
//
// --table: все спектры --corpus (136), библиотека --lib (поставка), объявленный родитель из
// manifest.csv. Поиск пиков — на «всех нуклидах», в двух положениях фона (без вычитания и с
// вычитанием), функция NuclideSetAdvisor.Suggest. Таблица: верна / молчит / неверна, по сетам и по
// выборкам подбора. Спектр смеси считается верным, если предложен сет ЛЮБОГО объявленного родителя;
// родитель без сета в библиотеке (K-40 один, Y-88 и другие): верно только молчание. Имена нуклидов
// здесь законны: это таблица истины пробы, а не код приложения.
//
//   Выборка A: G1S16, ASN16, GS4000, ASN8_*, HPGE_GEM, AS1PRO. Выборка B: все прочие группы.
//   Деление по приборам заморожено ДО подбора. Выводы: --tsv= (балл каждого спектра).
//
// --panel: настоящая панель на настоящей MainForm(args), на скрытом столе: подсказка появляется,
// нажатие выбирает сет тем же путём, что рука, закрытие держится за документом, молчит при
// выбранном сете, без пиков и при неуверенном различии; файл библиотеки не меняется. Снимки
// --shots= (RU/EN — ключ --lang=).
//
// --folder=: спектры вне корпуса, только чтение, два режима фона; истина из имени файла.
//
// --thin=: вложенное прореживание корпуса (живой набор, увиденный из конца в начало), зёрен --seeds=:
// сколько смен «показ/молчание» по росту набора с NuclideSetHintTracker и без него, и что показано.
//
// Коды: 0 — чисто; 4 — есть неверные подсказки или дефекты; 5 — обвязка.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using BecquerelMonitor;

static class SetHintProbeA313
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static int defects;

    [System.Runtime.InteropServices.DllImport("kernel32")] static extern IntPtr GetCurrentProcess();
    [System.Runtime.InteropServices.DllImport("kernel32")] static extern bool TerminateProcess(IntPtr h, uint code);

    static readonly HashSet<string> FoldA = new HashSet<string>
    {
        "G1S16", "ASN16", "GS4000", "ASN8_1024", "ASN8_2048", "ASN8_3000", "ASN8_4096", "ASN8_8192", "HPGE_GEM", "AS1PRO",
    };

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = Inv;
        try
        {
            if (args.Contains("--panel"))
            {
                Panel(args);
                Console.WriteLine("дефектов: {0}", defects);
                Exit(defects == 0 ? 0 : 4);
                return 0;
            }

            if (Key(args, "--folder=") != null)
            {
                return Folder(args);
            }

            if (Key(args, "--thin=") != null)
            {
                return Thin(args);
            }

            return Table(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine("⛔ обвязка: " + ex);
            Exit(5);
            return 5;
        }
    }

    // ---------------------------------------------------------------------
    // Таблица на корпусе
    // ---------------------------------------------------------------------

    sealed class Row
    {
        public string Key, Det, Mode, Truth, Hint, BestAny, Verdict, Fold;
        public int Peaks;
    }

    static int Table(string[] args)
    {
        string corpus = Key(args, "--corpus=");
        string lib = Key(args, "--lib=");
        string tsv = Key(args, "--tsv=");
        NuclideDefinitionFile file;
        using (var s = File.OpenRead(lib))
        {
            file = (NuclideDefinitionFile)new XmlSerializer(typeof(NuclideDefinitionFile)).Deserialize(s);
        }

        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        var serializer = new XmlSerializer(typeof(ResultDataFile));
        var rows = new List<Row>();
        Stopwatch cost = new Stopwatch();
        long calls = 0;
        int nMax = 0;
        foreach (string[] m in Manifest(Path.Combine(corpus, "manifest.csv")))
        {
            string key = m[0], det = m[1], chains = m[2], nuclides = m[3];
            string path = Path.Combine(corpus, "spectra", key + ".xml");
            ResultDataFile f;
            using (var s = File.OpenRead(path))
            {
                f = (ResultDataFile)serializer.Deserialize(s);
            }

            ResultData rd = f.ResultDataList[0];
            FillTotal(rd);
            string device = ProbeDeviceConfig.Attach(rd);
            if (device.Contains("НЕТ") || device.Contains("нет"))
            {
                Console.WriteLine("⚠ {0}: {1}", key, device);
            }

            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }
                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            HashSet<string> truth = Truth(chains, nuclides);
            truth.IntersectWith(file.NuclideSets.Select(x => x.Name));
            foreach (BackgroundMode mode in new[] { BackgroundMode.Invisible, BackgroundMode.Substract })
            {
                if (mode == BackgroundMode.Substract && rd.BackgroundEnergySpectrum == null)
                {
                    continue;
                }

                List<Peak> peaks = new PeakDetector().DetectPeak(rd, mode, SmoothingMethod.None, null, file.NuclideDefinitions);
                cost.Start();
                NuclideSetSuggestion hint = NuclideSetAdvisor.Suggest(peaks, rd.EnergySpectrum.EnergyCalibration, file.NuclideSets, file.NuclideDefinitions);
                cost.Stop();
                calls++;
                nMax = Math.Max(nMax, peaks.Count);
                string name = hint != null ? hint.Set.Name : "";
                rows.Add(new Row
                {
                    Key = key, Det = det, Mode = mode.ToString(), Fold = FoldA.Contains(det) ? "A" : "B",
                    Truth = truth.Count == 0 ? "(нет сета)" : string.Join("+", truth.OrderBy(x => x, StringComparer.Ordinal)),
                    Hint = name, Peaks = peaks.Count,
                    BestAny = hint != null ? hint.Score.ToString("F3", Inv) + "/" + hint.RunnerUp.ToString("F3", Inv) + "/" + hint.PeakShare.ToString("F3", Inv) + "/" + hint.LineShare.ToString("F3", Inv) : "",
                    Verdict = hint == null ? "молчит" : truth.Contains(name) ? "верна" : "неверна",
                });
            }
        }

        Console.WriteLine("сетов в библиотеке {0}, линий {1}; пороги: балл ≥ {2}, отрыв ≥ {3}, окно {4} FWHM, один пик — от SNR {5}, ниже {6} кэВ — от SNR {7}",
                          file.NuclideSets.Count, file.NuclideDefinitions.Count,
                          NuclideSetAdvisor.MinimumScore.ToString(Inv), NuclideSetAdvisor.MinimumGap.ToString(Inv), NuclideSetAdvisor.LineWindowInFwhm.ToString(Inv),
                          NuclideSetAdvisor.MinimumSinglePeakSnr.ToString(Inv), NuclideSetAdvisor.LowEnergyKev.ToString(Inv), NuclideSetAdvisor.MinimumLowPeakSnr.ToString(Inv));
        Console.WriteLine("цена: {0} вызовов, среднее {1} мкс, самый длинный список пиков {2}", calls,
                          (cost.Elapsed.TotalMilliseconds * 1000.0 / Math.Max(1, calls)).ToString("F1", Inv), nMax);
        Console.WriteLine("спектров в манифесте: {0}; выборка A {1}, выборка B {2}", rows.Select(r => r.Key).Distinct().Count(),
                          rows.Where(r => r.Fold == "A").Select(r => r.Key).Distinct().Count(), rows.Where(r => r.Fold == "B").Select(r => r.Key).Distinct().Count());
        Console.WriteLine();
        foreach (string mode in new[] { "Invisible", "Substract" })
        {
            foreach (string fold in new[] { "A", "B", "A+B" })
            {
                List<Row> part = rows.Where(r => r.Mode == mode && (fold == "A+B" || r.Fold == fold)).ToList();
                Console.WriteLine("{0}, выборка {1}: всего {2}, верна {3}, молчит {4}, неверна {5}", mode, fold, part.Count, Count(part, "верна"), Count(part, "молчит"), Count(part, "неверна"));
            }
        }

        foreach (string mode in new[] { "Invisible", "Substract" })
        {
            Console.WriteLine();
            Console.WriteLine("=== по объявленному родителю, {0} (все 136)", mode);
            Console.WriteLine("родитель\tвсего\tверна\tмолчит\tневерна");
            foreach (var g in rows.Where(r => r.Mode == mode).GroupBy(r => r.Truth).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Console.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}", g.Key, g.Count(), Count(g, "верна"), Count(g, "молчит"), Count(g, "неверна"));
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== неверные подсказки");
        foreach (Row r in rows.Where(x => x.Verdict == "неверна"))
        {
            Console.WriteLine("{0}\t{1}\tродитель {2}\tподсказка {3}\t{4}", r.Key, r.Mode, r.Truth, r.Hint, r.BestAny);
        }

        Console.WriteLine();
        Console.WriteLine("=== молчит, хотя сет есть");
        foreach (Row r in rows.Where(x => x.Verdict == "молчит" && x.Truth != "(нет сета)"))
        {
            Console.WriteLine("{0}\t{1}\tродитель {2}\tпиков {3}", r.Key, r.Mode, r.Truth, r.Peaks);
        }

        if (tsv != null)
        {
            File.WriteAllLines(tsv, new[] { "key\tdet\tfold\tmode\ttruth\tpeaks\tverdict\thint\tscore/runner/peakShare/lineShare" }
                .Concat(rows.Select(r => string.Join("\t", r.Key, r.Det, r.Fold, r.Mode, r.Truth, r.Peaks.ToString(Inv), r.Verdict, r.Hint, r.BestAny))), new UTF8Encoding(false));
        }

        int wrong = rows.Count(r => r.Verdict == "неверна");
        // Чароит AS80: объявлен Ra-226, в камне есть и торий; известен с заморозки правила, журнал полосы.
        int unknownWrong = rows.Count(r => r.Verdict == "неверна" && r.Key != "AS80_Charoite");
        Console.WriteLine();
        Console.WriteLine("итог: неверных {0} из {1} прогонов ({2} показанных), вне известного {3}", wrong, rows.Count, rows.Count(r => r.Verdict != "молчит"), unknownWrong);
        Exit(unknownWrong == 0 ? 0 : 4);
        return 0;
    }

    static void Exit(int code)
    {
        // MainForm держит таймеры и потоки, а окна её не показаны: Environment.Exit падает на
        // обратном вызове окна (0xC000041D) и теряет код возврата. Снимаем себя сами.
        Console.Out.Flush();
        TerminateProcess(GetCurrentProcess(), (uint)code);
    }

    /// <summary>
    /// --folder=: спектры вне корпуса (только чтение), рядом — копия конфига приборов владельца.
    /// Истина — из имени файла регулярками (<see cref="Label"/>), имя без указания остаётся без истины.
    /// Половины C1/C2 — по чётности MD5 пути. --skip= — manifest.csv: файлы, названные в нём, пропускаются.
    /// Имён файлов проба в вывод не печатает: они только в --tsv=.
    /// </summary>
    static int Folder(string[] args)
    {
        string root = Key(args, "--folder=");
        string lib = Key(args, "--lib=");
        string skip = Key(args, "--skip=");
        string tsv = Key(args, "--tsv=");
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (skip != null)
        {
            foreach (string line in File.ReadAllLines(skip, Encoding.UTF8))
            {
                foreach (string cell in line.Split(','))
                {
                    if (cell.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        known.Add(Path.GetFileName(cell.Trim('"')));
                    }
                }
            }
        }

        NuclideDefinitionFile file;
        using (var s = File.OpenRead(lib))
        {
            file = (NuclideDefinitionFile)new XmlSerializer(typeof(NuclideDefinitionFile)).Deserialize(s);
        }

        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        var serializer = new XmlSerializer(typeof(ResultDataFile));
        var lines = new List<string> { "file\tdevice\tpeaks\thint\tscore/runner/peakShare/lineShare\tpart\ttruth\tmode" };
        var tally = new Dictionary<string, int>();
        int read = 0, skipped = 0, foreign = 0, shown = 0;
        foreach (string path in Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            if (known.Contains(Path.GetFileName(path)))
            {
                skipped++;
                continue;
            }

            try
            {
                ResultDataFile f;
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    f = (ResultDataFile)serializer.Deserialize(s);
                }

                ResultData rd = f.ResultDataList[0];
                FillTotal(rd);
                string device = ProbeDeviceConfig.Attach(rd);
                if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
                {
                    if (cfg.FwhmCalibration == null)
                    {
                        cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                    }
                    if (cfg.FwhmCalibration != null)
                    {
                        rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                    }
                }

                read++;
                string rel = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
                HashSet<string> named = Label(rel);
                bool csOnly = named != null && named.Count == 1 && named.Contains("Cs-137 + K-40");
                HashSet<string> truth = named;
                if (truth != null)
                {
                    truth = new HashSet<string>(truth);
                    truth.IntersectWith(file.NuclideSets.Select(x => x.Name));
                }

                string part = Half(rel);
                foreach (BackgroundMode mode in new[] { BackgroundMode.Invisible, BackgroundMode.Substract })
                {
                    if (mode == BackgroundMode.Substract && rd.BackgroundEnergySpectrum == null)
                    {
                        continue;
                    }

                    List<Peak> peaks = new PeakDetector().DetectPeak(rd, mode, SmoothingMethod.None, null, file.NuclideDefinitions);
                    NuclideSetSuggestion hint = NuclideSetAdvisor.Suggest(peaks, rd.EnergySpectrum.EnergyCalibration, file.NuclideSets, file.NuclideDefinitions);
                    shown += hint != null ? 1 : 0;
                    string verdict = truth == null ? (hint != null ? "нет истины, показана" : "нет истины, молчит")
                        : hint == null ? (truth.Count > 0 ? "молчит, сет есть" : "молчит, сета нет")
                        : truth.Contains(hint.Set.Name) ? "верна" : "неверна";
                    Bump(tally, part + "\t" + mode + "\t" + verdict);
                    if (csOnly)
                    {
                        Bump(tally, part + "\t" + mode + "\tCs-137 всего");
                        if (hint != null && hint.Set.Name == "Cs-137 + K-40")
                        {
                            Bump(tally, part + "\t" + mode + "\tCs-137 верна");
                        }
                    }

                    lines.Add(string.Join("\t", rel, device, peaks.Count.ToString(Inv), hint != null ? hint.Set.Name : "",
                        hint != null ? hint.Score.ToString("F3", Inv) + "/" + hint.RunnerUp.ToString("F3", Inv) + "/" + hint.PeakShare.ToString("F3", Inv) + "/" + hint.LineShare.ToString("F3", Inv) : "",
                        part, truth == null ? "?" : string.Join("+", truth.OrderBy(x => x, StringComparer.Ordinal)), mode.ToString()));
                }
            }
            catch (Exception)
            {
                foreign++;
            }
        }

        File.WriteAllLines(tsv, lines, new UTF8Encoding(false));
        Console.WriteLine("прочитано {0}, из корпуса пропущено {1}, не спектры {2}, подсказка у {3}", read, skipped, foreign, shown);
        foreach (var pair in tally.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Console.WriteLine("{0}\t{1}", pair.Key, pair.Value);
        }
        Exit(0);
        return 0;
    }

    static void Bump(Dictionary<string, int> tally, string key)
    {
        int have;
        tally.TryGetValue(key, out have);
        tally[key] = have + 1;
    }

    /// <summary>Как приложение при открытии (`DocumentManager.cs`): нуль в файле значит «посчитать».</summary>
    static void FillTotal(ResultData rd)
    {
        EnergySpectrum s = rd.EnergySpectrum;
        if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
        {
            s.TotalPulseCount = s.Spectrum.Sum(x => (long)x);
            s.ValidPulseCount = s.TotalPulseCount;
        }
    }

    /// <summary>
    /// --thin=: доли через запятую, вложенное прореживание корпуса; прогон снизу вверх — как растёт
    /// живой набор. Без удержания — голая функция, с удержанием — <see cref="NuclideSetHintTracker"/>.
    /// </summary>
    static int Thin(string[] args)
    {
        string corpus = Key(args, "--corpus=");
        string lib = Key(args, "--lib=");
        double[] fractions = Key(args, "--thin=").Split(',').Select(x => double.Parse(x, Inv)).OrderByDescending(x => x).ToArray();
        int seeds = int.Parse(Key(args, "--seeds=") ?? "3", Inv);
        NuclideDefinitionFile file;
        using (var s = File.OpenRead(lib))
        {
            file = (NuclideDefinitionFile)new XmlSerializer(typeof(NuclideDefinitionFile)).Deserialize(s);
        }

        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        var serializer = new XmlSerializer(typeof(ResultDataFile));
        var tally = new Dictionary<string, int>();
        foreach (string[] m in Manifest(Path.Combine(corpus, "manifest.csv")))
        {
            string key = m[0], det = m[1];
            string fold = FoldA.Contains(det) ? "A" : "B";
            HashSet<string> truth = Truth(m[2], m[3]);
            truth.IntersectWith(file.NuclideSets.Select(x => x.Name));
            ResultDataFile f;
            using (var st = File.OpenRead(Path.Combine(corpus, "spectra", key + ".xml")))
            {
                f = (ResultDataFile)serializer.Deserialize(st);
            }

            ResultData rd = f.ResultDataList[0];
            FillTotal(rd);
            ProbeDeviceConfig.Attach(rd);
            EnergySpectrum es = rd.EnergySpectrum;
            int[] full = (int[])es.Spectrum.Clone();
            double measured = es.MeasurementTime, live = es.LiveTime;
            foreach (BackgroundMode mode in new[] { BackgroundMode.Invisible, BackgroundMode.Substract })
            {
                if (mode == BackgroundMode.Substract && rd.BackgroundEnergySpectrum == null)
                {
                    continue;
                }

                for (int seed = 0; seed < seeds; seed++)
                {
                    // Спектры от полного к малому, затем прогон в обратном порядке.
                    var random = new Random(1000 + seed * 7919);
                    var stages = new List<int[]>();
                    int[] current = (int[])full.Clone();
                    double previous = 1.0;
                    foreach (double fraction in fractions)
                    {
                        if (fraction < previous)
                        {
                            current = Thinned(current, fraction / previous, random);
                            previous = fraction;
                        }
                        stages.Add(current);
                    }

                    var tracker = new NuclideSetHintTracker();
                    string lastBare = null, lastHeld = null;
                    int changesBare = 0, changesHeld = 0;
                    for (int i = stages.Count - 1; i >= 0; i--)
                    {
                        es.Spectrum = stages[i];
                        long total = stages[i].Sum(x => (long)x);
                        es.TotalPulseCount = total;
                        es.ValidPulseCount = total;
                        es.MeasurementTime = measured * fractions[i];
                        es.LiveTime = live * fractions[i];
                        List<Peak> peaks = new PeakDetector().DetectPeak(rd, mode, SmoothingMethod.None, null, file.NuclideDefinitions);
                        NuclideSetSuggestion bare = NuclideSetAdvisor.Suggest(peaks, es.EnergyCalibration, file.NuclideSets, file.NuclideDefinitions);
                        NuclideSetSuggestion held = tracker.Step(peaks, es.EnergyCalibration, file.NuclideSets, file.NuclideDefinitions, true);
                        string nameBare = bare != null ? bare.Set.Name : "", nameHeld = held != null ? held.Set.Name : "";
                        if (lastBare != null && lastBare != nameBare) changesBare++;
                        if (lastHeld != null && lastHeld != nameHeld) changesHeld++;
                        lastBare = nameBare;
                        lastHeld = nameHeld;
                        string stage = fold + "\t" + fractions[i].ToString(Inv) + "\t";
                        Bump(tally, stage + "голая\t" + (bare == null ? "молчит" : truth.Contains(nameBare) ? "верна" : "неверна"));
                        Bump(tally, stage + "с удержанием\t" + (held == null ? "молчит" : truth.Contains(nameHeld) ? "верна" : "неверна"));
                        if (held != null && !truth.Contains(nameHeld) && fractions[i] >= 0.03)
                        {
                            Console.WriteLine("неверна\t{0}\t{1}\tзерно {2}\tдоля {3}\t{4}", key, mode, seed, fractions[i].ToString(Inv), nameHeld);
                        }
                    }

                    Bump(tally, fold + "\tсмен голая " + Math.Min(changesBare, 3));
                    Bump(tally, fold + "\tсмен с удержанием " + Math.Min(changesHeld, 3));
                }
            }

            es.Spectrum = full;
            es.MeasurementTime = measured;
            es.LiveTime = live;
        }

        foreach (var pair in tally.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Console.WriteLine("{0}\t{1}", pair.Key, pair.Value);
        }

        Exit(0);
        return 0;
    }

    static int[] Thinned(int[] spectrum, double keep, Random random)
    {
        int[] result = new int[spectrum.Length];
        for (int i = 0; i < spectrum.Length; i++)
        {
            int n = spectrum[i], c = 0;
            if (n < 3000)
            {
                for (int j = 0; j < n; j++)
                {
                    if (random.NextDouble() < keep) c++;
                }
            }
            else
            {
                double mean = n * keep, sd = Math.Sqrt(mean * (1.0 - keep));
                double u = 1.0 - random.NextDouble(), v = random.NextDouble();
                c = (int)Math.Round(mean + sd * Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v));
                c = Math.Max(0, Math.Min(n, c));
            }
            result[i] = c;
        }
        return result;
    }

    /// <summary>Половина по чётности последнего байта MD5 пути: C1 — чётный (подбор), C2 — нечётный (запас).</summary>
    static string Half(string rel)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes(rel));
            return h[h.Length - 1] % 2 == 0 ? "C1" : "C2";
        }
    }

    static readonly string[][] NameRules =
    {
        new[] { "Th-232 + K-40", @"th[-_ ]?23[28]|th[-_ ]?ки|\bth\b|торий|thorium|медальон|колеман|tho?rit" },
        new[] { "Cs-137 + K-40", @"cs[-_ ]?137|\bcs\b|цези|caesium|cesium|600bq_cs|\b137\b" },
        new[] { "Am-241", @"am[-_ ]?241|америц|адик|\badi|smoke" },
        new[] { "Ra-226", @"ra[-_ ]?226|радий|rn[-_ ]?222|радон" },
        new[] { "UGlass", @"uglass|u[-_ ]?235|урановое|ладь" },
        new[] { "Lu-176", @"lu[-_ ]?176|лютец|lutet" },
        new[] { "Eu-152", @"eu[-_ ]?152" },
        new[] { "Ba-133", @"ba[-_ ]?133" },
        new[] { "Co-60", @"co[-_ ]?60" },
        new[] { "Na-22", @"na[-_ ]?22" },
    };

    const string NoSetName = @"фон|background|backgnd|\bbg\b|bkg|k[-_ ]?40|калий|тритий|tritiy|y[-_ ]?88|cd[-_ ]?109|mn[-_ ]?54|i[-_ ]?131|\bbly\b|свинец|\bpb\b$";

    /// <summary>
    /// Истина из имени файла: сеты, названные в имени; пустое множество — имя называет фон или источник
    /// без сета; иначе смотрится путь; null — имя ничего не говорит.
    /// </summary>
    static HashSet<string> Label(string rel)
    {
        string lower = rel.ToLowerInvariant();
        string name = lower.Substring(lower.LastIndexOf('\\') + 1);
        HashSet<string> truth = Named(name);
        if (truth.Count > 0)
        {
            return truth;
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(name, NoSetName))
        {
            return truth;
        }
        truth = Named(lower);
        return truth.Count > 0 ? truth : null;
    }

    static HashSet<string> Named(string text)
    {
        var truth = new HashSet<string>();
        foreach (string[] rule in NameRules)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(text, rule[1]))
            {
                truth.Add(rule[0]);
            }
        }
        return truth;
    }

    static int Count(IEnumerable<Row> rows, string verdict)
    {
        return rows.Count(r => r.Verdict == verdict);
    }

    /// <summary>Сет объявленного родителя: имена библиотеки поставки 05.11.2026.</summary>
    static HashSet<string> Truth(string chains, string nuclides)
    {
        var map = new Dictionary<string, string>
        {
            { "137CS", "Cs-137 + K-40" }, { "176LU", "Lu-176" }, { "241AM", "Am-241" }, { "152EU", "Eu-152" },
            { "22NA", "Na-22" }, { "133BA", "Ba-133" }, { "60CO", "Co-60" },
        };
        var truth = new HashSet<string>();
        foreach (string c in chains.Split(';'))
        {
            if (c == "Th-232" || c == "Th-228") truth.Add("Th-232 + K-40");
            if (c == "Ra-226" || c == "Rn-222") truth.Add("Ra-226");
            if (c.StartsWith("U-", StringComparison.Ordinal)) truth.Add("UGlass");
            // `U-238` в манифесте — ряд целиком, с Ra-226 и дочерними; `U-238u` — оборванный на Ra-226.
            if (c == "U-238") truth.Add("Ra-226");
        }
        foreach (string n in nuclides.Split(';'))
        {
            if (map.ContainsKey(n)) truth.Add(map[n]);
        }
        return truth;
    }

    /// <summary>key, det, chains, nuclides из manifest.csv (кавычки только в хвостовых колонках).</summary>
    static IEnumerable<string[]> Manifest(string path)
    {
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        string[] head = lines[0].Split(',');
        int key = Array.IndexOf(head, "key"), det = Array.IndexOf(head, "det"), chains = Array.IndexOf(head, "chains"), nuclides = Array.IndexOf(head, "nuclides");
        foreach (string line in lines.Skip(1))
        {
            if (line.Length == 0) continue;
            string[] c = line.Split(',');
            yield return new[] { c[key], c[det], c[chains], c[nuclides] };
        }
    }

    // ---------------------------------------------------------------------
    // Панель
    // ---------------------------------------------------------------------

    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    static void Panel(string[] args)
    {
        string corpus = Key(args, "--corpus=");
        string shots = Key(args, "--shots=");
        string lang = Key(args, "--lang=") ?? "en";
        string good = Key(args, "--good=") ?? "G1S16_Cs137_P5";
        string quiet = Key(args, "--quiet=") ?? "G1S24_K40_Petri";
        CultureInfo ui = new CultureInfo(lang == "ru" ? "ru-RU" : "en-US");
        Thread.CurrentThread.CurrentUICulture = ui;
        CultureInfo.DefaultThreadCurrentUICulture = ui;
        // Application.Run в пробе нет, а DoEvents снимает контекст окон: продолжение await в
        // UpdatePeakDetectionResult уходило на пул, и RefreshTable шёл не на потоке окон (Н5 критика
        // `critWave2`: H22 ДЕФЕКТ, NRE в XPTable). Контекст ставится сам, как при Application.Run.
        WindowsFormsSynchronizationContext.AutoInstall = false;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.EnableVisualStyles();
        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        string libFile = Package.GetInstance().NuclideDefinition;
        string libBefore = Sha(libFile);
        NuclideDefinitionManager mgr = NuclideDefinitionManager.GetInstance();
        Console.WriteLine("библиотека: {0}, линий {1}, наборов {2}", libFile, mgr.NuclideDefinitions.Count, mgr.NuclideSets.Count);
        MainForm mainForm = new MainForm(new string[0]);
        Thread.CurrentThread.CurrentUICulture = ui;  // подъём приложения ставит язык из конфига
        // Панели MainForm заводит при показе (`InitializeToolViews`); здесь панель поднята рукой на её поле.
        DCPeakDetectionView panel = new DCPeakDetectionView(mainForm);
        typeof(MainForm).GetField("dcPeakDetectionView", Any).SetValue(mainForm, panel);
        ComboBox combo = (ComboBox)Field(panel, "comboBoxNuclSet");
        Control hint = (Control)Field(panel, "setHintPanel");
        LinkLabel link = (LinkLabel)Field(panel, "setHintLink");
        Button close = (Button)Field(panel, "setHintClose");
        Form host = Host(panel, 432, 260);
        DocEnergySpectrum a = Open(Path.Combine(corpus, "spectra", good + ".xml"));
        DocEnergySpectrum q = Open(Path.Combine(corpus, "spectra", quiet + ".xml"));
        DocEnergySpectrum second = Open(Path.Combine(corpus, "spectra", (Key(args, "--good2=") ?? "GS4000_Th232") + ".xml"));
        DocEnergySpectrum empty = Open(Path.Combine(corpus, "spectra", good + ".xml"));
        Array.Clear(empty.ActiveResultData.EnergySpectrum.Spectrum, 0, empty.ActiveResultData.EnergySpectrum.Spectrum.Length);
        empty.ActiveResultData.EnergySpectrum.TotalPulseCount = 0;
        empty.ActiveResultData.EnergySpectrum.ValidPulseCount = 0;

        Show(mainForm, panel, a);
        Verdict("H1", "по итогу поиска подсказка видна", hint.Visible + ": «" + link.Text + "»", hint.Visible);
        string expected = string.Format(Inv, BecquerelMonitor.Properties.Resources.PeakSetHintText, "Cs-137 + K-40");
        Verdict("H2", "текст по ресурсу с именем сета", expected, link.Text == expected);
        Verdict("H3", "документ не менялся: «все нуклиды»", a.SelectedNuclideSet == null ? "null" : a.SelectedNuclideSet.Name, a.SelectedNuclideSet == null && combo.SelectedIndex == 0);
        ToolTip tip = (ToolTip)Field(panel, "setHintToolTip");
        Console.WriteLine("подсказка мыши на ссылке: {0}", tip.GetToolTip(link).Replace(Environment.NewLine, " / "));
        Console.WriteLine("подсказка мыши на кнопке: {0}", tip.GetToolTip(close).Replace(Environment.NewLine, " / "));
        Verdict("H4", "текст влезает в панель", TextRenderer.MeasureText(link.Text, link.Font).Width + " из " + link.Width, TextRenderer.MeasureText(link.Text, link.Font).Width <= link.Width);
        Verdict("H5", "панель не перекрывает таблицу и список", hint.Bounds.ToString(),
                !hint.Bounds.IntersectsWith(((Control)Field(panel, "table1")).Bounds)
                && !hint.Bounds.IntersectsWith(combo.Bounds)
                && !hint.Bounds.IntersectsWith(((Control)Field(panel, "label4")).Bounds));
        Shot(panel, shots, "A313_hint_" + lang + ".png");

        // Нажатие: настоящие сообщения мыши в окно ссылки.
        int peaksBefore = a.ActiveResultData.DetectedPeaks.Count;
        IntPtr at = (IntPtr)((link.Height / 2) << 16 | 12);
        Peak labelled = a.ActiveResultData.DetectedPeaks.First();
        SendMessage(link.Handle, 0x0200, IntPtr.Zero, at);
        SendMessage(link.Handle, 0x0201, (IntPtr)1, at);
        SendMessage(link.Handle, 0x0202, IntPtr.Zero, at);
        WaitSearch(panel);
        NuclideSet chosen = a.SelectedNuclideSet;
        Verdict("H6", "нажатие выбрало сет за документом", chosen == null ? "null" : chosen.Name, chosen != null && chosen.Name == "Cs-137 + K-40");
        Verdict("H7", "список и панель показывают его", combo.SelectedItem + "", Equals(combo.SelectedItem, "Cs-137 + K-40") && ReferenceEquals(mgr.ActiveSet, chosen));
        Verdict("H8", "поиск прошёл заново: подписи только из сета", a.ActiveResultData.DetectedPeaks.Count + " пиков (было " + peaksBefore + ")",
                !ReferenceEquals(labelled, a.ActiveResultData.DetectedPeaks.FirstOrDefault())
                && a.ActiveResultData.DetectedPeaks.Where(p => p.Nuclide != null).All(p => p.Nuclide.Sets.Contains(chosen.Id)));
        Verdict("H9", "сет выбран: подсказка молчит", hint.Visible.ToString(), !hint.Visible);

        // Возврат на «все нуклиды»: подсказка возвращается после нового поиска.
        combo.SelectedIndex = 0;
        WaitSearch(panel);
        Verdict("H10", "снова «все нуклиды»: подсказка снова видна", hint.Visible.ToString(), hint.Visible);

        // Закрытие: для этого документа и только для него.
        close.PerformClick();
        Application.DoEvents();
        Verdict("H11", "закрыта", hint.Visible.ToString(), !hint.Visible);
        panel.UpdatePeakDetectionResult();
        WaitSearch(panel);
        Verdict("H12", "новый поиск её не возвращает", hint.Visible.ToString(), !hint.Visible);
        Verdict("H13", "закрытие не выбрало сет", a.SelectedNuclideSet == null ? "null" : a.SelectedNuclideSet.Name, a.SelectedNuclideSet == null);
        Show(mainForm, panel, second);
        Verdict("H14", "другой документ: своя подсказка", hint.Visible + ": «" + link.Text + "»", hint.Visible && link.Text.Contains("Th-232"));
        Shot(panel, shots, "A313_hint2_" + lang + ".png");
        Show(mainForm, panel, a);
        Verdict("H15", "вернулись: у закрывшего документа молчит", hint.Visible.ToString(), !hint.Visible);

        // По итогу FSA: тот же сет по тем же пикам; событие приходит с фонового потока.
        ShowFsa(mainForm, panel, second, hint, link);

        // Запись: настоящий порядок вызовов панели, голосует только конец поиска.
        ShowRecording(mainForm, panel, Open(Path.Combine(corpus, "spectra", good + ".xml")), hint);

        // Молчание.
        Show(mainForm, panel, q);
        Verdict("H16", "неуверенное различие (родитель без сета): молчит", q.ActiveResultData.DetectedPeaks.Count + " пиков, видна " + hint.Visible, q.ActiveResultData.DetectedPeaks.Count > 0 && !hint.Visible);
        Show(mainForm, panel, empty);
        Verdict("H17", "пиков нет: молчит", empty.ActiveResultData.DetectedPeaks.Count + " пиков, видна " + hint.Visible, empty.ActiveResultData.DetectedPeaks.Count == 0 && !hint.Visible);
        mainForm.ActiveDocument = null;
        panel.ShowPeakDetectionResult();
        Application.DoEvents();
        Verdict("H18", "документа нет: молчит", hint.Visible.ToString(), !hint.Visible);

        // Чистая функция на голых числах: положительные контроли без окна.
        List<NuclideSet> sets = mgr.NuclideSets;
        List<NuclideDefinition> defs = mgr.NuclideDefinitions;
        double[] e = { 661.7, 1460.8, 75.0 };
        double[] s = { 400.0, 30.0, 25.0 };
        double[] w = { 45.0, 80.0, 18.0 };
        NuclideSetSuggestion good1 = NuclideSetAdvisor.Suggest(e, s, w, sets, defs);
        Verdict("P1", "три линии сета: предлагает его", good1 == null ? "null" : good1.Set.Name + " " + good1.Score.ToString("F2", Inv), good1 != null && good1.Set.Name == "Cs-137 + K-40");
        Verdict("P2", "линии без пиков по ширине (FWHM не измерена): молчит", "", NuclideSetAdvisor.Suggest(e, s, new double[3], sets, defs) == null);
        Verdict("P3", "пиков нет: молчит", "", NuclideSetAdvisor.Suggest(new double[0], new double[0], new double[0], sets, defs) == null);
        Verdict("P4", "сеты пусты: молчит", "", NuclideSetAdvisor.Suggest(e, s, w, new List<NuclideSet>(), defs) == null);
        double[] tie = { 1460.8, 75.0 };
        Verdict("P5", "общие линии двух сетов: разрыва нет, молчит", "", NuclideSetAdvisor.Suggest(tie, new[] { 100.0, 100.0 }, new[] { 80.0, 18.0 }, sets, defs) == null);
        List<NuclideDefinition> hidden = defs.Select(d => d).ToList();
        foreach (NuclideDefinition d in hidden) d.Visible = false;
        Verdict("P6", "линии скрыты: молчит", "", NuclideSetAdvisor.Suggest(e, s, w, sets, defs) == null);
        foreach (NuclideDefinition d in hidden) d.Visible = true;
        double lone = sets.Any(x => x.Name == "Am-241") ? 59.54 : 0.0;
        Verdict("P7", "один слабый пик внизу шкалы: молчит", "", NuclideSetAdvisor.Suggest(new[] { lone }, new[] { 100.0 }, new[] { 15.0 }, sets, defs) == null);
        NuclideSetSuggestion strong = NuclideSetAdvisor.Suggest(new[] { lone }, new[] { 3000.0 }, new[] { 15.0 }, sets, defs);
        Verdict("P8", "один сильный пик внизу шкалы: предлагает", strong == null ? "null" : strong.Set.Name, strong != null && strong.Set.Name == "Am-241");
        NuclideSetSuggestion cs = NuclideSetAdvisor.Suggest(new[] { 661.7 }, new[] { 150.0 }, new[] { 45.0 }, sets, defs);
        Verdict("P9", "один пик 662 кэВ: рентген и общая линия сета не роняют балл", cs == null ? "null" : cs.Set.Name + " " + cs.Score.ToString("F2", Inv), cs != null && cs.Set.Name == "Cs-137 + K-40" && cs.Score > 0.99);
        Verdict("P10", "один пик 662 кэВ, слабый: молчит", "", NuclideSetAdvisor.Suggest(new[] { 661.7 }, new[] { 50.0 }, new[] { 45.0 }, sets, defs) == null);
        Verdict("P11", "только общие линии (калий, рентген): молчит", "", NuclideSetAdvisor.Suggest(new[] { 1460.8, 74.97 }, new[] { 900.0, 900.0 }, new[] { 80.0, 18.0 }, sets, defs) == null);
        // Удержание: балл 0.40 между порогами показа и удержания держит только уже показанный сет.
        double[] eh = { 661.7, 300.0 };
        double[] sh = { 200.0, 300.0 };
        double[] wh = { 45.0, 20.0 };
        NuclideSet csSet = sets.First(x => x.Name == "Cs-137 + K-40");
        NuclideSetSuggestion plain = NuclideSetAdvisor.Suggest(eh, sh, wh, sets, defs);
        NuclideSetSuggestion kept = NuclideSetAdvisor.Suggest(eh, sh, wh, sets, defs, csSet);
        Verdict("P12", "между порогами: новый сет молчит, показанный держится", (plain == null ? "null" : plain.Set.Name) + " / " + (kept == null ? "null" : kept.Set.Name + " " + kept.Score.ToString("F2", Inv)),
                plain == null && kept != null && ReferenceEquals(kept.Set, csSet));
        // Подтверждение при записи: второй вердикт только на новых данных; готовый спектр — сразу.
        var tr = new NuclideSetHintTracker();
        List<Peak> pk = a.ActiveResultData.DetectedPeaks;
        EnergyCalibration ec = a.ActiveResultData.EnergySpectrum.EnergyCalibration;
        NuclideSetSuggestion t1 = tr.Step(pk, ec, sets, defs, true);
        NuclideSetSuggestion t2 = tr.Step(pk, ec, sets, defs, true);
        NuclideSetSuggestion t3 = tr.Step(new List<Peak>(pk), ec, sets, defs, true);
        Verdict("P13", "запись: первый и повтор на том же списке молчат, новый список показывает", (t1 == null) + "/" + (t2 == null) + "/" + (t3 != null), t1 == null && t2 == null && t3 != null);
        Verdict("P14", "готовый спектр: сразу", "", new NuclideSetHintTracker().Step(pk, ec, sets, defs, false) != null);
        NuclideSetSuggestion xr = NuclideSetAdvisor.Suggest(new[] { 661.7, 74.97 }, new[] { 400.0, 400.0 }, new[] { 45.0, 18.0 }, sets, defs);
        Verdict("P16", "пик рентгена из сета балл не роняет", xr == null ? "null" : xr.Set.Name + " " + xr.Score.ToString("F2", Inv), xr != null && xr.Set.Name == "Cs-137 + K-40" && xr.Score > 0.99);
        if (sets.Any(x => x.Name == "Na-22"))
        {
            NuclideSetSuggestion na = NuclideSetAdvisor.Suggest(new[] { 511.0, 1274.5 }, new[] { 2000.0, 300.0 }, new[] { 40.0, 70.0 }, sets, defs);
            Verdict("P15", "линия без выхода объясняет пик, в долю линий не идёт", na == null ? "null" : na.Set.Name + " " + na.Score.ToString("F2", Inv), na != null && na.Set.Name == "Na-22" && na.Score > 0.99);
        }
        else
        {
            Console.WriteLine("P15\tв библиотеке нет сета с линией без выхода, не судится");
        }

        Verdict("Z1", "файл библиотеки не менялся", Sha(libFile), Sha(libFile) == libBefore);
        host.Dispose();
    }

    static void ShowFsa(MainForm mainForm, DCPeakDetectionView panel, DocEnergySpectrum document, Control hint, LinkLabel link)
    {
        Show(mainForm, panel, document);
        object session = document.FsaSession;
        Verdict("H19", "панель подписана на сеанс FSA активного документа", "", ReferenceEquals(Field(panel, "setHintSession"), session));
        string before = link.Text;
        typeof(DCPeakDetectionView).GetMethod("ShowSetHint", Any).Invoke(panel, new object[] { null });
        Application.DoEvents();
        bool hidden = !hint.Visible;
        var completed = (MulticastDelegate)Field(session, "Completed");
        var raise = System.Threading.Tasks.Task.Run(() => completed.DynamicInvoke(session, EventArgs.Empty));
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!hint.Visible && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        raise.Wait();
        Verdict("H20", "итог FSA с фонового потока показывает подсказку на потоке окон", hidden + " → " + hint.Visible + ": «" + link.Text + "»", hidden && hint.Visible && link.Text == before);
        Stopwatch watch = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            typeof(DCPeakDetectionView).GetMethod("UpdateSetHint", Any).Invoke(panel, null);
        }
        watch.Stop();
        Console.WriteLine("цена подсказки на потоке окон: {0} мкс на вызов, пиков {1}", (watch.Elapsed.TotalMilliseconds).ToString("F1", Inv), document.ActiveResultData.DetectedPeaks.Count);
    }

    static void ShowRecording(MainForm mainForm, DCPeakDetectionView panel, DocEnergySpectrum document, Control hint)
    {
        ResultData rd = document.ActiveResultData;
        if (rd.ResultDataStatus == null) rd.ResultDataStatus = new ResultDataStatus();
        rd.ResultDataStatus.Recording = true;
        EnergySpectrum es = rd.EnergySpectrum;
        int[] full = (int[])es.Spectrum.Clone();
        long total = es.TotalPulseCount;
        Show(mainForm, panel, document);
        Verdict("H21", "запись: первый вердикт не показывает", hint.Visible.ToString(), !hint.Visible);
        // Следующий такт: спектр пуст, поиск ещё не кончился; прежний список голосовать не должен.
        var list = rd.DetectedPeaks;
        es.Spectrum = new int[full.Length];
        es.TotalPulseCount = total + 7;
        es.ValidPulseCount = total + 7;
        panel.ShowPeakDetectionResult();
        Verdict("H22", "начало такта: прежний список не подтверждает сет", hint.Visible + ", список тот же " + ReferenceEquals(list, rd.DetectedPeaks), !hint.Visible && ReferenceEquals(list, rd.DetectedPeaks));
        WaitSearch(panel);
        Verdict("H23", "конец поиска по пустому спектру: молчит", rd.DetectedPeaks.Count + " пиков, видна " + hint.Visible, rd.DetectedPeaks.Count == 0 && !hint.Visible);
        es.Spectrum = (int[])full.Clone();
        es.TotalPulseCount = total;
        es.ValidPulseCount = total;
        Show(mainForm, panel, document);
        bool first = hint.Visible;
        Show(mainForm, panel, document);
        Verdict("H24", "два конца поиска подряд: со второго видна", first + " → " + hint.Visible, !first && hint.Visible);
        rd.ResultDataStatus.Recording = false;
        // Закрытие последнего документа: MainForm только гасит панель.
        object session = document.FsaSession;
        mainForm.ActiveDocument = null;
        panel.Enabled = false;
        Application.DoEvents();
        var completed = (MulticastDelegate)Field(session, "Completed");
        bool subscribed = completed != null && completed.GetInvocationList().Any(d => ReferenceEquals(d.Target, panel));
        Verdict("H25", "панель погашена: подсказки и подписки нет", ((bool)hint.GetType().GetProperty("Visible").GetValue(hint, null)) + "/" + subscribed, !hint.Visible && !subscribed && Field(panel, "setHintSet") == null);
        mainForm.ActiveDocument = document;
        panel.Enabled = true;
        panel.ShowPeakDetectionResult();
        WaitSearch(panel);
        Verdict("H26", "панель включена: подсказка вернулась", hint.Visible.ToString(), hint.Visible);
    }

    static void Show(MainForm mainForm, DCPeakDetectionView panel, DocEnergySpectrum document)
    {
        mainForm.ActiveDocument = document;
        panel.ShowPeakDetectionResult();
        WaitSearch(panel);
    }

    static void Shot(Control panel, string shots, string name)
    {
        if (shots == null)
        {
            return;
        }
        Directory.CreateDirectory(shots);
        string path = Path.Combine(shots, name);
        panel.Refresh();
        using (Bitmap bmp = new Bitmap(panel.Width, Math.Min(panel.Height, 140)))
        {
            panel.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(path, ImageFormat.Png);
        }
        Console.WriteLine("снимок: {0}", path);
    }

    static DocEnergySpectrum Open(string path)
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
            for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
            s.TotalPulseCount = total;
            s.ValidPulseCount = total;
        }
        Console.WriteLine("спектр {0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));
        if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
        {
            if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
            {
                cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
            }
            if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
        }
        if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
        var doc = new DocEnergySpectrum(path);
        doc.ResultDataFile = file;
        doc.ActiveResultDataIndex = 0;
        doc.UpdateEnergySpectrum();
        return doc;
    }

    static Form Host(Form content, int width, int height)
    {
        var host = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            ShowInTaskbar = false,
            ClientSize = new Size(width, height),
        };
        content.TopLevel = false;
        content.Dock = DockStyle.Fill;
        host.Controls.Add(content);
        content.Show();
        host.Show();
        return host;
    }

    static void WaitSearch(DCPeakDetectionView panel)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        do
        {
            Application.DoEvents();
            Thread.Sleep(10);
            if (DateTime.UtcNow > deadline) throw new TimeoutException("поиск пиков не завершился за 60 с");
        }
        while ((bool)Field(panel, "isProcessing") || (bool)Field(panel, "refreshPending"));
        Application.DoEvents();
    }

    static string Sha(string path)
    {
        using (SHA256 s = SHA256.Create())
        {
            return BitConverter.ToString(s.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }
    }

    static string Key(string[] args, string key)
    {
        string a = args.FirstOrDefault(x => x.StartsWith(key, StringComparison.Ordinal));
        return a == null ? null : a.Substring(key.Length);
    }

    static void Verdict(string id, string what, string got, bool clean)
    {
        if (!clean) defects++;
        Console.WriteLine("{0}\t{1}\t{2}\t{3}", id, what, got ?? "(null)", clean ? "ЧИСТО" : "ДЕФЕКТ");
    }

    static object Field(object o, string name)
    {
        for (Type t = o.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo fi = t.GetField(name, Any);
            if (fi != null) return fi.GetValue(o);
        }
        throw new MissingFieldException(o.GetType().Name, name);
    }
}
