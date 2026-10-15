using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Измеренная точка кривой ЛСРМ из `.efa`/`.efr`: строка
    /// `энергия=эффективность,погрешность %,нуклид,площадь,погрешность площади,выход %[, No]`.
    /// </summary>
    public sealed class LsrmMeasuredPoint
    {
        public double Energy;

        public double Efficiency;

        /// <summary>Заявленная ЛСРМ погрешность точки, %.</summary>
        public double ErrorPercent;

        public string Nuclide = "";

        /// <summary>Источник (третья часть имени секции `.efr`); у `.efa` пусто.</summary>
        public string Source = "";

        /// <summary>Хвост «, No»: точку исключил из градуировки сам ЛСРМ.</summary>
        public bool Excluded;
    }

    /// <summary>
    /// Зона подгонки `.efa`: `lg ε = Σ c_j·P_j(lg E)` на отрезке
    /// `[LogLo, LogHi]` по lg E (кэВ). `P_j` — ортогональные с весами 1/σ²
    /// полиномы самого ЛСРМ (`Curve_i_j`, старшая степень первой), `c_j` —
    /// `Curve_i`. Проверено на файлах поставки 15.10.2026: нуль `P_2` лежит в
    /// средневзвешенном (1/σ²) lg E точек зоны, кривая «Точечная-25см» повторяет
    /// свои точные точки до 0.5 %.
    /// </summary>
    public sealed class LsrmZone
    {
        public int Degree;

        public double LogLo;

        public double LogHi;

        /// <summary>
        /// Четвёртое число строки `Zone_i`. По файлам поставки — разброс подгонки
        /// в lg ε (0.004…0.06; у зон с большим разбросом точек он больше), но
        /// описания формата ЛСРМ под рукой нет, и смысл числа доказан только
        /// этим сходством.
        /// </summary>
        public double Sigma;

        public double[][] Basis;

        public double[] Coefficients;

        public double EvalLog(double x)
        {
            double y = 0.0;
            for (int j = 0; j < this.Coefficients.Length; j++)
            {
                double p = 0.0;
                foreach (double c in this.Basis[j])
                {
                    p = p * x + c;
                }

                y += this.Coefficients[j] * p;
            }

            return y;
        }
    }

    /// <summary>
    /// Одна геометрия файла ЛСРМ: секция `.efa` или все секции `.efr` с
    /// одинаковыми «детектор;геометрия».
    /// </summary>
    public sealed class LsrmCurveSet
    {
        public string Detector = "";

        public string Geometry = "";

        /// <summary>Шапка первой секции — пары «ключ=значение» в порядке файла (без точек и зон).</summary>
        public List<KeyValuePair<string, string>> Header = new List<KeyValuePair<string, string>>();

        public List<LsrmMeasuredPoint> Points = new List<LsrmMeasuredPoint>();

        public List<LsrmZone> Zones = new List<LsrmZone>();

        /// <summary>Источники `.efr` (третья часть имени секции) в порядке файла.</summary>
        public List<string> Sources = new List<string>();

        /// <summary>Значение шапки по ключу; пусто — нет такого.</summary>
        public string HeaderValue(string key)
        {
            foreach (KeyValuePair<string, string> pair in this.Header)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }

            return "";
        }

        public string Title
        {
            get
            {
                return string.IsNullOrEmpty(this.Detector)
                    ? this.Geometry
                    : this.Geometry + " (" + this.Detector + ")";
            }
        }
    }

    /// <summary>Что взято кривой и что отброшено — для журнала и заметки кривой.</summary>
    public sealed class LsrmCurveReport
    {
        /// <summary>Кривая — подгонка ЛСРМ (`.efa`); иначе — измеренные точки (`.efr`).</summary>
        public bool FromFit;

        public int Zones;

        public int ExcludedByLsrm;

        public int OverMaxError;

        public int MergedDuplicates;

        /// <summary>Узлов сетки, попавших в разрыв между зонами (там кривой нет).</summary>
        public int GapNodes;
    }

    /// <summary>
    /// (`AMBER226`, П330 15.10.2026) Читатель файлов эффективности программы
    /// ЛСРМ «Efficiency»: `.efa` — измеренные точки и подгонка по зонам, `.efr`
    /// — точки, разбитые по источникам. Текст в стиле INI, однобайтная
    /// кириллица (cp1251), секция `[детектор;геометрия]` (у `.efr` —
    /// `[детектор;геометрия;источник]`); в одном `.efa` бывает несколько
    /// геометрий (сводный `…_2024.efa` — четыре).
    ///
    /// Решения Amber 15.10.2026 вопросником, дословно: из `.efa` — «Подгонку
    /// ЛСРМ (Рекомендую)», из `.efr` — «Точки как есть (Рекомендую)».
    ///
    /// Статический и без окна нарочно — его проверяет проба
    /// `P330LsrmCurveImportProbe` на файлах поставки.
    /// </summary>
    public static class LsrmEfficiencyFile
    {
        /// <summary>
        /// Плотность сетки, на которой считается подгонка `.efa`, — узлов на
        /// декаду энергии. Экспорт `EffCalcMC.txt` несёт около 150 узлов на
        /// 20…3000 кэВ (≈ 70 на декаду); подгонка — гладкий полином, и 50 на
        /// декаду дают между узлами отклонение ломаной от неё меньше 0.01 %.
        /// </summary>
        public const int NodesPerDecade = 50;

        /// <summary>Разные энергии ближе этого — одна линия разных источников `.efr`, кэВ.</summary>
        public const double SameEnergyKev = 0.01;

        /// <summary>Допуск края зоны по lg E — против округления, не по существу.</summary>
        const double EdgeTolerance = 1e-12;

        /// <summary>Строка активности источника в `.efr` (`Cs-137=328.35,2.5,1`) — не шапка геометрии.</summary>
        static readonly Regex NuclideKey = new Regex(@"^[A-Z][a-z]?-[0-9]+m?[0-9]?$", RegexOptions.CultureInvariant);

        /// <summary>Файл кривой (`.txt`, `.efa`, `.efr`), а не геометрии `.in`.</summary>
        public static bool IsCurveFile(string path)
        {
            string ext = SafeExtension(path);
            return ext == ".txt" || ext == ".efa" || ext == ".efr";
        }

        /// <summary>`.efa` — кривой берётся подгонка; `.efr` — точки.</summary>
        public static bool IsApproximation(string path)
        {
            return SafeExtension(path) == ".efa";
        }

        static string SafeExtension(string path)
        {
            try
            {
                return (Path.GetExtension(path) ?? "").ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return "";
            }
        }

        /// <summary>
        /// Текст файла: UTF-8 с BOM или без, если байты им разбираются; иначе
        /// cp1251, в которой пишет ЛСРМ. Кириллица cp1251 почти всегда негодна
        /// как UTF-8, поэтому строгая попытка различает их надёжно.
        /// </summary>
        static string ReadText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(1251).GetString(bytes);
            }
        }

        static readonly Regex PointKey = new Regex(@"^[0-9]+(\.[0-9]+)?$", RegexOptions.CultureInvariant);

        static readonly Regex ZoneKey = new Regex(@"^Zone_([0-9]+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        static readonly Regex BasisKey = new Regex(@"^Curve_([0-9]+)_([0-9]+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        static readonly Regex CurveKey = new Regex(@"^Curve_([0-9]+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        static bool TryNumber(string text, out double value)
        {
            return double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>Сырые зоны секции, пока строки `Zone_i`/`Curve_i_j`/`Curve_i` собираются.</summary>
        sealed class RawZones
        {
            public int Declared = -1;
            public readonly Dictionary<int, double[]> Zone = new Dictionary<int, double[]>();
            public readonly Dictionary<int, Dictionary<int, double[]>> Basis = new Dictionary<int, Dictionary<int, double[]>>();
            public readonly Dictionary<int, double[]> Curve = new Dictionary<int, double[]>();
        }

        /// <summary>
        /// Прочитать `.efa`/`.efr`. null — файл негоден, причина в
        /// <paramref name="problem"/>. Геометрии — в порядке файла.
        /// </summary>
        public static List<LsrmCurveSet> Read(string path, out string problem)
        {
            problem = null;
            string text;
            try
            {
                text = ReadText(path);
            }
            catch (Exception ex)
            {
                problem = string.Format(CultureInfo.InvariantCulture, "{0}: {1}", path, ex.Message);
                return null;
            }

            bool approximation = IsApproximation(path);
            List<LsrmCurveSet> sets = new List<LsrmCurveSet>();
            Dictionary<string, LsrmCurveSet> byKey = new Dictionary<string, LsrmCurveSet>(StringComparer.Ordinal);
            Dictionary<LsrmCurveSet, RawZones> zones = new Dictionary<LsrmCurveSet, RawZones>();
            LsrmCurveSet current = null;
            string source = "";
            bool headerOpen = false;
            string[] lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    string[] parts = line.Substring(1, line.Length - 2).Split(';');
                    string detector = parts.Length > 1 ? parts[0].Trim() : "";
                    string geometry = parts.Length > 1 ? parts[1].Trim() : parts[0].Trim();
                    source = parts.Length > 2 ? string.Join(";", parts, 2, parts.Length - 2).Trim() : "";

                    // `.efr` — секция на источник, геометрия у них общая: точки
                    // собираются в одну. У `.efa` секция — своя геометрия со своей
                    // подгонкой, и две секции одного имени не сливаются.
                    string key = detector + ";" + geometry;
                    if (!approximation && byKey.TryGetValue(key, out current))
                    {
                        headerOpen = false;
                    }
                    else
                    {
                        current = new LsrmCurveSet { Detector = detector, Geometry = geometry };
                        sets.Add(current);
                        zones[current] = new RawZones();
                        if (!approximation)
                        {
                            byKey[key] = current;
                        }

                        headerOpen = true;
                    }

                    if (source.Length > 0)
                    {
                        current.Sources.Add(source);
                    }

                    continue;
                }

                int eq = line.IndexOf('=');
                if (current == null || eq <= 0)
                {
                    continue;
                }

                string name = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (PointKey.IsMatch(name))
                {
                    LsrmMeasuredPoint point;
                    if (!TryPoint(name, value, source, out point))
                    {
                        problem = string.Format(CultureInfo.InvariantCulture,
                            Text("LsrmCurveBadPoint",
                                "{0} (line {1}): \"{2}\" is not an LSRM efficiency point"
                                + " (energy=efficiency,uncertainty %,nuclide,...)."),
                            path, i + 1, line);
                        return null;
                    }

                    current.Points.Add(point);
                    continue;
                }

                RawZones raw = zones[current];
                Match m;
                if (string.Equals(name, "Zones", StringComparison.OrdinalIgnoreCase))
                {
                    double declared;
                    if (!TryNumber(value, out declared) || declared < 0 || declared != Math.Floor(declared))
                    {
                        return BadLine(path, i + 1, line, out problem);
                    }

                    raw.Declared = (int)declared;
                }
                else if ((m = ZoneKey.Match(name)).Success)
                {
                    double[] numbers;
                    if (!TryNumbers(value, out numbers) || numbers.Length != 4)
                    {
                        return BadLine(path, i + 1, line, out problem);
                    }

                    raw.Zone[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] = numbers;
                }
                else if ((m = BasisKey.Match(name)).Success)
                {
                    double[] numbers;
                    if (!TryNumbers(value, out numbers))
                    {
                        return BadLine(path, i + 1, line, out problem);
                    }

                    int zone = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    Dictionary<int, double[]> basis;
                    if (!raw.Basis.TryGetValue(zone, out basis))
                    {
                        raw.Basis[zone] = basis = new Dictionary<int, double[]>();
                    }

                    basis[int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)] = numbers;
                }
                else if ((m = CurveKey.Match(name)).Success)
                {
                    double[] numbers;
                    if (!TryNumbers(value, out numbers))
                    {
                        return BadLine(path, i + 1, line, out problem);
                    }

                    raw.Curve[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] = numbers;
                }
                else if (headerOpen && !NuclideKey.IsMatch(name))
                {
                    current.Header.Add(new KeyValuePair<string, string>(name, value));
                }
            }

            if (sets.Count == 0)
            {
                problem = string.Format(CultureInfo.InvariantCulture,
                    Text("LsrmCurveNoSections",
                        "{0}: no LSRM section ([detector;geometry]) found - this is not an .efa / .efr file."),
                    path);
                return null;
            }

            foreach (LsrmCurveSet set in sets)
            {
                string zoneProblem = BuildZones(zones[set], set.Zones);
                if (zoneProblem != null)
                {
                    problem = string.Format(CultureInfo.InvariantCulture,
                        Text("LsrmCurveBadZone", "{0}: geometry \"{1}\": the LSRM fit is broken - {2}."),
                        path, set.Title, zoneProblem);
                    return null;
                }
            }

            return sets;
        }

        static List<LsrmCurveSet> BadLine(string path, int lineNumber, string line, out string problem)
        {
            problem = string.Format(CultureInfo.InvariantCulture,
                Text("LsrmCurveBadLine", "{0} (line {1}): \"{2}\" - numbers expected."),
                path, lineNumber, line);
            return null;
        }

        static bool TryNumbers(string value, out double[] numbers)
        {
            string[] cells = value.Split(',');
            numbers = new double[cells.Length];
            for (int k = 0; k < cells.Length; k++)
            {
                if (!TryNumber(cells[k], out numbers[k]))
                {
                    return false;
                }
            }

            return cells.Length > 0;
        }

        static bool TryPoint(string name, string value, string source, out LsrmMeasuredPoint point)
        {
            point = null;
            string[] cells = value.Split(',');
            double energy, efficiency, error;
            if (cells.Length < 3 || !TryNumber(name, out energy) || !TryNumber(cells[0], out efficiency)
                || !TryNumber(cells[1], out error))
            {
                return false;
            }

            bool excluded = false;
            for (int k = 6; k < cells.Length; k++)
            {
                if (string.Equals(cells[k].Trim(), "No", StringComparison.OrdinalIgnoreCase))
                {
                    excluded = true;
                }
            }

            point = new LsrmMeasuredPoint
            {
                Energy = energy,
                Efficiency = efficiency,
                ErrorPercent = error,
                Nuclide = cells[2].Trim(),
                Source = source,
                Excluded = excluded,
            };
            return true;
        }

        /// <summary>Собрать зоны из строк; null — собраны (или зон нет вовсе), иначе что не так.</summary>
        static string BuildZones(RawZones raw, List<LsrmZone> into)
        {
            if (raw.Declared < 0 && raw.Zone.Count == 0)
            {
                return null;
            }

            int count = raw.Declared >= 0 ? raw.Declared : raw.Zone.Count;
            for (int i = 1; i <= count; i++)
            {
                double[] zone;
                double[] curve;
                Dictionary<int, double[]> basis;
                if (!raw.Zone.TryGetValue(i, out zone))
                {
                    return string.Format(CultureInfo.InvariantCulture, "Zones={0}, but Zone_{1} is missing", count, i);
                }

                int degree = (int)zone[0];
                if (zone[0] != degree || degree < 0 || !(zone[2] > zone[1]))
                {
                    return string.Format(CultureInfo.InvariantCulture,
                        "Zone_{0}: degree {1}, lg E {2}...{3}", i, zone[0], zone[1], zone[2]);
                }

                if (!raw.Curve.TryGetValue(i, out curve) || curve.Length != degree + 1)
                {
                    return string.Format(CultureInfo.InvariantCulture,
                        "Curve_{0} must hold {1} coefficient(s)", i, degree + 1);
                }

                raw.Basis.TryGetValue(i, out basis);
                double[][] polys = new double[degree + 1][];
                for (int j = 1; j <= degree + 1; j++)
                {
                    double[] poly;
                    if (basis == null || !basis.TryGetValue(j, out poly) || poly.Length != j)
                    {
                        return string.Format(CultureInfo.InvariantCulture,
                            "Curve_{0}_{1} must hold {1} coefficient(s)", i, j);
                    }

                    polys[j - 1] = poly;
                }

                into.Add(new LsrmZone
                {
                    Degree = degree,
                    LogLo = zone[1],
                    LogHi = zone[2],
                    Sigma = zone[3],
                    Basis = polys,
                    Coefficients = curve,
                });
            }

            return null;
        }

        /// <summary>
        /// Подгонка ЛСРМ в точке. Ложь — энергия вне всех зон (за краями или в
        /// разрыве между ними): ЗА ЗОНЫ НЕ ПРОДОЛЖАЕТСЯ, полином вне своего
        /// отрезка — выдумка.
        ///
        /// ⚠ Перекрытие зон сшивается ЛИНЕЙНЫМ ВЕСОМ по lg E (моё решение,
        /// названо вслух: правила самого ЛСРМ под рукой нет). Каждая зона в
        /// полосе перекрытия с соседом входит с весом, растущим от 0 на её
        /// крае до 1 на крае соседа, и lg ε — взвешенное среднее. Резкий переход
        /// дал бы ступеньку: на файлах поставки зоны у краёв перекрытия
        /// расходятся до 26 % («Петри-60» сводного файла, 363 кэВ), у середины —
        /// на единицы процентов. Погрешность — тем же весом из
        /// <see cref="LsrmZone.Sigma"/>: (10^σ − 1)·100 %.
        /// </summary>
        public static bool TryEvalFit(List<LsrmZone> zones, double energyKev,
                                      out double efficiency, out double errorPercent)
        {
            efficiency = 0.0;
            errorPercent = 0.0;
            if (zones == null || zones.Count == 0 || !(energyKev > 0.0))
            {
                return false;
            }

            List<LsrmZone> sorted = zones.OrderBy(z => z.LogLo).ToList();
            double logEnergy = Math.Log10(energyKev);
            double weightSum = 0.0, logSum = 0.0, sigmaSum = 0.0;
            double plainLog = 0.0, plainSigma = 0.0;
            int covering = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                LsrmZone zone = sorted[i];
                // Допуск на краю — только против округления lg(10^x): узел
                // сетки, поставленный ровно на край зоны, иначе выпадал бы.
                if (logEnergy < zone.LogLo - EdgeTolerance || logEnergy > zone.LogHi + EdgeTolerance)
                {
                    continue;
                }

                double x = Math.Min(Math.Max(logEnergy, zone.LogLo), zone.LogHi);
                double y = zone.EvalLog(x);
                covering++;
                plainLog += y;
                plainSigma += zone.Sigma;

                double width = zone.LogHi - zone.LogLo;
                double left = i > 0 ? Math.Min(width, Math.Max(0.0, sorted[i - 1].LogHi - zone.LogLo)) : 0.0;
                double right = i + 1 < sorted.Count
                    ? Math.Min(width, Math.Max(0.0, zone.LogHi - sorted[i + 1].LogLo))
                    : 0.0;
                double w = 1.0;
                if (left > 0.0 && x < zone.LogLo + left)
                {
                    w *= (x - zone.LogLo) / left;
                }

                if (right > 0.0 && x > zone.LogHi - right)
                {
                    w *= (zone.LogHi - x) / right;
                }

                weightSum += w;
                logSum += w * y;
                sigmaSum += w * zone.Sigma;
            }

            if (covering == 0)
            {
                return false;
            }

            double logEff, sigma;
            if (weightSum > 0.0)
            {
                logEff = logSum / weightSum;
                sigma = sigmaSum / weightSum;
            }
            else
            {
                // Точка ровно на крае, где у всех покрывающих зон вес нуль, —
                // простое среднее, а не отказ: энергия внутри зон.
                logEff = plainLog / covering;
                sigma = plainSigma / covering;
            }

            efficiency = Math.Pow(10.0, logEff);
            errorPercent = (Math.Pow(10.0, Math.Abs(sigma)) - 1.0) * 100.0;
            return efficiency > 0.0 && !double.IsInfinity(efficiency);
        }

        /// <summary>
        /// Кривая для конфигурации из геометрии файла: у `.efa`
        /// (<paramref name="fromFit"/>) — подгонка на сетке
        /// <see cref="NodesPerDecade"/> внутри зон, у `.efr` — измеренные точки.
        /// null — кривой не вышло, причина в <paramref name="problem"/>.
        /// </summary>
        public static List<ROIEfficiencyData> BuildCurve(LsrmCurveSet set, bool fromFit, string path,
                                                         out LsrmCurveReport report, out string problem)
        {
            report = new LsrmCurveReport { FromFit = fromFit, Zones = set.Zones.Count };
            problem = null;
            foreach (LsrmMeasuredPoint point in set.Points)
            {
                if (point.Excluded)
                {
                    report.ExcludedByLsrm++;
                }
            }

            if (fromFit && set.Zones.Count == 0)
            {
                problem = string.Format(CultureInfo.InvariantCulture,
                    Text("LsrmCurveNoFit",
                        "{0}: geometry \"{1}\" holds no LSRM fit (Zones / Zone_i / Curve_i)"
                        + " - the curve of an .efa is its fit, and there is none."),
                    path, set.Title);
                return null;
            }

            List<ROIEfficiencyData> curve = fromFit
                ? FitCurve(set, report)
                : PointCurve(set, report);
            if (curve.Count < DeviceConfigForm.LsrmMinPoints)
            {
                problem = string.Format(CultureInfo.InvariantCulture,
                    Text("LsrmCurveFewPoints",
                        "{0}: geometry \"{1}\" gave {2} curve point(s) - at least {3} are needed"
                        + " ({4} excluded by LSRM itself, {5} declared to more than {6:f0} % uncertainty)."),
                    path, set.Title, curve.Count, DeviceConfigForm.LsrmMinPoints,
                    report.ExcludedByLsrm, report.OverMaxError, DeviceConfigForm.LsrmMaxErrorPercent);
                return null;
            }

            return curve;
        }

        static List<ROIEfficiencyData> FitCurve(LsrmCurveSet set, LsrmCurveReport report)
        {
            List<ROIEfficiencyData> curve = new List<ROIEfficiencyData>();
            if (set.Zones.Count == 0)
            {
                return curve;
            }

            double lo = set.Zones.Min(z => z.LogLo);
            double hi = set.Zones.Max(z => z.LogHi);
            int nodes = Math.Max(2, (int)Math.Ceiling((hi - lo) * NodesPerDecade)) + 1;
            for (int k = 0; k < nodes; k++)
            {
                // Последний узел — ровно верх зон, а не lo + (hi − lo)·1 с
                // округлением, которое вывело бы его за край.
                double x = k == nodes - 1 ? hi : lo + (hi - lo) * k / (nodes - 1);
                double energy = Math.Pow(10.0, x);
                double efficiency, error;
                if (!TryEvalFit(set.Zones, energy, out efficiency, out error))
                {
                    report.GapNodes++;
                    continue;
                }

                curve.Add(new ROIEfficiencyData
                {
                    Energy = energy,
                    Efficiency = efficiency,
                    ErrorPercent = error,
                });
            }

            return curve;
        }

        static List<ROIEfficiencyData> PointCurve(LsrmCurveSet set, LsrmCurveReport report)
        {
            // Правило `T174` (решение Amber 05.09.2026) — то же, что у экспорта
            // `EffCalcMC.txt`: точке, которой сам ЛСРМ заявил погрешность выше
            // 100 %, не верить. Отбрасывается числом, а не молча.
            List<LsrmMeasuredPoint> kept = new List<LsrmMeasuredPoint>();
            foreach (LsrmMeasuredPoint point in set.Points)
            {
                if (point.Excluded)
                {
                    continue;
                }

                if (!(point.Efficiency > 0.0) || !(point.Energy > 0.0)
                    || !(point.ErrorPercent <= DeviceConfigForm.LsrmMaxErrorPercent))
                {
                    report.OverMaxError++;
                    continue;
                }

                kept.Add(point);
            }

            // Одна энергия у двух источников (241.995 кэВ Ra-226 в двух эталонах
            // и т. п.) — одна точка: среднее с весами 1/σ², погрешность среднего
            // 1/√Σw. Два узла на одной энергии сплайн дозы и FSA не берут вовсе.
            List<ROIEfficiencyData> curve = new List<ROIEfficiencyData>();
            foreach (LsrmMeasuredPoint point in kept.OrderBy(p => p.Energy))
            {
                ROIEfficiencyData last = curve.Count > 0 ? curve[curve.Count - 1] : null;
                if (last != null && point.Energy - last.Energy < SameEnergyKev)
                {
                    // Без погрешности у одной из двух веса не из чего взять —
                    // простое среднее, и погрешность остаётся неизвестной (0).
                    bool weighted = point.ErrorPercent > 0.0 && last.ErrorPercent > 0.0;
                    double w = weighted ? 1.0 / (point.ErrorPercent * point.ErrorPercent) : 1.0;
                    double lastW = weighted ? 1.0 / (last.ErrorPercent * last.ErrorPercent) : 1.0;
                    double sum = lastW + w;
                    last.Efficiency = (last.Efficiency * lastW + point.Efficiency * w) / sum;
                    last.Energy = (last.Energy * lastW + point.Energy * w) / sum;
                    last.ErrorPercent = weighted ? 1.0 / Math.Sqrt(sum) : 0.0;
                    report.MergedDuplicates++;
                    continue;
                }

                curve.Add(new ROIEfficiencyData
                {
                    Energy = point.Energy,
                    Efficiency = point.Efficiency,
                    ErrorPercent = point.ErrorPercent,
                });
            }

            return curve;
        }

        /// <summary>Строка ресурсов с запасным английским текстом.</summary>
        internal static string Text(string key, string fallback)
        {
            string text = Resources.ResourceManager.GetString(key, Resources.Culture);
            return string.IsNullOrEmpty(text) ? fallback : text;
        }
    }
}
