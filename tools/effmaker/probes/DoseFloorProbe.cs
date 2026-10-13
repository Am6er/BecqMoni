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

namespace DoseFloorProbe
{
    /// <summary>
    /// Полоса П157 (24.09.2026), `S192`: НЕПРЕРЫВНО ЛИ ПОКАЗАНИЕ ДОЗЫ ПО ПОРОГУ
    /// ПОЛА ЭФФЕКТИВНОСТИ.
    ///
    /// Пол (<see cref="DoseRateManager.MinOwnEfficiencyFraction"/>) снимает
    /// диапазон, у которого своя доля меньше сотой от наибольшей. Прежде это был
    /// обрез: диапазон у самого пола давал полную дозу чуть выше него и ноль чуть
    /// ниже (П153: `ASN16_Cs137_10cm`, 13.1…17.2 кэВ — 8.6…11.2 % показания,
    /// переход через пол −7.7 %). Решение Amber 24.09.2026 вопросником, дословно:
    /// «Плавный пол (Рекомендую)».
    ///
    /// Проба двигает порог ОТРАЖЕНИЕМ — закрытым полем `floorFraction`
    /// (мерный рычаг, в приложении им никто не пишет) — по логарифмической сетке
    /// `--lo…--hi` от штатного и на каждом шаге считает показание настоящих
    /// спектров корпуса (четыре прибора, путь матрицы и путь «≈»). Приёмка —
    /// производная показания по порогу ограничена:
    ///
    ///     |Δ ln Ḣ / Δ ln порога| ≤ --limit (1.0) и |Δ ln Ḣ| ≤ --jump (0.5 %)
    ///     на каждом шаге сетки,
    ///
    /// то есть сдвиг порога на 1 % меняет показание не больше чем на 1 %, и
    /// ни на одном шаге показание не прыгает больше чем на полпроцента.
    /// Обрез этого не держит: скачок −9.4 % на шаге сетки 0.9 % — производная ~10.
    ///
    /// ⚠ Не всё, что осталось, — пол: на `ASN16_Cs137_10cm` по матрице
    /// остаётся скачок ~0.2 % у 1.2 штатного порога — диапазон 17…22 кэВ
    /// переходит порог «приписано не меньше пятой части отсчётов»
    /// (`DoseRateManager.RepresentativeMinShare`, `AMBER77`), его энергия
    /// прыгает с середины на подобранную; вес пола двигает приписку к нему
    /// плавно, но сам этот порог — обрез (журнал П157 §4). ✅ `S194`, П159:
    /// порог сделан плавным (<c>DoseRateManager.ShareTrust</c>), на сетке 1200
    /// шагов скачка больше нет; мерка по доле — `DoseShareProbe`.
    ///
    /// Для каждого диапазона, который на сетке переходит через пол, печатается
    /// таблица показания вокруг его доли (0.5…3×) — пороги, на которых он
    /// входит и выходит.
    ///
    ///     dosefloorprobe [--dir=&lt;корпус&gt;] [--steps=300] [--lo=0.25] [--hi=4]
    ///                    [--limit=1.0] [--jump=0.5] [--quiet] [--set=&lt;рычаг&gt;=&lt;число&gt;]
    ///
    /// Положительный контроль — проба на прежнем расчёте (обрез) с тем же
    /// рычагом: краснеет на `ASN16_Cs137_10cm`.
    /// </summary>
    static class Program
    {
        static int failed;
        static int checks;
        static string corpusDir = @"tools\CORPUS\corpus";
        static int steps = 300;
        static double lo = 0.25;
        static double hi = 4.0;
        static double limit = 1.0;
        static double jumpPercent = 0.5;
        static bool quiet;
        static FieldInfo floorField;
        static FieldInfo weightField;
        static FieldInfo bandField;
        static double standardBand;

        sealed class Scene
        {
            public string Spectrum;
            public string Matrix;
        }

