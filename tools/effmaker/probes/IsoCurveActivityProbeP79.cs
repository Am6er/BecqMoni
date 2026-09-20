using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using XPTable.Models;

namespace IsoCurveActivityProbeP79
{
    /// <summary>
    /// СТОРОЖ `AMBER34` (П79, 15.09.2026): кривая сцены поля `ISO` — площадь в
    /// см² на единичный флюенс, клеймо `norm=fluence` — НЕ идёт в активность
    /// как доля на квант, а точка выше единицы у кривой ДОЛЕЙ называется, а не
    /// выбрасывается.
    ///
    ///     isocurveactivityprobep79
    ///
    /// Две кривые ОДНОЙ геометрии (числа синтетические, в духе П2: у G1S
    /// Ø63×63 A_пик ≈ 13.8 см² на 662 кэВ — `handover-2026-09-12-p2-iso-field.md`
    /// §3.1): `Point` — доли, клеймо без `norm=fluence`; `Iso` — см², клеймо с
    /// `norm=fluence`. Одна сцена (пик 662 кэВ на плоском континууме, фон),
    /// три пути приложения — ТЕМ ЖЕ кодом, что у экрана:
    ///
    ///   1. зона — `MeasurementResultManager.Translate` → `BecquerelCoefficient.Resolve`;
    ///   2. выделение — `EnergySpectrumView.EnsureSelectionAnalytics`
    ///      (вид поднимается без конструктора, как в `BqActivityProbe`);
    ///   3. разбор FSA — `FsaAnalyzer.Analyze` + строки блока качества окна
    ///      отчёта `FSAReportView.MakeQualityRows`.
    ///
    /// Ожидание (решения Amber 15.09.2026, дословно: «В автоматическом режиме
    /// скрыть активность и показать причину; явно ручной режим сохранить» и
    /// «Разбор идёт, Бк скрыты с причиной»):
    ///   * `Point`: K конечен, активность считается на всех трёх путях;
    ///   * `Iso`, зона авто: `IsValid = false`, статус — причина
    ///     (`ResultFieldCurve`); зона ручная: побитово как у `Point` с тем же
    ///     сохранённым K; выделение: отказ `ActivityFieldCurveRefused`;
    ///     FSA: результат есть, состав тот же, `EfficiencyPerUnitFluence`,
    ///     строка «Efficiency curve» окна — `FSAReportEfficiencyFieldCurve`;
    ///     с матрицей поля — суммирование не создано, причина в строке;
    ///   * подсадка 1.5 в кривую долей — НАЗВАНА (точка и энергия) на всех
    ///     трёх путях, а не выброшена;
    ///   * нормировка спектра кривой (`SpectrumAriphmetics.NormalizeRefusal`,
    ///     доделка П79): отвергнутая кривая — причина с точкой, не «не
    ///     выбрана»; кривая ПОЛЯ — отказ словами (спектр — `int[]`, деление на
    ///     A > 1 см² обнуляет малые каналы: 5 / 13.82 → 0), спектр не делится.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — старая сборка (worktree HEAD до правки)
    /// обязана краснеть: новые члены (`FromConfig(config, out refusal)`,
    /// `LineProblem.FieldCurve`, `Result.Refused`, `FsaResult.EfficiencyPerUnitFluence`,
    /// ресурсы) берутся ОТРАЖЕНИЕМ и по имени, чтобы проба собиралась и на
    /// старой сборке и там отказывала числами, а не компилятором.
    ///
    /// Числа печатаются инвариантной культурой. Окон нет: `MessageBox` вешает
    /// безоконный прогон. Ожидание: «ВСЕ СОШЛИСЬ», код 0.
    /// </summary>
    static class Program
    {
        static int bad;

        const double LineKev = 661.657;
        const double LineYield = 85.1;
        const double StoredK = 777.0;
        const double StoredKError = 7.0;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // ⛔ Культура ЦЕЛИКОМ инвариантная (`T247`): печать И разбор.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек FSA — ДО разбора ключей.
            FsaTuningReport.Snapshot();

            if (args.Length > 0)
            {
                Console.Error.WriteLine("ключей у пробы нет: " + string.Join(" ", args));
                return 2;
            }

            // ⛔ ОБЕ карты примитивов ROI — ДО менеджеров-одиночек (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            Console.WriteLine("=== чем мерено ===");
            Console.WriteLine("сборка приложения: {0}", typeof(EnergySpectrumView).Assembly.Location);
            Console.WriteLine("клеймо поля: {0}", FluenceMark());
            Console.WriteLine();

            EfficiencyConfigData point = PointCurve();
            EfficiencyConfigData iso = IsoCurve();

            Section1_Curves(point, iso);
            Section2_Zones(point, iso);
            Section3_Selection(point, iso);
            Section4_Fsa(point, iso);
            Section5_Planted(point);
            Section6_Normalize(point, iso);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format(CultureInfo.InvariantCulture, "НЕ СОШЛОСЬ: {0}", bad));
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // кривые
        // ------------------------------------------------------------------

        static string FluenceMark()
        {
            FieldInfo f = typeof(DoseRateInput).GetField("FluenceStampMark", BindingFlags.Public | BindingFlags.Static);
            return f != null ? (string)f.GetRawConstantValue() : "norm=fluence";
        }

