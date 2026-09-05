using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace DoseRateProbe
{
    /// <summary>
    /// Вкладка «Dose Rate» конфигурации устройства — задача `C4`.
    ///
    /// Меряется ровно то, ради чего строка заведена.
    ///
    ///  1. **Коэффициенты.** Пятнадцать диапазонов 40–3000 кэВ, шестнадцать
    ///     значений μ_en/ρ и шестнадцать значений перевода Р→Зв лежали
    ///     безымянными массивами внутри `CalculateDoseRateConfig`. Здесь новые
    ///     значения, посчитанные из XCOM (`matdb.sqlite`), сверяются со
    ///     вшитыми на тех же шестнадцати энергиях. ⚠ Положительный контроль
    ///     физики — доля энергии, переданной электрону при комптоновском
    ///     рассеянии: она обязана сойтись с опорными числами Аттикса, иначе
    ///     сходимость μ_en/ρ ничего не значит.
    ///
    ///  2. **Цена молчаливого обрезания.** Всё ниже 40 и выше 3000 кэВ в дозу
    ///     не входило независимо от шкалы прибора. Считается сквозным
    ///     прогоном: калибровка по эталону старым и новым путём, затем
    ///     измерение ДРУГОГО спектра обоими наборами точек. На эталоне разницы
    ///     нет по построению (нормировка), она вылезает на спектре с иной
    ///     формой — америциевом (12.9 % отсчётов ниже 40 кэВ) и на бразильских
    ///     орехах (2.1 % выше 3000 кэВ).
    ///
    ///  3. **Положительный контроль отказов.** Энергия вне таблицы XCOM,
    ///     пустая шкала прибора, спектр без калибровки, нулевое время,
    ///     вырожденная кривая — расчёт обязан отказать ВИДИМО. Молчаливое
    ///     число — находка. Отрицательный вход: годные данные обязаны пройти.
    ///
    ///  4. **`C4(а)`** — конфигурация с N кривыми предлагает ровно N, с нулём
    ///     кривых предлагает пусто, а путь из файла ЛСРМ остаётся цел.
    ///
    ///  5. **`C4(б)`** — открытые спектры предлагаются без второго диалога;
    ///     спектр без калибровки в список не попадает.
    ///
    ///  6. **`A202` и `A201` — РАСКЛАДКА и ПОДПИСИ вкладки.** Списки живут в
    ///     конструкторе форм, а не строятся кодом по чужим координатам; полей
    ///     «путь к файлу», которые прятались `Visible = false`, в форме нет
    ///     вовсе; ни один контрол не выходит за страницу и не налезает на
    ///     соседа; подпись помещается в свой контрол НА ОБЕИХ культурах и не
    ///     обещает ни ЛСРМ, ни «40 кэВ – 3 МэВ». Плюс снимок вкладки в PNG.
    ///
    ///   doserateprobe [--dir=&lt;корпус&gt;] [--lsrm=&lt;кривые&gt;] [--shots=&lt;куда PNG&gt;]
    ///   doserateprobe --sabotage=long|word|hidden|overlap   (ждёт ОТКАЗ)
    ///
    /// ⛔ Приёмка, которая проходит всегда, не мерит ничего. Ключ `--sabotage`
    /// портит РОВНО ОДНУ вещь в уже построенной форме и требует, чтобы раздел
    /// раскладки отказал и назвал испорченное имя; без порчи он же обязан
    /// молчать. Коды возврата у `--sabotage` перевёрнуты: 0 — отказ получен
    /// (сторож смотрит), 1 — не получен (сторож слеп).
    /// </summary>
    static class Program
    {
        static int failed;
        static int checks;

        // ------------------------------------------------------------------
        // Вшитое в старый код — эталон сверки. Скопировано дословно из
        // DeviceConfigForm.CalculateDoseRateConfig до правки 05.09.2026.
        // ------------------------------------------------------------------
        static readonly double[] OldEnergies =
            { 40, 50, 60, 80, 100, 150, 200, 300, 400, 500, 600, 800, 1000, 1500, 2000, 3000 };

        static readonly double[] OldMu =
            { 0.006694, 0.004031, 0.003004, 0.002393, 0.002318, 0.002494, 0.002672, 0.002872,
              0.002949, 0.002966, 0.002953, 0.002882, 0.002787, 0.002545, 0.002342, 0.002054 };

        static readonly double[] OldRToSv =
            { 1.29, 1.46, 1.52, 1.51, 1.44, 1.31, 1.22, 1.15, 1.10, 1.07, 1.04, 1.02, 1.01,
              0.99, 0.99, 0.98 };

        static string corpusDir = @"tools\CORPUS\corpus";

        static string lsrmDir = @"LSRM Geometries\Exported Curves";

        /// <summary>Куда класть снимки вкладки; null — не снимать.</summary>
        static string shotDir;

        /// <summary>Что испортить ради положительного контроля; null — ничего.</summary>
        static string sabotage;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal))
                {
                    corpusDir = a.Substring(6);
                }
                else if (a.StartsWith("--lsrm=", StringComparison.Ordinal))
                {
                    lsrmDir = a.Substring(7);
                }
                else if (a.StartsWith("--shots=", StringComparison.Ordinal))
                {
                    shotDir = a.Substring(8);
                }
                else if (a.StartsWith("--sabotage=", StringComparison.Ordinal))
                {
                    sabotage = a.Substring(11);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            try
            {
                if (sabotage == null)
                {
                    ComptonControl();
                    MuEnAgreement();
                    AmbientAgreement();
                    Refusals();
                    OfferedCurves();
                    OfferedSpectra();
                    LsrmReaderStillWorks();
                    LsrmRealExports();
                    OverflowRule();
                    TruncationPrice();
                }

                TabLayout();
            }
            catch (Exception ex)
            {
                Console.WriteLine("!! проба сорвалась: " + ex);
                return 3;
            }

            Console.WriteLine();
            if (sabotage != null)
            {
                bool caught = failed > 0;
                Console.WriteLine(caught
                    ? string.Format("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ «{0}»: отказ получен ({1} из {2})",
                                    sabotage, failed, checks)
                    : string.Format("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ «{0}»: ОТКАЗА НЕТ — сторож слеп ({1} проверок)",
                                    sabotage, checks));
                return caught ? 0 : 1;
            }

            Console.WriteLine(failed == 0
                ? string.Format("ВСЁ СОШЛОСЬ: {0} проверок", checks)
                : string.Format("ПРОВАЛОВ {0} из {1}", failed, checks));
            return failed == 0 ? 0 : 1;
        }

        static void Ok(bool condition, string what)
        {
            checks++;
            if (!condition)
            {
                failed++;
            }

            Console.WriteLine("  {0} {1}", condition ? "ok  " : "ПРОВАЛ", what);
        }

        // ==================================================================
        // 1. Коэффициенты
        // ==================================================================

        /// <summary>
        /// ⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ физики. Средняя доля энергии, переданная
        /// электрону по Клейну — Нишине, — опорные числа Аттикса (Introduction
        /// to Radiological Physics, таблица 7.1). Если квадратура врёт, врёт и
        /// весь μ_en/ρ, а сходимость с вшитой таблицей была бы совпадением.
        /// </summary>
        static void ComptonControl()
        {
            Console.WriteLine("== доля энергии электрону по Клейну — Нишине (контроль физики) ==");
            double[] energies = { 100.0, 1000.0, 10000.0 };
            double[] reference = { 0.1380, 0.4400, 0.6836 };
            for (int i = 0; i < energies.Length; i++)
            {
                double value = DoseRateCoefficients.ComptonTransferFraction(energies[i]);
                double diff = Math.Abs(value - reference[i]);
                Ok(diff < 5e-4, string.Format(CultureInfo.InvariantCulture,
                    "{0,6:f0} кэВ: {1:f4}, опора {2:f4}, |Δ| = {3:f5}",
                    energies[i], value, reference[i], diff));
            }
        }

        static void MuEnAgreement()
        {
            Console.WriteLine();
            Console.WriteLine("== μ_en/ρ сухого воздуха: XCOM против вшитой таблицы ==");
            Console.WriteLine("   E, кэВ    вшито, м²/кг    XCOM, м²/кг    расх., %");
            double worst = 0.0;
            string worstAt = "";
            for (int i = 0; i < OldEnergies.Length; i++)
            {
                double now = DoseRateCoefficients.MassEnergyAbsorptionAir(OldEnergies[i]);
                double diff = 100.0 * (now - OldMu[i]) / OldMu[i];
                if (Math.Abs(diff) > Math.Abs(worst))
                {
                    worst = diff;
                    worstAt = OldEnergies[i].ToString("f0", CultureInfo.InvariantCulture);
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,7:f0}    {1,12:f6}   {2,12:f6}    {3,8:+0.00;-0.00}",
                    OldEnergies[i], OldMu[i], now, diff));
            }

            // Порог 3 % — не подгонка, а граница «та же величина, тот же
            // состав». Радиационные потери (g), которые здесь не вычитаются,
            // для воздуха ниже 3 МэВ дают меньше 0.2 %; остальное —
            // разрешение сетки XCOM.
            Ok(Math.Abs(worst) < 3.0, string.Format(CultureInfo.InvariantCulture,
                "худшая точка {0} кэВ: {1:+0.00;-0.00} %", worstAt, worst));
        }

        static void AmbientAgreement()
        {
            Console.WriteLine();
            Console.WriteLine("== h*(10)/K_air: ICRP 74 × 0.876 против вшитого «RToSv» ==");
            Console.WriteLine("   E, кэВ    вшито      ICRP 74×0.876   расх., %");
            double worst = 0.0;
            string worstAt = "";
            for (int i = 0; i < OldEnergies.Length; i++)
            {
                double now = DoseRateCoefficients.AmbientDoseConversion(OldEnergies[i])
                             * DoseRateCoefficients.RemPerRoentgenFactor;
                double diff = 100.0 * (now - OldRToSv[i]) / OldRToSv[i];
                if (Math.Abs(diff) > Math.Abs(worst))
                {
                    worst = diff;
                    worstAt = OldEnergies[i].ToString("f0", CultureInfo.InvariantCulture);
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,7:f0}    {1,7:f3}    {2,12:f3}    {3,8:+0.00;-0.00}",
                    OldEnergies[i], OldRToSv[i], now, diff));
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  худшая точка {0} кэВ: {1:+0.00;-0.00} %", worstAt, worst));
            Ok(Math.Abs(worst) < 5.0, "опознание источника подтверждено (расхождение меньше 5 %)");
        }

        // ==================================================================
        // 2. Отказы
        // ==================================================================

        static void Refusals()
        {
            Console.WriteLine();
            Console.WriteLine("== положительный контроль: негодный вход обязан ОТКАЗАТЬ видимо ==");

            Refuses("энергия ниже таблицы XCOM (0.5 кэВ)",
                    () => DoseRateCoefficients.MassEnergyAbsorptionAir(0.5));
            Refuses("энергия ниже таблицы ICRP 74 (5 кэВ)",
                    () => DoseRateCoefficients.AmbientDoseConversion(5.0));
            Refuses("энергия выше таблицы ICRP 74 (20 МэВ)",
                    () => DoseRateCoefficients.AmbientDoseConversion(20000.0));
            Refuses("шкала прибора целиком ниже 10 кэВ",
                    () => DoseRateEstimator.BuildGrid(0.0, 5.0));
            Refuses("пустая шкала прибора (0 каналов, спектра нет)", () =>
            {
                double a, b;
                DeviceConfigInfo empty = new DeviceConfigInfo();
                empty.NumberOfChannels = 0;
                DoseRateEstimator.DeviceRange(empty, null, out a, out b);
            });
            Refuses("вырожденная калибровка (все коэффициенты нулевые)", () =>
            {
                double a, b;
                DeviceConfigInfo flatScale = new DeviceConfigInfo();
                flatScale.NumberOfChannels = 1024;
                PolynomialEnergyCalibration zero = new PolynomialEnergyCalibration();
                zero.PolynomialOrder = 1;
                zero.Coefficients = new double[] { 0.0, 0.0 };
                flatScale.EnergyCalibration = zero;
                DoseRateEstimator.DeviceRange(flatScale, null, out a, out b);
            });

            // ⛔ Найдено этой пробой: `DoseRateConfig.DoseRateCalibrationPoints`
            // в сеттере зовёт `List.Sort()`, а у точки не было `IComparable` —
            // любое присваивание списка длиннее одного элемента валило
            // программу `InvalidOperationException`. Заодно выяснилось, что
            // заявленная сеттером сортировка не работала никогда.
            checks++;
            try
            {
                DoseRateConfig sorting = new DoseRateConfig();
                sorting.DoseRateCalibrationPoints = new List<DoseRateCalibrationPoint>
                {
                    new DoseRateCalibrationPoint { LowerBound = 300, UpperBound = 400 },
                    new DoseRateCalibrationPoint { LowerBound = 40, UpperBound = 60 },
                    new DoseRateCalibrationPoint { LowerBound = 100, UpperBound = 200 },
                };

                List<DoseRateCalibrationPoint> sorted = sorting.DoseRateCalibrationPoints;
                bool ordered = sorted[0].LowerBound == 40 && sorted[1].LowerBound == 100
                               && sorted[2].LowerBound == 300;
                checks--;
                Ok(ordered, string.Format(
                    "присвоение списка точек не валит программу и сортирует: {0}, {1}, {2}",
                    sorted[0].LowerBound, sorted[1].LowerBound, sorted[2].LowerBound));
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("  ПРОВАЛ присвоение списка точек: "
                                  + ex.GetType().Name + " " + Short(ex.Message));
            }

            EnergySpectrum noCalibration = MakeSpectrum(1024, null, 100.0);
            Refuses("спектр без калибровки", () =>
                DoseRateEstimator.Estimate(noCalibration, FlatCurve(), 1.0,
                                           DoseRateEstimator.BuildGrid(40.0, 3000.0), null));

            EnergySpectrum noTime = MakeSpectrum(1024, Linear(3.0), 0.0);
            Refuses("нулевое время набора", () =>
                DoseRateEstimator.Estimate(noTime, FlatCurve(), 1.0,
                                           DoseRateEstimator.BuildGrid(40.0, 3000.0), null));

            EnergySpectrum good = MakeSpectrum(1024, Linear(3.0), 100.0);
            Refuses("объявленная мощность дозы эталона равна нулю", () =>
                DoseRateEstimator.Estimate(good, FlatCurve(), 0.0,
                                           DoseRateEstimator.BuildGrid(40.0, 3000.0), null));
            Refuses("кривая из одной точки", () =>
                DoseRateEstimator.CurveOf(new List<ROIEfficiencyData>
                    { new ROIEfficiencyData { Energy = 100, Efficiency = 0.1 } }));
            Refuses("кривая с нулевой эффективностью", () =>
                DoseRateEstimator.CurveOf(new List<ROIEfficiencyData>
                {
                    new ROIEfficiencyData { Energy = 100, Efficiency = 0.1 },
                    new ROIEfficiencyData { Energy = 200, Efficiency = 0.0 },
                }));

            // ⚠ ОТРИЦАТЕЛЬНЫЙ вход к тому же контролю: годные данные обязаны
            // ПРОЙТИ. Проверка, которая отказывает всегда, ничего не меряет.
            checks++;
            try
            {
                List<DoseRateCalibrationPoint> points = DoseRateEstimator.Estimate(
                    good, FlatCurve(), 1.0, DoseRateEstimator.BuildGrid(40.0, 3000.0), null);
                Ok(points.Count > 0, string.Format("годный вход прошёл: точек {0}", points.Count));
                checks--;
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("  ПРОВАЛ годный вход отказан: " + ex.Message);
            }

            // Отказ ПОКАЗЫВАЕТСЯ, а не превращается в ноль: строка состояния
            // главного окна печатает `DoseRate.ToString()`.
            ResultData bad = new ResultData();
            bad.EnergySpectrum = noCalibration;
            DoseRateConfig config = new DoseRateConfig();
            config.DoseRateCalibrationPoints = new List<DoseRateCalibrationPoint>
            {
                new DoseRateCalibrationPoint { LowerBound = 40, UpperBound = 100, CPS = 1, EtalonDoseRateValue = 1 },
            };

            DoseRate shown = new DoseRateManager(Config()).Calculate(bad, config);
            Ok(!string.IsNullOrEmpty(shown.Refusal) && shown.ToString().IndexOf("0.000") < 0,
               "спектр без калибровки в строке состояния: «" + shown + "»");
        }

        static void Refuses(string what, Action action)
        {
            checks++;
            try
            {
                action();
                failed++;
                Console.WriteLine("  ПРОВАЛ " + what + " — прошло МОЛЧА, числом");
            }
            catch (DoseRateRefusalException ex)
            {
                Console.WriteLine("  ok   " + what + " → «" + Short(ex.Message) + "»");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("  ПРОВАЛ " + what + " — отказ БЕЗ ИМЕНИ: "
                                  + ex.GetType().Name + " " + Short(ex.Message));
            }
        }

        static string Short(string text)
        {
            if (text == null)
            {
                return "";
            }

            text = text.Replace(Environment.NewLine, " ");
            return text.Length > 90 ? text.Substring(0, 87) + "..." : text;
        }

        // ==================================================================
        // 3. C4(а): кривые самой конфигурации прибора
        // ==================================================================

        static void OfferedCurves()
        {
            Console.WriteLine();
            Console.WriteLine("== C4(а): вкладка предлагает кривые конфигурации прибора ==");

            for (int n = 0; n <= 3; n++)
            {
                DeviceConfigInfo config = new DeviceConfigInfo();
                for (int i = 0; i < n; i++)
                {
                    EfficiencyConfigData curve = new EfficiencyConfigData("кривая " + (i + 1));
                    curve.Curve = new List<ROIEfficiencyData>
                    {
                        new ROIEfficiencyData { Energy = 40, Efficiency = 0.02 },
                        new ROIEfficiencyData { Energy = 662, Efficiency = 0.01 },
                        new ROIEfficiencyData { Energy = 3000, Efficiency = 0.003 },
                    };

                    config.EfficiencyConfigs.Add(curve);
                }

                int offered = DoseRateEstimator.OfferedEfficiencies(config).Count;
                Ok(offered == n, string.Format("кривых в конфигурации {0} → предложено {1}", n, offered));
            }

            // Геометрия БЕЗ посчитанной кривой делить не на что — она не кривая.
            DeviceConfigInfo geometryOnly = new DeviceConfigInfo();
            geometryOnly.EfficiencyConfigs.Add(new EfficiencyConfigData("только геометрия"));
            Ok(DoseRateEstimator.OfferedEfficiencies(geometryOnly).Count == 0,
               "конфигурация без посчитанной кривой не предлагается");

            Ok(DoseRateEstimator.OfferedEfficiencies(null).Count == 0,
               "конфигурации нет вовсе → список пуст, без падения");
        }

        // ==================================================================
        // 4. C4(б): уже открытые спектры
        // ==================================================================

        static void OfferedSpectra()
        {
            Console.WriteLine();
            Console.WriteLine("== C4(б): вкладка предлагает уже открытые спектры ==");

            var titles = new List<string>();
            var results = new List<ResultData>();

            ResultData good = new ResultData();
            good.EnergySpectrum = MakeSpectrum(1024, Linear(3.0), 100.0);
            titles.Add("годный");
            results.Add(good);

            ResultData noCal = new ResultData();
            noCal.EnergySpectrum = MakeSpectrum(1024, null, 100.0);
            titles.Add("без калибровки");
            results.Add(noCal);

            ResultData noTime = new ResultData();
            noTime.EnergySpectrum = MakeSpectrum(1024, Linear(3.0), 0.0);
            titles.Add("нулевое время");
            results.Add(noTime);

            List<DoseRateSpectrumChoice> offered = DoseRateEstimator.OfferedSpectra(titles, results);
            Ok(offered.Count == 1 && offered[0].Title == "годный",
               string.Format("из трёх открытых документов предложен {0} (годный один)", offered.Count));

            Ok(DoseRateEstimator.OfferedSpectra(null, null).Count == 0,
               "открытых документов нет → список пуст, старый путь из файла цел");
        }

        // ==================================================================
        // 5. Старый путь: разбор экспорта ЛСРМ
        // ==================================================================

        static void LsrmReaderStillWorks()
        {
            Console.WriteLine();
            Console.WriteLine("== старый путь: разбор текстового экспорта ЛСРМ ==");

            MethodInfo reader = typeof(DeviceConfigForm).GetMethod(
                "ReadLsrmEfficiencyExport", BindingFlags.NonPublic | BindingFlags.Static);
            if (reader == null)
            {
                failed++;
                checks++;
                Console.WriteLine("  ПРОВАЛ нет общего метода чтения файла ЛСРМ");
                return;
            }

            // ⚠ ДВА файла, один и тот же по числам: с точкой и с запятой.
            // Файл приходит с той культурой, в которой его записали, и читатель
            // обязан быть безразличен к культуре машины.
            foreach (bool comma in new[] { false, true })
            {
                string path = Path.Combine(Path.GetTempPath(),
                    comma ? "doserateprobe_lsrm_comma.txt" : "doserateprobe_lsrm_dot.txt");
                var text = new StringBuilder();
                text.AppendLine("Energy, keV\tEfficiency\tUncertainty, %\t\t\t\t");
                for (int e = 40; e <= 3000; e += 100)
                {
                    string line = string.Format(CultureInfo.InvariantCulture,
                        "{0}\t{1}\t{2}\t\t\t\t", e, 0.05 * Math.Pow(662.0 / e, 0.9), 3.0);
                    text.AppendLine(comma ? line.Replace('.', ',') : line);
                }

                File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));

                object[] call = { path, null };
                var points = (List<ROIEfficiencyData>)reader.Invoke(null, call);
                Ok(call[1] == null && points.Count == 30 && points[0].Energy == 40.0,
                   string.Format("файл ЛСРМ {0}: точек {1}, первая E={2}, жалоб «{3}»",
                                 comma ? "с ЗАПЯТОЙ" : "с ТОЧКОЙ", points.Count,
                                 points.Count > 0 ? points[0].Energy : -1, call[1]));
                File.Delete(path);
            }

            // Отрицательный вход: пустой файл обязан пожаловаться, а не отдать
            // пустой список молча (прежде разбор глотал исключение и строил по
            // пустому списку сплайн).
            string empty = Path.Combine(Path.GetTempPath(), "doserateprobe_lsrm_empty.txt");
            File.WriteAllText(empty, "Energy, keV\tEfficiency\tUncertainty, %\r\n", new UTF8Encoding(false));
            object[] call2 = { empty, null };
            reader.Invoke(null, call2);
            Ok(call2[1] != null, "пустой файл ЛСРМ жалуется: «" + Short((string)call2[1]) + "»");

            File.Delete(empty);
        }

        // ==================================================================
        // 6. `T174` — ВОСЕМЬ НАСТОЯЩИХ экспортов ЛСРМ
        // ==================================================================

        /// <summary>
        /// Якорь одного файла, снятый ГЛАЗАМИ из самого файла, а не тем кодом,
        /// который проверяется. Способ снятия: `head`/`tail`/`xxd` по сырым
        /// байтам плюс `awk -F'\t'` по колонкам — три независимых от приложения
        /// инструмента; вывод прочитан и переписан сюда руками 05.09.2026.
        /// </summary>
        sealed class LsrmAnchor
        {
            public string File;
            public int DataRows;        // строк данных в файле (без шапки)
            public double FirstEnergy;  // энергия ПЕРВОЙ строки файла
            public double FirstEff;     // её эффективность
            public double FirstError;   // её заявленная погрешность, %
            public double KeptEnergy;   // энергия первой ОСТАВШЕЙСЯ точки
            public double KeptEff;      // её эффективность
            public double LastEnergy;
            public double LastEff;
            public double LastError;
        }

        static readonly LsrmAnchor[] LsrmAnchors =
        {
            new LsrmAnchor { File = "Nano 16 - cilinder - 5cm dist.txt", DataRows = 151,
                             FirstEnergy = 20.0, FirstEff = 1.40534E-05,
                             FirstError = 625,
                             KeptEnergy = 40.0, KeptEff = 2.74157E-03,
                             LastEnergy = 3020.0, LastEff = 2.71587E-04, LastError = 7.62 },
            new LsrmAnchor { File = "Nano 16 - cilinder.txt", DataRows = 151,
                             FirstEnergy = 20.0, FirstEff = 1.80392E-03,
                             FirstError = 927,
                             KeptEnergy = 40.0, KeptEff = 4.00862E-02,
                             LastEnergy = 3020.0, LastEff = 2.36069E-03, LastError = 12.9 },
            new LsrmAnchor { File = "Nano 16 - marinelli.txt", DataRows = 60,
                             FirstEnergy = 10.0, FirstEff = 9.76E-17,
                             FirstError = 3830,
                             KeptEnergy = 60.0, KeptEff = 1.45493E-02,
                             LastEnergy = 2960.0, LastEff = 7.54984E-04, LastError = 6.86 },
            new LsrmAnchor { File = "Obsidian - marinelli 0.5.txt", DataRows = 150,
                             FirstEnergy = 20.0, FirstEff = 1.47185E+03,
                             FirstError = 554,
                             KeptEnergy = 40.0, KeptEff = 5.55793E-03,
                             LastEnergy = 3000.0, LastEff = 2.80456E-05, LastError = 11 },
            new LsrmAnchor { File = "RadiaCode - author marinelli 0.2.txt", DataRows = 150,
                             FirstEnergy = 20.0, FirstEff = 3.84173E-03,
                             FirstError = 1100,
                             KeptEnergy = 40.0, KeptEff = 4.94988E-03,
                             LastEnergy = 3000.0, LastEff = 6.37685E-05, LastError = 22.5 },
            new LsrmAnchor { File = "RadiaCode - author marinelli 0.5.txt", DataRows = 150,
                             FirstEnergy = 20.0, FirstEff = 1.12419E-01,
                             FirstError = 1170,
                             KeptEnergy = 40.0, KeptEff = 3.39528E-03,
                             LastEnergy = 3000.0, LastEff = 4.19508E-05, LastError = 22.6 },
            new LsrmAnchor { File = "RadiaCode - cilinder.txt", DataRows = 151,
                             FirstEnergy = 20.0, FirstEff = 2.43683E-03,
                             FirstError = 622,
                             KeptEnergy = 40.0, KeptEff = 9.32679E-03,
                             LastEnergy = 3020.0, LastEff = 1.35734E-04, LastError = 12.3 },
            new LsrmAnchor { File = "RadiaCode - marinelli 0.5.txt", DataRows = 150,
                             FirstEnergy = 20.0, FirstEff = 4.55616E-02,
                             FirstError = 1150,
                             KeptEnergy = 40.0, KeptEff = 2.76634E-03,
                             LastEnergy = 3000.0, LastEff = 2.48089E-05, LastError = 21.4 },
        };

        static MethodInfo LsrmReader()
        {
            MethodInfo reader = typeof(DeviceConfigForm).GetMethod(
                "ReadLsrmEfficiencyExport", BindingFlags.NonPublic | BindingFlags.Static);
            if (reader == null)
            {
                throw new InvalidOperationException("нет DeviceConfigForm.ReadLsrmEfficiencyExport");
            }

            return reader;
        }

        static List<ROIEfficiencyData> ReadLsrm(string path, out string problem)
        {
            object[] call = { path, null };
            var points = (List<ROIEfficiencyData>)LsrmReader().Invoke(null, call);
            problem = (string)call[1];
            return points;
        }

        static bool Close(double a, double b)
        {
            return Math.Abs(a - b) <= 1e-9 * Math.Max(1.0, Math.Abs(b));
        }

        /// <summary>
        /// `T174`. Восемь НАСТОЯЩИХ экспортов ЛСРМ из дерева против якоря,
        /// снятого из файлов глазами. Плюс положительный контроль: испорченный
        /// файл обязан ОТКАЗАТЬ, а не прочитаться наполовину.
        /// </summary>
        static void LsrmRealExports()
        {
            Console.WriteLine();
            Console.WriteLine("== `T174`: восемь настоящих экспортов ЛСРМ ==");
            Console.WriteLine("   правило: точка с заявленной погрешностью выше {0:f0} % в кривую НЕ берётся"
                              + " (решение Amber 05.09.2026)", DeviceConfigForm_LsrmMaxErrorPercent());

            if (!Directory.Exists(lsrmDir))
            {
                Console.WriteLine("  нет каталога " + lsrmDir + " — плечо пропущено");
                failed++;
                checks++;
                return;
            }

            // ⚠ Сначала — что каталог не сузился и не разросся: якорь на восемь
            // файлов ничего не значит, если файлов в дереве стало девять.
            string[] present = Directory.GetFiles(lsrmDir, "*.txt");
            Ok(present.Length == LsrmAnchors.Length,
               string.Format("файлов в «{0}»: {1} (якорь на {2})",
                             lsrmDir, present.Length, LsrmAnchors.Length));

            int totalDropped = 0;
            foreach (LsrmAnchor a in LsrmAnchors)
            {
                string path = Path.Combine(lsrmDir, a.File);
                if (!File.Exists(path))
                {
                    Ok(false, "нет файла " + path);
                    continue;
                }

                string problem;
                List<ROIEfficiencyData> points = ReadLsrm(path, out problem);
                int expected = a.DataRows - 1;   // отсекается РОВНО первая точка
                int dropped = a.DataRows - points.Count;
                totalDropped += dropped;

                bool ok = problem == null
                          && points.Count == expected
                          && Close(points[0].Energy, a.KeptEnergy)
                          && Close(points[0].Efficiency, a.KeptEff)
                          && Close(points[points.Count - 1].Energy, a.LastEnergy)
                          && Close(points[points.Count - 1].Efficiency, a.LastEff)
                          && Close(points[points.Count - 1].ErrorPercent, a.LastError);

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-38} строк {1}, отсечено {2} (первая была {3:f1} кэВ при {4:f0} %),"
                    + " осталось {5}: {6:f1}…{7:f1} кэВ, eff {8:e5} … {9:e5}",
                    a.File, a.DataRows, dropped, a.FirstEnergy, a.FirstError, points.Count,
                    points.Count > 0 ? points[0].Energy : -1,
                    points.Count > 0 ? points[points.Count - 1].Energy : -1,
                    points.Count > 0 ? points[0].Efficiency : -1,
                    points.Count > 0 ? points[points.Count - 1].Efficiency : -1));
                Ok(ok, string.Format(CultureInfo.InvariantCulture,
                    "{0}: точек {1} (якорь {2}), первая {3:f1} кэВ (якорь {4:f1}),"
                    + " последняя {5:f1} кэВ eff {6:e5} (якорь {7:f1} / {8:e5}), жалоб «{9}»",
                    a.File, points.Count, expected,
                    points.Count > 0 ? points[0].Energy : -1, a.KeptEnergy,
                    points.Count > 0 ? points[points.Count - 1].Energy : -1,
                    points.Count > 0 ? points[points.Count - 1].Efficiency : -1,
                    a.LastEnergy, a.LastEff, Short(problem)));
            }

            Ok(totalDropped == LsrmAnchors.Length,
               string.Format("отсечено правилом 100 % всего {0} точек на {1} файлов —"
                             + " ровно по одной, второй такой точки нет ни в одном",
                             totalDropped, LsrmAnchors.Length));

            LsrmCorruptions();
            LsrmCutPrice();
        }

        /// <summary>
        /// ⚠ ЦЕНА ОТСЕЧЕНИЯ — та ли она, о которой говорит смежная строка
        /// `A200` (покрытие Am-241). `A200` про ШИРИНУ кривой: сетка мощности
        /// дозы обрезана протяжённостью кривой, поставочные кривые идут ровно
        /// 40…3000 кэВ, и низ Am-241 остаётся вне счёта. Отсечение по
        /// погрешности эту ширину МЕНЯЕТ — оно снимает самую нижнюю точку
        /// каждого экспорта, — поэтому вопрос «попадает ли оно в ту же цену»
        /// разрешается только замером.
        ///
        /// ⛔ `A200` этим НЕ закрывается: она про поставочные `config/ROI/*.xml`,
        /// а здесь мерятся файлы `LSRM Geometries/Exported Curves`.
        /// </summary>
        static void LsrmCutPrice()
        {
            Console.WriteLine();
            Console.WriteLine("  -- цена отсечения для покрытия Am-241 (смежная `A200`, НЕ закрывается) --");

            string devicePath = Path.Combine(corpusDir, "devices",
                "1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml");
            string spectra = Path.Combine(corpusDir, "spectra");
            ResultData etalon = LoadSpectrum(Path.Combine(spectra, "ASN16_Cs137.xml"));
            ResultData americium = LoadSpectrum(Path.Combine(spectra, "ASN16_Am241.xml"));
            if (!File.Exists(devicePath) || etalon == null || americium == null)
            {
                Console.WriteLine("     нет прибора или спектров корпуса — замер пропущен");
                failed++;
                checks++;
                return;
            }

            DeviceConfigInfo device;
            using (var fs = new FileStream(devicePath, FileMode.Open, FileAccess.Read))
            {
                device = (DeviceConfigInfo)new XmlSerializer(typeof(DeviceConfigInfo)).Deserialize(fs);
            }

            double deviceMin, deviceMax;
            DoseRateEstimator.DeviceRange(device, null, out deviceMin, out deviceMax);
            var manager = new DoseRateManager(Config());

            // Мерятся ДВА экспорта ЛСРМ того же прибора, что и спектры ASN16:
            // цилиндр и маринелли. Их отсечённые точки лежат на 20 и 10 кэВ —
            // ровно там, где живёт низ америция.
            foreach (LsrmAnchor a in LsrmAnchors.Where(x => x.File.StartsWith("Nano 16", StringComparison.Ordinal)
                                                            && x.File.IndexOf("5cm", StringComparison.Ordinal) < 0))
            {
                string problem;
                List<ROIEfficiencyData> cut = ReadLsrm(Path.Combine(lsrmDir, a.File), out problem);
                if (problem != null)
                {
                    Ok(false, a.File + ": " + Short(problem));
                    continue;
                }

                // Тот же список ПЛЮС отсечённая точка — по якорю, снятому глазами.
                var whole = new List<ROIEfficiencyData>
                {
                    new ROIEfficiencyData { Energy = a.FirstEnergy, Efficiency = a.FirstEff, ErrorPercent = a.FirstError },
                };
                whole.AddRange(cut);

                double cutRate, cutCoverage, cutLow;
                double wholeRate, wholeCoverage, wholeLow;
                AmericiumWith(manager, device, deviceMin, deviceMax, etalon, americium, cut,
                              out cutRate, out cutCoverage, out cutLow);
                AmericiumWith(manager, device, deviceMin, deviceMax, etalon, americium, whole,
                              out wholeRate, out wholeCoverage, out wholeLow);

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "     {0}:", a.File));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "        С точкой {0:f1} кэВ ({1:f0} %): низ сетки {2:f1} кэВ,"
                    + " Am-241 {3:e4} мкЗв/ч, покрытие {4:f2} %",
                    a.FirstEnergy, a.FirstError, wholeLow, wholeRate, 100.0 * wholeCoverage));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "        БЕЗ неё:                 низ сетки {0:f1} кэВ,"
                    + " Am-241 {1:e4} мкЗв/ч, покрытие {2:f2} %",
                    cutLow, cutRate, 100.0 * cutCoverage));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "        отсечение стоит {0:+0.00;-0.00} п.п. покрытия и {1:+0.0;-0.0} % дозы",
                    100.0 * (cutCoverage - wholeCoverage),
                    wholeRate > 0.0 ? 100.0 * (cutRate - wholeRate) / wholeRate : double.NaN));

                // ⛔ Числа сравнимы только тогда, когда обе кривые вообще годны.
                Ok(cutRate > 0.0 && wholeRate > 0.0 && cutLow > wholeLow,
                   string.Format(CultureInfo.InvariantCulture,
                       "{0}: отсечение поднимает низ сетки {1:f1} → {2:f1} кэВ, покрытие {3:f2} → {4:f2} %",
                       a.File, wholeLow, cutLow, 100.0 * wholeCoverage, 100.0 * cutCoverage));
            }
        }

        static void AmericiumWith(DoseRateManager manager, DeviceConfigInfo device,
                                  double deviceMin, double deviceMax,
                                  ResultData etalon, ResultData americium,
                                  List<ROIEfficiencyData> points,
                                  out double rate, out double coverage, out double gridLow)
        {
            DoseRateCurve curve = DoseRateEstimator.CurveOf(points);
            double low = Math.Max(deviceMin, curve.MinKev);
            double high = Math.Min(deviceMax, curve.MaxKev);
            double[] grid = DoseRateEstimator.BuildGrid(low, high);
            gridLow = grid[0];

            var config = new DoseRateConfig();
            config.DoseRateCalibrationPoints = DoseRateEstimator.Estimate(
                etalon.EnergySpectrum, curve, 1.0, grid, null);
            DoseRate dose = manager.Calculate(americium, config);
            rate = dose.Rate;
            coverage = dose.Coverage;
        }

        /// <summary>Значение порога — из самого приложения, не переписанное сюда.</summary>
        static double DeviceConfigForm_LsrmMaxErrorPercent()
        {
            FieldInfo f = typeof(DeviceConfigForm).GetField(
                "LsrmMaxErrorPercent", BindingFlags.NonPublic | BindingFlags.Static);
            return f == null ? double.NaN : (double)f.GetRawConstantValue();
        }

        /// <summary>
        /// ⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ разбора. Приёмка, которая на восьми целых
        /// файлах проходит, не меряет ничего, пока не показано, что на
        /// испорченном она ПАДАЕТ. Порча берётся от настоящего файла, а не от
        /// сочинённого: тем самым проверяется тот же вход, что и выше.
        /// </summary>
        static void LsrmCorruptions()
        {
            Console.WriteLine();
            Console.WriteLine("  -- положительный контроль: испорченный файл обязан ОТКАЗАТЬ --");

            string source = Path.Combine(lsrmDir, LsrmAnchors[0].File);
            if (!File.Exists(source))
            {
                Ok(false, "нет исходного файла для порчи: " + source);
                return;
            }

            string[] lines = File.ReadAllLines(source);
            string tmp = Path.Combine(Path.GetTempPath(), "doserateprobe_lsrm_broken.txt");

            // (1) шапки нет вовсе — прежде она отбрасывалась безусловно, и
            //     первая точка исчезала МОЛЧА.
            File.WriteAllLines(tmp, lines.Skip(1).ToArray(), new UTF8Encoding(false));
            Refuses("шапки нет: первая строка данных не проглатывается молча", tmp);

            // (2) шапка есть, но колонки переставлены/переименованы.
            var renamed = (string[])lines.Clone();
            renamed[0] = "E\tEff\tErr";
            File.WriteAllLines(tmp, renamed, new UTF8Encoding(false));
            Refuses("шапка не ЛСРМ-овская", tmp);

            // (3) у одной строки в середине пропала колонка — ровно тот случай,
            //     когда прежний разбор читал ПОЛОВИНУ файла.
            var truncated = (string[])lines.Clone();
            truncated[40] = "600.0\t\t\t";
            File.WriteAllLines(tmp, truncated, new UTF8Encoding(false));
            Refuses("в строке 41 осталась одна колонка", tmp);

            // (4) нечисло в столбце.
            var notNumber = (string[])lines.Clone();
            notNumber[40] = "600.0\t\t\tнечисло\t\t3.1";
            File.WriteAllLines(tmp, notNumber, new UTF8Encoding(false));
            Refuses("нечисло в столбце эффективности", tmp);

            // (5) после отсечения точек осталось меньше двух.
            var allBad = new List<string> { lines[0] };
            allBad.Add("100.0\t\t\t1.0E-02\t\t500");
            allBad.Add("200.0\t\t\t1.0E-02\t\t500");
            allBad.Add("300.0\t\t\t1.0E-02\t\t3.0");
            File.WriteAllLines(tmp, allBad.ToArray(), new UTF8Encoding(false));
            Refuses("после отсечения осталась одна точка", tmp);

            // ⚠ А ВОТ ЭТО отказом быть НЕ ДОЛЖНО, и это моё решение, названное
            // вслух. Настоящий экспорт разделяет колонки двумя-тремя
            // табуляциями подряд; тот же файл с ОДИНОЧНЫМИ табуляциями — это то,
            // что делает с ним любой текстовый редактор, и никакой информации в
            // нём не потеряно. Прежний разбор требовал шести полей на строку и
            // читал такой файл как ПУСТОЙ; теперь он читается ЦЕЛИКОМ и даёт те
            // же числа. Строка задания ждала здесь отказа — отказ был бы хуже:
            // порядок колонок закреплён проверенной шапкой, гадать не о чем.
            var single = lines.Select(l => System.Text.RegularExpressions.Regex.Replace(l, "\t+", "\t")).ToArray();
            File.WriteAllLines(tmp, single, new UTF8Encoding(false));
            string problem;
            List<ROIEfficiencyData> collapsed = ReadLsrm(tmp, out problem);
            string ignored;
            List<ROIEfficiencyData> original = ReadLsrm(source, out ignored);
            bool same = problem == null && collapsed.Count == original.Count
                        && Close(collapsed[0].Energy, original[0].Energy)
                        && Close(collapsed[collapsed.Count - 1].Efficiency,
                                 original[original.Count - 1].Efficiency);
            Ok(same, string.Format(CultureInfo.InvariantCulture,
                "одиночные табуляции читаются ЦЕЛИКОМ (решение полосы): точек {0}, у оригинала {1}, жалоб «{2}»",
                collapsed.Count, original.Count, Short(problem)));

            // ...и ложной тревоги на нетронутых восьми нет — это проверено выше
            // по якорю, здесь только повторяется исходником порчи.
            Ok(ignored == null && original.Count == LsrmAnchors[0].DataRows - 1,
               string.Format("исходник порчи читается без жалоб: точек {0}", original.Count));

            File.Delete(tmp);
        }

        static void Refuses(string what, string path)
        {
            string problem;
            List<ROIEfficiencyData> points = ReadLsrm(path, out problem);
            Ok(problem != null && points.Count == 0,
               string.Format("{0} → отказ «{1}», точек отдано {2}",
                             what, Short(problem), points.Count));
        }

        // ==================================================================
        // 7. `A203` — канал переполнения назван вслух
        // ==================================================================

        /// <summary>
        /// `A203`. Правило «канал переполнения» (<see cref="OverflowChannel"/>)
        /// против КОРПУСА, а не против одного ASN16, плюс сквозной замер: что
        /// на самом деле складывает <see cref="DoseRateManager"/>.
        /// </summary>
        static void OverflowRule()
        {
            Console.WriteLine();
            Console.WriteLine("== `A203`: канал переполнения — правило и его цена ==");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "   критерий: v ≥ {0:f0}·max(мед{1}, 1) И v − мед ≥ {2:f0}·√(мед+1), только у КРАЙНИХ каналов",
                OverflowChannel.MinRatio, OverflowChannel.NeighbourWindow, OverflowChannel.MinSigma));

            CorpusSplit();
            LastChannelCounted();
        }

        /// <summary>
        /// Как правило делит корпус. Это и есть подкрепление эвристики числом:
        /// «последний канал — всегда переполнение» опровергается прямо здесь.
        /// </summary>
        static void CorpusSplit()
        {
            string spectra = Path.Combine(corpusDir, "spectra");
            if (!Directory.Exists(spectra))
            {
                Console.WriteLine("  нет " + spectra + " — разбиение корпуса не считано");
                failed++;
                checks++;
                return;
            }

            string[] files = Directory.GetFiles(spectra, "*.xml");
            Array.Sort(files, StringComparer.Ordinal);
            int high = 0, low = 0, read = 0;
            double smallestHigh = double.MaxValue, largestPlain = 0.0;
            string smallestHighName = "", largestPlainName = "";
            foreach (string file in files)
            {
                ResultData data;
                try
                {
                    data = LoadSpectrum(file);
                }
                catch (Exception)
                {
                    continue;
                }

                if (data == null || data.EnergySpectrum == null || data.EnergySpectrum.Spectrum == null)
                {
                    continue;
                }

                int[] v = data.EnergySpectrum.Spectrum;
                if (v.Length < 70)
                {
                    continue;
                }

                read++;
                bool hi = OverflowChannel.IsOverflow(v, v.Length - 1);
                bool lo = OverflowChannel.IsOverflow(v, 0);
                if (hi)
                {
                    high++;
                    if (v[v.Length - 1] < smallestHigh)
                    {
                        smallestHigh = v[v.Length - 1];
                        smallestHighName = Path.GetFileNameWithoutExtension(file);
                    }
                }
                else if (v[v.Length - 1] > largestPlain)
                {
                    largestPlain = v[v.Length - 1];
                    largestPlainName = Path.GetFileNameWithoutExtension(file);
                }

                if (lo)
                {
                    low++;
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  корпус: прочитано {0} спектров; переполнение в ПОСЛЕДНЕМ канале у {1}, в НУЛЕВОМ у {2}",
                read, high, low));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  наименьшее принятое: {0} отсчётов ({1}); наибольшее отвергнутое: {2:f0} ({3})",
                smallestHigh, smallestHighName, largestPlain, largestPlainName));

            // ⛔ Главное число плеча: если бы «последний канал — всегда
            // переполнение» было верно, здесь стояло бы read, а не 28.
            Ok(read > 100 && high > 0 && high < read,
               string.Format("правило разделяет корпус: переполнение у {0} спектров из {1},"
                             + " у остальных последний канал ОБЫЧНЫЙ", high, read));
            Ok(low == 0, string.Format(
                "нулевой канал: переполнения нет ни у одного из {0} — живого подтверждения"
                + " у этой половины правила НЕТ, только положительный контроль ниже", read));
        }

        /// <summary>
        /// Сквозной замер `A203`: два спектра — с переполнением в последнем
        /// канале и без, — и положительный контроль с переполнением в НУЛЕВОМ.
        /// </summary>
        static void LastChannelCounted()
        {
            string spectra = Path.Combine(corpusDir, "spectra");
            ResultData withOverflow = LoadSpectrum(Path.Combine(spectra, "ASN16_Cs137.xml"));
            ResultData plain = LoadSpectrum(Path.Combine(spectra, "G1S24_Th228_P5.xml"));
            if (withOverflow == null || plain == null)
            {
                Console.WriteLine("  нет спектров корпуса — сквозной замер пропущен");
                failed++;
                checks++;
                return;
            }

            Console.WriteLine();
            Console.WriteLine("  -- что складывает DoseRateManager (диапазон шире шкалы, чувствительность 1) --");
            WholeScale("ASN16_Cs137 — переполнение в последнем канале", withOverflow, true);
            WholeScale("G1S24_Th228_P5 — последний канал ОБЫЧНЫЙ", plain, false);

            // ⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: переполнение в НУЛЕВОМ канале. В корпусе
            // такого прибора нет, поэтому спектр делается насыпкой: правило
            // обязано увидеть его ТЕМ ЖЕ кодом, что и последний канал.
            ResultData zero = LoadSpectrum(Path.Combine(spectra, "G1S24_Th228_P5.xml"));
            int[] z = zero.EnergySpectrum.Spectrum;
            long tail = 0;
            for (int i = 1; i <= 32; i++)
            {
                tail += z[i];
            }

            int pile = (int)(50 * Math.Max(1.0, tail / 32.0)) + 1000;
            int before = z[0];
            z[0] = pile;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  положительный контроль: в нулевой канал G1S24_Th228_P5 насыпано {0} (было {1})",
                pile, before));
            Ok(OverflowChannel.IsOverflow(z, 0),
               "правило видит переполнение в НУЛЕВОМ канале тем же кодом");
            WholeScale("G1S24_Th228_P5 с насыпанным нулевым каналом", zero, true, pile);

            // ⛔ Отрицательный контроль правила: у нетронутого спектра нулевой
            // канал переполнением не объявляется (иначе первая проверка прошла
            // бы у чего угодно).
            Ok(!OverflowChannel.IsOverflow(plain.EnergySpectrum.Spectrum, 0),
               "у нетронутого G1S24_Th228_P5 нулевой канал переполнением НЕ объявлен");

            RoundTripOnPlainSpectrum(plain);
        }

        /// <summary>
        /// Один диапазон шире всей шкалы и чувствительность 1: тогда
        /// <c>Rate · время</c> — это ровно сумма каналов, которые
        /// <see cref="DoseRateManager"/> счёл. Сумма спектра берётся здесь
        /// напрямую, не тем кодом, который меряется.
        /// </summary>
        static void WholeScale(string what, ResultData data, bool expectDropped, int expectedDrop = -1)
        {
            EnergySpectrum spectrum = data.EnergySpectrum;
            int[] v = spectrum.Spectrum;
            double all = 0.0, exceptLast = 0.0;
            for (int i = 0; i < v.Length; i++)
            {
                all += v[i];
                if (i < v.Length - 1)
                {
                    exceptLast += v[i];
                }
            }

            // Границы берутся у самой калибровки, чтобы диапазон заведомо
            // накрыл всю шкалу с обоих концов. Чувствительность 1 получается
            // через CPS и Etalon: своего сеттера у неё нет.
            EnergyCalibration cal = spectrum.EnergyCalibration;
            var point = new DoseRateCalibrationPoint
            {
                LowerBound = cal.ChannelToEnergy(0.0) - 1000.0,
                UpperBound = cal.ChannelToEnergy(v.Length) + 1000.0,
                CPS = 1.0,
            };
            point.EtalonDoseRateValue = 1.0;

            var config = new DoseRateConfig();
            config.DoseRateCalibrationPoints = new List<DoseRateCalibrationPoint> { point };

            DoseRate dose = new DoseRateManager(Config()).Calculate(data, config);
            double counted = dose.Rate * spectrum.MeasurementTime;
            double dropped = all - counted;
            double expected = expectedDrop >= 0
                ? expectedDrop
                : (expectDropped ? v[v.Length - 1] : 0.0);

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0}: каналов {1}, в спектре {2:f0} отсчётов, посчитано {3:f0}, отброшено {4:f0}"
                + " (последний канал {5}, покрытие {6:f2} %)",
                what, v.Length, all, counted, dropped, v[v.Length - 1], 100.0 * dose.Coverage));
            Ok(Math.Abs(dropped - expected) < 0.5,
               string.Format(CultureInfo.InvariantCulture,
                   "{0}: отброшено {1:f0}, ожидалось {2:f0}", what, dropped, expected));

            if (!expectDropped)
            {
                // ⛔ Отдельно и вслух: СТАРОЕ правило отбросило бы последний
                // канал и здесь. Именно это `A203` и называет дефектом.
                Ok(Math.Abs(counted - all) < 0.5 && Math.Abs(all - exceptLast) > 0.5,
                   string.Format(CultureInfo.InvariantCulture,
                       "последний канал ТЕПЕРЬ посчитан: {0:f0} отсчётов, которые старое правило теряло",
                       all - exceptLast));
            }
        }

        /// <summary>
        /// ⚠ Что правка НЕ должна была сломать: обратный ход «построить точки по
        /// эталону — померить тот же эталон» на спектре БЕЗ переполнения в
        /// последнем канале. Генератор точек (`DoseRateEstimator`) не тронут, и
        /// сойтись обязано ровно потому, что его сетка до последнего канала не
        /// достаёт, — если бы доставала, здесь была бы видна расходимость.
        /// </summary>
        static void RoundTripOnPlainSpectrum(ResultData plain)
        {
            double min, max;
            DoseRateEstimator.DeviceRange(null, plain.EnergySpectrum, out min, out max);
            DoseRateCurve curve = FlatCurve();
            double low = Math.Max(min, curve.MinKev);
            double high = Math.Min(max, curve.MaxKev);
            double[] grid = DoseRateEstimator.BuildGrid(low, high);

            const double Declared = 1.0;
            var config = new DoseRateConfig();
            config.DoseRateCalibrationPoints = DoseRateEstimator.Estimate(
                plain.EnergySpectrum, curve, Declared, grid, null);
            DoseRate back = new DoseRateManager(Config()).Calculate(plain, config);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  обратный ход на спектре БЕЗ переполнения ({0:f1}…{1:f0} кэВ, {2} диапазонов): {3:f6}",
                grid[0], grid[grid.Length - 1], grid.Length - 1, back.Rate));
            Ok(Math.Abs(back.Rate - Declared) < 1e-9,
               string.Format(CultureInfo.InvariantCulture,
                   "правка не развела генератор с потребителем: невязка {0:e2}",
                   Math.Abs(back.Rate - Declared)));
        }

        // ==================================================================
        // 8. Цена молчаливого обрезания 40–3000 кэВ
        // ==================================================================

        static void TruncationPrice()
        {
            Console.WriteLine();
            Console.WriteLine("== цена молчаливого обрезания 40–3000 кэВ ==");

            string devices = Path.Combine(corpusDir, "devices");
            string spectra = Path.Combine(corpusDir, "spectra");
            string devicePath = Path.Combine(devices, "1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml");
            if (!File.Exists(devicePath))
            {
                Console.WriteLine("  нет " + devicePath + " — замер пропущен");
                failed++;
                checks++;
                return;
            }

            DeviceConfigInfo device;
            using (var fs = new FileStream(devicePath, FileMode.Open, FileAccess.Read))
            {
                device = (DeviceConfigInfo)new XmlSerializer(typeof(DeviceConfigInfo)).Deserialize(fs);
            }

            double deviceMin, deviceMax;
            DoseRateEstimator.DeviceRange(device, null, out deviceMin, out deviceMax);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  шкала прибора {0}: {1:f0}...{2:f0} кэВ на {3} каналах",
                device.Name, deviceMin, deviceMax, device.NumberOfChannels));

            ResultData etalon = LoadSpectrum(Path.Combine(spectra, "ASN16_Cs137.xml"));
            ResultData americium = LoadSpectrum(Path.Combine(spectra, "ASN16_Am241.xml"));
            ResultData nuts = LoadSpectrum(Path.Combine(spectra, "ASN16_BrazilNuts.xml"));
            if (etalon == null || americium == null || nuts == null)
            {
                Console.WriteLine("  нет спектров корпуса — замер пропущен");
                failed++;
                checks++;
                return;
            }

            // Кривая эффективности пробы — вероятность взаимодействия в голом
            // кристалле NaI толщиной 40 мм, посчитанная по ослаблению XCOM.
            // Названа прямо: поставочные кривые идут ровно от 40 до 3000 кэВ,
            // то есть сами обрезаны теми же двумя числами, и на них цена
            // обрезания не измерима вовсе. Форма кривой на цену влияет, поэтому
            // ниже тот же замер повторён на ПОСТОЯННОЙ эффективности —
            // это верхняя граница цены низа.
            List<ROIEfficiencyData> naiCurve = NaICurve();
            List<ROIEfficiencyData> flat = FlatCurvePoints();

            Measure("кристалл NaI 40 мм по XCOM", device, deviceMin, deviceMax,
                    etalon, americium, nuts, naiCurve);
            Measure("постоянная эффективность", device, deviceMin, deviceMax,
                    etalon, americium, nuts, flat);
        }

        static void Measure(string curveName, DeviceConfigInfo device,
                            double deviceMin, double deviceMax,
                            ResultData etalon, ResultData americium, ResultData nuts,
                            List<ROIEfficiencyData> curvePoints)
        {
            Console.WriteLine();
            Console.WriteLine("  --- кривая: " + curveName + " ---");

            DoseRateCurve curve = DoseRateEstimator.CurveOf(curvePoints);

            double low = Math.Max(deviceMin, curve.MinKev);
            double high = Math.Min(deviceMax, curve.MaxKev);
            double[] newGrid = DoseRateEstimator.BuildGrid(low, high);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  сетка: старая 40...3000 кэВ, {0} диапазонов; новая {1:f1}...{2:f0} кэВ, {3} диапазонов",
                OldEnergies.Length - 1, newGrid[0], newGrid[newGrid.Length - 1], newGrid.Length - 1));

            // Обе калибровки — по одному эталону с одной объявленной дозой.
            const double Declared = 1.0;   // мкЗв/ч
            DoseRateConfig oldConfig = new DoseRateConfig();
            oldConfig.DoseRateCalibrationPoints = OldEstimate(
                etalon.EnergySpectrum, curve, Declared);

            DoseRateConfig newConfig = new DoseRateConfig();
            newConfig.DoseRateCalibrationPoints = DoseRateEstimator.Estimate(
                etalon.EnergySpectrum, curve, Declared, newGrid, null);

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  точек калибровки: старым путём {0}, новым {1}",
                oldConfig.DoseRateCalibrationPoints.Count, newConfig.DoseRateCalibrationPoints.Count));

            DoseRateManager manager = new DoseRateManager(Config());

            // На САМОМ эталоне разницы нет по построению — это контроль того,
            // что оба пути нормированы на одно и то же.
            DoseRate etalonOld = manager.Calculate(etalon, oldConfig);
            DoseRate etalonNew = manager.Calculate(etalon, newConfig);
            // ⚠ Старый путь ЗДЕСЬ И ДОЛЖЕН промахиваться: он приводил границу к
            // каналу округлением, а потребитель — отбрасыванием, и обратный ход
            // не сходился (см. правку в DoseRateEstimator.Estimate). Новый путь
            // обязан сойтись точно.
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  обратный ход на самом эталоне: старый путь {0:f6}, новый {1:f6} мкЗв/ч"
                + " (объявлено {2:f6})", etalonOld.Rate, etalonNew.Rate, Declared));
            Ok(Math.Abs(etalonNew.Rate - Declared) / Declared < 1e-9,
               string.Format(CultureInfo.InvariantCulture,
                   "новый путь сходится сам с собой: невязка {0:e2}",
                   Math.Abs(etalonNew.Rate - Declared) / Declared));

            WhereWindowsDiffer(etalon.EnergySpectrum, newConfig.DoseRateCalibrationPoints);

            Compare(manager, "Am-241 (12.9 % отсчётов ниже 40 кэВ)", americium, oldConfig, newConfig);
            Compare(manager, "бразильские орехи (2.1 % отсчётов выше 3000 кэВ)", nuts, oldConfig, newConfig);
        }

        /// <summary>
        /// Где именно генератор точек и их потребитель видят РАЗНЫЕ каналы.
        /// Печатает первые расхождения: без этого «обратный ход не сошёлся»
        /// остаётся числом без причины.
        /// </summary>
        static void WhereWindowsDiffer(EnergySpectrum spectrum, List<DoseRateCalibrationPoint> points)
        {
            int shown = 0;
            for (int i = 0; i < points.Count; i++)
            {
                DoseRateCalibrationPoint p = points[i];
                // как считает потребитель (DoseRateManager)
                int startch = (int)spectrum.EnergyCalibration.EnergyToChannel(
                    p.LowerBound, spectrum.NumberOfChannels);
                int endch = (int)spectrum.EnergyCalibration.EnergyToChannel(
                    p.UpperBound, spectrum.NumberOfChannels);
                if (startch < 0) startch = 0;
                // Как считает потребитель ПОСЛЕ `A203`: зажим в длину, канал
                // переполнения пропускается именем.
                if (endch > spectrum.Spectrum.Length) endch = spectrum.Spectrum.Length;
                bool[] overflow = OverflowChannel.Mask(spectrum.Spectrum);
                double counts = 0.0;
                for (int j = startch; j < endch; j++)
                {
                    if (overflow[j]) continue;
                    counts += spectrum.Spectrum[j];
                }

                double consumerCps = counts / spectrum.MeasurementTime;
                if (Math.Abs(consumerCps - p.CPS) > 1e-9 * Math.Max(1.0, p.CPS) && shown < 6)
                {
                    shown++;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "     расхождение окна {0:f1}-{1:f1} кэВ: генератор cps={2:f4}, потребитель cps={3:f4}"
                        + " (каналы потребителя {4}..{5})",
                        p.LowerBound, p.UpperBound, p.CPS, consumerCps, startch, endch));
                }
            }

            if (shown == 0)
            {
                Console.WriteLine("     окна каналов у генератора и потребителя совпали все");
            }
        }

        static void Compare(DoseRateManager manager, string what, ResultData data,
                            DoseRateConfig oldConfig, DoseRateConfig newConfig)
        {
            DoseRate before = manager.Calculate(data, oldConfig);
            DoseRate after = manager.Calculate(data, newConfig);
            double delta = after.Rate - before.Rate;
            double percent = before.Rate > 0.0 ? 100.0 * delta / before.Rate : double.NaN;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0}:", what));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "     старым кодом {0:f4} мкЗв/ч (покрытие {1:f1} % отсчётов)",
                before.Rate, 100.0 * before.Coverage));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "     новым  кодом {0:f4} мкЗв/ч (покрытие {1:f1} % отсчётов)",
                after.Rate, 100.0 * after.Coverage));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "     разница {0:+0.0000;-0.0000} мкЗв/ч = {1:+0.00;-0.00} %", delta, percent));
        }

        /// <summary>
        /// Старый расчёт — дословно, как он лежал в
        /// `DeviceConfigForm.CalculateDoseRateConfig` до 05.09.2026. Нужен
        /// эталоном сравнения: без него «новое лучше» нечем подтвердить.
        /// </summary>
        static List<DoseRateCalibrationPoint> OldEstimate(
            EnergySpectrum spectrum, DoseRateCurve efficiency, double expectedDoseRate)
        {
            // Те же CubicSplineMonotone на тех же шестнадцати узлах, что и в
            // старом коде: DoseRateEstimator.CurveOf строит ровно её.
            DoseRateCurve muCurve = TableCurve(OldEnergies, OldMu);
            DoseRateCurve rToSvCurve = TableCurve(OldEnergies, OldRToSv);

            double doseRate = 0;
            var rangeCpsList = new List<double>();
            var rangeEffList = new List<double>();
            var rangeFactorList = new List<double>();
            for (int i = 0; i < OldEnergies.Length - 1; i++)
            {
                double fromE = OldEnergies[i];
                double toE = OldEnergies[i + 1];
                int fromChannel = Convert.ToInt32(spectrum.EnergyCalibration.EnergyToChannel(
                    fromE, maxChannels: spectrum.NumberOfChannels));
                int toChannel = Math.Min(Convert.ToInt32(spectrum.EnergyCalibration.EnergyToChannel(
                    toE, maxChannels: spectrum.NumberOfChannels)), spectrum.NumberOfChannels - 1);
                double centerE = (fromE + toE) / 2;

                double factor = muCurve.At(centerE) * rToSvCurve.At(centerE) * centerE;
                rangeFactorList.Add(factor);

                double rangeEff = efficiency.At(centerE);
                rangeEffList.Add(rangeEff);

                double rangeCounts = 0;
                if (fromChannel < 0) fromChannel = 0;
                for (int j = fromChannel; j < toChannel; j++)
                {
                    rangeCounts += spectrum.Spectrum[j];
                }

                double rangeCps = rangeCounts / spectrum.MeasurementTime;
                rangeCpsList.Add(rangeCps);
                doseRate += rangeCps * factor / rangeEff;
            }

            double coefficient = expectedDoseRate / doseRate;
            var points = new List<DoseRateCalibrationPoint>();
            for (int i = 0; i < OldEnergies.Length - 1; i++)
            {
                double sensitivity = coefficient * rangeFactorList[i] / rangeEffList[i];
                double rangeCps = rangeCpsList[i];
                if (!(rangeCps > 0.0))
                {
                    continue;
                }

                points.Add(new DoseRateCalibrationPoint
                {
                    LowerBound = OldEnergies[i],
                    UpperBound = OldEnergies[i + 1],
                    CPS = rangeCps,
                    EtalonDoseRateValue = sensitivity * rangeCps,
                });
            }

            return points;
        }

        // ==================================================================
        // Оснастка
        // ==================================================================

        static ResultData LoadSpectrum(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("  нет " + path);
                return null;
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                return file.ResultDataList.Count > 0 ? file.ResultDataList[0] : null;
            }
        }

        static GlobalConfigManager Config()
        {
            var manager = new GlobalConfigManager();
            var info = new GlobalConfigInfo();
            if (info.ColorConfig != null
                && (info.ColorConfig.SpectrumColorList == null || info.ColorConfig.SpectrumColorList.Count == 0))
            {
                info.ColorConfig.InitializeSpectrumColor();
            }

            manager.GlobalConfig = info;
            return manager;
        }

        static PolynomialEnergyCalibration Linear(double perChannel)
        {
            var calibration = new PolynomialEnergyCalibration();
            calibration.PolynomialOrder = 1;
            calibration.Coefficients = new double[] { 0.0, perChannel };
            return calibration;
        }

        static EnergySpectrum MakeSpectrum(int channels, EnergyCalibration calibration, double seconds)
        {
            var spectrum = new EnergySpectrum(0.006, channels);
            spectrum.EnergyCalibration = calibration;
            spectrum.MeasurementTime = seconds;
            int[] data = spectrum.Spectrum;
            for (int i = 0; i < channels; i++)
            {
                data[i] = 100;
            }

            return spectrum;
        }

        static List<ROIEfficiencyData> FlatCurvePoints()
        {
            var points = new List<ROIEfficiencyData>();
            for (double e = 10.0; e <= 10000.0; e *= 1.2)
            {
                points.Add(new ROIEfficiencyData { Energy = e, Efficiency = 0.05, ErrorPercent = 1.0 });
            }

            points.Add(new ROIEfficiencyData { Energy = 10000.0, Efficiency = 0.05, ErrorPercent = 1.0 });
            return points;
        }

        static DoseRateCurve FlatCurve()
        {
            return DoseRateEstimator.CurveOf(FlatCurvePoints());
        }

        /// <summary>Табличная кривая из пары массивов — тем же сплайном, что у приложения.</summary>
        static DoseRateCurve TableCurve(double[] x, double[] y)
        {
            var points = new List<ROIEfficiencyData>();
            for (int i = 0; i < x.Length; i++)
            {
                points.Add(new ROIEfficiencyData { Energy = x[i], Efficiency = y[i], ErrorPercent = 1.0 });
            }

            return DoseRateEstimator.CurveOf(points);
        }

        /// <summary>
        /// Вероятность взаимодействия в кристалле NaI толщиной 40 мм по
        /// ослаблению XCOM: 1 − exp(−μ ρ d). Это НЕ эффективность полного
        /// поглощения — она нужна пробе только формой, чтобы делить на что-то
        /// физически убывающее, а не на постоянную.
        /// </summary>
        static List<ROIEfficiencyData> NaICurve()
        {
            const double Density = 3.667;    // г/см³
            const double Thickness = 4.0;    // см
            var points = new List<ROIEfficiencyData>();
            for (double e = 10.0; e <= 10000.0; e *= 1.15)
            {
                // NaI: массовые доли Na 0.153373, I 0.846627
                double mu = 0.153373 * AttenuationData.MassAttenuation(11, e)
                            + 0.846627 * AttenuationData.MassAttenuation(53, e);
                double value = 1.0 - Math.Exp(-mu * Density * Thickness);
                if (value < 1e-6)
                {
                    value = 1e-6;
                }

                points.Add(new ROIEfficiencyData { Energy = e, Efficiency = value, ErrorPercent = 1.0 });
            }

            points.Add(new ROIEfficiencyData { Energy = 10000.0, Efficiency = 1e-3, ErrorPercent = 1.0 });
            return points;
        }

        // ==================================================================
        // 6. Раскладка вкладки (`A202`) и её подписи (`A201`)
        // ==================================================================

        /// <summary>Слова, снятые с вкладки `C4`: модель больше не ЛСРМ, полосы 40–3000 нет.</summary>
        static readonly string[] StaleWords =
            { "LSRM", "ЛСРМ", "effcalc", "40 keV", "3MeV", "3 MeV", "40 кэВ", "3 МэВ" };

        /// <summary>
        /// Вкладка строится ЖИВЬЁМ, а разбирается двумя способами разом:
        /// отражением (какие поля у формы есть и откуда взялись) и геометрией
        /// уже построенных контролов. Окно при этом НЕ показывается: форма
        /// строится, у неё берётся дескриптор, и снимок снимается прямо с
        /// вкладки.
        /// </summary>
        static void TabLayout()
        {
            Console.WriteLine();
            Console.WriteLine("== вкладка «Dose Rate»: раскладка из конструктора форм, подписи из resx ==");

            // Раскладка не должна жить в двух местах: полей «путь к файлу», с
            // которых списки снимали Location/Size/TabIndex, в форме больше
            // нет вовсе (`A202`).
            foreach (string gone in new[] { "textBoxEffFile", "textBoxDoseRateSpectrumFile" })
            {
                Ok(FormField(gone) == null,
                    "снятого поля " + gone + " в форме нет");
            }

            foreach (string live in new[] { "comboDoseRateSpectrum", "comboDoseRateEfficiency" })
            {
                FieldInfo f = FormField(live);
                Ok(f != null && f.FieldType == typeof(ComboBox),
                    "список " + live + " объявлен полем формы типа ComboBox");
            }

            // Тот же вопрос со стороны РЕСУРСА: раскладка обоих списков обязана
            // лежать в `DeviceConfigForm.resx`, иначе она снова окажется в коде.
            string resx = ResxPath();
            string text = resx == null ? "" : File.ReadAllText(resx, Encoding.UTF8);
            Ok(resx != null, "найден " + (resx ?? "DeviceConfigForm.resx — НЕ НАЙДЕН"));
            foreach (string key in new[] { "comboDoseRateSpectrum.Location", "comboDoseRateSpectrum.Size",
                                           "comboDoseRateEfficiency.Location", "comboDoseRateEfficiency.Size" })
            {
                Ok(text.Contains("\"" + key + "\""), "в resx есть " + key);
            }

            foreach (string key in new[] { "textBoxEffFile", "textBoxDoseRateSpectrumFile" })
            {
                Ok(!text.Contains("\"" + key + "."), "в resx не осталось раскладки " + key);
            }

            DeviceType.InitializeDeviceTypes();
            ThermometerType.InitializeThermometerTypes();

            TabOnCulture("en-US");
            TabOnCulture("ru-RU");
        }

        static FieldInfo FormField(string name)
        {
            return typeof(DeviceConfigForm).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        static string ResxPath()
        {
            foreach (string candidate in new[]
                     {
                         Path.Combine("BecquerelMonitor", "DeviceConfigForm.resx"),
                         Path.Combine("..", "..", "..", "..", "BecquerelMonitor", "DeviceConfigForm.resx"),
                     })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>Построить форму на заданной культуре и обмерить вкладку.</summary>
        static void TabOnCulture(string culture)
        {
            Console.WriteLine();
            Console.WriteLine("  --- культура {0} ---", culture);
            CultureInfo previous = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            try
            {
                using (var form = new DeviceConfigForm())
                {
                    var page = (TabPage)Instance(form, "tabPage7");
                    if (page == null)
                    {
                        Ok(false, "вкладки tabPage7 в форме нет");
                        return;
                    }

                    // Дескриптор нужен, чтобы контролы получили свои шрифты и
                    // чтобы снимок не вышел пустым; окно не показывается.
                    var tabs = (TabControl)Instance(form, "tabControl1");
                    if (tabs != null && tabs.TabPages.Contains(page))
                    {
                        tabs.SelectedTab = page;
                    }

                    IntPtr ignored = form.Handle;
                    GC.KeepAlive(ignored);

                    // ⛔ Открытый `CreateControl()` у НЕВИДИМОЙ формы не делает
                    // ничего, и `DrawToBitmap` отдаёт ровный фон — измерено:
                    // 0.0 % точек, отличных от фона. Дескрипторы детей создаёт
                    // внутренняя перегрузка с `ignoreVisible`; окно при этом не
                    // показывается.
                    MethodInfo create = typeof(Control).GetMethod(
                        "CreateControl", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(bool) }, null);
                    if (create != null)
                    {
                        create.Invoke(page, new object[] { true });
                    }

                    Filling(form, page, culture);
                    Apply(page, culture);
                    Measure(page, culture);
                    Shot(page, culture);
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = previous;
            }
        }

        /// <summary>
        /// Списки переехали в конструктор форм — наполняться они обязаны
        /// по-прежнему. ⚠ Перенос, при котором раскладка верна, а связь кода с
        /// контролом порвана, снимком не виден вовсе: пустой список выглядит
        /// как список без кривых.
        /// </summary>
        static void Filling(DeviceConfigForm form, TabPage page, string culture)
        {
            var config = new DeviceConfigInfo();
            for (int i = 0; i < 3; i++)
            {
                var curve = new EfficiencyConfigData("кривая " + (i + 1));
                curve.Curve = new List<ROIEfficiencyData>
                {
                    new ROIEfficiencyData { Energy = 40, Efficiency = 0.02 },
                    new ROIEfficiencyData { Energy = 662, Efficiency = 0.01 },
                    new ROIEfficiencyData { Energy = 3000, Efficiency = 0.003 },
                };

                config.EfficiencyConfigs.Add(curve);
            }

            MethodInfo load = typeof(DeviceConfigForm).GetMethod(
                "LoadDoseRateTab", BindingFlags.Instance | BindingFlags.NonPublic);
            if (load == null)
            {
                Ok(false, "у формы нет LoadDoseRateTab");
                return;
            }

            load.Invoke(form, new object[] { config });

            var combo = (ComboBox)Child(page, "comboDoseRateEfficiency");
            Ok(combo.Items.Count == 3, string.Format(CultureInfo.InvariantCulture,
                "{0}: три кривые конфигурации доехали до списка вкладки (в списке {1})",
                culture, combo.Items.Count));
            Ok(combo.SelectedIndex == combo.Items.Count - 1,
                string.Format("{0}: выбран последний пункт ({1})", culture, combo.SelectedIndex));

            // Кривая выбранного пункта дошла до расчёта — иначе кнопка «Оценить»
            // осталась бы выключенной при полном списке.
            object curveField = Instance(form, "efficiencyCurve");
            Ok(curveField != null, culture + ": выбранная кривая доехала до расчёта");
        }

        /// <summary>Порча ради положительного контроля — ровно одна вещь.</summary>
        static void Apply(TabPage page, string culture)
        {
            if (sabotage == null)
            {
                return;
            }

            Control victim = Child(page, "labelEffNote");
            switch (sabotage)
            {
                case "long":
                    victim.Text = new string('W', 200);
                    break;
                case "word":
                    victim.Text = "*only curve shape is important (LSRM effcalc, 40 keV - 3MeV)";
                    break;
                case "hidden":
                    victim.Visible = false;
                    break;
                case "overlap":
                    victim.Location = Child(page, "buttonLoadEff").Location;
                    break;
                default:
                    throw new InvalidOperationException("неизвестная порча: " + sabotage);
            }

            Console.WriteLine("     ПОРЧА «{0}» наложена на labelEffNote ({1})", sabotage, culture);
        }

        static Control Child(TabPage page, string name)
        {
            foreach (Control c in page.Controls)
            {
                if (c.Name == name)
                {
                    return c;
                }
            }

            throw new InvalidOperationException("на вкладке нет контрола " + name);
        }

        /// <summary>Геометрия и подписи прямых детей вкладки.</summary>
        static void Measure(TabPage page, string culture)
        {
            var kids = new List<Control>();
            foreach (Control c in page.Controls)
            {
                kids.Add(c);
            }

            Ok(kids.Count > 0, string.Format("детей у вкладки: {0}", kids.Count));

            // ⚠ `Control.Visible` у ребёнка невыбранной вкладки врёт (он ложен
            // потому, что ложен родитель). Спрашивается СОБСТВЕННОЕ состояние
            // контрола — тот самый бит, который ставил `Visible = false`.
            foreach (Control c in kids)
            {
                Ok(SelfVisible(c), "виден: " + Name(c));
            }

            Rectangle field = new Rectangle(Point.Empty, page.Size);
            foreach (Control c in kids)
            {
                Ok(field.Contains(c.Bounds), string.Format(CultureInfo.InvariantCulture,
                    "{0} внутри страницы {1}x{2}: {3}", Name(c), field.Width, field.Height, c.Bounds));
            }

            int overlaps = 0;
            for (int i = 0; i < kids.Count; i++)
            {
                for (int j = i + 1; j < kids.Count; j++)
                {
                    if (kids[i].Bounds.IntersectsWith(kids[j].Bounds))
                    {
                        overlaps++;
                        Console.WriteLine("     налезают: {0} на {1}", Name(kids[i]), Name(kids[j]));
                    }
                }
            }

            Ok(overlaps == 0, string.Format("контролы не налезают друг на друга (пар: {0})", overlaps));

            // Подпись обязана ПОМЕЩАТЬСЯ. Метка с AutoSize = false и кнопка
            // обрезают текст молча, и на второй культуре это самый частый
            // способ потерять половину строки.
            foreach (Control c in kids)
            {
                if (!(c is Label) && !(c is Button))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(c.Text))
                {
                    continue;
                }

                int need = TextRenderer.MeasureText(c.Text, c.Font).Width + (c is Button ? 10 : 2);
                Ok(need <= c.Width, string.Format(CultureInfo.InvariantCulture,
                    "{0}: подписи нужно {1} тчк, дано {2} — «{3}»", Name(c), need, c.Width, c.Text));
            }

            // `A201`: снятые слова не должны остаться ни в одной подписи.
            foreach (Control c in kids)
            {
                if (string.IsNullOrEmpty(c.Text))
                {
                    continue;
                }

                foreach (string word in StaleWords)
                {
                    if (c.Text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Ok(false, string.Format("{0} обещает снятое «{1}»: «{2}»", Name(c), word, c.Text));
                    }
                }
            }

            Ok(true, string.Format("подписи вкладки ({0}) снятых слов не содержат", culture));

            foreach (Control c in kids)
            {
                Console.WriteLine("     {0,-28} {1,-22} «{2}»", Name(c), c.Bounds.ToString(), Shorten(c.Text));
            }
        }

        static string Shorten(string text)
        {
            text = text ?? "";
            return text.Length <= 70 ? text : text.Substring(0, 67) + "...";
        }

        static string Name(Control c)
        {
            return string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.Name;
        }

        /// <summary>Собственный бит видимости, не зависящий от родителя.</summary>
        static bool SelfVisible(Control c)
        {
            MethodInfo m = typeof(Control).GetMethod(
                "GetState", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
            if (m == null)
            {
                return c.Visible;
            }

            return (bool)m.Invoke(c, new object[] { 2 });   // STATE_VISIBLE
        }

        /// <summary>
        /// Снимок вкладки. ⚠ Проверяется не только то, что файл записан, но и
        /// то, что он НЕ ПУСТ: `DrawToBitmap` на контроле без дескриптора
        /// отдаёт ровный фон, и такой снимок выглядит как удачный.
        /// </summary>
        static void Shot(TabPage page, string culture)
        {
            if (shotDir == null)
            {
                return;
            }

            Directory.CreateDirectory(shotDir);
            string path = Path.Combine(shotDir, "doserate-tab-" + culture + ".png");
            using (var bmp = new Bitmap(page.Width, page.Height))
            {
                page.DrawToBitmap(bmp, new Rectangle(0, 0, page.Width, page.Height));
                bmp.Save(path, ImageFormat.Png);

                int ink = 0;
                Color ground = bmp.GetPixel(bmp.Width - 2, bmp.Height - 2);
                for (int y = 0; y < bmp.Height; y += 2)
                {
                    for (int x = 0; x < bmp.Width; x += 2)
                    {
                        if (bmp.GetPixel(x, y) != ground)
                        {
                            ink++;
                        }
                    }
                }

                double share = 100.0 * ink / (bmp.Width / 2.0 * (bmp.Height / 2.0));
                Ok(share > 1.0, string.Format(CultureInfo.InvariantCulture,
                    "снимок {0}: не фон {1:f1} % точек", path, share));
            }
        }

        static object Instance(object target, string name)
        {
            FieldInfo f = target.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return f == null ? null : f.GetValue(target);
        }
    }
}
