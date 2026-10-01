using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using XPTable.Models;

namespace FsaInputCheckProbeP195
{
    /// <summary>
    /// П195 (01.10.2026): приёмка ЗАВЕРЕНИЯ окна отчёта FSA по трём строкам —
    /// тем же путём, каким его видит человек: сеанс разбора
    /// (<see cref="FsaAnalysisSession"/>) → окно <see cref="FSAReportView"/> →
    /// строки блока «Качество разбора».
    ///
    ///   * `AMBER159` — сверка плотности пробы (вес/объём карточки) с плотностью
    ///     сцены: положительный контроль — W/V = 1.3 г/см³ против сцены
    ///     маринелли; отрицательные — плотность сцены и нулевые W, V.
    ///   * `AMBER157` — кривая обрезана выше 1250 кэВ, разбор без матрицы:
    ///     строка «кривая … вне: K-40 1460.8»; контроль — полная кривая.
    ///   * `AMBER156` (в) — разбор без матрицы: строка-предупреждение; контроль —
    ///     матричный разбор того же спектра.
    ///
    ///     FsaInputCheckProbeP195 --spectrum=G1S16_K40_Mar.xml
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0; иначе 1; мерить нечем — 2.
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            string path = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (path == null || !File.Exists(path)) { Console.Error.WriteLine("нужен --spectrum=<файл.xml>"); return 2; }

            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Application.EnableVisualStyles();