        /// <summary>Кривая долей: точка на 50 см от G1S Ø63×63, синтетика.</summary>
        static EfficiencyConfigData PointCurve()
        {
            var c = new EfficiencyConfigData("G1S point 50 cm (проба)");
            c.ComputeStamp = "phys=18; hist=200000; grid=30-3000 keV/34 std";
            c.Geometry = new GeometryModel();
            c.Curve = new List<ROIEfficiencyData>
            {
                new ROIEfficiencyData { Energy = 50.0,   Efficiency = 9.5e-4, ErrorPercent = 2.0 },
                new ROIEfficiencyData { Energy = 100.0,  Efficiency = 9.0e-4, ErrorPercent = 2.0 },
                new ROIEfficiencyData { Energy = LineKev, Efficiency = 4.4e-4, ErrorPercent = 5.0 },
                new ROIEfficiencyData { Energy = 2000.0, Efficiency = 2.1e-4, ErrorPercent = 8.0 },
            };
            return c;
        }

        /// <summary>Кривая сцены поля той же геометрии: A_пик, см² (П2 §3.1: 13.82 на 662 кэВ).</summary>
        static EfficiencyConfigData IsoCurve()
        {
            var c = new EfficiencyConfigData("G1S ISO field (проба)");
            c.ComputeStamp = "phys=18; hist=200000; grid=30-3000 keV/34 std; " + FluenceMark();
            c.Geometry = new GeometryModel { Scene = GeometrySceneKind.Iso };
            c.Curve = new List<ROIEfficiencyData>
            {
                new ROIEfficiencyData { Energy = 50.0,   Efficiency = 29.8,  ErrorPercent = 2.0 },
                new ROIEfficiencyData { Energy = 100.0,  Efficiency = 28.3,  ErrorPercent = 2.0 },
                new ROIEfficiencyData { Energy = LineKev, Efficiency = 13.82, ErrorPercent = 5.0 },
                new ROIEfficiencyData { Energy = 2000.0, Efficiency = 6.6,   ErrorPercent = 8.0 },
            };
            return c;
        }

        // ------------------------------------------------------------------
        // 1. кривая доезжает до интерполятора
        // ------------------------------------------------------------------

        static void Section1_Curves(EfficiencyConfigData point, EfficiencyConfigData iso)
        {
            Console.WriteLine("=== 1. FsaEfficiency.FromConfig: кривая доезжает, нормировка названа ===");
            FsaEfficiency p = FsaEfficiency.FromConfig(point);
            FsaEfficiency i = FsaEfficiency.FromConfig(iso);
            Same("Point: кривая построена", true, p != null);
            Same("Iso: кривая построена (было: все точки > 1 выброшены → null)", true, i != null);

            PropertyInfo fluence = typeof(FsaEfficiency).GetProperty("IsPerUnitFluence");
            Same("FsaEfficiency несёт признак IsPerUnitFluence", true, fluence != null);
            if (fluence != null)
            {
                Same("Point: не поле", false, p != null && (bool)fluence.GetValue(p, null));
                Same("Iso: поле", true, i != null && (bool)fluence.GetValue(i, null));
            }

            double eps, err;
            if (p != null && p.TryEval(LineKev, out eps, out err))
            {
                Console.WriteLine("  Point ε(662) = {0}", eps.ToString("G6", CultureInfo.InvariantCulture));
            }

            if (i != null && i.TryEval(LineKev, out eps, out err))
            {
                Console.WriteLine("  Iso   A(662) = {0} см²", eps.ToString("G6", CultureInfo.InvariantCulture));
                Near("Iso: A(662) — та самая точка, не обрезана", 13.82, eps, 1e-9);
            }

            MethodInfo withRefusal = FromConfigWithRefusal();
            Same("есть перегрузка FromConfig(config, out refusal)", true, withRefusal != null);
            Console.WriteLine();
        }

