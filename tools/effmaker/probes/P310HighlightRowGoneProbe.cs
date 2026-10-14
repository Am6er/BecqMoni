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
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П310, 15.10.2026) ПОДСВЕТКА ОКНА РЕЗУЛЬТАТА, ЧЬЯ СТРОКА УШЛА ПРИ ПЕРЕСТРОЙКЕ.
    ///
    /// Ревизия П290 («главнее последний щелчок»). Окно отчёта при перестройке, у которой
    /// своей строки больше нет, снимает СВОЮ подсветку (<c>RestoreSelection</c> →
    /// <c>PushHighlightIfOwn</c> с null). Окно результата после П290 переносит выбор на
    /// строку того же имени молча, а если строки больше нет — выбор пропадает, подсветка
    /// же остаётся: график приглушён, а выбранной строки нет ни в одном окне. Ровно это
    /// состояние журнал П290 называет недопустимым («остался бы приглушённым без видимого
    /// выбора где-либо»). До П290 подсветку стирала перестройка отчёта.
    ///
    /// Инвариант, который судит проба в каждом покое: подсветка графика есть ⇒ строка с её
    /// именем выбрана на экране в окне-хозяине (отчёт — признак ряда «нет», окно результата
    /// — «да»). Приглушение мерится кадром: настоящий <c>ShowFsaOverlay</c> с подсветкой
    /// против него же без подсветки (подсветка ставится обратно ровно той же).
    ///
    ///   P310HighlightRowGoneProbe --spectrum=&lt;xml&gt; [--off-row=Tl-208] [--on-row=Th-232]
    ///                             [--rep=Pb-212] [--timeout=300]
    ///
    /// Шаги: A — главный (строка окна результата «Tl-208» при равновесии ВЫКЛ → ВКЛ:
    /// строки нет); B — контроль «строка жива» (ВКЛ, «Th-232», пересчёт); C — контроль
    /// «подсветка чужая» (щелчок отчёта после щелчка результата, ВЫКЛ → ВКЛ — правка окна
    /// результата чужого не трогает).
    /// Коды: 0 — инвариант держится везде; 4 — нарушен (на сборке «до» — положительный
    /// контроль дефекта); 3 — разбор не дошёл; 5 — обвязка; 2 — ключи.
    /// </summary>
    static class P310HighlightRowGoneProbe
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int Width = 1200, Height = 620;
        static int timeoutS = 300;
        static int broken, rigFail;
        static bool eq;

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
            string path = null, offRow = "Tl-208", onRow = "Th-232", rep = "Pb-212";
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--off-row=", StringComparison.Ordinal)) offRow = a.Substring(10);
                else if (a.StartsWith("--on-row=", StringComparison.Ordinal)) onRow = a.Substring(9);
                else if (a.StartsWith("--rep=", StringComparison.Ordinal)) rep = a.Substring(6);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (path == null)
            {
                Console.Error.WriteLine("нужен --spectrum=");
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
            eq = false;
            make = () =>
            {
                FsaCalculationOptions o = FsaCalculationOptions.FromConfig(cfg);
                o.DbLookups = true;
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
            view = doc.EnergySpectrumView;
            Setup(view, rd, session.Result);

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
            Console.WriteLine("SETUP\t{0}: равновесие ВЫКЛ, χ²/ndf {1}", Path.GetFileName(path),
                              session.Result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture));
            Console.WriteLine("SETUP\tстроки окна результата: [{0}]", string.Join(", ", ResultRows().Keys));
            Console.WriteLine("SETUP\tстроки отчёта со слоем: [{0}]", string.Join(", ", ReportRows().Keys));
            if (!ResultRows().ContainsKey(offRow) || !ReportRows().ContainsKey(rep))
            {
                Console.WriteLine("⛔ обвязка: нет строки «{0}» в окне результата или «{1}» в отчёте", offRow, rep);
                return 5;
            }

            // ==== A. главный: строка окна результата уходит при перестройке ====
            Step("A", "ВЫКЛ: щелчок результата «" + offRow + "» → равновесие ВКЛ (пересчёт): строки «" + offRow + "» больше нет");
            Reset();
            ClickResult(offRow);
            Judge("после щелчка результата", true);
            SetEquilibrium(true);
            Console.WriteLine("  строки окна результата после ВКЛ: [{0}]", string.Join(", ", ResultRows().Keys));
            if (ResultRows().ContainsKey(offRow))
            {
                Console.WriteLine("  ⛔ обвязка: строка «{0}» осталась — сцена не та", offRow);
                rigFail++;
            }
            Judge("после пересчёта с равновесием ВКЛ", false);

            // ==== B. контроль: строка жива — подсветка и выбор переживают пересчёт ====
            Step("B", "ВКЛ: щелчок результата «" + onRow + "» → пересчёт (строка остаётся)");
            Reset();
            if (!ResultRows().ContainsKey(onRow))
            {
                Console.WriteLine("  ⛔ обвязка: нет строки «{0}» при ВКЛ", onRow);
                rigFail++;
            }
            else
            {
                ClickResult(onRow);
                Judge("после щелчка результата", true);
                Recalc();
                Judge("после пересчёта", true, onRow, true);
            }

            // ==== C. контроль: подсветка ОТЧЁТА, строка окна результата уходит ====
            Step("C", "ВЫКЛ: щелчок результата «" + offRow + "» → щелчок отчёта «" + rep + "» → равновесие ВКЛ");
            SetEquilibrium(false);
            Reset();
            ClickResult(offRow);
            ClickReport(rep);
            Judge("после щелчка отчёта", true, rep, false);
            SetEquilibrium(true);
            Judge("после пересчёта с равновесием ВКЛ", false, ReportRows().ContainsKey(rep) ? rep : null, false);

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
            Console.WriteLine("ИТОГ ПРОБЫ: нарушений инварианта {0}, отказов обвязки {1}", broken, rigFail);
            if (rigFail > 0) return 5;
            return broken == 0 ? 0 : 4;
        }

        static void Step(string id, string text)
        {
            Console.WriteLine();
            Console.WriteLine("=== ШАГ {0}: {1}", id, text);
        }

        /// <summary>
        /// Судить покой: подсветка есть ⇒ строка с её именем видимо выбрана в окне-хозяине.
        /// <paramref name="expectLit"/> — ждём ли подсветку вообще; <paramref name="expectName"/>
        /// и <paramref name="expectChain"/> — если заданы, ждём именно её.
        /// </summary>
        static void Judge(string when, bool expectLit, string expectName = null, bool expectChain = false)
        {
            string hl = HighlightOf();
            bool chain = ChainOf();
            int changed = MutedPixels(hl, chain);
            List<string> resultShown = ResultVisiblySelected();
            string reportSel = report.SelectedComponent;
            bool owned = hl == null
                         || (chain && resultShown.Count == 1 && resultShown[0] == hl)
                         || (!chain && string.Equals(reportSel, hl, StringComparison.Ordinal));
            bool ok = owned;
            if (expectName != null)
            {
                ok &= string.Equals(hl, expectName, StringComparison.Ordinal) && chain == expectChain;
            }
            else if (expectLit)
            {
                ok &= hl != null;
            }
            if (!ok) broken++;
            Console.WriteLine("  {0}: подсветка «{1}», весь ряд {2}; пикселей приглушения {3}", when, hl ?? "нет",
                              chain ? "да" : "нет", changed.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("    выбор: отчёт «{0}», окно результата на экране [{1}] — {2}", reportSel ?? "нет",
                              string.Join(", ", resultShown),
                              ok ? "ок" : (owned ? "НЕ ТА ПОДСВЕТКА" : "⛔ ГРАФИК ПРИГЛУШЁН БЕЗ ВИДИМОГО ВЫБОРА"));
        }

        static void SetEquilibrium(bool on)
        {
            eq = on;
            Recalc();
        }

        static void Reset()
        {
            object sel = Selections(TableModelOf(results, "tableModel1"));
            sel.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(sel, null);
            Pump(30);
            report.SelectComponent(null);
            Pump(30);
            if (HighlightOf() != null)
            {
                // снятие выбора в окнах подсветку окна результата не снимает, если она
                // уже ничья (ровно дефект шага A) — снять её как ставит окно: пустым выбором
                typeof(EnergySpectrumView).GetMethod("SetFsaHighlight", Any).Invoke(view, new object[] { null, true });
                Console.WriteLine("  (сброс: подсветку «осиротевшую» пришлось снять отражением)");
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

        /// <summary>Настоящий пересчёт сеанса: его начало и конец доходят до обоих окон сами.</summary>
        static void Recalc()
        {
            FsaAnalysisSession session = doc.FsaSession;
            ResultData rd = doc.ActiveResultData;
            int before = session.RunCount;
            session.Invalidate();
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, make());
            Pump(100);
            WaitIdle(session);
            Pump(600);
            if (session.RunCount == before)
            {
                Console.WriteLine("  ⛔ обвязка: пересчёт не запустился");
                rigFail++;
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

        /// <summary>Сколько пикселей кадра отличает подсветку от её отсутствия (0 — графику всё равно).</summary>
        static int MutedPixels(string hl, bool chain)
        {
            if (hl == null)
            {
                return 0;
            }
            MethodInfo set = typeof(EnergySpectrumView).GetMethod("SetFsaHighlight", Any);
            using (Bitmap lit = Render())
            {
                set.Invoke(view, new object[] { null, false });
                Bitmap none = Render();
                set.Invoke(view, new object[] { hl, chain });
                using (none)
                {
                    int n = 0;
                    for (int y = 0; y < Height; y++)
                    {
                        for (int x = 0; x < Width; x++)
                        {
                            if (lit.GetPixel(x, y).ToArgb() != none.GetPixel(x, y).ToArgb()) n++;
                        }
                    }
                    return n;
                }
            }
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
