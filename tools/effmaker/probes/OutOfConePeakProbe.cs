using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace OutOfConePeakProbe
{
    /// <summary>
    /// ⛔ `AMBER66` (П132, 22.09.2026): ЧЕМ ВЕЛИК ПОТЕРЯННЫЙ КЛАСС.
    ///
    /// Взвешенная (пиковая) ветвь разыгрывает направление кванта в конусе на
    /// объемлющую сферу ДЕТЕКТОРА (`sphereR` — кристалл с отражателем и
    /// оправой, БЕЗ пробы) и луча мимо этого конуса не рождает никогда. Между
    /// тем такой квант может рассеяться в пробе или обвязке когерентно (энергия
    /// та же) либо комптоном на малый угол (недобор в допуске пика) и
    /// поглотиться целиком. Аналоговая ветвь его РОЖДАЕТ (её конус — на габарит
    /// сцены, ~~`A57`~~), но до `AMBER66` историю в бине пика отбрасывала:
    /// класс не считался НИГДЕ.
    ///
    /// Проба печатает разложение пика узла на два непересекающихся множества:
    /// внутри конуса (взвешенная оценка) и вне его (`WeightPeakOutOfCone`), —
    /// и оценку пика аналоговой ветвью целиком, которой обе половины сверяются
    /// (`A58`: два независимых оценивателя одной величины).
    ///
    ///     outofconepeakprobe --geometry=X.in [--energies=32,60,122]
    ///                        [--n=4000000] [--bin=1] [--fwhm662=8]
    ///                        [--tol=halfbin | --tol=&lt;кэВ&gt;]
    ///
    /// `--tol=halfbin` — допуск СКЛАДА (`PeakToleranceHalfBin`, умолчание
    /// построителя): половина шага сетки. Без ключа допуск берётся из
    /// геометрии (ПШПВ/2) — это путь КРИВОЙ, он шире складского.
    ///
    /// ⚠ Читать отношение надо при НЕНУЛЕВОМ допуске (`--fwhm662=`): при нулевом
    /// класс состоит из одного когерентного рассеяния и вдвое меньше.
    /// </summary>
    static class Program
    {
        static double[] Parse(string s)
        {
            string[] parts = s.Split(',');
            double[] values = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                values[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
            }

            return values;
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null;
            double[] energies = { 32.0, 60.0, 122.0 };
            int histories = 4000000;
            double binKev = 1.0, fwhm662 = 0.0, tolKev = -1.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal))
                {
                    geometryPath = a.Substring(11);
                }
                else if (a.StartsWith("--energies=", StringComparison.Ordinal))
                {
                    energies = Parse(a.Substring(11));
                }
                else if (a.StartsWith("--n=", StringComparison.Ordinal))
                {
                    histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--bin=", StringComparison.Ordinal))
                {
                    binKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--fwhm662=", StringComparison.Ordinal))
                {
                    fwhm662 = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                }
                else if (a == "--tol=halfbin")
                {
                    // Допуск СКЛАДА (`ResponseMatrixOptions.PeakToleranceHalfBin`,
                    // умолчание): половина шага сетки. Именно его увидит ночной
                    // пересчёт, и именно на нём читается доля класса.
                    tolKev = -2.0;
                }
                else if (a.StartsWith("--tol=", StringComparison.Ordinal))
                {
                    tolKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (geometryPath == null)
            {
                Console.Error.WriteLine("нужен --geometry=<файл .in>");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            GeometryModel geometry = GeometryModel.Load(geometryPath);
            if (fwhm662 > 0.0)
            {
                geometry.FwhmAt662Percent = fwhm662;
            }

            Console.WriteLine("геометрия {0}, историй {1}, бин {2} кэВ, разрешение {3} % на 662",
                              Path.GetFileName(geometryPath), histories,
                              binKev.ToString("0.##", CultureInfo.InvariantCulture),
                              geometry.FwhmAt662Percent.ToString("0.00", CultureInfo.InvariantCulture));
            Console.WriteLine();
            Console.WriteLine("{0,10} {1,9} {2,14} {3,14} {4,9} {5,10} {6,14}",
                              "E, кэВ", "допуск", "пик всего", "вне конуса", "доля, %",
                              "историй", "аналог. пик");

            foreach (double e in energies)
            {
                var simulator = new EfficiencySimulator(geometry);
                simulator.Histories = histories;
                simulator.LightNonproportionality = false;   // шкала энерговыделения, как у арбитра
                simulator.AnalogConeSampling = true;         // конус на габарит сцены (`A57`)
                simulator.PeakHalfWidthKev =
                    tolKev <= -2.0 ? 0.5 * binKev
                    : tolKev >= 0.0 ? tolKev
                    : geometry.PeakHalfWidthKev(e);
                simulator.WeightPeakOutOfCone = 0.0;
                simulator.CountPeakOutOfCone = 0;
                simulator.WeightPeakBinDropped = 0.0;
                double error;
                double[] response = simulator.Response(e, binKev, out error);
                int n = Math.Max(1000, histories);
                double peak = response[response.Length - 1];
                double outside = simulator.WeightPeakOutOfCone / n;
                Console.WriteLine("{0,10:F2} {1,9:F3} {2,14:E6} {3,14:E6} {4,9:F2} {5,10} {6,14:E6}",
                                  e, simulator.PeakHalfWidthKev, peak, outside,
                                  peak > 0.0 ? 100.0 * outside / peak : 0.0,
                                  simulator.CountPeakOutOfCone,
                                  simulator.WeightPeakBinDropped / n);
            }

            return 0;
        }
    }
}
