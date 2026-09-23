using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AttnRuleProbe
{
    /// <summary>
    /// ⛔ `AMBER74` (П132, 22.09.2026): ДВА ПРАВИЛА ПОЛНОГО ОСЛАБЛЕНИЯ РЯДОМ.
    ///
    /// ЗАЧЕМ. Слои сцены (проба, стенка, отражатель, оправа) брали полное
    /// ослабление ОДНОЙ лог-лог прямой по `element.Total` — по СУММЕ пяти
    /// каналов в узлах, — а кристалл считал по каналам, каждый своей прямой.
    /// Сумма степенных с разными наклонами вогнута в лог-лог, хорда лежит ВЫШЕ,
    /// и знак ошибки на участке смены хозяина (фотоэффект → комптон) всегда
    /// плюс. Проба печатает ОБА правила от одних и тех же узлов, чтобы разница
    /// была числом, а не доводом.
    ///
    /// Печатает четыре вещи:
    ///
    /// 1. НАЗВАННЫЕ ТОЧКИ — μ/ρ элемента двумя правилами и их отношение.
    ///    Середины интервалов рабочей сетки XCOM (10-15-20-30-40-50-60-80-100-
    ///    150-200-300-400 кэВ) — там, где ошибка хорды наибольшая.
    /// 2. КОНТРОЛЬ УЗЛА — на энергии, СОВПАДАЮЩЕЙ с узлом сетки, оба правила
    ///    обязаны дать табличное значение. Печатается худшее по всем 100
    ///    элементам и всем узлам: это и есть «правка не трогает узлы».
    /// 3. ТОЖДЕСТВО `Total = NoCoherent + Coherent` для веществ сцены. Пока
    ///    остаток считался вычитанием «прямая по сумме минус канал
    ///    когерентного», тождество держалось, но обе половины были по разным
    ///    правилам; теперь по одному.
    /// 4. ПРОПУСКАНИЕ СЛОЯ exp(−μx) на толщинах сцен — цена правила там, где
    ///    её видит человек.
    ///
    ///     attnruleprobe [--nodes=0|1]
    ///
    /// Код возврата 0 — сошлось; 1 — контроль узла разошёлся больше 1e-9.
    /// </summary>
    static class Program
    {
        /// <summary>
        /// СТАРОЕ правило (до `AMBER74`): одна лог-лог прямая по `element.Total`
        /// — та же строка, что стояла в `AttenuationData.MassAttenuation`.
        /// Оставлена здесь как ПЛЕЧО замера, а не как рабочий путь.
        /// </summary>
        static double OldRule(int z, double energyKev)
        {
            if (!(energyKev > 0.0))
            {
                return 0.0;
            }

            MaterialDatabase.Element element;
            if (!MaterialDatabase.TryGet(z, out element) || element.EnergyKev == null)
            {
                return 0.0;
            }

            return MaterialDatabase.Interpolate(element.EnergyKev, element.LogEnergyKev,
                                                element.Total, element.LogTotal, energyKev);
        }

        /// <summary>НОВОЕ правило — то, чем считает дерево сейчас.</summary>
        static double NewRule(int z, double energyKev)
        {
            return AttenuationData.MassAttenuation(z, energyKev);
        }

        static double MassOld(Dictionary<int, double> fractions, double energyKev)
        {
            double sum = 0.0;
            foreach (KeyValuePair<int, double> pair in fractions)
            {
                sum += pair.Value * OldRule(pair.Key, energyKev);
            }

            return sum;
        }

        static GeometryMaterial Material(string name, double density,
                                         params double[] zAndFraction)
        {
            GeometryMaterial m = new GeometryMaterial { Name = name, Density = density };
            for (int i = 0; i + 1 < zAndFraction.Length; i += 2)
            {
                m.Fractions[(int)zAndFraction[i]] = zAndFraction[i + 1];
            }

            return m;
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // Культура ЦЕЛИКОМ инвариантная (приказ Amber 05.09.2026): разделитель
            // дробной части — точка и в печати, и в разборе.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            bool nodes = true;
            foreach (string a in args)
            {
                if (a == "--nodes=0") { nodes = false; continue; }
                if (a == "--nodes=1") { nodes = true; continue; }
                Console.Error.WriteLine("неизвестный ключ: " + a);
                return 2;
            }

            int bad = 0;

            Console.WriteLine("=== 1. НАЗВАННЫЕ ТОЧКИ: μ/ρ элемента, см2/г ===");
            Console.WriteLine("{0,4} {1,10} {2,16} {3,16} {4,12}",
                              "Z", "E, кэВ", "прямая по сумме", "сумма каналов", "прямая/сумма-1,%");
            double[][] cases =
            {
                new[] { 8.0, 24.4949 },    // кислород, середина [20, 30]
                new[] { 6.0, 24.4949 },    // углерод
                new[] { 1.0, 24.4949 },    // водород — контроль: один канал хозяин
                new[] { 26.0, 141.0 },     // железо, линия Tc-99m
                new[] { 26.0, 122.4745 },  // железо, середина [100, 150]
                new[] { 53.0, 244.9490 },  // иод, середина [200, 300]
                new[] { 55.0, 244.9490 },  // цезий
                new[] { 71.0, 244.9490 },  // лютеций
                new[] { 82.0, 244.9490 },  // свинец
                new[] { 53.0, 350.0 },     // иод, середина [300, 400]
                new[] { 14.0, 141.0 },     // кремний
                new[] { 8.0, 60.0 },       // кислород НА УЗЛЕ — контроль
                new[] { 53.0, 200.0 },     // иод НА УЗЛЕ — контроль
            };
            foreach (double[] c in cases)
            {
                int z = (int)c[0];
                double e = c[1];
                double oldValue = OldRule(z, e), newValue = NewRule(z, e);
                Console.WriteLine("{0,4} {1,10:F4} {2,16:E6} {3,16:E6} {4,12:F3}",
                                  z, e, oldValue, newValue,
                                  newValue > 0.0 ? (oldValue / newValue - 1.0) * 100.0 : 0.0);
            }

            if (nodes)
            {
                Console.WriteLine();
                Console.WriteLine("=== 2. КОНТРОЛЬ УЗЛА: энергия СОВПАДАЕТ с узлом сетки ===");
                Console.WriteLine("   (эталон — табличное `element.Total[i]`; оба правила обязаны отдать его)");
                double worstOld = 0.0, worstNew = 0.0;
                string whereOld = "", whereNew = "";
                int points = 0;
                for (int z = 1; z <= 100; z++)
                {
                    MaterialDatabase.Element element;
                    if (!MaterialDatabase.TryGet(z, out element) || element.EnergyKev == null)
                    {
                        continue;
                    }

                    double[] grid = element.EnergyKev;
                    for (int i = 0; i < grid.Length; i++)
                    {
                        // Край поглощения — два узла на (почти) одной энергии:
                        // какой из них «тот самый», по энергии не решается, и
                        // спрашивать там нечего.
                        if (i > 0 && grid[i] - grid[i - 1] <= 1e-5 * grid[i])
                        {
                            continue;
                        }

                        if (i + 1 < grid.Length && grid[i + 1] - grid[i] <= 1e-5 * grid[i])
                        {
                            continue;
                        }

                        double reference = element.Total[i];
                        if (!(reference > 0.0))
                        {
                            continue;
                        }

                        points++;
                        double dOld = Math.Abs(OldRule(z, grid[i]) / reference - 1.0);
                        double dNew = Math.Abs(NewRule(z, grid[i]) / reference - 1.0);
                        if (dOld > worstOld)
                        {
                            worstOld = dOld;
                            whereOld = string.Format("Z={0}, {1:G8} кэВ", z, grid[i]);
                        }

                        if (dNew > worstNew)
                        {
                            worstNew = dNew;
                            whereNew = string.Format("Z={0}, {1:G8} кэВ", z, grid[i]);
                        }
                    }
                }

                Console.WriteLine("узлов проверено {0}", points);
                Console.WriteLine("  прямая по сумме: худшее {0:E3} ({1})", worstOld, whereOld);
                Console.WriteLine("  сумма каналов:   худшее {0:E3} ({1})", worstNew, whereNew);
                if (worstNew > 1e-9)
                {
                    Console.WriteLine("⛔ УЗЛЫ РАЗОШЛИСЬ: правило меняет табличные значения");
                    bad++;
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== 3. ТОЖДЕСТВО Total = NoCoherent + Coherent, 1/см ===");
            GeometryMaterial[] materials =
            {
                Material("вода", 1.0, 1, 0.111894, 8, 0.888106),
                Material("кварц", 2.65, 8, 0.532565, 14, 0.467435),
                Material("железо", 7.87, 26, 1.0),
                Material("Lu2O3", 9.42, 71, 0.878, 8, 0.122),
            };
            double[] energies = { 24.4949, 26.3, 59.5, 122.4745, 141.0, 244.949, 661.657, 1460.8, 2614.5 };
            double worstIdentity = 0.0;
            string whereIdentity = "";
            foreach (GeometryMaterial m in materials)
            {
                foreach (double e in energies)
                {
                    double total = m.LinearAttenuation(e);
                    double parts = m.LinearAttenuationWithoutCoherent(e) + m.LinearCoherent(e);
                    if (!(total > 0.0))
                    {
                        continue;
                    }

                    double rel = Math.Abs(parts / total - 1.0);
                    if (rel > worstIdentity)
                    {
                        worstIdentity = rel;
                        whereIdentity = string.Format("{0}, {1:G8} кэВ: {2:G8} против {3:G8}",
                                                      m.Name, e, parts, total);
                    }
                }
            }

            Console.WriteLine("худшее расхождение {0:E3} ({1})", worstIdentity, whereIdentity);

            Console.WriteLine();
            Console.WriteLine("=== 4. ПРОПУСКАНИЕ СЛОЯ exp(-μx) ===");
            Console.WriteLine("{0,8} {1,10} {2,8} {3,12} {4,12} {5,14}",
                              "вещество", "E, кэВ", "x, см", "μ прямая", "μ сумма", "проп. пр./сум.-1,%");
            double[][] layers =
            {
                new[] { 0.0, 24.4949, 2.0 },    // вода, маринелли 0.5 л
                new[] { 0.0, 22.1, 5.0 },       // Cd-109
                new[] { 0.0, 26.3, 5.0 },       // Am-241
                new[] { 0.0, 59.5, 5.0 },       // Am-241 59.5 — контроль: узел рядом
                new[] { 1.0, 24.4949, 1.0 },    // кварц (почва)
                new[] { 2.0, 141.0, 0.1 },      // стальная стенка 1 мм
                new[] { 3.0, 201.83, 1.0 },     // Lu2O3
                new[] { 3.0, 306.78, 1.0 },
            };
            foreach (double[] l in layers)
            {
                GeometryMaterial m = materials[(int)l[0]];
                double e = l[1], x = l[2];
                double muNew = m.LinearAttenuation(e);
                double muOld = MassOld(m.Fractions, e) * m.Density;
                double tNew = Math.Exp(-muNew * x), tOld = Math.Exp(-muOld * x);
                Console.WriteLine("{0,8} {1,10:F4} {2,8:F2} {3,12:F5} {4,12:F5} {5,14:F3}",
                                  m.Name, e, x, muOld, muNew,
                                  tNew > 0.0 ? (tOld / tNew - 1.0) * 100.0 : 0.0);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "ЕСТЬ РАСХОЖДЕНИЯ");
            return bad == 0 ? 0 : 1;
        }
    }
}
