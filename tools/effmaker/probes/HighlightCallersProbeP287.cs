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
    /// (П287, 14.10.2026) ПОДСВЕТКА СТРОКИ РЯДА — ОТ НАСТОЯЩИХ ВЫЗЫВАЮЩИХ ДО ПИКСЕЛЕЙ.
    ///
    /// Решение Amber 14.10.2026 вопросником, дословно: «В отчёте — только лента корня:
    /// как до ночи — в отчёте строка ряда зажигает ленту Th-232, остальное гаснет; окно
    /// результата — весь ряд.» Ночная правка П260 зажигала весь ряд по имени корня у
    /// обоих окон: оба толкают одно и то же имя слоя, и отрисовке различать было нечем.
    ///
    /// Проба выбирает строку ТЕМ ЖЕ путём, что человек: в окне отчёта —
    /// <c>FSAReportView.SelectComponent</c> на документе пробы (приём F16), в окне
    /// результата — выбор строки таблицы <c>DCResultView</c> на <c>MainForm</c> с тем
    /// же документом (приём П254); подсветку туда кладут сами окна. Затем рисует
    /// НАСТОЯЩИЙ <c>EnergySpectrumView.ShowFsaOverlay</c> вида того же документа
    /// (отражением, кадр пробы П260) и судит каждый слой стека по маске лент (та же
    /// <c>DrawFsaBand</c>): «горит» — ни один пиксель ленты не сменился против кадра
    /// без выбора, «приглушён» — сменилось больше 90 %. Кадр без выбора снимается тем
    /// же окном (снятием выбора), отпечаток кадра (sha256 пикселей) — для побайтного
    /// сравнения «до/после» на двух сборках.
    ///
    ///   HighlightCallersProbeP287 --spectrum=&lt;xml&gt; --eq=on|off --db=on|off
    ///                             --pick=&lt;имя&gt; [--pick=…] [--via=report|result]…
    ///                             [--out=&lt;каталог снимков&gt;] [--timeout=300]
    ///
    /// Коды: 0 — прошло; 3 — разбор не дошёл; 4 — окно не приняло выбор, который
    /// обязано принять (строки нет — не отказ, а сведение); 2 — ключи.
    /// Сборка «до» (без <c>SetFsaHighlight</c>) читается тем же кодом: признак ряда
    /// печатается «—».
    /// </summary>
    static class HighlightCallersProbeP287
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int Width = 1200, Height = 620;
        static int timeoutS = 300;
        static int bad;
        static string outDir;
        static string tag;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string path = null;
            bool eq = true, db = true;
            var picks = new List<string>();
            var vias = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--eq=", StringComparison.Ordinal)) eq = a.Substring(5) == "on";
                else if (a.StartsWith("--db=", StringComparison.Ordinal)) db = a.Substring(5) == "on";
                else if (a.StartsWith("--pick=", StringComparison.Ordinal)) picks.Add(a.Substring(7));
                else if (a.StartsWith("--via=", StringComparison.Ordinal)) vias.Add(a.Substring(6));
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (path == null || picks.Count == 0)
            {
                Console.Error.WriteLine("нужны --spectrum= и --pick=");
                return 2;
            }
            if (vias.Count == 0) { vias.Add("report"); vias.Add("result"); }
            foreach (string v in vias)
            {
                if (v != "report" && v != "result") { Console.Error.WriteLine("--via= только report|result: " + v); return 2; }
            }
            tag = Path.GetFileNameWithoutExtension(path) + "_" + (eq ? "eqon" : "eqoff");

            Application.EnableVisualStyles();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            DocEnergySpectrum doc = Open(path, nuclides);
            if (doc == null) return 2;
            ResultData rd = doc.ActiveResultData;
            var cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            // Настройки разбора — те же, что у пробы П260 (с конфигурации спектра, состав
            // и равновесие — ключами), и ОДНИ на все три потребителя сеанса документа
            // (график, отчёт, окно результата): иначе окно заказало бы свой пересчёт.
            Func<FsaCalculationOptions> make = () =>
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
            FsaResult result = session.Result;
            if (result == null)
            {
                Console.WriteLine("разбора нет: {0}", session.Status ?? "?");
                return 3;
            }
            Console.WriteLine("SETUP\t{0}: равновесие {1}, состав {2}, χ²/ndf {3}", Path.GetFileName(path),
                              eq ? "ВКЛ" : "ВЫКЛ", db ? "NucBase" : "по подписям",
                              result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture));
            bool newApi = typeof(EnergySpectrumView).GetMethod("SetFsaHighlight", Any) != null;
            Console.WriteLine("SETUP\tсборка: {0}", newApi ? "с SetFsaHighlight (после П287)" : "без SetFsaHighlight (до П287)");

            EnergySpectrumView view = doc.EnergySpectrumView;
            Setup(view, rd, result);

            foreach (string via in vias)
            {
                if (via == "report") ReportWindow(doc, view, picks);
                else ResultWindow(doc, view, picks);
            }

            doc.Dispose();
            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ИТОГ ПРОБЫ: ок" : "ИТОГ ПРОБЫ: отказов " + bad);
            return bad == 0 ? 0 : 4;
        }

        // ==================================================================
        // Окно отчёта: FSAReportView.SelectComponent → PushHighlight
        // ==================================================================

        static void ReportWindow(DocEnergySpectrum doc, EnergySpectrumView view, List<string> picks)
        {
            Console.WriteLine();
            Console.WriteLine("=== ОКНО ОТЧЁТА (FSAReportView на документе пробы) ===");
            using (var report = new FSAReportView(null))
            using (Form host = Host(report, 460, 900))
            {
                report.SetDocument(doc);
                Pump(200);
                foreach (string pick in picks)
                {
                    report.SelectComponent(null);
                    Pump(50);
                    if (HighlightOf(view) != null)
                    {
                        Console.WriteLine("  ⛔ снятие выбора в отчёте не сняло подсветку: «{0}»", HighlightOf(view));
                        bad++;
                        continue;
                    }
                    using (Bitmap none = Render(view))
                    using (Bitmap mask = MaskFrame(view))
                    {
                        bool taken = report.SelectComponent(pick);
                        Pump(50);
                        if (!taken)
                        {
                            Console.WriteLine();
                            Console.WriteLine("ОТЧЁТ «{0}»: строки нет — окно выбор не приняло (сведение)", pick);
                            continue;
                        }
                        using (Bitmap picked = Render(view))
                        {
                            Judge("отчёт", pick, view, none, picked, mask);
                        }
                    }
                }
                report.SelectComponent(null);
                report.SetDocument(null);
                Pump(50);
                host.Hide();
            }
        }

        // ==================================================================
        // Окно результата: выбор строки таблицы DCResultView → PushFsaHighlight
        // ==================================================================

        static void ResultWindow(DocEnergySpectrum doc, EnergySpectrumView view, List<string> picks)
        {
            Console.WriteLine();
            Console.WriteLine("=== ОКНО РЕЗУЛЬТАТА (DCResultView на MainForm с документом пробы) ===");
            var mainForm = new MainForm();
            mainForm.ActiveDocument = doc;
            var results = new DCResultView(mainForm);
            Form host = Host(results, 640, 400);
            try
            {
                results.ShowResult(true);
                WaitIdle(doc.FsaSession);
                Pump(300);
                results.ShowResult(true);
                Pump(100);
                object table = Field(typeof(DCResultView), "tableModel1").GetValue(results);
                IList rows = (IList)table.GetType().GetProperty("Rows").GetValue(table, null);
                object selections = table.GetType().GetProperty("Selections").GetValue(table, null);
                var collection = (MeasurementResultCollection)Field(typeof(DCResultView), "previousCollection").GetValue(results);
                var names = new List<string>();
                var rowOf = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < rows.Count; i++)
                {
                    object rowTag = rows[i].GetType().GetProperty("Tag").GetValue(rows[i], null);
                    if (rowTag is int index && collection != null && index >= 0 && index < collection.ResultList.Count)
                    {
                        MeasurementResult m = collection.ResultList[index];
                        if (m.IsValid && m.Line != null)
                        {
                            names.Add(m.Line.Name);
                            if (!rowOf.ContainsKey(m.Line.Name)) rowOf[m.Line.Name] = i;
                        }
                    }
                }
                Console.WriteLine("  строки окна с образом: [{0}]", string.Join(", ", names));

                foreach (string pick in picks)
                {
                    selections.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(selections, null);
                    Pump(50);
                    if (HighlightOf(view) != null)
                    {
                        Console.WriteLine("  ⛔ снятие выбора в окне результата не сняло подсветку: «{0}»", HighlightOf(view));
                        bad++;
                        continue;
                    }
                    int row;
                    if (!rowOf.TryGetValue(pick, out row))
                    {
                        Console.WriteLine();
                        Console.WriteLine("РЕЗУЛЬТАТ «{0}»: строки с этим образом в окне нет (сведение)", pick);
                        continue;
                    }
                    using (Bitmap none = Render(view))
                    using (Bitmap mask = MaskFrame(view))
                    {
                        selections.GetType().GetMethod("SelectCell", new[] { typeof(int), typeof(int) }).Invoke(selections, new object[] { row, 0 });
                        Pump(50);
                        if (!string.Equals(HighlightOf(view), pick, StringComparison.Ordinal))
                        {
                            Console.WriteLine("  ⛔ строка «{0}» выбрана, а подсветка «{1}»", pick, HighlightOf(view) ?? "нет");
                            bad++;
                            continue;
                        }
                        using (Bitmap picked = Render(view))
                        {
                            Judge("результат", pick, view, none, picked, mask);
                        }
                    }
                }
                selections.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(selections, null);
                Pump(50);
            }
            finally
            {
                host.Hide();
                results.Dispose();
                host.Dispose();
                mainForm.ActiveDocument = null;
                mainForm.Dispose();
            }
        }

        // ==================================================================
        // Суд кадра
        // ==================================================================

        static void Judge(string via, string pick, EnergySpectrumView view, Bitmap none, Bitmap picked, Bitmap mask)
        {
            List<FsaStackLayer> layers = Layers(view);
            string chain = ChainFlagOf(view);
            if (outDir != null)
            {
                Directory.CreateDirectory(outDir);
                picked.Save(Path.Combine(outDir, "p287_" + tag + "_" + (via == "отчёт" ? "report" : "result") + "_" + pick + ".png"), ImageFormat.Png);
            }
            long[] region = new long[layers.Count + 1];
            long[] changed = new long[layers.Count + 1];
            for (int y = 0; y < mask.Height; y++)
            {
                for (int x = 0; x < mask.Width; x++)
                {
                    int id = mask.GetPixel(x, y).R;
                    if (id == 0 || id > layers.Count) continue;
                    region[id]++;
                    if (none.GetPixel(x, y).ToArgb() != picked.GetPixel(x, y).ToArgb()) changed[id]++;
                }
            }
            Console.WriteLine();
            Console.WriteLine("{0} «{1}»: подсветка вида «{2}», весь ряд: {3}", via.ToUpperInvariant(), pick, HighlightOf(view) ?? "нет", chain);
            Console.WriteLine("  {0,-14} {1,-10} {2,8} {3,9} {4,9}  {5}", "слой", "ChainRoot", "доля %", "px", "сменилось", "вид");
            int lit = 0, muted = 0, mixed = 0, empty = 0;
            double litShare = 0, mutedShare = 0;
            long litPx = 0, mutedPx = 0;
            var litNames = new List<string>();
            for (int k = 0; k < layers.Count; k++)
            {
                long r = region[k + 1], c = changed[k + 1];
                string verdict;
                if (r == 0) { verdict = "нет пикселей"; empty++; }
                else if (c == 0) { verdict = "ГОРИТ"; lit++; litShare += layers[k].SharePercent; litPx += r; litNames.Add(layers[k].Name); }
                else if (c * 10 > r * 9) { verdict = "приглушён"; muted++; mutedShare += layers[k].SharePercent; mutedPx += r; }
                else { verdict = "СМЕШАН"; mixed++; }
                Console.WriteLine("  {0,-14} {1,-10} {2,8} {3,9} {4,9}  {5}", layers[k].Name, layers[k].ChainRoot ?? "—",
                                  layers[k].SharePercent.ToString("F2", CultureInfo.InvariantCulture), r, c, verdict);
            }
            Console.WriteLine("  ИТОГ {0} «{1}»: слоёв {2}, горит {3} [{4}] (доля {5} %, {6} px), приглушено {7} (доля {8} %, {9} px), смешанных {10}, без пикселей {11}; кадр без выбора sha {12}, кадр с выбором sha {13}",
                              via, pick, layers.Count, lit, string.Join(", ", litNames), litShare.ToString("F2", CultureInfo.InvariantCulture), litPx,
                              muted, mutedShare.ToString("F2", CultureInfo.InvariantCulture), mutedPx, mixed, empty, Sha(none), Sha(picked));
            if (mixed > 0)
            {
                Console.WriteLine("  ⛔ смешанные слои — кадр судить нельзя");
                bad++;
            }
        }

        static string HighlightOf(EnergySpectrumView view)
        {
            return (string)typeof(EnergySpectrumView).GetProperty("FsaHighlight", Any).GetValue(view, null);
        }

        static string ChainFlagOf(EnergySpectrumView view)
        {
            PropertyInfo p = typeof(EnergySpectrumView).GetProperty("FsaHighlightWholeChain", Any);
            if (p == null) return "— (сборка до П287: связка по ChainRoot всегда)";
            return (bool)p.GetValue(view, null) ? "да" : "нет";
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

        // ==================================================================
        // Кадр (приём П260 / F16): настоящий ShowFsaOverlay, маска лент DrawFsaBand
        // ==================================================================

        static List<FsaStackLayer> Layers(EnergySpectrumView view)
        {
            object presentation = Field(typeof(EnergySpectrumView), "fsaPresentation").GetValue(view);
            return (List<FsaStackLayer>)presentation.GetType().GetProperty("Layers").GetValue(presentation, null);
        }

        static Bitmap Render(EnergySpectrumView view)
        {
            var image = new Bitmap(Width, Height);
            using (Graphics g = Graphics.FromImage(image))
            {
                g.Clear(Color.Black);
                typeof(EnergySpectrumView).GetMethod("ShowFsaOverlay", Any).Invoke(view, new object[] { g });
            }
            return image;
        }

        static Bitmap MaskFrame(EnergySpectrumView view)
        {
            var image = new Bitmap(Width, Height);
            List<FsaStackLayer> layers = Layers(view);
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

        static void Setup(EnergySpectrumView view, ResultData rd, FsaResult result)
        {
            EnergySpectrum spectrum = rd.EnergySpectrum;
            EnergyCalibration calibration = spectrum.EnergyCalibration;
            double toKev = calibration.ChannelToEnergy(spectrum.NumberOfChannels - 1);
            double ceiling = 0.0;
            for (int i = 0; i < result.Model.Length; i++) if (result.Model[i] > ceiling) ceiling = result.Model[i];
            const int left = 1;
            const double pownum = 4.0;
            Set(view, "energySpectrum", spectrum);
            Set(view, "activeResultData", rd);
            if (rd.BackgroundEnergySpectrum != null)
            {
                Set(view, "backgroundEnergySpectrum", rd.BackgroundEnergySpectrum);
                Set(view, "backgroundEnergyCalibration", rd.BackgroundEnergySpectrum.EnergyCalibration);
                Set(view, "backgroundNumberOfChannels", rd.BackgroundEnergySpectrum.NumberOfChannels);
            }
            Set(view, "energyCalibration", calibration);
            Set(view, "numberOfChannels", spectrum.NumberOfChannels);
            Set(view, "backgroundMode", BackgroundMode.ShowFSA);
            Set(view, "horizontalUnit", HorizontalUnit.Energy);
            Set(view, "verticalUnit", VerticalUnit.Counts);
            Set(view, "verticalScaleType", VerticalScaleType.PowerScale);
            Set(view, "pownum", pownum);
            Set(view, "totalMinValuePow", 0.0);
            Set(view, "valueRangePow", Math.Pow(ceiling, 1.0 / pownum));
            Set(view, "totalMinValueLog", 0.0);
            Set(view, "valueRangeLog", Math.Log10(ceiling));
            Set(view, "height", Height);
            Set(view, "width", Width - left);
            Set(view, "left", left);
            Set(view, "scrollX", 0);
            Set(view, "scrollY", 0);
            Set(view, "scrollBaseY", 0.0);
            Set(view, "verticalScale", 1.0);
            Set(view, "horizontalScale", 1.0);
            Set(view, "totalMinValue", 0.0);
            Set(view, "valueRange", ceiling);
            Set(view, "energyViewOffset", 0.0);
            Set(view, "pixelPerEnergy", (Width - left) / toKev);
            Set(view, "dirty", false);
        }

        // ==================================================================
        // Обвязка (образцы F16 / П254)
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
            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            return doc;
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

        static void Set(object target, string name, object value)
        {
            Field(target.GetType(), name).SetValue(target, value);
        }
    }
}
