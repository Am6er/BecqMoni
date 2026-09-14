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
    ///     …  [--culture=ru-RU|en-US] [--expect-sum=&lt;образец&gt;]
    ///
    /// `--culture=` задаёт ЯЗЫК ПРИЛОЖЕНИЯ — тем же полем настройки, каким его
    /// задаёт меню языка (`GlobalConfigInfo.Language`), а не только культурой
    /// потока: подпись собирается в рабочем потоке, и культура потока ей не
    /// указ (`A229`). Без ключа берётся «» — первичные английские ресурсы,
    /// чтобы выгрузка не зависела от языка Windows, на которой её считали.
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
        /// <summary>
        /// Язык приложения на этот прогон. «» — первичные английские ресурсы;
        /// умолчание нарочно НЕ «как в настройке» и не «как у Windows»: иначе
        /// выгрузка на русской машине и на английской различалась бы текстом
        /// приборной подписи, а сравнивать плечи стало бы нечем.
        /// </summary>
        static string language = "";

        /// <summary>
        /// ⛔ ТОЛЬКО ДЛЯ ПОЛОЖИТЕЛЬНОГО КОНТРОЛЯ. Подменяет ОЖИДАНИЕ, а не то,
        /// что считает приложение: прогон с заведомо неверным образцом обязан
        /// ОТКАЗАТЬ. Ключ ничего не ослабляет — без него ожидание берётся из
        /// таблицы ниже и проверка идёт ВСЕГДА.
        /// </summary>
        static string expectSumOverride;

        /// <summary>
        /// ⛔ ТОЛЬКО ДЛЯ ПОЛОЖИТЕЛЬНОГО КОНТРОЛЯ (`S64`). Подменяет ОЖИДАНИЕ
        /// первого опыта над списком кандидатов, а не то, что считает
        /// приложение: прогон с заведомо неверным образцом обязан ОТКАЗАТЬ.
        /// Без ключа ожидание берётся из таблицы опытов и сверяется всегда.
        /// </summary>
        static string expectCandidatesOverride;

        /// <summary>
        /// ⛔ ТОЛЬКО ДЛЯ ПОЛОЖИТЕЛЬНОГО КОНТРОЛЯ (`A227`). Подменяет ОЖИДАНИЕ
        /// опыта «ЯРКАЯ линия, промах 0.5 ПШПВ, второй своей нет», а не то, что
        /// считает приложение: прогон с заведомо неверным образцом обязан
        /// ОТКАЗАТЬ. Без ключа ожидание берётся из таблицы опытов.
        /// </summary>
        static string expectFarOverride;

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
                else if (a.StartsWith("--culture=", StringComparison.Ordinal)) language = a.Substring(10);
                else if (a.StartsWith("--expect-sum=", StringComparison.Ordinal)) expectSumOverride = a.Substring(13);
                else if (a.StartsWith("--expect-candidates=", StringComparison.Ordinal)) expectCandidatesOverride = a.Substring(20);
                else if (a.StartsWith("--expect-far=", StringComparison.Ordinal)) expectFarOverride = a.Substring(13);
                else if (a == "--selftest") selftest = true;
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            PrintGates();
            ApplyLanguage();

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
            // ⚠ ТРИ ПОСЛЕДНИЕ КОЛОНКИ — СПИСОК КАНДИДАТОВ (`S64`, полоса O21).
            //   `nuclide` осталась ИМЕНЕМ ПОБЕДИТЕЛЯ и ничем иным: по ней
            //   считается разряд подписи (`label_score.py`), и положи сюда
            //   склейку — мерка стала бы засчитывать ИСТИНУ по любому из имён,
            //   то есть мерить не то, что показано. `candidates` — все имена
            //   через «|» (свой разделитель, чтобы не спорить с тем, который
            //   выбран для человека), `label_text` — надпись КАК НА ЭКРАНЕ,
            //   `label_token` — лексема нуклида из неё
            //   (`NuclideDefinition.NuclideNameOf`), та самая, по которой
            //   `FsaCompositionInference` и `FsaLibrary` читают состав.
            csv.AppendLine("spectrum,peak_kev,peak_counts,snr,fwhm_ch,fwhm_kev,nuclide,line_kev,"
                           + "intensity_pct,miss_kev,miss_fwhm,tol_pct,range_min_kev,range_max_kev,"
                           + "candidates,label_text,label_token");

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

            // (`A229`) Материал приговора по ИМЕНАМ образа аннигиляции. Копится
            // тут же, чтобы приговор шёл по тем же строкам, что и выгрузка, а
            // не по перечитанному файлу.
            var labelRows = new List<LabelRow>();

            int spectra = 0, failed = 0, peaksTotal = 0, labelled = 0;
            // (`S64`) Сколько подписей несут больше одного имени и каков самый
            // длинный список — заглавные числа правки, поэтому считаются здесь
            // же, тем же проходом, что и выгрузка.
            int multiName = 0, longestList = 0, tokenMismatch = 0;
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

                    List<NuclideDefinition> cands = CandidatesOf(peak);
                    string labelText = LabelTextOf(peak);
                    string labelToken = NuclideDefinition.NuclideNameOf(labelText);
                    if (cands.Count > 1) multiName++;
                    if (cands.Count > longestList) longestList = cands.Count;
                    // ⛔ ЛЕКСЕМА НАДПИСИ ОБЯЗАНА СОВПАСТЬ С ЛЕКСЕМОЙ ПОБЕДИТЕЛЯ.
                    //    Это и есть проверка, что разбор состава не поехал: по
                    //    ней читают нуклид `FsaCompositionInference` и
                    //    `FsaLibrary`, и разойдись она с победителем — состав
                    //    сменился бы молча.
                    if (nd != null && !string.Equals(labelToken,
                                                     NuclideDefinition.NuclideNameOf(nd.Name),
                                                     StringComparison.Ordinal))
                    {
                        tokenMismatch++;
                    }

                    csv.AppendLine(string.Join(",",
                        name,
                        F(peak.Energy, "F3"), F(peak.Count, "F1"), F(peak.SNR, "F3"),
                        F(peak.FWHM, "F3"), F(fwhmKev, "F3"),
                        nd == null ? "" : nd.Name.Replace(',', ';'),
                        nd == null ? "" : F(nd.Energy, "F3"),
                        nd == null ? "" : F(nd.Intencity, "G6"),
                        F(miss, "F3"), F(missFwhm, "F4"),
                        F(tol, "G6"), F(rangeMin, "F3"), F(rangeMax, "F3"),
                        Names(cands).Replace(',', ';'),
                        labelText.Replace(',', ';'),
                        labelToken.Replace(',', ';')));

                    if (nd != null)
                    {
                        labelRows.Add(new LabelRow
                        {
                            Spectrum = name,
                            PeakKev = peak.Energy,
                            Name = nd.Name,
                            LineKev = nd.Energy,
                            Intensity = nd.Intencity
                        });
                    }

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

            Console.WriteLine();
            Console.WriteLine("(`S64`) СПИСОК КАНДИДАТОВ В ПОДПИСИ");
            if (!HasCandidates())
            {
                Console.WriteLine("  сборка БЕЗ правки `S64` — у пика списка нет, подпись одноимённая");
            }
            else
            {
                Console.WriteLine("  подписей со списком длиннее одного: {0} из {1}; самый длинный список: {2} имён",
                                  multiName, labelled, longestList);
                Console.WriteLine("  лексема надписи разошлась с лексемой победителя: {0}", tokenMismatch);
            }

            int badNames = JudgeSumNames(labelRows, AnnihilationLine(nuclides.NuclideDefinitions));
            // ⛔ Разошедшаяся лексема — ОТКАЗ, а не примечание: по ней читают
            //    состав, и молчаливая смена нуклида дороже любой надписи.
            return failed > 0 || badNames > 0 || tokenMismatch > 0 ? 1 : 0;
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
        // (`S64`) СПИСОК КАНДИДАТОВ — через отражение
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ Список и надпись спрашиваются ОТРАЖЕНИЕМ, а не прямым вызовом, по
        /// той же причине, что и пороги: одна и та же проба обязана считаться
        /// на сборке ДО правки, где ни свойства, ни метода ещё нет. Прямой
        /// вызов сделал бы плечо «до» неизмеримым этой пробой вовсе.
        /// </summary>
        static readonly PropertyInfo PiCandidates = typeof(Peak).GetProperty("NuclideCandidates");
        static readonly MethodInfo MiPeakLabel = typeof(PeakDetector).GetMethod(
            "PeakLabel", BindingFlags.Public | BindingFlags.Static);

        static bool HasCandidates()
        {
            return PiCandidates != null && MiPeakLabel != null;
        }

        static List<NuclideDefinition> CandidatesOf(Peak peak)
        {
            var list = new List<NuclideDefinition>();
            if (PiCandidates == null)
            {
                if (peak.Nuclide != null) list.Add(peak.Nuclide);
                return list;
            }
            var seq = PiCandidates.GetValue(peak, null) as System.Collections.IEnumerable;
            if (seq == null)
            {
                if (peak.Nuclide != null) list.Add(peak.Nuclide);
                return list;
            }
            foreach (object o in seq)
            {
                var nd = o as NuclideDefinition;
                if (nd != null) list.Add(nd);
            }
            return list;
        }

        /// <summary>Надпись пика — та же, что увидит человек.</summary>
        static string LabelTextOf(Peak peak)
        {
            if (peak.Nuclide == null) return "";
            if (MiPeakLabel == null) return peak.Nuclide.Name;
            return (string)MiPeakLabel.Invoke(null, new object[] { peak }) ?? "";
        }

        /// <summary>Имена списка через «|» — разделитель ВЫГРУЗКИ, не экрана.</summary>
        static string Names(List<NuclideDefinition> list)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(list[i].Name);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // (`A229`) ИМЯ приборной подписи суммы 511+511 — ожидание ДОСЛОВНО
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ ОЖИДАЕМЫЙ ОБРАЗЕЦ ИМЕНИ, ВЫПИСАННЫЙ ЗДЕСЬ ДОСЛОВНО, — это и есть
        /// проверка. Спроси проба то же имя у приложения (`Resources`,
        /// `AnnihilationSumName`), и она напечатала бы «сошлось» при ЛЮБОМ
        /// имени, в том числе при пустом хвосте, то есть при том самом дефекте
        /// `A229`, ради которого заведена.
        ///
        /// Ключ — двухбуквенный код языка; всё, что не `ru`, — первичный
        /// английский ресурс (`Resources.resx`), потому что спутника у такой
        /// культуры нет и `ResourceManager` откатывается на нейтральный.
        /// `{0}` — имя библиотечной записи образа аннигиляции.
        /// </summary>
        static readonly Dictionary<string, string> ExpectedSumFormat =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ru", "{0} (сумма 511+511)" },
                { "",   "{0} (sum 511+511)" }
            };

        /// <summary>
        /// Язык приложения выставляется ТЕМ ЖЕ полем настройки, каким его
        /// выставляет меню языка. Культура потока тоже двигается — но одной её
        /// мало: `DetectPeak` крутится в `Task.Run`, и подпись читает ресурс не
        /// в этом потоке (`A229`).
        ///
        /// ⚠ Настройка правится ТОЛЬКО В ПАМЯТИ: `SaveGlobalConfig` проба не
        /// зовёт, конфиг Amber остаётся нетронутым.
        /// </summary>
        static void ApplyLanguage()
        {
            try
            {
                GlobalConfigManager.GetInstance().GlobalConfig.Language = language;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("⛔ язык приложения не выставлен: {0}", e.Message);
            }
            try
            {
                System.Threading.Thread.CurrentThread.CurrentUICulture =
                    language.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(language);
            }
            catch (CultureNotFoundException)
            {
            }
            Console.WriteLine("язык приложения: «{0}» -> ожидаемый образец имени суммы: «{1}»",
                              language, ExpectedFormat());
        }

        /// <summary>Ожидаемый образец на выставленный язык (или подмена контроля).</summary>
        static string ExpectedFormat()
        {
            if (expectSumOverride != null)
            {
                return expectSumOverride;
            }
            string two;
            try
            {
                two = language.Length == 0 ? "" : CultureInfo.GetCultureInfo(language).TwoLetterISOLanguageName;
            }
            catch (CultureNotFoundException)
            {
                two = "";
            }
            string fmt;
            return ExpectedSumFormat.TryGetValue(two, out fmt) ? fmt : ExpectedSumFormat[""];
        }

        /// <summary>Ожидаемое ИМЯ подписи суммы при данном имени записи образа.</summary>
        static string ExpectedSumName(string imageName)
        {
            return ExpectedFormat().Replace("{0}", imageName);
        }

        /// <summary>
        /// Есть ли в собранном приложении сама правка `A229`. Спрашивается
        /// ОТРАЖЕНИЕМ, чтобы одна и та же проба годилась обоим плечам замера:
        /// на сборке без правки приговор не выносится, как и с порогами
        /// (см. <see cref="PrintGates"/>).
        /// </summary>
        static bool HasSumNaming()
        {
            return typeof(PeakDetector).GetMethod(
                "AnnihilationSumName",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static) != null;
        }

        /// <summary>
        /// Запись образа аннигиляции — тем же правилом, что у
        /// <c>PeakDetector.AnnihilationLine</c>: видимая, БЕЗ проставленного
        /// выхода, у энергии кванта. Повтор нарочен — проба обязана считаться и
        /// на сборке БЕЗ правки.
        /// </summary>
        static NuclideDefinition AnnihilationLine(List<NuclideDefinition> defs)
        {
            NuclideDefinition best = null;
            double bestMiss = 2.0;
            foreach (NuclideDefinition nd in defs)
            {
                if (nd == null || !nd.Visible || nd.Intencity > 0.0) continue;
                double miss = Math.Abs(nd.Energy - PeakDetector.AnnihilationKev);
                if (miss <= bestMiss)
                {
                    bestMiss = miss;
                    best = nd;
                }
            }
            return best;
        }

        /// <summary>
        /// ПРИГОВОР ПО ИМЕНАМ ОБРАЗА АННИГИЛЯЦИИ, собранный по всей выгрузке.
        ///
        /// Подпись суммы узнаётся НЕ ПО ТЕКСТУ (текст и есть предмет спора), а
        /// по записи: энергия ровно вдвое против записи образа, выход не
        /// проставлен. Подпись самого образа — по энергии кванта.
        ///
        /// Три условия, и все три обязательны:
        ///  * подписи суммы вообще ЕСТЬ (ноль — это не «сошлось», а «нечего
        ///    проверять», и такой прогон принят быть не может);
        ///  * у каждой из них имя РОВНО ожидаемое;
        ///  * у подписей на 511 имя ПРЕЖНЕЕ, без хвоста, — иначе хвост уехал бы
        ///    и на образ, а различать было бы снова нечего.
        /// </summary>
        static int JudgeSumNames(List<LabelRow> rows, NuclideDefinition ann)
        {
            Console.WriteLine();
            Console.WriteLine("(`A229`) ИМЕНА ОБРАЗА АННИГИЛЯЦИИ В ВЫГРУЗКЕ");
            if (ann == null)
            {
                Console.WriteLine("  ⚠ записи образа аннигиляции в библиотеке НЕТ — правило суммы не работает,");
                Console.WriteLine("    и проверять имя не на чем");
                return 0;
            }
            double sumKev = 2.0 * ann.Energy;
            Console.WriteLine("  запись образа: «{0}» {1:F3} кэВ -> сумма {2:F3} кэВ",
                              ann.Name, ann.Energy, sumKev);

            var sums = rows.Where(r => r.LineKev.HasValue
                                       && Math.Abs(r.LineKev.Value - sumKev) < 1e-6
                                       && r.Intensity == 0.0).ToList();
            var images = rows.Where(r => r.LineKev.HasValue
                                         && Math.Abs(r.LineKev.Value - ann.Energy) < 1e-6
                                         && r.Intensity == 0.0).ToList();

            string want = ExpectedSumName(ann.Name);
            Console.WriteLine();
            Console.WriteLine("  подписи СУММЫ ({0} шт.), ждём «{1}»:", sums.Count, want);
            int bad = 0;
            foreach (LabelRow r in sums.OrderBy(r => r.Spectrum, StringComparer.OrdinalIgnoreCase))
            {
                bool ok = string.Equals(r.Name, want, StringComparison.Ordinal);
                if (!ok) bad++;
                Console.WriteLine("    {0,-22} пик {1,9:F3} кэВ  получено «{2}» {3}",
                                  r.Spectrum, r.PeakKev, r.Name,
                                  ok ? "✓" : "⛔ РАСХОЖДЕНИЕ, ждали «" + want + "»");
            }
            Console.WriteLine();
            Console.WriteLine("  подписи САМОГО ОБРАЗА ({0} шт.), ждём «{1}» без хвоста:", images.Count, ann.Name);
            foreach (LabelRow r in images.OrderBy(r => r.Spectrum, StringComparer.OrdinalIgnoreCase))
            {
                bool ok = string.Equals(r.Name, ann.Name, StringComparison.Ordinal);
                if (!ok) bad++;
                Console.WriteLine("    {0,-22} пик {1,9:F3} кэВ  получено «{2}» {3}",
                                  r.Spectrum, r.PeakKev, r.Name,
                                  ok ? "✓" : "⛔ РАСХОЖДЕНИЕ, ждали «" + ann.Name + "»");
            }

            Console.WriteLine();
            if (!HasSumNaming())
            {
                Console.WriteLine("  сборка БЕЗ правки `A229` — исходы напечатаны, приговор не выносится");
                return 0;
            }
            if (sums.Count == 0)
            {
                Console.WriteLine("  ⛔ ОТКАЗ: подписей суммы в корпусе НЕТ ВОВСЕ — проверять имя не на чем,");
                Console.WriteLine("     «сошлось» здесь означало бы ровно ничего");
                return 1;
            }
            Console.WriteLine(bad == 0
                ? "  имена сошлись полностью"
                : ("  ⛔ расхождений по имени: " + bad));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Строка выгрузки, нужная приговору по именам.</summary>
        class LabelRow
        {
            public string Spectrum;
            public double PeakKev;
            public string Name;
            public double? LineKev;
            public double Intensity;
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
            bad += SelfTestCandidates();
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

            // (`A229`) Имя подписи суммы ждётся ДОСЛОВНОЕ, из таблицы образцов,
            // а не спрашивается у приложения: спроси — и опыт принял бы любое
            // имя, включая прежнее одинаковое с образом.
            string sumLabel = (HasSumNaming() ? ExpectedSumName("Образ") : "Образ") + "@1022";

            var cases = new List<ConfirmCase>
            {
                // --- подтверждение слабой линии (`A197`) ---
                Confirm("слабая линия, второй своей НЕ видно", 1500,
                        new[] { Line("Икс", 1001.0, 0.842), Line("Икс", 766.0, 0.317) },
                        new[] { P(1001.0, 60, 20, "Икс", 1001.0), P(1275.0, 70, 40, null, 0) },
                        new[] { "(нет)", "(нет)" }),
                // ⚠ Улика — пик, спор за который наш нуклид НЕ ПРОИГРЫВАЕТ
                //   (`A227`): подтверждающий пик обязан нести это имя среди
                //   кандидатов, а «просто пик рядом» уликой больше не считается.
                Confirm("слабая линия, вторая своя ВИДНА", 1500,
                        new[] { Line("Икс", 1001.0, 0.842), Line("Икс", 766.0, 0.317) },
                        new[] { P(1001.0, 60, 20, "Икс", 1001.0), P(770.0, 60, 15, "Икс", 766.0) },
                        new[] { "Икс@1001", "Икс@766" }),
                // Близнец предыдущего: пик у второй линии ЕСТЬ, но он ЧУЖОЙ —
                // ровно случай `I-131` 722.0, «подтверждаемой» пиком Bi-212
                // 727.3 в спектрах тория (`A227`).
                Confirm("слабая линия, пик у второй линии ЧУЖОЙ", 1500,
                        new[] { Line("Икс", 1001.0, 0.842), Line("Икс", 766.0, 0.317),
                                Line("Чужой", 770.0, 60.0) },
                        new[] { P(1001.0, 60, 20, "Икс", 1001.0), P(770.0, 60, 15, "Чужой", 770.0) },
                        new[] { "(нет)", "Чужой@770" }, true),
                Confirm("ЯРКАЯ линия В ПИКЕ, второй своей не видно", 1500,
                        new[] { Line("Игрек", 911.0, 25.8), Line("Игрек", 969.0, 15.8) },
                        new[] { P(911.0, 40, 30, "Игрек", 911.0) },
                        new[] { "Игрек@911" }),
                // --- подтверждение ДАЛЁКОЙ линии (`A227`) ---
                // Промах 0.5 ПШПВ (20 кэВ при ПШПВ 40) — линия яркая, но одна
                // за себя не отвечает: второй своей в спектре нет.
                Confirm("ЯРКАЯ линия, промах 0.5 ПШПВ, второй своей нет", 1500,
                        new[] { Line("Йод-мнимый", 364.0, 81.5), Line("Йод-мнимый", 636.0, 7.2) },
                        new[] { P(384.0, 40, 30, "Йод-мнимый", 364.0) },
                        new[] { "(нет)" }, true),
                Confirm("ЯРКАЯ линия, промах 0.5 ПШПВ, вторая своя ВИДНА", 1500,
                        new[] { Line("Йод-мнимый", 364.0, 81.5), Line("Йод-мнимый", 636.0, 7.2) },
                        new[] { P(384.0, 40, 30, "Йод-мнимый", 364.0),
                                P(638.0, 50, 12, "Йод-мнимый", 636.0) },
                        new[] { "Йод-мнимый@364", "Йод-мнимый@636" }, true),
                // Германий: промах 0.4 ПШПВ, но ВСЕГО 0.6 кэВ — это огрубление
                // записи библиотеки (74 % записей несут целую энергию), а не
                // положение пика. Пол в кэВ обязан такую подпись сохранить.
                Confirm("ЯРКАЯ линия, промах 0.4 ПШПВ, но 0.6 кэВ", 1500,
                        new[] { Line("Актиний-мнимый", 911.0, 25.8),
                                Line("Актиний-мнимый", 969.0, 15.8) },
                        new[] { P(911.6, 1.5, 30, "Актиний-мнимый", 911.0) },
                        new[] { "Актиний-мнимый@911" }, true),
                // Рентген: вторая своя линия попала В ЭТОТ ЖЕ пик (`A226` п.4).
                Confirm("далёкая линия, вторая своя ВНУТРИ ТОГО ЖЕ пика", 1500,
                        new[] { Line("Свинец-мнимый", 87.3, 8.0),
                                Line("Свинец-мнимый", 84.9, 23.0) },
                        new[] { P(93.5, 15, 30, "Свинец-мнимый", 87.3) },
                        new[] { "Свинец-мнимый@87.3" }, true),
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
                        new[] { "Образ@511", sumLabel }),
                Confirm("суммы нет: пика 511 в спектре НЕТ", 3000,
                        new[] { Line("Образ", 511.0, 0.0), Line("Нуклид", 1001.0, 0.842),
                                Line("Нуклид", 766.0, 0.317) },
                        new[] { P(1002.0, 90, 16, "Нуклид", 1001.0), P(766.0, 60, 15, "Нуклид", 766.0) },
                        new[] { "Нуклид@1001", "Нуклид@766" }),
                Confirm("суммы нет: пик 1022 ЗАМЕТНЕЕ пика 511", 3000,
                        new[] { Line("Образ", 511.0, 0.0), Line("Нуклид", 1001.0, 0.842),
                                Line("Нуклид", 766.0, 0.317) },
                        new[] { P(511.0, 40, 10, "Образ", 511.0), P(1002.0, 90, 500, "Нуклид", 1001.0),
                                P(766.0, 60, 15, "Нуклид", 766.0) },
                        new[] { "Образ@511", "Нуклид@1001", "Нуклид@766" })
            };

            // (`A227`) Опыт над правилом ДАЛЁКОЙ линии ставится только там, где
            // правило есть: на сборке без него исходы печатаются, но приговора
            // нет — иначе плечо «до» отказывало бы за отсутствие того, чего в
            // нём и не должно быть.
            bool farRule = Gate("LabelMissConfirmInFwhm") != null;
            if (expectFarOverride != null)
            {
                foreach (ConfirmCase c in cases)
                {
                    if (c.Title.StartsWith("ЯРКАЯ линия, промах 0.5 ПШПВ, второй", StringComparison.Ordinal))
                    {
                        c.Expect = new[] { expectFarOverride };
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("ОПЫТ НАД ПРАВИЛАМИ ПО СОСТАВУ СПЕКТРА (`A196`, `A197`, `A227`):");
            Console.WriteLine("  правило далёкой линии в сборке: {0}",
                              farRule ? "есть (промах > "
                                        + Gate("LabelMissConfirmInFwhm").Value.ToString("G6", CultureInfo.InvariantCulture)
                                        + " ПШПВ и > "
                                        + Gate("LabelMissConfirmFloorKev").Value.ToString("G6", CultureInfo.InvariantCulture)
                                        + " кэВ)"
                                      : "НЕТ — опыты над ним не судятся");
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
                bool judged = farRule || !c.NeedsFarRule;
                if (!ok && judged) bad++;
                Console.WriteLine("  {0,-46} ждали [{1}] получили [{2}] {3}",
                                  c.Title, string.Join(" ", c.Expect), string.Join(" ", got),
                                  !judged ? "(сборка без правила `A227`)"
                                          : (ok ? "✓" : "⛔ РАСХОЖДЕНИЕ"));
            }
            return bad;
        }

        /// <summary>
        /// (`S64`) ОПЫТ НАД СПИСКОМ КАНДИДАТОВ. Подставной пик с подставной
        /// библиотекой подаётся в <c>PeakDetector.MatchNuclides</c>, список
        /// ставится пику, и сверяется НАДПИСЬ — ровно та строка, которую
        /// увидит человек.
        ///
        /// ⚠ Опыт держит ОБА конца, и это здесь важнее обычного: правило,
        /// приписывающее второе имя ВСЕМ пикам, отличается от полезного только
        /// по паре «спор не решается положением» / «спор решён положением».
        ///
        /// ⛔ Ожидаемые надписи выписаны ЗДЕСЬ ДОСЛОВНО, разделитель в том
        /// числе. Спроси проба разделитель у приложения — она напечатала бы
        /// «сошлось» при любом, в том числе при пустом, то есть при подписи,
        /// потерявшей все имена, кроме первого.
        /// </summary>
        static int SelfTestCandidates()
        {
            MethodInfo mMatchList = typeof(PeakDetector).GetMethod(
                "MatchNuclides", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo mSet = typeof(Peak).GetMethod(
                "SetNuclideCandidates", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo fDefs = typeof(PeakDetector).GetField(
                "nuclideDefinitions", BindingFlags.NonPublic | BindingFlags.Instance);
            Console.WriteLine();
            Console.WriteLine("ОПЫТ НАД СПИСКОМ КАНДИДАТОВ (`S64`):");
            if (mMatchList == null || mSet == null || fDefs == null || !HasCandidates())
            {
                Console.WriteLine("  списка кандидатов в сборке НЕТ — опыт над ним не ставится");
                return 0;
            }

            // Разделитель ждём ДОСЛОВНО: пробел, косая, пробел на обоих языках.
            const string sep = " / ";
            var cases = new[]
            {
                // Спор НЕ решается положением: 0.223 кэВ разницы при ПШПВ 10 —
                // это 0.022 ПШПВ, то есть много меньше окна. Оба имени.
                Cand("спор не решается положением", 58.57, 10.0,
                     "Рентген-мнимый" + sep + "Америций-мнимый",
                     Line("Рентген-мнимый", 59.318, 57.6), Line("Америций-мнимый", 59.541, 35.9)),
                // Тот же вход, но соперник отодвинут: 2.68 кэВ = 0.27 ПШПВ,
                // спор решён — ОДНО имя. Близнец предыдущего опыта.
                Cand("спор решён положением", 58.57, 10.0, "Рентген-мнимый",
                     Line("Рентген-мнимый", 59.318, 57.6), Line("Далёкий-мнимый", 62.0, 35.9)),
                // Четверо в окне — на экран идут ТРИ (предел числа имён).
                Cand("соперников больше предела", 100.0, 20.0,
                     "Первый-мнимый" + sep + "Второй-мнимый" + sep + "Третий-мнимый",
                     Line("Первый-мнимый", 100.5, 10.0), Line("Второй-мнимый", 98.8, 10.0),
                     Line("Третий-мнимый", 101.4, 10.0), Line("Четвёртый-мнимый", 102.3, 10.0)),
                // Две линии ОДНОГО имени в пике — имя одно, а не дважды.
                Cand("две линии одного имени", 100.0, 20.0, "Двойной-мнимый",
                     Line("Двойной-мнимый", 100.5, 10.0), Line("Двойной-мнимый", 99.8, 10.0)),
                // ПШПВ не измерена — мерить спор нечем, соперников не ищем.
                Cand("ПШПВ не измерена", 58.57, 0.0, "Рентген-мнимый",
                     Line("Рентген-мнимый", 59.318, 57.6), Line("Америций-мнимый", 59.541, 35.9)),
                // Ни одна линия не проходит порог по выходу — подписи нет вовсе.
                Cand("подписи нет вовсе", 58.57, 10.0, "(нет подписи)",
                     Line("Ничтожный-мнимый", 59.318, 0.0009))
            };

            int bad = 0;
            for (int i = 0; i < cases.Length; i++)
            {
                CandCase c = cases[i];
                string want = i == 0 && expectCandidatesOverride != null
                    ? expectCandidatesOverride
                    : c.Expect;
                var det = new PeakDetector();
                fDefs.SetValue(det, c.Library);
                var peak = new Peak { Energy = c.PeakKev, FWHM = c.Fwhm };
                object list = mMatchList.Invoke(det, new object[] { peak, 10.0, null, c.Fwhm });
                mSet.Invoke(peak, new object[] { list });
                string got = peak.Nuclide == null ? "(нет подписи)" : LabelTextOf(peak);
                bool ok = string.Equals(got, want, StringComparison.Ordinal);
                if (!ok) bad++;
                Console.WriteLine("  {0,-30} ждали «{1}» получили «{2}» {3}",
                                  c.Title, want, got, ok ? "✓" : "⛔ РАСХОЖДЕНИЕ");
            }
            return bad;
        }

        class CandCase
        {
            public string Title;
            public double PeakKev;
            public double Fwhm;
            public string Expect;
            public List<NuclideDefinition> Library;
        }

        static CandCase Cand(string title, double peakKev, double fwhm, string expect,
                             params NuclideDefinition[] lib)
        {
            return new CandCase
            {
                Title = title,
                PeakKev = peakKev,
                Fwhm = fwhm,
                Expect = expect,
                Library = lib.ToList()
            };
        }

        class ConfirmCase
        {
            public string Title;
            public double MaxRangeKev;
            public List<NuclideDefinition> Library;
            public List<Peak> Peaks;
            public string[] Expect;
            /// <summary>
            /// (`A227`) Опыт спрашивает правило ДАЛЁКОЙ линии либо новую силу
            /// улики. На сборке без них исход печатается, но не судится.
            /// </summary>
            public bool NeedsFarRule;
        }

        static ConfirmCase Confirm(string title, double maxRangeKev, NuclideDefinition[] lib,
                                   Peak[] peaks, string[] expect, bool needsFarRule = false)
        {
            return new ConfirmCase
            {
                Title = title,
                MaxRangeKev = maxRangeKev,
                Library = lib.ToList(),
                Peaks = peaks.ToList(),
                Expect = expect,
                NeedsFarRule = needsFarRule
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
