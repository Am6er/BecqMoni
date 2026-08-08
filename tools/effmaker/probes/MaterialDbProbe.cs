using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Text;

namespace MaterialDbProbe
{
    /// <summary>
    /// Данные о веществе после переезда в `matdb.sqlite`.
    ///
    /// Проверяются три вещи, каждая молчит у компилятора:
    ///
    /// 1. Охват. Полное ослабление было на 92 элемента, парциальные сечения —
    ///    на ДЕВЯТЬ. У кого парциальных нет, тот считается грубым приближением
    ///    «фотоэффект = всё, что не комптон» — и получает правдоподобные
    ///    неверные числа, а не отказ.
    /// 2. Все ли кристаллы библиотеки обеспечены. Это то самое место, ради
    ///    которого переезд и делался: CeBr3, CdTe, CZT и GSO сидели на грубом.
    /// 3. Две опечатки в атомных массах, которые в переписанном от руки массиве
    ///    не бросались в глаза: Pr 40.9076 вместо 140.9077 и Pa 238.0289
    ///    (масса урана) вместо 231.0359.
    ///
    ///     matdbprobe
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ». Собирать с ссылкой на
    /// `WeifenLuo.WinFormsUI.Docking.dll` и класть рядом `matdbprobe.exe.config`
    /// — копию `BecquerelMonitor.exe.config`: без её перенаправлений сборок
    /// `Microsoft.Data.Sqlite` не поднимется.
    /// </summary>
    static class Program
    {
        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            int bad = 0;

            int withAttenuation = 0, withPartials = 0;
            for (int z = 1; z <= 100; z++)
            {
                if (AttenuationData.MassAttenuation(z, 662.0) > 0.0)
                {
                    withAttenuation++;
                }

                if (PartialCrossSections.HasElement(z))
                {
                    withPartials++;
                }
            }

            Console.WriteLine("элементов с полным ослаблением: {0}", withAttenuation);
            Console.WriteLine("элементов с парциальными сечениями: {0}", withPartials);
            bad += Same("полное ослабление", 100, withAttenuation);
            bad += Same("парциальные сечения", 100, withPartials);

            Console.WriteLine();
            Console.WriteLine("кристаллы библиотеки:");
            foreach (GeometryMaterialLibrary.Entry entry in
                     GeometryMaterialLibrary.Of(GeometryMaterialLibrary.MaterialKind.Crystal))
            {
                GeometryMaterial material = GeometryMaterialLibrary.Make(entry, entry.Density);
                List<string> missing = new List<string>();
                foreach (KeyValuePair<int, double> f in material.Fractions)
                {
                    if (f.Value > 0.0 && !PartialCrossSections.HasElement(f.Key))
                    {
                        missing.Add(GeometryMaterialLibrary.SymbolOf(f.Key));
                    }
                }

                Console.WriteLine("  {0,-8} {1}", entry.Abbr,
                                  missing.Count == 0
                                      ? "парциальные есть"
                                      : "НЕТ ПАРЦИАЛЬНЫХ: " + string.Join(", ", missing.ToArray()));
                bad += missing.Count;
            }

            Console.WriteLine();
            // Сумма каналов обязана сойтись с полным ослаблением: это одна и та
            // же величина, посчитанная двумя путями из одной таблицы.
            int checkedPoints = 0;
            double worst = 0.0;
            string worstWhat = "";
            foreach (int z in new int[] { 1, 8, 13, 26, 32, 48, 53, 55, 58, 64, 82, 92 })
            {
                foreach (double e in new double[] { 30.0, 59.5, 122.0, 662.0, 1332.0, 2614.0, 8000.0 })
                {
                    double total = AttenuationData.MassAttenuation(z, e);
                    double sum = PartialCrossSections.MassCrossSection(z, e, PhotonProcess.Coherent)
                               + PartialCrossSections.MassCrossSection(z, e, PhotonProcess.Incoherent)
                               + PartialCrossSections.MassCrossSection(z, e, PhotonProcess.Photoelectric)
                               + PartialCrossSections.MassCrossSection(z, e, PhotonProcess.PairProduction);
                    if (!(total > 0.0))
                    {
                        continue;
                    }

                    checkedPoints++;
                    double rel = Math.Abs(sum / total - 1.0);
                    if (rel > worst)
                    {
                        worst = rel;
                        worstWhat = string.Format("Z={0} {1:0.#} кэВ: сумма {2:G6} полное {3:G6}", z, e, sum, total);
                    }
                }
            }

            // ЭТО НЕ ПРОВЕРКА, А ИЗМЕРЕНИЕ. Числа у обеих величин теперь одни и
            // те же, из одной таблицы, и в узлах сетки они сходятся точно.
            // Расходятся МЕЖДУ узлами, потому что правила интерполяции разные:
            // полное ослабление берётся лог-лог, а каналы — лог по энергии и
            // ЛИНЕЙНО по значению (так было сделано из-за нулей: рождение пар
            // ниже 1.022 МэВ тождественно нулевое, логарифм там брать не от
            // чего). Круто падающий фотоэффект линейная интерполяция между
            // редкими узлами завышает, отсюда и величина.
            //
            // Правило унаследовано, переезд его не менял: менять его — значит
            // двигать физику, и это отдельное решение с прогоном по всем
            // геометриям. Проба печатает размер расхождения, чтобы он был
            // виден числом, а не подразумевался.
            Console.WriteLine("сумма каналов против полного ослабления: {0} точек, худшее {1:0.###} %",
                              checkedPoints, worst * 100.0);
            Console.WriteLine("   {0}", worstWhat);
            Console.WriteLine("   (разные правила интерполяции между узлами, см. комментарий в пробе)");

            Console.WriteLine();
            bad += Mass("Pr", 59, 140.9077);
            bad += Mass("Pa", 91, 231.0359);
            bad += Mass("Cs", 55, 132.905);
            bad += Mass("I", 53, 126.9045);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("НЕ СОШЛОСЬ: {0}", bad));
            return bad == 0 ? 0 : 1;
        }

        static int Mass(string name, int z, double expected)
        {
            double got;
            if (!AttenuationData.AtomicMass.TryGetValue(z, out got))
            {
                Console.WriteLine("!! атомной массы {0} (Z={1}) в базе нет", name, z);
                return 1;
            }

            bool ok = Math.Abs(got / expected - 1.0) < 1e-4;
            Console.WriteLine("  {0,-3} Z={1,-3} масса {2:0.0000} {3}", name, z, got, ok ? "" : "!! ждали " + expected);
            return ok ? 0 : 1;
        }

        static int Same(string what, object expected, object got)
        {
            if (Equals(expected, got))
            {
                return 0;
            }

            Console.WriteLine("!! {0}: ждали {1}, получили {2}", what, expected, got);
            return 1;
        }
    }
}
