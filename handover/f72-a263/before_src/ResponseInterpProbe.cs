using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ResponseInterpProbe
{
    /// <summary>
    /// Где ошибка интерполяции матрицы отклика выходит на полку.
    ///
    /// Вопрос стоит так: между узлами сетки отклик берётся переносом двух
    /// соседних строк на шкалу линии и их смешиванием. Перенос знает про
    /// масштаб, но не знает, что у маринелли заметная часть отклика — это
    /// рассеяние В САМОЙ ПРОБЕ, и с энергией оно ведёт себя не так, как отклик
    /// кристалла. Отсюда прежнее измерение: на маринелли 7.3 % на 300 кэВ и
    /// 9.2 % на 1841 при ста узлах, на цилиндре 2.0 и 4.8 %. Лечится ли это
    /// сгущением сетки и где сгущать перестаёт помогать — здесь и меряется.
    ///
    /// **Нулевая линия обязательна.** Прямой прогон идёт своим потоком
    /// случайных чисел, и его собственная погрешность — единицы процентов;
    /// без неё «полка» неотличима от шума. Поэтому на каждом якоре меряются
    /// ДВА расхождения:
    ///
    /// * **ошибка** — интерполяция в середине интервала против прямого прогона
    ///   на той же энергии. Это шум плюс интерполяция;
    /// * **шум** — строка ЛЕВОГО УЗЛА того же интервала против прямого прогона
    ///   на энергии узла. Интерполяции там нет вовсе (перенос с масштабом 1 —
    ///   тождество), значит остаётся ровно статистика обоих прогонов.
    ///
    /// Полка — там, где ошибка легла на шум: дальше сгущать сетку бессмысленно,
    /// разницы всё равно не видно за статистикой.
    ///
    /// **Сравниваются УШИРЕННЫЕ отклики**, а не сырые гистограммы поглощения.
    /// Причина не в косметике: поканальное расхождение — это почти целиком
    /// статистика. Континуум размазан по тысяче бинов, в каждом единицы
    /// отсчётов, и первый же прогон дал форму 44 % при шуме 41 % — мера,
    /// которая меряет сама себя. В модель отклик попадает уширенным
    /// разрешением детектора (десятки кэВ), и ошибка интерполяции — это сдвиг
    /// КРУПНЫХ структур: края, пиков вылета, ступеней. Уширение оставляет их
    /// нетронутыми и глушит статистику в корень из ширины окна.
    ///
    /// Мера расхождения — три числа. Главное **форма**: sum|I−E| / sum E, то
    /// есть какая доля образа стоит не там. **Пик** (окно ±1.5 ПШПВ вокруг
    /// линии) и **сумма** (доля провзаимодействовавших) идут рядом, потому что
    /// образ может сойтись в целом и разъехаться в пике.
    ///
    ///     responseinterpprobe --geometry=X.in [--nodes=25,50,100,200,400]
    ///                         [--n=300000] [--ref=1000000] [--bin=2]
    ///                         [--fwhm=7] [--csv=путь]
    ///
    /// Печатает таблицу по числу узлов и подробную по якорям, плюс время
    /// построения и размер файла — цену решения.
    /// </summary>
    static class Program
    {
        // Якоря шкалы: низ, где сетка густа сама (она логарифмическая), рабочая
        // середина с цезием, и верх, где узлы реже всего.
        static readonly double[] Anchors = { 150.0, 300.0, 662.0, 1200.0, 1800.0, 2600.0 };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null;
            string csvPath = null;
            int[] nodeCounts = { 25, 50, 100, 200, 400 };
            int histories = 300000;
            int refHistories = 1000000;
            double binKev = 2.0;
            double resolution = 0.07;

            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csvPath = a.Substring(6);
                else if (a.StartsWith("--nodes=", StringComparison.Ordinal)) nodeCounts = ParseList(a.Substring(8));
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4));
                else if (a.StartsWith("--ref=", StringComparison.Ordinal)) refHistories = int.Parse(a.Substring(6));
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) binKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--fwhm=", StringComparison.Ordinal)) resolution = 0.01 * double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            }

            if (geometryPath == null || !File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нужен --geometry=<файл .in>");
                return 2;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            Console.WriteLine("геометрия: {0}", geometry.Describe());
            Console.WriteLine("узлы: {0}; {1} историй на узел, {2} на эталон, бин {3:F2} кэВ, уширение {4:P0} на 662 кэВ",
                              string.Join(", ", Array.ConvertAll(nodeCounts, v => v.ToString())),
                              histories, refHistories, binKev, resolution);
            Console.WriteLine();

            var template = new ResponseMatrixOptions { Histories = histories, BinKev = binKev };

            // Какие энергии придётся считать напрямую, известно ДО построения:
            // сетка зависит только от числа узлов. Значит эталоны считаются
            // разом и параллельно, а не по одному внутри цикла по сеткам.
            var wanted = new List<double>();
            var plans = new List<Plan>();
            foreach (int n in nodeCounts)
            {
                var options = template.Clone();
                options.NodeCount = n;
                double[] grid = options.BuildGrid();
                foreach (double anchor in Anchors)
                {
                    int lo = IndexBelow(grid, anchor);
                    if (lo < 0)
                    {
                        continue;
                    }

                    var plan = new Plan
                    {
                        Nodes = n,
                        Anchor = anchor,
                        NodeEnergy = grid[lo],
                        MidEnergy = 0.5 * (grid[lo] + grid[lo + 1]),
                        StepKev = grid[lo + 1] - grid[lo]
                    };

                    plans.Add(plan);
                    Remember(wanted, plan.NodeEnergy);
                    Remember(wanted, plan.MidEnergy);
                }
            }

            Console.WriteLine("прямых прогонов: {0}", wanted.Count);
            var reference = new Dictionary<double, double[]>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var slots = new double[wanted.Count][];
            int threads = Math.Max(1, Environment.ProcessorCount - 1);
            Parallel.For(0, wanted.Count, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
            {
                slots[i] = Direct(geometry, wanted[i], binKev, refHistories, i, template);
            });

            for (int i = 0; i < wanted.Count; i++)
            {
                reference[wanted[i]] = slots[i];
            }

            Console.WriteLine("эталоны посчитаны за {0:F0} с", clock.Elapsed.TotalSeconds);
            Console.WriteLine();

            var lines = new List<string>();
            lines.Add("geometry;nodes;anchor;step_kev;err_shape;err_peak;err_sum;noise_shape;noise_peak;noise_sum;build_s;file_kb");

            // В каждой клетке «ошибка/шум»: ошибка — интерполяция против прямого
            // прогона, шум — тот же прогон против строки узла, где интерполяции
            // нет. Полка там, где первое легло на второе.
            Console.WriteLine("{0,6} {1,14} {2,14} {3,14} {4,7} {5,7}",
                              "узлов", "форма% е/ш", "пик% е/ш", "сумма% е/ш", "мин", "КБ");

            foreach (int n in nodeCounts)
            {
                var options = template.Clone();
                options.NodeCount = n;
                ResponseMatrix matrix = ResponseMatrixBuilder.Build(geometry, options, null, CancellationToken.None);

                string path = Path.Combine(Path.GetTempPath(), "rmx_interp_" + Guid.NewGuid().ToString("N") + ".rmx");
                matrix.Save(path);
                double fileKb = new FileInfo(path).Length / 1024.0;
                File.Delete(path);

                double sumShape = 0.0, sumPeak = 0.0, sumSum = 0.0;
                double noiseShape = 0.0, noisePeak = 0.0, noiseSum = 0.0;
                int count = 0;
                var detail = new List<string>();

                foreach (Plan plan in plans)
                {
                    if (plan.Nodes != n)
                    {
                        continue;
                    }

                    double[] exactMid = reference[plan.MidEnergy];
                    double[] exactNode = reference[plan.NodeEnergy];

                    double[] gotMid = matrix.Evaluate(plan.MidEnergy,
                                                      EfficiencySimulator.PeakBin(plan.MidEnergy, binKev) + 1);
                    double[] gotNode = matrix.Evaluate(plan.NodeEnergy,
                                                       EfficiencySimulator.PeakBin(plan.NodeEnergy, binKev) + 1);

                    double eShape, ePeak, eSum, nShape, nPeak, nSum;
                    Diff(Broaden(gotMid, binKev, resolution), Broaden(exactMid, binKev, resolution),
                         plan.MidEnergy, binKev, resolution, out eShape, out ePeak, out eSum);
                    Diff(Broaden(gotNode, binKev, resolution), Broaden(exactNode, binKev, resolution),
                         plan.NodeEnergy, binKev, resolution, out nShape, out nPeak, out nSum);

                    sumShape += eShape; sumPeak += ePeak; sumSum += eSum;
                    noiseShape += nShape; noisePeak += nPeak; noiseSum += nSum;
                    count++;

                    detail.Add(string.Format(CultureInfo.InvariantCulture,
                        "         {0,6:F0} кэВ (шаг {1,5:F0}): форма {2,6:P2} при шуме {3,6:P2}, пик {4,6:P2} при шуме {5,6:P2}",
                        plan.MidEnergy, plan.StepKev, eShape, nShape, ePeak, nPeak));

                    lines.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0};{1};{2:F0};{3:F1};{4:F5};{5:F5};{6:F5};{7:F5};{8:F5};{9:F5};{10:F1};{11:F1}",
                        geometry.Name, n, plan.Anchor, plan.StepKev,
                        eShape, ePeak, eSum, nShape, nPeak, nSum,
                        matrix.BuildSeconds, fileKb));
                }

                if (count == 0)
                {
                    continue;
                }

                Console.WriteLine("{0,6} {1,14} {2,14} {3,14} {4,7:F1} {5,7:F0}",
                                  n,
                                  Pair(sumShape / count, noiseShape / count),
                                  Pair(sumPeak / count, noisePeak / count),
                                  Pair(sumSum / count, noiseSum / count),
                                  matrix.BuildSeconds / 60.0, fileKb);

                foreach (string line in detail)
                {
                    Console.WriteLine(line);
                }
            }

            if (csvPath != null)
            {
                File.WriteAllLines(csvPath, lines.ToArray(), Encoding.UTF8);
                Console.WriteLine();
                Console.WriteLine("таблица: {0}", csvPath);
            }

            return 0;
        }

        sealed class Plan
        {
            public int Nodes;
            public double Anchor;
            public double NodeEnergy;
            public double MidEnergy;
            public double StepKev;
        }

        /// <summary>
        /// Прямой прогон на заданной энергии. Зерно НЕ такое, как у строк
        /// матрицы (там оно от номера узла): совпади потоки чисел — эталон стал
        /// бы копией строки, и шум вышел бы нулевым, а вместе с ним и вся
        /// нулевая линия.
        /// </summary>
        static double[] Direct(GeometryModel geometry, double energyKev, double binKev, int histories, int index,
                               ResponseMatrixOptions options)
        {
            // Ключи физики берутся ИЗ ОПЦИЙ матрицы: эталон, посчитанный другой
            // физикой, мерил бы не интерполяцию, а разницу моделей.
            var sim = new EfficiencySimulator(geometry.Clone())
            {
                Histories = histories,
                XrayEscape = options.XrayEscape,
                CoherentPassesThrough = options.CoherentPassesThrough,
                Bremsstrahlung = options.Bremsstrahlung,
                SingleScatter = options.SingleScatter,
                PeakHalfWidthKev = 0.0
            };

            sim.ResetStream(0xC0FFEE0000000000UL + (ulong)(index + 1) * 0x9E3779B97F4A7C15UL);
            double error;
            return sim.Response(energyKev, binKev, out error);
        }

        /// <summary>
        /// Уширить отклик разрешением детектора: ПШПВ = R662·sqrt(662·E), то
        /// есть постоянная доля на 662 кэВ и корневой закон по шкале. Гауссиана
        /// нормируется на единичную сумму, поэтому полная площадь не меняется —
        /// и «сумма» остаётся мерой доли провзаимодействовавших, а не побочным
        /// следствием уширения. Хвосты, вышедшие за края, ЗАЖИМАЮТСЯ в крайние
        /// бины: иначе площадь утекала бы у нижнего края, где ПШПВ сравнима с
        /// самой энергией.
        /// </summary>
        static double[] Broaden(double[] histogram, double binKev, double resolution)
        {
            double[] result = new double[histogram.Length];
            for (int b = 0; b < histogram.Length; b++)
            {
                double value = histogram[b];
                if (!(value > 0.0))
                {
                    continue;
                }

                double energy = b * binKev;
                double fwhm = resolution * Math.Sqrt(662.0 * Math.Max(1.0, energy));
                double sigma = fwhm / 2.354820045 / binKev;
                if (!(sigma > 0.25))
                {
                    result[b] += value;
                    continue;
                }

                int span = (int)Math.Ceiling(4.0 * sigma);
                double norm = 0.0;
                for (int k = -span; k <= span; k++)
                {
                    double t = k / sigma;
                    norm += Math.Exp(-0.5 * t * t);
                }

                if (!(norm > 0.0))
                {
                    result[b] += value;
                    continue;
                }

                for (int k = -span; k <= span; k++)
                {
                    double t = k / sigma;
                    int at = b + k;
                    if (at < 0) at = 0;
                    if (at >= result.Length) at = result.Length - 1;
                    result[at] += value * Math.Exp(-0.5 * t * t) / norm;
                }
            }

            return result;
        }

        /// <summary>
        /// Расхождение образа с эталоном: доля образа, стоящая не там (форма),
        /// расхождение пика (окно ±1.5 ПШПВ вокруг линии) и полной суммы.
        /// Хвосты за общей длиной идут в расхождение целиком — они и есть
        /// расхождение.
        /// </summary>
        static void Diff(double[] got, double[] exact, double lineEnergy, double binKev, double resolution,
                         out double shape, out double peak, out double sum)
        {
            int common = Math.Min(got.Length, exact.Length);
            double sg = 0.0, se = 0.0, l1 = 0.0;
            for (int i = 0; i < common; i++)
            {
                sg += got[i];
                se += exact[i];
                l1 += Math.Abs(got[i] - exact[i]);
            }

            for (int i = common; i < got.Length; i++)
            {
                sg += got[i];
                l1 += got[i];
            }

            for (int i = common; i < exact.Length; i++)
            {
                se += exact[i];
                l1 += exact[i];
            }

            double fwhm = resolution * Math.Sqrt(662.0 * lineEnergy);
            int lo = (int)Math.Floor((lineEnergy - 1.5 * fwhm) / binKev);
            int hi = (int)Math.Ceiling((lineEnergy + 1.5 * fwhm) / binKev);
            double pg = Window(got, lo, hi), pe = Window(exact, lo, hi);
            shape = se > 0.0 ? l1 / se : 0.0;
            sum = se > 0.0 ? Math.Abs(sg - se) / se : 0.0;
            peak = pe > 0.0 ? Math.Abs(pg - pe) / pe : 0.0;
        }

        static string Pair(double error, double noise)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F2}/{1:F2}", 100.0 * error, 100.0 * noise);
        }

        static double Window(double[] values, int lo, int hi)
        {
            double total = 0.0;
            for (int i = Math.Max(0, lo); i <= hi && i < values.Length; i++)
            {
                total += values[i];
            }

            return total;
        }

        /// <summary>Индекс узла слева от энергии; −1, если якорь вне сетки.</summary>
        static int IndexBelow(double[] grid, double energy)
        {
            for (int i = 0; i + 1 < grid.Length; i++)
            {
                if (grid[i] <= energy && energy < grid[i + 1])
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Запомнить энергию, если такой ещё нет. Сравнение точное, и это
        /// правильно: энергии приходят из одной и той же `BuildGrid`, а
        /// «близкие» склеивать нельзя — эталон должен стоять ровно там, где его
        /// потом спросят.
        /// </summary>
        static void Remember(List<double> list, double energy)
        {
            if (!list.Contains(energy))
            {
                list.Add(energy);
            }
        }

        static int[] ParseList(string text)
        {
            string[] parts = text.Split(',');
            var values = new List<int>();
            foreach (string part in parts)
            {
                int value;
                if (int.TryParse(part.Trim(), out value) && value >= 2)
                {
                    values.Add(value);
                }
            }

            return values.ToArray();
        }
    }
}
