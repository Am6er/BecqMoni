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
    /// (`AMBER327`, полоса a327, 11.11.2026) СТРОКА-ПОТОМОК ОТЧЁТА FSA ВЫДЕЛЯЕТ СВОЙ СЛОЙ НА СПЕКТРЕ.
    /// Задача Amber 11.11.2026, дословно: «Если выбираются пики суммирования, обратного рассеяние и
    /// прочие пики, которые раскрываются треугольником, нужно их выделать на спектре при нажатии на
    /// них мышкой. При этом слой основного родителя должнен становится мало заметным, по аналогии как
    /// сейчас отрисовываются не выбранные слои. НО! Не так блёкло. Думаю, в половину того визуала.»;
    /// вопросником того же дня: «Прочие как невыбранные, сейчас (Рекомендую)».
    ///
    /// Окно отчёта (<c>FSAReportView</c>) и разбор — настоящие (обвязка `FsaRowGroupsProbeA320`), щелчок
    /// — настоящий <c>Table.OnMouseDown</c>/<c>OnMouseUp</c>, кадр — настоящий
    /// <c>EnergySpectrumView.ShowFsaOverlay</c> (отражением); своей отрисовки у пробы нет. Области кадра —
    /// маски той же <c>DrawFsaBand</c> по кадровым массивам вида (приём F16). Все новые члены
    /// приложения зовутся ОТРАЖЕНИЕМ: проба собирается и против сборки «до» — положительный контроль.
    ///
    /// На каждый род строк-групп сцены («— сумм-пик», «— пики вылета», «— обратное рассеяние») —
    /// строка группы и её потомок:
    ///   В1 проводка: щелчок → график выделяет слой родителя, часть (подслой рода) и окно потомка
    ///      (группа — часть целиком, −1), ряд не тянется; окно строки = окно группы по
    ///      `FsaPresentationBuilder.*Groups` (свой пересчёт пробы);
    ///   В2 прочие слои — ровно как невыбранные: пиксели прочих слоёв = кадру «выбран слой родителя»
    ///      (там прочие и есть невыбранные), и они бледнее кадра без выбора;
    ///   В3 родитель — приглушён ВДВОЕ слабее невыбранного: r = (c − c_невыбр) / (c_выбр − c_невыбр)
    ///      по контрасту с полем в [0.30, 0.70] (ждём ≈ 0.5); у потомка — и прочие пики той же части
    ///      вне его окна;
    ///   В4 выбранное — ярко: r ≥ 0.85 по пикселям части (у потомка — в его окне);
    /// и для всей сцены:
    ///   В5 снятие выбора — кадр побитово как без выбора; строка слоя — побитово как до `AMBER327`
    ///      (кадр «выбран слой»);
    ///   В6 свёртка группы при выбранном потомке — выбор на родителе, график — часть целиком;
    ///   В7 пересчёт при выбранном потомке — выбор на той же строке, график — та же часть;
    ///   В8 щелчок по уже выбранному потомку возвращает его подсветку (П290);
    ///   В9 контроль судьи: подставленный вместо кадра выбора кадр «выбран слой родителя» (родитель
    ///      ярок) и кадр «выбран другой слой» (родитель приглушён как прочие) судья В3 обязан отвергнуть.
    /// Поле — чёрное и белое (приглушение прозрачностью уходит к фону, `A246`).
    ///
    ///   FsaChildHighlightProbeA327 --spectrum=&lt;xml&gt; [--store=&lt;склад по guid&gt;] [--set=&lt;набор&gt;]
    ///                              [--ui=en|ru] [--out=&lt;каталог снимков&gt;] [--timeout=300]
    ///
    /// Вывод — «  ok   …» / «  ⛔  …», последняя строка «ВСЕ СОШЛИСЬ» / «НЕ СОШЛОСЬ: N». Коды: 0 — все
    /// критерии; 1 — есть отказы (сборка «до» и испорченная — положительный контроль); 2 — мерить нечем.
    /// ⚠ Окна пробы — на скрытом рабочем столе (`HiddenRun.exe`), на экран не выводятся.
    /// </summary>
    static class FsaChildHighlightProbeA327
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int Width = 1200, Height = 620;
        const double HalfLo = 0.30, HalfHi = 0.70, BrightMin = 0.85;
        const int MinPixels = 30;
        static int timeoutS = 300;
        static int offRule, rigFail;
        static string outDir;

        static DocEnergySpectrum doc;
        static EnergySpectrumView view;
        static FSAReportView report;
        static Func<FsaCalculationOptions> make;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string path = null, setName = null, ui = "en";
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--ui=", StringComparison.Ordinal)) ui = a.Substring(5);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
                else if (a.StartsWith("--store=", StringComparison.Ordinal)) storeDir = a.Substring(8);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (path == null || (ui != "en" && ui != "ru"))
            {
                Console.Error.WriteLine("нужен --spectrum=; --ui= — en или ru");
                return 2;
            }

            CultureInfo uiCulture = CultureInfo.GetCultureInfo(ui == "ru" ? "ru-RU" : "en-US");
            Thread.CurrentThread.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = uiCulture;

            Application.EnableVisualStyles();
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }
            uiSync = SynchronizationContext.Current;
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            if (setName != null && !SelectSet(nuclides, setName)) return 2;
            Forget();

            doc = Open(path, nuclides);
            if (doc == null) return 2;
            if (!Compute(doc)) return 2;
            view = doc.EnergySpectrumView;
            Setup(view, doc.ActiveResultData, doc.FsaSession.Result);
            // Измеряются ленты: линия спектра и лента невязки от выбора не зависят и только мешают мерке.
            SetProp(view, "FsaShowResidual", false);
            SetProp(view, "FsaShowSpectralData", false);

            report = new FSAReportView(null);
            Form host = Host(report, 460, 900, -4000);
            report.SetDocument(doc);
            Track(doc.FsaSession);
            Settle(doc.FsaSession, "окно на документе");
            Frame(Color.Black);     // кадровые массивы вида (стопка, подслои) строит первый кадр

            // строки-группы частей ленты: по одной на род
            var groups = new List<int>();
            var kinds = new HashSet<FsaReportRowKind>();
            foreach (int p in Parents())
            {
                FsaReportRow m = Model(p);
                if (m != null && m.Layer != null && IsPartGroup(m.Kind) && !kinds.Contains(m.Kind))
                {
                    kinds.Add(m.Kind);
                    groups.Add(p);
                }
            }
            Console.WriteLine("SETUP\tстрок {0}, групп частей ленты {1}: {2}", Rows().Count, groups.Count,
                              string.Join(", ", groups.ConvertAll(g => "«" + Model(g).Name + "» (" + Rows()[g].SubRows.Count.ToString(CultureInfo.InvariantCulture) + ")")));
            if (groups.Count == 0)
            {
                Console.WriteLine("МЕРИТЬ НЕЧЕМ: на спектре нет строк-групп сумм-пиков, вылета или рассеяния");
                host.Close();
                return 2;
            }

            bool judgeChecked = false;
            foreach (int g0 in groups)
            {
                int g = FindRow(Model(g0));
                CollapseAll();
                Step("ГРУППА", "«" + Model(g).Name + "» — строка группы: выделены все её потомки");
                RunCase(g, false, ref judgeChecked);

                CollapseAll();
                g = FindRow(Model(g0));
                SetExpanded(g, true);
                int leaf = PickLeaf(g);
                if (leaf < 0)
                {
                    Judge(false, "у группы нет потомка, видного на графике (≥ " + MinPixels + " px части в окне)");
                    continue;
                }
                Step("ПОТОМОК", "«" + Model(leaf).Name + "» — строка-лист: выделен только он");
                RunCase(leaf, true, ref judgeChecked);
            }

            GlobalChecks(groups[0]);
            return Finish(host);
        }

        // ==================================================================
        // Случай: строка группы или потомок
        // ==================================================================

        static void RunCase(int row, bool leaf, ref bool judgeChecked)
        {
            FsaReportRow model = Model(row);
            string layer = model.Layer.Name;
            string part = PartName(model.Kind);
            int first = -1, last = -1;
            if (leaf && !WindowOf(model, out first, out last))
            {
                Judge(false, "окно потомка своим пересчётом групп не найдено (Counts не совпал ни с одной группой)");
                return;
            }

            // ---- В1 проводка ----
            ClickText(row);
            Judge(Rows()[row].AnyCellsSelected, "строка выбрана щелчком");
            string hl = HighlightOf();
            string hp = PartOf();
            int hf = IntProp(view, "FsaHighlightFirstChannel"), hlast = IntProp(view, "FsaHighlightLastChannel");
            bool chain = (bool)GetProp(view, "FsaHighlightWholeChain");
            Judge(hl == layer && hp == part && hf == first && hlast == last && !chain,
                  string.Format(CultureInfo.InvariantCulture,
                                "В1 график: слой «{0}», часть {1}, окно {2}…{3}, ряд {4}; ждём «{5}», {6}, {7}…{8}, нет",
                                hl, hp ?? "НЕТ ВХОДА", hf, hlast, chain ? "да" : "нет", layer, part, first, last));
            if (leaf)
            {
                int rf = IntProp(model, "FirstChannel"), rl = IntProp(model, "LastChannel");
                Judge(rf == first && rl == last, string.Format(CultureInfo.InvariantCulture,
                      "В1 окно строки модели {0}…{1} = окну группы своим пересчётом {2}…{3}", rf, rl, first, last));
            }

            int k = StackIndex(layer);
            int other = OtherLayer(k);
            if (k < 0 || other < 0)
            {
                Judge(false, "слоя родителя или другого слоя в стопке нет");
                return;
            }

            // ---- кадры ----
            Bitmap[] fc = Frames();                                  // как поставил щелчок
            SetHighlight(null);
            Bitmap[] f0 = Frames();
            SetHighlight(layer);
            Bitmap[] fp = Frames();                                  // выбран слой родителя: прочие — невыбранные
            SetHighlight(Stack()[other].Name);
            Bitmap[] fo = Frames();                                  // выбран другой: родитель — невыбранный
            SetHighlight(null);

            Bitmap layerMask = LayerMask();
            Bitmap partAll = PartMask(k, model.Kind, -1, -1);
            Bitmap partSel = leaf ? PartMask(k, model.Kind, first, last) : partAll;
            Region R = Regions(layerMask, partAll, partSel, k);
            Console.WriteLine("    области (px): родитель {0}, выбранное {1}, та же часть вне окна {2}, прочие слои {3}",
                              R.ParentRest.Count, R.Selected.Count, R.PartOutside.Count, R.Others.Count);
            if (R.Selected.Count < MinPixels || R.ParentRest.Count < MinPixels || R.Others.Count < MinPixels)
            {
                Judge(false, "областей мало для мерки — мерить нечем на этой строке");
                Save(fc, f0, row, leaf);
                return;
            }

            for (int gi = 0; gi < 2; gi++)
            {
                string ground = gi == 0 ? "чёрное" : "белое";
                Color gc = gi == 0 ? Color.Black : Color.White;

                // ---- В2 прочие как невыбранные ----
                int same = 0;
                foreach (Point p in R.Others) if (At(fc[gi], p) == At(fp[gi], p)) same++;
                double dimO = Sum(fc[gi], R.Others, gc) / Math.Max(1e-9, Sum(f0[gi], R.Others, gc));
                Judge(same == R.Others.Count && dimO < 0.8, string.Format(CultureInfo.InvariantCulture,
                      "В2 [{0}] прочие слои: как невыбранные {1} из {2} px; контраст к кадру без выбора {3:F3}",
                      ground, same, R.Others.Count, dimO));

                // ---- В3 родитель вдвое слабее ----
                double rp = Ratio(fc[gi], fo[gi], fp[gi], R.ParentRest, gc);
                Judge(rp >= HalfLo && rp <= HalfHi, string.Format(CultureInfo.InvariantCulture,
                      "В3 [{0}] родитель: r = {1:F3} (0 — как невыбранный, 1 — как выбранный; ждём {2:F2}…{3:F2})",
                      ground, rp, HalfLo, HalfHi));
                if (leaf && R.PartOutside.Count >= MinPixels)
                {
                    double ro = Ratio(fc[gi], fo[gi], fp[gi], R.PartOutside, gc);
                    Judge(ro >= HalfLo && ro <= HalfHi, string.Format(CultureInfo.InvariantCulture,
                          "В3 [{0}] та же часть вне окна потомка: r = {1:F3} (ждём {2:F2}…{3:F2})", ground, ro, HalfLo, HalfHi));
                }

                // ---- В4 выбранное ярко ----
                double rs = Ratio(fc[gi], fo[gi], fp[gi], R.Selected, gc);
                Judge(rs >= BrightMin, string.Format(CultureInfo.InvariantCulture,
                      "В4 [{0}] выбранное: r = {1:F3} (ждём ≥ {2:F2})", ground, rs, BrightMin));

                // ---- В9 контроль судьи ----
                if (!judgeChecked)
                {
                    double asBright = Ratio(fp[gi], fo[gi], fp[gi], R.ParentRest, gc);
                    double asMuted = Ratio(fo[gi], fo[gi], fp[gi], R.ParentRest, gc);
                    bool caught = !(asBright >= HalfLo && asBright <= HalfHi) && !(asMuted >= HalfLo && asMuted <= HalfHi);
                    Judge(caught, string.Format(CultureInfo.InvariantCulture,
                          "В9 [{0}] контроль судьи В3: родитель ярок r = {1:F3}, приглушён как прочие r = {2:F3} — оба отвергнуты",
                          ground, asBright, asMuted));
                }
            }
            judgeChecked = true;
            Save(fc, f0, row, leaf);
            SelectNone();
        }

        // ==================================================================
        // Сцена целиком
        // ==================================================================

        static void GlobalChecks(int g0)
        {
            Step("В5", "снятие выбора и строка слоя — как было");
            CollapseAll();
            int g = FindRow(Model(g0));
            string layer = Model(g).Layer.Name;
            SetHighlight(null);
            Bitmap[] f0 = Frames();
            SetHighlight(layer);
            Bitmap[] fp = Frames();
            SetHighlight(null);

            SetExpanded(g, true);
            int leaf = PickLeaf(g);
            if (leaf < 0) { Judge(false, "потомка нет"); return; }
            ClickText(leaf);
            Judge(PartOf() != null && PartOf() != "None", "потомок выбран — часть на графике: " + (PartOf() ?? "НЕТ ВХОДА"));
            SelectNone();
            Bitmap[] f = Frames();
            Judge(HighlightOf() == null && Same(f, f0), string.Format("выбор снят: подсветка «{0}», часть {1}, кадр побитово как без выбора: {2}",
                                                                       HighlightOf(), PartOf(), Same(f, f0)));
            int layerRow = RowOfLayer(layer);
            ClickText(layerRow);
            f = Frames();
            Judge(HighlightOf() == layer && PartOf() == "None" && Same(f, fp),
                  string.Format("строка слоя «{0}»: подсветка «{1}», часть {2}, кадр побитово как «выбран слой»: {3}",
                                Model(layerRow).Name, HighlightOf(), PartOf(), Same(f, fp)));

            Step("В6", "свёртка группы при выбранном потомке");
            g = FindRow(Model(g0));
            SetExpanded(g, true);
            leaf = PickLeaf(g);
            ClickText(leaf);
            ClickArrow(g);
            Judge(!Rows()[g].ExpandSubRows && Rows()[g].AnyCellsSelected && HighlightOf() == layer
                  && PartOf() == PartName(Model(g).Kind) && IntProp(view, "FsaHighlightFirstChannel") == -1,
                  string.Format(CultureInfo.InvariantCulture, "свёрнута: {0}, выбран родитель: {1}, график: «{2}» {3} окно {4}",
                                !Rows()[g].ExpandSubRows, Rows()[g].AnyCellsSelected, HighlightOf(), PartOf(),
                                IntProp(view, "FsaHighlightFirstChannel")));

            Step("В7", "пересчёт при выбранном потомке");
            SetExpanded(g, true);
            leaf = PickLeaf(g);
            FsaReportRow before = Model(leaf);
            ClickText(leaf);
            Row oldRow = Rows()[leaf];
            Recalc(doc);
            int now = Selected();
            FsaReportRow after = now >= 0 ? Model(now) : null;
            int nf = after != null ? IntProp(after, "FirstChannel") : -2;
            Judge(!ReferenceEquals(oldRow, now >= 0 ? Rows()[now] : null) && after != null && after.Kind == before.Kind
                  && after.Name == before.Name && HighlightOf() == layer && PartOf() == PartName(before.Kind)
                  && IntProp(view, "FsaHighlightFirstChannel") == nf,
                  string.Format(CultureInfo.InvariantCulture, "после пересчёта выбрана «{0}» (была «{1}»), график «{2}» {3} окно {4} (строки {5})",
                                after != null ? after.Name : "—", before.Name, HighlightOf(), PartOf(),
                                IntProp(view, "FsaHighlightFirstChannel"), nf));

            Step("В8", "щелчок по уже выбранному потомку возвращает подсветку (П290)");
            now = Selected();
            SetHighlight(null);                     // подсветку сменило «другое окно»
            if (now >= 0) ClickText(now);
            Judge(HighlightOf() == layer && PartOf() == PartName(before.Kind),
                  string.Format("подсветка «{0}», часть {1}", HighlightOf(), PartOf()));
            SelectNone();
        }

        // ==================================================================
        // Мерка
        // ==================================================================

        sealed class Region
        {
            public List<Point> ParentRest = new List<Point>(), Selected = new List<Point>(),
                               PartOutside = new List<Point>(), Others = new List<Point>();
        }

        /// <summary>
        /// Области кадра по маскам: родитель без своей части; выбранное (часть в окне); та же часть вне
        /// окна; прочие слои — без пикселей, соседних по вертикали с родителем (там краска смешана).
        /// </summary>
        static Region Regions(Bitmap layers, Bitmap partAll, Bitmap partSel, int k)
        {
            var r = new Region();
            int w = layers.Width, h = layers.Height;
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int id = Id(layers, x, y);
                    if (id < 0) continue;
                    bool inAll = Id(partAll, x, y) >= 0, inSel = Id(partSel, x, y) >= 0;
                    if (id == k)
                    {
                        if (inSel) r.Selected.Add(new Point(x, y));
                        else if (inAll) r.PartOutside.Add(new Point(x, y));
                        else r.ParentRest.Add(new Point(x, y));
                    }
                    else
                    {
                        bool near = (y > 0 && Id(layers, x, y - 1) == k) || (y + 1 < h && Id(layers, x, y + 1) == k)
                                    || (x > 0 && Id(layers, x - 1, y) == k) || (x + 1 < w && Id(layers, x + 1, y) == k);
                        if (!near) r.Others.Add(new Point(x, y));
                    }
                }
            }
            return r;
        }

        static readonly Dictionary<Bitmap, int[]> pixels = new Dictionary<Bitmap, int[]>();

        /// <summary>Пиксели кадра ARGB построчно (LockBits; GetPixel на 0.7 Мпикс в цикле — секунды).</summary>
        static int[] Px(Bitmap b)
        {
            int[] a;
            if (pixels.TryGetValue(b, out a)) return a;
            var rect = new Rectangle(0, 0, b.Width, b.Height);
            BitmapData d = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                a = new int[b.Width * b.Height];
                for (int y = 0; y < b.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), a, y * b.Width, b.Width);
                }
            }
            finally
            {
                b.UnlockBits(d);
            }
            pixels[b] = a;
            return a;
        }

        static int At(Bitmap b, Point p)
        {
            return Px(b)[p.Y * b.Width + p.X];
        }

        /// <summary>Номер в маске: красный канал − 1; −1 — пусто (поле маски чёрное).</summary>
        static int Id(Bitmap mask, int x, int y)
        {
            return ((Px(mask)[y * mask.Width + x] >> 16) & 0xFF) - 1;
        }
        static double Contrast(Color c, Color g)
        {
            double dr = c.R - g.R, dg = c.G - g.G, db = c.B - g.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        static double Sum(Bitmap f, List<Point> pts, Color g)
        {
            double s = 0.0;
            foreach (Point p in pts) s += Contrast(Color.FromArgb(At(f, p)), g);
            return s;
        }

        /// <summary>
        /// Доля яркости по области: (Σc − Σc_невыбр) / (Σc_выбр − Σc_невыбр) по пикселям, где кадры
        /// «невыбран» и «выбран» различаются заметно (≥ 6 уровней контраста; белая линия модели и метки
        /// одинаковы во всех кадрах и в мерку не идут).
        /// </summary>
        static double Ratio(Bitmap f, Bitmap muted, Bitmap bright, List<Point> pts, Color g)
        {
            double a = 0.0, m = 0.0, b = 0.0;
            foreach (Point p in pts)
            {
                double cm = Contrast(Color.FromArgb(At(muted, p)), g), cb = Contrast(Color.FromArgb(At(bright, p)), g);
                if (Math.Abs(cb - cm) < 6.0) continue;
                a += Contrast(Color.FromArgb(At(f, p)), g);
                m += cm;
                b += cb;
            }
            return Math.Abs(b - m) < 1e-9 ? double.NaN : (a - m) / (b - m);
        }

        static bool Same(Bitmap[] a, Bitmap[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                int[] pa = Px(a[i]), pb = Px(b[i]);
                for (int j = 0; j < pa.Length; j++) if (pa[j] != pb[j]) return false;
            }
            return true;
        }

        /// <summary>Кадр стопки настоящим методом вида на чёрном и белом поле.</summary>
        static Bitmap[] Frames()
        {
            return new[] { Frame(Color.Black), Frame(Color.White) };
        }

        static Bitmap Frame(Color ground)
        {
            var image = new Bitmap(Width, Height);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(ground);
                object drawn = typeof(EnergySpectrumView).GetMethod("ShowFsaOverlay", Any).Invoke(view, new object[] { g });
                if (!(drawn is bool) || !(bool)drawn)
                {
                    Console.WriteLine("  ⛔ обвязка: ShowFsaOverlay не нарисовал стопку");
                    rigFail++;
                }
            }
            return image;
        }

        /// <summary>Маска слоёв стопки кадра (R = номер + 1) той же <c>DrawFsaBand</c>, приём F16.</summary>
        static Bitmap LayerMask()
        {
            var image = new Bitmap(Width, Height);
            var cumulative = (double[][])Field(typeof(EnergySpectrumView), "fsaCumulative").GetValue(view);
            var zero = (double[])Field(typeof(EnergySpectrumView), "fsaZeroLevel").GetValue(view);
            MethodInfo band = typeof(EnergySpectrumView).GetMethod("DrawFsaBand", Any);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(Color.Black);
                for (int k = 0; k < cumulative.Length; k++)
                {
                    double[] lower = k > 0 ? cumulative[k - 1] : zero;
                    using (Brush brush = new SolidBrush(Color.FromArgb(255, k + 1, 0, 0)))
                    {
                        band.Invoke(view, new object[] { g, brush, lower, cumulative[k] });
                    }
                }
            }
            return image;
        }

        /// <summary>
        /// Маска подслоя рода строки у k-го слоя — по кадровым массивам вида (поля есть и в сборке
        /// «до», `AMBER311`), в окне каналов (−1 — целиком); низ — тот же, от которого подслой нарисован.
        /// </summary>
        static Bitmap PartMask(int k, FsaReportRowKind kind, int first, int last)
        {
            var image = new Bitmap(Width, Height);
            var cumulative = (double[][])Field(typeof(EnergySpectrumView), "fsaCumulative").GetValue(view);
            var zero = (double[])Field(typeof(EnergySpectrumView), "fsaZeroLevel").GetValue(view);
            string levelName, baseName;
            if (kind == FsaReportRowKind.SumPeaks || kind == FsaReportRowKind.SumPeakLine) { levelName = "fsaSumPeakLevel"; baseName = null; }
            else if (kind == FsaReportRowKind.EscapePeaks || kind == FsaReportRowKind.EscapePeakLine) { levelName = "fsaEscapeLevel"; baseName = "fsaEscapeBase"; }
            else { levelName = "fsaBackscatterLevel"; baseName = "fsaBackscatterBase"; }
            double[] level = ((double[][])Field(typeof(EnergySpectrumView), levelName).GetValue(view))[k];
            double[] from = baseName == null ? (k > 0 ? cumulative[k - 1] : zero)
                                             : ((double[][])Field(typeof(EnergySpectrumView), baseName).GetValue(view))[k];
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(Color.Black);
                if (level == null || from == null) return image;
                double[] top = (double[])level.Clone();
                if (first >= 0)
                {
                    for (int i = 0; i < top.Length; i++) if (i < first || i > last) top[i] = from[i];
                }
                MethodInfo band = typeof(EnergySpectrumView).GetMethod("DrawFsaBand", Any);
                using (Brush brush = new SolidBrush(Color.White))
                {
                    band.Invoke(view, new object[] { g, brush, from, top });
                }
            }
            return image;
        }

        /// <summary>
        /// Окно потомка СВОИМ пересчётом: группы рода строки у её слоя теми же публичными
        /// `FsaPresentationBuilder.*Groups`, что строят таблицу (есть и в сборке «до»), группа — по
        /// отсчётам строки.
        /// </summary>
        static bool WindowOf(FsaReportRow row, out int first, out int last)
        {
            first = last = -1;
            FsaResult result = doc.FsaSession.Result;
            List<FsaSumPeakGroup> groups;
            if (row.Kind == FsaReportRowKind.SumPeakLine) groups = FsaPresentationBuilder.SumPeakGroups(result, row.Layer.SumPeakLines);
            else if (row.Kind == FsaReportRowKind.EscapePeakLine) groups = FsaPresentationBuilder.EscapeGroups(result, row.Layer.EscapeLines);
            else groups = FsaPresentationBuilder.BackscatterGroups(result, row.Layer.BackscatterLines);
            foreach (FsaSumPeakGroup grp in groups)
            {
                if (grp.Shown && Math.Abs(grp.Counts - row.Counts) <= 1e-9 * Math.Max(1.0, Math.Abs(row.Counts)))
                {
                    first = grp.FirstChannel;
                    last = grp.LastChannel;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Потомок группы, чья часть в окне видна на графике (не меньше <see cref="MinPixels"/> px).</summary>
        static int PickLeaf(int g)
        {
            Row pr = Rows()[g];
            int k = StackIndex(Model(g).Layer.Name);
            if (k < 0) return -1;
            foreach (Row c in pr.SubRows)
            {
                FsaReportRow m = c.Tag as FsaReportRow;
                int first, last;
                if (m == null || m.Layer == null || !WindowOf(m, out first, out last)) continue;
                Bitmap mask = PartMask(k, m.Kind, first, last);
                Bitmap layers = LayerMask();
                int n = 0;
                for (int x = 0; x < Width; x++)
                    for (int y = 0; y < Height; y++)
                        if (Id(mask, x, y) >= 0 && Id(layers, x, y) == k) n++;
                if (n >= MinPixels) return c.Index;
            }
            return -1;
        }

        static void Save(Bitmap[] fc, Bitmap[] f0, int row, bool leaf)
        {
            if (outDir == null) return;
            Directory.CreateDirectory(outDir);
            string name = string.Format(CultureInfo.InvariantCulture, "{0}_{1}", leaf ? "leaf" : "group", PartName(Model(row).Kind));
            fc[1].Save(Path.Combine(outDir, name + "_white.png"), ImageFormat.Png);
            fc[0].Save(Path.Combine(outDir, name + "_black.png"), ImageFormat.Png);
            string none = Path.Combine(outDir, "none_white.png");
            if (!File.Exists(none)) f0[1].Save(none, ImageFormat.Png);
            Console.WriteLine("SHOT\t{0}_white.png / _black.png", Path.Combine(outDir, name));
        }

        // ==================================================================
        // Чтение и щелчки
        // ==================================================================

        static bool IsPartGroup(FsaReportRowKind kind)
        {
            return kind == FsaReportRowKind.SumPeaks || kind == FsaReportRowKind.EscapePeaks || kind == FsaReportRowKind.Backscatter;
        }

        /// <summary>Имя члена `FsaLayerPart` для рода строки — так его печатает график.</summary>
        static string PartName(FsaReportRowKind kind)
        {
            switch (kind)
            {
                case FsaReportRowKind.SumPeaks:
                case FsaReportRowKind.SumPeakLine:
                    return "SumPeaks";
                case FsaReportRowKind.EscapePeaks:
                case FsaReportRowKind.EscapePeakLine:
                    return "EscapePeaks";
                default:
                    return "Backscatter";
            }
        }

        static RowCollection Rows()
        {
            return report.ReportTable.TableModel.Rows;
        }

        static FsaReportRow Model(int i)
        {
            return i >= 0 && i < Rows().Count ? Rows()[i].Tag as FsaReportRow : null;
        }

        /// <summary>Строка таблицы с тем же родом, слоем и подписью (после перестройки — новые объекты).</summary>
        static int FindRow(FsaReportRow like)
        {
            for (int i = 0; i < Rows().Count; i++)
            {
                FsaReportRow m = Model(i);
                if (m != null && m.Kind == like.Kind && m.Name == like.Name
                    && (m.Layer != null ? m.Layer.Name : null) == (like.Layer != null ? like.Layer.Name : null)) return i;
            }
            return -1;
        }

        static int RowOfLayer(string layer)
        {
            for (int i = 0; i < Rows().Count; i++)
            {
                FsaReportRow m = Model(i);
                if (m != null && m.Kind == FsaReportRowKind.Layer && m.Layer != null && m.Layer.Name == layer) return i;
            }
            return -1;
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

        static List<FsaStackLayer> Stack()
        {
            return (List<FsaStackLayer>)Field(typeof(EnergySpectrumView), "fsaStack").GetValue(view);
        }

        static int StackIndex(string name)
        {
            List<FsaStackLayer> stack = Stack();
            for (int k = 0; k < stack.Count; k++) if (stack[k].Name == name) return k;
            return -1;
        }

        /// <summary>Другой слой стопки — самый толстый по площади ленты, не родитель.</summary>
        static int OtherLayer(int k)
        {
            List<FsaStackLayer> stack = Stack();
            int best = -1;
            double area = -1.0;
            for (int i = 0; i < stack.Count; i++)
            {
                if (i == k || stack[i].Curve == null) continue;
                double s = 0.0;
                foreach (double v in stack[i].Curve) s += v;
                if (s > area) { area = s; best = i; }
            }
            return best;
        }

        static void CollapseAll()
        {
            foreach (int p in Parents()) SetExpanded(p, false);
        }

        static void SetExpanded(int p, bool expanded)
        {
            typeof(FSAReportView).GetMethod("SetGroupExpanded", BindingFlags.Instance | BindingFlags.Public).Invoke(report, new object[] { p, expanded });
            Pump(30);
        }

        static void SelectNone()
        {
            report.SelectComponent(null);
            Pump(30);
        }

        static Rectangle CellRect(int row)
        {
            Table t = report.ReportTable;
            return (Rectangle)t.GetType().GetMethod("CellRect", new[] { typeof(int), typeof(int) }).Invoke(t, new object[] { row, 0 });
        }

        static void ClickArrow(int row)
        {
            Rectangle rect = CellRect(row);
            Click(row, rect.X + Rows()[row].Cells[0].Padding.Left - 4, rect);
        }

        static void ClickText(int row)
        {
            if (row < 0) { Console.WriteLine("  ⛔ обвязка: строки нет"); rigFail++; return; }
            report.ReportTable.EnsureVisible(row, 0);
            Pump(30);
            Rectangle rect = CellRect(row);
            Click(row, rect.X + Rows()[row].Cells[0].Padding.Left + Math.Min(20, Math.Max(4, rect.Width / 4)), rect);
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
            t.GetType().GetMethod("OnMouseDown", Any, null, new[] { typeof(MouseEventArgs) }, null).Invoke(t, new object[] { e });
            t.GetType().GetMethod("OnMouseUp", Any, null, new[] { typeof(MouseEventArgs) }, null).Invoke(t, new object[] { e });
            Pump(50);
        }

        static int Selected()
        {
            int[] s = report.ReportTable.TableModel.Selections.SelectedIndicies;
            return s != null && s.Length > 0 ? s[0] : -1;
        }

        static void SetHighlight(string layer)
        {
            typeof(EnergySpectrumView).GetMethod("SetFsaHighlight", Any).Invoke(view, new object[] { layer, false });
        }

        static string HighlightOf()
        {
            return (string)GetProp(view, "FsaHighlight");
        }

        /// <summary>Часть на графике именем члена; null — входа нет (сборка «до»).</summary>
        static string PartOf()
        {
            object v = GetProp(view, "FsaHighlightPart");
            return v != null ? v.ToString() : null;
        }

        static object GetProp(object target, string name)
        {
            PropertyInfo p = target.GetType().GetProperty(name, Any);
            return p != null ? p.GetValue(target, null) : null;
        }

        static int IntProp(object target, string name)
        {
            object v = GetProp(target, name);
            return v is int ? (int)v : -99;
        }

        static void SetProp(object target, string name, object value)
        {
            PropertyInfo p = target.GetType().GetProperty(name, Any);
            if (p == null) { Console.WriteLine("  ⛔ обвязка: нет свойства " + name); rigFail++; return; }
            p.SetValue(target, value, null);
        }

        static void Forget()
        {
            MethodInfo m = typeof(FSAReportView).GetMethod("ForgetExpandedGroups", BindingFlags.Static | BindingFlags.Public);
            if (m != null) m.Invoke(null, null);
        }

        static void Step(string id, string text)
        {
            Console.WriteLine();
            Console.WriteLine("{0}\t{1}", id, text);
        }

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

        // ==================================================================
        // Обвязка (образец FsaRowGroupsProbeA320)
        // ==================================================================

        static string storeDir;

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
            Track(session);
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, make());
            Settle(session, "первый счёт документа");
            if (session.Result == null)
            {
                Console.WriteLine("разбора нет: {0}", session.Status ?? "?");
                return false;
            }
            Console.WriteLine("SETUP\tχ²/ndf {0}", session.Result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture));
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

        // Готовность — по признаку, а не по паузе (`AMBER320`, четвёртый заход).
        static readonly object doneGate = new object();
        static readonly Dictionary<FsaAnalysisSession, int> completedRun = new Dictionary<FsaAnalysisSession, int>();
        static readonly Dictionary<FsaAnalysisSession, int> drainedRun = new Dictionary<FsaAnalysisSession, int>();
        static SynchronizationContext uiSync;

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
                }
            }

            uiSync.Post(mark, null);
        }

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