        static readonly Scene[] Scenes =
        {
            new Scene { Spectrum = "AS80_Cs137_0cm", Matrix = "AS80_point0" },
            new Scene { Spectrum = "G1S16_Cs137_P5", Matrix = "G1S_point5_p16" },
            new Scene { Spectrum = "RC103_Cs137_0cm", Matrix = "RC103_point0" },
            new Scene { Spectrum = "ASN16_Cs137_10cm", Matrix = "ASN16_point10_house" },
        };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) corpusDir = a.Substring(6);
                else if (a.StartsWith("--steps=", StringComparison.Ordinal))
                    steps = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--lo=", StringComparison.Ordinal))
                    lo = double.Parse(a.Substring(5), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--hi=", StringComparison.Ordinal))
                    hi = double.Parse(a.Substring(5), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--limit=", StringComparison.Ordinal))
                    limit = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--jump=", StringComparison.Ordinal))
                    jumpPercent = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a == "--quiet") quiet = true;
                else if (a.StartsWith("--set=", StringComparison.Ordinal))
                {
                    // (`S194`, П159) Мерный рычаг расчёта: закрытое статическое
                    // поле `DoseRateManager` — отражением, для замера вариантов.
                    string[] kv = a.Substring(6).Split('=');
                    FieldInfo lever = kv.Length == 2
                        ? typeof(DoseRateManager).GetField(kv[0], BindingFlags.NonPublic | BindingFlags.Static)
                        : null;
                    if (lever == null || lever.FieldType != typeof(double))
                    {
                        Console.Error.WriteLine("нет рычага: " + a);
                        return 2;
                    }

                    lever.SetValue(null, double.Parse(kv[1], CultureInfo.InvariantCulture));
                    Console.WriteLine("рычаг " + kv[0] + " = " + kv[1]);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            floorField = typeof(DoseRateManager).GetField("floorFraction", BindingFlags.NonPublic | BindingFlags.Static);
            if (floorField == null || floorField.FieldType != typeof(double))
            {
                Console.WriteLine("ОСНАСТКА: у приложения нет мерного рычага пола `DoseRateManager.floorFraction`");
                return 2;
            }

            // Вес диапазона есть только у расчёта с плавным полом — отражением.
            weightField = typeof(DoseRateRange).GetField("Weight");
            bandField = typeof(DoseRateManager).GetField("floorBand", BindingFlags.NonPublic | BindingFlags.Static);
            if (bandField != null) standardBand = (double)bandField.GetValue(null);
            double standard = DoseRateManager.MinOwnEfficiencyFraction;
            if ((double)floorField.GetValue(null) != standard)
            {
                Console.WriteLine("ОСНАСТКА: рычаг пола не равен штатному порогу до начала мерки");
                return 2;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "П157 (S192): показание дозы по порогу пола; штатный порог {0}, сетка {1}…{2}× в {3} шагов"
                + " (шаг ln {4:F4}), допуск |d ln Ḣ/d ln порога| ≤ {5}; плавный пол у приложения: {6}",
                standard, lo, hi, steps, Math.Log(hi / lo) / steps, limit, weightField != null ? "есть" : "НЕТ"));

            try
            {
                foreach (Scene scene in Scenes)
                {
                    ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", scene.Spectrum + ".xml"));
                    if (data == null || data.Efficiency == null || !data.Efficiency.HasGeometry)
                    {
                        Console.WriteLine("ОСНАСТКА: нет спектра или кривой с геометрией: " + scene.Spectrum);
                        return 2;
                    }

                    ResponseMatrix matrix = LoadMatrix(Path.Combine(corpusDir, "geometries", scene.Matrix + ".rmx"),
                                                       data.Efficiency.Geometry);
                    if (matrix == null)
                    {
                        Console.WriteLine("ОСНАСТКА: нет матрицы " + scene.Matrix);
                        return 2;
                    }

                    Scan(scene.Spectrum + " матрица", data, DoseRateInput.Of(data.Efficiency, matrix), standard);
                    Scan(scene.Spectrum + " «≈»", data, DoseRateInput.Of(data.Efficiency, null), standard);
                }
            }
            finally
            {
                floorField.SetValue(null, standard);
                if (bandField != null) bandField.SetValue(null, standardBand);
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "СОШЛОСЬ (" + checks.ToString(CultureInfo.InvariantCulture) + ")"
                : "РАЗОШЛОСЬ: " + failed.ToString(CultureInfo.InvariantCulture)
                  + " из " + checks.ToString(CultureInfo.InvariantCulture));
            return failed == 0 ? 0 : 1;
        }

        static DoseRate At(DoseRateManager manager, ResultData data, DoseRateInput input, double floor)
        {
            floorField.SetValue(null, floor);
            return manager.Calculate(data, input);
        }

