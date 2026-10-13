using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace PeakResponseProbeP174
{
    /// <summary>
    /// (`S198`, полоса П174 28.09.2026) ОТКЛИК В ПИКЕ ЛИНИИ ПО ТОМУ ПУТИ, КОТОРЫМ
    /// ЕГО БЕРЁТ РАЗБОР: перенос по каналам (<see cref="ResponseMatrix.TransferByChannel"/>
    /// = true — умолчание `FsaAnalyzer.MatrixTransferByChannel`), канал полного
    /// поглощения (<c>ResponseChannel.Peak</c> = 0) целиком, без окна по бинам.
    /// Рядом — то же число путём пробы `KbetaEdgeProbeP168` (общий масштаб, окно
    /// ±half + полбина по центрам бинов), чтобы видеть, что мерила она.
    ///
    ///   PeakResponseProbeP174 --matrix=X.rmx --energies=63.163,63.333 [--half=1.5]
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo inv = CultureInfo.InvariantCulture;
            string matrixPath = null, list = null;
            double half = 1.5;
            foreach (string a in args)
            {
                if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixPath = a.Substring(9);
                else if (a.StartsWith("--energies=", StringComparison.Ordinal)) list = a.Substring(11);
                else if (a.StartsWith("--half=", StringComparison.Ordinal)) half = double.Parse(a.Substring(7), inv);
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
            Console.WriteLine("MATRIX\t{0}\tbin={1}\tузлов {2}\tканалы {3}", Path.GetFileName(matrixPath),
                              bin.ToString("G", inv), matrix.Energies.Length, matrix.HasChannels ? "есть" : "НЕТ");
            Console.WriteLine("E, кэВ\tузлы\tпик(разбор)\tцентр пика(разбор)\tвсего(разбор)\tпик(проба П168)");
            foreach (string item in list.Split(','))
            {
                double energy = double.Parse(item, inv);
                int n = ResponseMatrix.ImageBins(energy, bin) + 4;
                int hi = Array.FindIndex(matrix.Energies, e => e >= energy);
                string nodes = hi > 0
                    ? matrix.Energies[hi - 1].ToString("F3", inv) + "|" + matrix.Energies[hi].ToString("F3", inv)
                    : "край";

                matrix.TransferByChannel = true;
                double[] peakRow = new double[n];
                matrix.AccumulateChannel(peakRow, energy, 1.0, 0);
                double[] all = new double[n];
                matrix.Accumulate(all, energy, 1.0);
                double peak = 0.0, moment = 0.0, total = 0.0;
                for (int b = 0; b < n; b++)
                {
                    peak += peakRow[b];
                    moment += peakRow[b] * b * bin;
                    total += all[b];
                }

                matrix.TransferByChannel = false;
                double[] old = matrix.Evaluate(energy, (int)Math.Ceiling(energy / bin) + 4);
                double oldPeak = 0.0;
                for (int b = 0; b < old.Length; b++)
                {
                    if (Math.Abs((b + 0.5) * bin - energy) <= half + 0.5 * bin) oldPeak += old[b];
                }

                Console.WriteLine("{0}\t{1}\t{2}\t{3}\t{4}\t{5}", energy.ToString("F3", inv), nodes,
                                  peak.ToString("E5", inv),
                                  (peak > 0 ? moment / peak : double.NaN).ToString("F3", inv),
                                  total.ToString("E5", inv), oldPeak.ToString("E5", inv));
            }

            return 0;
        }
    }
}