        static MethodInfo FromConfigWithRefusal()
        {
            return typeof(FsaEfficiency).GetMethod("FromConfig", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(EfficiencyConfigData), typeof(string).MakeByRefType() }, null);
        }

        /// <summary>FromConfig с причиной — отражением: на старой сборке перегрузки нет.</summary>
        static FsaEfficiency FromConfigNamed(EfficiencyConfigData config, out string refusal)
        {
            refusal = null;
            MethodInfo m = FromConfigWithRefusal();
            if (m == null)
            {
                return FsaEfficiency.FromConfig(config);
            }

            object[] a = { config, null };
            var curve = (FsaEfficiency)m.Invoke(null, a);
            refusal = (string)a[1];
            return curve;
        }

        // ------------------------------------------------------------------
        // 2. зоны
        // ------------------------------------------------------------------

        static ROIDefinitionData Zone(bool auto)
        {
            return new ROIDefinitionData
            {
                Name = "зона 662",
                PeakEnergy = LineKev,
                Intencity = LineYield,
                BecquerelCoefficient = StoredK,
                BecquerelCoefficientError = StoredKError,
                AutoBecquerelCoefficient = auto,
                Enabled = true,
            };
        }

        /// <summary>Строка беккерелей ТЕМ ЖЕ кодом, что таблица результатов: `Translate`.</summary>
        static MeasurementResult BqRow(Scene sc, EfficiencyConfigData curve, bool auto)
        {
            ROIDefinitionData zone = Zone(auto);
            var rd = new ResultData { EnergySpectrum = sc.Fg, BackgroundEnergySpectrum = sc.Bg, Efficiency = curve };
            var roi = new ROIConfigData();
            roi.ROIDefinitions.Add(zone);
            rd.ROIConfig = roi;
            var counts = new MeasurementResultCollection
            {
                ResultData = rd,
                ROIConfig = roi,
                MeasurementTime = sc.FgTime,
            };
            counts.ResultList.Add(new MeasurementResult(zone, sc.NetCounts, Math.Sqrt(sc.FgCounts), 0.0));
            MeasurementResultCollection bq = new MeasurementResultManager().Translate(counts, ResultTranslation.Becquerels);
            return bq.ResultList[0];
        }

        static void Section2_Zones(EfficiencyConfigData point, EfficiencyConfigData iso)
        {
            Console.WriteLine("=== 2. зона: MeasurementResultManager.Translate → BecquerelCoefficient.Resolve ===");
            Scene sc = Scene.Standard();
            double cps = sc.NetCounts / sc.FgTime;
            Console.WriteLine("  сцена: нетто {0} отсч. за {1} с = {2} имп/с",
                              sc.NetCounts.ToString("F1", CultureInfo.InvariantCulture),
                              sc.FgTime.ToString("F0", CultureInfo.InvariantCulture),
                              cps.ToString("F4", CultureInfo.InvariantCulture));

            // Point, авто: K по кривой, активность есть
            BecquerelCoefficient.Result kp = BecquerelCoefficient.Resolve(Zone(true), point);
            double expectedK = 100.0 / (4.4e-4 * LineYield);
            Near("Point авто: K = 100/(ε·I)", expectedK, kp.Value, 1e-9);
            Same("Point авто: источник — кривая", BecquerelCoefficient.Source.Efficiency, kp.From);
            MeasurementResult rp = BqRow(sc, point, true);
            Same("Point авто: строка валидна", true, rp.IsValid);
            Near("Point авто: A = cps·K", cps * expectedK, rp.ResultValue, 1e-9);
            Console.WriteLine("  Point авто: K = {0}, A = {1} Бк ± {2}",
                              kp.Value.ToString("G6", CultureInfo.InvariantCulture),
                              rp.ResultValue.ToString("F1", CultureInfo.InvariantCulture),
                              rp.ResultError.ToString("F1", CultureInfo.InvariantCulture));

            // Iso, авто: отказ, не откат
            BecquerelCoefficient.Result ki = BecquerelCoefficient.Resolve(Zone(true), iso);
            FieldInfo refused = typeof(BecquerelCoefficient.Result).GetField("Refused");
            FieldInfo statusText = typeof(BecquerelCoefficient.Result).GetField("StatusText");
            Same("Result несёт признак Refused", true, refused != null);
            Same("Iso авто: отказ (Refused)", true, refused != null && (bool)refused.GetValue(ki));
            Same("Iso авто: K не подменён сохранённым", 0.0, ki.Value);
            NotEmpty("Iso авто: причина названа (Problem)", ki.Problem);
            string fieldStatus = Resources.ResourceManager.GetString("ResultFieldCurve", CultureInfo.InvariantCulture);
            NotEmpty("ресурс ResultFieldCurve есть", fieldStatus);
            Same("Iso авто: короткий статус — ResultFieldCurve", fieldStatus,
                 statusText != null ? (string)statusText.GetValue(ki) : null);
            Contains("Iso авто: причина называет кривую по имени", ki.Problem, iso.Name);
            MeasurementResult ri = BqRow(sc, iso, true);
            Same("Iso авто: строка НЕ валидна (активность скрыта)", false, ri.IsValid);
            Same("Iso авто: статус строки — та же причина", fieldStatus, ri.StatusText);
            Console.WriteLine("  Iso авто: IsValid = {0}, статус «{1}»", ri.IsValid, ri.StatusText);
            Console.WriteLine("  Iso авто: причина «{0}»", ki.Problem);

            // ручной режим: кривую не спрашивают вовсе — побитово одно и то же у обеих кривых
            BecquerelCoefficient.Result mp = BecquerelCoefficient.Resolve(Zone(false), point);
            BecquerelCoefficient.Result mi = BecquerelCoefficient.Resolve(Zone(false), iso);
            Same("ручной: Point K = сохранённый", StoredK, mp.Value);
            Same("ручной: Iso K = сохранённый", StoredK, mi.Value);
            Same("ручной: Iso без причины", null, mi.Problem);
            Same("ручной: Iso не отказ", false, refused != null && (bool)refused.GetValue(mi));
            MeasurementResult rmp = BqRow(sc, point, false);
            MeasurementResult rmi = BqRow(sc, iso, false);
            Same("ручной: обе строки валидны", true, rmp.IsValid && rmi.IsValid);
            Same("ручной: A побитово одинакова у Point и Iso",
                 BitConverter.DoubleToInt64Bits(rmp.ResultValue), BitConverter.DoubleToInt64Bits(rmi.ResultValue));
            Same("ручной: dA побитово одинакова", BitConverter.DoubleToInt64Bits(rmp.ResultError),
                 BitConverter.DoubleToInt64Bits(rmi.ResultError));
            Near("ручной: A = cps·777", cps * StoredK, rmi.ResultValue, 1e-9);
            Console.WriteLine("  ручной (K = 777): A = {0} Бк у обеих кривых",
                              rmi.ResultValue.ToString("F1", CultureInfo.InvariantCulture));

            // прежние мягкие беды не тронуты: кривой нет — сохранённый K, причина, не отказ
            BecquerelCoefficient.Result none = BecquerelCoefficient.Resolve(Zone(true), null);
            Same("кривой нет: K сохранённый, как прежде", StoredK, none.Value);
            Same("кривой нет: не отказ", false, refused != null && (bool)refused.GetValue(none));
            NotEmpty("кривой нет: причина есть", none.Problem);
            Console.WriteLine();
        }

        // ------------------------------------------------------------------
        // 3. выделение
        // ------------------------------------------------------------------

        static void Section3_Selection(EfficiencyConfigData point, EfficiencyConfigData iso)
        {
            Console.WriteLine("=== 3. выделение: EnergySpectrumView.EnsureSelectionAnalytics ===");
            // (`AMBER34`) новые причины отказа — по имени, чтобы старая сборка краснела, а не не собиралась
            Same("LineProblem.FieldCurve есть", true, Enum.IsDefined(typeof(BecquerelCoefficient.LineProblem), "FieldCurve"));
            Same("LineProblem.CurveRefused есть", true, Enum.IsDefined(typeof(BecquerelCoefficient.LineProblem), "CurveRefused"));

            BecquerelCoefficient.LineResult lp = BecquerelCoefficient.ForLine(LineKev, LineYield, point);
            Same("Point ForLine: коэффициент получен", true, lp.Ok);
            BecquerelCoefficient.LineResult li = BecquerelCoefficient.ForLine(LineKev, LineYield, iso);
            Same("Iso ForLine: отказ", false, li.Ok);
            Same("Iso ForLine: причина — FieldCurve", "FieldCurve", li.Problem.ToString());

            Scene sp = Scene.Standard(); sp.Result.Efficiency = point; sp.SetLabel("Line-662", LineKev, LineYield);
            Scene.Answer ap = sp.Run();
            Same("Point: активность на панели есть", true, ap.Activity > 0.0);
            Same("Point: отказа нет", null, ap.Refusal);
            Console.WriteLine("  Point: A = {0} Бк", ap.Activity.ToString("F1", CultureInfo.InvariantCulture));

            Scene si = Scene.Standard(); si.Result.Efficiency = iso; si.SetLabel("Line-662", LineKev, LineYield);
            Scene.Answer ai = si.Run();
            Same("Iso: числа нет", 0.0, ai.Activity);
            NotEmpty("Iso: отказ назван", ai.Refusal);
            string pattern = Resources.ResourceManager.GetString("ActivityFieldCurveRefused", CultureInfo.InvariantCulture);
            NotEmpty("ресурс ActivityFieldCurveRefused есть", pattern);
            if (pattern != null)
            {
                Same("Iso: отказ — ActivityFieldCurveRefused с именем кривой",
                     string.Format(CultureInfo.InvariantCulture, pattern, iso.Name), ai.Refusal);
            }

            Console.WriteLine("  Iso: отказ «{0}»", ai.Refusal);
            Console.WriteLine();
        }

        // ------------------------------------------------------------------
        // 4. FSA
        // ------------------------------------------------------------------

        static FsaResult Fsa(Scene sc, EfficiencyConfigData curve, ResponseMatrix matrix, out FsaAnalyzer analyzer)
        {
            var definitions = new List<NuclideDefinition>
            {
                new NuclideDefinition { Name = "Line-662", Energy = LineKev, Intencity = LineYield, Visible = true, Sets = new HashSet<Guid>() },
            };
            var peak = new Peak
            {
                Energy = LineKev,
                Channel = (int)Math.Round(LineKev),
                Count = 40000,
                FWHM = 2.354820045 * 12.0,
                SNR = 100.0,
                Nuclide = definitions[0],
            };
            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(new[] { peak }, definitions);
            analyzer = new FsaAnalyzer();
            if (matrix != null)
            {
                analyzer.ResponseMatrix = matrix;
            }

            // (`T243`) чем считали — до счёта и вслух
            FsaTuningReport.Print(analyzer, curve.Name + (matrix != null ? " + матрица" : ""));
            var fwhm = new SimpleSqrtFwhmCalibration { Coefficients = new[] { 0.0, 28.26 * 28.26 / LineKev } };
            return analyzer.Analyze(sc.Fg, null, fwhm, library, FsaEfficiency.FromConfig(curve));
        }

        /// <summary>Матрица сцены поля из одного узла на линии: строка — образ пика.</summary>
        static ResponseMatrix FieldMatrix()
        {
            int bins = (int)Math.Round(LineKev) + 1;
            var row = new float[bins];
            row[bins - 1] = 13.82f;
            return new ResponseMatrix
            {
                Energies = new[] { LineKev },
                BinKev = 1.0,
                Rows = new[] { row },
                Normalization = ResponseMatrixNormalization.PerUnitFluence,
            };
        }

        static void Section4_Fsa(EfficiencyConfigData point, EfficiencyConfigData iso)
        {
            Console.WriteLine("=== 4. разбор FSA: FsaAnalyzer.Analyze + строки блока качества окна отчёта ===");
            Scene sc = Scene.Standard();
            FsaAnalyzer ap, ai, am;
            FsaResult rp = Fsa(sc, point, null, out ap);
            FsaResult ri = Fsa(sc, iso, null, out ai);
            Same("Point: разбор есть", true, rp != null);
            Same("Iso: разбор ИДЁТ (было: null — гейт геометрии от пустой кривой)", true, ri != null);
            Same("Iso: гейт геометрии не сработал", false, ai.GeometryRefused);

            PropertyInfo fluenceProp = typeof(FsaResult).GetProperty("EfficiencyPerUnitFluence");
            PropertyInfo summingProp = typeof(FsaResult).GetProperty("CascadeSummingRefusedFieldMatrix");
            Same("FsaResult несёт EfficiencyPerUnitFluence", true, fluenceProp != null);
            Same("FsaResult несёт CascadeSummingRefusedFieldMatrix", true, summingProp != null);
            if (rp != null && ri != null)
            {
                Same("состав тот же: компонентов поровну", rp.Components.Count, ri.Components.Count);
                Same("Iso: кривая учтена", true, ri.EfficiencyUsed);
                if (fluenceProp != null)
                {
                    Same("Point: не поле", false, (bool)fluenceProp.GetValue(rp, null));
                    Same("Iso: поле", true, (bool)fluenceProp.GetValue(ri, null));
                }

                Console.WriteLine("  Point: компонентов {0}, χ²/ndf {1}; Iso: компонентов {2}, χ²/ndf {3}",
                                  rp.Components.Count, rp.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture),
                                  ri.Components.Count, ri.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture));

                // окно отчёта — те же строки, что видит человек
                string fieldRow = Resources.ResourceManager.GetString("FSAReportEfficiencyFieldCurve", CultureInfo.InvariantCulture);
                NotEmpty("ресурс FSAReportEfficiencyFieldCurve есть", fieldRow);
                Dictionary<string, string> rowsIso = QualityRows(ri);
                Dictionary<string, string> rowsPoint = QualityRows(rp);
                string effCaption = ReportText("FSAReport_EfficiencyRow");
                Same("окно, Iso: строка «Efficiency curve» — причина", fieldRow,
                     rowsIso.ContainsKey(effCaption) ? rowsIso[effCaption] : null);
                Same("окно, Point: строка «Efficiency curve» — как прежде «used»", ReportText("FSAReport_EfficiencyUsed"),
                     rowsPoint.ContainsKey(effCaption) ? rowsPoint[effCaption] : null);
                Console.WriteLine("  окно, Iso: «{0}» = «{1}»", effCaption,
                                  rowsIso.ContainsKey(effCaption) ? rowsIso[effCaption] : "(строки нет)");
            }

            // матрица сцены поля: суммирование не создаётся, причина — в строке окна
            FsaResult rm = null;
            try
            {
                rm = Fsa(sc, iso, FieldMatrix(), out am);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ⚠ плечо с матрицей поля не посчиталось: {0}", ex.GetType().Name + ": " + ex.Message);
            }

            Same("Iso + матрица поля: разбор есть", true, rm != null);
            if (rm != null && summingProp != null)
            {
                Same("Iso + матрица поля: образы по матрице", true, rm.ResponseMatrixUsed);
                Same("Iso + матрица поля: суммирование НЕ применено", false, rm.CascadeSummingUsed);
                Same("Iso + матрица поля: причина — матрица поля", true, (bool)summingProp.GetValue(rm, null));
                string summingRow = Resources.ResourceManager.GetString("FSAReportSummingFieldMatrix", CultureInfo.InvariantCulture);
                NotEmpty("ресурс FSAReportSummingFieldMatrix есть", summingRow);
                Dictionary<string, string> rows = QualityRows(rm);
                string sumCaption = ReportText("FSAReport_SummingRow");
                Same("окно, Iso + матрица: строка «Cascade summing» — причина", summingRow,
                     rows.ContainsKey(sumCaption) ? rows[sumCaption] : null);
            }

            Console.WriteLine();
        }

        static string ReportText(string key)
        {
            var rm = new System.ComponentModel.ComponentResourceManager(typeof(FSAReportView));
            return rm.GetString(key, CultureInfo.InvariantCulture) ?? key;
        }

        /// <summary>
        /// Строки блока качества — ТЕМ ЖЕ кодом окна (`FSAReportView.MakeQualityRows`),
        /// без показа окна: представление подкладывается отражением, как
        /// подкладывает результат `FsaStackShot`.
        /// </summary>
        static Dictionary<string, string> QualityRows(FsaResult result)
        {
            var rows = new Dictionary<string, string>(StringComparer.Ordinal);
            FsaPresentation presentation = FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, false);
            FsaReportRow quality = null;
            foreach (FsaReportRow row in presentation.Rows)
            {
                if (row.Kind == FsaReportRowKind.Quality)
                {
                    quality = row;
                }
            }

            if (quality == null)
            {
                return rows;
            }

            using (var view = new FSAReportView(null))
            {
                FieldInfo f = typeof(FSAReportView).GetField("presentation", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo m = typeof(FSAReportView).GetMethod("MakeQualityRows", BindingFlags.Instance | BindingFlags.NonPublic);
                if (f == null || m == null)
                {
                    throw new InvalidOperationException("нет FSAReportView.presentation / MakeQualityRows");
                }

                f.SetValue(view, presentation);
                var made = (List<Row>)m.Invoke(view, new object[] { quality });
                foreach (Row row in made)
                {
                    string name = row.Cells.Count > 1 && row.Cells[1] != null ? row.Cells[1].Text : "";
                    string value = row.Cells.Count > 2 && row.Cells[2] != null ? row.Cells[2].Text : "";
                    if (!string.IsNullOrEmpty(name) && !rows.ContainsKey(name))
                    {
                        rows[name] = value;
                    }
                }
            }

            return rows;
        }

        // ------------------------------------------------------------------
        // 5. подсадка точки 1.5 в кривую долей
        // ------------------------------------------------------------------

        static void Section5_Planted(EfficiencyConfigData point)
        {
            Console.WriteLine("=== 5. подсадка 1.5 в кривую долей: названа, не выброшена ===");
            EfficiencyConfigData planted = point.Copy();
            planted.Curve[2].Efficiency = 1.5;

            string refusal;
            FsaEfficiency curve = FromConfigNamed(planted, out refusal);
            Same("кривая с точкой 1.5 отвергнута целиком", null, curve);
            NotEmpty("причина названа", refusal);
            Contains("причина называет энергию точки", refusal, LineKev.ToString("0.##", CultureInfo.InvariantCulture));
            Contains("причина называет значение точки", refusal, "1.5");
            Console.WriteLine("  причина: «{0}»", refusal);

            // зона авто: «кривой нет» с названной причиной — сохранённый K, как у прежнего «кривой нет»
            BecquerelCoefficient.Result k = BecquerelCoefficient.Resolve(Zone(true), planted);
            Same("зона: K сохранённый (как «кривой нет»)", StoredK, k.Value);
            NotEmpty("зона: причина есть", k.Problem);
            Contains("зона: причина несёт точку", k.Problem, "1.5");
            Console.WriteLine("  зона: «{0}»", k.Problem);

            // выделение
            BecquerelCoefficient.LineResult line = BecquerelCoefficient.ForLine(LineKev, LineYield, planted);
            Same("выделение ForLine: причина — CurveRefused", "CurveRefused", line.Problem.ToString());
            Scene sc = Scene.Standard(); sc.Result.Efficiency = planted; sc.SetLabel("Line-662", LineKev, LineYield);
            Scene.Answer a = sc.Run();
            Same("выделение: числа нет", 0.0, a.Activity);
            NotEmpty("выделение: отказ назван", a.Refusal);
            Contains("выделение: отказ несёт точку", a.Refusal, "1.5");
            Console.WriteLine("  выделение: «{0}»", a.Refusal);

            // FSA: кривой нет → гейт геометрии; слова — у сессии (`FSACurveRefused`), ресурс обязан быть
            NotEmpty("ресурс FSACurveRefused есть", Resources.ResourceManager.GetString("FSACurveRefused", CultureInfo.InvariantCulture));

            // нормировка спектра: причина с точкой, а не «не выбрана» (доделка П79, находка 2)
            string norm = NormalizeRefusalOf(planted);
            NotEmpty("нормировка: отказ назван", norm);
            Contains("нормировка: отказ несёт точку", norm, "1.5");
            Same("нормировка: это не «кривая не выбрана»", false,
                 string.Equals(norm, Resources.ResourceManager.GetString("NormalizeNoCurve", CultureInfo.InvariantCulture), StringComparison.Ordinal));
            Console.WriteLine("  нормировка: «{0}»", norm);

            // кривая поля с тем же значением 1.5 — норма, фильтра нет
            EfficiencyConfigData isoLow = IsoCurve();
            isoLow.Curve[2].Efficiency = 1.5;
            string none;
            Same("кривая поля с точкой 1.5 строится", true, FromConfigNamed(isoLow, out none) != null);
            Same("кривая поля: причины нет", null, none);
            Console.WriteLine();
        }

        /// <summary>`SpectrumAriphmetics.NormalizeRefusal` — отражением: на старой сборке метода нет.</summary>
        static string NormalizeRefusalOf(EfficiencyConfigData config)
        {
            MethodInfo m = typeof(SpectrumAriphmetics).GetMethod("NormalizeRefusal", BindingFlags.Public | BindingFlags.Static);
            if (m == null)
            {
                bad++;
                Console.WriteLine("  {0,-64} !! в сборке нет SpectrumAriphmetics.NormalizeRefusal", "предикат нормировки");
                return null;
            }

            return (string)m.Invoke(null, new object[] { config });
        }

        // ------------------------------------------------------------------
        // 6. нормировка спектра кривой: кривая поля — отказ словами (доделка П79, находка 3)
        // ------------------------------------------------------------------

        static void Section6_Normalize(EfficiencyConfigData point, EfficiencyConfigData iso)
        {
            Console.WriteLine("=== 6. нормировка спектра кривой: SpectrumAriphmetics.NormalizeRefusal / NormalizeSpectrum ===");

            // хранение — int[], и это то, из-за чего кривой поля отказано
            PropertyInfo spectrumProp = typeof(EnergySpectrum).GetProperty("Spectrum");
            Same("спектр хранится int[]", typeof(int[]), spectrumProp != null ? spectrumProp.PropertyType : null);
            int q5 = Convert.ToInt32(5.0 / 13.82);
            int q200 = Convert.ToInt32(200.0 / 13.82);
            Console.WriteLine("  int-квантование при A = 13.82 см²: 5 отсч. → {0} (точно {1}), 200 отсч. → {2} (точно {3})",
                              q5, (5.0 / 13.82).ToString("F3", CultureInfo.InvariantCulture),
                              q200, (200.0 / 13.82).ToString("F2", CultureInfo.InvariantCulture));
            Same("5 отсчётов делением на 13.82 см² уходят в ноль", 0, q5);

            Scene sc = Scene.Standard();
            int centre = (int)Math.Round(LineKev);

            // Point: нормируется, причины нет, канал 662 = counts/ε(662)
            Same("Point: причины нет", null, NormalizeRefusalOf(point));
            EnergySpectrum np = SpectrumAriphmetics.NormalizeSpectrum(sc.Fg, point);
            double eps, err;
            FsaEfficiency pc = FsaEfficiency.FromConfig(point);
            if (pc != null && pc.TryEval(sc.Fg.EnergyCalibration.ChannelToEnergy(centre), out eps, out err))
            {
                Near("Point: канал 662 = counts/ε", Math.Round(sc.Fg.Spectrum[centre] / eps), np.Spectrum[centre], 1e-9);
                Console.WriteLine("  Point: канал {0}: {1} отсч. / ε {2} = {3}", centre, sc.Fg.Spectrum[centre],
                                  eps.ToString("G6", CultureInfo.InvariantCulture), np.Spectrum[centre]);
            }

            // Iso: отказ словами, спектр оставлен как есть (не поделён на см²)
            string refusal = NormalizeRefusalOf(iso);
            NotEmpty("Iso: отказ назван", refusal);
            Contains("Iso: отказ называет кривую", refusal, iso.Name);
            string fieldText = Resources.ResourceManager.GetString("NormalizeFieldCurve", CultureInfo.InvariantCulture);
            NotEmpty("ресурс NormalizeFieldCurve есть", fieldText);
            if (fieldText != null)
            {
                Same("Iso: отказ — NormalizeFieldCurve", string.Format(CultureInfo.InvariantCulture, fieldText, iso.Name), refusal);
            }

            EnergySpectrum ni = SpectrumAriphmetics.NormalizeSpectrum(sc.Fg, iso);
            Same("Iso: спектр не поделён на см² (канал 662 как есть)", sc.Fg.Spectrum[centre], ni.Spectrum[centre]);
            Same("Iso: спектр не поделён на см² (канал 100 как есть)", sc.Fg.Spectrum[100], ni.Spectrum[100]);
            Console.WriteLine("  Iso: «{0}»", refusal);

            // кривой нет — своя причина про нормировку, не про K зоны
            string noCurve = Resources.ResourceManager.GetString("NormalizeNoCurve", CultureInfo.InvariantCulture);
            NotEmpty("ресурс NormalizeNoCurve есть", noCurve);
            Same("кривой нет: причина — NormalizeNoCurve", noCurve, NormalizeRefusalOf(null));
            Console.WriteLine();
        }

        // ------------------------------------------------------------------
        // сцена — вид поднимается без окна, как в `BqActivityProbe`
        // ------------------------------------------------------------------

        class Scene
        {
            static readonly Type TView = typeof(EnergySpectrumView);
            static readonly Type TAn = TView.GetNestedType("SelectionAnalytics", BindingFlags.NonPublic);
            static readonly MethodInfo MEnsure = TView.GetMethod("EnsureSelectionAnalytics",
                BindingFlags.NonPublic | BindingFlags.Instance);

            public ResultData Result;
            public EnergySpectrum Fg, Bg;
            public double FgTime = 1000.0, BgTime = 2000.0;
            public double FgCounts, BgCounts, NetCounts;
            public int StartChannel, EndChannel;
            PolynomialEnergyCalibration cal;
            Peak peak;

            public class Answer
            {
                public double Activity;
                public string Label, Refusal;
            }

            public static Scene Standard()
            {
                var sc = new Scene();
                sc.cal = new PolynomialEnergyCalibration();
                sc.cal.PolynomialOrder = 1;
                sc.cal.Coefficients = new double[] { 0.0, 1.0 };

                int channels = 4096;
                int centre = (int)Math.Round(LineKev);
                double sigma = 12.0;
                int[] fgArr = new int[channels];
                int[] bgArr = new int[channels];
                for (int i = 0; i < channels; i++)
                {
                    double gauss = 40000.0 * Math.Exp(-0.5 * Math.Pow((i - centre) / sigma, 2.0));
                    fgArr[i] = 200 + (int)Math.Round(gauss);
                    bgArr[i] = 300;
                }

                sc.Fg = MakeSpectrum(fgArr, sc.cal, sc.FgTime);
                sc.Bg = MakeSpectrum(bgArr, sc.cal, sc.BgTime);
                sc.StartChannel = centre - 40;
                sc.EndChannel = centre + 40;
                for (int i = sc.StartChannel; i <= sc.EndChannel; i++)
                {
                    sc.FgCounts += fgArr[i];
                    sc.BgCounts += bgArr[i];
                }

                sc.NetCounts = sc.FgCounts - sc.BgCounts * sc.FgTime / sc.BgTime;

                sc.peak = new Peak
                {
                    Energy = LineKev,
                    Channel = centre,
                    Count = 40000,
                    FWHM = 2.354820045 * sigma,
                    SNR = 100.0,
                };

                sc.Result = new ResultData
                {
                    EnergySpectrum = sc.Fg,
                    BackgroundEnergySpectrum = sc.Bg,
                    Visible = true,
                };
                sc.Result.SampleInfo.Weight = 1.0;
                sc.Result.SampleInfo.Volume = 1.0;
                sc.Result.DetectedPeaks.Add(sc.peak);
                return sc;
            }

            static EnergySpectrum MakeSpectrum(int[] data, EnergyCalibration cal, double time)
            {
                var s = new EnergySpectrum();
                s.NumberOfChannels = data.Length;
                s.Spectrum = data;
                s.EnergyCalibration = cal;
                s.MeasurementTime = time;
                s.LiveTime = time;
                long total = 0;
                for (int i = 0; i < data.Length; i++) total += data[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
                return s;
            }

            public void SetLabel(string name, double lineKev, double intensity)
            {
                this.peak.Nuclide = new NuclideDefinition
                {
                    Name = name,
                    Energy = lineKev,
                    Intencity = intensity,
                    Visible = true,
                    Sets = new HashSet<Guid>(),
                };
            }

            public Answer Run()
            {
                object view = FormatterServices.GetUninitializedObject(TView);
                Set(view, "energySpectrum", this.Fg);
                Set(view, "backgroundEnergySpectrum", this.Bg);
                Set(view, "substractedEnergySpectrum", null);
                Set(view, "normByEffEnergySpectrum", null);
                Set(view, "energyCalibration", this.cal);
                Set(view, "baseEnergyCalibration", this.cal);
                Set(view, "backgroundEnergyCalibration", this.cal);
                Set(view, "backgroundNumberOfChannels", this.Bg == null ? 0 : this.Bg.NumberOfChannels);
                Set(view, "selectionStart", this.StartChannel);
                Set(view, "selectionEnd", this.EndChannel);
                Set(view, "peakMode", PeakMode.Visible);
                Set(view, "backgroundMode", BackgroundMode.Invisible);
                Set(view, "activeResultData", this.Result);
                Set(view, "globalConfigManager", Config());
                // Менеджер нуклидов — null: спор подписи (`ScanActivityRivals`)
                // без него молчит, а библиотеки этой пробе не нужно.
                Set(view, "nuclideManager", null);
                Set(view, "selectionAnalyticsDirty", true);
                Set(view, "selectionAnalytics", null);
                Set(view, "selectionFWHM", 0.0);

                MEnsure.Invoke(view, null);

                object an = TView.GetField("selectionAnalytics",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
                var a = new Answer();
                if (an == null)
                {
                    throw new InvalidOperationException("аналитика выделения не построена");
                }

                a.Activity = (double)Get(an, "Activity");
                a.Label = (string)Get(an, "ActivityLabel");
                a.Refusal = (string)Get(an, "ActivityRefusal");
                return a;
            }

            static GlobalConfigManager Config()
            {
                var m = new GlobalConfigManager();
                var c = new GlobalConfigInfo();
                if (c.ColorConfig != null &&
                    (c.ColorConfig.SpectrumColorList == null || c.ColorConfig.SpectrumColorList.Count == 0))
                {
                    c.ColorConfig.InitializeSpectrumColor();
                }

                m.GlobalConfig = c;
                return m;
            }

            static void Set(object target, string field, object value)
            {
                FieldInfo f = TView.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f == null) throw new InvalidOperationException("нет поля EnergySpectrumView." + field);
                f.SetValue(target, value);
            }

            static object Get(object an, string prop)
            {
                PropertyInfo p = TAn.GetProperty(prop);
                if (p == null) throw new InvalidOperationException("нет свойства SelectionAnalytics." + prop);
                return p.GetValue(an, null);
            }
        }

        // ------------------------------------------------------------------
        // сверки
        // ------------------------------------------------------------------

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0,-64} {1} {2}{3}", what, ok ? "=" : "!!", got ?? "null",
                              ok ? "" : string.Format(CultureInfo.InvariantCulture, " вместо {0}", expected ?? "null"));
            if (!ok) bad++;
        }

        static void Near(string what, double expected, double got, double tolerance)
        {
            bool ok = Math.Abs(got - expected) <= tolerance * Math.Max(1.0, Math.Abs(expected));
            Console.WriteLine("  {0,-64} {1} {2}{3}", what, ok ? "=" : "!!",
                              got.ToString("G8", CultureInfo.InvariantCulture),
                              ok ? "" : " вместо " + expected.ToString("G8", CultureInfo.InvariantCulture));
            if (!ok) bad++;
        }

        static void NotEmpty(string what, string value)
        {
            bool ok = !string.IsNullOrEmpty(value);
            Console.WriteLine("  {0,-64} {1} {2}", what, ok ? "=" : "!!", ok ? "названа" : "ПУСТО");
            if (!ok) bad++;
        }

        static void Contains(string what, string text, string needle)
        {
            bool ok = text != null && needle != null && text.IndexOf(needle, StringComparison.Ordinal) >= 0;
            Console.WriteLine("  {0,-64} {1} {2}", what, ok ? "=" : "!!", ok ? "есть «" + needle + "»" : "нет «" + needle + "» в «" + (text ?? "null") + "»");
            if (!ok) bad++;
        }
    }
}
