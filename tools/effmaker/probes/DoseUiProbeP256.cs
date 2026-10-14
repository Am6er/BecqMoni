using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace DoseUiProbeP256
{
    /// <summary>
    /// (П256, 14.10.2026) То, что человек видит ЧИСЛОМ вне FSA, — без окна
    /// (окно BecqMoni агенту не дают; запрос доступа к экрану отклонён).
    /// Проба кладётся В КАТАЛОГ КОПИИ ПРИЛОЖЕНИЯ рядом с копией конфигурации
    /// Amber, чтобы приложение читало ЕЁ приборы, кривые и склад матриц
    /// (её живой конфиг не трогается: копия — `D:\BqMoni_Claude\&lt;полоса&gt;\app\config`).
    ///
    ///   Д. Строка дозы, как её собирает `MainForm.ShowDoseRate` («подпись +
    ///      DoseRate.ToString()»), на спектрах Amber, в культуре ru-RU (система
    ///      Amber) с языком ru и en; покрытие, «≈», причина без матрицы. Рядом —
    ///      погрешность кривой эффективности в мере дозы (для вопроса Amber:
    ///      строка несёт только пуассоновскую погрешность).
    ///   М. Метка «!» вкладки Efficiency (`AMBER218`) у ЕЁ кривых: какие
    ///      помечены и почему (кривая / матрица).
    ///   Э. Экспорт CSV конструктора кривой: файл в культуре ru-RU — разделитель
    ///      полей «,», дробная часть «.», числа читаются инвариантом обратно в
    ///      те же; шапка у сохранённой кривой и у свежего расчёта — «saved» /
    ///      «NOT saved» по содержимому (правка П256).
    ///
    ///   DoseUiProbeP256.exe --spectrum=&lt;файл&gt; [--spectrum=…] --csv=&lt;каталог&gt;
    ///
    /// Код: 0 — контроли Э сошлись; 1 — нет; 2 — оснастка.
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var spectra = new List<string>();
            string csvDir = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectra.Add(a.Substring(11));
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csvDir = a.Substring(6);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (csvDir == null) { Console.Error.WriteLine("нужен --csv=<каталог>"); return 2; }
            Directory.CreateDirectory(csvDir);

            // Культура системы Amber — ru-RU (десятичная запятая): так виден любой
            // забытый инвариант.
            CultureInfo ru = CultureInfo.GetCultureInfo("ru-RU");
            Thread.CurrentThread.CurrentCulture = ru;
            GlobalConfigManager gcm = GlobalConfigManager.GetInstance();
            DeviceConfigManager dcm = DeviceConfigManager.GetInstance();
            Console.WriteLine("сборка: {0}", typeof(DoseRate).Assembly.Location);
            Console.WriteLine("склад матриц: {0}", ResponseMatrixStore.Directory);
            Console.WriteLine("культура счёта: {0}, десятичный знак «{1}»", CultureInfo.CurrentCulture.Name,
                CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
            Console.WriteLine();

            Dose(gcm, spectra);
            StaleMarks(dcm);
            Csv(dcm, csvDir);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ИТОГ: СОШЛОСЬ" : "ИТОГ: РАЗОШЛОСЬ, отказов " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        // ==================================================================
        // Д. Строка дозы
        // ==================================================================

        static void Dose(GlobalConfigManager gcm, List<string> spectra)
        {
            Console.WriteLine("=== Д. Строка дозы (как MainForm.ShowDoseRate) ===");
            foreach (string path in spectra)
            {
                ResultData rd = Load(path);
                if (rd == null) { bad++; continue; }
                EfficiencyConfigData eff = rd.Efficiency;
                Console.WriteLine("  {0}", Path.GetFileName(path));
                Console.WriteLine("    кривая «{0}», геометрия {1}, сцена {2}; живое {3} с, полное {4} с",
                    eff == null ? "-" : eff.Name, eff != null && eff.HasGeometry ? "есть" : "нет",
                    eff != null && eff.HasGeometry ? eff.Geometry.Scene.ToString() : "-",
                    Inv(rd.EnergySpectrum.LiveTime), Inv(rd.EnergySpectrum.MeasurementTime));
                foreach (string ui in new[] { "ru-RU", "en-US" })
                {
                    Resources.Culture = CultureInfo.GetCultureInfo(ui);
                    Thread.CurrentThread.CurrentUICulture = Resources.Culture;
                    var manager = new DoseRateManager(gcm);
                    DoseRate d = manager.Calculate(rd);
                    if (d == null) { Console.WriteLine("    [{0}] строки нет (кривая не выбрана)", ui); continue; }
                    string text = d.ToString();
                    string label = Resources.DoseRate ?? "";
                    if (string.IsNullOrEmpty(d.Refusal) || !text.StartsWith(label.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        text = label + " " + text;
                    }

                    Console.WriteLine("    [{0}] «{1}»", ui, text);
                    if (ui == "ru-RU" && string.IsNullOrEmpty(d.Refusal))
                    {
                        Console.WriteLine("      Ḣ = {0} мкЗв/ч, σ(Пуассон, ×уровень) = {1} ({2} %); покрытие {3}; «≈» {4}; сцена с источником {5}; ослабление в пробе {6}",
                            Inv(d.Rate), Inv(d.Error), Inv(d.Epsilon), Inv(d.Coverage), d.Approximate, d.SourceScene, Inv(d.SampleTransmission));
                        Console.WriteLine("      матрица: {0}", string.IsNullOrEmpty(manager.LastMatrixNote) ? "есть" : "нет — " + manager.LastMatrixNote);
                        CurveError(eff, d);
                    }
                }
            }

            Resources.Culture = null;
            Console.WriteLine();
        }

        /// <summary>
        /// Погрешность кривой эффективности в мере дозы: δ(E) кривой на энергии
        /// каждого диапазона, взвешенная его дозой, — коррелированно (Σ D·δ) и
        /// независимо (√Σ(D·δ)²). Только печать — для вопроса Amber.
        /// </summary>
        static void CurveError(EfficiencyConfigData eff, DoseRate d)
        {
            string refusal;
            FsaEfficiency curve = FsaEfficiency.FromConfig(eff, out refusal);
            if (curve == null) { Console.WriteLine("      δ кривой: кривой нет ({0})", refusal); return; }
            double corr = 0.0, indep = 0.0, covered = 0.0;
            foreach (DoseRateRange r in d.Ranges)
            {
                if (r.Skipped || !(r.DoseRate > 0.0)) continue;
                double eps, err;
                if (!curve.TryEval(r.RepresentativeKev, out eps, out err)) continue;
                corr += r.DoseRate * err / 100.0;
                indep += Math.Pow(r.DoseRate * err / 100.0, 2);
                covered += r.DoseRate;
            }

            Console.WriteLine("      δ кривой в мере дозы: коррелированно {0} %, независимо {1} % (по {2} % дозы)",
                Inv(100.0 * corr / d.Rate), Inv(100.0 * Math.Sqrt(indep) / d.Rate), Inv(100.0 * covered / d.Rate));
        }

        // ==================================================================
        // М. Метка «!» (AMBER218)
        // ==================================================================

        static void StaleMarks(DeviceConfigManager dcm)
        {
            Console.WriteLine("=== М. Метка «!» вкладки Efficiency у кривых конфигурации ===");
            Type form = typeof(DeviceConfigForm);
            MethodInfo curveStale = form.GetMethod("CurveStale", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo matrixStale = form.GetMethod("MatrixStale", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo generation = form.GetMethod("GenerationNotes", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo scene = form.GetMethod("CurveSceneNotes", BindingFlags.NonPublic | BindingFlags.Static);
            if (curveStale == null || matrixStale == null || generation == null || scene == null)
            {
                Console.WriteLine("  ОСНАСТКА: нет методов метки в сборке");
                bad++;
                return;
            }

            foreach (DeviceConfigInfo device in dcm.DeviceConfigList)
            {
                if (device.EfficiencyConfigs == null || device.EfficiencyConfigs.Count == 0) continue;
                Console.WriteLine("  прибор «{0}»", device.Name);
                foreach (EfficiencyConfigData c in device.EfficiencyConfigs)
                {
                    bool cs = (bool)curveStale.Invoke(null, new object[] { c });
                    bool ms = (bool)matrixStale.Invoke(null, new object[] { c });
                    MatrixRefusal refusal;
                    int format;
                    ResponseMatrix m = c.HasGeometry ? ResponseMatrixStore.Load(c.Guid, ResponseMatrixStore.EditingSource(c.Guid), out refusal, out format)
                                                     : null;
                    string matrix = !c.HasGeometry ? "без геометрии"
                        : m == null ? "матрицы нет"
                        : string.Format(CultureInfo.InvariantCulture, "phys={0}, годна {1}",
                            ResponseMatrix.PhysicsFromStamp(m.Stamp), m.IsValidFor(c.Geometry));
                    var notes = new List<string>();
                    notes.AddRange((List<string>)generation.Invoke(null, new object[] {
                        c.ComputeStamp, 0, ResponseMatrix.PhysicsVersion, EfficiencyCalculation.PeakWindowExpected(c.Geometry) }));
                    notes.AddRange((List<string>)scene.Invoke(null, new object[] { c }));
                    Console.WriteLine("    {0}{1,-34} кривая phys={2}; {3}{4}",
                        cs || ms ? "! " : "  ", c.Name, ResponseMatrix.PhysicsFromStamp(c.ComputeStamp), matrix,
                        cs || ms ? "  — " + (cs ? "кривая" : "") + (cs && ms ? " и " : "") + (ms ? "матрица" : "")
                                   + (notes.Count > 0 ? ": " + string.Join(" | ", notes.ToArray()) : "")
                                 : "");
                }
            }

            Console.WriteLine();
        }

        // ==================================================================
        // Э. Экспорт CSV конструктора кривой
        // ==================================================================

        static void Csv(DeviceConfigManager dcm, string dir)
        {
            Console.WriteLine("=== Э. Экспорт CSV конструктора кривой (культура {0}) ===", CultureInfo.CurrentCulture.Name);
            DeviceConfigInfo device = null;
            EfficiencyConfigData config = null;
            foreach (DeviceConfigInfo d in dcm.DeviceConfigList)
            {
                foreach (EfficiencyConfigData c in d.EfficiencyConfigs ?? new List<EfficiencyConfigData>())
                {
                    if (c.HasCurve && c.HasGeometry && !string.IsNullOrEmpty(c.ComputeStamp)
                        && (config == null || c.Curve.Count > config.Curve.Count))
                    {
                        device = d;
                        config = c;
                    }
                }
            }

            if (config == null) { Console.WriteLine("  ОСНАСТКА: нет посчитанной кривой"); bad++; return; }
            Console.WriteLine("  прибор «{0}», кривая «{1}», точек {2}", device.Name, config.Name, config.Curve.Count);

            // Работают на КЛОНЕ: форма прибора тоже правит клон.
            EfficiencyConfigData clone = config.Copy();
            Type tf = typeof(EfficiencyMakerForm);
            MethodInfo header = tf.GetMethod("ExportHeader", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo last = tf.GetField("lastResult", BindingFlags.NonPublic | BindingFlags.Instance);
            using (var form = new EfficiencyMakerForm())
            {
                form.BindTo(device, clone);

                // 1) сохранённая кривая конфигурации, без нового счёта (d42eac54)
                var savedHeader = (List<string>)header.Invoke(form, new object[] { false, clone.Curve });
                string savedPath = Path.Combine(dir, "saved.csv");
                EfficiencyCurveIo.ExportCsv(savedPath, clone.Curve, savedHeader);
                CheckFile(savedPath, clone.Curve, "source: saved curve of the configuration");

                // 2) свежий расчёт, лежащий в конфигурации точка в точку (как после «Сохранить»)
                var fresh = new EfficiencyFitResult { ComputeStamp = clone.ComputeStamp };
                foreach (ROIEfficiencyData p in clone.Curve) fresh.Curve.Add(p.Clone());
                last.SetValue(form, fresh);
                var h2 = (List<string>)header.Invoke(form, new object[] { true, fresh.Curve });
                string p2 = Path.Combine(dir, "fresh_saved.csv");
                EfficiencyCurveIo.ExportCsv(p2, fresh.Curve, h2);
                CheckFile(p2, fresh.Curve, "source: computed in the curve designer, saved into the configuration");

                // 3) свежий расчёт, отличный от сохранённого одной точкой — «NOT saved»
                fresh.Curve[fresh.Curve.Count / 2].Efficiency *= 1.01;
                var h3 = (List<string>)header.Invoke(form, new object[] { true, fresh.Curve });
                string p3 = Path.Combine(dir, "fresh_unsaved.csv");
                EfficiencyCurveIo.ExportCsv(p3, fresh.Curve, h3);
                CheckFile(p3, fresh.Curve, "source: computed in the curve designer, NOT saved into the configuration");
            }

            Console.WriteLine();
        }

        static void CheckFile(string path, List<ROIEfficiencyData> curve, string expectSource)
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            var head = lines.Where(l => l.StartsWith("# ", StringComparison.Ordinal)).ToList();
            var data = lines.Where(l => !l.StartsWith("#", StringComparison.Ordinal)).ToList();
            bool sourceOk = head.Any(l => l == "# " + expectSource);
            int badRows = 0, commaDecimal = 0;
            double worst = 0.0;
            if (data.Count == 0 || data[0] != "E_keV,eps,err_pct") badRows++;
            for (int i = 1; i < data.Count; i++)
            {
                string[] f = data[i].Split(',');
                if (f.Length != 3) { badRows++; continue; }
                double e, eps, err;
                if (!double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out e)
                    || !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out eps)
                    || !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out err))
                {
                    badRows++;
                    continue;
                }

                if (i - 1 < curve.Count)
                {
                    ROIEfficiencyData p = curve[i - 1];
                    worst = Math.Max(worst, Math.Max(Rel(e, p.Energy), Math.Max(Rel(eps, p.Efficiency), Rel(err, p.ErrorPercent) * 0.01)));
                }
            }

            foreach (string l in data) if (l.Contains(";")) commaDecimal++;
            bool countOk = data.Count - 1 == curve.Count;
            bool ok = sourceOk && badRows == 0 && countOk && worst < 1e-6 && commaDecimal == 0;
            Console.WriteLine("  {0}: строк данных {1} (точек {2}), шапка {3}, «source» {4}, негодных строк {5}, худшее отн. расхождение {6}; {7}",
                Path.GetFileName(path), data.Count - 1, curve.Count, head.Count, sourceOk ? "верна" : "НЕ ТА", badRows,
                worst.ToString("G3", CultureInfo.InvariantCulture), ok ? "ok" : "!!");
            Console.WriteLine("    первые строки: {0} | {1} | {2}", lines.Length > 0 ? lines[0] : "", data.Count > 1 ? data[1] : "",
                data.Count > 2 ? data[data.Count - 1] : "");
            if (!ok)
            {
                foreach (string l in head) Console.WriteLine("    " + l);
                bad++;
            }
        }

        static double Rel(double a, double b)
        {
            return b == 0.0 ? Math.Abs(a) : Math.Abs(a / b - 1.0);
        }

        static ResultData Load(string path)
        {
            if (!File.Exists(path)) { Console.WriteLine("  ОСНАСТКА: нет файла " + path); return null; }
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            var pcal = rd.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (pcal != null) pcal.CheckCalibration(rd.EnergySpectrum.NumberOfChannels);
            ProbeDeviceConfig.Attach(rd);
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                rd.FwhmCalibration = cfg.FwhmCalibration ?? FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
            }

            rd.FileEfficiency = rd.Efficiency;
            return rd;
        }

        static string Inv(double v)
        {
            return v.ToString("G6", CultureInfo.InvariantCulture);
        }
    }
}
