using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace FsaMatchingProbe
{
    /// <summary>
    /// (`A297`) Сопоставление «группа линий — найденный пик» обязано быть
    /// МАКСИМАЛЬНЫМ ПО ЧИСЛУ ПАР И МИНИМАЛЬНЫМ ПО СТОИМОСТИ.
    ///
    /// ⛔ ЗАЧЕМ ОТДЕЛЬНАЯ ПРОБА, А НЕ КОРПУСНЫЙ ПРОГОН. Корпус меряет ИТОГ —
    /// доли, невязку, состав, — и на нём разница между двумя максимальными
    /// паросочетаниями видна только как сдвиг `Coverage` у отдельных родителей,
    /// которого не с чем сравнить. Свойство же формулируется точно и проверяется
    /// на четырёх парах: у задачи есть ДВА паросочетания мощности 2, стоимостью
    /// 0.4 и 1.1, и правильным является ровно одно. Прежний ход (алгоритм Куна
    /// увеличивающими путями) давал ЛЮБОЕ из двух — то, до которого доводил
    /// порядок путей, — и это не ловится ни одним корпусным числом.
    ///
    /// Проверяется через отражение: `MinCostMatching` и типы `Group`/`Candidate`
    /// закрыты, и открывать их наружу ради пробы было бы хуже — правило
    /// «мерить метод из сборки ОТРАЖЕНИЕМ» заведено ровно для этого случая.
    ///
    ///     FsaMatchingProbe.exe
    /// </summary>
    static class Program
    {
        static int failed;

        static Type inference;
        static Type groupType;
        static Type candidateType;
        static MethodInfo minCost;

        static void Say(string format, params object[] args)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, format, args));
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // ⛔ (`T247`) Культура ЦЕЛИКОМ инвариантная, приказ Amber 05.09.2026. Проба
            //    не ставила её ВОВСЕ, и на русской машине часть её чисел шла с ЗАПЯТОЙ
            //    (замер 10.09.2026, полоса П8: мест без поставщика культуры — 4).
            //    Инвариант ЦЕЛИКОМ, а не клон с подменённым разделителем: клон
            //    чинит печать и оставляет РАЗБОР системным (`T245`).
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (string a in args)
            {
                // (`A263`) неизвестный ключ — отказ, а не молчание.
                Console.WriteLine("не знаю ключа: " + a);
                return 2;
            }

            inference = typeof(FsaCompositionInference);
            groupType = inference.GetNestedType("Group", BindingFlags.NonPublic);
            candidateType = inference.GetNestedType("Candidate", BindingFlags.NonPublic);
            minCost = inference.GetMethod("MinCostMatching",
                                          BindingFlags.NonPublic | BindingFlags.Static);
            if (groupType == null || candidateType == null || minCost == null)
            {
                Say("⛔ ОТКАЗ: закрытых Group/Candidate/MinCostMatching в сборке нет —"
                    + " проба мерила бы пустоту (Group {0}, Candidate {1}, метод {2})",
                    groupType != null, candidateType != null, minCost != null);
                return 1;
            }

            Say("ПАРОСОЧЕТАНИЕ ГРУПП И ПИКОВ (A297)");
            Say("");

            // ── 1. Контрпример разбора: два максимума, стоимости 0.4 и 1.1 ──
            //
            // G1 видит P1 ценой 0.1 и P2 ценой 0.9; G2 видит P1 ценой 0.2 и
            // P2 ценой 0.3. Мощность у обоих раскладов 2, дешевле — первый.
            Case("две пары, два максимума",
                 new[] { 100.0, 200.0 },
                 new[] { 511.0, 662.0 },
                 new[]
                 {
                     new[] { 0.0, 0.0, 0.1 },
                     new[] { 0.0, 1.0, 0.9 },
                     new[] { 1.0, 0.0, 0.2 },
                     new[] { 1.0, 1.0, 0.3 }
                 },
                 2, 0.4);

            // ── 2. То же наоборот: дешевле оказывается ПЕРЕКРЁСТНЫЙ расклад ──
            //
            // Положительный контроль к первому случаю: если бы код просто
            // раздавал пики по порядку групп, он проходил бы случай 1 и падал
            // здесь. G1 дёшево берёт P2, G2 — P1.
            Case("дешевле перекрёстный расклад",
                 new[] { 100.0, 200.0 },
                 new[] { 511.0, 662.0 },
                 new[]
                 {
                     new[] { 0.0, 0.0, 0.7 },
                     new[] { 0.0, 1.0, 0.1 },
                     new[] { 1.0, 0.0, 0.2 },
                     new[] { 1.0, 1.0, 0.6 }
                 },
                 2, 0.3);

            // ── 3. Мощность важнее дешевизны (`A293` не отменена) ──
            //
            // G2 видит ТОЛЬКО P1. Дешёвая пара G1–P1 (0.1) оставила бы G2 ни с
            // чем: одна пара стоимостью 0.1. Верно — две пары стоимостью 1.1.
            Case("мощность важнее цены",
                 new[] { 100.0, 200.0 },
                 new[] { 511.0, 662.0 },
                 new[]
                 {
                     new[] { 0.0, 0.0, 0.1 },
                     new[] { 0.0, 1.0, 0.9 },
                     new[] { 1.0, 0.0, 0.2 }
                 },
                 2, 1.1);

            // ── 4. Один пик на две группы: вторая остаётся ни с чем ──
            Case("пик один, групп две",
                 new[] { 100.0, 200.0 },
                 new[] { 511.0 },
                 new[]
                 {
                     new[] { 0.0, 0.0, 0.4 },
                     new[] { 1.0, 0.0, 0.2 }
                 },
                 1, 0.2);

            // ── 5. Цепочка вытеснений на три звена ──
            //
            // G1 видит P1 и P2, G2 видит P1 и P3, G3 видит только P1. Максимум
            // 3 достижим единственным раскладом; дешёвого выбора здесь нет,
            // проверяется, что вытеснение идёт вглубь, а не на один шаг.
            Case("цепочка вытеснений",
                 new[] { 100.0, 200.0, 300.0 },
                 new[] { 511.0, 662.0, 1173.0 },
                 new[]
                 {
                     new[] { 0.0, 0.0, 0.1 },
                     new[] { 0.0, 1.0, 0.5 },
                     new[] { 1.0, 0.0, 0.2 },
                     new[] { 1.0, 2.0, 0.6 },
                     new[] { 2.0, 0.0, 0.3 }
                 },
                 3, 1.4);

            Say("");
            Say(failed == 0 ? "ВСЕ СОШЛИСЬ" : "ПРОВАЛОВ: " + failed);
            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// Один случай: собрать вход, позвать закрытый метод и сверить мощность
        /// и стоимость. Порядок входных списков проверяется ДВАЖДЫ — прямой и
        /// обратный, — потому что «итог не зависит от порядка» есть заявленное
        /// свойство, а не пожелание.
        /// </summary>
        static void Case(string name, double[] groupEnergies, double[] peakEnergies,
                         double[][] links, int wantCount, double wantCost)
        {
            double gotCost, gotCostBack;
            int gotCount = Run(name, groupEnergies, peakEnergies, links, false, out gotCost);
            int gotCountBack = Run(name, groupEnergies, peakEnergies, links, true, out gotCostBack);

            bool ok = gotCount == wantCount
                      && Math.Abs(gotCost - wantCost) < 1.0E-9
                      && gotCountBack == wantCount
                      && Math.Abs(gotCostBack - wantCost) < 1.0E-9;
            if (!ok)
            {
                failed++;
            }

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: тот же вход прежним ходом (алгоритм
            // Куна, ~~`A293`~~). Без него «сошлось» ничего не значит — случай,
            // на котором старое и новое дают ОДНО, проверяет лишь то, что код
            // не сломан вовсе. Столбец «Кун» называет, какие случаи различают
            // ходы, а какие взяты для полноты.
            double control;
            int controlCount = Kuhn(groupEnergies.Length, peakEnergies.Length, links,
                                    out control);
            bool separates = controlCount != wantCount
                             || Math.Abs(control - wantCost) >= 1.0E-9;

            Say("  {0,-28} пар {1} (ждём {2}), цена {3:F4} (ждём {4:F4});"
                + " обратный порядок: пар {5}, цена {6:F4};"
                + " Кун: пар {7}, цена {8:F4} — {9}   {10}",
                name, gotCount, wantCount, gotCost, wantCost,
                gotCountBack, gotCostBack, controlCount, control,
                separates ? "РАЗЛИЧАЕТ" : "совпадает",
                ok ? "СОШЛОСЬ" : "⛔ ПРОВАЛ");
        }

        /// <summary>
        /// Прежний ход: увеличивающие пути в глубину, кандидаты каждой группы —
        /// по близости, группы — по порядку. Максимальная мощность, стоимость
        /// какая выйдет. Держится здесь ТОЛЬКО как контроль.
        /// </summary>
        static int Kuhn(int groups, int peaks, double[][] links, out double cost)
        {
            var options = new List<double[]>[groups];
            for (int g = 0; g < groups; g++)
            {
                options[g] = new List<double[]>();
            }

            var sorted = new List<double[]>(links);
            sorted.Sort(delegate(double[] a, double[] b) { return a[2].CompareTo(b[2]); });
            foreach (double[] link in sorted)
            {
                options[(int)link[0]].Add(link);
            }

            var takenBy = new double[peaks][];
            for (int g = 0; g < groups; g++)
            {
                Augment(g, options, takenBy, new bool[peaks]);
            }

            cost = 0.0;
            int count = 0;
            foreach (double[] taken in takenBy)
            {
                if (taken != null)
                {
                    cost += taken[2];
                    count++;
                }
            }

            return count;
        }

        static bool Augment(int group, List<double[]>[] options, double[][] takenBy, bool[] seen)
        {
            foreach (double[] link in options[group])
            {
                int peak = (int)link[1];
                if (seen[peak])
                {
                    continue;
                }

                seen[peak] = true;
                if (takenBy[peak] == null || Augment((int)takenBy[peak][0], options, takenBy, seen))
                {
                    takenBy[peak] = link;
                    return true;
                }
            }

            return false;
        }

        static int Run(string name, double[] groupEnergies, double[] peakEnergies,
                       double[][] links, bool reversed, out double cost)
        {
            var groups = new List<object>();
            foreach (double energy in groupEnergies)
            {
                object group = Activator.CreateInstance(groupType, true);
                groupType.GetField("Energy").SetValue(group, energy);
                groupType.GetField("Window").SetValue(group, 1.0);
                groups.Add(group);
            }

            var peaks = new List<Peak>();
            foreach (double energy in peakEnergies)
            {
                peaks.Add(new Peak { Energy = energy, SNR = 10.0 });
            }

            var candidates = new List<object>();
            foreach (double[] link in links)
            {
                object candidate = Activator.CreateInstance(candidateType, true);
                candidateType.GetField("Group").SetValue(candidate, groups[(int)link[0]]);
                candidateType.GetField("Peak").SetValue(candidate, peaks[(int)link[1]]);
                candidateType.GetField("Away").SetValue(candidate, link[2]);
                candidateType.GetField("Snr").SetValue(candidate, 10.0);
                candidates.Add(candidate);
            }

            // Порядок кандидатов у настоящего вызова задан `Candidate.Order`:
            // ближе — раньше. Здесь он воспроизведён своим счётом, чтобы проба
            // не зависела от того же кода, который поверяет.
            candidates.Sort(delegate(object a, object b)
            {
                double aa = (double)candidateType.GetField("Away").GetValue(a);
                double bb = (double)candidateType.GetField("Away").GetValue(b);
                return aa.CompareTo(bb);
            });

            var order = new List<object>(groups);
            var ranked = new List<object>(candidates);
            if (reversed)
            {
                order.Reverse();
                ranked.Reverse();
            }

            object orderList = ToTyped(groupType, order);
            object rankedList = ToTyped(candidateType, ranked);
            object options = Options(candidates);

            var got = (IEnumerable)minCost.Invoke(null, new[] { orderList, options, rankedList });

            cost = 0.0;
            int count = 0;
            foreach (object candidate in got)
            {
                cost += (double)candidateType.GetField("Away").GetValue(candidate);
                count++;
            }

            return count;
        }

        /// <summary>`List&lt;object&gt;` в `List&lt;T&gt;` закрытого типа.</summary>
        static object ToTyped(Type item, List<object> source)
        {
            Type listType = typeof(List<>).MakeGenericType(item);
            object list = Activator.CreateInstance(listType);
            MethodInfo add = listType.GetMethod("Add");
            foreach (object element in source)
            {
                add.Invoke(list, new[] { element });
            }

            return list;
        }

        /// <summary>`Dictionary&lt;Group, List&lt;Candidate&gt;&gt;` кандидатов каждой группы.</summary>
        static object Options(List<object> candidates)
        {
            Type listType = typeof(List<>).MakeGenericType(candidateType);
            Type mapType = typeof(Dictionary<,>).MakeGenericType(groupType, listType);
            object map = Activator.CreateInstance(mapType);
            MethodInfo contains = mapType.GetMethod("ContainsKey");
            PropertyInfo item = mapType.GetProperty("Item");
            MethodInfo add = listType.GetMethod("Add");

            foreach (object candidate in candidates)
            {
                object group = candidateType.GetField("Group").GetValue(candidate);
                if (!(bool)contains.Invoke(map, new[] { group }))
                {
                    item.SetValue(map, Activator.CreateInstance(listType), new[] { group });
                }

                object bag = item.GetValue(map, new[] { group });
                add.Invoke(bag, new[] { candidate });
            }

            return map;
        }
    }
}
