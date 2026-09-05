using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace LabelTruthProbe
{
    /// <summary>
    /// (`S134`, `S64`) ЧТО ИМЕННО НАПИСАНО НАД ПИКАМИ КОРПУСА — выгрузка каждой
    /// подписи с промахом, разрешением и выходом линии, плюс прямой опыт над
    /// самим отбором.
    ///
    /// Зачем отдельно от `S109ActivityProbe`. Та проба меряет ЧИСЛО беккерелей
    /// и всё, что его отменяет; здесь мерится ПОДПИСЬ — то, что человек читает
    /// с графика и из списка пиков даже тогда, когда никакого числа ему не
    /// показали. Ровно на этом разошлись `S99` и `S134`: порог 1 % убрал у
    /// пятнадцати пиков активность, а метку «Pu-238» оставил.
    ///
    /// ⛔ ПОРОГИ ЧИТАЮТСЯ ИЗ СОБРАННОЙ СБОРКИ (`GetRawConstantValue`), а не
    /// переписываются сюда: копия константы вкомпилировалась бы намертво и
    /// продолжала бы мерить старое число после смены порога. Сборка ДО правки
    /// констант не содержит — это НЕ ошибка, проба так и печатает («порогов в
    /// сборке нет»), и та же проба годится обоим плечам замера.
    ///
    /// ⛔ ИМЁН НУКЛИДОВ ЗДЕСЬ НЕТ И БЫТЬ НЕ ДОЛЖНО. Что реально лежало под
    /// детектором — свойство корпуса, оно живёт в `corpus/manifest.csv` и
    /// разбирается снаружи (`tools/CORPUS/scripts/c1/label_score.py`). Проба
    /// отдаёт только измеренное: пик, подпись, промах, ПШПВ, выход.
    ///
    ///     LabelTruthProbe --spectra=&lt;…\CORPUS\corpus\spectra&gt; [--csv=labels.csv]
    ///     LabelTruthProbe --selftest
    ///
    /// `--selftest` — ПОЛОЖИТЕЛЬНЫЙ И ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ отбора: пять
    /// подставных входов подаются прямо в `PeakDetector.MatchNuclide`
    /// отражением, с подставной же библиотекой из одной-двух линий. Прогон по
    /// корпусу отвечает «сколько», опыт — «почему»: он показывает, что заведомо
    /// ложный вход отвергнут ИМЕННО тем окном, которым должен, а честный —
    /// принят.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            string spectraDir = null;
            string csvPath = "labels.csv";
            string rivalsPath = null;
            bool selftest = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectraDir = a.Substring(10);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csvPath = a.Substring(6);
                else if (a.StartsWith("--rivals=", StringComparison.Ordinal)) rivalsPath = a.Substring(9);
                else if (a == "--selftest") selftest = true;
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            PrintGates();

            if (selftest)
            {
                return SelfTest();
            }

            if (spectraDir == null || !Directory.Exists(spectraDir))
            {
                Console.Error.WriteLine("нет каталога спектров: {0}", spectraDir ?? "(не задан)");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            NuclideSet set = nuclides.ActiveSet;

            Console.WriteLine("библиотека: {0} записей, набор «{1}»",
                              nuclides.NuclideDefinitions != null ? nuclides.NuclideDefinitions.Count : -1,
                              set != null ? set.Name : "(нет)");
            Console.WriteLine();

            var csv = new StringBuilder();
            // ⛔ ДВЕ КОЛОНКИ ПШПВ, И ЭТО НЕ ИЗБЫТОК. `Peak.FWHM` приходит от
            //    финдера В КАНАЛАХ (`FWHMPeakDetector.Spectrum` строит рёбра
            //    как `bin_edges[i] = i`), и так её и читают потребители
            //    приложения — `DCPeakDetectionView` подставляет её в
            //    `ChannelToEnergy(Channel ± FWHM/2)`, `FwhmCalibration` делит
            //    её на кратность склейки. Промах же линии меряется В КЭВ.
            //    Делить одно на другое нельзя, поэтому кэВ считаются ТЕМ ЖЕ
            //    выражением, что и в приложении, и печатаются отдельно.
            // ⚠ ДВЕ ПОСЛЕДНИЕ КОЛОНКИ — ПОЛОСА ПОИСКА В КЭВ (`A197`, полоса O1).
            //   Правилу «у победителя обязана быть ещё одна СВОЯ линия» нужно
            //   знать, какие линии прибор вообще МОГ увидеть: требовать линию
            //   2614 кэВ от спектра, кончающегося на 1500, — значит снять
            //   законную подпись за недостижимую улику. Считается ровно так,
            //   как её строит `PeakDetector.PeakFinder`: настройки в кэВ ->
            //   каналы -> обрезка по числу каналов -> обратно в кэВ.
            csv.AppendLine("spectrum,peak_kev,peak_counts,snr,fwhm_ch,fwhm_kev,nuclide,line_kev,"
                           + "intensity_pct,miss_kev,miss_fwhm,tol_pct,range_min_kev,range_max_kev");

            // ⚠ Соперники — материал `S64`, а не `S134`: строка на КАЖДУЮ
            //   видимую линию, попавшую в пик. Окно берётся широкое (2 ПШПВ),
            //   узкое (полПШПВ, как у `ScanActivityRivals`) нарезается снаружи:
            //   иначе всякий вопрос «а при другом окне?» стоил бы прогона.
            var rivals = rivalsPath == null ? null : new StringBuilder();
            if (rivals != null)
            {
                rivals.AppendLine("spectrum,peak_kev,fwhm_kev,winner,cand_name,cand_kev,"
                                  + "cand_intensity_pct,cand_miss_kev,cand_miss_fwhm");
            }
            const double RivalWindowFwhm = 2.0;

            int spectra = 0, failed = 0, peaksTotal = 0, labelled = 0;
            foreach (string file in Directory.GetFiles(spectraDir, "*.xml")
                                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                ResultData rd;
                List<Peak> peaks;
                try
                {
                    rd = LoadResult(file);
                    peaks = new PeakDetector().DetectPeak(
                        rd, BackgroundMode.Invisible, SmoothingMethod.None,
                        set, nuclides.NuclideDefinitions);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("{0}: {1}", name, e.Message);
                    failed++;
                    continue;
                }

                spectra++;
                if (peaks == null) peaks = new List<Peak>();
                double tol = ((FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig).Tolerance;

                EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
                double rangeMin, rangeMax;
                SearchRangeKev(rd.EnergySpectrum,
                               (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig,
                               out rangeMin, out rangeMax);

                foreach (Peak peak in peaks.OrderBy(p => p.Energy))
                {
                    peaksTotal++;
                    NuclideDefinition nd = peak.Nuclide;
                    if (nd != null) labelled++;
                    double fwhmKev = FwhmKev(peak, cal);
                    double miss = nd == null ? Double.NaN : Math.Abs(peak.Energy - nd.Energy);
                    double missFwhm = nd == null || !(fwhmKev > 0.0) ? Double.NaN : miss / fwhmKev;

                    csv.AppendLine(string.Join(",",
                        name,
                        F(peak.Energy, "F3"), F(peak.Count, "F1"), F(peak.SNR, "F3"),
                        F(peak.FWHM, "F3"), F(fwhmKev, "F3"),
                        nd == null ? "" : nd.Name.Replace(',', ';'),
                        nd == null ? "" : F(nd.Energy, "F3"),
                        nd == null ? "" : F(nd.Intencity, "G6"),
                        F(miss, "F3"), F(missFwhm, "F4"),
                        F(tol, "G6"), F(rangeMin, "F3"), F(rangeMax, "F3")));

                    if (rivals != null && fwhmKev > 0.0)
                    {
                        double window = RivalWindowFwhm * fwhmKev;
                        foreach (NuclideDefinition cand in nuclides.NuclideDefinitions)
                        {
                            if (cand == null || !cand.Visible || cand.Energy == 0.0) continue;
                            if (set != null && (cand.Sets == null || !cand.Sets.Contains(set.Id))) continue;
                            double cmiss = Math.Abs(peak.Energy - cand.Energy);
                            if (cmiss > window) continue;
                            rivals.AppendLine(string.Join(",",
                                name, F(peak.Energy, "F3"), F(fwhmKev, "F3"),
                                nd == null ? "" : nd.Name.Replace(',', ';'),
                                cand.Name.Replace(',', ';'), F(cand.Energy, "F3"),
                                F(cand.Intencity, "G6"), F(cmiss, "F3"), F(cmiss / fwhmKev, "F4")));
                        }
                    }
                }
            }

            File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));
            if (rivals != null)
            {
                File.WriteAllText(rivalsPath, rivals.ToString(), new UTF8Encoding(false));
                Console.WriteLine("соперники (окно {0} ПШПВ) -> {1}",
                                  RivalWindowFwhm.ToString("G4", CultureInfo.InvariantCulture), rivalsPath);
            }
            Console.WriteLine("спектров разобрано {0}, отказало {1}; пиков {2}, из них с подписью {3} -> {4}",
                              spectra, failed, peaksTotal, labelled, csvPath);
            return failed > 0 ? 1 : 0;
        }

        // ------------------------------------------------------------------
        // Пороги отбора — ИЗ СБОРКИ
        // ------------------------------------------------------------------

        static double? Gate(string field)
        {
            FieldInfo f = typeof(PeakDetector).GetField(field, BindingFlags.Public | BindingFlags.Static);
            if (f == null) return null;
            return Convert.ToDouble(f.GetRawConstantValue(), CultureInfo.InvariantCulture);
        }

        static void PrintGates()
        {
            double? y = Gate("MinimumLabelYieldPercent");
            double? w = Gate("MaximumLabelMissInFwhm");
            if (y == null && w == null)
            {
                Console.WriteLine("⚠ порогов подписи в сборке НЕТ — это плечо ДО правки (`S134`)");
            }
            else
            {
                Console.WriteLine("пороги подписи ИЗ СБОРКИ: выход ≥ {0} %, промах ≤ {1} ПШПВ пика",
                                  y == null ? "(нет)" : y.Value.ToString("G6", CultureInfo.InvariantCulture),
                                  w == null ? "(нет)" : w.Value.ToString("G6", CultureInfo.InvariantCulture));
            }
        }

        // ------------------------------------------------------------------
        // Опыт над самим отбором
        // ------------------------------------------------------------------

        /// <summary>
        /// Пять подставных входов прямо в `MatchNuclide`. Библиотека у каждого
        /// СВОЯ и состоит из названных линий — так видно, каким именно окном
        /// отвергнут вход, а не «отвергнут вообще».
        ///
        /// ⚠ Ожидание записано ЗДЕСЬ и сравнивается машинно, иначе опыт
        /// превращается в распечатку, которую всякий читает как хочет.
        /// Ожидание сказано для сборки С ПРАВКОЙ; на сборке без неё проба
        /// печатает исход и НЕ судит (`код 0`), потому что судить там нечего —
        /// порогов в сборке нет.
        /// </summary>
        static int SelfTest()
        {
            bool patched = Gate("MinimumLabelYieldPercent") != null;
            var det = new PeakDetector();
            FieldInfo fDefs = typeof(PeakDetector).GetField("nuclideDefinitions",
                                                            BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo mMatch = typeof(PeakDetector).GetMethod("MatchNuclide",
                                                               BindingFlags.NonPublic | BindingFlags.Instance);
            if (fDefs == null || mMatch == null)
            {
                Console.Error.WriteLine("⛔ отбор подписи не найден отражением (nuclideDefinitions={0}, MatchNuclide={1})",
                                        fDefs != null, mMatch != null);
                return 3;
            }

            var cases = new[]
            {
                // Заведомо ЛОЖНЫЙ вход: ничтожный выход и промах больше пика.
                // Ровно случай `G1S16_Ce139_P25` из `S134`.
                Case("ничтожный выход + промах 1.76 ПШПВ", 162.729, 6.091, null,
                     Line("Плутоний-мнимый", 152.0, 0.0009)),
                // Тот же ничтожный выход, но линия ВНУТРИ пика: отвергнуть
                // обязан порог по выходу, а не по промаху.
                Case("ничтожный выход, промах 0.04 ПШПВ", 152.24, 6.091, null,
                     Line("Плутоний-мнимый", 152.0, 0.0009)),
                // Честный выход, линия внутри пика — ПРИНЯТЬ.
                Case("честный выход, промах 0.02 ПШПВ", 662.5, 40.0, "Цезий-мнимый",
                     Line("Цезий-мнимый", 661.657, 85.1)),
                // Честный выход, но промах 11 ПШПВ (германий, `HPGeGEM_Ra226`).
                Case("честный выход, промах 11 ПШПВ", 1052.285, 4.653, null,
                     Line("Протактиний-мнимый", 1001.0, 0.842)),
                // Выход НЕ ПРОСТАВЛЕН (0) — порогом по выходу не судится.
                Case("выход не проставлен, промах 0.05 ПШПВ", 511.5, 10.0, "Образ-мнимый",
                     Line("Образ-мнимый", 511.0, 0.0)),
                // Спор двух линий внутри одного пика: побеждает ближайшая —
                // это НЕ чинится здесь и служит опорой для `S64`.
                Case("две линии в одном пике", 58.57, 10.0, "Рентген-мнимый",
                     Line("Рентген-мнимый", 59.318, 57.6), Line("Америций-мнимый", 59.541, 35.9)),
            };

            // ⛔ Ширина подаётся ОТДЕЛЬНЫМ доводом и в кэВ — ровно так, как её
            //    считает `CollectPeaks`. Пробовать через `Peak.FWHM` нельзя:
            //    она в КАНАЛАХ, и опыт мерил бы не то, что приложение.
            //    Число доводов проверяется у самого метода: сборка без правки
            //    берёт три, с правкой — четыре.
            int argc = mMatch.GetParameters().Length;
            int bad = 0;
            Console.WriteLine();
            Console.WriteLine("{0,-38} {1,10} {2,8} {3,-20} {4,-20} {5}",
                              "вход", "пик, кэВ", "ПШПВ кэВ", "ожидание", "получено", "");
            foreach (var c in cases)
            {
                fDefs.SetValue(det, c.Library);
                // ПШПВ в каналах у подставного пика равна кэВ: калибровка
                // опыта — тождественная, и второй величины здесь не заведено.
                var peak = new Peak { Energy = c.PeakKev, FWHM = c.Fwhm };
                object[] argv = argc >= 4
                    ? new object[] { peak, 10.0, null, c.Fwhm }
                    : new object[] { peak, 10.0, null };
                var got = (NuclideDefinition)mMatch.Invoke(det, argv);
                string gotName = got == null ? "(нет подписи)" : got.Name;
                string wantName = c.Expect ?? "(нет подписи)";
                bool ok = gotName == wantName;
                if (patched && !ok) bad++;
                Console.WriteLine("{0,-38} {1,10:F3} {2,8:F3} {3,-20} {4,-20} {5}",
                                  c.Title, c.PeakKev, c.Fwhm, wantName, gotName,
                                  patched ? (ok ? "✓" : "⛔ РАСХОЖДЕНИЕ") : "(сборка без правки)");
            }

            Console.WriteLine();
            if (!patched)
            {
                Console.WriteLine("сборка без правки — исходы напечатаны, приговор не выносится");
                return 0;
            }
            bad += SelfTestConfirm();
            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "опыт сошёлся полностью" : ("расхождений: " + bad));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// (`A196`, `A197`) ОПЫТ НАД ПРАВИЛАМИ, КОТОРЫМ НУЖЕН ВЕСЬ СПЕКТР —
        /// подтверждение слабой линии и приборная подпись суммы 511+511.
        /// Подставные пики с подставной библиотекой подаются прямо в
        /// <c>PeakDetector.ConfirmLabels</c> отражением.
        ///
        /// ⚠ Опыт держит ОБА конца: на каждый вход, который правило обязано
        /// отвергнуть, есть близнец, который оно обязано пропустить. Правило,
        /// отвергающее всё, отличается от полезного только по таким парам.
        /// Калибровка тождественная (1 кэВ на канал), поэтому ПШПВ в каналах и
        /// в кэВ совпадают и опыт не спорит с единицами (см. `S134` §1).
        /// </summary>
        static int SelfTestConfirm()
        {
            MethodInfo mConfirm = typeof(PeakDetector).GetMethod(
                "ConfirmLabels", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo fDefs = typeof(PeakDetector).GetField(
                "nuclideDefinitions", BindingFlags.NonPublic | BindingFlags.Instance);
            if (mConfirm == null || fDefs == null)
            {
                Console.WriteLine("⚠ правил по составу спектра в сборке НЕТ — опыт над ними не ставится");
                return 0;
            }

            var cases = new List<ConfirmCase>
            {
                // --- подтверждение слабой линии (`A197`) ---
                Confirm("слабая линия, второй своей НЕ видно", 1500,
                        new[] { Line("Икс", 1001.0, 0.842), Line("Икс", 766.0, 0.317) },
                        new[] { P(1001.0, 60, 20, "Икс", 1001.0), P(1275.0, 70, 40, null, 0) },
                        new[] { "(нет)", "(нет)" }),
                Confirm("слабая линия, вторая своя ВИДНА", 1500,
                        new[] { Line("Икс", 1001.0, 0.842), Line("Икс", 766.0, 0.317) },
                        new[] { P(1001.0, 60, 20, "Икс", 1001.0), P(770.0, 60, 15, null, 0) },
                        new[] { "Икс@1001", "(нет)" }),
                Confirm("ЯРКАЯ линия, второй своей не видно", 1500,
                        new[] { Line("Игрек", 911.0, 25.8), Line("Игрек", 969.0, 15.8) },
                        new[] { P(911.0, 40, 30, "Игрек", 911.0) },
                        new[] { "Игрек@911" }),
                Confirm("слабая линия, других своих НЕТ вовсе", 1500,
                        new[] { Line("Зет", 1001.0, 0.842) },
                        new[] { P(1001.0, 60, 20, "Зет", 1001.0) },
                        new[] { "Зет@1001" }),
                Confirm("слабая линия, вторая своя ВНЕ полосы поиска", 1500,
                        new[] { Line("Дубль", 1001.0, 0.842), Line("Дубль", 2614.0, 35.8) },
                        new[] { P(1001.0, 60, 20, "Дубль", 1001.0) },
                        new[] { "Дубль@1001" }),
                // --- приборная подпись суммы 511+511 (`A196`) ---
                Confirm("сумма 511+511: пик 511 есть и он заметнее", 3000,
                        new[] { Line("Образ", 511.0, 0.0), Line("Нуклид", 1001.0, 0.842),
                                Line("Нуклид", 766.0, 0.317) },
                        new[] { P(511.0, 40, 300, "Образ", 511.0), P(1002.0, 90, 16, "Нуклид", 1001.0) },
                        new[] { "Образ@511", "Образ@1022" }),
                Confirm("суммы нет: пика 511 в спектре НЕТ", 3000,
                        new[] { Line("Образ", 511.0, 0.0), Line("Нуклид", 1001.0, 0.842),
                                Line("Нуклид", 766.0, 0.317) },
                        new[] { P(1002.0, 90, 16, "Нуклид", 1001.0), P(766.0, 60, 15, null, 0) },
                        new[] { "Нуклид@1001", "(нет)" }),
                Confirm("суммы нет: пик 1022 ЗАМЕТНЕЕ пика 511", 3000,
                        new[] { Line("Образ", 511.0, 0.0), Line("Нуклид", 1001.0, 0.842),
                                Line("Нуклид", 766.0, 0.317) },
                        new[] { P(511.0, 40, 10, "Образ", 511.0), P(1002.0, 90, 500, "Нуклид", 1001.0),
                                P(766.0, 60, 15, null, 0) },
                        new[] { "Образ@511", "Нуклид@1001", "(нет)" })
            };

            Console.WriteLine();
            Console.WriteLine("ОПЫТ НАД ПРАВИЛАМИ ПО СОСТАВУ СПЕКТРА (`A196`, `A197`):");
            int bad = 0;
            foreach (ConfirmCase c in cases)
            {
                var det = new PeakDetector();
                fDefs.SetValue(det, c.Library);
                // ⛔ ПОДПИСЬ ПИКА — ЗАПИСЬ ЭТОЙ ЖЕ БИБЛИОТЕКИ, а не её двойник:
                //    правило судит по ВЫХОДУ подписи, и двойник с нулевым
                //    выходом проскакивал бы мимо порога, ничего не измеряя.
                foreach (Peak p in c.Peaks)
                {
                    if (p.Nuclide == null) continue;
                    NuclideDefinition real = c.Library.FirstOrDefault(
                        d => d.Name == p.Nuclide.Name && Math.Abs(d.Energy - p.Nuclide.Energy) < 1e-9);
                    if (real == null)
                    {
                        Console.Error.WriteLine("⛔ опыт «{0}»: подписи {1}@{2} нет в подставной библиотеке",
                                                c.Title, p.Nuclide.Name, p.Nuclide.Energy);
                        return 1;
                    }
                    p.Nuclide = real;
                }
                var spectrum = new EnergySpectrum(1.0, 4096);
                spectrum.EnergyCalibration = new PolynomialEnergyCalibration();
                var cfg = new FWHMPeakDetectionMethodConfig();
                cfg.Min_Range = 20.0;
                cfg.Max_Range = c.MaxRangeKev;
                mConfirm.Invoke(det, new object[] { c.Peaks, spectrum, cfg, null });

                var got = c.Peaks.Select(p => p.Nuclide == null
                              ? "(нет)"
                              : p.Nuclide.Name + "@" + p.Nuclide.Energy.ToString("G6", CultureInfo.InvariantCulture))
                          .ToArray();
                bool ok = got.Length == c.Expect.Length;
                for (int i = 0; ok && i < got.Length; i++) ok = got[i] == c.Expect[i];
                if (!ok) bad++;
                Console.WriteLine("  {0,-42} ждали [{1}] получили [{2}] {3}",
                                  c.Title, string.Join(" ", c.Expect), string.Join(" ", got),
                                  ok ? "✓" : "⛔ РАСХОЖДЕНИЕ");
            }
            return bad;
        }

        class ConfirmCase
        {
            public string Title;
            public double MaxRangeKev;
            public List<NuclideDefinition> Library;
            public List<Peak> Peaks;
            public string[] Expect;
        }

        static ConfirmCase Confirm(string title, double maxRangeKev, NuclideDefinition[] lib,
                                   Peak[] peaks, string[] expect)
        {
            return new ConfirmCase
            {
                Title = title,
                MaxRangeKev = maxRangeKev,
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

        class TestCase
        {
            public string Title;
            public double PeakKev;
            public double Fwhm;
            public string Expect;
            public List<NuclideDefinition> Library;
        }

        static TestCase Case(string title, double peakKev, double fwhm, string expect,
                             params NuclideDefinition[] lib)
        {
            return new TestCase
            {
                Title = title,
                PeakKev = peakKev,
                Fwhm = fwhm,
                Expect = expect,
                Library = lib.ToList()
            };
        }

        /// <summary>
        /// Подставная линия. Имена НАРОЧНО не настоящие: проба не должна
        /// утверждать ничего о конкретном нуклиде, она мерит правило отбора.
        /// </summary>
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

        /// <summary>
        /// ПШПВ пика В КЭВ. `Peak.FWHM` живёт В КАНАЛАХ — финдер строит рёбра
        /// как `bin_edges[i] = i`, — и переводится тем же выражением, каким её
        /// читает приложение (`DCPeakDetectionView`: `ChannelToEnergy(Channel ±
        /// FWHM/2)`). Своего пересчёта «умножить на кэВ-на-канал» здесь нет
        /// нарочно: калибровка нелинейна, и половинки растягиваются по-разному.
        /// </summary>
        /// <summary>
        /// Полоса поиска пиков В КЭВ — тем же построением, что у
        /// <c>PeakDetector.PeakFinder</c>: настройки задают её в кэВ, финдер
        /// переводит их в каналы и ОБРЕЗАЕТ по числу каналов спектра, поэтому
        /// у спектра на 1024 канала с калибровкой до 1500 кэВ верх полосы —
        /// 1500, а не записанные в приборе 3000.
        ///
        /// ⚠ Повтор четырёх строк приложения здесь НАРОЧЕН: проба обязана
        /// считаться и на сборке БЕЗ правки, где этого расчёта ещё нет, — иначе
        /// плечо «до» пришлось бы мерить другой пробой.
        /// </summary>
        static void SearchRangeKev(EnergySpectrum spectrum, FWHMPeakDetectionMethodConfig cfg,
                                   out double minKev, out double maxKev)
        {
            EnergyCalibration cal = spectrum.EnergyCalibration;
            int n = spectrum.NumberOfChannels;
            int lo = Convert.ToInt32(cal.EnergyToChannel(cfg.Min_Range, maxChannels: n));
            int hi = Convert.ToInt32(cal.EnergyToChannel(cfg.Max_Range, maxChannels: n));
            lo = Math.Max(0, Math.Min(n - 1, lo));
            hi = Math.Max(0, Math.Min(n - 1, hi));
            if (hi < lo) { int swap = lo; lo = hi; hi = swap; }
            minKev = cal.ChannelToEnergy(lo);
            maxKev = cal.ChannelToEnergy(hi);
        }

        static double FwhmKev(Peak peak, EnergyCalibration cal)
        {
            if (cal == null || !(peak.FWHM > 0.0) || Double.IsNaN(peak.FWHM))
            {
                return 0.0;
            }
            return Math.Abs(cal.ChannelToEnergy(peak.Channel + peak.FWHM / 2.0)
                            - cal.ChannelToEnergy(peak.Channel - peak.FWHM / 2.0));
        }

        static string F(double v, string fmt)
        {
            return double.IsNaN(v) || double.IsInfinity(v)
                ? ""
                : v.ToString(fmt, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Чтение спектра корпуса — тем же порядком, что у `S109ActivityProbe`:
        /// прибор и его настройки поиска подставляет `ProbeDeviceConfig` (`S82`),
        /// иначе поиск идёт с умолчаниями библиотеки и подписи выходят не те.
        /// </summary>
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

            string deviceNote = ProbeDeviceConfig.Attach(rd);
            if (deviceNote.Contains("НЕТ") || deviceNote.Contains("нет"))
            {
                Console.Error.WriteLine("⚠ " + Path.GetFileNameWithoutExtension(path) + ": " + deviceNote);
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
    }
}
