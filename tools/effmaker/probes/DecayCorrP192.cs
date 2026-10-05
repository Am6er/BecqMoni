using BecquerelMonitor;
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
using WeifenLuo.WinFormsUI.Docking;

namespace DecayCorrP192
{
    /// <summary>
    /// ПРИЁМКА `AMBER148` — поправка на распад к дате отбора (полоса П192, 01.10.2026).
    ///
    ///     decaycorrp192 [--spectrum=&lt;файл корпуса&gt;] [--shots=&lt;каталог кадров&gt;]
    ///
    /// Перенесена из пробы проверки П191 `DecayCorrP191` и расширена. Две части.
    ///
    /// ЗОНЫ — `MeasurementResultManager.Correct` (метод есть в обеих сборках, поэтому
    /// та же проба против сборки ДО правки — положительный контроль: она обязана
    /// показать отклонение). Спектр с `ResultValue` = СРЕДНЕЕ за набор от источника
    /// A(tₛ) = 1, то есть верный ответ `Correct()` — ровно 1. Судится:
    ///   * |Correct() − 1| ≤ 0.001 у I-131 (T = 0.25…8.03 сут), Tc-99m (1…6 ч),
    ///     Cs-137 (100 сут) и при задержке набора после отбора;
    ///   * MDA после / MDA до = значение после / значение до (MDA тем же множителем);
    ///   * вердикт «≥ MDA» поправкой не меняется;
    ///   * λT → 0: при T = 0 — прежняя форма 1/0.5^{Δt/T½} ПОБИТОВО; при T = 1 с у
    ///     Cs-137 — в пределах 1e-9;
    ///   * год: период из импорта базы (`NucBase.HalfLifeYearsFromCell("8.0252(d)")`),
    ///     задержка 10 периодов при T = 0 — ровно 2^10 = 1024 (± 1e-9).
    ///
    /// ВЫДЕЛЕНИЕ (с ключом `--spectrum=`) — НАСТОЯЩАЯ панель выделения
    /// `EnergySpectrumView` на документе из файла корпуса (окно приложения не
    /// показывается, образец — `SelectionPanelProbeG10`). Период подписанной линии
    /// ставится равным длительности набора (T = T½), и судится:
    ///   * без окна результатов в режиме поправки — беккерели те же, строка момента
    ///     «mean over acquisition»;
    ///   * окно результатов в режиме «поправка на полураспад» — беккерели ×2·ln2
    ///     (= λT/(1−e^{−λT}) при λT = ln2), σ и верхний предел тем же множителем,
    ///     строка момента «at sampling …»;
    ///   * отбор за период до начала — ×4·ln2;
    ///   * окно результатов скрыто — снова ×1;
    ///   * у линии нет периода — ×1 и «no T½».
    /// Сборка до правки строки момента не имеет — плечо выделения ей отказывает.
    ///
    /// Коды: 0 — всё сошлось; 1 — есть расхождения (перечень выше итога); 2 — ключи.
    /// </summary>
    public static class Program
    {
        static int bad;
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly DateTime Ts = new DateTime(2026, 9, 30, 12, 0, 0);

        [STAThread]
        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            string spectrum = null, shots = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--shots=", StringComparison.Ordinal)) shots = a.Substring(8);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            Console.WriteLine("приложение: {0}", typeof(MeasurementResultManager).Assembly.Location);
            bool fixedBuild = typeof(MeasurementResultManager).GetMethod("DecayToSamplingFactor") != null;
            Console.WriteLine("сборка: {0}", fixedBuild ? "С ПРАВКОЙ AMBER148 (есть DecayToSamplingFactor)" : "БЕЗ ПРАВКИ (плечо «до» — ждём отклонения)");

            Zones();
            if (spectrum != null)
            {
                Selection(spectrum, shots);
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("выделение: ключ --spectrum= не задан — плечо не мерено");
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : bad + " НЕ СОШЛОСЬ");
            return bad == 0 ? 0 : 1;
        }

        // ==================================================================
        // зоны
        // ==================================================================

