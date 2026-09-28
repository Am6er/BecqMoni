using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace KbetaEdgeProbeP168
{
    /// <summary>
    /// (`AMBER120`, полоса П168 28.09.2026) ВО ЧТО ОБХОДИТСЯ ЭНЕРГИЯ ГРУППЫ Kβ,
    /// взятая серединой текстового диапазона строки ENSDF, у пробы из своего же
    /// элемента. Печатает по матрице отклика сцены отклик в пике (бины в пределах
    /// ±<c>--half=</c> кэВ от линии) и полный отклик на каждую из энергий
    /// <c>--energies=</c> — тем же <see cref="ResponseMatrix.Evaluate"/>, что
    /// собирает образ линии в разборе; и узлы сетки, между которыми лежат линии.
    ///
    ///   KbetaEdgeProbeP168 --matrix=X.rmx --energies=63.163,63.333 [--half=1.5]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string matrixPath = null, list = null;
            double half = 1.5;
            foreach (string a in args)
            {
                if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixPath = a.Substring(9);
                else if (a.StartsWith("--energies=", StringComparison.Ordinal)) list = a.Substring(11);
                else if (a.StartsWith("--half=", StringComparison.Ordinal)) half = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (matrixPath == null || !File.Exists(matrixPath) || string.IsNullOrEmpty(list))
            {
                Console.Error.WriteLine("нужны --matrix=<.rmx> и --energies=<кэВ,кэВ>");
                return 2;
            }

            ResponseMatrix matrix = ResponseMatrix.Load(matrixPath);
            if (matrix == null)
            {
                Console.Error.WriteLine("матрица не читается: " + matrixPath);
                return 2;
            }

            double bin = matrix.BinKev > 0.0 ? matrix.BinKev : 2.0;
            Console.WriteLine("MATRIX\t{0}\tbin={1} кэВ\tузлов {2}", Path.GetFileName(matrixPath),
                              bin.ToString("G", CultureInfo.InvariantCulture), matrix.Energies.Length);
            foreach (string item in list.Split(','))
            {
                double energy = double.Parse(item, CultureInfo.InvariantCulture);
                int hi = Array.FindIndex(matrix.Energies, e => e >= energy);
                string nodes = hi > 0
                    ? matrix.Energies[hi - 1].ToString("F3", CultureInfo.InvariantCulture) + " | "
                      + matrix.Energies[hi].ToString("F3", CultureInfo.InvariantCulture)
                    : "край сетки";
                int bins = (int)Math.Ceiling(energy / bin) + 4;
                double[] response = matrix.Evaluate(energy, bins);
                double peak = 0.0, total = 0.0;
                for (int b = 0; b < response.Length; b++)
                {
                    total += response[b];
                    double centre = (b + 0.5) * bin;
                    if (Math.Abs(centre - energy) <= half + 0.5 * bin)
                    {
                        peak += response[b];
                    }
                }

                Console.WriteLine("LINE\t{0}\tузлы {1}\tпик {2}\tвсего {3}",
                                  energy.ToString("F3", CultureInfo.InvariantCulture), nodes,
                                  peak.ToString("E6", CultureInfo.InvariantCulture),
                                  total.ToString("E6", CultureInfo.InvariantCulture));
            }

            return 0;
        }
    }
}