            MainForm mainForm = new MainForm();
            try
            {
                return Run(mainForm, path, nuclides);
            }
            finally
            {
                mainForm.Dispose();
            }
        }

        static int Run(MainForm mainForm, string path, NuclideDefinitionManager nuclides)
        {
            ResultData rd = Load(path, nuclides);
            if (rd.Efficiency == null || !rd.Efficiency.HasGeometry)
            {
                Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: у спектра нет кривой с геометрией");
                return 2;
            }

            double scene = rd.Efficiency.Geometry.Source.Density;
            Console.WriteLine("спектр {0}: кривая «{1}», сцена {2}, ρ источника сцены {3} г/см³, пиков {4}",
                              Path.GetFileName(path), rd.Efficiency.Name, rd.Efficiency.Geometry.SourceType,
                              scene.ToString("0.000", CultureInfo.InvariantCulture), rd.DetectedPeaks.Count);

            // ---------------- AMBER159: статическая сверка ----------------
            Console.WriteLine();
            Console.WriteLine("=== AMBER159: плотность пробы против сцены (FsaAnalysisSession.CheckSampleDensity) ===");
            var cases = new[]
            {
                new { Name = "W = 0, V = 0 (карточка пуста)", W = 0.0, V = 0.0, Row = false, Warn = false },
                new { Name = "W = 1.0 кг, V = 0 (объёма нет)", W = 1.0, V = 0.0, Row = false, Warn = false },
                new { Name = "W = 1 кг, V = 1 л (умолчание карточки)", W = 1.0, V = 1.0, Row = false, Warn = false },
                new { Name = "W = 1.01 кг, V = 1 л (вписано)", W = 1.01, V = 1.0, Row = true, Warn = true },
                new { Name = "W/V = плотности сцены", W = scene * 1.0, V = 1.0, Row = true, Warn = false },
                new { Name = "W/V = 1.05 × сцены (+5 %)", W = scene * 1.05, V = 1.0, Row = true, Warn = false },
                new { Name = "W = 1.3 кг, V = 1.0 л (грунт)", W = 1.3, V = 1.0, Row = true, Warn = true },
                new { Name = "W = 0.45 кг, V = 1.0 л (рыхлая)", W = 0.45, V = 1.0, Row = true, Warn = true }
            };
            foreach (var c in cases)
            {
                rd.SampleInfo.Weight = c.W;
                rd.SampleInfo.Volume = c.V;
                FsaSampleDensityCheck check = FSAReportView.DensityOf(rd);
                Console.WriteLine("  {0,-34} → {1}", c.Name,
                                  check == null ? "строки нет"
                                                : FSAReportView.DensityCaption(check) + " | " + (check.Warning ? "ПРЕДУПРЕЖДЕНИЕ" : "в пределах"));
                Same(c.Name + ": строка есть", c.Row, check != null);
                if (check != null) Same(c.Name + ": предупреждение", c.Warn, check.Warning);
            }

            // ---------------- окно отчёта: три сцены ----------------
            var session = new FsaAnalysisSession();
            using (var report = new FSAReportView(mainForm))
            {
                // (1) с матрицей, грунт 1.3: строка плотности красная; нет строки «без матрицы»
                rd.SampleInfo.Weight = 1.3; rd.SampleInfo.Volume = 1.0;
                List<string[]> rows = Window(report, session, rd, "с матрицей, W/V 1.3");
                Same("(1) матричный разбор", true, session.Result != null && session.Result.ResponseMatrixUsed);
                Same("(1) строка плотности в окне, красная", true, Has(rows, "1.30", true));
                Same("(1) строки «без матрицы» нет", false, Has(rows, Own("FSAReport_NoMatrixWarnValue"), true));
                Same("(1) строки «вне кривой» нет", false, Has(rows, Own("FSAReport_CurveOutsideValue"), true));

                // (2) без матрицы, полная кривая, карточка пуста: предупреждение «без матрицы»,
                //     строки плотности и «вне кривой» нет
                rd.SampleInfo.Weight = 0.0; rd.SampleInfo.Volume = 0.0;
                bool useMatrix = rd.Efficiency.UseResponseMatrix;
                rd.Efficiency.UseResponseMatrix = false;
                session.Reset();
                rows = Window(report, session, rd, "без матрицы, кривая полная, W = V = 0");
                Same("(2) разбор без матрицы", true, session.Result != null && !session.Result.ResponseMatrixUsed);
                Same("(2) предупреждение «без матрицы» в окне", true, Has(rows, Own("FSAReport_NoMatrixWarnValue"), true));
                Same("(2) строки «вне кривой» нет", false, Has(rows, Own("FSAReport_CurveOutsideValue"), true));
                Same("(2) строки плотности нет", false, Has(rows, "g/cm", false) || Has(rows, "г/см", false));
                Same("(2) привязка без матрицы: опоры есть", true, session.Result != null && session.Result.ScaleAnchorsUsed > 0);

                // (3) без матрицы, кривая до 1250 кэВ: K-40 1460.8 исключён и назван
                int before = rd.Efficiency.Curve.Count;
                var kept = new List<ROIEfficiencyData>(rd.Efficiency.Curve);
                rd.Efficiency.Curve.RemoveAll(p => p.Energy > 1250.0);
                Console.WriteLine("  кривая обрезана выше 1250 кэВ: точек {0} → {1}", before, rd.Efficiency.Curve.Count);
                session.Reset();
                rows = Window(report, session, rd, "без матрицы, кривая до 1250 кэВ");
                bool named = false;
                foreach (string[] r in rows) if (r[0].Contains("K-40 1460") && r[1] == Own("FSAReport_CurveOutsideValue")) named = true;
                Same("(3) строка «вне кривой» называет K-40 1460", true, named);
                Same("(3) результат: исключённая линия K-40 у 1460 кэВ", true,
                     session.Result != null && session.Result.EfficiencyOutOfRangeLines.Exists(l => Math.Abs(l.EnergyKev - 1460.8) < 1.5));
                if (session.Result != null)
                    foreach (FsaComponentResult c in session.Result.Components)
                    {
                        int top = 0;
                        for (int i = 1; c.Curve != null && i < c.Curve.Length; i++) if (c.Curve[i] > c.Curve[top]) top = i;
                        Console.WriteLine("  компонент {0,-14} {1,-9} A={2:E4} z={4:F2} пик.отсч={3:F0}, вершина ленты {5:F1} кэВ", c.Name, c.Kind, c.CountRate * session.Result.LiveTime, c.PeakCounts, c.Z,
                                          rd.EnergySpectrum.EnergyCalibration.ChannelToEnergy(top));
                    }
                Same("(3) компонента K-40 в разборе нет (единственная линия вне кривой)", true,
                     session.Result != null && !session.Result.Components.Exists(c => c.Name.StartsWith("K-40", StringComparison.Ordinal)));

                rd.Efficiency.Curve.Clear();
                rd.Efficiency.Curve.AddRange(kept);
                rd.Efficiency.UseResponseMatrix = useMatrix;
                report.SetProbeSource(null, null);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Счёт сеанса до покоя, окно — заполнить и вернуть строки блока (подпись, значение, красное?).</summary>
        static List<string[]> Window(FSAReportView report, FsaAnalysisSession session, ResultData rd, string title)
        {
            session.EnsureUpToDate(rd, true);
            for (int i = 0; i < 1200 && session.IsRunning; i++)
            {
                Thread.Sleep(50);
                Application.DoEvents();
            }

            report.SetProbeSource(session, rd);
            report.RefreshReport();
            Console.WriteLine();
            Console.WriteLine("--- окно: {0} (состояние: {1}) ---", title, session.Status ?? "—");
            var rows = new List<string[]>();
            TableModel model = report.ReportTable.TableModel;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                var tag = model.Rows[i].Tag as FsaReportRow;
                if (tag == null || tag.Kind != FsaReportRowKind.Quality) continue;
                string name = model.Rows[i].Cells[1].Text, value = model.Rows[i].Cells[2].Text;
                if (name.Length == 0 && value.Length == 0) continue;
                bool red = tag.Warning;
                rows.Add(new[] { name, value, red ? "red" : "" });
                Console.WriteLine("  «{0}» | {1}{2}", name, value, red ? "  [внимание]" : "");
            }

            return rows;
        }

        static bool Has(List<string[]> rows, string text, bool redOnly)
        {
            foreach (string[] r in rows)
            {
                if ((r[0].Contains(text) || r[1].Contains(text)) && (!redOnly || r[2] == "red")) return true;
            }

            return false;
        }

        static readonly System.ComponentModel.ComponentResourceManager ViewResources =
            new System.ComponentModel.ComponentResourceManager(typeof(FSAReportView));

        static string Own(string key)
        {
            return ViewResources.GetString(key) ?? key;
        }

        static void Same<T>(string what, T expected, T actual)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            if (!ok) bad++;
            Console.WriteLine("  {0} {1}: ждали {2}, есть {3}", ok ? "OK " : "⛔ ", what, expected, actual);
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
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

            Console.WriteLine("прибор: {0}", ProbeDeviceConfig.Attach(rd));
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }

            if (rd.SampleInfo == null) rd.SampleInfo = new SampleInfoData();
            rd.DetectedPeaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None, nuclides.ActiveSet, nuclides.NuclideDefinitions);
            return rd;
        }
    }
}
