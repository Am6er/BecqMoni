using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace BackscatterShapeProbe
{
    /// <summary>
    /// `A83`: ОБРАЗ ОБРАТНОГО РАССЕЯНИЯ FSA ПРОТИВ БУГРА В МАТРИЦЕ ОТКЛИКА.
    ///
    /// ЗАЧЕМ. Пять спектров потеряли объявленный нуклид, и у всех пяти счёт
    /// забрал приборный образ `Backscatter` (74…99 % против 0 % у нуклида).
    /// Догадка, записанная в строке: образ строится по СВОБОДНЫМ электронам
    /// (чистая Клейна — Нишина, `FsaAnalyzer.BuildBackscatter`), а матрица
    /// считает перенос по СВЯЗАННЫМ — с функцией некогерентного рассеяния
    /// `S(x, Z)` и доплеровским уширением, оба включены умолчанием. Оба
    /// отличия в одну сторону: свободная формула даёт лишний вес назад и
    /// строит бугор ОСТРЕЕ настоящего.
    ///
    /// Проба меряет это прямо, без прогона корпуса: берёт форму отклика на
    /// энергии линии из матрицы сцены и рядом считает образ той же формулой,
    /// какой его строит разбор. Сравниваются положение, ширина и края.
    ///
    /// ⚠ Эффективность в образ НЕ вводится нарочно: она общий множитель на
    /// энергии рассеянного и положения бугра не двигает, а вопрос строки —
    /// именно про положение и ширину.
    ///
    ///     backscattershapeprobe --matrix=&lt;путь .rmx&gt; --energy=81.0
    ///                           [--out=&lt;csv&gt;] [--band=0.35]
    /// </summary>
    static class Program
    {
        const double ElectronMassKev = 510.998950;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string matrixPath = null, outCsv = null;
            double energy = 0.0;
            double bandFraction = 0.35;

            foreach (string a in args)
            {
                if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixPath = a.Substring(9);
                else if (a.StartsWith("--energy=", StringComparison.Ordinal)) energy = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outCsv = a.Substring(6);
                else if (a.StartsWith("--band=", StringComparison.Ordinal)) bandFraction = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (matrixPath == null || !File.Exists(matrixPath))
            {
                Console.Error.WriteLine("нужен --matrix=<путь .rmx>");
                return 2;
            }

            if (!(energy > 0.0))
            {
                Console.Error.WriteLine("нужен --energy=<кэВ линии>");
                return 2;
            }

            ResponseMatrix matrix = ResponseMatrix.Load(matrixPath);
            if (matrix == null)
            {
                Console.Error.WriteLine("матрица не читается (чужой формат?): " + matrixPath);
                return 2;
            }

            double bin = matrix.BinKev > 0.0 ? matrix.BinKev : 2.0;
            int bins = (int)Math.Ceiling(energy / bin) + 4;
            double[] response = matrix.Evaluate(energy, bins);

            // ⛔ СУММАРНЫЙ ОТКЛИК ДЛЯ ЭТОГО ЗАМЕРА НЕ ГОДИТСЯ, и это выяснилось
            // первым же прогоном: у линии 81 кэВ его максимум сел на 53 кэВ —
            // это ПИК ВЫЛЕТА K-рентгена иода (81 − 28.6 = 52.4), а вовсе не
            // бугор обратного рассеяния (61.5). Сравнивать образ с суммой
            // значило бы мерить не то и получить «смещение на 10 кэВ» там, где
            // его нет.
            //
            // Матрица раскладывает отклик по каналам исхода (`Peak`, `Compton`,
            // `Escape511`, `EscapeXray`), и вылет живёт в своём. Квант, ушедший
            // в обвязку, рассеявшийся там назад и поглощённый целиком, уносит
            // часть энергии — значит метка «утечка», канал `Compton`. Там его и
            // ищем.
            string[] channelNames = { "Peak", "Compton", "Escape511", "EscapeXray" };
            double[][] byChannel = new double[channelNames.Length][];
            bool hasChannels = matrix.HasChannels;
            for (int c = 0; c < channelNames.Length; c++)
            {
                byChannel[c] = new double[bins];
                if (hasChannels)
                {
                    matrix.AccumulateShifted(byChannel[c], energy, 1.0, c, 0.0);
                }
            }

            // Образ FSA — та же арифметика, что в `FsaAnalyzer.BuildBackscatter`:
            // 180 шагов по углу от 110° до 180°, вес Клейна — Нишины на телесный
            // угол, энергия рассеянного E/(1+alpha(1-cosθ)).
            const int Steps = 180;
            double[] image = new double[bins];
            double alpha = energy / ElectronMassKev;
            double thetaMin = 110.0 * Math.PI / 180.0;
            double thetaMax = 180.0 * Math.PI / 180.0;
            for (int s = 0; s < Steps; s++)
            {
                double theta = thetaMin + (thetaMax - thetaMin) * (s + 0.5) / Steps;
                double sin = Math.Sin(theta);
                double ratio = 1.0 / (1.0 + alpha * (1.0 - Math.Cos(theta)));
                double weight = ratio * ratio * (ratio + 1.0 / ratio - sin * sin) * sin;
                double scattered = energy * ratio;
                int b = (int)(scattered / bin);
                if (b >= 0 && b < bins && weight > 0.0)
                {
                    image[b] += weight;
                }
            }

            // Полоса обратного рассеяния: от энергии при 110° до предела при 180°.
            double edge180 = energy / (1.0 + 2.0 * alpha);
            double edge110 = energy / (1.0 + alpha * (1.0 - Math.Cos(thetaMin)));

            Console.WriteLine("матрица: {0}", Path.GetFileName(matrixPath));
            Console.WriteLine("  узлов {0}, бин {1:F2} кэВ, клеймо {2}",
                              matrix.Energies != null ? matrix.Energies.Length : 0, bin,
                              (matrix.Stamp ?? "").Split(';')[0]);
            Console.WriteLine("линия {0:F2} кэВ", energy);
            Console.WriteLine("  предел обратного рассеяния (180°): {0:F2} кэВ", edge180);
            Console.WriteLine("  начало полосы образа       (110°): {0:F2} кэВ", edge110);
            Console.WriteLine("  до фотопика от предела: {0:F2} кэВ", energy - edge180);
            Console.WriteLine();

            // Полоса сравнения — вокруг бугра, шириной bandFraction от энергии.
            double lo = Math.Max(0.0, edge180 - bandFraction * energy);
            double hi = Math.Min(energy * 0.98, edge110 + bandFraction * energy);
            Console.WriteLine("полоса сравнения: {0:F1}…{1:F1} кэВ", lo, hi);

            Report("отклик, сумма ", response, bin, lo, hi);
            if (hasChannels)
            {
                for (int c = 0; c < channelNames.Length; c++)
                {
                    Report(("канал " + channelNames[c]).PadRight(14), byChannel[c], bin, lo, hi);
                }
            }
            else
            {
                Console.WriteLine("  ⚠ у матрицы НЕТ раскладки по каналам — вылет от рассеяния не отделить");
            }

            Report("образ FSA     ", image, bin, lo, hi);

            if (outCsv != null)
            {
                using (var w = new StreamWriter(outCsv, false, new UTF8Encoding(true)))
                {
                    w.WriteLine("energy_kev,response,peak,compton,escape511,escapexray,image");
                    for (int i = 0; i < bins; i++)
                    {
                        double e = (i + 0.5) * bin;
                        w.WriteLine(string.Join(",", new[]
                        {
                            e.ToString("F3", CultureInfo.InvariantCulture),
                            response[i].ToString("E6", CultureInfo.InvariantCulture),
                            byChannel[0][i].ToString("E6", CultureInfo.InvariantCulture),
                            byChannel[1][i].ToString("E6", CultureInfo.InvariantCulture),
                            byChannel[2][i].ToString("E6", CultureInfo.InvariantCulture),
                            byChannel[3][i].ToString("E6", CultureInfo.InvariantCulture),
                            image[i].ToString("E6", CultureInfo.InvariantCulture)
                        }));
                    }
                }

                Console.WriteLine();
                Console.WriteLine("таблица: {0}", outCsv);
            }

            return 0;
        }

        /// <summary>Центр тяжести, максимум и ширина на полувысоте в полосе.</summary>
        static void Report(string title, double[] y, double bin, double lo, double hi)
        {
            double sum = 0.0, moment = 0.0, top = 0.0;
            double topE = 0.0;
            int i0 = (int)(lo / bin), i1 = (int)(hi / bin);
            for (int i = Math.Max(0, i0); i <= Math.Min(y.Length - 1, i1); i++)
            {
                double e = (i + 0.5) * bin;
                sum += y[i];
                moment += y[i] * e;
                if (y[i] > top) { top = y[i]; topE = e; }
            }

            if (!(sum > 0.0))
            {
                Console.WriteLine("  {0}: в полосе ПУСТО", title);
                return;
            }

            double half = top * 0.5;
            double left = double.NaN, right = double.NaN;
            for (int i = Math.Max(0, i0); i <= Math.Min(y.Length - 1, i1); i++)
            {
                double e = (i + 0.5) * bin;
                if (y[i] >= half)
                {
                    if (double.IsNaN(left)) left = e;
                    right = e;
                }
            }

            Console.WriteLine("  {0}: центр {1,7:F2}  максимум {2,7:F2}  ПШПВ {3,6:F2} ({4:F1}…{5:F1})",
                              title, moment / sum, topE,
                              double.IsNaN(left) ? 0.0 : right - left, left, right);
        }
    }
}
