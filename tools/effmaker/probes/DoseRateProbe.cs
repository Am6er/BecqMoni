using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
    ///   doserateprobe [--dir=&lt;корпус&gt;]
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

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal))
                {
                    corpusDir = a.Substring(6);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            try
            {
                ComptonControl();
                MuEnAgreement();
                AmbientAgreement();
                Refusals();
                OfferedCurves();
                OfferedSpectra();
                LsrmReaderStillWorks();
                TruncationPrice();
            }
            catch (Exception ex)
            {
                Console.WriteLine("!! проба сорвалась: " + ex);
                return 3;
            }

            Console.WriteLine();
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
        // 6. Цена молчаливого обрезания 40–3000 кэВ
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
                if (endch >= spectrum.Spectrum.Length) endch = spectrum.Spectrum.Length - 1;
                double counts = 0.0;
                for (int j = startch; j < endch; j++)
                {
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
    }
}
