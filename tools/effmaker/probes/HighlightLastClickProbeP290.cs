using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П290, 14.10.2026) ПОДСВЕТКА FSA: ГЛАВНЕЕ ПОСЛЕДНИЙ ЩЕЛЧОК ЧЕЛОВЕКА.
    ///
    /// Два окна пишут одну подсветку графика — окно отчёта FSA (<c>FSAReportView</c>) и
    /// окно результата (<c>DCResultView</c>). Находка П287 (чтением кода): перестройка
    /// отчёта после каждого счёта (<c>RefreshReport → RestoreSelection → PushHighlight</c>)
    /// заново шлёт графику выбор ОТЧЁТА и стирает подсветку, поставленную щелчком в окне
    /// результата. Правило (умолчание распорядителя, не решение Amber): перестройка окна —
    /// не действие человека и возвращает подсветку своего окна, только если последним её
    /// ставило оно само; щелчок человека в любом окне ставит своё.
    ///
    /// Проба держит ОБА окна разом на одном документе (приёмы F16 / П254 / П287): отчёт —
    /// на документе пробы, окно результата — на <c>MainForm</c> с тем же документом. Щелчок
    /// человека — настоящий <c>Table.OnMouseDown</c> по середине ячейки строки (XPTable сам
    /// решает, менять ли выбор). Перестройка — настоящие обработчики конца счёта:
    /// <c>FSAReportView.SessionCompleted</c>, <c>DCResultView.FsaSessionCompleted</c>; пересчёт —
    /// настоящий (<c>Invalidate</c> + <c>EnsureUpToDate</c>), его события доходят до обоих окон
    /// сами. Кадр — настоящий <c>EnergySpectrumView.ShowFsaOverlay</c>, слои судятся маской
    /// лент <c>DrawFsaBand</c> (приём П260 / П287).
    ///
    ///   HighlightLastClickProbeP290 --spectrum=&lt;xml&gt; --eq=on|off --db=on|off
    ///                               --res=&lt;строка окна результата&gt; --rep=&lt;другая строка отчёта&gt;
    ///                               [--timeout=300]
    ///
    /// Коды: 0 — все шаги по правилу; 4 — есть шаги не по правилу (на сборке «до» — это
    /// положительный контроль дефекта); 3 — разбор не дошёл; 5 — обвязка (щелчок не
    /// принят, строки нет); 2 — ключи.
    /// </summary>
    static class HighlightLastClickProbeP290
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int Width = 1200, Height = 620;
        static int timeoutS = 300;
        static int offRule, rigFail;
        static Bitmap noneFrame, maskFrame;
        static string noneSha;

        static DocEnergySpectrum doc;
        static EnergySpectrumView view;
        static FSAReportView report;
        static DCResultView results;
        static Func<FsaCalculationOptions> make;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string path = null, res = null, rep = null;
            bool eq = true, db = true;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--eq=", StringComparison.Ordinal)) eq = a.Substring(5) == "on";
                else if (a.StartsWith("--db=", StringComparison.Ordinal)) db = a.Substring(5) == "on";
                else if (a.StartsWith("--res=", StringComparison.Ordinal)) res = a.Substring(6);
                else if (a.StartsWith("--rep=", StringComparison.Ordinal)) rep = a.Substring(6);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (path == null || res == null || rep == null)
            {
                Console.Error.WriteLine("нужны --spectrum=, --res=, --rep=");
                return 2;
            }

            Application.EnableVisualStyles();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            doc = Open(path, nuclides);
            if (doc == null) return 2;
            ResultData rd = doc.ActiveResultData;
            var cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            make = () =>
            {
                FsaCalculationOptions o = FsaCalculationOptions.FromConfig(cfg);
                o.DbLookups = db;
                o.ChainEquilibrium = eq;
                return o;
            };
            FsaCalculationOptions.ApplicationSource = make;
            FsaAnalysisSession session = doc.FsaSession;
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, make());
            WaitIdle(session);
            if (session.Result == null)
            {
                Console.WriteLine("разбора нет: {0}", session.Status ?? "?");
                return 3;
            }
            Console.WriteLine("SETUP\t{0}: равновесие {1}, состав {2}, χ²/ndf {3}", Path.GetFileName(path),
                              eq ? "ВКЛ" : "ВЫКЛ", db ? "NucBase" : "по подписям",
                              session.Result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture));
            view = doc.EnergySpectrumView;
            Setup(view, rd, session.Result);

            // ---- оба окна разом на одном документе ----
            report = new FSAReportView(null);
            Form hostReport = Host(report, 460, 900, -4000);
            report.SetDocument(doc);
            Pump(200);
            var mainForm = new MainForm();
            mainForm.ActiveDocument = doc;
            results = new DCResultView(mainForm);
            Form hostResults = Host(results, 640, 400, -3000);
            results.ShowResult(true);
            WaitIdle(session);
            Pump(300);
            results.ShowResult(true);
            Pump(100);

            Console.WriteLine("SETUP\tстроки отчёта со слоем: [{0}]", string.Join(", ", ReportRows().Keys));
            Console.WriteLine("SETUP\tстроки окна результата с образом: [{0}]", string.Join(", ", ResultRows().Keys));
            if (!ReportRows().ContainsKey(rep) || !ResultRows().ContainsKey(res))
            {
                Console.WriteLine("⛔ обвязка: нет строки «{0}» в отчёте или «{1}» в окне результата", rep, res);
                return 5;
            }
            bool repHasRes = ReportRows().ContainsKey(res);

            // кадр без выбора и маска лент — один раз; в конце сверяется, что кадр без
            // выбора тот же (разбор детерминирован, пересчёт ленты не сдвинул)
            Reset();
            noneFrame = Render();
            maskFrame = Mask();
            noneSha = Sha(noneFrame);
            List<FsaStackLayer> layers = Layers();
            Console.WriteLine("SETUP\tслоёв в стеке {0}: [{1}]; кадр без выбора sha {2}", layers.Count,
                              string.Join(", ", layers.ConvertAll(l => l.Name + (l.ChainRoot != null ? "→" + l.ChainRoot : ""))), noneSha);

            // ==== 1. щелчок в окне результата, затем перестройка отчёта (отчёт без выбора) ====
            Step("1", "отчёт без выбора → щелчок результата «" + res + "» → перестройка отчёта (конец счёта)");
            Reset();
            ClickResult(res);
            Show("после щелчка результата", res, true, null);
            RebuildReport();
            Show("после перестройки отчёта", res, true, "главный случай находки П287");

            // ==== 2. отчёт выбрал ту же строку, затем щелчок результата, затем перестройка ====
            if (repHasRes)
            {
                Step("2", "щелчок отчёта «" + res + "» → щелчок результата «" + res + "» → перестройка отчёта");
                Reset();
                ClickReport(res);
                Show("после щелчка отчёта", res, false, null);
                ClickResult(res);
                Show("после щелчка результата", res, true, null);
                RebuildReport();
                Show("после перестройки отчёта", res, true, "П287: смена «весь ряд» → «только корень»");
            }

            // ==== 3. отчёт выбрал другую строку, затем щелчок результата, затем перестройка ====
            Step("3", "щелчок отчёта «" + rep + "» → щелчок результата «" + res + "» → перестройка отчёта");
            Reset();
            ClickReport(rep);
            Show("после щелчка отчёта", rep, false, null);
            ClickResult(res);
            Show("после щелчка результата", res, true, null);
            RebuildReport();
            Show("после перестройки отчёта", res, true, null);

            // ==== 4. то же, но настоящий пересчёт: события доходят до обоих окон ====
            Step("4", "щелчок отчёта «" + rep + "» → щелчок результата «" + res + "» → ПЕРЕСЧЁТ (оба окна перестроены)");
            Reset();
            ClickReport(rep);
            ClickResult(res);
            Show("после щелчка результата", res, true, null);
            Recalc();
            Show("после пересчёта", res, true, null, res);
            Recalc();
            Show("после второго пересчёта", res, true, null, res);

            // ==== 5. обратный путь: щелчок отчёта после щелчка результата, перестройка результата ====
            Step("5", "щелчок результата «" + res + "» → щелчок отчёта «" + rep + "» → перестройка окна результата → пересчёт");
            Reset();
            ClickResult(res);
            ClickReport(rep);
            Show("после щелчка отчёта", rep, false, "щелчок отчёта после щелчка результата ставит своё");
            RebuildResult();
            Show("после перестройки окна результата", rep, false, "обратный путь", res);
            Recalc();
            Show("после пересчёта", rep, false, null, res);

            // ==== 6. своё отчёт возвращает: последним ставил он ====
            Step("6", "щелчок отчёта «" + rep + "» → перестройка отчёта → пересчёт");
            Reset();
            ClickReport(rep);
            RebuildReport();
            Show("после перестройки отчёта", rep, false, "своё — возвращается");
            Recalc();
            Show("после пересчёта", rep, false, null);

            // ==== 7. повторный щелчок по УЖЕ выбранной строке ====
            Step("7а", "щелчок отчёта «" + rep + "» → щелчок результата «" + res + "» → снова щелчок отчёта по выбранной «" + rep + "»");
            Reset();
            ClickReport(rep);
            ClickResult(res);
            ClickReport(rep);
            Show("после повторного щелчка отчёта", rep, false, "щелчок по выбранной строке");
            Step("7б", "щелчок результата «" + res + "» → щелчок отчёта «" + rep + "» → снова щелчок результата по выбранной «" + res + "»");
            Reset();
            ClickResult(res);
            ClickReport(rep);
            Show("после щелчка отчёта", rep, false, null, res);
            ClickResult(res);
            Show("после повторного щелчка результата", res, true, "щелчок по выбранной строке");

            // ==== 8. окно результата после своей перестройки: видно ли выбор ====
            Step("8", "щелчок результата «" + res + "» → перестройка окна результата → пересчёт: выбор строки на экране");
            Reset();
            ClickResult(res);
            RebuildResult();
            Show("после перестройки окна результата", res, true, null, res);
            Recalc();
            Show("после пересчёта", res, true, null, res);

            // ---- контроль кадра без выбора ----
            Reset();
            using (Bitmap again = Render())
            {
                string sha = Sha(again);
                Console.WriteLine();
                Console.WriteLine("КОНТРОЛЬ\tкадр без выбора в конце sha {0} — {1}", sha, sha == noneSha ? "тот же" : "⛔ ДРУГОЙ");
                if (sha != noneSha) rigFail++;
            }

            hostResults.Hide();
            results.Dispose();
            hostResults.Dispose();
            mainForm.ActiveDocument = null;
            mainForm.Dispose();
            report.SetDocument(null);
            hostReport.Hide();
            report.Dispose();
            hostReport.Dispose();
            doc.Dispose();
            Console.WriteLine();
            Console.WriteLine("ИТОГ ПРОБЫ: не по правилу {0}, отказов обвязки {1}", offRule, rigFail);
            if (rigFail > 0) return 5;
            return offRule == 0 ? 0 : 4;
        }

        // ==================================================================
        // Действия человека и окон
        // ==================================================================

        static void Step(string id, string text)
        {
            Console.WriteLine();
            Console.WriteLine("=== ШАГ {0}: {1}", id, text);
        }

        /// <summary>Снять выбор в обоих окнах (действием окон) — исход: подсветки нет.</summary>
        static void Reset()
        {
            object sel = Selections(TableModelOf(results, "tableModel1"));
            sel.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(sel, null);
            Pump(30);
            report.SelectComponent(null);
            Pump(30);
            if (HighlightOf() != null)
            {
                Console.WriteLine("  ⛔ обвязка: снятие выбора в обоих окнах не сняло подсветку: «{0}»", HighlightOf());
                rigFail++;
            }
        }

        static void ClickReport(string name)
        {
            int row;
            if (!ReportRows().TryGetValue(name, out row))
            {
                Console.WriteLine("  ⛔ обвязка: строки «{0}» в отчёте нет", name);
                rigFail++;
                return;
            }
            Click(Field(typeof(FSAReportView), "reportTable").GetValue(report) as Control, row);
        }

        static void ClickResult(string name)
        {
            int row;
            if (!ResultRows().TryGetValue(name, out row))
            {
                Console.WriteLine("  ⛔ обвязка: строки «{0}» в окне результата нет", name);
                rigFail++;
                return;
            }
            Click(Field(typeof(DCResultView), "table1").GetValue(results) as Control, row);
        }

        /// <summary>Щелчок мышью — настоящий <c>Table.OnMouseDown</c>/<c>OnMouseUp</c> по середине ячейки.</summary>
        static void Click(Control table, int row)
        {
            var rect = (Rectangle)table.GetType().GetMethod("CellRect", new[] { typeof(int), typeof(int) }).Invoke(table, new object[] { row, 0 });
            if (rect.IsEmpty)
            {
                Console.WriteLine("  ⛔ обвязка: у строки {0} нет прямоугольника", row);
                rigFail++;
                return;
            }
            var e = new MouseEventArgs(MouseButtons.Left, 1, rect.X + Math.Min(20, rect.Width / 2), rect.Y + rect.Height / 2, 0);
            MethodInfo down = table.GetType().GetMethod("OnMouseDown", Any, null, new[] { typeof(MouseEventArgs) }, null);
            MethodInfo up = table.GetType().GetMethod("OnMouseUp", Any, null, new[] { typeof(MouseEventArgs) }, null);
            down.Invoke(table, new object[] { e });
            up.Invoke(table, new object[] { e });
            Pump(50);
        }

        /// <summary>Перестройка отчёта — настоящий обработчик конца счёта (отложенно, как в приложении).</summary>
        static void RebuildReport()
        {
            typeof(FSAReportView).GetMethod("SessionCompleted", Any).Invoke(report, new object[] { null, EventArgs.Empty });
            Pump(300);
        }

        /// <summary>Перестройка окна результата — настоящий обработчик конца счёта.</summary>
        static void RebuildResult()
        {
            typeof(DCResultView).GetMethod("FsaSessionCompleted", Any).Invoke(results, new object[] { null, EventArgs.Empty });
            Pump(300);
        }

        /// <summary>Настоящий пересчёт сеанса документа (как такт набора раз в 30 с).</summary>
        static void Recalc()
        {
            FsaAnalysisSession session = doc.FsaSession;
            ResultData rd = doc.ActiveResultData;
            int before = session.RunCount;
            session.Invalidate();
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, make());
            Pump(100);
            WaitIdle(session);
            Pump(500);
            if (session.RunCount == before)
            {
                Console.WriteLine("  ⛔ обвязка: пересчёт не запустился");
                rigFail++;
            }
        }

        // ==================================================================
        // Чтение состояния
        // ==================================================================

        /// <param name="expectResultRow">
        /// Не null — строка, которая обязана быть выбрана НА ЭКРАНЕ окна результата
        /// (единственной): выбор этого окна не теряется при его перестройке.
        /// </param>
        static void Show(string when, string expectName, bool expectChain, string note, string expectResultRow = null)
        {
            string hl = HighlightOf();
            bool chain = ChainOf();
            List<FsaStackLayer> layers = Layers();
            int lit = 0, muted = 0, mixed = 0;
            var litNames = new List<string>();
            using (Bitmap frame = Render())
            {
                long[] region = new long[layers.Count + 1];
                long[] changed = new long[layers.Count + 1];
                for (int y = 0; y < maskFrame.Height; y++)
                {
                    for (int x = 0; x < maskFrame.Width; x++)
                    {
                        int id = maskFrame.GetPixel(x, y).R;
                        if (id == 0 || id > layers.Count) continue;
                        region[id]++;
                        if (noneFrame.GetPixel(x, y).ToArgb() != frame.GetPixel(x, y).ToArgb()) changed[id]++;
                    }
                }
                for (int k = 0; k < layers.Count; k++)
                {
                    long r = region[k + 1], c = changed[k + 1];
                    if (r == 0) continue;
                    if (c == 0) { lit++; litNames.Add(layers[k].Name); }
                    else if (c * 10 > r * 9) muted++;
                    else mixed++;
                }
                bool ok = string.Equals(hl, expectName, StringComparison.Ordinal) && chain == expectChain && mixed == 0;
                if (!ok) offRule++;
                Console.WriteLine("  {0}: подсветка «{1}», весь ряд {2}; горит {3} [{4}], приглушено {5}, смешанных {6}; sha {7}",
                                  when, hl ?? "нет", chain ? "да" : "нет", lit, string.Join(", ", litNames), muted, mixed, Sha(frame));
                Console.WriteLine("    выбор: отчёт «{0}», окно результата на экране [{1}], SelectedIndicies [{2}]",
                                  report.SelectedComponent ?? "нет", string.Join(", ", ResultVisiblySelected()), string.Join(", ", ResultSelectedIndicies()));
                Console.WriteLine("    по правилу «{0}», весь ряд {1}: {2}{3}", expectName, expectChain ? "да" : "нет",
                                  ok ? "ок" : "НЕ ПО ПРАВИЛУ", note != null ? "  (" + note + ")" : "");
                if (expectResultRow != null)
                {
                    List<string> shown = ResultVisiblySelected();
                    bool kept = shown.Count == 1 && shown[0] == expectResultRow;
                    if (!kept) offRule++;
                    Console.WriteLine("    выбор окна результата на экране [{0}], ждём [{1}]: {2}",
                                      string.Join(", ", shown), expectResultRow, kept ? "ок" : "ВЫБОР ПОТЕРЯН");
                }
            }
        }

        static string HighlightOf()
        {
            return (string)typeof(EnergySpectrumView).GetProperty("FsaHighlight", Any).GetValue(view, null);
        }

        static bool ChainOf()
        {
            return (bool)typeof(EnergySpectrumView).GetProperty("FsaHighlightWholeChain", Any).GetValue(view, null);
        }

        /// <summary>Имя слоя → номер строки отчёта.</summary>
        static Dictionary<string, int> ReportRows()
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            IList rows = RowsOf(TableModelOf(report, "tableModel"));
            for (int i = 0; i < rows.Count; i++)
            {
                object tag = rows[i].GetType().GetProperty("Tag").GetValue(rows[i], null);
                if (tag == null) continue;
                PropertyInfo lp = tag.GetType().GetProperty("Layer");
                object layer = lp != null ? lp.GetValue(tag, null) : null;
                if (layer == null) continue;
                string name = (string)layer.GetType().GetProperty("Name").GetValue(layer, null);
                if (name != null && !map.ContainsKey(name)) map[name] = i;
            }
            return map;
        }

        /// <summary>Имя образа → номер строки окна результата.</summary>
        static Dictionary<string, int> ResultRows()
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            IList rows = RowsOf(TableModelOf(results, "tableModel1"));
            var collection = (MeasurementResultCollection)Field(typeof(DCResultView), "previousCollection").GetValue(results);
            for (int i = 0; i < rows.Count; i++)
            {
                object tag = rows[i].GetType().GetProperty("Tag").GetValue(rows[i], null);
                if (tag is int index && collection != null && index >= 0 && index < collection.ResultList.Count)
                {
                    MeasurementResult m = collection.ResultList[index];
                    if (m.IsValid && m.Line != null && !map.ContainsKey(m.Line.Name)) map[m.Line.Name] = i;
                }
            }
            return map;
        }

        static List<string> ResultVisiblySelected()
        {
            var names = new List<string>();
            IList rows = RowsOf(TableModelOf(results, "tableModel1"));
            var byRow = new Dictionary<int, string>();
            foreach (KeyValuePair<string, int> kv in ResultRows()) byRow[kv.Value] = kv.Key;
            for (int i = 0; i < rows.Count; i++)
            {
                bool any = (bool)rows[i].GetType().GetProperty("AnyCellsSelected").GetValue(rows[i], null);
                if (any) names.Add(byRow.ContainsKey(i) ? byRow[i] : "#" + i.ToString(CultureInfo.InvariantCulture));
            }
            return names;
        }

        static List<string> ResultSelectedIndicies()
        {
            object sel = Selections(TableModelOf(results, "tableModel1"));
            int[] idx = (int[])sel.GetType().GetProperty("SelectedIndicies").GetValue(sel, null);
            var list = new List<string>();
            if (idx != null) foreach (int i in idx) list.Add(i.ToString(CultureInfo.InvariantCulture));
            return list;
        }

        static object TableModelOf(object window, string field)
        {
            return Field(window.GetType(), field).GetValue(window);
        }

        static IList RowsOf(object tableModel)
        {
            return (IList)tableModel.GetType().GetProperty("Rows").GetValue(tableModel, null);
        }

        static object Selections(object tableModel)
        {
            return tableModel.GetType().GetProperty("Selections").GetValue(tableModel, null);
        }

        // ==================================================================
        // Кадр (приём П260 / П287): настоящий ShowFsaOverlay, маска лент DrawFsaBand
        // ==================================================================

        static List<FsaStackLayer> Layers()
        {
            object presentation = Field(typeof(EnergySpectrumView), "fsaPresentation").GetValue(view);
            return (List<FsaStackLayer>)presentation.GetType().GetProperty("Layers").GetValue(presentation, null);
        }

        static Bitmap Render()
        {
            var image = new Bitmap(Width, Height);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(Color.Black);
                typeof(EnergySpectrumView).GetMethod("ShowFsaOverlay", Any).Invoke(view, new object[] { g });
            }
            return image;
        }

        static Bitmap Mask()
        {
            var image = new Bitmap(Width, Height);
            List<FsaStackLayer> layers = Layers();
            var cumulative = (double[][])Field(typeof(EnergySpectrumView), "fsaCumulative").GetValue(view);
            var zero = (double[])Field(typeof(EnergySpectrumView), "fsaZeroLevel").GetValue(view);
            MethodInfo band = typeof(EnergySpectrumView).GetMethod("DrawFsaBand", Any);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(Color.Black);
                for (int k = 0; k < layers.Count; k++)
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

        static string Sha(Bitmap image)
        {
            Rectangle all = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData data = image.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] bytes = new byte[data.Stride * data.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                using (var sha = SHA256.Create())
                {
                    byte[] h = sha.ComputeHash(bytes);
                    var sb = new StringBuilder();
                    for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                    return sb.ToString();
                }
            }
            finally
            {
                image.UnlockBits(data);
            }
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

        // ==================================================================
        // Обвязка (образцы F16 / П254 / П287)
        // ==================================================================

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