        static void Zones()
        {
            Console.WriteLine();
            Console.WriteLine("=== ЗОНЫ: MeasurementResultManager.Correct; верный ответ — 1 ===");
            Console.WriteLine("{0,-9} {1,9} {2,9} {3,8} {4,10} {5,11} {6,9} {7,12} {8,7}",
                              "нуклид", "T½, сут", "T, сут", "Δ, сут", "среднее", "Correct()", "ошибка", "MDA×/знач×", "вердикт");
            var cases = new List<Tuple<string, double, double, double>>
            {
                Tuple.Create("I-131", 8.0252, 0.25, 0.0),
                Tuple.Create("I-131", 8.0252, 1.0, 0.0),
                Tuple.Create("I-131", 8.0252, 3.0, 0.0),
                Tuple.Create("I-131", 8.0252, 8.0252, 0.0),
                Tuple.Create("I-131", 8.0252, 1.0, 2.0),
                Tuple.Create("I-131", 8.0252, 8.0252, 16.0504),
                Tuple.Create("Tc-99m", 0.25083, 1.0 / 24.0, 0.0),
                Tuple.Create("Tc-99m", 0.25083, 3.0 / 24.0, 0.0),
                Tuple.Create("Tc-99m", 0.25083, 6.0 / 24.0, 0.0),
                Tuple.Create("Cs-137", 10983.0, 100.0, 0.0),
            };
            foreach (var c in cases)
            {
                double halfDays = c.Item2, T = c.Item3, delay = c.Item4;
                double lambda = Math.Log(2.0) / halfDays;
                double lt = lambda * T;
                double measured = Math.Exp(-lambda * delay) * (1.0 - Math.Exp(-lt)) / lt;
                double mda = 2.0 * measured;   // «не обнаружено» до поправки
                MeasurementResult r = Run(halfDays / 365.0, Ts, Ts.AddDays(delay), Ts.AddDays(delay + T), measured, mda);
                double v = r.ResultValue;
                double mdaRatio = r.MDA / mda, valueRatio = v / measured;
                bool okValue = Math.Abs(v - 1.0) <= 1e-3;
                bool okMda = Math.Abs(mdaRatio / valueRatio - 1.0) <= 1e-12;
                bool okVerdict = v < r.MDA;
                Console.WriteLine("{0,-9} {1,9:F4} {2,9:F4} {3,8:F3} {4,10:F4} {5,11:F6} {6,8:+0.000;-0.000} % {7,12:F6} {8,7}  {9}",
                                  c.Item1, halfDays, T, delay, measured, v, (v - 1.0) * 100.0, mdaRatio / valueRatio,
                                  okVerdict ? "< MDA" : "≥ MDA",
                                  okValue && okMda && okVerdict ? "ok" : "НЕТ"
                                  + (okValue ? "" : " [значение]") + (okMda ? "" : " [MDA без множителя]")
                                  + (okVerdict ? "" : " [вердикт сменился]"));
                if (!(okValue && okMda && okVerdict)) bad++;
            }

            Console.WriteLine();
            Console.WriteLine("--- λT → 0 ---");
            // T = 0: прежняя форма ПОБИТОВО (задержка 10 сут, I-131)
            {
                double hy = 8.0252 / 365.0;
                MeasurementResult r = Run(hy, Ts, Ts.AddDays(10.0), Ts.AddDays(10.0), 1.0, 2.0);
                double old = 1.0 / Math.Pow(0.5, (Ts.AddDays(10.0) - Ts).TotalDays / 365.0 / hy);
                bool ok = r.ResultValue == old;
                Console.WriteLine("  T = 0, Δ = 10 сут, I-131: Correct() {0:R} против прежней формы {1:R} — {2}",
                                  r.ResultValue, old, ok ? "ok (побитово)" : "НЕТ");
                if (!ok) bad++;
            }
            // T = 1 с, Cs-137: в пределах 1e-9 от прежней формы
            {
                double hy = 10983.0 / 365.0;
                DateTime start = Ts.AddDays(30.0), end = start.AddSeconds(1.0);
                MeasurementResult r = Run(hy, Ts, start, end, 1.0, 2.0);
                double old = 1.0 / Math.Pow(0.5, (end - Ts).TotalDays / 365.0 / hy);
                double rel = r.ResultValue / old - 1.0;
                bool ok = Math.Abs(rel) <= 1e-9;
                Console.WriteLine("  T = 1 с, Δ = 30 сут, Cs-137: Correct() {0:R}, прежняя {1:R}, отн. {2:E2} — {3}",
                                  r.ResultValue, old, rel, ok ? "ok (≤ 1e-9)" : "НЕТ");
                if (!ok) bad++;
            }

            Console.WriteLine();
            Console.WriteLine("--- год периода: импорт базы пишет годы по 365 сут ---");
            {
                double hy = BecquerelMonitor.NucBase.NucBase.HalfLifeYearsFromCell("8.0252(d)");
                double delay = 10.0 * 8.0252;
                MeasurementResult r = Run(hy, Ts, Ts.AddDays(delay), Ts.AddDays(delay), 1.0, 2.0);
                double tropical = 1.0 / Math.Pow(0.5, delay / 365.2422 / hy);
                bool ok = Math.Abs(r.ResultValue - 1024.0) <= 1e-9;
                Console.WriteLine("  HalfLifeYearsFromCell(\"8.0252(d)\") = {0:R} лет; к 8.0252/365 отн. {1:E1} (год = 365 сут)", hy, hy / (8.0252 / 365.0) - 1.0);
                Console.WriteLine("  Δ = 10 периодов, T = 0: Correct() {0:F6} (ждём 1024) — {1}; при делении на 365.2422 было бы {2:F3} ({3:+0.00;-0.00} %)",
                                  r.ResultValue, ok ? "ok" : "НЕТ", tropical, (tropical / 1024.0 - 1.0) * 100.0);
                if (!ok) bad++;
            }
        }

