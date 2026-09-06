using BecquerelMonitor.EfficiencyMaker;
using GadrasShared;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace GadrasProbe
{
    /// <summary>
    /// Сверка нашего расчёта переносом с типовыми детекторами GADRAS.
    ///
    /// Зачем. Эталонов у `EfficiencySimulator` до сих пор было два: семь
    /// поставочных кривых ЛСРМ (одна геометрия — одна кривая, и все они с
    /// пробой, то есть с самопоглощением) и `TCCFCALC.dll`. Ни один не
    /// отвечает на простой вопрос: какая СОБСТВЕННАЯ эффективность бывает у
    /// голого кристалла известного размера. У InterSpec этот ответ лежит
    /// таблицей на тринадцать типовых детекторов — NaI восьми размеров,
    /// LaBr3 двух, германий трёх (`tools/interspec/gadras`, разбор поставки в
    /// `tools/interspec/README.md`).
    ///
    /// ЧЕГО ЭТА СВЕРКА НЕ ДОКАЗЫВАЕТ. Кривые GADRAS — не чертёж, а отклик
    /// КЛАССА приборов, подогнанный под измерения: у «NaI 3x3» в файле стоит
    /// ширина 6.35 см (2.5 дюйма) при длине 7.6, а собственная эффективность на
    /// 60 кэВ — 80 %, тогда как чистая геометрия дала бы почти 100 %. Поэтому
    /// расхождение здесь не улика против нашего переноса; это мера того, где мы
    /// стоим относительно чужого, независимо сделанного ответа. Эталон для
    /// поверки алгоритма — по-прежнему `TCCFCALC.dll`.
    ///
    /// Что делает проба:
    ///
    /// 1. Читает `Detector.dat` — размеры кристалла, входное окно (эффективный
    ///    Z и г/см2), отступ и расстояние до источника.
    /// 2. Собирает `GeometryModel`: точечный источник на оси, цилиндр
    ///    кристалла, окно из вещества с ближайшим целым Z.
    /// 3. Гоняет `EfficiencySimulator` и делит на телесный угол — чтобы
    ///    получилась СОБСТВЕННАЯ эффективность, в тех же единицах, что в
    ///    `Efficiency.csv`.
    /// 4. Печатает отношение наше/GADRAS по узлам сетки.
    ///
    ///     gadrasprobe [каталог с gadras] [--n=200000] [--csv=out.csv]
    ///
    /// Ожидания «ВСЕ СОШЛИСЬ» здесь нет и быть не может — печатается таблица
    /// отношений, читать её глазами.
    /// </summary>
    static class Program
    {
        /// <summary>Узлы, на которых сверяемся. Верх ограничен 3 МэВ: выше
        /// таблица GADRAS уходит в область, где наш перенос всё равно не
        /// заявлен, а низ — 30 кэВ, ниже начинает править окно и рентген.</summary>
        static readonly double[] Grid =
        {
            30, 40, 50, 60, 80, 100, 150, 200, 300, 400, 500,
            600, 662, 800, 1000, 1200, 1500, 2000, 2614, 3000
        };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string dir = null;
            int histories = 200000;
            string csvPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--n=", StringComparison.Ordinal))
                    histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal))
                    csvPath = a.Substring(6);
                else if (!a.StartsWith("--", StringComparison.Ordinal))
                    dir = a;
            }
            if (dir == null)
                dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                   @"..\..\tools\interspec\gadras");
            if (!Directory.Exists(dir))
            {
                Console.WriteLine("нет каталога {0}", dir);
                return 2;
            }

            var csv = new StringBuilder();
            csv.AppendLine("detector,energy_kev,gadras_intrinsic_pct,ours_intrinsic_pct,ratio,rel_err");

            foreach (string sub in Directory.GetDirectories(dir))
            {
                string name = Path.GetFileName(sub);
                GadrasDetector det;
                try
                {
                    det = GadrasDetector.Read(sub, name);
                }
                catch (Exception e)
                {
                    Console.WriteLine("{0}: пропущен — {1}", name, e.Message);
                    continue;
                }

                if (det.CrystalLengthCm <= 0.0 || det.CrystalWidthCm <= 0.0)
                {
                    // «HPGe 40%» несёт нулевую длину: у файла подогнан отклик, а
                    // размеры проставлены не полностью. Считать по нему нечего.
                    Console.WriteLine("{0}: пропущен — в Detector.dat нет размера"
                                      + " кристалла (длина {1}, ширина {2} см)",
                                      name, det.CrystalLengthCm, det.CrystalWidthCm);
                    continue;
                }

                GeometryModel model;
                try
                {
                    model = det.ToModel();
                }
                catch (Exception e)
                {
                    Console.WriteLine("{0}: сцена не собралась — {1}", name, e);
                    continue;
                }
                var sim = new EfficiencySimulator(model) { Histories = histories };

                double dSrc = det.DistanceCm + det.SetbackCm;
                double omega = det.SolidAngleFraction;

                Console.WriteLine();
                Console.WriteLine("=== {0}: {1}, D {2:F2} см, H {3:F2} см, окно {4:F2} г/см2 (Z {5}),"
                                  + " расстояние {6:F1} см, Ω/4π {7:E3}",
                                  name, det.CrystalName, det.CrystalWidthCm, det.CrystalLengthCm,
                                  det.WindowArealDensity, det.WindowZ, dSrc, omega);
                Console.WriteLine("   энергия   GADRAS      наш   наш/GADRAS  стат.,%");

                foreach (double e in Grid)
                {
                    double refPct = det.Reference(GadrasDetector.Column.Peak, e);
                    if (double.IsNaN(refPct))
                        continue;

                    double relErr;
                    double abs = sim.Efficiency(e, out relErr);
                    double oursPct = 100.0 * abs / omega;
                    double ratio = refPct > 0.0 ? oursPct / refPct : double.NaN;

                    // relativeError у симулятора уже в ПРОЦЕНТАХ, не в долях.
                    Console.WriteLine("  {0,8:F0}  {1,7:F3}  {2,7:F3}   {3,8:F3}  {4,6:F2}",
                                      e, refPct, oursPct, ratio, relErr);
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2:R},{3:R},{4:R},{5:R}",
                        name, e, refPct, oursPct, ratio, relErr));
                }
            }

            if (csvPath != null)
            {
                File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));
                Console.WriteLine();
                Console.WriteLine("записано {0}", csvPath);
            }
            return 0;
        }

    }
}
