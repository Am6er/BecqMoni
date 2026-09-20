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
    /// Расчёт мощности дозы — задача `C4` и её наследники (`A203`, `T174`,
    /// `AMBER18`).
    ///
    /// ⛔ ПЕРЕПИСАНА 12.09.2026 (полоса П1, `AMBER18`): ручные точки калибровки,
    /// эталонный спектр, `DoseRateConfig` и вкладка `Dose Rate` сняты целиком
    /// решениями Amber 10–11.09.2026. Ушли разделы, мерившие снятое: обратный
    /// ход «построить точки по эталону — померить эталон», цена обрезания
    /// 40…3000 кэВ старой и новой сеткой, списки `C4(а)`/`C4(б)`, раскладка и
    /// подписи вкладки. Их числа остались в журналах полос `C4`, `A203`, П19 и
    /// П21. Приёмка нового расчёта — `DoseRateFromCurveProbe`.
    ///
    /// Что меряется здесь:
    ///
    ///  1. **Коэффициенты.** μ_en/ρ из XCOM (`matdb.sqlite`) против прежней
    ///     вшитой таблицы на её шестнадцати энергиях; положительный контроль
    ///     физики — доля энергии электрону по Клейну — Нишине против чисел
    ///     Аттикса. ⛔ Таблицу h*(10)/K_air эта проба НЕ судит (`T201`): её
    ///     единственный судья — `DoseCoefProbeO2`.
    ///
    ///  2. **Положительный контроль отказов.** Энергия вне таблиц, пустая
    ///     шкала, вырожденная калибровка, кривая из одной точки, кривая с
    ///     нулём, кривая без геометрии, чужая матрица, спектр без калибровки,
    ///     нулевое время — расчёт обязан отказать ВИДИМО. Отрицательный вход:
    ///     годные данные обязаны пройти числом.
    ///
    ///  3. **Разбор экспорта ЛСРМ** (`ReadLsrmEfficiencyExport`): оба
    ///     разделителя дробной части, пустой файл жалуется; порча файла
    ///     обязана отказать (источник порчи — синтетический экспорт в формате
    ///     настоящего). `T174` — восемь настоящих экспортов против якоря,
    ///     снятого глазами, и цена отсечения первой точки для покрытия Am-241
    ///     (по пиковой, с геометрией корпусной ASN16) — ТОЛЬКО с `--lsrm=`:
    ///     ⛔ каталог `LSRM Geometries/` снят из дерева 15.09.2026 (решение
    ///     Amber: «Удалить вместе с каталогом»), без ключа плечо пропускается
    ///     вслух и отказом не считается.
    ///
    ///  4. **`A203` — канал переполнения.** Правило против корпуса; что
    ///     складывает `DoseRateManager` (сумма по диапазонам против независимой
    ///     суммы каналов без переполнения); насыпанный нулевой канал; и
    ///     линейность: вдвое больше отсчётов — вдвое больше дозы, вдвое больше
    ///     G — вдвое больше дозы.
    ///
    ///   doserateprobe [--dir=&lt;корпус&gt;] [--lsrm=&lt;каталог с восемью экспортами ЛСРМ&gt;]
    ///   doserateprobe --sabotage=mu|lsrm|overflow   (ждёт ОТКАЗ; `lsrm` — только с `--lsrm=`)
    ///
    /// ⛔ Приёмка, которая проходит всегда, не мерит ничего. `--sabotage`
    /// портит РОВНО ОДНУ вещь и требует отказа своего раздела; коды у него
    /// перевёрнуты: 0 — отказ получен (сторож смотрит), 1 — не получен
    /// (сторож слеп).
    ///
    ///   mu       — в прежней таблице μ_en/ρ испорчен узел 300 кэВ (×1.5):
    ///              сверка с XCOM обязана отказать;
    ///   lsrm     — у якоря первого экспорта подменена эффективность
    ///              оставшейся точки: сверка обязана отказать;
    ///   overflow — ожидаемое число отброшенных отсчётов сдвинуто на единицу:
    ///              сквозной замер обязан отказать.
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

        static string corpusDir = @"tools\CORPUS\corpus";

        /// <summary>
        /// Каталог с восемью настоящими экспортами ЛСРМ — только ключом `--lsrm=`.
        /// ⛔ Умолчания нет: `LSRM Geometries/Exported Curves` снят из дерева
        /// 15.09.2026 (решение Amber), и плечо `T174` без ключа не гоняется.
        /// </summary>
        static string lsrmDir;

        /// <summary>Что испортить ради положительного контроля; null — ничего.</summary>
        static string sabotage;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // ⛔ (`T247`) Культура ЦЕЛИКОМ инвариантная, приказ Amber 05.09.2026.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
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

            if (sabotage != null && sabotage != "mu" && sabotage != "lsrm" && sabotage != "overflow")
            {
                Console.Error.WriteLine("--sabotage= принимает mu, lsrm или overflow");
                return 2;
            }

            if (sabotage == "lsrm" && lsrmDir == null)
            {
                // Порча якоря экспорта без самих экспортов «отказала» бы всегда —
                // положительный контроль, который не может не пройти, не контроль.
                Console.Error.WriteLine("--sabotage=lsrm нужен --lsrm=<каталог с восемью экспортами ЛСРМ>:"
                                        + " экспорты сняты из дерева 15.09.2026");
                return 2;
            }

            if (sabotage != null)
            {
                Console.WriteLine("⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: испорчено «" + sabotage + "», ждём ОТКАЗ");
            }

            try
            {
                ComptonControl();
                MuEnAgreement();
                Refusals();
                LsrmReaderStillWorks();
                LsrmRealExports();
                OverflowRule();
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
            double[] table = (double[])OldMu.Clone();
            if (sabotage == "mu")
            {
                table[7] *= 1.5;
                Console.WriteLine("   ⚠ ПОРЧА: узел 300 кэВ прежней таблицы ×1.5");
            }

            double worst = 0.0;
            string worstAt = "";
            for (int i = 0; i < OldEnergies.Length; i++)
            {
                double now = DoseRateCoefficients.MassEnergyAbsorptionAir(OldEnergies[i]);
                double diff = 100.0 * (now - table[i]) / table[i];
                if (Math.Abs(diff) > Math.Abs(worst))
                {
                    worst = diff;
                    worstAt = OldEnergies[i].ToString("f0", CultureInfo.InvariantCulture);
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,7:f0}    {1,12:f6}   {2,12:f6}    {3,8:+0.00;-0.00}",
                    OldEnergies[i], table[i], now, diff));
            }

            // Порог 3 % — не подгонка, а граница «та же величина, тот же
            // состав». Радиационные потери (g), которые здесь не вычитаются,
            // для воздуха ниже 3 МэВ дают меньше 0.2 %; остальное —
            // разрешение сетки XCOM.
            Ok(Math.Abs(worst) < 3.0, string.Format(CultureInfo.InvariantCulture,
                "худшая точка {0} кэВ: {1:+0.00;-0.00} %", worstAt, worst));
        }

        // ⛔ h*(10)/K_air здесь НЕ судится (`T201`, 06.09.2026): единственный
        // судья таблицы — `DoseCoefProbeO2` (все 25 узлов дословно, порча узла
        // отражением с ожиданием отказа, схема интерполяции между узлами).

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
            Refuses("кривая из одной точки", () =>
                DoseRateEstimator.CurveOf(new List<ROIEfficiencyData>
                    { new ROIEfficiencyData { Energy = 100, Efficiency = 0.1 } }));
            Refuses("кривая с нулевой эффективностью", () =>
                DoseRateEstimator.CurveOf(new List<ROIEfficiencyData>
                {
                    new ROIEfficiencyData { Energy = 100, Efficiency = 0.1 },
                    new ROIEfficiencyData { Energy = 200, Efficiency = 0.0 },
                }));

            // Вход от кривой (`AMBER18`): чего у кривой не хватает — названо.
            Refuses("кривой нет вовсе", () => DoseRateInput.Of(null, null));
            Refuses("кривая без точек", () => DoseRateInput.Of(new EfficiencyConfigData("пустая") { Geometry = PointGeometry(100.0) }, null));
            Refuses("кривая без геометрии", () => DoseRateInput.Of(new EfficiencyConfigData("без геометрии") { Curve = FlatCurvePoints() }, null));
            Refuses("матрица чужой геометрии", () =>
            {
                var curve = new EfficiencyConfigData("своя") { Curve = FlatCurvePoints(), Geometry = PointGeometry(100.0) };
                var foreign = new ResponseMatrix { Stamp = "чужое клеймо", Options = new ResponseMatrixOptions() };
                DoseRateInput.Of(curve, foreign);
            });

            // Спектры: без калибровки и с нулевым временем — отказ ПОКАЗЫВАЕТСЯ,
            // а не превращается в ноль: строка состояния главного окна печатает
            // `DoseRate.ToString()`.
            DoseRateInput input = DoseRateInput.Of(
                new EfficiencyConfigData("плоская") { Curve = FlatCurvePoints(), Geometry = PointGeometry(100.0) }, null);
            var manager = new DoseRateManager(Config());

            ResultData bad = new ResultData();
            bad.EnergySpectrum = MakeSpectrum(1024, null, 100.0);
            DoseRate shown = manager.Calculate(bad, input);
            Ok(!string.IsNullOrEmpty(shown.Refusal) && shown.ToString().IndexOf("0.000") < 0,
               "спектр без калибровки в строке состояния: «" + Short(shown.ToString()) + "»");

            ResultData noTime = new ResultData();
            noTime.EnergySpectrum = MakeSpectrum(1024, Linear(3.0), 0.0);
            DoseRate zeroTime = manager.Calculate(noTime, input);
            Ok(!string.IsNullOrEmpty(zeroTime.Refusal),
               "нулевое время набора: «" + Short(zeroTime.ToString()) + "»");

            // ⚠ ОТРИЦАТЕЛЬНЫЙ вход к тому же контролю: годные данные обязаны
            // ПРОЙТИ числом. Проверка, которая отказывает всегда, ничего не меряет.
            ResultData good = new ResultData();
            good.EnergySpectrum = MakeSpectrum(1024, Linear(3.0), 100.0);
            DoseRate fine = manager.Calculate(good, input);
            Ok(fine.Refusal.Length == 0 && fine.Rate > 0.0 && fine.Ranges.Count > 0 && fine.Approximate,
               string.Format(CultureInfo.InvariantCulture,
                   "годный вход прошёл: {0} диапазонов, «{1}»", fine.Ranges.Count, Short(fine.ToString())));
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
        // 3. Разбор экспорта ЛСРМ
        // ==================================================================

        static void LsrmReaderStillWorks()
        {
            Console.WriteLine();
            Console.WriteLine("== разбор текстового экспорта ЛСРМ (вкладка Efficiency) ==");

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
            // пустой список молча.
            string empty = Path.Combine(Path.GetTempPath(), "doserateprobe_lsrm_empty.txt");
            File.WriteAllText(empty, "Energy, keV\tEfficiency\tUncertainty, %\r\n", new UTF8Encoding(false));
            object[] call2 = { empty, null };
            reader.Invoke(null, call2);
            Ok(call2[1] != null, "пустой файл ЛСРМ жалуется: «" + Short((string)call2[1]) + "»");

            File.Delete(empty);
        }

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
        /// `T174`. Восемь НАСТОЯЩИХ экспортов ЛСРМ (каталог `--lsrm=`) против
        /// якоря, снятого из файлов глазами. Плюс положительный контроль:
        /// испорченный файл обязан ОТКАЗАТЬ, а не прочитаться наполовину.
        /// ⛔ Без `--lsrm=` настоящих файлов нет (сняты из дерева 15.09.2026,
        /// решение Amber): якорь и цена отсечения пропускаются ВСЛУХ и отказом
        /// не считаются, а порча идёт по синтетическому экспорту того же
        /// формата — читатель приложения судится и без файлов ЛСРМ.
        /// </summary>
        static void LsrmRealExports()
        {
            Console.WriteLine();
            Console.WriteLine("== `T174`: восемь настоящих экспортов ЛСРМ ==");
            Console.WriteLine("   правило: точка с заявленной погрешностью выше {0:f0} % в кривую НЕ берётся"
                              + " (решение Amber 05.09.2026)", DeviceConfigForm_LsrmMaxErrorPercent());

            if (lsrmDir == null)
            {
                Console.WriteLine("  ⚠ ПЛЕЧО НЕ ГОНЯЕТСЯ: экспорты ЛСРМ сняты из дерева 15.09.2026"
                                  + " (решение Amber: «Удалить вместе с каталогом»);");
                Console.WriteLine("    якорь восьми файлов и цена отсечения для Am-241 — только с"
                                  + " --lsrm=<каталог с восемью экспортами>. Отказом не считается.");
                LsrmCorruptions(SyntheticExport(), SyntheticRows);
                return;
            }

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

                double keptEff = a.KeptEff;
                if (sabotage == "lsrm" && ReferenceEquals(a, LsrmAnchors[0]))
                {
                    keptEff *= 1.01;
                    Console.WriteLine("  ⚠ ПОРЧА: у якоря «" + a.File + "» эффективность оставшейся точки ×1.01");
                }

                string problem;
                List<ROIEfficiencyData> points = ReadLsrm(path, out problem);
                int expected = a.DataRows - 1;   // отсекается РОВНО первая точка
                int dropped = a.DataRows - points.Count;
                totalDropped += dropped;

                bool ok = problem == null
                          && points.Count == expected
                          && Close(points[0].Energy, a.KeptEnergy)
                          && Close(points[0].Efficiency, keptEff)
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

            LsrmCorruptions(Path.Combine(lsrmDir, LsrmAnchors[0].File), LsrmAnchors[0].DataRows);
            LsrmCutPrice();
        }

        /// <summary>Строк данных в синтетическом экспорте: 20…3000 кэВ шагом 20.</summary>
        const int SyntheticRows = 150;

        /// <summary>
        /// Синтетический экспорт В ФОРМАТЕ НАСТОЯЩЕГО (шапка `Energy, keV`,
        /// колонки через две-три табуляции, шесть значащих, CRLF): первая точка
        /// с погрешностью выше 100 %, как у всех восьми настоящих, — правило
        /// `T174` отсекает ровно её. Источник порчи, когда каталога `--lsrm=`
        /// нет.
        /// </summary>
        static string SyntheticExport()
        {
            string path = Path.Combine(Path.GetTempPath(), "doserateprobe_lsrm_synthetic.txt");
            var text = new StringBuilder();
            text.Append("Energy, keV\tEfficiency\tUncertainty, %\r\n");
            for (int i = 0; i < SyntheticRows; i++)
            {
                double e = 20.0 * (i + 1);
                text.Append(string.Format(CultureInfo.InvariantCulture, "{0:f1}\t\t\t{1:E5}\t\t{2}\r\n",
                                          e, 0.05 * Math.Pow(662.0 / e, 0.9), i == 0 ? "554" : "3.0"));
            }

            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            Console.WriteLine("  источник порчи — синтетический экспорт " + path);
            return path;
        }

        /// <summary>
        /// ⚠ ЦЕНА ОТСЕЧЕНИЯ — та ли она, что у покрытия Am-241 ШИРИНОЙ кривой:
        /// сетка мощности дозы обрезана протяжённостью кривой, и отсечение по
        /// погрешности эту ширину МЕНЯЕТ — оно снимает самую нижнюю точку
        /// каждого экспорта. Вопрос «попадает ли оно в ту же цену» разрешается
        /// только замером.
        ///
        /// ⛔ С 12.09.2026 (`AMBER18`) замер идёт ПО ПИКОВОЙ с пометкой, как и
        /// в приложении у кривой без матрицы; геометрия кривой — точечная
        /// сцена корпусного `ASN16_Cs137` (у экспорта ЛСРМ своей нет, а без
        /// геометрии у дозы нет масштаба). Мерятся два экспорта того же прибора,
        /// что и спектр Am-241: цилиндр и маринелли; их отсечённые точки лежат
        /// на 20 и 10 кэВ — ровно там, где живёт низ америция.
        /// </summary>
        static void LsrmCutPrice()
        {
            Console.WriteLine();
            Console.WriteLine("  -- цена отсечения для покрытия Am-241 (по пиковой, геометрия ASN16_Cs137) --");

            string spectra = Path.Combine(corpusDir, "spectra");
            ResultData carrier = LoadSpectrum(Path.Combine(spectra, "ASN16_Cs137.xml"));
            ResultData americium = LoadSpectrum(Path.Combine(spectra, "ASN16_Am241.xml"));
            if (carrier == null || carrier.Efficiency == null || !carrier.Efficiency.HasGeometry || americium == null)
            {
                Console.WriteLine("     нет спектров корпуса или геометрии у ASN16_Cs137 — замер пропущен");
                failed++;
                checks++;
                return;
            }

            GeometryModel geometry = carrier.Efficiency.Geometry;
            var manager = new DoseRateManager(Config());

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

                DoseRate cutDose = PeakDose(manager, americium, geometry, cut, a.File + " (после отсечения)");
                DoseRate wholeDose = PeakDose(manager, americium, geometry, whole, a.File + " (с точкой)");
                double cutLow = cutDose.Ranges.Count > 0 ? cutDose.Ranges[0].LowKev : double.NaN;
                double wholeLow = wholeDose.Ranges.Count > 0 ? wholeDose.Ranges[0].LowKev : double.NaN;

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "     {0}:", a.File));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "        С точкой {0:f1} кэВ ({1:f0} %): низ сетки {2:f1} кэВ, Am-241 {3}, покрытие {4:f2} %",
                    a.FirstEnergy, a.FirstError, wholeLow, wholeDose, 100.0 * wholeDose.Coverage));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "        БЕЗ неё:                 низ сетки {0:f1} кэВ, Am-241 {1}, покрытие {2:f2} %",
                    cutLow, cutDose, 100.0 * cutDose.Coverage));
                if (cutDose.Refusal.Length == 0 && wholeDose.Refusal.Length == 0)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "        отсечение стоит {0:+0.00;-0.00} п.п. покрытия и {1:+0.0;-0.0} % дозы",
                        100.0 * (cutDose.Coverage - wholeDose.Coverage),
                        wholeDose.Rate > 0.0 ? 100.0 * (cutDose.Rate - wholeDose.Rate) / wholeDose.Rate : double.NaN));
                }

                // ⛔ Числа сравнимы только тогда, когда обе кривые вообще годны.
                Ok(cutDose.Refusal.Length == 0 && wholeDose.Refusal.Length == 0 && cutDose.Rate > 0.0
                   && wholeDose.Rate > 0.0 && cutLow > wholeLow,
                   string.Format(CultureInfo.InvariantCulture,
                       "{0}: отсечение поднимает низ сетки {1:f1} → {2:f1} кэВ, покрытие {3:f2} → {4:f2} %",
                       a.File, wholeLow, cutLow, 100.0 * wholeDose.Coverage, 100.0 * cutDose.Coverage));
            }
        }

        static DoseRate PeakDose(DoseRateManager manager, ResultData data, GeometryModel geometry,
                                 List<ROIEfficiencyData> points, string name)
        {
            try
            {
                var curve = new EfficiencyConfigData(name) { Curve = points, Geometry = geometry, Origin = EfficiencyOrigin.Lsrm };
                return manager.Calculate(data, DoseRateInput.Of(curve, null));
            }
            catch (DoseRateRefusalException ex)
            {
                return new DoseRate { Refusal = ex.Message };
            }
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
        /// испорченном она ПАДАЕТ. Порча берётся от настоящего файла (с
        /// `--lsrm=`), а без него — от синтетического того же формата;
        /// <paramref name="dataRows"/> — сколько строк данных в источнике.
        /// </summary>
        static void LsrmCorruptions(string source, int dataRows)
        {
            Console.WriteLine();
            Console.WriteLine("  -- положительный контроль: испорченный файл обязан ОТКАЗАТЬ --");

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

            // ⚠ А ВОТ ЭТО отказом быть НЕ ДОЛЖНО (решение полосы `T174`):
            // настоящий экспорт разделяет колонки двумя-тремя табуляциями
            // подряд; тот же файл с ОДИНОЧНЫМИ табуляциями — это то, что
            // делает с ним любой текстовый редактор, и информации в нём не
            // потеряно. Он читается ЦЕЛИКОМ и даёт те же числа.
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

            Ok(ignored == null && original.Count == dataRows - 1,
               string.Format("исходник порчи читается без жалоб: точек {0} (строк данных {1}, отсечена одна)",
                             original.Count, dataRows));

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
        // 4. `A203` — канал переполнения назван вслух
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
            Console.WriteLine("  -- что складывает DoseRateManager (плоская кривая 10…10000 кэВ, точечная сцена) --");
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
            // канал переполнением не объявляется.
            Ok(!OverflowChannel.IsOverflow(plain.EnergySpectrum.Spectrum, 0),
               "у нетронутого G1S24_Th228_P5 нулевой канал переполнением НЕ объявлен");

            Linearity(plain);
        }

        /// <summary>
        /// Плоская кривая 10…10000 кэВ и точечная сцена: сумма `Counts` по
        /// диапазонам, которые сложил <see cref="DoseRateManager"/>, против
        /// НЕЗАВИСИМОЙ суммы каналов от низа сетки до конца шкалы без каналов
        /// переполнения. Сумма спектра берётся здесь напрямую, не тем кодом,
        /// который меряется.
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

            DoseRateInput input = DoseRateInput.Of(
                new EfficiencyConfigData("плоская") { Curve = FlatCurvePoints(), Geometry = PointGeometry(100.0) }, null);
            DoseRate dose = new DoseRateManager(Config()).Calculate(data, input);
            if (dose.Refusal.Length > 0)
            {
                Ok(false, what + ": отказ «" + Short(dose.Refusal) + "»");
                return;
            }

            double counted = dose.Ranges.Sum(r => r.Counts);
            // Независимо: каналы от низа сетки (тем же отбрасыванием) до конца
            // шкалы, минус крайние каналы, которые правило называет переполнением.
            int fromChannel = (int)spectrum.EnergyCalibration.EnergyToChannel(dose.Ranges[0].LowKev, spectrum.NumberOfChannels);
            if (fromChannel < 0) fromChannel = 0;
            // Ниже сетки — БЕЗ насыпанного нулевого канала: он переполнение, и
            // ему положено оказаться среди «отброшенных», а не «ниже сетки».
            double belowGrid = 0.0;
            for (int i = expectedDrop >= 0 ? 1 : 0; i < fromChannel && i < v.Length; i++)
            {
                belowGrid += v[i];
            }

            double dropped = all - belowGrid - counted;
            double expected = expectedDrop >= 0
                ? expectedDrop
                : (expectDropped ? v[v.Length - 1] : 0.0);
            if (sabotage == "overflow")
            {
                expected += 1.0;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0}: каналов {1}, в спектре {2:f0} отсчётов, ниже сетки ({3:f1} кэВ) {4:f0}, посчитано {5:f0},"
                + " отброшено {6:f0} (последний канал {7}, покрытие {8:f2} %, доза {9})",
                what, v.Length, all, dose.Ranges[0].LowKev, belowGrid, counted, dropped, v[v.Length - 1],
                100.0 * dose.Coverage, dose));
            Ok(Math.Abs(dropped - expected) < 0.5,
               string.Format(CultureInfo.InvariantCulture,
                   "{0}: отброшено {1:f0}, ожидалось {2:f0}", what, dropped, expected));

            if (!expectDropped)
            {
                // ⛔ Отдельно и вслух: СТАРОЕ правило отбросило бы последний
                // канал и здесь. Именно это `A203` и называет дефектом.
                Ok(Math.Abs(counted + belowGrid - all) < 0.5 && Math.Abs(all - exceptLast) > 0.5,
                   string.Format(CultureInfo.InvariantCulture,
                       "последний канал ТЕПЕРЬ посчитан: {0:f0} отсчётов, которые старое правило теряло",
                       all - exceptLast));
            }
        }

        /// <summary>
        /// Расчёт линеен по отсчётам и по геометрическому множителю: вдвое
        /// больше того или другого — ровно вдвое больше дозы. Замена прежнего
        /// обратного хода по эталону (эталона больше нет).
        /// </summary>
        static void Linearity(ResultData plain)
        {
            var manager = new DoseRateManager(Config());
            var curve = new EfficiencyConfigData("плоская") { Curve = FlatCurvePoints(), Geometry = PointGeometry(100.0) };
            DoseRate one = manager.Calculate(plain, DoseRateInput.Of(curve, null));

            ResultData doubled = LoadSpectrum(Path.Combine(corpusDir, "spectra", "G1S24_Th228_P5.xml"));
            int[] d = doubled.EnergySpectrum.Spectrum;
            for (int i = 0; i < d.Length; i++)
            {
                d[i] *= 2;
            }

            DoseRate two = manager.Calculate(doubled, DoseRateInput.Of(curve, null));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  линейность: {0:f6} → {1:f6} мкЗв/ч при удвоении отсчётов (×{2:f6})",
                one.Rate, two.Rate, two.Rate / one.Rate));
            Ok(Math.Abs(two.Rate / one.Rate - 2.0) < 1e-9, "удвоение отсчётов удваивает дозу");

            // Удвоение G: точка на расстоянии R√2 даёт вдвое меньший G — доза вдвое меньше.
            double r0 = 0.5 * 5.0 + 0.1 + 0.2 + 10.0;
            double rFar = r0 * Math.Sqrt(2.0);
            var farCurve = new EfficiencyConfigData("плоская, дальше") { Curve = FlatCurvePoints(), Geometry = PointGeometry(10.0 * (rFar - 0.5 * 5.0 - 0.3)) };
            DoseRate half = manager.Calculate(plain, DoseRateInput.Of(farCurve, null));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  линейность по G: R {0:f3} → {1:f3} см, доза {2:f6} → {3:f6} мкЗв/ч (×{4:f6})",
                r0, rFar, one.Rate, half.Rate, half.Rate / one.Rate));
            Ok(Math.Abs(half.Rate / one.Rate - 0.5) < 1e-6, "удвоение расстояния² вдвое снижает дозу");
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

        /// <summary>Точечная сцена: цилиндр Ø50×50 мм, обвязка 1+2 мм, источник на заданном расстоянии (мм).</summary>
        static GeometryModel PointGeometry(double distanceMm)
        {
            var g = new GeometryModel();
            g.IsScintillator = true;
            g.Shape = CrystalShape.Cylinder;
            g.CrystalDiameter = 50.0;
            g.CrystalHeight = 50.0;
            g.FrontReflectorThickness = 1.0;
            g.FrontCladdingThickness = 2.0;
            g.SideReflectorThickness = 1.0;
            g.SideCladdingThickness = 2.0;
            g.SourceType = GeometrySourceType.Point;
            g.PointDistance = distanceMm;
            return g;
        }
    }
}