        static MeasurementResult Run(double halfLifeYears, DateTime sampling, DateTime start, DateTime end, double value, double mda)
        {
            var roi = new ROIDefinitionData { HalfLife = halfLifeYears, Enabled = true, Name = "probe" };
            var rd = new ResultData();
            rd.SampleInfo = new SampleInfoData();
            rd.SampleInfo.Time = sampling;
            rd.StartTime = start;
            rd.EndTime = end;
            rd.EnergySpectrum = new EnergySpectrum();
            var coll = new MeasurementResultCollection { ResultData = rd, ROIConfig = new ROIConfigData() };
            coll.ResultList.Add(new MeasurementResult(roi, value, 0.1 * value, mda));
            return new MeasurementResultManager().Correct(coll).ResultList[0];
        }

        // ==================================================================
        // выделение
        // ==================================================================

        static void Selection(string path, string shots)
        {
            Console.WriteLine();
            Console.WriteLine("=== ВЫДЕЛЕНИЕ: панель EnergySpectrumView, {0} ===", Path.GetFileName(path));
            if (shots != null) Directory.CreateDirectory(shots);
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();
            Application.EnableVisualStyles();

            MainForm mainForm = new MainForm();
            DCPeakDetectionView panel = new DCPeakDetectionView(mainForm);
            Form panelHost = Host(panel, 520, 620);
            DocEnergySpectrum doc = OpenDocument(path);
            Form docHost = Host(doc, 1200, 620);
            EnergySpectrumView view = doc.EnergySpectrumView;
            view.PeakMode = PeakMode.Visible;
            view.FitHorizontalScale();
            mainForm.ActiveDocument = doc;
            panel.ShowPeakDetectionResult();
            Pump(panel);

            ResultData rd = doc.ActiveResultData;
            Peak picked = PickPeak(rd.DetectedPeaks);
            if (picked == null)
            {
                Console.WriteLine("  НЕТ  подписанного пика нет — мерить нечего");
                bad++;
                return;
            }
            int half = Math.Max(2, (int)Math.Round(picked.FWHM));
            view.SelectionStart = Math.Max(0, picked.Channel - half);
            view.SelectionEnd = Math.Min(rd.EnergySpectrum.NumberOfChannels - 1, picked.Channel + half);
            var curve = new EfficiencyConfigData("проба П192");
            curve.Curve = new List<ROIEfficiencyData>
            {
                new ROIEfficiencyData { Energy = 50.0,   Efficiency = 2.0e-2, ErrorPercent = 2.0 },
                new ROIEfficiencyData { Energy = 662.0,  Efficiency = 1.0e-3, ErrorPercent = 5.0 },
                new ROIEfficiencyData { Energy = 2000.0, Efficiency = 1.0e-4, ErrorPercent = 8.0 },
            };
            rd.Efficiency = curve;
            rd.BackgroundEnergySpectrum = Scaled(rd.EnergySpectrum, 0.1, rd.EnergySpectrum.MeasurementTime);

            double T = (rd.EndTime - rd.StartTime).TotalDays;
            double savedHalfLife = picked.Nuclide.HalfLife;
            picked.Nuclide.HalfLife = T / 365.0;       // T = T½, λT = ln2
            rd.SampleInfo.Time = rd.StartTime;
            Console.WriteLine("  пик {0:F2} кэВ, подпись «{1}»; набор {2:F6} сут = T½ линии (поставлено пробой); отбор = начало набора",
                              picked.Energy, picked.Nuclide.Name, T);

            double ln2 = Math.Log(2.0);
            object a0 = Measure(view, "без окна результатов", shots, "p192-off");
            double A0 = (double)Get(a0, "Activity"), E0 = (double)Get(a0, "ActivityError"), U0 = (double)Get(a0, "ActivityUpperLimit");
            Expect(a0, A0, E0, U0, 1.0, "mean over acquisition", "без окна результатов");

            DCResultView results = new DCResultView(mainForm);
            DockPanel dock = (DockPanel)Field(typeof(MainForm), "dockPanel1").GetValue(mainForm);
            results.Show(dock);
            results.ResultCorrection = ResultCorrection.HalfLifeCorrection;   // переключатель зовёт перерисовку панели сам
            object a1 = Measure(view, "окно результатов: поправка, отбор = начало", shots, "p192-on", refresh: false);
            Expect(a1, A0, E0, U0, 2.0 * ln2, "at sampling ", "поправка, отбор = начало (×2·ln2)");

            rd.SampleInfo.Time = rd.StartTime.AddDays(-T);
            // AddDays округляет до миллисекунды — ожидание от ФАКТИЧЕСКОЙ задержки:
            // 2·ln2 · 2^(Δ/T½), Δ ≈ T½ (≈ 4·ln2)
            double delayFactor = 2.0 * ln2 * Math.Pow(2.0, (rd.StartTime - rd.SampleInfo.Time).TotalDays / T);
            object a2 = Measure(view, "окно результатов: поправка, отбор за T½ до начала", shots, "p192-on-delay");
            Expect(a2, A0, E0, U0, delayFactor, "at sampling ", "поправка, отбор за T½ (≈ ×4·ln2)");

            picked.Nuclide.HalfLife = 0.0;
            object a3 = Measure(view, "окно результатов: поправка, у линии нет T½", shots, "p192-noT");
            Expect(a3, A0, E0, U0, 1.0, "mean over acquisition: no T½", "поправка, нет T½ (×1)");
            picked.Nuclide.HalfLife = T / 365.0;

            results.Hide();
            object a4 = Measure(view, "окно результатов СКРЫТО", shots, "p192-hidden");
            Expect(a4, A0, E0, U0, 1.0, "mean over acquisition", "окно скрыто (×1)");

            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("ru");
            results.Show(dock);
            object a5 = Measure(view, "ru: окно результатов снова показано", shots, "p192-ru");
            Expect(a5, A0, E0, U0, delayFactor, "на дату отбора ", "ru: поправка, отбор за T½ (≈ ×4·ln2)");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en");

            picked.Nuclide.HalfLife = savedHalfLife;
            foreach (Form form in new Form[] { results, docHost, panelHost, doc, panel, mainForm })
            {
                form.Dispose();
            }
        }