        static void Scan(string name, ResultData data, DoseRateInput input, double standard)
        {
            Head(name);
            var manager = new DoseRateManager(Config());
            // Рычаг — на штатный ДО опорного расчёта: прошлая сцена оставила его
            // там, где кончилась её таблица.
            floorField.SetValue(null, standard);
            DoseRate reference = manager.Calculate(data, input);
            DoseRate atStandard = At(manager, data, input, standard);
            if (!string.IsNullOrEmpty(reference.Refusal))
            {
                Console.WriteLine("  ОТКАЗ: " + reference.Refusal);
                Ok(false, name + ": расчёт отказал");
                return;
            }

            Ok(reference.Rate == atStandard.Rate,
               string.Format(CultureInfo.InvariantCulture,
                   "{0}: рычаг на штатном пороге даёт то же показание побитово ({1:R} = {2:R})",
                   name, reference.Rate, atStandard.Rate));

            double lnStep = Math.Log(hi / lo) / steps;
            var floors = new double[steps + 1];
            var rates = new double[steps + 1];
            var skipped = new bool[steps + 1][];
            for (int j = 0; j <= steps; j++)
            {
                floors[j] = standard * lo * Math.Exp(j * lnStep);
                DoseRate d = At(manager, data, input, floors[j]);
                rates[j] = string.IsNullOrEmpty(d.Refusal) ? d.Rate : double.NaN;
                skipped[j] = d.Ranges.Select(r => r.Skipped).ToArray();
            }

            double worst = 0.0;
            int worstAt = -1;
            for (int j = 1; j <= steps; j++)
            {
                double deriv = Math.Abs(Math.Log(rates[j] / rates[j - 1])) / lnStep;
                if (double.IsNaN(deriv) || deriv > worst)
                {
                    worst = double.IsNaN(deriv) ? double.PositiveInfinity : deriv;
                    worstAt = j;
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  штатно {0:R} мкЗв/ч; на сетке {1:F6}…{2:F6} мкЗв/ч", reference.Rate, rates.Min(), rates.Max()));
            if (worstAt > 0)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  худший шаг: порог {0:F3}→{1:F3}× штатного, показание {2:F6} → {3:F6} ({4:+0.000;-0.000} %),"
                    + " |d ln Ḣ/d ln порога| = {5:F3}",
                    floors[worstAt - 1] / standard, floors[worstAt] / standard, rates[worstAt - 1], rates[worstAt],
                    100.0 * (rates[worstAt] / rates[worstAt - 1] - 1.0), worst));
                // Кто сдвинулся на худшем шаге: три диапазона с наибольшим
                // изменением дозы.
                DoseRate before = At(manager, data, input, floors[worstAt - 1]);
                DoseRate after = At(manager, data, input, floors[worstAt]);
                foreach (int k in Enumerable.Range(0, before.Ranges.Count)
                             .OrderByDescending(k => Math.Abs(after.Ranges[k].DoseRate - before.Ranges[k].DoseRate))
                             .Take(3))
                {
                    DoseRateRange p = before.Ranges[k], q = after.Ranges[k];
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "      {0,9:F2}…{1,9:F2} кэВ: доза {2:E4} → {3:E4}, энергия {4:F3} → {5:F3}, своя {6:E4} → {7:E4},"
                        + " объяснено {8:F1} → {9:F1}{10}{11}",
                        p.LowKev, p.HighKev, p.DoseRate, q.DoseRate, Representative(p), Representative(q),
                        p.OwnEfficiency, q.OwnEfficiency, p.Explained, q.Explained,
                        p.Skipped ? " (снят)" : "", q.Skipped ? " (→ снят)" : ""));
                }
            }

            Ok(worst <= limit, string.Format(CultureInfo.InvariantCulture,
                "{0}: показание непрерывно по порогу — max |d ln Ḣ/d ln порога| = {1:F3} ≤ {2}", name, worst, limit));
            double biggest = 0.0;
            for (int j = 1; j <= steps; j++)
            {
                double step = 100.0 * Math.Abs(Math.Log(rates[j] / rates[j - 1]));
                biggest = double.IsNaN(step) ? double.PositiveInfinity : Math.Max(biggest, step);
            }

            Ok(biggest <= jumpPercent, string.Format(CultureInfo.InvariantCulture,
                "{0}: скачка нет — наибольший сдвиг показания за шаг {1:F3} % ≤ {2} %", name, biggest, jumpPercent));

            // Диапазоны, переходящие через пол на сетке, — таблица вокруг их доли.
            int bins = reference.Ranges.Count;
            for (int k = 0; k < bins; k++)
            {
                for (int j = 1; j <= steps; j++)
                {
                    if (skipped[j - 1][k] == skipped[j][k])
                    {
                        continue;
                    }

                    // Бисекция порога, на котором диапазон k снимается.
                    double a = floors[j - 1], b = floors[j];
                    for (int it = 0; it < 40; it++)
                    {
                        double m = Math.Sqrt(a * b);
                        bool s = At(manager, data, input, m).Ranges[k].Skipped;
                        if (s == skipped[j - 1][k]) a = m; else b = m;
                    }

                    double x0 = Math.Sqrt(a * b);
                    DoseRateRange r = reference.Ranges[k];
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  диапазон {0:F2}…{1:F2} кэВ снимается полом при пороге x0 = {2:F5}× штатного ({3:E4});"
                        + " штатно: отсчётов {4:F0}, доза {5:E4} мкЗв/ч ({6:F2} % показания)",
                        r.LowKev, r.HighKev, x0 / standard, x0, r.Counts, r.DoseRate,
                        100.0 * r.DoseRate / reference.Rate));
                    Console.WriteLine("      порог/x0   порог/штатн   показание, мкЗв/ч   к 0.5·x0, %   вес диапазона   доза диапазона");
                    double first = double.NaN;
                    foreach (double t in new[] { 0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 0.99, 0.999, 1.001, 1.01, 1.05, 1.1,
                                                  1.25, 1.5, 2.0, 2.5, 3.0 })
                    {
                        DoseRate d = At(manager, data, input, t * x0);
                        if (double.IsNaN(first)) first = d.Rate;
                        DoseRateRange rr = d.Ranges[k];
                        string w = weightField != null
                            ? ((double)weightField.GetValue(rr)).ToString("F4", CultureInfo.InvariantCulture)
                            : (rr.Skipped ? "0 (снят)" : "1");
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "      {0,7:F3}   {1,11:F5}   {2,17:F8}   {3,11:+0.000;-0.000}   {4,13}   {5:E4}",
                            t, t * x0 / standard, d.Rate, 100.0 * (d.Rate / first - 1.0), w, rr.DoseRate));
                    }

                    break;
                }
            }

            // Ширина полосы плавного пола — обоснование замером: показание на
            // штатном пороге и худшая производная по порогу при разной ширине.
            if (bandField != null)
            {
                double keep = (double)bandField.GetValue(null);
                try
                {
                    foreach (double band in new[] { 1.25, 1.5, 2.0, 3.0, 4.0 })
                    {
                        bandField.SetValue(null, band);
                        DoseRate d = At(manager, data, input, standard);
                        double w = 0.0;
                        int wAt = -1;
                        double prev = double.NaN;
                        const int coarse = 150;
                        double lnc = Math.Log(hi / lo) / coarse;
                        for (int j = 0; j <= coarse; j++)
                        {
                            double rj = At(manager, data, input, standard * lo * Math.Exp(j * lnc)).Rate;
                            if (j > 0)
                            {
                                double deriv = Math.Abs(Math.Log(rj / prev)) / lnc;
                                if (double.IsNaN(deriv) || deriv > w)
                                {
                                    w = double.IsNaN(deriv) ? double.PositiveInfinity : deriv;
                                    wAt = j;
                                }
                            }

                            prev = rj;
                        }

                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "  полоса {0:F2}× пола: показание на штатном пороге {1:F8} ({2:+0.000;-0.000} % к штатной полосе),"
                            + " худшая |d ln Ḣ/d ln порога| {3:F3} (сетка {4} шагов)",
                            band, d.Rate, 100.0 * (d.Rate / reference.Rate - 1.0), w, coarse));
                    }
                }
                finally
                {
                    bandField.SetValue(null, keep);
                    floorField.SetValue(null, standard);
                }
            }
        }

        /// <summary>Представительная энергия — отражением (у старых сборок поля нет).</summary>
        static double Representative(DoseRateRange r)
        {
            FieldInfo f = typeof(DoseRateRange).GetField("RepresentativeKev");
            return f != null ? (double)f.GetValue(r) : r.CenterKev;
        }

        // ------------------------------------------------------------------

        static void Head(string text)
        {
            Console.WriteLine();
            Console.WriteLine(text);
            Console.WriteLine(new string('-', Math.Min(100, text.Length)));
        }

        static void Ok(bool ok, string what)
        {
            checks++;
            if (!ok) failed++;
            if (!quiet || !ok) Console.WriteLine("  [" + (ok ? "ок" : "НЕТ") + "]   " + what);
        }

        static ResultData LoadSpectrum(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("  нет " + path);
                return null;
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                return file.ResultDataList.Count > 0 ? file.ResultDataList[0] : null;
            }
        }

        static ResponseMatrix LoadMatrix(string path, GeometryModel geometry)
        {
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrix.Load(path, out refusal, out fileFormat);
            if (matrix == null)
            {
                Console.WriteLine("  матрица {0}: {1} (формат {2})", path, refusal, fileFormat);
                return null;
            }

            if (geometry != null && !matrix.IsValidFor(geometry))
            {
                // Как у `DoseGridProbe`: склад посчитан прежним поколением физики,
                // клеймо пересчитывается в памяти, файл не пишется.
                matrix.Stamp = ResponseMatrix.ComputeStamp(geometry, matrix.Options);
            }

            return matrix;
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
    }
}
