using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace MatdbStampProbeP180
{
    /// <summary>
    /// (`S202`, П180 29.09.2026) КЛЕЙМО МАТРИЦЫ И КРИВОЙ ВИДИТ СОДЕРЖИМОЕ `matdb`.
    ///
    ///     matdbstampprobep180 --geometry=X.in [--db=копия.sqlite ...]
    ///
    /// Печатает: отпечаток таблиц переноса базы РЯДОМ С ПРОБОЙ
    /// (`MaterialDatabase.SimulatorDataFingerprint()`) и время его счёта, клеймо
    /// матрицы сцены с умолчаниями склада (`ResponseMatrix.ComputeStamp`), и
    /// отпечаток каждой базы из `--db=` (файл явным путём).
    ///
    /// Положительный контроль (журнал П180): копия базы с правкой ОДНОЙ строки
    /// одной из таблиц переноса обязана дать другой отпечаток, а проба,
    /// запущенная из каталога с этой копией вместо `matdb.sqlite`, — другое
    /// клеймо той же сцены; копия без правки (VACUUM) — тот же отпечаток;
    /// правка таблицы, которой перенос не читает (`icc_coefficients`), — тот же.
    /// </summary>
    internal static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string geometryPath = null;
            var dbs = new System.Collections.Generic.List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--db=", StringComparison.Ordinal)) dbs.Add(a.Substring(5));
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            var sw = Stopwatch.StartNew();
            string own = MaterialDatabase.SimulatorDataFingerprint();
            double first = sw.Elapsed.TotalSeconds;
            sw.Restart();
            string again = MaterialDatabase.SimulatorDataFingerprint();
            double cached = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine("база рядом с пробой: {0}", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "matdb.sqlite"));
            Console.WriteLine("отпечаток таблиц переноса (mdb): {0}  (счёт {1:F2} с, повтор из кэша {2:F3} мс, совпал {3})",
                              own, first, cached, own == again);
            Console.WriteLine("таблиц в отпечатке: {0}", MaterialDatabase.SimulatorTables.Length);
            if (geometryPath != null)
            {
                GeometryModel geometry = GeometryModel.Load(geometryPath);
                string stamp = ResponseMatrix.ComputeStamp(geometry, new ResponseMatrixOptions());
                Console.WriteLine("клеймо матрицы {0}: {1}", Path.GetFileName(geometryPath), stamp);
            }

            foreach (string db in dbs)
            {
                Console.WriteLine("отпечаток {0}: {1}", db, MaterialDatabase.SimulatorDataFingerprint(db));
            }

            return 0;
        }
    }
}
