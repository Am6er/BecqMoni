using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FsaQualityRowProbe
{
    /// <summary>
    /// СТОРОЖ СТРОКИ КАЧЕСТВА в легенде разбора (`A96`).
    ///
    /// ЗАЧЕМ. Хвост пометок этой строки — единственное место, где приложение
    /// говорит, чем именно посчитано разложение: с матрицей отклика, без неё
    /// или со СТАРОЙ (`A50`, пометка «· старая матрица»). Пометка заведена
    /// 03.09.2026, и до этой пробы её не читал никто: проверено было только
    /// то, что верно поднимается признак
    /// <see cref="FsaOverlay.ResponseMatrixOldFormat"/>, а доедет ли он до
    /// ТЕКСТА — нет. ⚠ Хвост вдобавок урезается с конца (`S104`), то есть
    /// пометка может просто не поместиться, и молчаливой эта потеря была бы
    /// вдвойне.
    ///
    /// ЧТО МЕРЯЕТСЯ, тремя разделами:
    ///
    ///   1. ТЕКСТ. <see cref="EnergySpectrumView.FsaQualityText"/> на всех
    ///      четырёх сочетаниях «матрица использована × формат старый»: ровно
    ///      ОДНА из трёх матричных пометок, и та, какая положена. Оба языка.
    ///   2. ЦЕПЬ. Признак ставится настоящему <see cref="FsaOverlay"/> полем, а
    ///      читается его ОТКРЫТЫМ свойством; строка рисуется настоящим
    ///      `DrawFsaRows` настоящего вида — тем самым, что зовёт `OnPaint`, — и
    ///      полученные пиксели сверяются ПОБИТОВО с независимой отрисовкой
    ///      ожидаемого текста.
    ///   3. УРЕЗАНИЕ. Сколько знаков хвоста помещается в отведённую ширину и
    ///      попадает ли в них пометка целиком. Мерено на рабочей ширине панели
    ///      (230 пкс) и развёрткой по ширине, на обоих языках.
    ///
    /// ⛔ И ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, четвёртым разделом. Сторож, который всегда
    /// доволен, не меряет ничего: те же проверки прогоняются по заведомо
    /// испорченному входу (пометку вырезали из текста; строку сжали так, что
    /// хвост не влезает) и ОБЯЗАНЫ отказать. Не отказали — проба падает.
    ///
    ///     fsaqualityrowprobe [--dump=&lt;png&gt;]
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        /// <summary>
        /// Ширина панели легенды — БЕРЁТСЯ У ПРИЛОЖЕНИЯ, своей копии здесь
        /// больше нет (`A128`).
        /// </summary>
        /// <remarks>
        /// ⛔ Прежде тут стоял литерал 230, а в приложении — такой же литерал
        /// местной переменной `table_width_origin`. Разойтись эти два числа
        /// могли МОЛЧА, и тогда порог `A127` («пометка «· старая матрица» стоит
        /// впритык, запас ноль») мерился бы не по той ширине. Теперь число
        /// живёт в одном месте — <c>EnergySpectrumView.CursorPanelWidth</c> — и
        /// читается ОТТУДА в работе, а не вкомпилируется сюда. Что это именно
        /// поле, а не константа (константа вкомпилировалась бы и копии
        /// разошлись бы снова), спрашивается отражением в разделе 0.
        ///
        /// ⚠ Развёртка по ширине в разделе 3 при этом ОСТАЁТСЯ: порог должен
        /// читаться независимо от того, 230 в приложении или уже нет.
        /// </remarks>
        static readonly int PanelWidth = EnergySpectrumView.CursorPanelWidth;

        /// <summary>Отступы панели: `DrawFsaOwnTable` строит строку как `width - 12`.</summary>
        const int PanelPadding = 12;

        const int RowHeight = 16;
        const int Pad = 8;

        /// <summary>χ²/ndf сцены — то же число, на котором мерен `S104`.</summary>
        const double Chi2 = 2.94;

        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки
            // (`T60`). `GlobalConfigManager` тянет `ROIConfigManager`, тот
            // подставляет из обеих карт и на пустых падает МОДАЛЬНЫМ окном, а
            // безоконный прогон на нём виснет навсегда. Вид, который проба
            // создаёт ниже, менеджеры трогает.
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            string dump = null;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--dump=", StringComparison.Ordinal))
                {
                    dump = arg.Substring(7);
                }
            }

            using (EnergySpectrumView view = new EnergySpectrumView())
            {
                Console.WriteLine("шрифт вида: {0} {1} пт; панель {2} пкс, строка {3} пкс",
                                  view.Font.Name,
                                  view.Font.SizeInPoints.ToString("0.##", CultureInfo.InvariantCulture),
                                  PanelWidth, PanelWidth - PanelPadding);

                Moved(view);
                Text();
                Chain(view);
                Trimming(view, dump);
                Control(view);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 0. ВЫНОС ПРИЁМОВ НИЧЕГО НЕ ИЗМЕНИЛ
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ `A96` вынес из отрисовки два расчёта — ширину под хвост и формат
        /// урезания. Оба переехали дословно, и «дословно» — ровно та посылка,
        /// которую в этом дереве трижды снимал замер. Поэтому здесь стоит
        /// НЕЗАВИСИМАЯ копия прежнего кода, и вынесенные приёмы сверяются с
        /// ней: ширина — числом на пяти ширинах строки, формат — ПОБИТОВО на
        /// строке, которая помещается, и на той, которую режет.
        /// </summary>
        static void Moved(EnergySpectrumView view)
        {
            Console.WriteLine();
            Console.WriteLine("=== 0. вынос приёмов из отрисовки ничего не изменил ===");
            Language("ru-RU");

            // `A128`: ширина берётся У ПРИЛОЖЕНИЯ и обязана быть ПОЛЕМ, а не
            // константой. Константа вкомпилировалась бы сюда при сборке, и
            // копии снова могли бы разойтись молча — на этот раз между свежим
            // приложением и вчерашней пробой.
            FieldInfo width = typeof(EnergySpectrumView).GetField("CursorPanelWidth",
                BindingFlags.Public | BindingFlags.Static);
            Same("ширина панели — открытое поле приложения, не константа",
                 true, width != null && !width.IsLiteral && width.IsInitOnly);

            using (Bitmap image = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(image))
            {
                foreach (int row in new[] { 60, 120, 218, 300, 900 })
                {
                    string value = Chi2.ToString("n2");

                    // Прежний код, слово в слово: `marks.Width = Math.Max(0,
                    // marks.Width - (int)Math.Ceiling(MeasureString(value).Width))`.
                    int before = Math.Max(0, row
                                             - (int)Math.Ceiling(g.MeasureString(value, view.Font).Width));
                    Same("ширина под хвост при строке " + row + " пкс — как раньше",
                         before, EnergySpectrumView.FsaQualityMarksWidth(g, view.Font, row, value));
                }
            }

            // Формат: прежний собирался на месте тремя строками.
            foreach (string text in new[] { "χ²/ndf · старая матрица",
                                            "χ²/ndf · подавлен · старая матрица (без кривой) !" })
            {
                Same("формат хвоста на «" + text + "» рисует как раньше",
                     true, SameFormat(view, text, 192));
            }
        }

        static bool SameFormat(EnergySpectrumView view, string text, int marks)
        {
            int canvas = Math.Max(marks, TextWidth(view, text)) + 2 * Pad;
            Rectangle band = new Rectangle(Pad, Pad, canvas - 2 * Pad, RowHeight);
            using (Bitmap now = new Bitmap(canvas, RowHeight + 2 * Pad, PixelFormat.Format32bppArgb))
            using (Bitmap was = new Bitmap(canvas, RowHeight + 2 * Pad, PixelFormat.Format32bppArgb))
            {
                Paint(view, now, text, marks);

                using (Graphics g = Graphics.FromImage(was))
                using (StringFormat old = new StringFormat())
                {
                    g.Clear(Color.White);
                    old.FormatFlags = StringFormatFlags.NoWrap;
                    old.Trimming = StringTrimming.EllipsisCharacter;
                    g.DrawString(text, view.Font, Brushes.Black,
                                 new Rectangle(Pad, Pad, marks, RowHeight), old);
                }

                return SamePixels(now, was, band);
            }
        }

        // ------------------------------------------------------------------
        // 1. ТЕКСТ
        // ------------------------------------------------------------------

        static void Text()
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. текст строки качества: какая матричная пометка ===");

            foreach (string lang in new[] { "ru-RU", "en-US" })
            {
                Language(lang);
                string old = Mark("FSAOldMatrixMark");
                string none = Mark("FSANoMatrixMark");
                string with = Mark("FSAMatrixMark");

                // Три пометки обязаны быть РАЗНЫМИ строками: слейся две — и
                // проверки ниже сошлись бы, ничего не проверив.
                Same(lang + ": три матричные пометки различны", 3,
                     DistinctCount(old, none, with));

                foreach (bool used in new[] { false, true })
                {
                    foreach (bool oldFormat in new[] { false, true })
                    {
                        FsaResult result = Scene(used, false, true, false, false);
                        string text = EnergySpectrumView.FsaQualityText(result, oldFormat);
                        string want = used ? with : (oldFormat ? old : none);
                        string scene = string.Format("{0}: матрица={1} старая={2}",
                                                     lang, used ? "да" : "нет",
                                                     oldFormat ? "да" : "нет");

                        // Одна и только одна: две пометки в строке значили бы,
                        // что человеку сказали два разных ответа сразу.
                        int found = (Has(text, old) ? 1 : 0) + (Has(text, none) ? 1 : 0)
                                    + (Has(text, with) ? 1 : 0);
                        Same(scene + " — пометок ровно одна", 1, found);
                        Same(scene + " — она «" + want.Trim() + "»", true, Has(text, want));
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // 2. ЦЕПЬ: признак наложения -> текст -> пиксели настоящей отрисовки
        // ------------------------------------------------------------------

        static void Chain(EnergySpectrumView view)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. цепь: FsaOverlay.ResponseMatrixOldFormat -> пиксели ===");
            Language("ru-RU");
            string old = Mark("FSAOldMatrixMark");
            string none = Mark("FSANoMatrixMark");

            // Сцена самая скромная: без подавления, кривая есть, дрейф не на
            // краю. Хвост тут короткий и помещается заведомо — раздел 2 про
            // ДОЕЗД признака, а не про урезание, и сверять пиксели можно с
            // текстом напрямую.
            FsaResult result = Scene(false, false, true, false, false);
            int row = PanelWidth - PanelPadding;

            SetOldFlag(view, true);
            Same("признак поднят — открытое свойство наложения это подтверждает",
                 true, OldFlag(view));
            Same("таблица рисует строку качества тем же приёмом, что и вид",
                 true, TableDrawsSameRow(view, result, row));

            string on = EnergySpectrumView.FsaQualityText(result, true);
            Same("хвост при поднятом признаке помещается целиком (иначе сверка ниже слепа)",
                 on.Length, Shown(view, result, row, on));
            Same("нарисованное ПОБИТОВО совпало с «" + on + "»",
                 true, PixelsShow(view, result, row, on));
            Same("в нём есть «старая матрица»", true, Has(on, old));
            Same("и нет «без матрицы»", false, Has(on, none));

            SetOldFlag(view, false);
            Same("признак снят — открытое свойство наложения это подтверждает",
                 false, OldFlag(view));
            string off = EnergySpectrumView.FsaQualityText(result, false);
            Same("нарисованное ПОБИТОВО совпало с «" + off + "»",
                 true, PixelsShow(view, result, row, off));
            Same("«старой матрицы» в нём НЕТ", false, Has(off, old));
            Same("а «без матрицы» есть", true, Has(off, none));

            // ⛔ И ОБРАТНАЯ ПРОВЕРКА: пиксели со снятым признаком НЕ обязаны
            // совпасть с текстом поднятого. Без неё сверка «побитово совпало»
            // прошла бы и на отрисовке, которая рисует что попало одинаково.
            Denies("со снятым признаком пиксели НЕ совпадают с текстом поднятого",
                   PixelsShow(view, result, row, on));
        }

        // ------------------------------------------------------------------
        // 3. УРЕЗАНИЕ
        // ------------------------------------------------------------------

        static void Trimming(EnergySpectrumView view, string dump)
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. урезание хвоста (S104): помещается ли пометка ===");
            Console.WriteLine("    «знаков» = наибольшее начало строки, которое GDI+ рисует БЕЗ");
            Console.WriteLine("    урезания в отведённой хвосту ширине; «запас» = сколько знаков");
            Console.WriteLine("    остаётся после конца пометки. Запас 0 — пометка стоит впритык.");

            SetOldFlag(view, true);
            int row = PanelWidth - PanelPadding;

            foreach (string lang in new[] { "ru-RU", "en-US" })
            {
                Language(lang);
                string old = Mark("FSAOldMatrixMark");
                Console.WriteLine();
                Console.WriteLine("  {0}, рабочая строка {1} пкс:", lang, row);

                foreach (Scenery s in Sceneries())
                {
                    FsaResult result = Scene(false, s.Suppressed, s.Efficiency, s.Summing, s.DriftEdge);
                    string full = EnergySpectrumView.FsaQualityText(result, true);
                    int shown = Shown(view, result, row, full);
                    int end = full.IndexOf(old, StringComparison.Ordinal) + old.Length;
                    bool intact = end <= shown;
                    int need = MinRowWidth(view, result, full, end);

                    Console.WriteLine("    {0,-28} {1,3} пкс хвоста из {2,3} · знаков {3,2}/{4,2} · пометка {5} (запас {6,3}) · цела от {7} пкс строки ({8} пкс панели){9}",
                                      s.Name, TextWidth(view, full), MarksWidth(view, row, result),
                                      shown, full.Length,
                                      intact ? "ЦЕЛА    " : "ПОТЕРЯНА", shown - end,
                                      need, need + PanelPadding,
                                      need > row ? "  ⛔ ШИРЕ РАБОЧЕЙ" : "");
                    if (shown < full.Length)
                    {
                        Console.WriteLine("      собрано:   «{0}»", full);
                        Console.WriteLine("      на экране: «{0}»", Visible(full, shown));
                    }
                }
            }

            if (dump != null)
            {
                Language("ru-RU");
                Shot(view, Scene(false, true, false, false, true), row, dump);
                Console.WriteLine();
                Console.WriteLine("  снимок худшей сцены: {0}", dump);
            }
        }

        struct Scenery
        {
            public string Name;
            public bool Suppressed;
            public bool Efficiency;
            public bool Summing;
            public bool DriftEdge;
        }

        /// <summary>
        /// Сцены хвоста, от голой к самой тяжёлой. ⚠ Каскадное суммирование
        /// сюда НЕ входит: без матрицы его нет вовсе (`FsaOverlay`), а «старая
        /// матрица» — это как раз работа без матрицы. Ставить его значило бы
        /// мерить сочетание, которого на экране не бывает.
        /// </summary>
        static IEnumerable<Scenery> Sceneries()
        {
            yield return new Scenery { Name = "голая", Efficiency = true, Summing = false };
            yield return new Scenery { Name = "+ подавлен", Suppressed = true, Efficiency = true };
            yield return new Scenery { Name = "+ подавлен, без кривой", Suppressed = true };
            yield return new Scenery
            {
                Name = "+ подавлен, без кривой, край",
                Suppressed = true,
                DriftEdge = true
            };
        }

        // ------------------------------------------------------------------
        // 4. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ Сторож обязан УМЕТЬ ОТКАЗАТЬ. Здесь ему подсовывают то, что он
        /// призван ловить, и каждая проверка обязана сказать «плохо». Молчание
        /// на порченом входе — отказ пробы, а не успех.
        /// </summary>
        static void Control(EnergySpectrumView view)
        {
            Console.WriteLine();
            Console.WriteLine("=== 4. положительный контроль: сторож на заведомо плохом входе ===");
            Language("ru-RU");
            SetOldFlag(view, true);
            string old = Mark("FSAOldMatrixMark");
            FsaResult result = Scene(false, true, false, false, true);
            string full = EnergySpectrumView.FsaQualityText(result, true);
            int end = full.IndexOf(old, StringComparison.Ordinal) + old.Length;

            // (а) пометку вырезали из текста — как если бы её забыли поставить.
            Denies("пометку вырезали из текста — сторож видит пропажу",
                   Has(full.Replace(old, string.Empty), old));

            // (б) строку сжали так, что хвост не влезает вовсе. Сперва — что
            //     урезание вообще случилось: контроль на строке, которая
            //     помещается целиком, не мерил бы ничего.
            int narrow = 60;
            int shownNarrow = Shown(view, result, narrow, full);
            Same("на " + narrow + " пкс строки урезание ДЕЙСТВИТЕЛЬНО случилось",
                 true, shownNarrow < full.Length);
            Denies("и пометка в оставшееся не попала — сторож видит пропажу",
                   end <= shownNarrow);
            Console.WriteLine("     на {0} пкс видно: «{1}»", narrow, Visible(full, shownNarrow));

            // (в) ОТРИЦАТЕЛЬНЫЙ конец того же опыта: на широкой строке сторож
            //     обязан быть ДОВОЛЕН, иначе он просто ругается всегда.
            int wide = 900;
            int shownWide = Shown(view, result, wide, full);
            Same("на широкой строке не урезано ничего", full.Length, shownWide);
            Same("и пометка цела", true, end <= shownWide);

            // (г) ПОДМЕНА ТЕКСТА: сверка пикселей обязана заметить лишний знак.
            //     Без этого «побитово совпало» из раздела 2 могло бы совпадать
            //     с чем угодно.
            Denies("пиксели НЕ совпадают с подменённым текстом",
                   PixelsShow(view, result, wide, full + "x"));
        }

        // ------------------------------------------------------------------
        // Сцена и отражение
        // ------------------------------------------------------------------

        /// <summary>
        /// Разложение, у которого заполнено ровно то, что читает строка
        /// качества. Состав пуст нарочно: строк легенды выше от этого нет, и
        /// строка качества оказывается ВТОРОЙ, сразу за невязкой, — на месте,
        /// которое проба умеет отсчитать.
        /// </summary>
        static FsaResult Scene(bool matrixUsed, bool suppressed, bool efficiency,
                               bool summing, bool driftEdge)
        {
            FsaResult result = new FsaResult
            {
                Chi2Ndf = Chi2,
                BackgroundUsed = true,
                ResponseMatrixUsed = matrixUsed,
                EfficiencyUsed = efficiency,
                CascadeSummingUsed = summing,
                GainOnGridEdge = driftEdge
            };

            if (suppressed)
            {
                // `SuppressorName` ставит только сам разбор; вердикт читается
                // из него, и подделать сцену иначе нечем.
                FieldInfo f = typeof(FsaResult).GetField(
                    "<SuppressorName>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (f == null)
                {
                    Console.WriteLine("  ⛔ у FsaResult нет поля вердикта — сцену «подавлен» не собрать");
                    bad++;
                }
                else
                {
                    f.SetValue(result, "Backscatter");
                }

                if (!result.CompositionSuppressed)
                {
                    Console.WriteLine("  ⛔ сцена «подавлен» не собралась — проверки по ней пусты");
                    bad++;
                }
            }

            return result;
        }

        static void SetOldFlag(EnergySpectrumView view, bool value)
        {
            object overlay = Field(typeof(EnergySpectrumView), "fsaOverlay").GetValue(view);
            Field(typeof(FsaOverlay), "matrixOldFormat").SetValue(overlay, value);
        }

        /// <summary>Признак ЧЕРЕЗ ОТКРЫТОЕ СВОЙСТВО — то же, что читает вид.</summary>
        static bool OldFlag(EnergySpectrumView view)
        {
            FsaOverlay overlay =
                (FsaOverlay)Field(typeof(EnergySpectrumView), "fsaOverlay").GetValue(view);
            return overlay.ResponseMatrixOldFormat;
        }

        static FieldInfo Field(Type type, string name)
        {
            FieldInfo f = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null)
            {
                throw new InvalidOperationException(
                    string.Format("нет поля {0}.{1} — проба смотрит не туда", type.Name, name));
            }

            return f;
        }

        static MethodInfo Method(string name)
        {
            MethodInfo m = typeof(EnergySpectrumView).GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (m == null)
            {
                throw new InvalidOperationException("нет EnergySpectrumView." + name);
            }

            return m;
        }

        // ------------------------------------------------------------------
        // Отрисовка и её разбор
        // ------------------------------------------------------------------

        /// <summary>
        /// ЦЕПЬ ЦЕЛИКОМ: полоса строки качества, нарисованная настоящим
        /// `DrawFsaRows` (тем самым, что зовёт `OnPaint`), обязана совпасть
        /// ПОБИТОВО с той же полосой, нарисованной `DrawFsaQualityRow`. Так
        /// проверяется, что таблица зовёт именно этот приём: вырежи вызов — и
        /// полоса опустеет, а сторож это увидит.
        /// </summary>
        static bool TableDrawsSameRow(EnergySpectrumView view, FsaResult result, int rowWidth)
        {
            Rectangle row = new Rectangle(Pad, Pad, rowWidth, RowHeight);
            int canvas = rowWidth + 2 * Pad;
            using (Bitmap table = new Bitmap(canvas, RowHeight + 2 * Pad, PixelFormat.Format32bppArgb))
            using (Bitmap alone = new Bitmap(canvas, RowHeight + 2 * Pad, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(table))
                {
                    g.Clear(Color.White);

                    // Список слоёв подставляется пустым: иначе `GetFsaLayers`
                    // полез бы строить кадр по спектру, которого у пробы нет.
                    // Строк состава от этого нет ни одной, и строка качества
                    // оказывается второй — на RowHeight ниже начала таблицы.
                    Field(typeof(EnergySpectrumView), "fsaLayers")
                        .SetValue(view, new List<FsaStackLayer>());
                    Field(typeof(EnergySpectrumView), "fsaLayersSource").SetValue(view, result);
                    Field(typeof(EnergySpectrumView), "fsaColors")
                        .SetValue(view, new Dictionary<string, Color>());

                    Rectangle whole = new Rectangle(row.X, row.Y - RowHeight,
                                                    row.Width, row.Height + RowHeight);
                    Method("DrawFsaRows").Invoke(view,
                        new object[] { g, whole, result, string.Empty, int.MaxValue });
                }

                using (Graphics g = Graphics.FromImage(alone))
                {
                    g.Clear(Color.White);
                    Method("DrawFsaQualityRow").Invoke(view, new object[] { g, row, result });
                }

                return SamePixels(table, alone, row);
            }
        }

        /// <summary>
        /// Пиксели строки, нарисованной приложением, ПОБИТОВО совпадают с
        /// независимой отрисовкой `text` в ту же ширину тем же форматом.
        ///
        /// ⚠ Годится только там, где хвост НЕ урезан (см. <see cref="Shown"/>):
        /// урезание GDI+ перекладывает строку заново, и совпадения не будет
        /// даже при том же составе знаков.
        /// </summary>
        static bool PixelsShow(EnergySpectrumView view, FsaResult result, int rowWidth, string text)
        {
            string value = result.Chi2Ndf.ToString("n2");
            Rectangle row = new Rectangle(Pad, Pad, rowWidth, RowHeight);
            int canvas = rowWidth + 2 * Pad;
            StringFormat far =
                (StringFormat)Field(typeof(EnergySpectrumView), "farFormat").GetValue(view);

            using (Bitmap shot = new Bitmap(canvas, RowHeight + 2 * Pad, PixelFormat.Format32bppArgb))
            using (Bitmap same = new Bitmap(canvas, RowHeight + 2 * Pad, PixelFormat.Format32bppArgb))
            {
                int marks;
                using (Graphics g = Graphics.FromImage(shot))
                {
                    g.Clear(Color.White);
                    marks = EnergySpectrumView.FsaQualityMarksWidth(g, view.Font, rowWidth, value);
                    Method("DrawFsaQualityRow").Invoke(view, new object[] { g, row, result });
                }

                using (Graphics g = Graphics.FromImage(same))
                {
                    g.Clear(Color.White);
                    using (StringFormat trimmed = EnergySpectrumView.FsaQualityMarksFormat())
                    {
                        g.DrawString(text, view.Font, Brushes.Black,
                                     new Rectangle(row.X, row.Y, marks, row.Height), trimmed);
                    }

                    g.DrawString(value, view.Font, Brushes.Black, row, far);
                }

                return SamePixels(shot, same, row);
            }
        }

        /// <summary>
        /// СКОЛЬКО ЗНАКОВ ХВОСТА ВИДНО: наибольшее `k`, при котором начало
        /// строки длиной `k` с многоточием на конце рисуется в отведённую
        /// ширину БЕЗ урезания.
        ///
        /// ⛔ Почему так, а не «восстановить урезанный текст по пикселям»
        /// (первый заход, снят замером). GDI+ при урезании ПЕРЕКЛАДЫВАЕТ
        /// строку заново: отрисовка «χ²/ndf · подавлен · старая матрица (без
        /// кривой) !» в 192 пкс расходится с отрисовкой любого своего начала
        /// уже НА ПЕРВОМ ЗНАКЕ (мерено: первый несовпавший столбец — 9-й при
        /// левом крае 8). Совпадения искать бессмысленно; а вот «урезано или
        /// нет» пиксели отвечают точно: отрисовка в ширину W и в заведомо
        /// большую ширину совпадают побитово ровно тогда, когда урезания нет.
        /// На этом признаке и стоит поиск делением пополам.
        ///
        /// ⚠ Число сходится с ответом самого GDI+ (`MeasureString` с
        /// `charactersFitted`) с разницей ровно в 1 знак — тот, чьё место
        /// занимает многоточие.
        /// </summary>
        static int Shown(EnergySpectrumView view, FsaResult result, int rowWidth, string full)
        {
            int marks = MarksWidth(view, rowWidth, result);
            if (Untrimmed(view, full, marks))
            {
                return full.Length;
            }

            int lo = 0;
            int hi = full.Length;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (Untrimmed(view, Visible(full, mid), marks))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        /// <summary>Текст на экране при `shown` видимых знаках.</summary>
        static string Visible(string full, int shown)
        {
            return shown >= full.Length ? full : full.Substring(0, shown) + "…";
        }

        /// <summary>
        /// Рисуется ли `text` в ширину `marks` БЕЗ урезания: отрисовка в эту
        /// ширину и в заведомо большую совпадают побитово.
        /// </summary>
        static bool Untrimmed(EnergySpectrumView view, string text, int marks)
        {
            int roomy = TextWidth(view, text) + 4 * RowHeight;
            int canvas = Math.Max(marks, roomy) + 2 * Pad;
            Rectangle band = new Rectangle(Pad, Pad, canvas - 2 * Pad, RowHeight);

            using (Bitmap tight = new Bitmap(canvas, RowHeight + 2 * Pad, PixelFormat.Format32bppArgb))
            using (Bitmap wide = new Bitmap(canvas, RowHeight + 2 * Pad, PixelFormat.Format32bppArgb))
            {
                Paint(view, tight, text, marks);
                Paint(view, wide, text, roomy);
                return SamePixels(tight, wide, band);
            }
        }

        static void Paint(EnergySpectrumView view, Bitmap target, string text, int width)
        {
            using (Graphics g = Graphics.FromImage(target))
            using (StringFormat trimmed = EnergySpectrumView.FsaQualityMarksFormat())
            {
                g.Clear(Color.White);
                g.DrawString(text, view.Font, Brushes.Black,
                             new Rectangle(Pad, Pad, width, RowHeight), trimmed);
            }
        }

        /// <summary>Побитовое сравнение полосы.</summary>
        static bool SamePixels(Bitmap a, Bitmap b, Rectangle band)
        {
            BitmapData da = a.LockBits(band, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData db = b.LockBits(band, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = band.Width * 4;
                byte[] ra = new byte[bytes];
                byte[] rb = new byte[bytes];
                for (int y = 0; y < band.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(da.Scan0, y * da.Stride), ra, 0, bytes);
                    Marshal.Copy(IntPtr.Add(db.Scan0, y * db.Stride), rb, 0, bytes);
                    for (int i = 0; i < bytes; i++)
                    {
                        if (ra[i] != rb[i])
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
            finally
            {
                a.UnlockBits(da);
                b.UnlockBits(db);
            }
        }

        static void Shot(EnergySpectrumView view, FsaResult result, int rowWidth, string path)
        {
            Rectangle row = new Rectangle(Pad, Pad, rowWidth, RowHeight);
            using (Bitmap image = new Bitmap(rowWidth + 2 * Pad, RowHeight + 2 * Pad,
                                             PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(Color.White);
                    Method("DrawFsaQualityRow").Invoke(view, new object[] { g, row, result });
                }

                image.Save(path, ImageFormat.Png);
            }
        }

        static int MarksWidth(EnergySpectrumView view, int rowWidth, FsaResult result)
        {
            using (Bitmap image = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(image))
            {
                return EnergySpectrumView.FsaQualityMarksWidth(
                    g, view.Font, rowWidth, result.Chi2Ndf.ToString("n2"));
            }
        }

        static int TextWidth(EnergySpectrumView view, string text)
        {
            using (Bitmap image = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(image))
            {
                return (int)Math.Ceiling(g.MeasureString(text, view.Font).Width);
            }
        }

        /// <summary>
        /// Наименьшая ширина строки, при которой в видимое попадают все `end`
        /// знаков до конца пометки. Ищется делением пополам; признак монотонен
        /// по ширине — что поместилось, шире не исчезает.
        /// </summary>
        static int MinRowWidth(EnergySpectrumView view, FsaResult result, string full, int end)
        {
            int lo = 20;
            int hi = 900;
            if (Shown(view, result, hi, full) < end)
            {
                return -1;
            }

            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (Shown(view, result, mid, full) >= end)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }

            return hi;
        }

        // ------------------------------------------------------------------
        // Мелочь
        // ------------------------------------------------------------------

        static bool Has(string text, string mark)
        {
            return text.IndexOf(mark, StringComparison.Ordinal) >= 0;
        }

        static int DistinctCount(params string[] items)
        {
            List<string> seen = new List<string>();
            foreach (string item in items)
            {
                if (!seen.Contains(item))
                {
                    seen.Add(item);
                }
            }

            return seen.Count;
        }

        static string Mark(string name)
        {
            PropertyInfo p = typeof(BecquerelMonitor.Properties.Resources).GetProperty(
                name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null)
            {
                Console.WriteLine("  ⛔ нет ресурса {0}", name);
                bad++;
                return " ";
            }

            return (string)p.GetValue(null, null);
        }

        /// <summary>
        /// Язык МЕНЯЕТСЯ ЦЕЛИКОМ — и надписи, и разделитель числа: по-русски
        /// хвост длиннее, а «2,94» шире «2.94», и обе половины влияют на то,
        /// сколько места осталось пометкам.
        /// </summary>
        static void Language(string name)
        {
            CultureInfo culture = new CultureInfo(name);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-64} {2}{3}", ok ? "ok  " : "⛔ ", what, got,
                              ok ? string.Empty : string.Format("  вместо {0}", expected));
            if (!ok)
            {
                bad++;
            }
        }

        /// <summary>
        /// Проверка ПОЛОЖИТЕЛЬНОГО контроля: `found` обязан быть ЛОЖЬЮ.
        /// Отдельным приёмом нарочно — «сторож промолчал на порченом входе» и
        /// «сторож нашёл дефект» читаются в отчёте по-разному.
        /// </summary>
        static void Denies(string what, bool found)
        {
            Console.WriteLine("  {0} {1,-64} {2}", found ? "⛔ " : "ok  ", what,
                              found ? "СТОРОЖ ПРОМОЛЧАЛ" : "отказал, как и должен");
            if (found)
            {
                bad++;
            }
        }
    }
}
