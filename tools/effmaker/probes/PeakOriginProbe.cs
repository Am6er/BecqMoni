using BecquerelMonitor;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace PeakOriginProbe
{
    /// <summary>
    /// Сколько найденных пиков объясняются НЕ линией нуклида, а устройством
    /// спектрометра: вылетом аннигиляционного кванта, суммированием двух
    /// квантов или обратным рассеянием.
    ///
    /// Зачем. Журнал `tools/pie` говорит, что фантом полноспектральной
    /// декомпозиции — это структура спектра без образа: компонент берут не
    /// потому, что нуклид есть, а потому, что структуру больше нечем закрыть.
    /// Три самых частых таких структуры имеют имена, и их можно назвать, а не
    /// приписывать нуклиду. У InterSpec это отдельный модуль (`AnalystChecks`);
    /// проба переносит его правила на наш поиск пиков и меряет, сколько их у
    /// нас на корпусе.
    ///
    /// Правила (по InterSpec, `AnalystChecks.h`):
    ///
    /// * **Вылет.** Пик на E объясним, если есть пик на E+511 или E+1022,
    ///   причём родитель выше порога рождения пар (~1255 кэВ). Двойной вылет
    ///   проверяется ПЕРВЫМ: иначе двойной опознается как одиночный от другого
    ///   родителя.
    /// * **Сумм-пик.** Есть пара пиков E1+E2 ≈ E. Каскадный — если ОБА пика
    ///   подписаны ОДНИМ нуклидом и эта пара стоит у него в таблице совпадений
    ///   `gamma_coincidence` (см. `database/scheme.md`, §8). Требование одного
    ///   нуклида здесь не украшение: без него годится любая пара, у которой
    ///   совпадение нашлось хоть у одного из 1643 родителей таблицы, и признак
    ///   срабатывает почти везде — на ASN16 это 47 пиков из 114 против 9 с
    ///   требованием. Прочие суммы считаются случайным наложением, и его
    ///   требуем только от заметных линий.
    /// * **Обратное рассеяние.** Пик на E объясним, если есть сильный пик на
    ///   E_p, для которого E = E_p/(1 + 2·E_p/511) — квант, рассеянный на 180°
    ///   в веществе вокруг детектора.
    ///
    /// Окно поиска — как у InterSpec: max(1, min(0.5·ПШПВ, 15)) кэВ.
    ///
    ///     peakoriginprobe --spectra=spectra [--nucdb=nucdb.sqlite]
    ///                     [--csv=out.csv] [--group=ASN16]
    ///
    /// Ожидания «ВСЕ СОШЛИСЬ» нет: это измерение, а не проверка. Печатается,
    /// сколько пиков объяснено и сколько из объяснённых УЖЕ ПОДПИСАНЫ нуклидом
    /// — последнее и есть цена вопроса.
    /// </summary>
    static class Program
    {
        const double PairThresholdKev = 1255.0;
        const double ElectronMassKev = 510.99895;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            string spectraDir = "spectra";
            string nucdbPath = null;
            string csvPath = null;
            string group = "";
            foreach (string a in args)
            {
                if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectraDir = a.Substring(10);
                else if (a.StartsWith("--nucdb=", StringComparison.Ordinal)) nucdbPath = a.Substring(8);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csvPath = a.Substring(6);
                else if (a.StartsWith("--group=", StringComparison.Ordinal)) group = a.Substring(8);
            }
            if (nucdbPath == null)
                nucdbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");

            if (!Directory.Exists(spectraDir))
            {
                Console.Error.WriteLine("нет каталога {0}", spectraDir);
                return 2;
            }

            Coincidences coinc;
            try
            {
                coinc = Coincidences.Load(nucdbPath);
            }
            catch (Exception e)
            {
                // Без таблицы совпадений каскадную ветвь отличить нечем, и
                // молча считать все суммы случайными нельзя — это подменило бы
                // ответ на вопрос пробы.
                Console.Error.WriteLine("таблица совпадений не прочитана: {0}", e);
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            NuclideSet set = nuclides.ActiveSet;

            var csv = new StringBuilder();
            csv.AppendLine("group,spectrum,energy_kev,counts,fwhm_kev,fwhm_expected_kev,"
                           + "width_ratio,width_rel,nuclide,origin,detail");

            int totalPeaks = 0, totalNamed = 0;
            // Отношение «измеренная ширина / ожидаемая» по классам: у фотопика
            // оно около единицы, у широкой структуры обязано быть выше — это и
            // проверяется, прежде чем делать из ширины правило (P4).
            var ratioByOrigin = new Dictionary<string, List<double>>();
            var byOrigin = new Dictionary<string, int>();
            var namedByOrigin = new Dictionary<string, int>();

            foreach (string file in Directory.GetFiles(spectraDir, "*.xml")
                                            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                List<Peak> peaks;
                string settings = "";
                ResultData rd = null;      // нужен и ниже — считать ожидаемую ширину
                try
                {
                    rd = LoadResult(file);
                    var cfg = (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig;
                    int nch = rd.EnergySpectrum.NumberOfChannels;
                    double ch662 = rd.EnergySpectrum.EnergyCalibration.EnergyToChannel(662.0, nch);
                    settings = string.Format(CultureInfo.InvariantCulture,
                        "SNR>={0}, допуск {1}, ПШПВ(662) {2:F2} кан. из {3}, калибровка {4}",
                        cfg.Min_SNR, cfg.Tolerance,
                        rd.FwhmCalibration != null ? rd.FwhmCalibration.ChannelToFwhm(ch662) : double.NaN,
                        nch,
                        rd.FwhmCalibration != null && rd.FwhmCalibration.NotCalibrated()
                            ? "не выполнена" : "есть");
                    peaks = new PeakDetector().DetectPeak(
                        rd, BackgroundMode.Invisible, SmoothingMethod.None,
                        set, nuclides.NuclideDefinitions);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("{0}: {1}", name, e.Message);
                    continue;
                }
                if (peaks == null || peaks.Count == 0)
                {
                    // Молчаливый ноль скрыл бы целую группу корпуса — говорим,
                    // с какими настройками искали.
                    Console.Error.WriteLine("{0}: пиков не найдено ({1})", name, settings);
                    continue;
                }

                peaks.Sort((a, b) => a.Energy.CompareTo(b.Energy));
                double maxCounts = peaks.Max(p => (double)p.Count);

                // Опорная ширина — МЕДИАНА по этому же спектру, а не единица.
                // Абсолютное «измеренная / ожидаемая» мерит не природу пика, а
                // ошибку ПШПВ-калибровки прибора: по корпусу медиана этого
                // отношения гуляет от 0.35 (G1S) до 2.97 (ASN8_8192), и внутри
                // группы все классы пиков сидят на одном значении. Сравнивать
                // поэтому надо с соседями по спектру.
                var spectrumRatios = new List<double>();
                foreach (Peak q in peaks)
                {
                    double e = ExpectedFwhmKev(rd, q);
                    if (e > 0.0 && q.FWHM > 0.0)
                    {
                        spectrumRatios.Add(q.FWHM / e);
                    }
                }

                double medianRatio = double.NaN;
                if (spectrumRatios.Count > 0)
                {
                    spectrumRatios.Sort();
                    medianRatio = spectrumRatios[spectrumRatios.Count / 2];
                }

                foreach (Peak p in peaks)
                {
                    totalPeaks++;
                    bool named = p.Nuclide != null;
                    if (named) totalNamed++;

                    string detail;
                    string origin = Classify(p, peaks, maxCounts, coinc, out detail);
                    if (origin == null)
                        continue;

                    Bump(byOrigin, origin);
                    if (named) Bump(namedByOrigin, origin);

                    double expected = ExpectedFwhmKev(rd, p);
                    double ratio = expected > 0.0 ? p.FWHM / expected : double.NaN;
                    double relative = medianRatio > 0.0 ? ratio / medianRatio : double.NaN;
                    Accumulate(ratioByOrigin, origin, relative);
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2:F2},{3},{4:F2},{5:F2},{6:F3},{7:F3},{8},{9},{10}",
                        group, Csv(name), p.Energy, p.Count, p.FWHM, expected, ratio, relative,
                        Csv(p.Nuclide != null ? p.Nuclide.Name : ""), origin, Csv(detail)));
                }
            }

            int explained = byOrigin.Values.Sum();
            Console.WriteLine("группа {0}: пиков {1}, подписаны нуклидом {2},"
                              + " объяснены устройством {3} ({4:F1} %)",
                              group == "" ? "(без имени)" : group, totalPeaks, totalNamed,
                              explained, totalPeaks > 0 ? 100.0 * explained / totalPeaks : 0.0);
            foreach (string key in byOrigin.Keys.OrderBy(k => k))
            {
                int named;
                namedByOrigin.TryGetValue(key, out named);
                Console.WriteLine("   {0,-22} {1,4}  из них подписаны {2,4}   ширина к соседям {3}",
                                  key, byOrigin[key], named, Spread(ratioByOrigin, key));
            }

            if (csvPath != null)
            {
                bool exists = File.Exists(csvPath);
                using (var w = new StreamWriter(csvPath, exists, new UTF8Encoding(false)))
                {
                    string text = csv.ToString();
                    if (exists)
                    {
                        int nl = text.IndexOf('\n');
                        text = nl >= 0 ? text.Substring(nl + 1) : "";
                    }
                    w.Write(text);
                }
            }
            return 0;
        }

        /// <summary>Окно поиска сопутствующего пика, кэВ — как у InterSpec.</summary>
        static double Window(Peak p)
        {
            return Math.Max(1.0, Math.Min(0.5 * p.FWHM, 15.0));
        }

        /// <summary>
        /// Какой ширины пик ЖДЁТ калибровка прибора на этой энергии, кэВ.
        ///
        /// Нужна ради P4: обратное рассеяние даёт не пик, а широкую структуру,
        /// и отличить его от фотопика можно только сравнив ИЗМЕРЕННУЮ ширину с
        /// ожидаемой. Измеренная у нас есть — финдер считает её по второй
        /// производной (`fwhm = 2√(2·snr₀/d²snr)`) и кладёт в `Peak.FWHM`;
        /// ожидаемую даёт ПШПВ-калибровка, но В КАНАЛАХ, поэтому её надо
        /// перевести в кэВ по энергетической — тем же способом, каким это
        /// делает `FsaAnalyzer.SplitContinuumBelowTrustFloor`.
        ///
        /// ⚠ Отношение ограничено сверху и снизу самим финдером: он берёт пик
        /// только при `Min_FWHM_Tol·ожидаемая ≤ измеренная ≤ Max_FWHM_Tol·…`
        /// (`PeakFinder.calculate`). Структуру шире допуска он не предъявит
        /// вовсе — значит признак по ширине работает ВНУТРИ окна допуска, а
        /// про то, что за окном, не говорит ничего.
        /// </summary>
        static double ExpectedFwhmKev(ResultData rd, Peak p)
        {
            if (rd == null || rd.FwhmCalibration == null || rd.EnergySpectrum == null)
            {
                return double.NaN;
            }

            EnergyCalibration energy = rd.EnergySpectrum.EnergyCalibration;
            double channels = rd.FwhmCalibration.ChannelToFwhm(p.Channel);
            if (!(channels > 0.0) || double.IsNaN(channels) || energy == null)
            {
                return double.NaN;
            }

            double kev = energy.ChannelToEnergy(p.Channel + channels / 2.0)
                         - energy.ChannelToEnergy(p.Channel - channels / 2.0);
            return kev > 0.0 ? kev : double.NaN;
        }

        /// <summary>Медиана и края отношения ширин по классу — одной строкой.</summary>
        static string Spread(Dictionary<string, List<double>> map, string key)
        {
            List<double> list;
            if (!map.TryGetValue(key, out list) || list.Count == 0)
            {
                return "нет";
            }

            list.Sort();
            double median = list[list.Count / 2];
            return string.Format(CultureInfo.InvariantCulture,
                                 "медиана {0:F2}  ({1:F2}…{2:F2})",
                                 median, list[0], list[list.Count - 1]);
        }

        static void Accumulate(Dictionary<string, List<double>> map, string key, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return;
            }

            List<double> list;
            if (!map.TryGetValue(key, out list))
            {
                map[key] = list = new List<double>();
            }

            list.Add(value);
        }

        static string Classify(Peak p, List<Peak> peaks, double maxCounts,
                               Coincidences coinc, out string detail)
        {
            detail = "";
            double w = Window(p);

            // Двойной вылет проверяется раньше одиночного: иначе двойной вылет
            // сильной линии опознаётся как одиночный вылет другой, более слабой.
            Peak parent = Find(peaks, p.Energy + 2.0 * ElectronMassKev, w);
            if (parent != null && parent.Energy > PairThresholdKev)
            {
                detail = string.Format(CultureInfo.InvariantCulture,
                                       "двойной вылет {0:F1}", parent.Energy);
                return "вылет двойной";
            }
            parent = Find(peaks, p.Energy + ElectronMassKev, w);
            if (parent != null && parent.Energy > PairThresholdKev)
            {
                detail = string.Format(CultureInfo.InvariantCulture,
                                       "одиночный вылет {0:F1}", parent.Energy);
                return "вылет одиночный";
            }

            // Сумма двух квантов. Перебираем пары пиков ниже данного; каскадная
            // ветвь узнаётся по таблице совпадений, всё остальное — случайное
            // наложение, и его требуем только от заметных линий (иначе на любой
            // сетке найдётся пара, дающая нужную сумму).
            Peak bestA = null, bestB = null;
            bool bestCascade = false;
            double bestScore = -1.0;
            for (int i = 0; i < peaks.Count; i++)
            {
                if (peaks[i].Energy >= p.Energy) break;
                for (int j = i; j < peaks.Count; j++)
                {
                    if (peaks[j].Energy >= p.Energy) break;
                    double sum = peaks[i].Energy + peaks[j].Energy;
                    if (Math.Abs(sum - p.Energy) > w) continue;

                    // Каскад — только у пары, подписанной одним нуклидом, и
                    // только если совпадение стоит именно у НЕГО.
                    string nuc = SameNuclide(peaks[i], peaks[j]);
                    bool cascade = nuc != null
                                   && coinc.IsPair(nuc, peaks[i].Energy, peaks[j].Energy, w);
                    double score = (double)peaks[i].Count * peaks[j].Count * (cascade ? 1.0 : 0.25);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    bestA = peaks[i];
                    bestB = peaks[j];
                    bestCascade = cascade;
                }
            }
            if (bestA != null)
            {
                bool strong = bestA.Count > 0.05 * maxCounts && bestB.Count > 0.05 * maxCounts;
                if (bestCascade || strong)
                {
                    detail = string.Format(CultureInfo.InvariantCulture,
                                           "{0:F1} + {1:F1}", bestA.Energy, bestB.Energy);
                    return bestCascade ? "сумма каскадная" : "сумма случайная";
                }
            }

            // Обратное рассеяние на 180°: E = Ep / (1 + 2 Ep / mc2).
            //
            // Правило самое слабое из трёх: годится ЛЮБОЙ пик выше по шкале с
            // заметной площадью. Поэтому рядом считается, сколько таких
            // родителей нашлось и сильнее ли рассеянный пик своего родителя —
            // и то и другое печатается, чтобы ужесточение выбиралось числом,
            // а не на слух (P4).
            Peak backParent = null;
            int backCandidates = 0;
            foreach (Peak q in peaks)
            {
                if (q.Energy <= p.Energy) continue;
                if (q.Count < 0.05 * maxCounts) continue;
                double back = q.Energy / (1.0 + 2.0 * q.Energy / ElectronMassKev);
                if (Math.Abs(back - p.Energy) <= w)
                {
                    backCandidates++;
                    if (backParent == null || q.Count > backParent.Count)
                    {
                        backParent = q;
                    }
                }
            }

            if (backParent != null)
            {
                detail = string.Format(CultureInfo.InvariantCulture,
                                       "рассеяние от {0:F1}; родителей {1}; площадь/родителя {2:F2}",
                                       backParent.Energy, backCandidates,
                                       backParent.Count > 0
                                           ? (double)p.Count / backParent.Count : double.NaN);
                return "обратное рассеяние";
            }

            return null;
        }

        /// <summary>Имя нуклида, если оба пика подписаны им одним, иначе null.</summary>
        static string SameNuclide(Peak a, Peak b)
        {
            if (a.Nuclide == null || b.Nuclide == null) return null;
            string na = a.Nuclide.NuclideName;
            string nb = b.Nuclide.NuclideName;
            return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase) ? na : null;
        }

        static Peak Find(List<Peak> peaks, double energy, double window)
        {
            Peak best = null;
            double bestDiff = double.MaxValue;
            foreach (Peak p in peaks)
            {
                double diff = Math.Abs(p.Energy - energy);
                if (diff <= window && diff < bestDiff)
                {
                    bestDiff = diff;
                    best = p;
                }
            }
            return best;
        }

        static void Bump(Dictionary<string, int> map, string key)
        {
            int n;
            map.TryGetValue(key, out n);
            map[key] = n + 1;
        }

        static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0
                ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
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

            // Настроек поиска пиков в файле спектра может не быть вовсе — у
            // германиевых спектров корпуса их нет. Приложение в этом случае
            // берёт их у конфигурации прибора (DCPeakDetectionView), и проба
            // обязана делать то же: иначе поиск падает на null, спектр молча
            // выпадает, и целый конец корпуса оказывается не измерен.
            if (!(rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig)
                && rd.DeviceConfig != null
                && rd.DeviceConfig.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig fromDevice)
            {
                rd.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)fromDevice.Clone();
            }
            if (!(rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig))
                throw new InvalidDataException("нет настроек поиска пиков ни в спектре, ни в приборе");

            if (rd.FwhmCalibration == null)
            {
                var cfg = (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig;
                rd.FwhmCalibration = cfg.FwhmCalibration
                    ?? FwhmCalibration.DefaultCalibration(cfg, s.EnergyCalibration);
            }
            return rd;
        }

        /// <summary>
        /// Таблица каскадных совпадений из `nucdb.sqlite`, разложенная по
        /// нуклидам. Вопрос к ней: вылетают ли ЭТИ две линии вместе У ЭТОГО
        /// нуклида. Спрашивать «хоть у кого-нибудь» бессмысленно — в 128 429
        /// парах найдётся почти всё.
        ///
        /// Ключ — наш `nucid` («214BI»), потому что подпись пика приходит из
        /// определений нуклидов в виде «Bi-214». Метастабильные из таблицы
        /// сюда не попадают: их `nucid` пуст (см. `database/scheme.md`, §8), а
        /// вымышленное соответствие уровней тут только навредило бы.
        /// </summary>
        sealed class Coincidences
        {
            /// <summary>nucid -> пары энергий, свёрнутых в целые кэВ.</summary>
            readonly Dictionary<string, Dictionary<int, List<int>>> byNuclide =
                new Dictionary<string, Dictionary<int, List<int>>>(StringComparer.OrdinalIgnoreCase);

            public int Nuclides { get { return byNuclide.Count; } }

            public static Coincidences Load(string path)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("нет " + path, path);

                var c = new Coincidences();
                using (var connection = new SqliteConnection(
                    "Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;"))
                {
                    connection.Open();
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText =
                            "select p.nucid, c.energy_mkev, c.coinc_energy_mkev"
                            + " from gamma_coincidence c"
                            + " join gamma_coincidence_parent p on p.id = c.parent_id"
                            + " where p.nucid is not null";
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string nucid = reader.GetString(0);
                                int a = (int)Math.Round(reader.GetInt64(1) / 1000.0);
                                int b = (int)Math.Round(reader.GetInt64(2) / 1000.0);
                                c.Add(nucid, Math.Min(a, b), Math.Max(a, b));
                            }
                        }
                    }
                }
                if (c.byNuclide.Count == 0)
                    throw new InvalidDataException("таблица gamma_coincidence пуста");
                return c;
            }

            void Add(string nucid, int lo, int hi)
            {
                Dictionary<int, List<int>> map;
                if (!byNuclide.TryGetValue(nucid, out map))
                {
                    map = new Dictionary<int, List<int>>();
                    byNuclide[nucid] = map;
                }
                List<int> list;
                if (!map.TryGetValue(lo, out list))
                {
                    list = new List<int>();
                    map[lo] = list;
                }
                if (!list.Contains(hi)) list.Add(hi);
            }

            /// <summary>«Bi-214» -> «214BI»; null, если имя не разбирается.</summary>
            public static string ToNucid(string name)
            {
                if (string.IsNullOrEmpty(name)) return null;
                int dash = name.IndexOf('-');
                if (dash <= 0 || dash + 1 >= name.Length) return null;
                string el = name.Substring(0, dash).Trim();
                string mass = name.Substring(dash + 1).Trim();
                // Метастабильные («Ba-137m») в таблице не по `nucid` — пропускаем.
                foreach (char ch in mass)
                {
                    if (ch < '0' || ch > '9') return null;
                }
                return mass + el.ToUpperInvariant();
            }

            public bool IsPair(string nuclideName, double e1, double e2, double window)
            {
                string nucid = ToNucid(nuclideName);
                if (nucid == null) return false;
                Dictionary<int, List<int>> map;
                if (!byNuclide.TryGetValue(nucid, out map)) return false;

                double lo = Math.Min(e1, e2), hi = Math.Max(e1, e2);
                int span = (int)Math.Ceiling(window);
                for (int a = (int)Math.Round(lo) - span; a <= (int)Math.Round(lo) + span; a++)
                {
                    List<int> list;
                    if (!map.TryGetValue(a, out list)) continue;
                    if (Math.Abs(a - lo) > window) continue;
                    foreach (int b in list)
                    {
                        if (Math.Abs(b - hi) <= window) return true;
                    }
                }
                return false;
            }
        }
    }
}
