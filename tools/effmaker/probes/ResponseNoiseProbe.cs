using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace ResponseNoiseProbe
{
    /// <summary>
    /// Отклик на ОДНУ линию, посчитанный дважды с разными зёрнами розыгрыша.
    ///
    /// ЗАЧЕМ. Вопрос Amber 01.09.2026: «каким образом размер сцены объясняет
    /// волнистость? у Geant4 меняется размер сцены и волн нет — как это так?».
    /// Она права: размер двигает УРОВЕНЬ, а волны остаются. Значит источник
    /// волн — в нашей строке отклика, и первое, что надо отделить, — ШУМ
    /// РОЗЫГРЫША: у измерения статистика 250 миллионов отсчётов, а у матрицы
    /// столько, сколько историй заказано на узел.
    ///
    /// Два прогона одной и той же линии в одной и той же геометрии отличаются
    /// ТОЛЬКО зерном. Разность двух таких кривых — чистый шум, без единой
    /// систематики: всё, что не шум, в ней сокращается.
    ///
    ///     responsenoiseprobe --spectrum=X.xml [--energy=661.657] [--n=300000]
    ///                        [--bin=1] [--out=noise.csv]
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, outPath = "noise.csv";
            double energyKev = 661.657, binKev = 1.0;
            int histories = 300000;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--energy=", StringComparison.Ordinal)) energyKev = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) binKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            ResultDataFile file;
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            using (var stream = new FileStream(spectrumPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            if (rd.Efficiency == null || !rd.Efficiency.HasGeometry)
            {
                Console.Error.WriteLine("у спектра нет кривой с геометрией");
                return 2;
            }

            Console.WriteLine("кривая: {0}, линия {1} кэВ, историй на прогон {2}, бин {3} кэВ",
                              rd.Efficiency.Name, energyKev, histories, binKev);

            double[] a1 = Run(rd.Efficiency.Geometry, energyKev, binKev, histories, 20260803);
            double[] a2 = Run(rd.Efficiency.Geometry, energyKev, binKev, histories, 20260804);
            if (a1 == null || a2 == null)
            {
                Console.Error.WriteLine("отклик не посчитался");
                return 1;
            }

            int n = Math.Min(a1.Length, a2.Length);
            using (var writer = new StreamWriter(outPath, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("keV,seed1,seed2");
                for (int i = 0; i < n; i++)
                {
                    writer.WriteLine("{0},{1},{2}",
                                     (i * binKev).ToString("F3", CultureInfo.InvariantCulture),
                                     a1[i].ToString("E6", CultureInfo.InvariantCulture),
                                     a2[i].ToString("E6", CultureInfo.InvariantCulture));
                }
            }

            Console.WriteLine("{0}: {1} бинов", outPath, n);
            return 0;
        }

        static double[] Run(GeometryModel geometry, double energyKev, double binKev,
                            int histories, int seed)
        {
            var simulator = new EfficiencySimulator(geometry);
            simulator.Seed = seed;
            simulator.Histories = histories;
            double error;
            double[] response = simulator.Response(energyKev, binKev, out error);
            Console.WriteLine("  зерно {0}: бинов {1}, ошибка взвешенной ветки {2:F2} %, шум континуума ПОСЛЕ СВЁРТКИ {3:F2} % (интеграл {4:F2} %)",
                              seed, response != null ? response.Length : 0, error,
                              simulator.LastContinuumRelativeError, simulator.LastContinuumIntegralError);
            return response;
        }
    }
}
