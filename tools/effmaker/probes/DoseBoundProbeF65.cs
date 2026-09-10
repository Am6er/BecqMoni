using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace DoseBoundProbeF65
{
    /// <summary>
    /// Сторож двух половин строки `A258`: граница эффективности ε ≤ 1 жива, а
    /// разведённая с ней «форма» не стоит ничего.
    ///
    /// ⛔ Зачем проба вообще есть. `A258` чинилась в ПРОБАХ: `DoseCoefProbeO2`
    /// и `DoseRateProbe` гоняли через `DoseRateEstimator.CurveOf` величины,
    /// эффективностью не являющиеся (h*(10)/K_air в Зв/Гр, прежнюю таблицу
    /// «RToSv» в бэр/Р), и граница ~~`A222`~~ отвергала их справедливо. Самый
    /// дешёвый способ «починить» такое — ослабить границу, и снаружи это
    /// неотличимо от честного разведения случаев: обе пробы после него дают
    /// код 0. Поэтому судит отдельная проба, и судит ОБЕ половины:
    ///
    ///  1. **Граница жива на НАСТОЯЩЕЙ кривой.** Берётся не выдуманное число, а
    ///     реальная точка реального файла: `LSRM Geometries\Exported Curves\
    ///     Obsidian - marinelli 0.5.txt` несёт на 20 кэВ ε = 1.47185E+03 —
    ///     единственное значение выше единицы среди всех восьми экспортов.
    ///     `CurveOf` обязан ОТКАЗАТЬ и назвать величину с энергией.
    ///     ⚠ Отрицательное плечо: тот же файл без первой строки обязан
    ///     ПОСТРОИТЬСЯ, иначе проба меряет «отказывает всегда».
    ///     ⚠ И граница обязана стоять РОВНО на единице: ε = 1 проходит,
    ///     ε = 1 + 1e-12 отвергается.
    ///
    ///  2. **Нормировка не двигает чисел.** Пробы делят значения на наименьшую
    ///     степень двойки, не меньшую наибольшего из них, и умножают обратно
    ///     после `At`. Утверждается, что кривая выходит ТА ЖЕ ДО БИТА: степень
    ///     двойки в двоичной плавающей точке умножается и делится точно, а
    ///     монотонный сплайн однороден по значениям первой степени. Мерится на
    ///     кривой, которая проходит границу и БЕЗ нормировки, — иначе сравнивать
    ///     было бы не с чем.
    ///     ⚠ Отрицательное плечо: масштаб НЕ степень двойки (3.0 и само
    ///     наибольшее значение) обязан дать различие, иначе «ноль» выше ничего
    ///     не доказывает — он был бы свойством сравнения, а не масштаба.
    ///
    ///  3. **Разведение не вакуумно.** Обе таблицы, ушедшие на путь формы,
    ///     действительно выходят за единицу. Если бы не выходили, разводить
    ///     было бы нечего, и правка была бы пустой.
    ///
    ///  4. **Обход живёт только в пробах.** В сборке приложения нет ни типа, ни
    ///     метода с именем `ShapeCurve`: путь человека к `CurveOf` не изменён.
    ///
    ///   doseboundprobef65 [--repo=&lt;корень дерева&gt;]
    ///   doseboundprobef65 --break=drop    (ждёт ОТКАЗ: снята точка 20 кэВ)
    ///   doseboundprobef65 --break=scale   (ждёт ОТКАЗ: масштаб 3.0 вместо 2^k)
    ///
    /// Коды возврата у `--break=` перевёрнуты: 0 — отказ получен (сторож
    /// смотрит), 1 — не получен (сторож слеп).
    /// </summary>
    static class Program
    {
        static int checks;
        static int failed;
        static string repo = ".";
        static string breakage = "";

        static int Main(string[] args)
        {
            // ⛔ (`T247`) Культура ЦЕЛИКОМ инвариантная, приказ Amber 05.09.2026. Проба
            //    не ставила её ВОВСЕ, и на русской машине часть её чисел шла с ЗАПЯТОЙ
            //    (замер 10.09.2026, полоса П8: мест без поставщика культуры — 5).
            //    Инвариант ЦЕЛИКОМ, а не клон с подменённым разделителем: клон
            //    чинит печать и оставляет РАЗБОР системным (`T245`).
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception)
            {
                // Консоль без UTF-8 — не повод не работать.
            }

            foreach (string a in args)
            {
                if (a.StartsWith("--repo=", StringComparison.Ordinal))
                {
                    repo = a.Substring(7);
                }
                else if (a.StartsWith("--break=", StringComparison.Ordinal))
                {
                    breakage = a.Substring(8);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            Console.WriteLine("=== DoseBoundProbeF65: A258 — граница ε ≤ 1 жива, нормировка бесплатна ===");
            Console.WriteLine("дерево: " + Path.GetFullPath(repo));
            if (breakage.Length > 0)
            {
                Console.WriteLine("⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: --break=" + breakage
                                  + " — проба ОБЯЗАНА отказать");
            }

            try
            {
                BoundOnRealCurve();
                BoundIsExactlyOne();
                NormalisationCostsNothing();
                SeparationIsNotVacuous();
                WorkaroundStaysInProbes();
            }
            catch (Exception ex)
            {
                Console.WriteLine("!! проба сорвалась: " + ex);
                return 3;
            }

            Console.WriteLine();
            if (breakage.Length > 0)
            {
                Console.WriteLine(failed > 0
                    ? string.Format(CultureInfo.InvariantCulture,
                        "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ «--break={0}»: отказ получен ({1} из {2})",
                        breakage, failed, checks)
                    : string.Format(CultureInfo.InvariantCulture,
                        "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ «--break={0}»: ОТКАЗА НЕТ — сторож слеп ({1} проверок)",
                        breakage, checks));
                return failed > 0 ? 0 : 1;
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
        // 1. Граница жива на НАСТОЯЩЕЙ кривой
        // ==================================================================

        const string ExportPath = @"LSRM Geometries\Exported Curves\Obsidian - marinelli 0.5.txt";

        /// <summary>Точки экспорта ЛСРМ: энергия, кэВ — эффективность — погрешность, %.</summary>
        static List<ROIEfficiencyData> ReadExport(out int skipped)
        {
            skipped = 0;
            var points = new List<ROIEfficiencyData>();
            string path = Path.Combine(repo, ExportPath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("нет экспорта ЛСРМ: " + Path.GetFullPath(path));
            }

            foreach (string raw in File.ReadAllLines(path))
            {
                string[] cells = raw.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (cells.Length < 2)
                {
                    continue;
                }

                double energy, efficiency;
                if (!double.TryParse(cells[0].Trim(), NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out energy)
                    || !double.TryParse(cells[1].Trim(), NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out efficiency))
                {
                    skipped++;      // заголовок
                    continue;
                }

                points.Add(new ROIEfficiencyData
                {
                    Energy = energy,
                    Efficiency = efficiency,
                    ErrorPercent = cells.Length > 2 ? Parse(cells[2]) : 1.0,
                });
            }

            return points;
        }

        static double Parse(string s)
        {
            double v;
            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                ? v : 1.0;
        }

        static void BoundOnRealCurve()
        {
            Console.WriteLine();
            Console.WriteLine("== граница ε ≤ 1 на НАСТОЯЩЕЙ кривой (экспорт ЛСРМ) ==");

            int skipped;
            List<ROIEfficiencyData> points = ReadExport(out skipped);
            int above = 0;
            double worst = 0.0, worstAt = 0.0;
            foreach (ROIEfficiencyData p in points)
            {
                if (p.Efficiency > 1.0)
                {
                    above++;
                    if (p.Efficiency > worst)
                    {
                        worst = p.Efficiency;
                        worstAt = p.Energy;
                    }
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0}: точек {1} (строк-заголовков {2}), выше единицы {3}, наибольшая {4:g6} на {5:f1} кэВ",
                ExportPath, points.Count, skipped, above, worst, worstAt));
            Ok(above == 1 && worst > 1.0, string.Format(CultureInfo.InvariantCulture,
                "в файле есть ровно одна точка выше единицы ({0:g6} на {1:f1} кэВ) — судить есть что",
                worst, worstAt));

            // ⚠ `--break=drop` снимает эту точку: отказа не будет, и проба
            // обязана это заметить.
            if (breakage == "drop")
            {
                points.RemoveAll(p => p.Efficiency > 1.0);
                Console.WriteLine("  --break=drop: точка выше единицы снята");
            }

            string message = null;
            try
            {
                DoseRateEstimator.CurveOf(points);
            }
            catch (DoseRateRefusalException ex)
            {
                message = ex.Message;
            }

            Console.WriteLine("  отказ: " + (message ?? "НЕ ПОЛУЧЕН"));
            Ok(message != null, "кривая с ε > 1 отвергнута (DoseRateRefusalException)");
            Ok(message != null
               && message.IndexOf("1471.85", StringComparison.Ordinal) >= 0
               && message.IndexOf("20.0", StringComparison.Ordinal) >= 0,
               "отказ НАЗЫВАЕТ величину 1471.85 и энергию 20.0 кэВ, а не молчит");

            // Отрицательное плечо: без этой точки кривая обязана построиться.
            var clean = new List<ROIEfficiencyData>();
            foreach (ROIEfficiencyData p in points)
            {
                if (p.Efficiency <= 1.0)
                {
                    clean.Add(p);
                }
            }

            DoseRateCurve curve = DoseRateEstimator.CurveOf(clean);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  без неё: {0} точек, {1:f0}...{2:f0} кэВ, ε(100 кэВ) = {3:g6}",
                curve.Count, curve.MinKev, curve.MaxKev, curve.At(100.0)));
            Ok(curve.Count == clean.Count && curve.At(100.0) > 0.0,
               "тот же файл без одной точки СТРОИТСЯ — граница не отвергает всё подряд");
        }

        static void BoundIsExactlyOne()
        {
            Console.WriteLine();
            Console.WriteLine("== граница стоит РОВНО на единице ==");

            Ok(Passes(1.0), "ε = 1 проходит (граница включительная)");
            Ok(!Passes(1.0 + 1e-12), "ε = 1 + 1e-12 отвергается");
            Ok(Passes(1.0 - 1e-12), "ε = 1 − 1e-12 проходит");
        }

        static bool Passes(double efficiency)
        {
            try
            {
                DoseRateEstimator.CurveOf(new List<ROIEfficiencyData>
                {
                    new ROIEfficiencyData { Energy = 100, Efficiency = 0.5, ErrorPercent = 1.0 },
                    new ROIEfficiencyData { Energy = 200, Efficiency = efficiency, ErrorPercent = 1.0 },
                    new ROIEfficiencyData { Energy = 300, Efficiency = 0.5, ErrorPercent = 1.0 },
                });
                return true;
            }
            catch (DoseRateRefusalException)
            {
                return false;
            }
        }

        // ==================================================================
        // 2. Нормировка не двигает чисел
        // ==================================================================

        static DoseRateCurve Build(double[] x, double[] y, double scale)
        {
            var points = new List<ROIEfficiencyData>();
            for (int i = 0; i < x.Length; i++)
            {
                points.Add(new ROIEfficiencyData
                {
                    Energy = x[i], Efficiency = y[i] / scale, ErrorPercent = 1.0,
                });
            }

            return DoseRateEstimator.CurveOf(points);
        }

        /// <summary>
        /// Худшее |Δ| между двумя нормировками одной таблицы: обе кривые
        /// возвращаются в исходную величину умножением на свой масштаб.
        /// </summary>
        static double WorstDiff(double[] x, double[] y, double a, double b, out double at)
        {
            DoseRateCurve first = Build(x, y, a);
            DoseRateCurve second = Build(x, y, b);
            double worst = 0.0;
            at = 0.0;
            for (int i = 0; i <= 2000; i++)
            {
                double e = x[0] + (x[x.Length - 1] - x[0]) * i / 2000.0;
                double d = Math.Abs(first.At(e) * a - second.At(e) * b);
                if (d > worst)
                {
                    worst = d;
                    at = e;
                }
            }

            return worst;
        }

        static void NormalisationCostsNothing()
        {
            Console.WriteLine();
            Console.WriteLine("== нормировка степенью двойки: цена ==");

            // ---------------------------------------------------------------
            // Плечо A. Таблица, которая проходит границу и БЕЗ нормировки, —
            // тут есть с чем сравнивать: масштаб 1 (то есть построение
            // напрямую, как строила проба до правки) против масштабов 2, 4,
            // …, 1024.
            //
            // ⚠ Одного «наименьшая степень двойки, не меньшая наибольшего» тут
            // мало: у кривой эффективности наибольшее ≤ 1, и такой масштаб
            // равен ЕДИНИЦЕ — сравнение вышло бы с самим собой. Первая
            // редакция этой проверки так и вышла вакуумной, поэтому масштабы
            // перебираются явно.
            // ---------------------------------------------------------------
            int skipped;
            List<ROIEfficiencyData> export = ReadExport(out skipped);
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (ROIEfficiencyData p in export)
            {
                if (p.Efficiency <= 1.0)
                {
                    xs.Add(p.Energy);
                    ys.Add(p.Efficiency);
                }
            }

            double[] x = xs.ToArray();
            double[] y = ys.ToArray();
            double[] powers = { 2.0, 4.0, 8.0, 64.0, 1024.0 };
            if (breakage == "scale")
            {
                powers = new[] { 3.0, 5.0 };
                Console.WriteLine("  --break=scale: масштабы 3 и 5 вместо степеней двойки");
            }

            double worstAll = 0.0;
            foreach (double scale in powers)
            {
                double at;
                double d = WorstDiff(x, y, 1.0, scale, out at);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  экспорт ЛСРМ, {0} узлов: масштаб 1 против {1:g6} — |Δ| = {2:e3} на {3:f1} кэВ",
                    x.Length, scale, d, at));
                if (d > worstAll)
                {
                    worstAll = d;
                }
            }

            Ok(worstAll == 0.0, string.Format(CultureInfo.InvariantCulture,
                "масштаб — степень двойки: кривая та же, что построенная напрямую, НИ НА БИТ не сдвинута"
                + " (худшее |Δ| = {0:e3} по {1} масштабам)", worstAll, powers.Length));

            // ---------------------------------------------------------------
            // Плечо B. ТЕ САМЫЕ таблицы, ради которых нормировка и заведена.
            // Прямой кривой у них не бывает вовсе — граница их отвергает, —
            // поэтому сравниваются два РАЗНЫХ допустимых масштаба: 2 и 8.
            // Если бы сплайн не был однороден, они разошлись бы.
            // ---------------------------------------------------------------
            double[] energies = { 40, 50, 60, 80, 100, 150, 200, 300, 400, 500, 600, 800,
                                  1000, 1500, 2000, 3000 };
            var sv = new double[OldRToSv.Length];
            for (int i = 0; i < sv.Length; i++)
            {
                sv[i] = OldRToSv[i] / DoseRateCoefficients.RemPerRoentgenFactor;
            }

            double atOld;
            double oldDiff = WorstDiff(energies, sv, 2.0, 8.0, out atOld);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  прежняя «RToSv»/0.876 (наибольшее 1.7352): масштаб 2 против 8 — |Δ| = {0:e3} на {1:f1} кэВ",
                oldDiff, atOld));

            double[] icrpE = Field("AmbientEnergyKev");
            double[] icrpY = Field("AmbientConversion");
            double atNew;
            double newDiff = WorstDiff(icrpE, icrpY, 2.0, 8.0, out atNew);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  ICRP 74 h*(10)/K_air (наибольшее 1.74): масштаб 2 против 8 — |Δ| = {0:e3} на {1:f1} кэВ",
                newDiff, atNew));
            Ok(oldDiff == 0.0 && newDiff == 0.0,
               "на САМИХ таблицах-формах два разных масштаба дают одну кривую до бита");

            // ⚠ Отрицательный контроль: если и не-степень двойки даёт ноль,
            // то нули выше — свойство сравнения, а не масштаба.
            double atThree;
            double three = WorstDiff(x, y, 1.0, 3.0, out atThree);
            double atFive;
            double five = WorstDiff(icrpE, icrpY, 2.0, 3.0, out atFive);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  отрицательный контроль: экспорт, 1 против 3.0 — |Δ| = {0:e3} на {1:f1} кэВ;"
                + " ICRP 74, 2 против 3.0 — |Δ| = {2:e3} на {3:f1} кэВ",
                three, atThree, five, atFive));
            Ok(three > 0.0 && five > 0.0,
               "масштаб НЕ степень двойки кривую двигает — сравнение не слепое");
        }

        // ==================================================================
        // 3. Разведение не вакуумно
        // ==================================================================

        static double[] Field(string name)
        {
            FieldInfo f = typeof(DoseRateCoefficients).GetField(
                name, BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null)
            {
                throw new InvalidOperationException(
                    "в DoseRateCoefficients нет поля " + name + " — проба смотрит не туда");
            }

            return (double[])f.GetValue(null);
        }

        static readonly double[] OldRToSv =
            { 1.29, 1.46, 1.52, 1.51, 1.44, 1.31, 1.22, 1.15, 1.10, 1.07, 1.04, 1.02, 1.01,
              0.99, 0.99, 0.98 };

        static void SeparationIsNotVacuous()
        {
            Console.WriteLine();
            Console.WriteLine("== то, что ушло на путь формы, действительно выше единицы ==");

            double maxOld = 0.0;
            foreach (double v in OldRToSv)
            {
                double sv = v / DoseRateCoefficients.RemPerRoentgenFactor;
                if (sv > maxOld)
                {
                    maxOld = sv;
                }
            }

            double maxNew = 0.0;
            foreach (double v in Field("AmbientConversion"))
            {
                if (v > maxNew)
                {
                    maxNew = v;
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  прежняя «RToSv»/0.876: наибольшее {0:f4} Зв/Гр; ICRP 74 h*(10)/K_air: {1:f4} Зв/Гр",
                maxOld, maxNew));
            Ok(maxOld > 1.0, string.Format(CultureInfo.InvariantCulture,
                "прежняя таблица выходит за единицу ({0:f4}) — разводить было что", maxOld));
            Ok(maxNew > 1.0, string.Format(CultureInfo.InvariantCulture,
                "таблица ICRP 74 выходит за единицу ({0:f4}) — разводить было что", maxNew));
        }

        // ==================================================================
        // 4. Обход живёт только в пробах
        // ==================================================================

        static void WorkaroundStaysInProbes()
        {
            Console.WriteLine();
            Console.WriteLine("== в приложении нормировки нет ==");

            Assembly app = typeof(DoseRateEstimator).Assembly;
            int found = 0;
            foreach (Type t in app.GetTypes())
            {
                if (t.Name.IndexOf("ShapeCurve", StringComparison.Ordinal) >= 0)
                {
                    found++;
                    Console.WriteLine("  найдено в приложении: " + t.FullName);
                }
            }

            Ok(found == 0,
               "в сборке приложения нет типа «ShapeCurve» — обход остался у проб, путь человека не тронут");
        }
    }
}
