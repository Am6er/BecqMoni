using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using XPTable.Models;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER320`, полоса a320, 08.11.2026) СТРОКИ-ПОТОМКИ ОТЧЁТА FSA — СВЁРНУТЫ, РАСКРЫВАЮТСЯ
    /// СТРЕЛКОЙ У РОДИТЕЛЯ. Решения Amber 07.11.2026 вопросником, дословно: «Все потомки»;
    /// «Сумму и число строк (Рекомендую)»; «Помнить до конца сеанса (Рекомендую)».
    ///
    /// Окно отчёта (<c>FSAReportView</c>) — настоящее, на документе пробы, разбор — настоящий
    /// сеанс документа (<c>FsaSession</c>), щелчок — настоящий <c>Table.OnMouseDown</c>/<c>OnMouseUp</c>
    /// (приём П290). Судится:
    ///   К1 свёрнуто по умолчанию: каждая строка с отступом (`Indent > 0`) — подстрока
    ///      (`Row.Parent`) свёрнутого родителя;
    ///   К2 свёрнутый родитель: подпись «{подпись} ({n})», значение — Σ отсчётов потомков их же
    ///      форматом (сверка с `FsaReportRow.Counts` потомков и с их напечатанными числами);
    ///   К3 щелчок по стрелке раскрывает (подпись и значение — прежние, потомки видны), второй —
    ///      сворачивает; щелчок по ТЕКСТУ родителя группу не трогает;
    ///   К4 скрытый потомок не ловит мышь: точка сразу под свёрнутым родителем — строка ПОСЛЕ группы;
    ///   К5 память раскрытия: пересчёт сеанса, смена документа и возврат; «запуск» (память
    ///      забыта, новое окно) — всё свёрнуто;
    ///   К6 счёт скрытых строк XPTable (`HiddenSubRows`, полоса прокрутки) после перестроек — верен;
    ///   К7 выбор потомка после раскрытия: выбран он, подсветка графика — его слой; щелчок по уже
    ///      выбранному потомку возвращает подсветку (П290); свёртка при выбранном потомке —
    ///      выбор на родителе, подсветка та же;
    ///   К8 подсказки: у потомка — своя подсказка модели; у свёрнутого родителя — прежняя и
    ///      строка о свёртке с числом.
    ///
    ///   FsaRowGroupsProbeA320 --spectrum=&lt;xml&gt; [--set=&lt;набор&gt;] [--ui=en|ru]
    ///                         [--png=&lt;префикс&gt;] [--timeout=300]
    ///
    ///   К9 заголовок собственной активности кристалла (строки подложены в результат): свёрнут,
    ///      сумма отсчётов и число строк — решение Amber 08.11.2026 «Сумма отсчётов (Рекомендую)»;
    ///   К10 клавиши у выбранной строки: → ← Enter Пробел по обычаю дерева Windows, ↓ — штатно мимо
    ///      скрытых, у строки без группы → и Enter групп не трогают;
    ///   К11 строки-родители без потомков — текст вровень со строками со стрелкой.
    ///
    ///   [--store=&lt;склад матриц по guid&gt;] — матрица кривой в память, как у P195;
    ///   [--spoil=expanded] — положительный контроль сторожа: группы раскрыты до проверок;
    ///   [--delay-completed=&lt;мс&gt;] — событие конца счёта доходит до окна позже (как под нагрузкой);
    ///   ожидание пробы — по признаку готовности (`Settle`), не по паузе.
    ///
    /// Вывод — в обычае check_fsa_report_view.py: «  ok   …» / «  ⛔  …», последняя строка
    /// «ВСЕ СОШЛИСЬ» / «НЕ СОШЛОСЬ: N». Коды: 0 — все критерии; 1 — есть отказы (на сборке «до» —
    /// положительный контроль); 2 — мерить нечем (нет спектра, матрицы, разбора или строк с отступом).
    /// ⚠ Окна пробы — на скрытом рабочем столе (`HiddenRun.exe`), на экран не выводятся.
    /// </summary>
    static class FsaRowGroupsProbeA320
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int Width = 1200, Height = 620;
        static int timeoutS = 300;
        static int offRule, rigFail;

        static DocEnergySpectrum doc;
        static EnergySpectrumView view;
        static FSAReportView report;
        static Func<FsaCalculationOptions> make;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string path = null, setName = null, ui = "en", png = null, spoil = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--ui=", StringComparison.Ordinal)) ui = a.Substring(5);
                else if (a.StartsWith("--png=", StringComparison.Ordinal)) png = a.Substring(6);
                else if (a.StartsWith("--store=", StringComparison.Ordinal)) storeDir = a.Substring(8);
                else if (a.StartsWith("--spoil=", StringComparison.Ordinal)) spoil = a.Substring(8);
                else if (a.StartsWith("--delay-completed=", StringComparison.Ordinal)) delayCompletedMs = int.Parse(a.Substring(18), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (path == null || (ui != "en" && ui != "ru") || (spoil != null && spoil != "expanded"))
            {
                Console.Error.WriteLine("нужен --spectrum=; --ui= — en или ru; --spoil= — только expanded");
                return 2;
            }

            CultureInfo uiCulture = CultureInfo.GetCultureInfo(ui == "ru" ? "ru-RU" : "en-US");
            Thread.CurrentThread.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = uiCulture;

            Application.EnableVisualStyles();
            // (четвёртый заход) контекст потока окон — для маркеров готовности (`Settle`)
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }
            uiSync = SynchronizationContext.Current;
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            if (setName != null && !SelectSet(nuclides, setName)) return 2;

            // «Запуск программы»: памяти раскрытия нет. На сборке «до» такого входа нет — это
            // тоже положительный контроль, ниже он считается отказом К5.
            bool api = Forget();
            Console.WriteLine("SETUP\tвход памяти раскрытия (ForgetExpandedGroups): {0}", api ? "есть" : "НЕТ");

            doc = Open(path, nuclides);
            if (doc == null) return 2;
            if (!Compute(doc)) return 2;
            view = doc.EnergySpectrumView;
            Setup(view, doc.ActiveResultData, doc.FsaSession.Result);

            report = new FSAReportView(null);
            Form host = Host(report, 460, 900, -4000);
            report.SetDocument(doc);
            Track(doc.FsaSession);
            Settle(doc.FsaSession, "окно на документе");

            List<int> parents = Parents();
            Console.WriteLine("SETUP\tстрок в таблице {0}, с отступом {1}, родителей-групп {2}",
                              Rows().Count, IndentedRows().Count, parents.Count);
            if (IndentedRows().Count == 0)
            {
                Console.WriteLine("МЕРИТЬ НЕЧЕМ: на этом спектре нет строк с отступом (сумм-пиков, вылета, рассеяния)");
                return 2;
            }

            // ⛔ Положительный контроль сторожа (`check_fsa_report_view --selftest`): дефект «раскрыто
            // по умолчанию» подсаживается входом самого окна — проба обязана отказать.
            if (spoil == "expanded")
            {
                Console.WriteLine("SETUP\t--spoil=expanded: все группы раскрыты до проверок — подсаженный дефект");
                SetAll(true);
            }

            if (png != null) Shot(host, png + "_collapsed.png");

            // ---- К1 свёрнуто по умолчанию ----
            Step("К1", "строки с отступом скрыты по умолчанию");
            int hiddenOk = 0, hiddenBad = 0;
            foreach (int i in IndentedRows())
            {
                Row r = Rows()[i];
                if (r.Parent != null && !r.Parent.ExpandSubRows) hiddenOk++;
                else
                {
                    hiddenBad++;
                    if (hiddenBad <= 5) Console.WriteLine("    видна: «{0}» (подстрока: {1})", r.Cells[0].Text, r.Parent != null ? "да" : "нет");
                }
            }
            Judge(hiddenBad == 0, string.Format(CultureInfo.InvariantCulture, "скрыто {0} из {1}", hiddenOk, hiddenOk + hiddenBad));

            // ---- К2 подпись и значение свёрнутого родителя ----
            Step("К2", "свёрнутый родитель: подпись с числом строк, значение — Σ отсчётов потомков");
            foreach (int p in parents) CheckCollapsedParent(p);
            if (parents.Count == 0) Judge(false, "родителей-групп нет (строки с отступом стоят сами по себе)");

            // ---- К8 подсказки ----
            Step("К8", "подсказки потомков и свёрнутых родителей");
            CheckTooltips();

            if (parents.Count == 0)
            {
                return Finish(host);
            }

            int g = parents[0];
            string gKey = KeyOf(Rows()[g]);

            // ---- К4 скрытый потомок не ловит мышь ----
            Step("К4", "точка под свёрнутым родителем — строка после группы");
            {
                Row pr = Rows()[g];
                Rectangle rect = CellRect(g);
                int after = pr.Index + pr.SubRows.Count + 1;
                int hit = report.ReportTable.RowIndexAt(rect.X + rect.Width / 2, rect.Bottom + 2);
                Judge(hit == after || (after >= Rows().Count && hit == -1),
                      string.Format(CultureInfo.InvariantCulture, "под строкой {0} — строка {1}, ждём {2}", g, hit, after));
            }

            // ---- К3 стрелка раскрывает, текст — нет ----
            Step("К3", "щелчок по тексту родителя группу не трогает; по стрелке — раскрывает и сворачивает");
            ClickText(g);
            Judge(!Rows()[g].ExpandSubRows, "после щелчка по тексту свёрнута");
            ClickArrow(g);
            CheckExpandedParent(g);
            if (png != null) { ExpandAll(); Shot(host, png + "_expanded.png"); CollapseAll(); ClickArrow(g); }
            ClickArrow(g);
            Judge(!Rows()[g].ExpandSubRows, "второй щелчок по стрелке — свёрнута");
            CheckCollapsedParent(g);

            // ---- К7 выбор и подсветка потомка ----
            Step("К7", "выбор потомка после раскрытия, П290, свёртка при выбранном потомке");
            ClickArrow(g);
            int child = g + 1;
            FsaReportRow childModel = Rows()[child].Tag as FsaReportRow;
            string childLayer = childModel != null && childModel.Layer != null ? childModel.Layer.Name : null;
            ClickText(child);
            Judge(Rows()[child].AnyCellsSelected, "потомок выбран");
            Judge(string.Equals(report.SelectedComponent, childLayer, StringComparison.Ordinal)
                  && string.Equals(HighlightOf(), childLayer, StringComparison.Ordinal),
                  string.Format("выбор окна «{0}», подсветка графика «{1}», ждём «{2}»", report.SelectedComponent, HighlightOf(), childLayer));
            typeof(EnergySpectrumView).GetMethod("SetFsaHighlight", Any).Invoke(view, new object[] { null, false });   // подсветку сменило «другое окно»
            ClickText(child);                     // щелчок по уже выбранной строке (П290)
            Judge(string.Equals(HighlightOf(), childLayer, StringComparison.Ordinal),
                  string.Format("повторный щелчок по выбранному потомку — подсветка «{0}», ждём «{1}»", HighlightOf(), childLayer));
            ClickArrow(g);
            Judge(!Rows()[g].ExpandSubRows && Rows()[g].AnyCellsSelected && !Rows()[child].AnyCellsSelected,
                  "свёртка при выбранном потомке — выбор на родителе");
            Judge(string.Equals(HighlightOf(), childLayer, StringComparison.Ordinal),
                  string.Format("подсветка после свёртки «{0}», ждём «{1}»", HighlightOf(), childLayer));

            // ---- К10 клавиатура ----
            Step("К10", "клавиши у выбранной строки: → ← Enter Пробел по обычаю дерева; прочие — штатно");
            {
                int n = Rows()[g].SubRows.Count;
                ClickText(g);
                Key(Keys.Right);
                Judge(Rows()[g].ExpandSubRows && Selected() == g, string.Format(CultureInfo.InvariantCulture,
                      "→ у свёрнутого родителя — раскрыт: {0}, выбрана строка {1} (ждём {2})", Rows()[g].ExpandSubRows, Selected(), g));
                // (третий заход, замер критика crit320) забранную клавишу таблица не обрабатывает
                // второй раз (`Table.OnKeyDown`, `e.Handled`): колонка фокуса остаётся 0
                Judge(Focus() == string.Format(CultureInfo.InvariantCulture, "({0},0)", g),
                      string.Format(CultureInfo.InvariantCulture, "→ забрана окном — фокус ячейки {0}, ждём ({1},0)", Focus(), g));
                Key(Keys.Right);
                Judge(Rows()[g].ExpandSubRows && Selected() == g + 1, string.Format(CultureInfo.InvariantCulture,
                      "→ у раскрытого — на первого потомка: выбрана {0} (ждём {1})", Selected(), g + 1));
                Judge(Focus() == string.Format(CultureInfo.InvariantCulture, "({0},0)", g + 1),
                      string.Format(CultureInfo.InvariantCulture, "второе → — фокус ячейки {0}, ждём ({1},0)", Focus(), g + 1));
                Key(Keys.Left);
                Judge(Rows()[g].ExpandSubRows && Selected() == g, string.Format(CultureInfo.InvariantCulture,
                      "← у потомка — на родителя: выбрана {0} (ждём {1}), группа раскрыта: {2}", Selected(), g, Rows()[g].ExpandSubRows));
                Key(Keys.Left);
                Judge(!Rows()[g].ExpandSubRows && Selected() == g, "← у раскрытого родителя — свёрнут, выбор на нём");
                Key(Keys.Enter);
                Judge(Rows()[g].ExpandSubRows, "Enter у свёрнутого родителя — раскрыт");
                Key(Keys.Space);
                Judge(!Rows()[g].ExpandSubRows, "Пробел у раскрытого родителя — свёрнут");
                Key(Keys.Down);
                Judge(Selected() == g + n + 1, string.Format(CultureInfo.InvariantCulture,
                      "↓ у свёрнутого родителя — штатно, мимо скрытых: выбрана {0} (ждём {1})", Selected(), g + n + 1));
                int expandedBefore = ExpandedCount();
                ClickText(0);
                Key(Keys.Right);
                Key(Keys.Enter);
                Judge(Selected() == 0 && ExpandedCount() == expandedBefore, string.Format(CultureInfo.InvariantCulture,
                      "→ и Enter у строки без группы — группы не тронуты (раскрыто {0}, было {1}), выбор на строке {2}",
                      ExpandedCount(), expandedBefore, Selected()));
            }

            // ---- К11 выравнивание родителей без стрелки ----
            Step("К11", "текст «— сумм-пик» без потомков — вровень с соседями со стрелкой");
            {
                var pads = new List<int>();
                int lonely = 0;
                foreach (Row r in Rows())
                {
                    FsaReportRow m = r.Tag as FsaReportRow;
                    if (m == null || r.Parent != null) continue;
                    if (m.Kind == FsaReportRowKind.SumPeaks || m.Kind == FsaReportRowKind.EscapePeaks || m.Kind == FsaReportRowKind.Backscatter)
                    {
                        pads.Add(r.Cells[0].Padding.Left);
                        if (r.SubRows.Count == 0) lonely++;
                    }
                }
                bool same = pads.Count > 0 && pads.TrueForAll(p => p == pads[0]);
                if (lonely == 0) Console.WriteLine("    (строк-родителей без потомков на спектре нет — выравнивание мерится на одних групповых)");
                Judge(same, string.Format(CultureInfo.InvariantCulture, "отступы текста строк-родителей: [{0}], без потомков {1}",
                                          string.Join(", ", pads.ConvertAll(p => p.ToString(CultureInfo.InvariantCulture))), lonely));
            }

            // ---- К9 собственная активность кристалла ----
            Step("К9", "заголовок собственной активности кристалла: сумма отсчётов и число строк (решение Amber 08.11.2026)");
            CheckCrystalOwn();

            // ---- К5 память раскрытия ----
            Step("К5", "память раскрытия: пересчёт, смена документа, запуск");
            ClickArrow(g);
            Row before = Rows()[g];
            Recalc(doc);
            Judge(!ReferenceEquals(before, Rows()[g]), "таблица перестроена пересчётом (строки — новые объекты)");
            CheckOnlyExpanded(gKey, "после пересчёта сеанса");
            CheckHiddenCounter("после пересчёта");
            Recalc(doc);
            CheckHiddenCounter("после второго пересчёта");

            DocEnergySpectrum doc2 = Open(path, nuclides);
            if (doc2 == null || !Compute(doc2))
            {
                Console.WriteLine("  ⛔ обвязка: второй документ не посчитан");
                rigFail++;
            }
            else
            {
                before = Rows()[g];
                report.SetDocument(doc2);
                Track(doc2.FsaSession);
                Settle(doc2.FsaSession, "окно на втором документе");
                Judge(!ReferenceEquals(before, Rows()[g]), "таблица перестроена сменой документа");
                CheckOnlyExpanded(gKey, "на другом документе");
                report.SetDocument(doc);
                Track(doc.FsaSession);
                Settle(doc.FsaSession, "окно снова на первом документе");
                CheckOnlyExpanded(gKey, "после возврата на документ");
                CheckHiddenCounter("после смены документа");
            }

            Judge(Forget(), "память забывается (вход ForgetExpandedGroups)");
            var fresh = new FSAReportView(null);
            Form host2 = Host(fresh, 460, 900, -3000);
            FSAReportView keep = report;
            report = fresh;
            fresh.SetDocument(doc);
            Track(doc.FsaSession);
            Settle(doc.FsaSession, "новое окно на документе");
            CheckOnlyExpanded(null, "«запуск»: новое окно без памяти");
            host2.Close();
            report = keep;

            return Finish(host);
        }

        // ==================================================================
        // Критерии
        // ==================================================================

        static void CheckCollapsedParent(int p)
        {
            Row pr = Rows()[p];
            FsaReportRow model = pr.Tag as FsaReportRow;
            int n = pr.SubRows.Count;
            string caption = model != null ? FSAReportView.RowCaption(model) : "?";
            string wantCaption = string.Format(CultureInfo.InvariantCulture, "{0} ({1})", caption, n);
            bool collapsed = !pr.ExpandSubRows;

            double sum = 0.0, printed = 0.0;
            bool summable = true, countsOnly = true;
            foreach (Row c in pr.SubRows)
            {
                FsaReportRow cm = c.Tag as FsaReportRow;
                double counts = CountsOf(cm);
                if (double.IsNaN(counts) || double.IsInfinity(counts)) { summable = false; continue; }
                sum += counts;
                // напечатанное число сверяется только там, где значение потомка — сами отсчёты
                if (cm.Kind == FsaReportRowKind.SumPeakLine || cm.Kind == FsaReportRowKind.EscapePeakLine
                    || cm.Kind == FsaReportRowKind.BackscatterLine)
                {
                    printed += LeadingNumber(c.Cells[1].Text ?? "");
                }
                else
                {
                    countsOnly = false;
                }
            }

            // Формат — строка ресурса приложения `FSASumPeakLineCounts` (тот же, что у потомков-пиков).
            string wantValue = summable
                ? string.Format(CultureInfo.InvariantCulture, AppText("FSASumPeakLineCounts"), sum.ToString("f0", CultureInfo.InvariantCulture))
                : (model != null ? model.Value ?? "" : "");
            string value = pr.Cells[1].Text ?? "";
            bool printedClose = !summable || !countsOnly || Math.Abs(LeadingNumber(value) - printed) <= 0.5 * n + 1e-9;
            bool ok = collapsed && pr.Cells[0].Text == wantCaption && value == wantValue && printedClose;
            Judge(ok, string.Format(CultureInfo.InvariantCulture,
                                    "«{0}» | «{1}» — ждём «{2}» | «{3}»; Σ Counts = {4}, Σ напечатанных потомков = {5}{6}",
                                    pr.Cells[0].Text, value, wantCaption, wantValue,
                                    summable ? sum.ToString("f1", CultureInfo.InvariantCulture) : "нет (не отсчёты)",
                                    summable && countsOnly ? printed.ToString("f0", CultureInfo.InvariantCulture) : "—",
                                    collapsed ? "" : "; ⛔ РАСКРЫТ"));
        }

        static void CheckExpandedParent(int p)
        {
            Row pr = Rows()[p];
            FsaReportRow model = pr.Tag as FsaReportRow;
            bool shown = true;
            foreach (Row c in pr.SubRows) if (c.Parent == null || !c.Parent.ExpandSubRows) shown = false;
            string wantCaption = model != null ? FSAReportView.RowCaption(model) : "?";
            string wantValue = model != null ? model.Value ?? "" : "";
            Judge(pr.ExpandSubRows && shown && pr.Cells[0].Text == wantCaption && (pr.Cells[1].Text ?? "") == wantValue,
                  string.Format("раскрыта: «{0}» | «{1}», ждём «{2}» | «{3}»; потомков видно: {4}",
                                pr.Cells[0].Text, pr.Cells[1].Text, wantCaption, wantValue, shown ? pr.SubRows.Count.ToString(CultureInfo.InvariantCulture) : "не все"));
        }

        static void CheckTooltips()
        {
            int childOk = 0, childBad = 0, parentOk = 0, parentBad = 0;
            foreach (int i in IndentedRows())
            {
                Row r = Rows()[i];
                FsaReportRow m = r.Tag as FsaReportRow;
                string want = m != null && !string.IsNullOrEmpty(m.Hint) ? m.Hint : r.Cells[0].Text;
                if (r.Cells[0].ToolTipText == want) childOk++;
                else { childBad++; if (childBad <= 3) Console.WriteLine("    подсказка потомка «{0}» не своя", r.Cells[0].Text); }
            }
            foreach (int p in Parents())
            {
                Row pr = Rows()[p];
                FsaReportRow m = pr.Tag as FsaReportRow;
                // (третий заход) своя подсказка модели — первой строкой; без неё подпись в подсказке
                // НЕ повторяется; en — «1 row is hidden» / «N rows are hidden»
                bool hasOwn = m != null && !string.IsNullOrEmpty(m.Hint);
                string own = hasOwn ? m.Hint : "";
                string caption = m != null ? FSAReportView.RowCaption(m) : "";
                string tip = pr.Cells[0].ToolTipText ?? "";
                string n = pr.SubRows.Count.ToString(CultureInfo.InvariantCulture);
                bool en = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
                bool number = !en || tip.Contains(pr.SubRows.Count == 1 ? " 1 row is hidden" : " " + n + " rows are hidden");
                if (!pr.ExpandSubRows && tip.StartsWith(own, StringComparison.Ordinal) && tip.Length > own.Length
                    && tip.Substring(own.Length).Contains(n) && number
                    && (hasOwn || !tip.StartsWith(caption, StringComparison.Ordinal)))
                {
                    parentOk++;
                }
                else
                {
                    parentBad++;
                    if (parentBad <= 3) Console.WriteLine("    подсказка родителя «{0}»: «{1}»", pr.Cells[0].Text, tip.Replace(Environment.NewLine, " ⏎ "));
                }
            }
            Judge(childBad == 0 && parentBad == 0,
                  string.Format(CultureInfo.InvariantCulture, "потомков со своей подсказкой {0} из {1}; родителей с подсказкой о свёртке {2} из {3}",
                                childOk, childOk + childBad, parentOk, parentOk + parentBad));
        }

        static void CheckOnlyExpanded(string key, string when)
        {
            int expanded = 0, collapsed = 0, wrong = 0;
            bool found = key == null;
            foreach (int p in Parents())
            {
                Row pr = Rows()[p];
                bool want = key != null && KeyOf(pr) == key;
                if (want) found = true;
                if (pr.ExpandSubRows) expanded++; else collapsed++;
                if (pr.ExpandSubRows != want) wrong++;
            }
            Judge(found && wrong == 0 && Parents().Count > 0,
                  string.Format(CultureInfo.InvariantCulture, "{0}: раскрыто {1}, свёрнуто {2}; ждём раскрытой {3}{4}",
                                when, expanded, collapsed, key ?? "ни одной", found ? "" : " — ⛔ группы нет"));
        }

        static void CheckHiddenCounter(string when)
        {
            int hidden = 0;
            foreach (Row r in Rows()) if (r.Parent != null && !r.Parent.ExpandSubRows) hidden++;
            int counter = report.ReportTable.TableModel.Rows.HiddenSubRows;
            Judge(counter == hidden, string.Format(CultureInfo.InvariantCulture,
                                                   "{0}: HiddenSubRows = {1}, скрыто на деле {2}", when, counter, hidden));
        }

        // ==================================================================
        // Чтение и щелчки
        // ==================================================================

        static RowCollection Rows()
        {
            return report.ReportTable.TableModel.Rows;
        }

        static List<int> IndentedRows()
        {
            var list = new List<int>();
            for (int i = 0; i < Rows().Count; i++)
            {
                FsaReportRow m = Rows()[i].Tag as FsaReportRow;
                if (m != null && m.Indent > 0) list.Add(i);
            }
            return list;
        }

        static List<int> Parents()
        {
            var list = new List<int>();
            for (int i = 0; i < Rows().Count; i++)
            {
                if (Rows()[i].Parent == null && Rows()[i].SubRows.Count > 0) list.Add(i);
            }
            return list;
        }

        static string KeyOf(Row r)
        {
            FsaReportRow m = r.Tag as FsaReportRow;
            return m == null ? "?" : m.Kind + "|" + (m.Layer != null ? m.Layer.Name : m.Name);
        }

        /// <summary>Все группы — входом окна (как стрелкой; строки вне видимой части щелчком не достать).</summary>
        static void ExpandAll()
        {
            SetAll(true);
        }

        static void CollapseAll()
        {
            SetAll(false);
        }

        static void SetAll(bool expanded)
        {
            MethodInfo m = typeof(FSAReportView).GetMethod("SetGroupExpanded", BindingFlags.Instance | BindingFlags.Public);
            if (m == null) return;
            foreach (int p in Parents()) m.Invoke(report, new object[] { p, expanded });
            Pump(50);
        }

        /// <summary>`FsaReportRow.Counts` отражением: на сборке «до» свойства нет — NaN.</summary>
        static double CountsOf(FsaReportRow row)
        {
            PropertyInfo p = row != null ? typeof(FsaReportRow).GetProperty("Counts") : null;
            return p != null ? (double)p.GetValue(row, null) : double.NaN;
        }

        static Rectangle CellRect(int row)
        {
            Table t = report.ReportTable;
            return (Rectangle)t.GetType().GetMethod("CellRect", new[] { typeof(int), typeof(int) }).Invoke(t, new object[] { row, 0 });
        }

        /// <summary>Щелчок по стрелке: левее текста ячейки имени, на 4 пикселя.</summary>
        static void ClickArrow(int row)
        {
            Rectangle rect = CellRect(row);
            int x = rect.X + Rows()[row].Cells[0].Padding.Left - 4;
            Click(row, x, rect);
        }

        /// <summary>Щелчок по тексту ячейки имени (как в П290: правее отступа).</summary>
        static void ClickText(int row)
        {
            Rectangle rect = CellRect(row);
            int x = rect.X + Rows()[row].Cells[0].Padding.Left + Math.Min(20, Math.Max(4, rect.Width / 4));
            Click(row, x, rect);
        }

        static void Click(int row, int x, Rectangle rect)
        {
            if (rect.IsEmpty)
            {
                Console.WriteLine("  ⛔ обвязка: у строки {0} нет прямоугольника", row);
                rigFail++;
                return;
            }
            Table t = report.ReportTable;
            var e = new MouseEventArgs(MouseButtons.Left, 1, x, rect.Y + rect.Height / 2, 0);
            MethodInfo down = t.GetType().GetMethod("OnMouseDown", Any, null, new[] { typeof(MouseEventArgs) }, null);
            MethodInfo up = t.GetType().GetMethod("OnMouseUp", Any, null, new[] { typeof(MouseEventArgs) }, null);
            down.Invoke(t, new object[] { e });
            up.Invoke(t, new object[] { e });
            Pump(50);
        }

        static string HighlightOf()
        {
            return (string)typeof(EnergySpectrumView).GetProperty("FsaHighlight", Any).GetValue(view, null);
        }

        static bool Forget()
        {
            MethodInfo m = typeof(FSAReportView).GetMethod("ForgetExpandedGroups", BindingFlags.Static | BindingFlags.Public);
            if (m == null) return false;
            m.Invoke(null, null);
            return true;
        }

        static double LeadingNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return double.NaN;
            int i = 0;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == '-')) i++;
            double v;
            return i > 0 && double.TryParse(text.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        static void Step(string id, string text)
        {
            Console.WriteLine();
            Console.WriteLine("{0}\t{1}", id, text);
        }

        /// <summary>
        /// Строка приговора в обычае сторожа `check_fsa_report_view.py`: «  ok   …» / «  ⛔  …»
        /// (два пробела перед знаком), итог последней строкой «ВСЕ СОШЛИСЬ» / «НЕ СОШЛОСЬ: N».
        /// </summary>
        static void Judge(bool ok, string text)
        {
            if (!ok) offRule++;
            Console.WriteLine("  {0} {1}", ok ? "ok  " : "⛔ ", text);
        }

        static int Finish(Form host)
        {
            host.Close();
            Console.WriteLine();
            int bad = offRule + rigFail;
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Клавиша — настоящий <c>Table.OnKeyDown</c> (как у мыши — <c>OnMouseDown</c>).</summary>
        static void Key(Keys key)
        {
            Table t = report.ReportTable;
            MethodInfo down = t.GetType().GetMethod("OnKeyDown", Any, null, new[] { typeof(KeyEventArgs) }, null);
            down.Invoke(t, new object[] { new KeyEventArgs(key) });
            Pump(50);
        }

        static string Focus()
        {
            CellPos f = report.ReportTable.FocusedCell;
            return string.Format(CultureInfo.InvariantCulture, "({0},{1})", f.Row, f.Column);
        }

        static int Selected()
        {
            int[] s = report.ReportTable.TableModel.Selections.SelectedIndicies;
            return s != null && s.Length > 0 ? s[0] : -1;
        }

        static int ExpandedCount()
        {
            int n = 0;
            foreach (int p in Parents()) if (Rows()[p].ExpandSubRows) n++;
            return n;
        }

        /// <summary>Строка ресурса ПРИЛОЖЕНИЯ (`Properties.Resources` — внутренний класс, отражением).</summary>
        static string AppText(string key)
        {
            Type t = typeof(FSAReportView).Assembly.GetType("BecquerelMonitor.Properties.Resources");
            var rm = (System.Resources.ResourceManager)t.GetProperty("ResourceManager", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null);
            var culture = (CultureInfo)t.GetProperty("Culture", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null, null);
            return rm.GetString(key, culture ?? CultureInfo.CurrentUICulture) ?? key;
        }

        /// <summary>
        /// К9: в результат сеанса подкладываются две строки собственной активности кристалла (слой
        /// выключен умолчанием — на спектрах его нет), окно перестраивается своим `RefreshReport`;
        /// заголовок обязан быть свёрнутым родителем двух строк с суммой их отсчётов. Затем всё
        /// возвращается как было.
        /// </summary>
        static void CheckCrystalOwn()
        {
            FsaResult result = doc.FsaSession.Result;
            List<FsaCrystalOwnResult> kept = result.CrystalOwn;
            result.CrystalOwn = new List<FsaCrystalOwnResult>
            {
                new FsaCrystalOwnResult { Nuclide = "La-138", Layer = "LaBr3", Free = false, ActivityBq = 1.25, Counts = 1234.4 },
                new FsaCrystalOwnResult { Nuclide = "Ac-227", Layer = "LaBr3", Free = true, ActivityBq = 0.30, ActivitySigmaBq = 0.10, Counts = 567.2 }
            };
            try
            {
                report.RefreshReport();
                Pump(100);
                int header = -1;
                for (int i = 0; i < Rows().Count; i++)
                {
                    Row r = Rows()[i];
                    if (r.Parent == null && r.SubRows.Count > 0 && (r.SubRows[0].Cells[0].Text ?? "").Contains("La-138")) header = i;
                }
                if (header < 0)
                {
                    int loose = 0;
                    foreach (Row r in Rows()) if ((r.Cells[0].Text ?? "").Contains("La-138") && r.Parent == null) loose++;
                    Judge(false, string.Format(CultureInfo.InvariantCulture,
                                               "заголовка-группы над строками кристалла нет (строк кристалла вне группы: {0})", loose));
                    return;
                }
                Row h = Rows()[header];
                string want = string.Format(CultureInfo.InvariantCulture, AppText("FSASumPeakLineCounts"),
                                            (1234.4 + 567.2).ToString("f0", CultureInfo.InvariantCulture));
                Judge(h.SubRows.Count == 2 && !h.ExpandSubRows && h.Cells[1].Text == want
                      && (h.Cells[0].Text ?? "").EndsWith("(2)", StringComparison.Ordinal),
                      string.Format(CultureInfo.InvariantCulture, "«{0}» | «{1}», потомков {2}, свёрнут {3}; ждём «… (2)» | «{4}»",
                                    h.Cells[0].Text, h.Cells[1].Text, h.SubRows.Count, !h.ExpandSubRows, want));
                CheckCollapsedParent(header);
            }
            finally
            {
                result.CrystalOwn = kept;
                report.RefreshReport();
                Pump(100);
            }
        }

        /// <summary>Каталог склада матриц по guid кривой (`--store=`), или null — склад рядом с exe.</summary>
        static string storeDir;

        /// <summary>
        /// Матрица кривой спектра — как у P195 (`T269`): склад приложения, если там годная; иначе
        /// `&lt;--store&gt;\&lt;guid&gt;.rmx` в память блоком «матрица из файла спектра» (`AMBER202`).
        /// null — годной нет (сумм-пиков, вылета и рассеяния без матрицы нет — мерить нечем).
        /// </summary>
        static string AttachMatrix(EfficiencyConfigData efficiency)
        {
            MatrixRefusal refusal;
            int format;
            ResponseMatrixSource source;
            string spectrumRefusal;
            ResponseMatrix own = ResponseMatrixStore.Resolve(efficiency, out refusal, out format, out source, out spectrumRefusal);
            if (own != null && own.IsValidFor(efficiency.Geometry))
            {
                return "склад приложения (" + source + ")";
            }
            if (string.IsNullOrEmpty(storeDir))
            {
                Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: нужна матрица кривой {0} ({1}); дайте --store=<склад по guid>", efficiency.Guid, refusal);
                return null;
            }
            string file = Path.Combine(storeDir, efficiency.Guid + ".rmx");
            ResponseMatrix matrix = File.Exists(file) ? ResponseMatrix.Load(file) : null;
            if (matrix == null || !matrix.IsValidFor(efficiency.Geometry))
            {
                Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: матрицы {0} нет или она негодна для геометрии кривой", file);
                return null;
            }
            efficiency.EmbeddedMatrix = EmbeddedResponseMatrix.FromFile(efficiency.Guid, file, matrix);
            return "--store " + file + " → в память (AMBER202)";
        }

        static void Shot(Form host, string file)
        {
            Pump(200);
            using (var bmp = new Bitmap(host.ClientSize.Width, host.ClientSize.Height))
            {
                report.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                string dir = Path.GetDirectoryName(Path.GetFullPath(file));
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                bmp.Save(file, ImageFormat.Png);
            }
            Console.WriteLine("SHOT\t{0}", file);
        }

        // ==================================================================
        // Обвязка (образцы П290 / F16)
        // ==================================================================

        static bool Compute(DocEnergySpectrum d)
        {
            ResultData rd = d.ActiveResultData;
            var cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (cfg != null) cfg.DbLookupsForFsa = true;
            make = () =>
            {
                FsaCalculationOptions o = FsaCalculationOptions.FromConfig(cfg);
                o.DbLookups = true;
                o.ChainEquilibrium = true;
                return o;
            };
            FsaCalculationOptions.ApplicationSource = make;
            FsaAnalysisSession session = d.FsaSession;
            if (delayCompletedMs > 0)
            {
                // Замедлитель (`--delay-completed=`): ПЕРВЫЙ обработчик конца счёта спит на фоновом
                // потоке — так, как под нагрузкой фоновый поток поднимает событие поздно. Окно и
                // проба подписаны после него и получают событие позже ровно на эту паузу.
                session.Completed += delegate { Thread.Sleep(delayCompletedMs); };
            }
            Track(session);
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, make());
            Settle(session, "первый счёт документа");
            if (session.Result == null)
            {
                Console.WriteLine("разбора нет: {0}", session.Status ?? "?");
                return false;
            }
            Console.WriteLine("SETUP\tχ²/ndf {0}",
                              session.Result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture));
            return true;
        }

        static void Recalc(DocEnergySpectrum d)
        {
            FsaAnalysisSession session = d.FsaSession;
            ResultData rd = d.ActiveResultData;
            int before = session.RunCount;
            session.Invalidate();
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, make());
            Settle(session, "пересчёт");
            if (session.RunCount == before)
            {
                Console.WriteLine("  ⛔ обвязка: пересчёт не запустился");
                rigFail++;
            }
        }

        static bool SelectSet(NuclideDefinitionManager nuclides, string name)
        {
            foreach (NuclideSet set in nuclides.NuclideSets)
            {
                if (set != null && string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    nuclides.ActiveSet = set;
                    Console.WriteLine("SETUP\tнабор: {0}", set.Name);
                    return true;
                }
            }
            Console.Error.WriteLine("набора «{0}» нет", name);
            return false;
        }

        static void Setup(EnergySpectrumView v, ResultData rd, FsaResult result)
        {
            EnergySpectrum spectrum = rd.EnergySpectrum;
            EnergyCalibration calibration = spectrum.EnergyCalibration;
            double toKev = calibration.ChannelToEnergy(spectrum.NumberOfChannels - 1);
            double ceiling = 0.0;
            for (int i = 0; i < result.Model.Length; i++) if (result.Model[i] > ceiling) ceiling = result.Model[i];
            const int left = 1;
            const double pownum = 4.0;
            Set(v, "energySpectrum", spectrum);
            Set(v, "activeResultData", rd);
            if (rd.BackgroundEnergySpectrum != null)
            {
                Set(v, "backgroundEnergySpectrum", rd.BackgroundEnergySpectrum);
                Set(v, "backgroundEnergyCalibration", rd.BackgroundEnergySpectrum.EnergyCalibration);
                Set(v, "backgroundNumberOfChannels", rd.BackgroundEnergySpectrum.NumberOfChannels);
            }
            Set(v, "energyCalibration", calibration);
            Set(v, "numberOfChannels", spectrum.NumberOfChannels);
            Set(v, "backgroundMode", BackgroundMode.ShowFSA);
            Set(v, "horizontalUnit", HorizontalUnit.Energy);
            Set(v, "verticalUnit", VerticalUnit.Counts);
            Set(v, "verticalScaleType", VerticalScaleType.PowerScale);
            Set(v, "pownum", pownum);
            Set(v, "totalMinValuePow", 0.0);
            Set(v, "valueRangePow", Math.Pow(ceiling, 1.0 / pownum));
            Set(v, "totalMinValueLog", 0.0);
            Set(v, "valueRangeLog", Math.Log10(ceiling));
            Set(v, "height", Height);
            Set(v, "width", Width - left);
            Set(v, "left", left);
            Set(v, "scrollX", 0);
            Set(v, "scrollY", 0);
            Set(v, "scrollBaseY", 0.0);
            Set(v, "verticalScale", 1.0);
            Set(v, "horizontalScale", 1.0);
            Set(v, "totalMinValue", 0.0);
            Set(v, "valueRange", ceiling);
            Set(v, "energyViewOffset", 0.0);
            Set(v, "pixelPerEnergy", (Width - left) / toKev);
            Set(v, "dirty", false);
        }

        static DocEnergySpectrum Open(string path, NuclideDefinitionManager nuclides)
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
            Console.WriteLine("SETUP\t{0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));
            if (rd.Efficiency == null || !rd.Efficiency.HasGeometry)
            {
                Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: у спектра нет кривой с геометрией — матрицы нет, строк-потомков не будет");
                return null;
            }
            string matrixFrom = AttachMatrix(rd.Efficiency);
            if (matrixFrom == null) return null;
            Console.WriteLine("SETUP\tматрица: {0}", matrixFrom);
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
            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            var d = new DocEnergySpectrum(path);
            d.ResultDataFile = file;
            d.ActiveResultDataIndex = 0;
            d.UpdateEnergySpectrum();
            return d;
        }

        // ------------------------------------------------------------------
        // (`AMBER320`, четвёртый заход 09.11.2026) ГОТОВНОСТЬ — ПО ПРИЗНАКУ, А НЕ ПО ПАУЗЕ.
        //
        // Сеанс снимает «занят» (`running = false`, под замком) РАНЬШЕ, чем поднимает
        // `Completed` (вне замка, `FsaAnalysisSession.Finish`), а окно отчёта перестраивает
        // таблицу по `Completed` ОТЛОЖЕННО (`BeginInvoke` → `Consume` + `RefreshReport`). Прежняя
        // обвязка ждала `!IsRunning` и паузу 300–500 мс; под нагрузкой фоновый поток поднимал
        // событие позже паузы, и перестройка таблицы приходила посреди щелчков (К7, К10, К5):
        // выбор уезжал на строку слоя, щелчки по стрелке — мимо. Теперь проба подписана на
        // `Completed` ПОСЛЕДНЕЙ (после окна) и в своём обработчике ставит маркер в очередь потока
        // окон; готово, когда счёт не идёт, последний начатый запуск закончен событием и маркер
        // этого события отработал, — значит, отложенная перестройка окна, поставленная раньше
        // маркера, тоже отработала.
        // ------------------------------------------------------------------

        static readonly object doneGate = new object();
        static readonly Dictionary<FsaAnalysisSession, int> completedRun = new Dictionary<FsaAnalysisSession, int>();
        static readonly Dictionary<FsaAnalysisSession, int> drainedRun = new Dictionary<FsaAnalysisSession, int>();
        static SynchronizationContext uiSync;

        /// <summary>
        /// (четвёртый заход) `--delay-completed=&lt;мс&gt;` — воспроизводимая «нагрузка»: событие конца
        /// счёта доходит до окна и пробы с этой задержкой. Прежняя обвязка (пауза 300–500 мс)
        /// на 1500 мс отказывала; ожидание по признаку (`Settle`) — нет.
        /// </summary>
        static int delayCompletedMs;

        /// <summary>Подписать пробу на конец счёта сеанса ПОСЛЕДНЕЙ — звать после каждого `SetDocument`.</summary>
        static void Track(FsaAnalysisSession session)
        {
            session.Completed -= OnCompleted;
            session.Completed += OnCompleted;
        }

        static void OnCompleted(object sender, EventArgs e)
        {
            var session = (FsaAnalysisSession)sender;
            int run = session.RunCount;
            lock (doneGate)
            {
                completedRun[session] = run;
            }

            SendOrPostCallback mark = delegate
            {
                lock (doneGate)
                {
                    drainedRun[session] = run;
                }
            };
            FSAReportView target = report;
            if (target != null && target.IsHandleCreated && !target.IsDisposed)
            {
                try
                {
                    target.BeginInvoke(mark, new object[] { null });
                    return;
                }
                catch (InvalidOperationException)
                {
                    // окно закрылось — маркер пойдёт контекстом потока окон
                }
            }

            uiSync.Post(mark, null);
        }

        /// <summary>
        /// Ждать, пока сеанс и окно успокоятся: счёт не идёт, последний начатый запуск
        /// (`RunCount`) закончен событием `Completed`, и маркер этого события отработал на потоке
        /// окон. Срок — <see cref="timeoutS"/>; вышел — отказ обвязки, а не «готово».
        /// </summary>
        static void Settle(FsaAnalysisSession session, string what)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(timeoutS);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                int run = session.RunCount;
                bool done;
                lock (doneGate)
                {
                    int completed, drained;
                    done = !session.IsRunning && run > 0
                           && completedRun.TryGetValue(session, out completed) && completed == run
                           && drainedRun.TryGetValue(session, out drained) && drained == run;
                }

                if (done && session.RunCount == run)
                {
                    Application.DoEvents();
                    return;
                }

                Thread.Sleep(10);
            }

            Console.WriteLine("  ⛔ обвязка: {0} — сеанс и окно не успокоились за {1} с", what, timeoutS);
            rigFail++;
        }

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
            rigFail++;
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

        static Form Host(Form content, int width, int height, int x)
        {
            var host = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(x, -4000),
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

        static void Set(object target, string name, object value)
        {
            Field(target.GetType(), name).SetValue(target, value);
        }
    }
}
