using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П254, 14.10.2026) ОКНО «MEASUREMENT RESULT» ПРОТИВ РАЗБОРА FSA — приёмка
    /// ревизии окна результата и сеанса документа (`AMBER208`, `AMBER211`).
    ///
    /// Решение Amber 07.10.2026, дословно: «Если это изотопы не имеющие родителя
    /// (неравновесная история) - то считается активность каждого изотопа в
    /// отдельности. … что мы видим в окне отчёта в списке изотопов - ту активность
    /// мы и считаем.» Проба судит строки окна НЕЗАВИСИМЫМ счётом, а не кодом окна:
    ///
    ///  1. ЕДИНИЦЫ (`units`): у члена ряда, вошедшего в разбор свободной колонкой
    ///     (равновесие ВЫКЛ), амплитуда — распады КОРНЯ: линии образа взяты с
    ///     долей ветвления от корня (`FsaSampleLibrary.Build`, `AddLine(... ×
    ///     member.Value)`; путь по подписям — выходы библиотеки, у Amber «Tl-208
    ///     (Th-232)» 2614 = 35.85 % на распад Th-232). Своя активность члена —
    ///     амплитуда × доля ветвления (`FsaSampleLibrary.ChainBranches`, вторым,
    ///     независимым от окна путём). Строка ряда (равновесие ВКЛ) — активность
    ///     корня; её предел a# — строки предела САМОЙ колонки ряда (по имени
    ///     ряда), а не первого члена.
    ///  2. ПОПРАВКА НА РАСПАД (`units`): период строки члена ряда — период корня
    ///     (решение Amber `AMBER195` «Ряд задан → период корня»), а не свой
    ///     (Tl-208 3.05 мин давал множитель ×10⁷ на Th232_29.07.2022). У строки
    ///     СВОБОДНОГО члена (равновесие ВЫКЛ) — тоже корня: решение Amber 14.10.2026
    ///     «Периодом корня (Рекомендую)» (П278); до П278 эти строки не судились.
    ///  3. ПРЕДЕЛ СТРОКИ РЯДА (`chainmda`): сцена без члена с именем корня.
    ///  4. ОКНО (`window`): настоящий <see cref="DCResultView"/> в форме-носителе
    ///     на документе пробы — текст ячеек против Build→Translate того же сеанса;
    ///     СКРЫТОЕ окно не заказывает счёт по событиям документа (решение Amber
    ///     `AMBER211` «Не чаще раза в 30 с и только для видимых»), видимое —
    ///     заказывает (положительный контроль); выбор строки переживает
    ///     обновление вида; смена документа посреди счёта — в окне строки
    ///     нового документа, не прежнего.
    ///
    /// Ключи: --spectrum=&lt;файл&gt; (несколько; первый — для окна, второй — второй
    /// документ гонки); --skip-window; --timeout=300; --dump=&lt;файл&gt; — строки окна
    /// формой "R" для побитового сравнения плеч «до/после» (П278).
    /// Коды: 0 — всё сошлось; 1 — есть расхождение; 2 — ключи/файлы; 3 — разбор не дошёл.
    /// </summary>
    static class FsaResultWindowProbeP254
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int bad;
        static int timeoutS = 300;
        static string shots;
        static string dump;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var spectra = new List<string>();
            bool skipWindow = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectra.Add(a.Substring(11));
                else if (a == "--skip-window") skipWindow = true;
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--shots=", StringComparison.Ordinal)) shots = a.Substring(8);
                else if (a.StartsWith("--dump=", StringComparison.Ordinal)) dump = a.Substring(7);
                else if (a.StartsWith("--ui=", StringComparison.Ordinal)) Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(a.Substring(5));
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (spectra.Count == 0)
            {
                Console.Error.WriteLine("нужен хотя бы один --spectrum=<файл>");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ChainMdaSection();
            foreach (string path in spectra)
            {
                foreach (bool db in new[] { true, false })
                {
                    foreach (bool eq in new[] { false, true })
                    {
                        if (!db && eq) continue;      // путь по подписям рядов не связывает
                        if (!UnitsSection(path, db, eq, nuclides)) return 3;
                    }
                }
            }
            if (!skipWindow)
            {
                WindowSection(spectra[0], spectra.Count > 1 ? spectra[1] : null, nuclides);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ИТОГ: ВСЁ СОШЛОСЬ" : "ИТОГ: РАСХОЖДЕНИЙ " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ==================================================================
        // 1–2. Единицы строк и поправка на распад
        // ==================================================================

        static bool UnitsSection(string path, bool dbLookups, bool equilibrium, NuclideDefinitionManager nuclides)
        {
            Console.WriteLine();
            Console.WriteLine("=== ЕДИНИЦЫ: {0}, равновесие {1}, состав {2} ===", Path.GetFileName(path),
                              equilibrium ? "ВКЛ" : "ВЫКЛ", dbLookups ? "из NucBase" : "по подписям пиков");
            ResultData rd = Load(path, nuclides);
            if (rd == null) return false;
            FsaCalculationOptions options = FsaCalculationOptions.FromConfig(rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig);
            options.DbLookups = dbLookups;
            options.ChainEquilibrium = equilibrium;
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
            if (!done.WaitOne(TimeSpan.FromSeconds(timeoutS)) || session.Result == null)
            {
                Console.WriteLine("  ⛔ разбора нет: {0}", session.Status ?? "причина не названа");
                bad++;
                return false;
            }
            FsaResult result = session.Result;
            FsaMeasurementResult.Summary summary;
            MeasurementResultCollection built = FsaMeasurementResult.Build(rd, result, nuclides.NuclideDefinitions,
                                                                           FsaMeasurementResult.Texts.Russian, out summary);
            var manager = new MeasurementResultManager();
            MeasurementResultCollection bq = manager.Translate(built, ResultTranslation.Becquerels);
            MeasurementResultCollection corr = manager.Correct(bq);
            Console.WriteLine("  {0}; {1}; набор {2:F0} с, отбор за {3:F1} мин до начала",
                              summary.Describe(null), summary.Details(null),
                              (rd.EndTime - rd.StartTime).TotalSeconds, (rd.StartTime - rd.SampleInfo.Time).TotalMinutes);
            Console.WriteLine("  {0,-9} {1,11} {2,10} {3,10} | {4,11} {5,-12} {6,7} | {7,12} {8,12} {9,12}",
                              "строка", "Бк окна", "±σ", "a#", "ожид. Бк", "корень", "r", "T½ окна, л", "×распад", "ожид. ×");
            var report = new FsaSampleLibrary.Report();
            for (int i = 0; i < bq.ResultList.Count; i++)
            {
                MeasurementResult row = bq.ResultList[i];
                string name = row.Line.Name;
                FsaComponentResult chainMember = result.Components.FirstOrDefault(
                    c => string.Equals(c.ChainRoot, name, StringComparison.OrdinalIgnoreCase));
                FsaComponentResult free = result.Components.FirstOrDefault(
                    c => string.IsNullOrEmpty(c.ChainRoot) && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
                         && c.Kind != FsaComponentKind.Nuisance && c.CountRate > 0.0);
                FsaCharacteristicLimit limit = result.CharacteristicLimits.FirstOrDefault(
                    l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

                // независимый счёт: скорость, корень, доля ветвления
                double rate, mda, sigma, r = 1.0;
                string root;
                if (chainMember != null)
                {
                    rate = chainMember.CountRate;
                    sigma = rate / chainMember.Z;
                    mda = limit != null ? limit.DetectionLimitRate : double.NaN;
                    root = null;                                      // строка — сам корень
                }
                else if (free != null)
                {
                    rate = free.CountRate;
                    sigma = rate / free.Z;
                    mda = free.DetectionLimitRate;
                    root = free.DecayChainRoot;
                }
                else
                {
                    rate = 0.0;
                    // не обнаружен: σ нулевой оценки — порог решения / k (ISO 11929, k = 1.6449)
                    sigma = limit != null ? limit.DecisionThresholdRate / 1.6449 : double.NaN;
                    mda = limit != null ? limit.DetectionLimitRate : double.NaN;
                    root = limit != null ? limit.DecayChainRoot : null;
                }
                string rootNucid = FsaSampleLibrary.NucidOf(root ?? "");
                string nucid = FsaSampleLibrary.NucidOf(name);
                bool byLabels = false;
                if (rootNucid.Length == 0 && chainMember == null)
                {
                    // путь по подписям: происхождения у колонки нет — ряд строки библиотеки
                    NuclideDefinition def = nuclides.NuclideDefinitions.FirstOrDefault(
                        d => d != null && FsaSampleLibrary.NucidOf(d.NuclideName ?? "") == nucid
                             && FsaSampleLibrary.NucidOf((d.Chain ?? "").Trim()).Length > 0);
                    if (def != null)
                    {
                        root = def.Chain.Trim();
                        rootNucid = FsaSampleLibrary.NucidOf(root);
                        byLabels = true;
                    }
                }
                if (rootNucid.Length > 0 && rootNucid != nucid && !byLabels)
                {
                    // путь из баз: образ сложен с долей ветвления от корня
                    Dictionary<string, double> branches = FsaSampleLibrary.ChainBranches(rootNucid, report);
                    double b;
                    if (branches != null && branches.TryGetValue(nucid, out b) && b > 0.0) r = b;
                    // (П278) и множитель переходного равновесия по Бейтману (`AMBER55`): образ члена
                    // сложен с ним (`FsaSampleLibrary.Build`, ветвление × множитель). Без него проба
                    // ждала у Pb-210 ряда Ra-226 долю 1 вместо 1.0141 и краснела на σ и a# строки,
                    // где окно право (Cs-137 «в домике», П278).
                    var factors = (Dictionary<string, double>)typeof(FsaSampleLibrary)
                        .GetMethod("EquilibriumFactors", Any).Invoke(null, new object[] { rootNucid, report });
                    double f;
                    if (factors != null && factors.TryGetValue(nucid, out f) && f > 0.0) r *= f;
                }
                else if (byLabels && rootNucid != nucid)
                {
                    // путь по подписям: выход сильнейшей строки библиотеки / свой выход той же линии по базе
                    NuclideDefinition strongest = nuclides.NuclideDefinitions
                        .Where(d => d != null && FsaSampleLibrary.NucidOf(d.NuclideName ?? "") == nucid && d.Intencity > 0.0)
                        .OrderByDescending(d => d.Intencity).FirstOrDefault();
                    double[] ownLine = strongest == null ? null : FsaSampleLibrary.DecayLines(nucid, report)
                        .FirstOrDefault(l => Math.Abs(l[0] - strongest.Energy) < 0.05);
                    if (ownLine != null && ownLine[1] > 0.0) r = strongest.Intencity / ownLine[1];
                }
                double expBq = rate * r;
                double expErr = double.IsNaN(sigma) ? 0.0 : sigma * r;
                double expMda = double.IsNaN(mda) ? 0.0 : mda * r;
                // ⛔ (П278) Период строки СВОБОДНОГО члена ряда — период КОРНЯ: решение Amber
                // 14.10.2026 вопросником, дословно «Периодом корня (Рекомендую)» (находка П254 №10;
                // тот же приём, что `AMBER195` «Ряд задан → период корня»). Строка без ряда и строка
                // самого ряда — свой период. Ожидание — период из базы по `nucid` корня, мимо кода окна.
                bool member = rootNucid.Length > 0 && rootNucid != nucid;
                double expHalf = BecquerelMonitor.NucBase.NucBaseFramework.HalfLifeYearsOf(member ? rootNucid : nucid);
                double expFactor = MeasurementResultManager.DecayToSamplingFactor(expHalf, rd.SampleInfo.Time, rd.StartTime, rd.EndTime);
                double factor = row.ResultValue > 0.0 ? corr.ResultList[i].ResultValue / row.ResultValue
                    : (row.MDA > 0.0 ? corr.ResultList[i].MDA / row.MDA : 1.0);
                bool okBq = Close(row.ResultValue, expBq, 1e-3);
                bool okMda = Close(row.MDA, expMda, 1e-3);
                bool okFactor = Close(factor, expFactor, 1e-6);
                bool okErr = Close(row.ResultError, expErr, 1e-3);
                Dump(path, dbLookups, equilibrium, row, corr.ResultList[i], factor, member);
                Console.WriteLine("  {0,-9} {1,11} {2,10} {3,10} | {4,11} {5,-12} {6,7} | {7,12} {8,12} {9,12}  {10}",
                                  name, F(row.ResultValue), F(row.ResultError), F(row.MDA), F(expBq), root ?? "—",
                                  r.ToString("0.####", CultureInfo.InvariantCulture),
                                  row.Line.HalfLifeYears.ToString("0.####E+0", CultureInfo.InvariantCulture),
                                  factor.ToString("0.####E+0", CultureInfo.InvariantCulture),
                                  expFactor.ToString("0.####E+0", CultureInfo.InvariantCulture) + (member ? " корня" : ""),
                                  okBq && okMda && okFactor && okErr ? "ok" : "НЕТ (" + (okBq ? "" : "Бк ") + (okErr ? "" : "σ=" + F(expErr) + " ") + (okMda ? "" : "a# ") + (okFactor ? "" : "распад") + ")");
                if (!(okBq && okMda && okFactor && okErr)) bad++;
            }
            return true;
        }

        /// <summary>
        /// (П278) Строка окна в файл `--dump=` — числа формой "R" (побитово), чтобы плечи
        /// «до/после» правки сравнивались построчно: одиночки и строка ряда обязаны совпасть
        /// до байта, у свободных членов ряда меняются только период и множитель распада.
        /// </summary>
        static void Dump(string path, bool dbLookups, bool equilibrium, MeasurementResult row, MeasurementResult corrected,
                         double factor, bool member)
        {
            if (dump == null) return;
            Func<double, string> r = v => v.ToString("R", CultureInfo.InvariantCulture);
            File.AppendAllText(dump, string.Join("\t", new[]
            {
                Path.GetFileName(path), dbLookups ? "nucbase" : "labels", equilibrium ? "eq_on" : "eq_off",
                row.Line.Name, member ? "member" : "own", r(row.Line.HalfLifeYears),
                r(row.ResultValue), r(row.ResultError), r(row.MDA),
                r(corrected.ResultValue), r(corrected.ResultError), r(corrected.MDA), r(factor)
            }) + Environment.NewLine, new UTF8Encoding(false));
        }

        static bool Close(double a, double b, double rel)
        {
            if (double.IsNaN(a) || double.IsNaN(b)) return double.IsNaN(a) && double.IsNaN(b);
            // (П278) бесконечность равна только себе: прежде |∞ − 1| ≤ rel·∞ проходило, и множитель
            // распада Tl-208 = ∞ (Cs-137 «в домике», до П278) печатался «ok»
            if (double.IsInfinity(a) || double.IsInfinity(b)) return a == b;
            double scale = Math.Max(Math.Abs(a), Math.Abs(b));
            return scale == 0.0 || Math.Abs(a - b) <= rel * scale;
        }

        // ==================================================================
        // 3. Предел строки ряда — по имени ряда
        // ==================================================================

        static void ChainMdaSection()
        {
            Console.WriteLine();
            Console.WriteLine("=== ПРЕДЕЛ СТРОКИ РЯДА: сцена, где корня среди членов нет ===");
            var rd = new ResultData();
            rd.EnergySpectrum = new EnergySpectrum { MeasurementTime = 100.0, LiveTime = 100.0 };
            rd.SampleInfo = new SampleInfoData();
            var result = new FsaResult { ResponseMatrixUsed = true, EfficiencyUsed = true };
            // ряд «Ra-228» связкой: у корня линий в полосе нет (13.5/16.2 кэВ), первый член — Ac-228
            result.Components.Add(new FsaComponentResult
            {
                Name = "Ac-228", Kind = FsaComponentKind.Single, ChainRoot = "Ra-228", DecayChainRoot = "Ra-228",
                CountRate = 50.0, Z = 25.0, DecisionThresholdRate = double.NaN, DetectionLimitRate = double.NaN
            });
            result.CharacteristicLimits.Add(new FsaCharacteristicLimit
            {
                Name = "Ra-228", Kind = FsaComponentKind.Chain, Detected = true, CountRate = 50.0,
                DecisionThresholdRate = 1.0, DetectionLimitRate = 2.5, TotalYieldPercent = double.NaN
            });
            FsaMeasurementResult.Summary summary;
            MeasurementResultCollection built = FsaMeasurementResult.Build(rd, result, new List<NuclideDefinition>(),
                                                                           FsaMeasurementResult.Texts.Russian, out summary);
            MeasurementResultCollection bq = new MeasurementResultManager().Translate(built, ResultTranslation.Becquerels);
            MeasurementResult row = bq.ResultList.FirstOrDefault();
            bool ok = row != null && row.Line.Name == "Ra-228" && Close(row.ResultValue, 50.0, 1e-9) && Close(row.MDA, 2.5, 1e-9);
            Console.WriteLine("  строка {0}: Бк {1}, a# {2} — ждём Ra-228, 50, 2.5  {3}",
                              row != null ? row.Line.Name : "(нет)", row != null ? F(row.ResultValue) : "—",
                              row != null ? F(row.MDA) : "—", ok ? "ok" : "НЕТ");
            if (!ok) bad++;
        }

        // ==================================================================
        // 4. Настоящее окно
        // ==================================================================

        static void WindowSection(string pathA, string pathB, NuclideDefinitionManager nuclides)
        {
            Console.WriteLine();
            Console.WriteLine("=== ОКНО: DCResultView на документе пробы ===");
            Application.EnableVisualStyles();
            bool equilibrium = false;
            FsaCalculationOptions.ApplicationSource = () =>
            {
                var o = new FsaCalculationOptions();
                o.DbLookups = true;
                o.ChainEquilibrium = equilibrium;
                o.AtomicXray = o.CascadeSumming = o.Backscatter = o.EscapeAndAnnihilation = o.PileUp = true;
                return o;
            };

            MainForm mainForm = new MainForm();
            DocEnergySpectrum a = Open(pathA, nuclides);
            DocEnergySpectrum b = pathB != null ? Open(pathB, nuclides) : null;
            if (a == null) { bad++; return; }
            a.EnergySpectrumView.BackgroundMode = BackgroundMode.Visible;   // график не заказчик
            if (b != null) b.EnergySpectrumView.BackgroundMode = BackgroundMode.Visible;
            mainForm.ActiveDocument = a;

            var results = new DCResultView(mainForm);
            Form host = Host(results, 640, 400);
            object table = Field(typeof(DCResultView), "tableModel1").GetValue(results);

            // 4а. текст окна против Build → Translate
            results.ShowResult(true);
            WaitIdle(a.FsaSession);
            Pump(300);
            results.ShowResult(true);
            Pump(100);
            CompareTable("равновесие ВЫКЛ", results, table, a, nuclides);
            Shot(host, "eq_off");

            equilibrium = true;
            results.ShowResult(false);
            WaitIdle(a.FsaSession);
            Pump(300);
            CompareTable("равновесие ВКЛ", results, table, a, nuclides);
            Shot(host, "eq_on");

            // 4б. скрытое окно не заказывает счёт по событию документа
            equilibrium = false;
            results.Visible = false;
            Pump(100);
            int before = a.FsaSession.RunCount;
            a.RefreshView();                          // событие ViewRefreshed — так приходит каждый такт набора
            Pump(500);
            int hiddenRuns = a.FsaSession.RunCount - before;
            WaitIdle(a.FsaSession);
            Console.WriteLine("  окно СКРЫТО, настройка сменилась, вид документа обновлён: запусков счёта {0} (ждём 0)", hiddenRuns);
            if (hiddenRuns != 0) bad++;

            // положительный контроль: видимое окно на том же событии заказывает
            results.Visible = true;
            Pump(200);
            WaitIdle(a.FsaSession);
            equilibrium = true;
            before = a.FsaSession.RunCount;
            a.RefreshView();
            Pump(500);
            int shownRuns = a.FsaSession.RunCount - before;
            WaitIdle(a.FsaSession);
            Pump(300);
            Console.WriteLine("  окно ВИДНО, настройка сменилась, вид документа обновлён: запусков счёта {0} (ждём 1)", shownRuns);
            if (shownRuns != 1) bad++;

            // 4в. выбор строки переживает обновление вида (такт набора)
            results.ShowResult(true);
            Pump(100);
            object selections = table.GetType().GetProperty("Selections").GetValue(table, null);
            selections.GetType().GetMethod("SelectCell", new[] { typeof(int), typeof(int) }).Invoke(selections, new object[] { 0, 0 });
            Pump(100);
            string highlight0 = Highlight(a);
            a.RefreshView();
            Pump(300);
            int[] picked = (int[])selections.GetType().GetProperty("SelectedIndicies").GetValue(selections, null);
            string highlight1 = Highlight(a);
            bool kept = picked != null && picked.Length > 0 && picked[0] == 0 && highlight1 == highlight0 && highlight0 != null;
            Console.WriteLine("  выбрана строка 0 (подсветка «{0}»), вид обновлён: выбрано {1}, подсветка «{2}»  {3}",
                              highlight0 ?? "нет", picked != null ? picked.Length : 0, highlight1 ?? "нет", kept ? "ok" : "НЕТ");
            if (!kept) bad++;

            // 4в′. СВЕДЕНИЕ (не судится — правка в EnergySpectrumView, не в окне): строка ряда
            // подсвечивает в стеке «дочерних» (умолчание вида) только слой с ТЕМ ЖЕ именем
            FsaPresentation daughters = FsaPresentationBuilder.Build(a.FsaSession.Result, FsaGrouping.Daughters, false);
            double lit = 0.0, chainShare = 0.0;
            int litCount = 0, chainCount = 0;
            foreach (FsaStackLayer layer in daughters.Layers)
            {
                if (highlight1 != null && string.Equals(layer.Name, highlight1, StringComparison.Ordinal)) { lit += layer.SharePercent; litCount++; }
                if (highlight1 != null && string.Equals(layer.ChainRoot, highlight1, StringComparison.Ordinal)) { chainShare += layer.SharePercent; chainCount++; }
            }
            Console.WriteLine("  СВЕДЕНИЕ: строка «{0}» в стеке дочерних зажигает слоёв {1} ({2} %), а слоёв её ряда {3} ({4} %)",
                              highlight1 ?? "нет", litCount, lit.ToString("F2", CultureInfo.InvariantCulture),
                              chainCount, chainShare.ToString("F2", CultureInfo.InvariantCulture));

            // 4д. пауза при записи (AMBER211): строки прежнего разбора помечены «пересчёт»
            WaitIdle(a.FsaSession);
            Pump(200);
            FsaAnalysisSession.AcquisitionIntervalOverride = 3600.0;
            ResultData rdA = a.ActiveResultData;
            rdA.ResultDataStatus.Recording = true;
            int runsBefore = a.FsaSession.RunCount;
            rdA.EnergySpectrum.Spectrum[100] += 10;           // такт набора: отпечаток сменился
            rdA.EnergySpectrum.TotalPulseCount += 10;
            rdA.EnergySpectrum.ValidPulseCount += 10;
            results.ShowResult(false);
            Pump(300);
            string staleMark = new System.ComponentModel.ComponentResourceManager(typeof(DCResultView)).GetString("DCResult_FsaStale");
            string head = HeaderText(results);
            bool paused = a.FsaSession.RunCount == runsBefore && !a.FsaSession.IsRunning;
            bool marked = staleMark != null && head.Contains(staleMark);
            Console.WriteLine("  запись, пауза 3600 с, спектр сменился: запусков {0} (ждём 0), заголовок «{1}» — пометка «{2}» {3}  {4}",
                              a.FsaSession.RunCount - runsBefore, head, staleMark, marked ? "есть" : "НЕТ", paused && marked ? "ok" : "НЕТ");
            if (!(paused && marked)) bad++;
            Shot(host, "recording_stale");
            int width = HeaderWidth(results);
            int textWidth = TextRenderer.MeasureText(head, new Font("Microsoft Sans Serif", 8F)).Width;
            Console.WriteLine("  ширина колонки заголовка {0} px, текст «{1}» {2} px  {3}", width, head, textWidth, width >= textWidth ? "ok" : "НЕТ (текст переносится и срезается)");
            if (width < textWidth) bad++;
            rdA.ResultDataStatus.Recording = false;
            FsaAnalysisSession.AcquisitionIntervalOverride = null;
            rdA.EnergySpectrum.Spectrum[100] -= 10;
            rdA.EnergySpectrum.TotalPulseCount -= 10;
            rdA.EnergySpectrum.ValidPulseCount -= 10;
            results.ShowResult(true);
            WaitIdle(a.FsaSession);
            Pump(300);

            // 4г. смена документа посреди счёта
            if (b != null)
            {
                FieldInfo gateField = typeof(FsaAnalysisSession).GetField("probeGate", Any);
                var gate = new ManualResetEvent(false);
                gateField.SetValue(null, gate);
                equilibrium = false;
                a.FsaSession.Reset();
                results.ShowResult(true);                     // A считает (стоит у задвижки)
                Pump(200);
                bool aRunning = a.FsaSession.IsRunning;
                mainForm.ActiveDocument = b;
                results.ShowResult(true);                     // B считает (стоит у задвижки)
                Pump(200);
                gateField.SetValue(null, null);
                gate.Set();
                WaitIdle(a.FsaSession);
                WaitIdle(b.FsaSession);
                Pump(500);
                List<string> names = Names(table);
                List<string> expected = BuildNames(b, nuclides);
                bool same = names.SequenceEqual(expected);
                Console.WriteLine("  A считал: {0}; документ сменён на B посреди счёта; строки окна: [{1}]; строки разбора B: [{2}]  {3}",
                                  aRunning, string.Join(", ", names), string.Join(", ", expected), same ? "ok" : "НЕТ");
                if (!same) bad++;

                // закрытие документа посреди счёта: окно без исключения и без строк прежнего
                gate = new ManualResetEvent(false);
                gateField.SetValue(null, gate);
                b.FsaSession.Reset();
                results.ShowResult(true);
                Pump(100);
                MethodInfo closed = typeof(DocEnergySpectrum).GetMethod("OnFormClosed", Any);
                closed.Invoke(b, new object[] { new FormClosedEventArgs(CloseReason.UserClosing) });
                mainForm.ActiveDocument = null;
                results.ShowResult(true);
                gateField.SetValue(null, null);
                gate.Set();
                WaitIdle(b.FsaSession);
                Pump(500);
                int rows = ((IList)table.GetType().GetProperty("Rows").GetValue(table, null)).Count;
                Console.WriteLine("  документ закрыт посреди счёта: строк в окне {0} (ждём 0)  {1}", rows, rows == 0 ? "ok" : "НЕТ");
                if (rows != 0) bad++;
            }

            host.Hide();
            results.Dispose();
            host.Dispose();
            a.Dispose();
            if (b != null) b.Dispose();
            mainForm.Dispose();
        }

        static void CompareTable(string what, DCResultView results, object table, DocEnergySpectrum doc, NuclideDefinitionManager nuclides)
        {
            ResultData rd = doc.ActiveResultData;
            FsaMeasurementResult.Summary summary;
            MeasurementResultCollection built = FsaMeasurementResult.Build(rd, doc.FsaSession.Result, nuclides.NuclideDefinitions,
                                                                           FsaMeasurementResult.Texts.Russian, out summary);
            MeasurementResultCollection bq = built != null ? new MeasurementResultManager().Translate(built, ResultTranslation.Becquerels) : null;
            IList rows = (IList)table.GetType().GetProperty("Rows").GetValue(table, null);
            int n = bq != null ? bq.ResultList.Count : 0;
            bool ok = rows.Count == n;
            Console.WriteLine("  [{0}] строк в окне {1}, в разборе {2}", what, rows.Count, n);
            for (int i = 0; i < Math.Min(n, rows.Count); i++)
            {
                IList cells = (IList)rows[i].GetType().GetProperty("Cells").GetValue(rows[i], null);
                string name = Text(cells[0]), value = Text(cells[1]), err = Text(cells[2]), mda = Text(cells[3]);
                MeasurementResult r = bq.ResultList[i];
                string expName = r.Line.Name;
                string expValue = r.ResultValue.ToString("f2", CultureInfo.InvariantCulture);
                string expMda = r.MDA > 0.0 ? r.MDA.ToString("f2", CultureInfo.InvariantCulture) : "0";
                bool rowOk = name == expName && value == expValue && mda == expMda;
                Console.WriteLine("    {0,-9} {1,11} {2,-24} MDA {3,10} | разбор {4,11} MDA {5,10}  {6}", name, value, err, mda, expValue, expMda, rowOk ? "ok" : "НЕТ");
                if (!rowOk) ok = false;
            }
            if (!ok) bad++;
        }

        static List<string> Names(object table)
        {
            var names = new List<string>();
            foreach (object row in (IList)table.GetType().GetProperty("Rows").GetValue(table, null))
            {
                IList cells = (IList)row.GetType().GetProperty("Cells").GetValue(row, null);
                names.Add(Text(cells[0]));
            }
            return names;
        }

        static List<string> BuildNames(DocEnergySpectrum doc, NuclideDefinitionManager nuclides)
        {
            FsaMeasurementResult.Summary summary;
            MeasurementResultCollection built = FsaMeasurementResult.Build(doc.ActiveResultData, doc.FsaSession.Result,
                                                                           nuclides.NuclideDefinitions, FsaMeasurementResult.Texts.Russian, out summary);
            return built != null ? built.ResultList.Select(r => r.Line.Name).ToList() : new List<string>();
        }

        /// <summary>Снимок окна в форме-носителе — в каталог --shots= (на диске, не в git).</summary>
        static void Shot(Form host, string tag)
        {
            if (shots == null) return;
            Directory.CreateDirectory(shots);
            using (var frame = new Bitmap(host.ClientSize.Width, host.ClientSize.Height))
            {
                host.DrawToBitmap(frame, new Rectangle(0, 0, frame.Width, frame.Height));
                string file = Path.Combine(shots, "p254_" + Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName + "_" + tag + ".png");
                frame.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                Console.WriteLine("  снимок: {0}", file);
            }
        }

        static int HeaderWidth(DCResultView view)
        {
            object model = Field(typeof(DCResultView), "columnModel1").GetValue(view);
            IList columns = (IList)model.GetType().GetProperty("Columns").GetValue(model, null);
            return (int)columns[0].GetType().GetProperty("Width").GetValue(columns[0], null);
        }

        static string HeaderText(DCResultView view)
        {
            object model = Field(typeof(DCResultView), "columnModel1").GetValue(view);
            IList columns = (IList)model.GetType().GetProperty("Columns").GetValue(model, null);
            return (string)columns[0].GetType().GetProperty("Text").GetValue(columns[0], null);
        }

        static string Highlight(DocEnergySpectrum doc)
        {
            return (string)typeof(EnergySpectrumView).GetProperty("FsaHighlight", Any).GetValue(doc.EnergySpectrumView, null);
        }

        static string Text(object cell)
        {
            return (string)cell.GetType().GetProperty("Text").GetValue(cell, null);
        }

        // ---- обвязка (образцы FsaReportViewProbe / DecayCorrP192) ----

        static void WaitIdle(FsaAnalysisSession session)
        {
            for (int i = 0; i < timeoutS * 20; i++)
            {
                if (!session.IsRunning)
                {
                    Application.DoEvents();
                    return;
                }
                Thread.Sleep(50);
                Application.DoEvents();
            }
            Console.WriteLine("  ⛔ сеанс не дошёл до покоя за {0} с", timeoutS);
            bad++;
        }

        static void Pump(int ms)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Application.DoEvents();
        }

        static Form Host(Form content, int width, int height)
        {
            var host = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
                ShowInTaskbar = false,
                ClientSize = new Size(width, height)
            };
            content.TopLevel = false;
            content.Dock = DockStyle.Fill;
            host.Controls.Add(content);
            content.Show();
            host.Show();
            return host;
        }

        static FieldInfo Field(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, Any);
                if (field != null) return field;
            }
            throw new InvalidOperationException("нет поля " + name + " у " + type.Name);
        }

        static string F(double v)
        {
            return double.IsNaN(v) ? "NaN" : v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
        {
            DocEnergySpectrum doc = null;
            ResultDataFile file = Read(path);
            if (file == null) return null;
            ResultData rd = file.ResultDataList[0];
            Prepare(rd, path, nuclides);
            GC.KeepAlive(doc);
            return rd;
        }

        static DocEnergySpectrum Open(string path, NuclideDefinitionManager nuclides)
        {
            ResultDataFile file = Read(path);
            if (file == null) return null;
            ResultData rd = file.ResultDataList[0];
            Prepare(rd, path, nuclides);
            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            return doc;
        }

        static ResultDataFile Read(string path)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("нет файла: " + path);
                return null;
            }
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return (ResultDataFile)serializer.Deserialize(stream);
            }
        }

        static void Prepare(ResultData rd, string path, NuclideDefinitionManager nuclides)
        {
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }
            Console.WriteLine("SETUP\t{0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }
                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }
            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
        }
    }
}
