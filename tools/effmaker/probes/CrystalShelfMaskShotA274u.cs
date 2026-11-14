using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace CrystalShelfMaskShotA274u
{
    /// <summary>
    /// (`AMBER274`, полоса `a274u`, 14.11.2026) ОКНО K-ПИКА СЛОЯ В ИНТЕРФЕЙСЕ: строка окна отчёта FSA и штрих на
    /// графике (решение Amber 14.11.2026 вопросником, дословно: «Да, строка и штрих (Рекомендую)»).
    ///
    /// Стенд — тот же, что у `CrystalOwnShelfProbeA274c` (сцена LaBr3 38×38, матрица из `--cache=`, раздел слоя
    /// `CrystalOwnActivityBuilder.Build`). Плечи:
    ///   В  — слой ВКЛ, окно ВКЛ: окно снято → строка в окне отчёта (en и ru) и штрих на графике в краях окна;
    ///   Б  — слой ВКЛ, окно ВЫКЛ: каналов нет → ни строки, ни штриха;
    ///   К  — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ «слой без пика»: слой ВКЛ, окно ВКЛ, порог пика `--ratio=` (умолчание 100) —
    ///        окно не снимается → ни строки, ни штриха;
    ///   А  — без слоя: ни строки, ни штриха.
    ///
    /// Штрих судится разностью двух кадров одного результата. У плеча В — как есть против обнулённого числа каналов окна:
    /// разность обязана быть, и только в столбцах окна. У плеч Б, К, А (`crit274u`) — как есть против кадра с краями
    /// 20/44 кэВ при нуле каналов: ни пикселя, то есть штрих держится на числе каналов, а не на краях. Строка судится по
    /// строкам НАСТОЯЩЕГО окна отчёта (`FSAReportView.ReportTable`), снятого в PNG.
    ///
    ///     CrystalShelfMaskShotA274u --spectrum=... --cache=каталог --out=каталог (--nuclides=152EU | --chains=Th-228)
    ///                               [--ratio=100] [--histories=100000]
    ///
    /// Коды: 0 — сошлось; 1 — нет; 2 — ключи или файл.
    /// </summary>
    static class Program
    {
        static int bad;
        static readonly List<string> chains = new List<string>();
        static readonly List<string> nuclides = new List<string>();
        static string cacheDir, outDir;

        const int ChartWidth = 900, ChartHeight = 520, ReportWidth = 460, ReportHeight = 1900;
        const double FromKev = 0.0, ToKev = 160.0;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();

            string spectrumPath = null;
            int histories = 100000;
            double controlRatio = 100.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--cache=", StringComparison.Ordinal)) cacheDir = a.Substring(8);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
                else if (a.StartsWith("--histories=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--ratio=", StringComparison.Ordinal)) controlRatio = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--nuclides=", StringComparison.Ordinal)) nuclides.AddRange(a.Substring(11).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--chains=", StringComparison.Ordinal)) chains.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else { Console.Error.WriteLine("непонятный ключ: " + a); return 2; }
            }

            if (spectrumPath == null || cacheDir == null || outDir == null || (chains.Count == 0 && nuclides.Count == 0))
            {
                Console.Error.WriteLine("нужны --spectrum=, --cache=, --out= и проба (--nuclides= или --chains=)");
                return 2;
            }

            if (!File.Exists(spectrumPath)) { Console.Error.WriteLine("нет файла: " + spectrumPath); return 2; }
            Directory.CreateDirectory(outDir);

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            GeometryModel scene = LanthanumScene();
            string note;
            ResponseCrystalSection section = CrystalOwnActivityBuilder.Build(scene, 2.0, histories, 0x274, out note);
            Console.WriteLine("слой собран: записей {0}{1}", section.Count, string.IsNullOrEmpty(note) ? "" : "; " + note);
            ResponseMatrix matrix = Matrix(scene);
            if (matrix == null) return 2;
            matrix.CrystalSection = section;
            matrix.CrystalSectionDamage = null;

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("=== {0} ===", Path.GetFileName(spectrumPath));

            var arms = new[]
            {
                new ArmSpec { Tag = "V", Name = "В слой, окно вкл", Section = section, Shelf = true, Ratio = double.NaN, ExpectWindow = true },
                new ArmSpec { Tag = "B", Name = "Б слой, окно выкл", Section = section, Shelf = false, Ratio = double.NaN, ExpectWindow = false },
                new ArmSpec { Tag = "K", Name = "К слой без пика (порог " + controlRatio.ToString("F0", CultureInfo.InvariantCulture) + ")", Section = section, Shelf = true, Ratio = controlRatio, ExpectWindow = false },
                new ArmSpec { Tag = "A", Name = "А без слоя", Section = null, Shelf = true, Ratio = double.NaN, ExpectWindow = false },
            };

            foreach (ArmSpec arm in arms)
            {
                Console.WriteLine();
                Console.WriteLine("--- {0} ---", arm.Name);
                int analyzerMasked;
                FsaResult result = Run(arm, rd, scene, matrix, out analyzerMasked);
                if (result == null)
                {
                    Check(arm.Name + ": разбор прошёл", false);
                    continue;
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "окно: каналов {0} (анализатор {1}), {2:F3}–{3:F3} кэВ; усиление {4:F6}, опор {5}, откат {6}, χ²/ndf П {7:F2}",
                    result.CrystalShelfMaskChannels, analyzerMasked, result.CrystalShelfMaskLoKev, result.CrystalShelfMaskHiKev,
                    result.Gain, result.ScaleAnchorsUsed, result.AnchorRolledBack ? "да" : "нет", result.Chi2NdfPoisson));
                Check(arm.Name + ": число каналов окна в результате равно числу анализатора", result.CrystalShelfMaskChannels == analyzerMasked);
                if (arm.ExpectWindow)
                {
                    Check(arm.Name + ": окно снято", result.CrystalShelfMaskChannels > 0);
                    Check(arm.Name + ": края окна внутри 20–44 кэВ", result.CrystalShelfMaskLoKev >= 20.0 && result.CrystalShelfMaskHiKev <= 44.0
                                                                       && result.CrystalShelfMaskHiKev > result.CrystalShelfMaskLoKev);
                }
                else
                {
                    Check(arm.Name + ": окно не снято", result.CrystalShelfMaskChannels == 0
                                                        && result.CrystalShelfMaskLoKev == 0.0 && result.CrystalShelfMaskHiKev == 0.0);
                }

                // ---- штрих: у плеча с окном — кадр как есть против кадра с обнулённым числом каналов; у плеч без окна
                // (`crit274u`) — кадр как есть против кадра с краями 20/44 кэВ при нуле каналов: штрих обязан держаться
                // на числе каналов, а не на краях ----
                Bitmap chart = Chart(rd, result);
                int saved = result.CrystalShelfMaskChannels;
                double savedLo = result.CrystalShelfMaskLoKev, savedHi = result.CrystalShelfMaskHiKev;
                if (arm.ExpectWindow)
                {
                    result.CrystalShelfMaskChannels = 0;
                }
                else
                {
                    result.CrystalShelfMaskLoKev = 20.0;
                    result.CrystalShelfMaskHiKev = 44.0;
                }

                Bitmap chartWithout = Chart(rd, result);
                if (!arm.ExpectWindow)
                {
                    Console.WriteLine("второй кадр: каналов {0}, края {1:F1}–{2:F1} кэВ",
                                      result.CrystalShelfMaskChannels, result.CrystalShelfMaskLoKev, result.CrystalShelfMaskHiKev);
                }

                result.CrystalShelfMaskChannels = saved;
                result.CrystalShelfMaskLoKev = savedLo;
                result.CrystalShelfMaskHiKev = savedHi;
                int diff, xMin, xMax;
                Diff(chart, chartWithout, out diff, out xMin, out xMax);
                int px0 = PixelOf(result.CrystalShelfMaskLoKev), px1 = PixelOf(result.CrystalShelfMaskHiKev);
                Console.WriteLine("штрих: различных пикселей {0}, столбцы {1}…{2}; столбцы окна {3}…{4}", diff, xMin, xMax, px0, px1);
                if (arm.ExpectWindow)
                {
                    Check(arm.Name + ": штрих есть", diff > 0);
                    Check(arm.Name + ": штрих только в столбцах окна", diff > 0 && xMin >= px0 && xMax <= px1);
                }
                else
                {
                    Check(arm.Name + ": штриха нет и при краях 20/44 кэВ с нулём каналов", diff == 0);
                }

                chartWithout.Dispose();

                // ---- строка: настоящее окно отчёта, en и ru ----
                foreach (string ui in new[] { "en", "ru" })
                {
                    Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(ui);
                    string expected = FSAReportView.CrystalShelfText(result);
                    string template = new ComponentResourceManager(typeof(FSAReportView)).GetString("FSAReport_CrystalShelfRow", Thread.CurrentThread.CurrentUICulture);
                    string tail = template != null && template.IndexOf("{1}", StringComparison.Ordinal) >= 0
                        ? template.Substring(template.IndexOf("{1}", StringComparison.Ordinal) + 3) : template;
                    List<string> rows;
                    string png = Path.Combine(outDir, "a274u_" + arm.Tag + "_" + ui + ".png");
                    Shot(rd, result, chart, png, out rows);
                    int found = 0, foreign = 0;
                    string foundTip = null;
                    foreach (string row in rows)
                    {
                        string[] cells = row.Split('\t');
                        if (expected != null && cells[0] == expected)
                        {
                            found++;
                            foundTip = cells.Length > 2 ? cells[2] : null;
                            Console.WriteLine("СТРОКА [{0}]\t{1}\t{2}", ui, cells[0], cells.Length > 1 ? cells[1] : "");
                            Console.WriteLine("ПОДСКАЗКА [{0}]\t{1}", ui, foundTip);
                        }
                        else if (!string.IsNullOrEmpty(tail) && cells[0].EndsWith(tail, StringComparison.Ordinal))
                        {
                            foreign++;
                        }
                    }

                    Console.WriteLine("{0}: строк в таблице {1}", png, rows.Count);
                    if (arm.ExpectWindow)
                    {
                        Check(arm.Name + " [" + ui + "]: строка окна в отчёте ровно одна", expected != null && found == 1 && foreign == 0);
                        Check(arm.Name + " [" + ui + "]: подсказка называет число каналов",
                              foundTip != null && foundTip.Contains(result.CrystalShelfMaskChannels.ToString(CultureInfo.InvariantCulture)));
                    }
                    else
                    {
                        Check(arm.Name + " [" + ui + "]: строки окна нет", expected == null && found == 0 && foreign == 0);
                    }
                }

                chart.Dispose();
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        sealed class ArmSpec
        {
            public string Tag, Name;
            public ResponseCrystalSection Section;
            public bool Shelf, ExpectWindow;
            public double Ratio;
        }

        static FsaResult Run(ArmSpec arm, ResultData rd, GeometryModel geometry, ResponseMatrix matrix, out int masked)
        {
            masked = -1;
            var analyzer = new FsaAnalyzer();
            analyzer.PileUp = false;
            analyzer.RequireGeometry = false;
            analyzer.CrystalOwnLayer = arm.Section != null;
            analyzer.CrystalOwnShelfMask = arm.Shelf;
            if (!double.IsNaN(arm.Ratio))
            {
                analyzer.CrystalOwnShelfPeakRatio = arm.Ratio;
            }

            FsaTuningReport.Print(analyzer, arm.Name);
            FsaMatrixBinding.Bind(analyzer, geometry, matrix);
            analyzer.CrystalOwnSection = arm.Section;
            if (rd.Efficiency == null)
            {
                rd.Efficiency = new EfficiencyConfigData { Geometry = geometry };
            }
            else
            {
                rd.Efficiency.Geometry = geometry;
            }

            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, nuclides, true, true);
            FsaCalculationOptions.Of(rd).ApplyTo(spec);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            if (library.Count == 0)
            {
                Console.Error.WriteLine("библиотека пуста");
                return null;
            }

            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, null, rd.FwhmCalibration, library, efficiency);
            if (result == null)
            {
                Console.Error.WriteLine("отказ разбора [{0}]: {1} — {2}", arm.Name, analyzer.Refusal, analyzer.RefusalNote);
                return null;
            }

            masked = analyzer.CrystalOwnShelfMaskChannels;
            if (!string.IsNullOrEmpty(result.CrystalOwnNote))
            {
                Console.WriteLine("примечание слоя: " + result.CrystalOwnNote);
            }

            return result;
        }

        // ---- график: вид приложения без окна, тем же приёмом, что `FsaStackShot` ----

        static int PixelOf(double kev)
        {
            return (int)((kev - FromKev) * ((ChartWidth - 1) / (ToKev - FromKev))) + 1;
        }

        static Bitmap Chart(ResultData rd, FsaResult result)
        {
            EnergySpectrum spectrum = rd.EnergySpectrum;
            EnergyCalibration calibration = spectrum.EnergyCalibration;
            double ceiling = 1.0;
            for (int i = 0; i < spectrum.NumberOfChannels; i++)
            {
                double kev = calibration.ChannelToEnergy(i);
                if (kev >= FromKev && kev <= ToKev)
                {
                    ceiling = Math.Max(ceiling, spectrum.Spectrum[i]);
                    if (result.Model != null && i < result.Model.Length)
                    {
                        ceiling = Math.Max(ceiling, result.Model[i]);
                    }
                }
            }

            ceiling *= 1.15;
            const int left = 1;
            var image = new Bitmap(ChartWidth, ChartHeight);
            using (var view = new EnergySpectrumView())
            {
                Set(view, "energySpectrum", spectrum);
                Set(view, "activeResultData", rd);
                Set(view, "energyCalibration", calibration);
                Set(view, "numberOfChannels", spectrum.NumberOfChannels);
                Set(view, "backgroundMode", BackgroundMode.ShowFSA);
                Set(view, "horizontalUnit", HorizontalUnit.Energy);
                Set(view, "verticalUnit", VerticalUnit.Counts);
                Set(view, "verticalScaleType", VerticalScaleType.LinearScale);
                Set(view, "pownum", 2.0);
                Set(view, "totalMinValuePow", 0.0);
                Set(view, "valueRangePow", Math.Sqrt(ceiling));
                Set(view, "totalMinValueLog", 0.0);
                Set(view, "valueRangeLog", Math.Log10(ceiling));
                Set(view, "height", ChartHeight);
                Set(view, "width", ChartWidth - left);
                Set(view, "left", left);
                Set(view, "scrollX", 0);
                Set(view, "scrollY", 0);
                Set(view, "scrollBaseY", 0.0);
                Set(view, "verticalScale", 1.0);
                Set(view, "horizontalScale", 1.0);
                Set(view, "totalMinValue", 0.0);
                Set(view, "valueRange", ceiling);
                Set(view, "energyViewOffset", FromKev);
                Set(view, "pixelPerEnergy", (ChartWidth - left) / (ToKev - FromKev));
                Set(view, "dirty", false);
                object overlay = typeof(EnergySpectrumView).GetProperty("FsaSession", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view, null);
                Field(overlay.GetType(), "result").SetValue(overlay, result);
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(Color.Black);
                    MethodInfo show = typeof(EnergySpectrumView).GetMethod("ShowFsaOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (show == null)
                    {
                        throw new InvalidOperationException("нет EnergySpectrumView.ShowFsaOverlay");
                    }

                    show.Invoke(view, new object[] { g });
                }
            }

            return image;
        }

        static void Diff(Bitmap a, Bitmap b, out int count, out int xMin, out int xMax)
        {
            count = 0;
            xMin = int.MaxValue;
            xMax = int.MinValue;
            for (int y = 0; y < a.Height; y++)
            {
                for (int x = 0; x < a.Width; x++)
                {
                    if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb())
                    {
                        count++;
                        xMin = Math.Min(xMin, x);
                        xMax = Math.Max(xMax, x);
                    }
                }
            }
        }

        /// <summary>Снимок: график слева, настоящее окно отчёта справа; строки таблицы — «подпись TAB значение TAB подсказка».</summary>
        static void Shot(ResultData rd, FsaResult result, Bitmap chart, string path, out List<string> rows)
        {
            rows = new List<string>();
            using (var view = new EnergySpectrumView())
            {
                var session = (FsaAnalysisSession)typeof(EnergySpectrumView).GetProperty("FsaSession", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view, null);
                Field(session.GetType(), "result").SetValue(session, result);
                Field(session.GetType(), "stamp").SetValue(session, FsaAnalysisSession.BuildStamp(rd, rd.BackgroundEnergySpectrum != null));
                using (var host = new Form())
                using (var report = new FSAReportView(null))
                {
                    host.ClientSize = new Size(ReportWidth, ReportHeight);
                    host.StartPosition = FormStartPosition.Manual;
                    host.Location = new Point(-4000, -4000);
                    host.ShowInTaskbar = false;
                    report.TopLevel = false;
                    report.Dock = DockStyle.Fill;
                    host.Controls.Add(report);
                    report.Show();
                    host.Show();
                    report.SetProbeSource(session, rd);
                    Application.DoEvents();
                    using (var combined = new Bitmap(chart.Width + ReportWidth, Math.Max(chart.Height, ReportHeight)))
                    {
                        using (Graphics g = Graphics.FromImage(combined))
                        {
                            g.Clear(Color.White);
                            g.DrawImageUnscaled(chart, 0, 0);
                            using (var shot = new Bitmap(ReportWidth, ReportHeight))
                            {
                                report.DrawToBitmap(shot, new Rectangle(0, 0, ReportWidth, ReportHeight));
                                g.DrawImageUnscaled(shot, chart.Width, 0);
                            }
                        }

                        combined.Save(path, ImageFormat.Png);
                    }

                    foreach (XPTable.Models.Row row in report.ReportTable.TableModel.Rows)
                    {
                        string name = row.Cells.Count > 0 && row.Cells[0] != null ? row.Cells[0].Text ?? "" : "";
                        string value = row.Cells.Count > 1 && row.Cells[1] != null ? row.Cells[1].Text ?? "" : "";
                        string hint = row.Cells.Count > 0 && row.Cells[0] != null ? row.Cells[0].ToolTipText ?? "" : "";
                        rows.Add(name.Replace('\t', ' ') + "\t" + value.Replace('\t', ' ') + "\t" + hint.Replace('\t', ' ').Replace(Environment.NewLine, " "));
                    }

                    host.Hide();
                }
            }
        }

        static FieldInfo Field(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    return field;
                }
            }

            throw new InvalidOperationException("нет поля " + name + " у " + type.Name);
        }

        static void Set(object target, string name, object value)
        {
            Field(target.GetType(), name).SetValue(target, value);
        }

        // ---- сцена, матрица, спектр: те же, что у `CrystalOwnShelfProbeA274c` ----

        static GeometryModel LanthanumScene()
        {
            return new GeometryModel
            {
                Name = "LaBr3 38x38 (AMBER274)",
                IsScintillator = true,
                Shape = CrystalShape.Cylinder,
                CrystalDiameter = 38.0,
                CrystalHeight = 38.0,
                Crystal = Material("Lanthanum bromide"),
                Reflector = Material("Polytetrafluoroethylene"),
                Cladding = Material("Aluminum"),
                Gap = Material("Air, dry"),
                FrontReflectorThickness = 1.0,
                SideReflectorThickness = 1.5,
                FrontCladdingThickness = 1.0,
                SideCladdingThickness = 1.0,
                FrontGapThickness = 0.0,
                SideGapThickness = 0.0,
                MountingThickness = 0.0,
            };
        }

        static GeometryMaterial Material(string name)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
            return entry != null ? GeometryMaterialLibrary.Make(entry, entry.Density) : new GeometryMaterial();
        }

        /// <summary>Матрица сцены — только из кэша (`a274_lanthanum_bromide_38x38.rmx`); нет годной — отказ, пересчёт не делается.</summary>
        static ResponseMatrix Matrix(GeometryModel geometry)
        {
            string path = Path.Combine(cacheDir, "a274_lanthanum_bromide_38x38.rmx");
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("нет матрицы в кэше: " + path);
                return null;
            }

            MatrixRefusal refusal;
            int format;
            ResponseMatrix loaded = ResponseMatrix.Load(path, out refusal, out format);
            if (loaded == null || !loaded.IsValidFor(geometry))
            {
                Console.Error.WriteLine("матрица из кэша не годна: " + refusal);
                return null;
            }

            Console.WriteLine("матрица из файла: {0}", path);
            return loaded;
        }

        static ResultData Load(string path)
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
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            return rd;
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine("{0}: {1}", ok ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ", what);
            if (!ok)
            {
                bad++;
            }
        }
    }
}
