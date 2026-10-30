using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// КРИВИЗНА ТРАКТА ПРИ КАЛИБРОВКЕ ПО ЭНЕРГИИ (`AMBER270`, 29–30.10.2026). Amber, консоль,
    /// дословно: «При калибровке по энергии так же проводить калибровку по кривизне тракта.»
    /// Вопросником: «Спектра, прибор — умолчание (Рекомендую)».
    ///
    /// Решения Amber вопросником 30.10.2026, дословно: набор линий — «Только с линиями до
    /// ≥ 2 МэВ (Рекомендую)», канал линии — «Центр масс пика (Рекомендую)», вещество без
    /// геометрии — «Поле прибора (Рекомендую)».
    ///
    ///   tractcurvaturefitprobea270 --mode=map [--curve=NaI:Tl]
    ///       ⛔ ТА ЖЕ ФОРМУЛА, ЧТО РАЗБОР: настоящий <c>FsaAnalyzer</c> (отражением: прямая
    ///       карта, <c>AdcScaleOf</c>, <c>LightToChannel</c>) кладёт фотопики заданных линий в
    ///       каналы при заданной κ и нуле света z₀; <c>DCFwhmCalibrationView.FitTractCurvature</c>
    ///       обязан вернуть ту же κ — дробными каналами до 1e-9 1/МэВ, целыми (прежний путь
    ///       строк таблицы) — в пределах округления. Положительный контроль: подгонка той же
    ///       формулой, но с другой опорной энергией кривой света, κ НЕ возвращает. Набор с
    ///       верхом ниже 2 МэВ (линии Eu-152) — отказ «нужна линия от 2000 кэВ».
    ///   ... --mode=corpus --spectra=&lt;файл&gt;|E1,E2,…[;&lt;файл&gt;|…] [--shift=Δκ] [--kappa0=κ]
    ///       настоящие спектры и НАСТОЯЩАЯ «Выполнить калибровку» вкладки (<c>RunCalibration</c>
    ///       на оболочке главного окна): строки — пики линий выделением ±1.5 FWHM (канал —
    ///       целый центр масс, как строка таблицы), κ спектра до и после, подпись итога,
    ///       число линий центром масс. Для сравнения — κ целыми каналами строк (прежний путь)
    ///       тем же ядром и тем же полиномом. Верх набора ≥ 2000 кэВ — κ обязана лечь в спектр
    ///       центрами масс всех линий; ниже — отказ словами, κ спектра побитово прежняя.
    ///       Несколько файлов — строки каждого в СВОЁМ документе, затем настоящее «Собрать
    ///       всё» в первый: центр масс строки обязан мериться по её спектру (⛔ контроль — те
    ///       же строки, мерянные по спектру первого документа, дают другое).
    ///       --shift=Δκ — ⛔ положительный контроль на данных: каждый спектр ПЕРЕСЧИТАН с
    ///       добавочной кривизной тракта Δκ (каналы пересобраны, верхняя линия на месте), и
    ///       подогнанная κ обязана сдвинуться на Δκ.
    ///       --kappa0=κ — спектр до расчёта несёт свою κ (как файл с `&lt;TractCurvature&gt;`):
    ///       отказ обязан оставить её побитово (без ключа «до» — κ прибора).
    /// Запускать из каталога, где лежит приложение (рядом `config\`, `matdb.sqlite`).
    /// Коды: 0 — сошлось; 1 — не сошлось; 2 — ключи; 3 — сбой.
    /// </summary>
    static class TractCurvatureFitProbeA270
    {
        static int bad;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = Inv;
            var opt = new Dictionary<string, string>();
            foreach (string a in args)
            {
                int eq = a.IndexOf('=');
                if (!a.StartsWith("--", StringComparison.Ordinal) || eq < 0)
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
                opt[a.Substring(2, eq - 2)] = a.Substring(eq + 1);
            }
            string mode;
            if (!opt.TryGetValue("mode", out mode))
            {
                Console.Error.WriteLine("нужен --mode=map|corpus");
                return 2;
            }
            try
            {
                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                NuclideDefinitionManager.GetInstance();
                Console.WriteLine("сборка: " + typeof(ResultData).Assembly.Location);
                switch (mode)
                {
                    case "map":
                        string curve;
                        return Map(opt.TryGetValue("curve", out curve) ? curve : "NaI:Tl");
                    case "corpus":
                        string shift, kappa0;
                        double dk = opt.TryGetValue("shift", out shift) ? double.Parse(shift, Inv) : 0.0;
                        double k0 = opt.TryGetValue("kappa0", out kappa0) ? double.Parse(kappa0, Inv) : double.NaN;
                        return Corpus(Need(opt, "spectra"), dk, k0);
                    default:
                        Console.Error.WriteLine("неизвестный режим: " + mode);
                        return 2;
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("СБОЙ: " + Inner(ex));
                return 3;
            }
        }

        static string Need(Dictionary<string, string> opt, string key)
        {
            string v;
            if (!opt.TryGetValue(key, out v) || string.IsNullOrEmpty(v))
            {
                throw new ArgumentException("нужен --" + key + "=");
            }
            return v;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  map — сверка с картой разбора
        // ══════════════════════════════════════════════════════════════════════

        static int Map(string curve)
        {
            FsaDatabaseFailures.Begin();
            bool known = LightKnown(curve);
            List<string> failures = FsaDatabaseFailures.End();
            Console.WriteLine("кривая света: " + curve + (known ? "" : " — НЕТ В БАЗЕ") + (failures.Count > 0 ? "; отказы чтения базы: " + string.Join(" | ", failures) : ""));
            if (!known)
            {
                return 1;
            }
            string scintillator = curve; // имя кривой — и есть вещество для CurveFor
            double[] lines = { 121.78, 238.63, 344.28, 583.19, 727.33, 911.20, 1460.82, 1764.49, 2614.51 };
            var cases = new[]
            {
                new { N = 1024, A = -4.0, B = 3.05, Z = 0.0 },
                new { N = 8192, A = -15.0, B = 0.41, Z = 0.0 },
                new { N = 1024, A = 2.0, B = 2.95, Z = 6.0 },
            };
            double[] kappas = { 0.0, 0.0025, -0.03, 0.01 };
            double worstExact = 0.0, worstRounded = 0.0;
            foreach (var c in cases)
            {
                var cal = new PolynomialEnergyCalibration { PolynomialOrder = 1, Coefficients = new[] { c.A, c.B, 0.0, 0.0, 0.0 } };
                foreach (double kappa in kappas)
                {
                    double[] ch = AnalyzerChannels(curve, cal, c.N, c.Z, kappa, lines);
                    var w = lines.Select(_ => 1.0).ToList();
                    // полином шкалы — по тем же точкам (как вкладка: степень min(4, N − 1))
                    var pts = new List<CalibrationPoint>();
                    for (int i = 0; i < lines.Length; i++)
                    {
                        pts.Add(new CalibrationPoint((int)Math.Round(ch[i]), (decimal)lines[i], 0));
                    }
                    int used;
                    double[] poly = SolveEnergy(pts, Math.Min(4, pts.Count - 1), c.N, out used);
                    var fitted = new PolynomialEnergyCalibration { PolynomialOrder = poly.Length - 1, Coefficients = poly };
                    // дробные каналы и шкала самого разбора — сверка формулы
                    var exact = DCFwhmCalibrationView.FitTractCurvature(ch.ToList(), lines.ToList(), w, cal, scintillator);
                    // целые каналы и подогнанный полином — прежний путь строк таблицы
                    var rounded = DCFwhmCalibrationView.FitTractCurvature(pts.Select(p => (double)p.Channel).ToList(), lines.ToList(), w, fitted, scintillator);
                    double de = exact.Ok ? Math.Abs(exact.Kappa - kappa) : double.NaN;
                    double dr = rounded.Ok ? Math.Abs(rounded.Kappa - kappa) : double.NaN;
                    worstExact = Math.Max(worstExact, double.IsNaN(de) ? double.PositiveInfinity : de);
                    worstRounded = Math.Max(worstRounded, double.IsNaN(dr) ? double.PositiveInfinity : dr);
                    Console.WriteLine("  {0} кан., E = {1} + {2}·c, z₀ = {3}: κ задана {4,8} → дробные {5} (z₀ {6}), целые {7} (промах линии ≤ {8} кан.)",
                                      c.N, c.A.ToString("R", Inv), c.B.ToString("R", Inv), c.Z.ToString("R", Inv), kappa.ToString("0.0000", Inv),
                                      exact.Ok ? exact.Kappa.ToString("0.0000000000", Inv) : "ОТКАЗ: " + exact.Refusal,
                                      exact.Ok ? exact.ZeroLightKev.ToString("0.000", Inv) : "-",
                                      rounded.Ok ? rounded.Kappa.ToString("0.00000", Inv) : "ОТКАЗ: " + rounded.Refusal,
                                      rounded.Ok ? rounded.WorstChannels.ToString("0.00", Inv) : "-");
                }
            }
            Check("дробные каналы: κ разбора возвращена до 1e-9 1/МэВ (наибольший промах " + worstExact.ToString("0.0E0", Inv) + ")", worstExact < 1e-9);
            Check("целые каналы: κ возвращена в пределах округления канала (наибольший промах " + worstRounded.ToString("0.00000", Inv) + " 1/МэВ)", worstRounded < 0.003);

            // ⛔ положительный контроль формулы: опорная энергия кривой света — не та, что у разбора
            {
                var cal = new PolynomialEnergyCalibration { PolynomialOrder = 1, Coefficients = new[] { -4.0, 3.05, 0.0, 0.0, 0.0 } };
                double[] ch = AnalyzerChannels(curve, cal, 1024, 0.0, 0.0025, lines);
                // свет без кривой (пропорционален энергии) и чужая кривая: кривизна света
                // уходит в κ — подгонка обязана это показать
                double kFlat = FitWithCurve(ch, lines, cal, null);
                double kOther = FitWithCurve(ch, lines, cal, "CsI:Tl");
                Console.WriteLine("  контроль: свет пропорционален энергии → κ {0}; кривая CsI:Tl вместо NaI:Tl → κ {1} (задана 0.0025)",
                                  kFlat.ToString("0.00000", Inv), kOther.ToString("0.00000", Inv));
                Check("контроль: без кривой света κ разбора НЕ возвращается", Math.Abs(kFlat - 0.0025) > 2e-4);
                Check("контроль: с чужой кривой света κ разбора НЕ возвращается", Math.Abs(kOther - 0.0025) > 2e-4);
                var none = DCFwhmCalibrationView.FitTractCurvature(ch.ToList(), lines.ToList(), null, cal, "");
                Check("без вещества кристалла κ не подгоняется и причина названа: «" + none.Refusal + "»", !none.Ok && !string.IsNullOrEmpty(none.Refusal));
                var cdte = DCFwhmCalibrationView.FitTractCurvature(ch.ToList(), lines.ToList(), null, cal, "CdTe");
                Check("вещество без фотонной кривой: отказ с причиной «" + cdte.Refusal + "»", !cdte.Ok && !string.IsNullOrEmpty(cdte.Refusal));
                var two = DCFwhmCalibrationView.FitTractCurvature(ch.Take(2).ToList(), lines.Take(2).ToList(), null, cal, scintillator);
                Check("две линии: отказ с причиной «" + two.Refusal + "»", !two.Ok && !string.IsNullOrEmpty(two.Refusal));
            }

            // ⛔ решение Amber 30.10.2026 «Только с линиями до ≥ 2 МэВ»: набор Eu-152 (верх
            // 1408 кэВ) — отказ словами; тот же набор с линией 2614.51 — κ разбора возвращена
            {
                var cal = new PolynomialEnergyCalibration { PolynomialOrder = 1, Coefficients = new[] { -4.0, 3.05, 0.0, 0.0, 0.0 } };
                double[] eu = { 121.78, 244.70, 344.28, 778.90, 964.08, 1408.01 };
                double[] chEu = AnalyzerChannels(curve, cal, 1024, 0.0, 0.0025, eu);
                var low = DCFwhmCalibrationView.FitTractCurvature(chEu.ToList(), eu.ToList(), null, cal, scintillator);
                Console.WriteLine("  набор Eu-152 (верх 1408.01 кэВ): {0}", low.Ok ? "κ " + low.Kappa.ToString("0.00000", Inv) : "ОТКАЗ: " + low.Refusal);
                Check("верх набора ниже " + DCFwhmCalibrationView.TractCurvatureTopLineKev.ToString("0", Inv) + " кэВ: отказ, причина называет порог и верхнюю линию",
                      !low.Ok && low.Refusal != null && low.Refusal.Contains("2000") && low.Refusal.Contains("1408.01"));
                double[] euTh = eu.Concat(new[] { 2614.51 }).ToArray();
                double[] chEuTh = AnalyzerChannels(curve, cal, 1024, 0.0, 0.0025, euTh);
                var high = DCFwhmCalibrationView.FitTractCurvature(chEuTh.ToList(), euTh.ToList(), null, cal, scintillator);
                Console.WriteLine("  тот же набор + 2614.51 кэВ: {0}", high.Ok ? "κ " + high.Kappa.ToString("0.0000000000", Inv) : "ОТКАЗ: " + high.Refusal);
                Check("с линией 2614.51 кэВ κ разбора возвращена до 1e-9", high.Ok && Math.Abs(high.Kappa - 0.0025) < 1e-9);
                double[] edge = { 238.63, 583.19, 1173.23, 2000.0 };
                double[] chEdge = AnalyzerChannels(curve, cal, 1024, 0.0, 0.0025, edge);
                var atEdge = DCFwhmCalibrationView.FitTractCurvature(chEdge.ToList(), edge.ToList(), null, cal, scintillator);
                Check("верхняя линия ровно 2000 кэВ — подгоняется (граница включена)", atEdge.Ok);
            }
            Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Каналы фотопиков линий по прямой карте настоящего разбора (отражением).</summary>
        static double[] AnalyzerChannels(string curve, EnergyCalibration cal, int n, double zeroLight, double kappa, double[] lines)
        {
            var an = new FsaAnalyzer();
            an.AdcTractCurvature = kappa;
            SetField(an, "adcChannels", n);
            SetField(an, "lightCurveName", curve);
            FieldInfo form = Field(an.GetType(), "lightForm");
            SetField(an, "lightForm", Enum.Parse(form.FieldType, "Line"));
            SetField(an, "adcZeroKev", zeroLight);
            var top = new FsaComponent("A270", FsaComponentKind.Single);
            foreach (double e in lines)
            {
                top.Lines.Add(new FsaLine("A270", e, 1.0));
            }
            var library = new List<FsaComponent> { top };
            double scale = (double)Call(an, "AdcScaleOf", library, cal);
            if (!(scale > 0.0))
            {
                throw new InvalidOperationException("AdcScaleOf отказал — прямая карта не включилась");
            }
            SetField(an, "adcScale", scale);
            double[] ch = new double[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                double s = (double)Call(an, "CurveShiftKev", lines[i]);
                ch[i] = (double)Call(an, "LightToChannel", cal, lines[i] + s, n);
            }
            return ch;
        }

        /// <summary>Та же подгонка по свету, но с другой кривой света (null — свет пропорционален энергии): контроль формулы.</summary>
        static double FitWithCurve(double[] ch, double[] lines, EnergyCalibration cal, string curve)
        {
            // квадрат c(x) и κ = 1000·b/(a·s₁) — копия формулы с подменённой кривой
            double reference = new FsaAnalyzer().AnchorLightReferenceKev;
            double[] x = lines.Select(e => curve == null ? e : e + LightShift(curve, e, reference)).ToArray();
            int n = x.Length;
            double mid = x.Average();
            double[,] m = new double[3, 4];
            for (int i = 0; i < n; i++)
            {
                double t = x[i] - mid;
                double[] b = { 1, t, t * t };
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++) m[r, c] += b[r] * b[c];
                    m[r, 3] += b[r] * ch[i];
                }
            }
            double[] p = Gauss(m);
            double edge = -0.5;
            double disc = p[1] * p[1] - 4 * p[2] * (p[0] - edge);
            double z1 = (-p[1] + Math.Sqrt(disc)) / (2 * p[2]), z2 = (-p[1] - Math.Sqrt(disc)) / (2 * p[2]);
            double lowT = x.Min() - mid;
            double zt = Math.Abs(z1 - lowT) < Math.Abs(z2 - lowT) ? z1 : z2;
            double slope = p[1] + 2 * p[2] * zt;
            double e0 = cal.ChannelToEnergy(0) - 0.5 * (cal.ChannelToEnergy(1) - cal.ChannelToEnergy(0));
            int top = Array.IndexOf(lines, lines.Max());
            double scale = (lines[top] - e0) / (x[top] - (zt + mid));
            return 1000 * p[2] / (slope * scale);
        }

        static double[] Gauss(double[,] m)
        {
            for (int col = 0; col < 3; col++)
            {
                int piv = col;
                for (int r = col + 1; r < 3; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
                for (int k = 0; k < 4; k++) { double t = m[col, k]; m[col, k] = m[piv, k]; m[piv, k] = t; }
                for (int r = 0; r < 3; r++)
                {
                    if (r == col) continue;
                    double f = m[r, col] / m[col, col];
                    for (int k = col; k < 4; k++) m[r, k] -= f * m[col, k];
                }
            }
            return new[] { m[0, 3] / m[0, 0], m[1, 3] / m[1, 1], m[2, 3] / m[2, 2] };
        }

        // ══════════════════════════════════════════════════════════════════════
        //  corpus — настоящие спектры
        // ══════════════════════════════════════════════════════════════════════

        sealed class Pick
        {
            public double Line, Centroid, Fwhm;
            public int Count;
        }

        sealed class Source
        {
            public string Name, Copy;
            public double[] Lines;
            public DocEnergySpectrum Doc;
            public List<CalibrationPeak> Rows;
        }

        static int Corpus(string spec, double shift, double kappa0)
        {
            var sources = new List<Source>();
            foreach (string part in spec.Split(';').Where(p => p.Length > 0))
            {
                int bar = part.LastIndexOf('|');
                var s = new Source
                {
                    Name = Path.GetFileNameWithoutExtension(part.Substring(0, bar)),
                    Lines = part.Substring(bar + 1).Split(',').Select(t => double.Parse(t, Inv)).ToArray(),
                    Copy = Path.Combine(Path.GetTempPath(), "a270_" + Guid.NewGuid().ToString("N") + ".xml")
                };
                File.Copy(part.Substring(0, bar), s.Copy, true);
                s.Doc = DocumentManager.GetInstance().OpenDocument(s.Copy);
                ResultData rd = s.Doc.ActiveResultData;
                if (shift != 0.0)
                {
                    Bend(rd, shift, s.Lines.Max());
                }
                // строки — как выделение пика: канал строки — целый центр масс, FWHM — каналы
                s.Rows = new List<CalibrationPeak>();
                foreach (Pick p in Picks(rd, s.Lines))
                {
                    int ch = (int)Math.Round(p.Centroid);
                    if (s.Rows.Any(r => r.Channel == ch))
                    {
                        continue;
                    }
                    s.Rows.Add(new CalibrationPeak { Channel = ch, Energy = p.Line, FWHM = p.Fwhm, EnergyLine = true, Count = p.Count });
                    Console.WriteLine("  {0}: линия {1} кэВ — центр масс {2}, строка: канал {3}, FWHM {4}", s.Name, p.Line.ToString("0.###", Inv),
                                      p.Centroid.ToString("0.000", Inv), ch, p.Fwhm.ToString("0.0", Inv));
                }
                rd.CalibrationLines.Clear();
                rd.CalibrationLines.AddRange(s.Rows);
                sources.Add(s);
            }
            Source first = sources[0];
            ResultData active = first.Doc.ActiveResultData;
            MainForm form = Shell(first.Doc);
            System.Windows.Forms.Timer closer = new System.Windows.Forms.Timer { Interval = 150 };
            closer.Tick += (o, e) =>
            {
                foreach (System.Windows.Forms.Form f in System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().ToList())
                {
                    if (f.Modal) f.Close();
                }
            };
            closer.Start();
            try
            {
                DCEnergyCalibrationView view = new DCEnergyCalibrationView(form);
                EnergyCalibration original = active.EnergySpectrum.EnergyCalibration.Clone();
                Call(view, "SetEnergyCalibration", original, original);
                Call(view, "UpdateFwhmCalibration", true);
                if (sources.Count > 1)
                {
                    // настоящее «Собрать всё»: строки всех документов — копиями в таблицу первого
                    Call(view, "GetAllPeaksButton_Click", null, EventArgs.Empty);
                    Console.WriteLine("«Собрать всё»: строк в таблице {0}", active.CalibrationLines.Count);
                }
                List<CalibrationPeak> rows = active.CalibrationLines;
                if (!double.IsNaN(kappa0))
                {
                    // спектр уже несёт свою κ (как файл с `<TractCurvature>`): отказ обязан её не тронуть
                    active.TractCurvature = kappa0;
                }
                double before = active.TractCurvature;
                bool ownBefore = active.HasOwnTractCurvature;
                string material = DCFwhmCalibrationView.ScintillatorOf(active);
                bool done = (bool)Call(view, "RunCalibration");
                var fit = (DCFwhmCalibrationView.TractCurvatureFit)Field(view, "lastTractFit");
                double after = active.TractCurvature;
                string status = ((System.Windows.Forms.ToolStripStatusLabel)Field(form, "toolStripStatusLabel1")).Text;
                string note = ((System.Windows.Forms.Label)Field(view, "tractCurvatureSourceLabel")).Text;
                var cal = (PolynomialEnergyCalibration)active.EnergySpectrum.EnergyCalibration;
                Console.WriteLine("вещество: «{0}»; прибор «{1}», κ прибора {2} 1/МэВ; κ спектра до {3} ({4}); линий {5}; полином степени {6}",
                                  material, active.DeviceConfig != null ? active.DeviceConfig.Name : "",
                                  active.DeviceConfig != null ? active.DeviceConfig.TractCurvature.ToString("R", Inv) : "-",
                                  before.ToString("R", Inv), ownBefore ? "своя" : "прибора", rows.Count(r => r.EnergyLine), cal.PolynomialOrder);
                Console.WriteLine("«Выполнить калибровку»: {0}", done ? "принята" : "ОТКАЗ");
                Console.WriteLine("  строка состояния: " + status);
                Console.WriteLine("  подпись под полем κ: " + note);
                Console.WriteLine("κ (центры масс, как приложение): " + Describe(fit));

                // прежний путь — целые каналы строк, тем же ядром и тем же полиномом
                var lines = rows.Where(r => r.EnergyLine).GroupBy(r => r.Channel).Select(g => g.First()).OrderBy(r => r.Channel).ToList();
                var integer = DCFwhmCalibrationView.FitTractCurvature(lines.Select(r => (double)r.Channel).ToList(), lines.Select(r => r.Energy).ToList(),
                                                                       null, cal, material);
                Console.WriteLine("κ (целые каналы строк, прежний путь): " + Describe(integer));
                Console.WriteLine("KAPPA\t{0}\t{1}\t{2}\t{3}", fit != null && fit.Ok ? fit.Kappa.ToString("R", Inv) : "NaN",
                                  integer.Ok ? integer.Kappa.ToString("R", Inv) : "NaN", shift.ToString("R", Inv), after.ToString("R", Inv));

                double top = first.Lines.Concat(sources.Skip(1).SelectMany(s => s.Lines)).Max();
                Check("«Выполнить калибровку» принята (шкала энергии)", done);
                if (top >= DCFwhmCalibrationView.TractCurvatureTopLineKev)
                {
                    Check("верх набора " + top.ToString("0.##", Inv) + " кэВ ≥ 2000: κ подогнана", fit != null && fit.Ok);
                    Check("κ спектра = подогнанной (" + after.ToString("R", Inv) + ")", fit != null && fit.Ok && after.Equals(fit.Kappa) && active.HasOwnTractCurvature);
                    Check("все линии — центром масс пика (" + (fit != null ? fit.CentroidLines + " из " + fit.Lines : "-") + ")",
                          fit != null && fit.Ok && fit.CentroidLines == fit.Lines && fit.Lines == lines.Count);
                    Check("строка состояния говорит κ", fit != null && status.Contains(fit.Kappa.ToString("0.00000", Inv)));
                }
                else
                {
                    Check("верх набора " + top.ToString("0.##", Inv) + " кэВ < 2000: κ НЕ подогнана", fit != null && !fit.Ok);
                    Check("причина называет порог 2000 кэВ и верхнюю линию " + top.ToString("0.##", Inv),
                          fit != null && fit.Refusal != null && fit.Refusal.Contains("2000") && fit.Refusal.Contains(top.ToString("0.##", Inv)));
                    Check("κ спектра побитово прежняя (" + after.ToString("R", Inv) + "), признак своей κ прежний",
                          BitConverter.DoubleToInt64Bits(after) == BitConverter.DoubleToInt64Bits(before) && active.HasOwnTractCurvature == ownBefore);
                    Check("причина — в строке состояния и под полем", fit != null && fit.Refusal != null && status.Contains(fit.Refusal) && note == fit.Refusal);
                }
                if (sources.Count > 1)
                {
                    // строки других документов несут центр масс СВОЕГО спектра; ⛔ контроль —
                    // те же строки без него мерятся по спектру первого документа
                    int foreign = 0, measured = 0;
                    foreach (Source s in sources.Skip(1))
                    {
                        foreach (CalibrationPeak r in rows.Where(r => s.Rows.Any(o => o.Channel == r.Channel && o.Energy == r.Energy)))
                        {
                            foreign++;
                            if (r.SpectrumCentroidChannel == r.Channel && !double.IsNaN(r.SpectrumCentroid))
                            {
                                measured++;
                            }
                        }
                    }
                    Check("строки других документов несут центр масс своего спектра: " + measured + " из " + foreign, foreign > 0 && measured == foreign);
                    var right = DCFwhmCalibrationView.FitTractCurvature(rows, active.EnergySpectrum, cal, material, false);
                    // ⛔ контроль: центр масс ВСЕХ строк по спектру первого документа, без сверки
                    // «строка — его» — то, от чего защищают память строки и сверка отсчётов
                    var naive = lines.Select(r =>
                    {
                        double c = DCFwhmCalibrationView.LineCentroid(active.EnergySpectrum, r.Channel, r.FWHM);
                        return double.IsNaN(c) ? r.Channel : c;
                    }).ToList();
                    var wrong = DCFwhmCalibrationView.FitTractCurvature(naive, lines.Select(r => r.Energy).ToList(), null, cal, material);
                    wrong.CentroidLines = lines.Count(r => !double.IsNaN(DCFwhmCalibrationView.LineCentroid(active.EnergySpectrum, r.Channel, r.FWHM)));
                    Console.WriteLine("  контроль: центры масс всех строк по спектру первого документа — {0}", Describe(wrong));
                    Console.WriteLine("            по своим спектрам (как приложение)                — {0}", Describe(right));
                    Check("контроль: мерянные по чужому спектру строки дают другую κ (" + (wrong.Ok ? wrong.Kappa.ToString("0.00000", Inv) : "-") + " против "
                          + (right.Ok ? right.Kappa.ToString("0.00000", Inv) : "-") + ")", wrong.Ok && right.Ok && Math.Abs(wrong.Kappa - right.Kappa) > 1e-4);
                    // файл сохранён и открыт заново: центра масс в памяти строк нет — свои строки
                    // мерятся по спектру, чужие (отсчёты канала не те) идут целым каналом
                    List<CalibrationPeak> reread = CalibrationPeak.ClonePeaks(rows);
                    foreach (CalibrationPeak r in reread)
                    {
                        r.SpectrumCentroid = double.NaN;
                        r.SpectrumCentroidChannel = -1;
                    }
                    var reopened = DCFwhmCalibrationView.FitTractCurvature(reread, active.EnergySpectrum, cal, material, false);
                    int own = lines.Count - foreign;
                    Console.WriteLine("  после открытия файла (центров в памяти нет): {0}", Describe(reopened));
                    Check("после открытия файла центром масс — ровно свои строки (" + reopened.CentroidLines + " из " + own + "), чужие — целым каналом",
                          reopened.Ok && reopened.CentroidLines == own);
                    Check("и итог об этом говорит: «" + Status(reopened) + "»",
                          Status(reopened).Contains(foreign.ToString(Inv)));
                }
            }
            finally
            {
                closer.Stop();
                foreach (Source s in sources)
                {
                    DocumentManager.GetInstance().DocumentList.Remove(s.Doc);
                    try { File.Delete(s.Copy); } catch (IOException) { }
                }
            }
            Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Итог κ строкой состояния — тем же текстом, что приложение (`TractCurvatureStatus`, внутренний: отражением).</summary>
        static string Status(DCFwhmCalibrationView.TractCurvatureFit f)
        {
            MethodInfo m = typeof(DCFwhmCalibrationView).GetMethod("TractCurvatureStatus", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return (string)m.Invoke(null, new object[] { f });
        }

        static string Describe(DCFwhmCalibrationView.TractCurvatureFit f)
        {
            if (f == null)
            {
                return "подгонки не было";
            }
            return f.Ok
                ? string.Format(Inv, "{0} 1/МэВ (кривая {1}, z₀ {2} кэВ, линий {3}, центром масс {4}, наибольший промах линии {5} кан.)", f.Kappa.ToString("0.00000", Inv), f.Curve,
                                f.ZeroLightKev.ToString("0.00", Inv), f.Lines, f.CentroidLines, f.WorstChannels.ToString("0.000", Inv))
                : "ОТКАЗ: " + f.Refusal;
        }

        /// <summary>Пики линий: окно ±1.5 FWHM вокруг канала линии, центр масс; второй проход — вокруг найденного.</summary>
        static List<Pick> Picks(ResultData rd, double[] lines)
        {
            EnergySpectrum es = rd.EnergySpectrum;
            EnergyCalibration cal = es.EnergyCalibration;
            var picks = new List<Pick>();
            foreach (double e in lines)
            {
                double ch0 = cal.EnergyToChannel(e, maxChannels: es.NumberOfChannels);
                double centroid = ch0;
                double fw = 10.0;
                for (int pass = 0; pass < 3; pass++)
                {
                    int center = (int)Math.Round(centroid);
                    fw = rd.FwhmCalibration != null ? rd.FwhmCalibration.ChannelToFwhm(Math.Max(1, center)) : 10.0;
                    int half = Math.Max(3, (int)Math.Ceiling(1.5 * fw));
                    int start = Math.Max(0, center - half), end = Math.Min(es.NumberOfChannels - 1, center + half);
                    int max = start;
                    for (int c = start; c <= end; c++) if (es.Spectrum[c] > es.Spectrum[max]) max = c;
                    if (pass == 0)
                    {
                        centroid = max;
                        continue;
                    }
                    centroid = new SpectrumAriphmetics().FindCentroid(es, max, start, end, true);
                }
                int k = (int)Math.Round(centroid);
                picks.Add(new Pick { Line = e, Centroid = centroid, Fwhm = fw, Count = k >= 0 && k < es.Spectrum.Length ? es.Spectrum[k] : 0 });
            }
            return picks;
        }

        /// <summary>Оболочка главного окна без окна: только то, что зовёт вкладка калибровки (как у `UnifiedCalibrationProbeA276`).</summary>
        static MainForm Shell(DocEnergySpectrum doc)
        {
            MainForm form = (MainForm)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(MainForm));
            SetField(form, "activeDocument", doc);
            SetField(form, "documentManager", DocumentManager.GetInstance());
            SetField(form, "dcResultViewList", new List<DCResultView>());
            SetField(form, "toolStripStatusLabel1", new System.Windows.Forms.ToolStripStatusLabel());
            try { SetField(form, "dcPeakDetectionView", new DCPeakDetectionView(form)); } catch (Exception) { }
            return form;
        }

        static object Field(object o, string name)
        {
            return Field(o.GetType(), name).GetValue(o);
        }

        /// <summary>
        /// ⛔ Положительный контроль на данных: спектр пересобран с добавочной кривизной тракта Δκ.
        /// Канал u (от точки нуля −½) уходит в u' = u·(1 + δ·u)/(1 + δ·U), δ = Δκ·шаг/1000 (шаг —
        /// кэВ на канал по калибровке файла), U — канал верхней линии: верхняя линия на месте, как
        /// у карты разбора. Отсчёты канала раскладываются по новым каналам пропорционально.
        /// </summary>
        static void Bend(ResultData rd, double dk, double topLine)
        {
            EnergySpectrum es = rd.EnergySpectrum;
            int n = es.NumberOfChannels;
            EnergyCalibration cal = es.EnergyCalibration;
            double topChannel = cal.EnergyToChannel(topLine, maxChannels: n) + 0.5;
            double step = (cal.ChannelToEnergy(n - 1) - cal.ChannelToEnergy(0)) / (n - 1);
            double delta = dk * step / 1000.0;
            Func<double, double> map = u => u * (1.0 + delta * u) / (1.0 + delta * topChannel);
            double[] acc = new double[n];
            for (int i = 0; i < n; i++)
            {
                double lo = map(i), hi = map(i + 1.0); // края канала i: от −½ — u = i…i+1
                double count = es.Spectrum[i];
                if (count == 0 || !(hi > lo)) continue;
                int a = (int)Math.Floor(lo), b = (int)Math.Floor(hi);
                for (int j = a; j <= b; j++)
                {
                    if (j < 0 || j >= n) continue;
                    double part = (Math.Min(hi, j + 1.0) - Math.Max(lo, j)) / (hi - lo);
                    if (part > 0) acc[j] += count * part;
                }
            }
            int[] spectrum = new int[n];
            double carry = 0.0;
            for (int j = 0; j < n; j++)
            {
                double v = acc[j] + carry;
                spectrum[j] = (int)Math.Round(v);
                carry = v - spectrum[j];
            }
            es.Spectrum = spectrum;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  мелочь
        // ══════════════════════════════════════════════════════════════════════

        // `DCFwhmCalibrationView.SolveEnergy` и `FsaLightScale` — внутренние: отражением
        static double[] SolveEnergy(List<CalibrationPoint> points, int order, int channels, out int used)
        {
            MethodInfo m = typeof(DCFwhmCalibrationView).GetMethod("SolveEnergy", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            object[] a = { points, order, channels, false, 0 };
            double[] r = (double[])m.Invoke(null, a);
            used = (int)a[4];
            return r;
        }

        static Type LightScaleType
        {
            get { return typeof(FsaAnalyzer).Assembly.GetType("BecquerelMonitor.FullSpectrumAnalysis.FsaLightScale", true); }
        }

        static bool LightKnown(string curve)
        {
            return (bool)LightScaleType.GetMethod("Known", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new object[] { curve });
        }

        static double LightShift(string curve, double e, double reference)
        {
            return (double)LightScaleType.GetMethod("ShiftKev", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new object[] { curve, e, reference });
        }

        static FieldInfo Field(Type t, string name)
        {
            for (; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, F | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            throw new MissingFieldException(name);
        }

        static void SetField(object o, string name, object value)
        {
            Field(o.GetType(), name).SetValue(o, value);
        }

        static object Call(object target, string name, params object[] args)
        {
            // закрытые методы базового класса (вкладка калибровки) видны только по своему типу
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                foreach (MethodInfo m in t.GetMethods(F | BindingFlags.DeclaredOnly))
                {
                    if (m.Name == name && m.GetParameters().Length == args.Length)
                    {
                        try { return m.Invoke(target, args); }
                        catch (TargetInvocationException ex) { throw ex.InnerException; }
                    }
                }
            }
            throw new MissingMethodException(target.GetType().Name, name);
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine("  {0} {1}", ok ? "да " : "НЕТ", what);
            if (!ok) bad++;
        }

        static string Inner(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split('\n')[0].Trim();
        }
    }
}
