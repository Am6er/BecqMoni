using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace MatrixRebuildProbe
{
    /// <summary>
    /// Пересчитать матрицу отклика ДЛЯ ГЕОМЕТРИИ ИЗ СПЕКТРА с заданным числом
    /// историй и положить её туда, откуда её берёт разбор.
    ///
    /// ЗАЧЕМ. Замер `A37`/`A38` показал: волны модели — это шум розыгрыша
    /// матрицы. У матрицы 300 000 историй на узел, и её строка после свёртки
    /// профилем прибора шумит на 3.2 %, тогда как волны на экране — около 2 %.
    /// Прямая проверка догадки — пересчитать ту же матрицу гуще и посмотреть,
    /// исчезнут ли волны; эта проба и делает пересчёт.
    ///
    ///     matrixrebuildprobe --spectrum=X.xml [--n=3000000] [--nodes=140]
    ///                        [--threads=N]
    ///
    /// ⛔ Пишет в `config\device\response\&lt;guid&gt;.rmx` РАБОЧЕГО КАТАЛОГА, то
    /// есть в тот же склад, откуда матрицу читает разбор. Гонять только в
    /// каталоге пробы: конфиг Amber правится ТОЛЬКО ею самой.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null;
            int histories = 3000000, nodes = 0, threads = 0, estimateOnly = 0;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--nodes=", StringComparison.Ordinal)) nodes = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--estimate=", StringComparison.Ordinal)) estimateOnly = int.Parse(a.Substring(11), CultureInfo.InvariantCulture);
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

            // Настройки берутся у ТОЙ ЖЕ матрицы, что лежит сейчас: пересчёт
            // должен отличаться от неё ровно числом историй, иначе сравнивать
            // будет нечего.
            ResponseMatrix current = ResponseMatrixStore.Load(rd.Efficiency.Guid);
            ResponseMatrixOptions options = current != null && current.Options != null
                ? current.Options.Clone()
                : new ResponseMatrixOptions();
            Console.WriteLine("кривая: {0}", rd.Efficiency.Name);
            if (current != null)
            {
                Console.WriteLine("сейчас на складе: историй {0}, узлов {1}, бин {2} кэВ",
                                  current.Histories,
                                  current.Energies != null ? current.Energies.Length : 0,
                                  current.BinKev.ToString("F2", CultureInfo.InvariantCulture));
            }

            options.Histories = histories;
            if (nodes > 0)
            {
                options.NodeCount = nodes;
            }

            if (threads > 0)
            {
                options.Threads = threads;
            }

            Console.WriteLine("считаю: историй {0}, узлов {1}, потоков {2}",
                              options.Histories, options.NodeCount,
                              options.Threads > 0 ? options.Threads : Environment.ProcessorCount - 1);

            // (`A44`) Предварительная оценка — та самая, что форма пишет строкой
            // «Estimated time: about …». Печатается ДО счёта, чтобы её враньё
            // было измерено, а не рассказано.
            var estimateClock = System.Diagnostics.Stopwatch.StartNew();
            double estimate = ResponseMatrixBuilder.EstimateSeconds(rd.Efficiency.Geometry, options);
            estimateClock.Stop();
            Console.WriteLine("ОЦЕНКА до счёта: {0:F0} с (сама оценка заняла {1:F1} с)",
                              estimate, estimateClock.Elapsed.TotalSeconds);

            // (`A44`) Только оценка, столько раз, сколько попросили: устойчивость
            // видна лишь несколькими подряд, а полный счёт идёт минуты.
            if (estimateOnly > 0)
            {
                for (int i = 1; i < estimateOnly; i++)
                {
                    var again = System.Diagnostics.Stopwatch.StartNew();
                    double value = ResponseMatrixBuilder.EstimateSeconds(rd.Efficiency.Geometry, options);
                    again.Stop();
                    Console.WriteLine("ОЦЕНКА ещё раз: {0:F0} с ({1:F1} с)",
                                      value, again.Elapsed.TotalSeconds);
                }

                return 0;
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            // (`A41`) Ход счёта печатается СТРОКАМИ: скачет ли остаток, видно
            // только рядом стоящими числами, а не одним последним.
            var seen = new System.Collections.Generic.List<string>();
            var progress = new Progress<ResponseMatrixProgress>(pr =>
            {
                seen.Add(string.Format(CultureInfo.InvariantCulture,
                                       "ХОД	{0}	{1:F1}	{2:F1}	{3:F1}	{4:F0}",
                                       pr.Done, pr.Percent, pr.ElapsedSeconds,
                                       pr.RemainingSeconds, pr.LastEnergyKev));
            });

            ResponseMatrix built = ResponseMatrixBuilder.Build(rd.Efficiency.Geometry, options,
                                                              progress, CancellationToken.None);
            Console.WriteLine("ХОД	узлов	доля,%	прошло,с	осталось,с	кэВ");
            foreach (string line in seen)
            {
                Console.WriteLine(line);
            }
            if (built == null)
            {
                Console.Error.WriteLine("матрица не построилась");
                return 1;
            }

            string path = ResponseMatrixStore.PathOf(rd.Efficiency.Guid);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            built.Save(path);
            Console.WriteLine("готово за {0:F0} с: {1}", clock.Elapsed.TotalSeconds, path);
            double fact = clock.Elapsed.TotalSeconds;
            Console.WriteLine("ИТОГ (`A44`): оценка {0:F0} с, факт {1:F0} с, врёт в {2:F2} раза",
                              estimate, fact, estimate > 0.0 ? fact / estimate : 0.0);
            Console.WriteLine("клеймо: {0}", built.Stamp);
            return 0;
        }
    }
}