        static object Measure(EnergySpectrumView view, string what, string shots, string tag, bool refresh = true)
        {
            if (refresh) view.RefreshSelectionOverlay();
            Bitmap frame = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(frame, new Rectangle(0, 0, view.Width, view.Height));   // строит аналитику и рисует панель
            if (shots != null) frame.Save(Path.Combine(shots, tag + ".png"), ImageFormat.Png);
            frame.Dispose();
            object an = Field(typeof(EnergySpectrumView), "selectionAnalytics").GetValue(view);
            Console.WriteLine();
            Console.WriteLine("  --- {0} ---", what);
            return an;
        }

        static void Expect(object an, double A0, double E0, double U0, double factor, string momentPrefix, string what)
        {
            if (an == null)
            {
                Console.WriteLine("  НЕТ  аналитики выделения нет");
                bad++;
                return;
            }
            double A = (double)Get(an, "Activity"), E = (double)Get(an, "ActivityError"), U = (double)Get(an, "ActivityUpperLimit");
            PropertyInfo mp = an.GetType().GetProperty("ActivityMoment", Any);
            PropertyInfo fp = an.GetType().GetProperty("ActivityDecayFactor", Any);
            string moment = mp != null ? (string)mp.GetValue(an, null) : null;
            double f = fp != null ? (double)fp.GetValue(an, null) : double.NaN;
            Console.WriteLine("  A = {0:G10} Бк (A/A₀ = {1:F9}), σ/σ₀ = {2:F9}, верх/верх₀ = {3:F9}; множитель в аналитике {4:F9}; строка момента «{5}»",
                              A, A / A0, E / E0, (U0 > 0 ? U / U0 : double.NaN), f, moment ?? "(нет)");
            bool ok = A > 0.0 && Math.Abs(A / A0 / factor - 1.0) <= 1e-9 && Math.Abs(E / E0 / factor - 1.0) <= 1e-9
                      && (!(U0 > 0.0) || Math.Abs(U / U0 / factor - 1.0) <= 1e-9)
                      && Math.Abs(f / factor - 1.0) <= 1e-9
                      && moment != null && moment.StartsWith(momentPrefix, StringComparison.Ordinal);
            Console.WriteLine("  {0} {1}: ждём ×{2:F9} и «{3}…»", ok ? "ok  " : "НЕТ ", what, factor, momentPrefix);
            if (!ok) bad++;
        }

