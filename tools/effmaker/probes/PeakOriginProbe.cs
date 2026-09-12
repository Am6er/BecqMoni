using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
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
    ///     peakoriginprobe --spectra=spectra [--manifest=manifest.csv]
    ///                     [--sample=137CS,40K] [--chain=Th-232]
    ///                     [--nucdb=nucdb.sqlite] [--csv=out.csv] [--group=ASN16]
    ///
    /// Ожидания «ВСЕ СОШЛИСЬ» нет: это измерение, а не проверка. Печатается,
    /// сколько пиков объяснено и сколько из объяснённых УЖЕ ПОДПИСАНЫ нуклидом
    /// — последнее и есть цена вопроса.
    ///
    /// **Набор нуклидов — СВОЙ у каждого спектра (`P5`, полоса П19 12.09.2026;
    /// первый постулат `S56`).** До того пики подписывались из поставочного
    /// `NuclideDefinition.xml` через активный набор — то есть весь список,
    /// предъявленный любому спектру, и столбец «из них подписаны» мерил не то,
    /// что снято, а то, что есть в поставке. Теперь состав спектра берётся из
    /// `manifest.csv` корпуса (колонки `chains`, `nuclides`; строка ищется по
    /// имени файла спектра = `key`), библиотека собирается общим входом
    /// приложения — `FsaSampleSpec.FromManifest` → `FsaSampleLibrary.Build` →
    /// `AsDefinitions` — из `nucdb`/`matdb`, как у `CorpusFsaProbe --lib=sample`
    /// и восьми проб полосы П11. `--sample=`/`--chain=` задают ОДИН состав всем
    /// спектрам каталога (для каталога из одного спектра) и манифест тогда не
    /// читается. Спектр, которого в манифесте нет или чей состав пуст, —
    /// отказ ДО прогона кодом 2 со списком: молча пропущенный спектр выглядел
    /// бы как «пиков нет». `NuclideDefinitionManager` не поднимается; счётчик
    /// его обращений печатается всегда, не ноль — код 12 (`AMBER19`).
    ///
    /// ⚠ Фон (`manifest.background`) — файл, а не нуклиды: проба ищет пики с
    /// `BackgroundMode.Invisible` (фон не вычитается, как и было), поэтому
    /// пики фона (K-40, ряды) у образца, где их нет в составе, остаются БЕЗ
    /// подписи. Это цена постулата, а не дефект: подписывать их «из общего
    /// списка» значило бы вернуть то, от чего строка ушла.
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
            string manifestPath = null;
            var oneChains = new List<string>();
            var oneNuclides = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectraDir = a.Substring(10);
                else if (a.StartsWith("--nucdb=", StringComparison.Ordinal)) nucdbPath = a.Substring(8);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csvPath = a.Substring(6);
                else if (a.StartsWith("--group=", StringComparison.Ordinal)) group = a.Substring(8);
                else if (a.StartsWith("--manifest=", StringComparison.Ordinal)) manifestPath = a.Substring(11);
                else if (a.StartsWith("--sample=", StringComparison.Ordinal))
                    oneNuclides.AddRange(a.Substring(9).Split(','));
                else if (a.StartsWith("--nuclides=", StringComparison.Ordinal))
                    oneNuclides.AddRange(a.Substring(11).Split(','));
                else if (a.StartsWith("--chain=", StringComparison.Ordinal))
                    oneChains.AddRange(a.Substring(8).Split(','));
                else if (a.StartsWith("--set=", StringComparison.Ordinal))
                {
                    // Поставочного набора у этой пробы больше нет (`P5`, `AMBER19`).
                    Console.Error.WriteLine("--set= снят: состав берётся из manifest.csv "
                                            + "(или --sample=/--chain= одним составом на каталог)");
                    return 2;
                }
                // `A263`: неизвестное ИМЯ ключа — отказ, а не молчание.
                else
                {
                    Console.WriteLine("не знаю ключа: " + a);
                    return 2;
                }
            }
            if (nucdbPath == null)
                nucdbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");

            if (!Directory.Exists(spectraDir))
            {
                Console.Error.WriteLine("нет каталога {0}", spectraDir);
                return 2;
            }

            // Метки рядов проверяются ДО чтения спектров — тем же словарём, каким
            // их читает манифест корпуса (`FsaSampleChain.FromLabel`).
            foreach (string label in oneChains)
            {
                if (FsaSampleChain.FromLabel(label) == null)
                {
                    Console.Error.WriteLine("--chain={0}: неизвестный ряд; известные: {1}",
                                            label, string.Join(", ", FsaSampleChain.KnownLabels));
                    return 2;
                }
            }

            string[] files = Directory.GetFiles(spectraDir, "*.xml")
                                      .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                      .ToArray();
            Dictionary<string, Composition> compositions;
            if (oneChains.Count > 0 || oneNuclides.Count > 0)
            {
                compositions = new Dictionary<string, Composition>(StringComparer.Ordinal);
                foreach (string file in files)
                {
                    compositions[Path.GetFileNameWithoutExtension(file)] =
                        new Composition(oneChains, oneNuclides);
                }
                Console.WriteLine("состав: один на каталог (--sample=/--chain=): ряды [{0}], нуклиды [{1}]",
                                  string.Join(", ", oneChains), string.Join(", ", oneNuclides));
            }
            else
            {
                if (manifestPath == null)
                    manifestPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(spectraDir)) ?? ".",
                                                "manifest.csv");
                compositions = ReadManifest(manifestPath, files);
                if (compositions == null)
                    return 2;
                Console.WriteLine("состав: из {0} ({1} спектров), линии из nucdb/matdb "
                                  + "(FsaSampleLibrary, --lib=sample)", manifestPath, compositions.Count);
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

            int definitionsTotal = 0;
            foreach (string file in files)
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
                    // Состав спектра — из манифеста, линии — из базы, общим входом
                    // приложения (`T257`); набор `null` нарочно: список уже свой,
                    // и `HideUnknownPeaks` чужого набора вычеркнул бы пики (см.
                    // `FsaSampleLibrary.AsDefinitions`).
                    Composition composition = compositions[name];
                    FsaSampleSpec spec = FsaSampleSpec.FromManifest(
                        rd, composition.Chains, composition.Nuclides, true, true);
                    FsaCalculationOptions.Of(rd).ApplyTo(spec);
                    List<NuclideDefinition> definitions =
                        FsaSampleLibrary.AsDefinitions(FsaSampleLibrary.Build(spec));
                    definitionsTotal += definitions.Count;
                    peaks = new PeakDetector().DetectPeak(
                        rd, BackgroundMode.Invisible, SmoothingMethod.None,
                        null, definitions);
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
            Console.WriteLine("группа {0}: пиков {1}, подписаны нуклидом {2} (определений из базы {5}),"
                              + " объяснены устройством {3} ({4:F1} %)",
                              group == "" ? "(без имени)" : group, totalPeaks, totalNamed,
                              explained, totalPeaks > 0 ? 100.0 * explained / totalPeaks : 0.0,
                              definitionsTotal);
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
            return SuppliedLibraryGate(0);
        }

        /// <summary>
        /// (`AMBER19`, П11/П19) Вторая дверь гейта «состав только из базы» — та
        /// же, что у `CorpusFsaProbe.RefuseIfManagerRaised`: счётчик обращений
        /// к поставочному менеджеру печатается ВСЕГДА, и не ноль — код 12 поверх
        /// любого итога. Считать по `isLoaded` нельзя: безоконный подъём без
        /// файла бросает, и по нему подъёма не видно (`S100`).
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

        /// <summary>Объявленный состав одного спектра — словами манифеста.</summary>
        sealed class Composition
        {
            public readonly List<string> Chains = new List<string>();
            public readonly List<string> Nuclides = new List<string>();

            public Composition(IEnumerable<string> chains, IEnumerable<string> nuclides)
            {
                foreach (string s in chains) if (s.Length > 0) Chains.Add(s);
                foreach (string s in nuclides) if (s.Length > 0) Nuclides.Add(NucidOf(s));
            }

            /// <summary>«Cs-137» → «137CS»: nucid, как его зовёт nucdb; nucid как есть.</summary>
            static string NucidOf(string label)
            {
                string nucid = FsaSampleLibrary.NucidOf(label);
                return string.IsNullOrEmpty(nucid) ? label : nucid;
            }
        }

        /// <summary>
        /// Состав каждого спектра каталога из `manifest.csv` (колонки `key`,
        /// `chains`, `nuclides`; разделитель внутри колонки — `;`). Читается
        /// так же, как в `CorpusFsaProbe.ReadTruth`: неизвестная метка ряда,
        /// спектр без строки или с пустым составом — отказ ДО прогона, с
        /// перечислением; вернуть <c>null</c> значит «не мерить».
        /// </summary>
        static Dictionary<string, Composition> ReadManifest(string path, string[] files)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("нет {0} — состав спектров брать неоткуда "
                                        + "(--manifest=<файл> или --sample=/--chain=)", path);
                return null;
            }

            var byKey = new Dictionary<string, Composition>(StringComparer.Ordinal);
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 0)
            {
                Console.Error.WriteLine("{0} пуст", path);
                return null;
            }

            List<string> head = SplitCsv(lines[0].TrimStart('﻿'));
            int iKey = head.IndexOf("key"), iChains = head.IndexOf("chains"),
                iNuclides = head.IndexOf("nuclides");
            if (iKey < 0 || iChains < 0 || iNuclides < 0)
            {
                Console.Error.WriteLine("{0}: нет колонок key/chains/nuclides", path);
                return null;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                List<string> cells = SplitCsv(lines[i]);
                if (cells.Count <= Math.Max(iKey, Math.Max(iChains, iNuclides))) continue;
                var chains = cells[iChains].Split(';');
                foreach (string label in chains)
                {
                    if (label.Length > 0 && FsaSampleChain.FromLabel(label) == null)
                    {
                        Console.Error.WriteLine("манифест: неизвестный ряд '{0}' у {1}; известные: {2}",
                                                label, cells[iKey],
                                                string.Join(", ", FsaSampleChain.KnownLabels));
                        return null;
                    }
                }

                byKey[cells[iKey]] = new Composition(chains, cells[iNuclides].Split(';'));
            }

            var silent = new List<string>();
            var result = new Dictionary<string, Composition>(StringComparer.Ordinal);
            foreach (string file in files)
            {
                string key = Path.GetFileNameWithoutExtension(file);
                Composition c;
                if (!byKey.TryGetValue(key, out c))
                    silent.Add(key);
                else if (c.Chains.Count == 0 && c.Nuclides.Count == 0)
                    silent.Add(key + " (состав пуст)");
                else
                    result[key] = c;
            }

            if (silent.Count > 0)
            {
                Console.Error.WriteLine("в манифесте нет состава для: " + string.Join(", ", silent));
                return null;
            }

            return result;
        }

        static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }

            cells.Add(sb.ToString());
            return cells;
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
            // ПРИБОР И ЕГО НАСТРОЙКИ ПОИСКА — ОБЩИМ ПРАВИЛОМ (`S82`, `T59`).
            //
            // ⛔ Прежде здесь стояла своя проверка «а нет ли настроек у прибора», и
            // она НЕ РАБОТАЛА НИКОГДА: `ResultData.DeviceConfig` помечен `[XmlIgnore]`
            // и заведён полем `= new DeviceConfigInfo()`, то есть после чтения файла
            // там лежит ПУСТОЙ прибор — не null, поэтому условие проходило, а настроек
            // в нём не было, и проба молча брала умолчания библиотеки (SNR 10 вместо
            // приборных 4, диапазон от 30 кэВ вместо 15…20). Настройки поиска задают
            // подписи пиков, подписи — состав библиотеки (`S57`), состав — разложение:
            // картинка получалась про НЕ ТОТ спектр, который видит человек.
            //
            // Отказ печатается, а не глотается: молчаливый откат на умолчания и есть
            // то, чем `S82` стоила двух сессий.
            // ⚠ Печатается ТОЛЬКО отказ: эта проба обходит весь корпус, и
            // строка «прибор такой-то» на каждый из 126 спектров утопила бы
            // в себе тот единственный случай, ради которого печать и нужна.
            // У однопрогонных проб наоборот — там имя прибора печатается
            // всегда, потому что читатель смотрит именно на этот спектр.
            string deviceNote = ProbeDeviceConfig.Attach(rd);
            if (deviceNote.Contains("НЕТ") || deviceNote.Contains("нет"))
            {
                Console.Error.WriteLine("⚠ " + Path.GetFileNameWithoutExtension(path)
                                        + ": " + deviceNote);
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
