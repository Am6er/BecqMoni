using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace FsaSessionProbe
{
    /// <summary>
    /// СТОРОЖ СЕАНСА РАЗБОРА И ПОСТРОИТЕЛЯ ПРЕДСТАВЛЕНИЯ (`A145`, этап 2).
    ///
    ///     fsasessionprobe --spectrum=&lt;спектр с рядом Th-232&gt; --control=&lt;спектр без ряда&gt;
    ///
    /// Четыре раздела, каждый с положительным контролем:
    ///
    ///   1. ОТПЕЧАТОК (критерий 7). На настоящем спектре: 128 раскладок семи
    ///      настроек расчёта дают 128 разных отпечатков сеанса, каждая
    ///      одиночная перестановка меняет его; а переключение
    ///      родители/дочерние не трогает ни отпечаток, ни сеанс — меняется
    ///      только представление.
    ///   2. БЫСТРАЯ СЕРИЯ (критерий 10). N смен настроек, пока первый счёт
    ///      стоит на задвижке: по окончании — РОВНО ОДНА публикация, отпечаток
    ///      последнего снимка, и результат действительно посчитан последними
    ///      настройками (по ним ряд связан, по первым — нет).
    ///   3. СМЕНА СПЕКТРА ВО ВРЕМЯ СЧЁТА (критерий 10). Счёт спектра A стоит
    ///      на задвижке, сеанс сброшен (как при смене активного спектра),
    ///      заказан спектр B: результат A не публикуется, публикуется B.
    ///      Положительный контроль — тот же счёт A без сброса ПУБЛИКУЕТСЯ, а
    ///      со сбросом и без нового заказа — отвергается (устаревший результат
    ///      подложен и отброшен по поколению).
    ///   4. ПОСТРОИТЕЛЬ (критерий 8). На Th-232 при NucBase + равновесии
    ///      родительские ленты и доли равны сумме дочерних с машинным допуском;
    ///      подложенная неполная сумма расходится; у спектра без ряда родители
    ///      недоступны с причиной.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, controlPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--control=", StringComparison.Ordinal)) controlPath = a.Substring(10);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null || controlPath == null)
            {
                Console.Error.WriteLine("нужны --spectrum=<файл с рядом Th-232> и --control=<файл без ряда>");
                return 2;
            }

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ResultData thorium = Load(spectrumPath, nuclides);
            ResultData control = Load(controlPath, nuclides);
            if (thorium == null || control == null)
            {
                return 2;
            }

            StampSection(thorium);
            SeriesSection(thorium);
            SwitchSection(thorium, control);
            BuilderSection(thorium, control);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 1. ОТПЕЧАТОК
        // ------------------------------------------------------------------

        static void StampSection(ResultData rd)
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. отпечаток сеанса: настройки расчёта в нём, группировка — нет ===");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            int collisions = 0, unchanged = 0;
            for (int mask = 0; mask < 128; mask++)
            {
                string stamp = FsaAnalysisSession.BuildStamp(rd, true, OptionsOf(mask));
                if (!seen.Add(stamp))
                {
                    collisions++;
                }

                for (int bit = 0; bit < 7; bit++)
                {
                    if (FsaAnalysisSession.BuildStamp(rd, true, OptionsOf(mask ^ (1 << bit))) == stamp)
                    {
                        unchanged++;
                    }
                }
            }

            Console.WriteLine("  раскладок 128, разных отпечатков {0}, одиночных перестановок без смены {1}",
                              seen.Count, unchanged);
            Same("128 раскладок настроек — 128 разных отпечатков сеанса", 128, seen.Count);
            Same("столкновений нет", 0, collisions);
            Same("каждый расчётный флаг меняет отпечаток", 0, unchanged);

            // Группировка: считаем один раз, потом строим представление в обоих
            // положениях — сеанс обязан остаться нетронутым.
            var session = new FsaAnalysisSession();
            int completed = 0;
            session.Completed += (s, e) => Interlocked.Increment(ref completed);
            FsaCalculationOptions linked = new FsaCalculationOptions { DbLookups = true, ChainEquilibrium = true };
            FsaResult result = RunToIdle(session, rd, linked);
            Same("разбор с NucBase+равновесием получился", true, result != null);
            string before = session.Stamp;
            int completedBefore = completed;

            FsaPresentation daughters = FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, false);
            FsaPresentation parents = FsaPresentationBuilder.Build(result, FsaGrouping.Parents, false);
            Console.WriteLine("  слоёв: дочерние {0}, родители {1}; строк: {2} и {3}",
                              daughters.Layers.Count, parents.Layers.Count,
                              daughters.Rows.Count, parents.Rows.Count);
            Same("родительский режим применён", FsaGrouping.Parents, parents.Grouping);
            Same("представление изменилось (слоёв стало меньше)", true,
                 parents.Layers.Count < daughters.Layers.Count);
            Same("отпечаток сеанса не изменился", before, session.Stamp);
            Same("сеанс не считал заново (событий не прибавилось)", completedBefore, completed);
            Same("сеанс не занят", false, session.IsRunning);
            Same("а тот же спектр с теми же настройками сеанс считает актуальным",
                 true, session.IsUpToDate(rd, false) || FsaAnalysisSession.BuildStamp(rd, false, linked) == session.Stamp);
        }

        // ------------------------------------------------------------------
        // 2. БЫСТРАЯ СЕРИЯ
        // ------------------------------------------------------------------

        static void SeriesSection(ResultData rd)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. быстрая серия смен настроек: одна публикация, отпечаток последней ===");

            var session = new FsaAnalysisSession();
            var published = new List<string>();
            var idle = new AutoResetEvent(false);
            session.Completed += (s, e) =>
            {
                lock (published)
                {
                    published.Add(session.Result != null ? session.Stamp : "(без результата: " + session.Status + ")");
                }

                idle.Set();
            };

            // Серия из восьми снимков: первый — по подписям пиков (ряда нет),
            // последний — NucBase + равновесие (ряд связан). Между ними —
            // перестановки флажков, все разные.
            var series = new List<FsaCalculationOptions>
            {
                new FsaCalculationOptions(),
                new FsaCalculationOptions { PileUp = false },
                new FsaCalculationOptions { Backscatter = false },
                new FsaCalculationOptions { EscapeAndAnnihilation = false },
                new FsaCalculationOptions { AtomicXray = false },
                new FsaCalculationOptions { CascadeSumming = false },
                new FsaCalculationOptions { DbLookups = true, ChainEquilibrium = false },
                new FsaCalculationOptions { DbLookups = true, ChainEquilibrium = true }
            };

            using (var gate = new ManualResetEvent(false))
            {
                Gate(gate);
                try
                {
                    foreach (FsaCalculationOptions options in series)
                    {
                        session.EnsureUpToDate(rd, false, options);
                    }

                    Same("пока задвижка закрыта: сеанс занят", true, session.IsRunning);
                    Same("пока задвижка закрыта: результата нет", true, session.Result == null);
                    Same("пока задвижка закрыта: публикаций нет", 0, published.Count);
                    gate.Set();
                    Same("серия дошла до покоя", true, WaitIdle(session, idle));
                }
                finally
                {
                    Gate(null);
                }
            }

            string last = FsaAnalysisSession.BuildStamp(rd, false, series[series.Count - 1]);
            string first = FsaAnalysisSession.BuildStamp(rd, false, series[0]);
            Console.WriteLine("  публикаций {0}; отпечаток сеанса …{1}", published.Count, Tail(session.Stamp));
            Same("публикация ровно одна на всю серию", 1, published.Count);
            Same("отпечаток сеанса — от ПОСЛЕДНЕГО снимка", last, session.Stamp);
            Same("и не от первого", false, session.Stamp == first);
            Same("результат есть", true, session.Result != null);
            Same("результат посчитан последними настройками: ряд связан, родители допустимы",
                 true, session.Result != null && session.Result.ParentGroupingAllowed);
            Same("сеанс свободен", false, session.IsRunning);

            // Контроль: первый снимок в одиночку — ряда НЕ даёт. Иначе проверка
            // выше прошла бы и тогда, когда результат остался от первого счёта.
            var alone = new FsaAnalysisSession();
            FsaResult byPeaks = RunToIdle(alone, rd, series[0]);
            Same("контроль: по первым настройкам (подписи пиков) родители НЕдопустимы",
                 false, byPeaks != null && byPeaks.ParentGroupingAllowed);
        }

        // ------------------------------------------------------------------
        // 3. СМЕНА СПЕКТРА ВО ВРЕМЯ СЧЁТА
        // ------------------------------------------------------------------

        static void SwitchSection(ResultData a, ResultData b)
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. смена спектра во время счёта: старый результат не подмешан ===");
            int channelsA = a.EnergySpectrum.NumberOfChannels;
            int channelsB = b.EnergySpectrum.NumberOfChannels;
            Console.WriteLine("  спектр A: {0} каналов; спектр B: {1} каналов", channelsA, channelsB);
            Same("спектры различимы по числу каналов", true, channelsA != channelsB);

            var options = new FsaCalculationOptions();

            // (а) A на задвижке, сброс, заказ B, задвижка открыта.
            {
                var session = new FsaAnalysisSession();
                int events = 0;
                var idle = new AutoResetEvent(false);
                session.Completed += (s, e) => { Interlocked.Increment(ref events); idle.Set(); };
                using (var gate = new ManualResetEvent(false))
                {
                    Gate(gate);
                    try
                    {
                        session.EnsureUpToDate(a, false, options);
                        Same("(а) счёт A идёт", true, session.IsRunning);
                        session.Reset();
                        session.EnsureUpToDate(b, false, options);
                        gate.Set();
                        Same("(а) дошло до покоя", true, WaitIdle(session, idle));
                    }
                    finally
                    {
                        Gate(null);
                    }
                }

                FsaResult result = session.Result;
                Same("(а) результат есть", true, result != null);
                Same("(а) результат — спектра B (по числу каналов)", channelsB,
                     result != null && result.Model != null ? result.Model.Length : -1);
                Same("(а) отпечаток — спектра B", FsaAnalysisSession.BuildStamp(b, false, options), session.Stamp);
                Same("(а) событий завершения — одно", 1, events);
            }

            // (б) ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: A на задвижке, сброс, БЕЗ нового заказа —
            //     устаревший результат обязан быть отвергнут по поколению.
            {
                var session = new FsaAnalysisSession();
                var idle = new AutoResetEvent(false);
                session.Completed += (s, e) => idle.Set();
                using (var gate = new ManualResetEvent(false))
                {
                    Gate(gate);
                    try
                    {
                        session.EnsureUpToDate(a, false, options);
                        session.Reset();
                        gate.Set();
                        Same("(б) дошло до покоя", true, WaitIdle(session, idle));
                    }
                    finally
                    {
                        Gate(null);
                    }
                }

                Same("(б) устаревший результат A отвергнут: результата нет", true, session.Result == null);
                Same("(б) отпечаток пуст", "", session.Stamp);
            }

            // (в) КОНТРОЛЬ КОНТРОЛЯ: тот же счёт A без сброса — публикуется.
            //     Без него (б) прошло бы и на сеансе, который не публикует ничего.
            {
                var session = new FsaAnalysisSession();
                var idle = new AutoResetEvent(false);
                session.Completed += (s, e) => idle.Set();
                using (var gate = new ManualResetEvent(false))
                {
                    Gate(gate);
                    try
                    {
                        session.EnsureUpToDate(a, false, options);
                        gate.Set();
                        Same("(в) дошло до покоя", true, WaitIdle(session, idle));
                    }
                    finally
                    {
                        Gate(null);
                    }
                }

                Same("(в) без сброса результат A опубликован", channelsA,
                     session.Result != null && session.Result.Model != null ? session.Result.Model.Length : -1);
            }

            // (г) Устаревший результат ПОДЛОЖЕН напрямую: поколение поднято
            //     отражением, без сброса очереди, — счёт всё равно молчит.
            {
                var session = new FsaAnalysisSession();
                var idle = new AutoResetEvent(false);
                session.Completed += (s, e) => idle.Set();
                using (var gate = new ManualResetEvent(false))
                {
                    Gate(gate);
                    try
                    {
                        session.EnsureUpToDate(a, false, options);
                        FieldInfo generation = typeof(FsaAnalysisSession).GetField(
                            "generation", BindingFlags.Instance | BindingFlags.NonPublic);
                        generation.SetValue(session, (int)generation.GetValue(session) + 1);
                        gate.Set();
                        Same("(г) дошло до покоя", true, WaitIdle(session, idle));
                    }
                    finally
                    {
                        Gate(null);
                    }
                }

                Same("(г) подложенный чужому поколению результат отвергнут", true, session.Result == null);
            }
        }

        // ------------------------------------------------------------------
        // 4. ПОСТРОИТЕЛЬ
        // ------------------------------------------------------------------

        static void BuilderSection(ResultData thorium, ResultData control)
        {
            Console.WriteLine();
            Console.WriteLine("=== 4. построитель: родители = сумма дочерних; без ряда — недоступны ===");

            var linked = new FsaCalculationOptions { DbLookups = true, ChainEquilibrium = true };
            FsaResult result = RunToIdle(new FsaAnalysisSession(), thorium, linked);
            if (result == null)
            {
                Console.WriteLine("  ⛔ разбор Th-232 не получился — раздел пуст");
                bad++;
                return;
            }

            Same("Th-232, NucBase+равновесие: родители допустимы", true, result.ParentGroupingAllowed);
            Console.WriteLine("  причина отказа: {0}", result.ParentGroupingRefusal ?? "(нет)");

            FsaPresentation daughters = FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, false);
            FsaPresentation parents = FsaPresentationBuilder.Build(result, FsaGrouping.Parents, false);
            List<FsaStackLayer> full = result.BuildStackedLayers(int.MaxValue);

            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (FsaStackLayer layer in full)
            {
                if (!string.IsNullOrEmpty(layer.DecayChainRoot) && layer.Kind != FsaComponentKind.Nuisance)
                {
                    roots.Add(layer.DecayChainRoot);
                }
            }

            Console.WriteLine("  корней ряда среди дочерних слоёв: {0} ({1})", roots.Count, string.Join(", ", roots));
            Same("хотя бы один корень ряда", true, roots.Count > 0);

            double worstCurve = 0.0, worstSum = 0.0, worstShare = 0.0;
            foreach (string root in roots)
            {
                FsaStackLayer parent = parents.Layers.Find(
                    l => string.Equals(l.DecayChainRoot, root, StringComparison.Ordinal));
                Same("родительская строка «" + root + "» есть", true, parent != null);
                if (parent == null)
                {
                    continue;
                }

                Same("  её имя — корень ряда", root, parent.Name);
                int members = 0;
                double[] curve = new double[result.Model.Length];
                double[] sums = null;
                double share = 0.0;
                foreach (FsaStackLayer layer in full)
                {
                    if (!string.Equals(layer.DecayChainRoot, root, StringComparison.Ordinal)
                        || layer.Kind == FsaComponentKind.Nuisance)
                    {
                        continue;
                    }

                    members++;
                    Add(curve, layer.Curve);
                    if (layer.SumPeakCurve != null)
                    {
                        sums = sums ?? new double[result.Model.Length];
                        Add(sums, layer.SumPeakCurve);
                    }

                    share += layer.SharePercent;
                }

                double dCurve = RelDiff(parent.Curve, curve);
                double dSum = sums == null ? (parent.SumPeakCurve == null ? 0.0 : 1.0) : RelDiff(parent.SumPeakCurve, sums);
                double dShare = Math.Abs(parent.SharePercent - share);
                worstCurve = Math.Max(worstCurve, dCurve);
                worstSum = Math.Max(worstSum, dSum);
                worstShare = Math.Max(worstShare, dShare);
                Console.WriteLine("  {0}: членов {1}, доля {2:F3} % = Σ {3:F3} %; |Δкривой|/max {4:E1}, |Δсумм-пиков|/max {5:E1}, |Δдоли| {6:E1}",
                                  root, members, parent.SharePercent, share, dCurve, dSum, dShare);

                // ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: неполная сумма (без одного члена)
                // обязана РАЗОЙТИСЬ, иначе допуск ничего не мерит.
                if (members > 1)
                {
                    double[] partial = new double[result.Model.Length];
                    int skipped = 0;
                    foreach (FsaStackLayer layer in full)
                    {
                        if (!string.Equals(layer.DecayChainRoot, root, StringComparison.Ordinal)
                            || layer.Kind == FsaComponentKind.Nuisance)
                        {
                            continue;
                        }

                        if (skipped == 0 && layer.SharePercent > 0.0)
                        {
                            skipped++;
                            continue;
                        }

                        Add(partial, layer.Curve);
                    }

                    Denies("  контроль: сумма без одного члена НЕ сходится с родителем",
                           RelDiff(parent.Curve, partial) <= 1e-9);
                }
            }

            Same("родительские кривые = Σ дочерних (допуск 1e-9)", true, worstCurve <= 1e-9);
            Same("родительские сумм-пики = Σ дочерних (допуск 1e-9)", true, worstSum <= 1e-9);
            Same("родительские доли = Σ дочерних (допуск 1e-9 %)", true, worstShare <= 1e-9);

            // Верх стека одинаков в обоих режимах: слияние не меняет модели.
            double[] topD = new double[result.Model.Length], topP = new double[result.Model.Length];
            foreach (FsaStackLayer l in daughters.Layers) Add(topD, l.Curve);
            foreach (FsaStackLayer l in parents.Layers) Add(topP, l.Curve);
            double dTop = RelDiff(topD, topP);
            Console.WriteLine("  верх стека дочерние против родители: |Δ|/max {0:E1}", dTop);
            Same("верх стека одинаков в обоих режимах (допуск 1e-9)", true, dTop <= 1e-9);

            // Все семь родов строк представлены хотя бы на одной из двух сцен
            // (Th-232 без фона: слои, сумм-пики при матрице, необнаруженные,
            // свёрнутые, «без фона», невязка, качество).
            var kinds = new HashSet<FsaReportRowKind>();
            foreach (FsaReportRow row in daughters.Rows) kinds.Add(row.Kind);
            foreach (FsaReportRow row in parents.Rows) kinds.Add(row.Kind);
            Console.WriteLine("  роды строк: {0}", string.Join(", ", kinds));
            Same("строки состава есть", true, kinds.Contains(FsaReportRowKind.Layer));
            Same("строка невязки есть", true, kinds.Contains(FsaReportRowKind.Residual));
            Same("строка качества есть", true, kinds.Contains(FsaReportRowKind.Quality));
            Same("строка «без фона» есть (фон не подавался)", true, kinds.Contains(FsaReportRowKind.NoBackground));
            int sumRows = 0, sumLayers = 0;
            foreach (FsaReportRow row in daughters.Rows) if (row.Kind == FsaReportRowKind.SumPeaks) sumRows++;
            foreach (FsaStackLayer l in daughters.Layers) if (l.SumPeakCurve != null) sumLayers++;
            Same("строк сумм-пиков столько же, сколько слоёв с сумм-пиками", sumLayers, sumRows);
            Same("строка качества — полный текст построителя", daughters.QualityText,
                 daughters.Rows[daughters.Rows.Count - 1].Name);
            Same("строка качества без многоточия", false,
                 daughters.QualityText.IndexOf('…') >= 0 || daughters.QualityText.IndexOf("...", StringComparison.Ordinal) >= 0);
            Same("порядок строк: слои идут первыми и их столько же, сколько слоёв",
                 daughters.Layers.Count, LeadingLayerRows(daughters.Rows));
            Same("цвет строки состава = цвет слоя из одной раздачи", true, ColorsAgree(daughters));
            Same("цвет строки состава = цвет слоя (родители)", true, ColorsAgree(parents));

            // Спектр без ряда: родители недоступны с причиной.
            FsaResult single = RunToIdle(new FsaAnalysisSession(), control, linked);
            Same("контроль (без ряда): разбор получился", true, single != null);
            if (single != null)
            {
                FsaPresentation asked = FsaPresentationBuilder.Build(single, FsaGrouping.Parents, false);
                Console.WriteLine("  контроль: причина отказа «{0}»", single.ParentGroupingRefusal);
                Same("контроль: родители недопустимы", false, single.ParentGroupingAllowed);
                Same("контроль: причина названа", true, !string.IsNullOrEmpty(single.ParentGroupingRefusal));
                Same("контроль: при запросе родителей показаны дочерние", FsaGrouping.Daughters, asked.Grouping);
                Same("контроль: запрошенное запомнено", FsaGrouping.Parents, asked.RequestedGrouping);
            }

            // И Th-232 по подписям пиков (без NucBase): ряда в составе нет.
            FsaResult byPeaks = RunToIdle(new FsaAnalysisSession(), thorium, new FsaCalculationOptions());
            Same("Th-232 по подписям пиков: родители недопустимы", false,
                 byPeaks != null && byPeaks.ParentGroupingAllowed);
        }

        static int LeadingLayerRows(List<FsaReportRow> rows)
        {
            int n = 0;
            foreach (FsaReportRow row in rows)
            {
                if (row.Kind != FsaReportRowKind.Layer)
                {
                    break;
                }

                n++;
            }

            return n;
        }

        static bool ColorsAgree(FsaPresentation presentation)
        {
            foreach (FsaReportRow row in presentation.Rows)
            {
                if (row.Kind == FsaReportRowKind.Layer
                    && row.Color != presentation.ColorOf(row.Layer.Name))
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Оснастка
        // ------------------------------------------------------------------

        /// <summary>Поставить/снять задвижку фонового счёта (закрытое поле сеанса).</summary>
        static void Gate(WaitHandle gate)
        {
            FieldInfo f = typeof(FsaAnalysisSession).GetField("probeGate",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (f == null)
            {
                throw new InvalidOperationException("нет FsaAnalysisSession.probeGate — проба смотрит не туда");
            }

            f.SetValue(null, gate);
        }

        /// <summary>Посчитать синхронно: заказ и ожидание покоя.</summary>
        static FsaResult RunToIdle(FsaAnalysisSession session, ResultData rd, FsaCalculationOptions options)
        {
            var idle = new AutoResetEvent(false);
            EventHandler handler = (s, e) => idle.Set();
            session.Completed += handler;
            try
            {
                session.EnsureUpToDate(rd, false, options);
                if (!WaitIdle(session, idle))
                {
                    Console.WriteLine("  ⛔ сеанс не дошёл до покоя за отведённое время");
                    bad++;
                }
            }
            finally
            {
                session.Completed -= handler;
            }

            if (session.Result == null)
            {
                Console.WriteLine("  состояние сеанса: {0}", session.Status);
            }

            return session.Result;
        }

        static bool WaitIdle(FsaAnalysisSession session, AutoResetEvent idle)
        {
            for (int i = 0; i < 600; i++)
            {
                if (!session.IsRunning)
                {
                    return true;
                }

                idle.WaitOne(100);
            }

            return !session.IsRunning;
        }

        static FsaCalculationOptions OptionsOf(int mask)
        {
            return new FsaCalculationOptions
            {
                DbLookups = (mask & 1) != 0,
                ChainEquilibrium = (mask & 2) != 0,
                AtomicXray = (mask & 4) != 0,
                CascadeSumming = (mask & 8) != 0,
                Backscatter = (mask & 16) != 0,
                EscapeAndAnnihilation = (mask & 32) != 0,
                PileUp = (mask & 64) != 0
            };
        }

        static void Add(double[] target, double[] source)
        {
            if (source == null) return;
            for (int i = 0; i < target.Length && i < source.Length; i++)
            {
                target[i] += source[i];
            }
        }

        /// <summary>max|a−b| / max(|a|,|b|); обе пустые — 0; одна пустая — 1.</summary>
        static double RelDiff(double[] a, double[] b)
        {
            if (a == null || b == null)
            {
                return a == b ? 0.0 : 1.0;
            }

            double diff = 0.0, scale = 0.0;
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                double x = i < a.Length ? a[i] : 0.0;
                double y = i < b.Length ? b[i] : 0.0;
                diff = Math.Max(diff, Math.Abs(x - y));
                scale = Math.Max(scale, Math.Max(Math.Abs(x), Math.Abs(y)));
            }

            return scale > 0.0 ? diff / scale : 0.0;
        }

        static string Tail(string stamp)
        {
            if (string.IsNullOrEmpty(stamp)) return "(пусто)";
            string[] parts = stamp.Split('|');
            return parts.Length >= 3 ? parts[parts.Length - 3] : stamp;
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("нет файла: " + path);
                return null;
            }

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

            // Прибор и его настройки поиска — одним правилом на все пробы (`S82`).
            Console.WriteLine("SETUP\t{0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            // Пики — как в окне: ими подписывается состав (`S57`), и сеанс
            // читает их из `DetectedPeaks`.
            rd.DetectedPeaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            Console.WriteLine("SETUP\t{0}: пиков {1}, каналов {2}", Path.GetFileName(path),
                              rd.DetectedPeaks.Count, rd.EnergySpectrum.NumberOfChannels);
            return rd;
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-72} {2}{3}", ok ? "ok  " : "⛔ ", what, got,
                              ok ? string.Empty : "  вместо " + expected);
            if (!ok) bad++;
        }

        static void Denies(string what, bool found)
        {
            Console.WriteLine("  {0} {1,-72} {2}", found ? "⛔ " : "ok  ", what,
                              found ? "СТОРОЖ ПРОМОЛЧАЛ" : "отказал, как и должен");
            if (found) bad++;
        }
    }
}