        // ---- обвязка (образец SelectionPanelProbeG10) ----

        static Peak PickPeak(IList<Peak> peaks)
        {
            double threshold = Convert.ToDouble(typeof(EnergySpectrumView)
                .GetField("MinimumActivityYieldPercent", BindingFlags.Public | BindingFlags.Static)
                .GetRawConstantValue(), CultureInfo.InvariantCulture);
            Peak best = null;
            foreach (Peak p in peaks)
            {
                if (p.Nuclide == null || !(p.Nuclide.Intencity >= threshold) || NuclideDefinition.IsElementXrayName(p.Nuclide.Name)) continue;
                if (!(p.Energy > 50.0 && p.Energy < 2000.0)) continue;
                if (best == null || p.SNR > best.SNR) best = p;
            }
            return best;
        }

        static EnergySpectrum Scaled(EnergySpectrum fg, double factor, double time)
        {
            var s = new EnergySpectrum();
            s.NumberOfChannels = fg.NumberOfChannels;
            s.Spectrum = new int[fg.NumberOfChannels];
            long total = 0;
            for (int i = 0; i < fg.NumberOfChannels; i++)
            {
                s.Spectrum[i] = (int)Math.Round(fg.Spectrum[i] * factor);
                total += s.Spectrum[i];
            }
            s.EnergyCalibration = fg.EnergyCalibration;
            s.MeasurementTime = time;
            s.TotalPulseCount = total;
            s.ValidPulseCount = total;
            return s;
        }

        static void Pump(DCPeakDetectionView panel)
        {
            FieldInfo busy = Field(typeof(DCPeakDetectionView), "isProcessing");
            FieldInfo pending = Field(typeof(DCPeakDetectionView), "refreshPending");
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            do
            {
                Application.DoEvents();
                Thread.Sleep(10);
                if (DateTime.UtcNow > deadline) throw new TimeoutException("поиск пиков не завершился за 60 с");
            }
            while ((bool)busy.GetValue(panel) || (bool)pending.GetValue(panel));
            Application.DoEvents();
        }

        static DocEnergySpectrum OpenDocument(string path)
        {
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
            Console.WriteLine("  {0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }
                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }
            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            return doc;
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

        static object Get(object an, string prop)
        {
            PropertyInfo p = an.GetType().GetProperty(prop, Any);
            if (p == null) throw new InvalidOperationException("нет свойства SelectionAnalytics." + prop);
            return p.GetValue(an, null);
        }
    }
}
