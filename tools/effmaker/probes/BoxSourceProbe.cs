using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;

namespace BoxSourceProbe
{
    /// <summary>
    /// Базовые проверки прямоугольной кюветы — источника, добавленного рядом с
    /// точкой, цилиндром и маринелли.
    ///
    /// Проверяется то, чего не видит компилятор:
    ///
    /// 1. **Круговорот через файл.** `GeometryWriter` пишет ключи `SB_*`, а
    ///    `GeometryModel.Load` их читает. Это наше расширение формата, ЛСРМ их
    ///    не знает, и ошибка в имени ключа не всплывёт нигде: поле просто
    ///    останется нулём, а кювета молча выродится в плоскую.
    /// 2. **Предел точечного источника.** Кювета, стянутая почти в точку и
    ///    отнесённая далеко, обязана дать ту же эффективность, что точечный
    ///    источник на том же расстоянии. Если розыгрыш точки внутри кюветы или
    ///    сборка сцены перепутали половину стороны с полной, расхождение видно
    ///    сразу.
    /// 3. **Равенство площадей.** Кювета и цилиндр с одинаковой площадью дна,
    ///    высотой и расстоянием обязаны дать почти одно и то же: телесный угол
    ///    у них разный лишь на углах. Это проверка сборки областей, а не
    ///    физики.
    ///
    /// Полная проверка на массовых прогонах — отдельная задача, здесь только
    /// базовая.
    ///
    ///     boxsourceprobe [--n=200000]
    /// </summary>
    static class Program
    {
        /// <summary>
        /// Ключи физики и зерно, которыми накрываются ОБА плеча сравнения.
        ///
        /// ⚠ Заведены 05.09.2026 (`A132`) не для красоты: провал «кювета против
        /// точки на 1461 кэВ» пришёл вместе с подъёмом физики до 16, и назвать
        /// виновника можно было ЛИБО чтением кода, ЛИБО плечами. Плечи дешевле
        /// и, в отличие от чтения, дают число.
        ///
        /// Зерно тут же и по той же причине: пока плечо по зерну не прогонено,
        /// «расхождение» и «шум» неразличимы, а у обоих одна и та же цифра.
        /// </summary>
        sealed class Physics
        {
            public bool? KLCascade, LXrayEscape, XcomPairThreshold,
                         PositronTransport, PositronOffset, RayleighToCrystal;
            public int? Seed;

            public void ApplyTo(EfficiencySimulator s)
            {
                if (this.KLCascade.HasValue) s.KLCascade = this.KLCascade.Value;
                if (this.LXrayEscape.HasValue) s.LXrayEscape = this.LXrayEscape.Value;
                if (this.XcomPairThreshold.HasValue) s.XcomPairThreshold = this.XcomPairThreshold.Value;
                if (this.PositronTransport.HasValue) s.PositronTransport = this.PositronTransport.Value;
                if (this.PositronOffset.HasValue) s.PositronOffset = this.PositronOffset.Value;
                if (this.RayleighToCrystal.HasValue) s.RayleighToCrystal = this.RayleighToCrystal.Value;
                if (this.Seed.HasValue) s.Seed = this.Seed.Value;
            }
        }

        static Physics physics = new Physics();

        /// <summary>
        /// Сколько НЕЗАВИСИМЫХ зёрен усредняется в каждой проверке (`A132`).
        ///
        /// ⛔ Одного зерна мало, и это измерено, а не предположено. На 1461 кэВ
        /// разброс отношения ОТ ЗЕРНА К ЗЕРНУ — 2.1…2.3 % СКО (12 зёрен), и
        /// худшее зерно даёт |r−1| до 5.0 %. Прежняя приёмка судила ОДНО зерно
        /// полосой в 2σ, то есть отказывала примерно на каждом третьем прогоне
        /// сама по себе: 8 проверок по 2σ дают 31 % ложных отказов. Ровно это и
        /// случилось — `A132` завелась на зерне, а не на физике.
        ///
        /// Три зерна снижают шум СРЕДНЕГО в √3 ≈ 1.73 раза и стоят втрое
        /// дороже; больше брать незачем — полоса и так шире мыслимой ошибки
        /// сборки сцены.
        /// </summary>
        static int seeds = 3;

        /// <summary>
        /// Полоса приёмки в СКО среднего. 3.3σ, а не 2σ, и число выбрано под
        /// ЧИСЛО ПРОВЕРОК: их восемь в штатном прогоне, и полоса обязана
        /// держать ложный отказ ВСЕГО ПРОГОНА, а не одной строки. 3.3σ даёт
        /// ~0.1 % на строку и ~0.8 % на прогон; 2σ давали 4.6 % и 31 %.
        /// </summary>
        const double Band = 3.3;

        /// <summary>Подставленная порча — положительный контроль (`A132`).</summary>
        static string breakage = "";
        static int broken, refused;

        /// <summary>Сводная сверка двух оценок шума — см. конец <see cref="Compare"/>.</summary>
        static double sdPooled, anPooled;
        static int pooledCount;

        /// <summary>
        /// Строгий разбор булева ключа (`A77`, вторая волна 06.09.2026). Принимает
        /// РОВНО <c>0</c> и <c>1</c>; на всё остальное бросает, и прогон кончается
        /// в первую секунду.
        ///
        /// ⛔ Прежний разбор был `v == "1" || v == "on" || v == "true"`, то есть
        /// ЛЮБОЕ неизвестное значение он молча толковал как ЛОЖЬ. Это та же беда
        /// `A77`, что и в `CorpusMatrixProbe` (там неизвестное шло за ИСТИНУ), и
        /// стоила она там трёх часов счёта: ключ синтаксически прежний, значение
        /// просто «понятное». ⚠ Хуже того, ключи здесь называются ТАК ЖЕ, как в
        /// `CorpusMatrixProbe` (`--pairth=`, `--positron=`, `--rayl2=` …), а
        /// толковались ИНАЧЕ: `--pairth=far` там отказ, здесь молча «выключено»,
        /// `--pairth=on` там отказ, здесь «включено». Два разбора одних и тех же
        /// ключей, расходящиеся молча, — ровно тот случай, ради которого `A77` и
        /// заведена; поэтому правило здесь ТО ЖЕ, что там, а не своё.
        /// </summary>
        static bool Flag(string arg, int prefix)
        {
            string key = arg.Substring(0, prefix);
            string value = arg.Substring(prefix);
            if (value == "0") return false;
            if (value == "1") return true;
            throw new ArgumentException(
                "ключ " + key + " понимает только 0 и 1, а получил «" + value
                + "». Разбор строгий (`A77`): прежний считал ЛЮБОЕ неизвестное "
                + "значение ложью и молчал.");
        }

        static int Main(string[] args)
        {
            // ⛔ Разделитель дробной части — ВСЕГДА ТОЧКА (правило Amber
            // 05.09.2026, `A242`). Разбор ключей здесь и так шёл инвариантной
            // культурой, а ПЕЧАТЬ — культурой потока, и проба выдавала «полоса
            // 3,3 СКО среднего». Обе стороны чинятся вместе.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            int n = 200000;
            double tolerance = 0.05;
            int spread = 0;
            var dirs = new System.Collections.Generic.List<string>();
            try
            {
            foreach (string a in args)
            {
                if (a.StartsWith("--n="))
                {
                    n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--models="))
                {
                    dirs.Add(a.Substring(9));
                }
                else if (a.StartsWith("--tolerance="))
                {
                    tolerance = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--seed="))
                {
                    physics.Seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--seeds="))
                {
                    seeds = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--spread="))
                {
                    spread = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--break="))
                {
                    breakage = a.Substring(8);
                }
                else if (a.StartsWith("--klcasc="))
                {
                    physics.KLCascade = Flag(a, 9);
                }
                else if (a.StartsWith("--lx="))
                {
                    physics.LXrayEscape = Flag(a, 5);
                }
                else if (a.StartsWith("--pairth="))
                {
                    physics.XcomPairThreshold = Flag(a, 9);
                }
                else if (a.StartsWith("--positron="))
                {
                    physics.PositronTransport = Flag(a, 11);
                }
                else if (a.StartsWith("--posoffset="))
                {
                    physics.PositronOffset = Flag(a, 12);
                }
                else if (a.StartsWith("--rayl2="))
                {
                    physics.RayleighToCrystal = Flag(a, 8);
                }
                else
                {
                    Console.WriteLine("не знаю ключа: " + a);
                    return 2;
                }
            }
            }
            catch (ArgumentException e)
            {
                // `A77`: ОТКАЗ, а не предупреждение. Предупреждение в первой
                // строке прогона никто не читает, а мёртвый ключ виден только
                // по итогу — то есть уже после счёта.
                Console.Error.WriteLine("⛔ " + e.Message);
                return 2;
            }

            if (spread > 0)
            {
                return Spread(n, spread);
            }

            if (breakage.Length > 0)
            {
                Console.WriteLine("### ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: подставлена порча «{0}» — проба ОБЯЗАНА отказать",
                                  breakage);
            }

            Console.WriteLine("зёрен на проверку {0}, историй {1}, полоса {2:F1} СКО среднего",
                              seeds, n, Band);

            int bad = 0;
            bad += RoundTrip();
            bad += PointLimit(n);
            bad += EqualArea(n);
            foreach (string dir in dirs)
            {
                bad += Models(dir, n, tolerance);
            }

            Console.WriteLine();
            bad += NoiseEstimateAgrees();

            if (breakage.Length > 0)
            {
                // ⚠ Порча, ни к чему не приложившаяся, — это НЕ пройденный
                // контроль, а контроль, которого не было.
                Console.WriteLine("### контроль «{0}»: испорчено сцен {1}, отказов {2}",
                                  breakage, broken, refused);
                if (broken == 0)
                {
                    Console.WriteLine("### ⛔ ПОРЧА НИ К ЧЕМУ НЕ ПРИЛОЖИЛАСЬ — контроль НЕ СОСТОЯЛСЯ");
                    return 3;
                }
            }

            Console.WriteLine(bad == 0 ? "ВСЕ ПРОВЕРКИ ПРОШЛИ" : "ПРОВАЛОВ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Развёртка по зерну (`A132`): одна и та же пара сцен считается K раз
        /// независимыми потоками, и печатается РАЗБРОС отношения.
        ///
        /// Зачем. Отдельный прогон отвечает на вопрос «сошлось ли ЗДЕСЬ», а
        /// нужен ответ на «сходится ли ВООБЩЕ»: у одного зерна расхождение и
        /// шум выглядят одинаково. Разброс по зёрнам разводит их сразу —
        /// систематическое смещение переживает смену потока, случайное нет.
        /// </summary>
        static int Spread(int n, int seeds)
        {
            // ⚠ Штатное зерно спрашивается У СИМУЛЯТОРА, а не пишется числом:
            // вписанное сюда, оно однажды разойдётся с настоящим молча, и
            // развёртка начнётся не с того потока, с которого идёт прогон.
            int seed0 = physics.Seed ?? new EfficiencySimulator(Base()).Seed;
            double[] energies = { 60.0, 200.0, 662.0, 1461.0 };
            Console.WriteLine("Развёртка по зерну: {0} зёрен от {1}, историй {2}", seeds, seed0, n);
            Console.WriteLine("   пара 1 — точка против кюветы 0.5 мм на 200 мм");
            Console.WriteLine("   пара 2 — цилиндр против кюветы равной площади дна");

            for (int pair = 1; pair <= 2; pair++)
            {
                Console.WriteLine();
                Console.WriteLine("  пара {0}", pair);
                foreach (double e in energies)
                {
                    double sum = 0.0, sum2 = 0.0, worst = 0.0;
                    for (int i = 0; i < seeds; i++)
                    {
                        physics.Seed = seed0 + i;
                        GeometryModel a, b;
                        if (pair == 1)
                        {
                            PointPair(out a, out b);
                        }
                        else
                        {
                            AreaPair(out a, out b);
                        }

                        EfficiencySimulator sa = Make(a, n);
                        EfficiencySimulator sb = Make(b, n);
                        double da, db;
                        double ea = sa.Efficiency(e, out da);
                        double eb = sb.Efficiency(e, out db);
                        double r = ea > 0.0 ? eb / ea : 0.0;
                        sum += r;
                        sum2 += r * r;
                        worst = Math.Max(worst, Math.Abs(r - 1.0));
                    }

                    double mean = sum / seeds;
                    double sd = Math.Sqrt(Math.Max(0.0, sum2 / seeds - mean * mean));
                    // СКО среднего: именно оно говорит, отличается ли среднее от единицы.
                    double sem = seeds > 1 ? sd / Math.Sqrt(seeds - 1.0) : 0.0;
                    // ⛔ `P` не применяется (`T247`): выше 1000 % он ставит
                    //    разделитель разрядов, а группировки разрядов нет вовсе
                    //    (решение Amber 05.09.2026). Процент — множителем и текстом.
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                      "   {0,6:F0} кэВ  среднее {1:F4}  СКО зёрен {2:F2} %  СКО среднего {3:F2} %"
                                      + "  худшее |r-1| {4:F2} %  отклонение среднего {5:F2}σ",
                                      e, mean, 100.0 * sd, 100.0 * sem, 100.0 * worst,
                                      sem > 0.0 ? Math.Abs(mean - 1.0) / sem : 0.0));
                }
            }

            physics.Seed = seed0;
            return 0;
        }

        /// <summary>
        /// Сводная сверка: та ли ещё величина шум, на который опирается полоса
        /// приёмки (`A132`).
        ///
        /// Полоса стоит на АНАЛИТИЧЕСКОЙ оценке, которую печатает сам
        /// симулятор. 05.09.2026 она сверена с разбросом по 12 зёрнам и
        /// оказалась верна (2.2 % против 2.14…2.29 % на 1461 кэВ) — но это
        /// измерение сегодняшнего дня, а не свойство мира. Разойдись оценка
        /// однажды — проба станет слепой (оценка завышена) или крикливой
        /// (занижена), и НИ ОДНА строка об этом не скажет.
        ///
        /// Судится СУММА по всем строкам прогона: у каждой две степени
        /// свободы, у восьми — шестнадцать, и отношение уже что-то значит.
        /// Полоса [0.4, 2.5] широкая нарочно: ловится ОБВАЛ оценки, а не её
        /// расстройка на десятки процентов.
        /// </summary>
        static int NoiseEstimateAgrees()
        {
            if (pooledCount < 4 || !(anPooled > 0.0) || seeds < 3)
            {
                return 0;
            }

            double ratio = Math.Sqrt(sdPooled / anPooled);
            bool ok = ratio > 0.4 && ratio < 2.5;
            Console.WriteLine("оценка шума против разброса зёрен: сводно по {0} строкам отношение {1:F2}  {2}",
                              pooledCount, ratio, ok ? "ок" : "⛔ РАЗОШЛАСЬ");
            return ok ? 0 : 1;
        }

        static EfficiencySimulator Make(GeometryModel g, int n)
        {
            EfficiencySimulator s = new EfficiencySimulator(g) { Histories = n };
            physics.ApplyTo(s);
            return s;
        }

        static EfficiencySimulator Make(GeometryModel g, int n, int seed)
        {
            EfficiencySimulator s = Make(g, n);
            s.Seed = seed;
            return s;
        }

        /// <summary>
        /// Подставить порчу — положительный контроль (`A132`).
        ///
        /// ⛔ Полоса приёмки расширена с 2σ до 3.3σ и опирается на среднее по
        /// зёрнам; вывод «теперь сходится» без этого ключа ничего не стоит.
        ///
        /// `halfside` — та самая ошибка, ради которой проверка равных площадей
        /// и написана: половина стороны вместо полной.
        /// `distance` — расстояние до детектора на десятую долю меньше.
        /// </summary>
        static void Break(GeometryModel box)
        {
            if (breakage.Length == 0)
            {
                return;
            }

            if (breakage == "halfside")
            {
                box.BoxSourceX *= 0.5;
                box.BoxSourceY *= 0.5;
                broken++;
            }
            else if (breakage == "distance")
            {
                box.BoxToDetectorDistance *= 0.9;
                broken++;
            }
            else
            {
                throw new ArgumentException("не знаю порчи: " + breakage);
            }
        }

        /// <summary>Геометрия-образец: кристалл маленький, вся суть в источнике.</summary>
        static GeometryModel Base()
        {
            GeometryModel g = new GeometryModel
            {
                Name = "boxprobe",
                CrystalDiameter = 20.0,
                CrystalHeight = 20.0,
                FrontReflectorThickness = 1.0,
                SideReflectorThickness = 1.0,
                FrontCladdingThickness = 1.0,
                SideCladdingThickness = 1.0,
                MountingThickness = 1.0,
            };

            g.Crystal = Material("CsI");
            g.Reflector = Material("PTFE");
            g.Cladding = Material("Al");
            g.BeakerWall = Material("PP");
            g.Source = Material("H2O");
            return g;
        }

        static GeometryMaterial Material(string abbr)
        {
            foreach (GeometryMaterialLibrary.MaterialKind kind
                     in Enum.GetValues(typeof(GeometryMaterialLibrary.MaterialKind)))
            {
                foreach (GeometryMaterialLibrary.Entry entry in GeometryMaterialLibrary.Of(kind))
                {
                    if (string.Equals(entry.Abbr, abbr, StringComparison.OrdinalIgnoreCase))
                    {
                        return GeometryMaterialLibrary.Make(entry, entry.Density);
                    }
                }
            }

            throw new ArgumentException("нет вещества " + abbr);
        }

        static int RoundTrip()
        {
            Console.WriteLine("1. Круговорот через файл");
            GeometryModel g = Base();
            g.SourceType = GeometrySourceType.Box;
            g.BoxSourceX = 71.0;
            g.BoxSourceY = 43.0;
            g.BoxSourceHeight = 17.0;
            g.BoxToDetectorDistance = 23.0;
            g.BoxSideWallThickness = 1.5;
            g.BoxEndWallThickness = 2.5;

            string path = Path.Combine(Path.GetTempPath(), "boxprobe.in");
            File.WriteAllText(path, GeometryWriter.Render(g));
            GeometryModel back = GeometryModel.Load(path);

            int bad = 0;
            bad += Same("SourceType", (double)g.SourceType, (double)back.SourceType);
            bad += Same("BoxSourceX", g.BoxSourceX, back.BoxSourceX);
            bad += Same("BoxSourceY", g.BoxSourceY, back.BoxSourceY);
            bad += Same("BoxSourceHeight", g.BoxSourceHeight, back.BoxSourceHeight);
            bad += Same("BoxToDetectorDistance", g.BoxToDetectorDistance, back.BoxToDetectorDistance);
            bad += Same("BoxSideWallThickness", g.BoxSideWallThickness, back.BoxSideWallThickness);
            bad += Same("BoxEndWallThickness", g.BoxEndWallThickness, back.BoxEndWallThickness);
            return bad;
        }

        static int Same(string what, double a, double b)
        {
            bool ok = Math.Abs(a - b) <= 1e-6 * Math.Max(1.0, Math.Abs(a));
            Console.WriteLine("   {0,-24} {1,10:G6} -> {2,10:G6}  {3}",
                              what, a, b, ok ? "ок" : "РАЗЪЕХАЛОСЬ");
            return ok ? 0 : 1;
        }

        static void PointPair(out GeometryModel point, out GeometryModel box)
        {
            point = Base();
            point.SourceType = GeometrySourceType.Point;
            point.PointDistance = 200.0;

            box = Base();
            box.SourceType = GeometrySourceType.Box;
            box.BoxSourceX = 0.5;
            box.BoxSourceY = 0.5;
            box.BoxSourceHeight = 0.5;
            box.BoxToDetectorDistance = 200.0;
            Break(box);
        }

        static void AreaPair(out GeometryModel cyl, out GeometryModel box)
        {
            cyl = Base();
            cyl.SourceType = GeometrySourceType.Cylinder;
            cyl.BeakerDiameter = 40.0;
            cyl.SourceHeight = 6.0;
            cyl.BeakerToDetectorDistance = 50.0;

            double side = 20.0 * Math.Sqrt(Math.PI);      // та же площадь, что у круга R = 20
            box = Base();
            box.SourceType = GeometrySourceType.Box;
            box.BoxSourceX = side;
            box.BoxSourceY = side;
            box.BoxSourceHeight = 6.0;
            box.BoxToDetectorDistance = 50.0;
            Break(box);
        }

        static int PointLimit(int n)
        {
            Console.WriteLine("2. Предел точечного источника (кювета 0.5 мм на 200 мм)");
            GeometryModel point, box;
            PointPair(out point, out box);
            return Compare(point, box, n, 0.03);
        }

        static int EqualArea(int n)
        {
            Console.WriteLine("3. Кювета и цилиндр равной площади дна");
            GeometryModel cyl, box;
            AreaPair(out cyl, out box);
            // ⛔ Допуск 2 %, а не 5 % (`A132`, 05.09.2026), и подняло его не
            // мнение, а ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. Проверка написана против
            // путаницы «половина стороны вместо полной», и подставленная
            // ровно эта ошибка двигает отношение всего на 3.8…4.9 % — то есть
            // МЕНЬШЕ прежнего допуска. Пять лет такая проверка отвечала бы
            // «ок» на ту самую ошибку, ради которой стоит.
            //
            // 2 % названы измерением: настоящая разница круга и квадрата
            // равной площади на этой сцене — 0.1…0.6 % (среднее по 12 зёрнам,
            // все четыре энергии), то есть запас втрое-двадцатикратный.
            // ⚠ Ключ `--tolerance=` (массовая проверка по настоящим сценам)
            // остаётся 5 %: там сцены стоят и вплотную, где разница по углам
            // действительно растёт.
            return Compare(cyl, box, n, 0.02);
        }

        /// <summary>
        /// Сравнить две геометрии на нескольких энергиях.
        ///
        /// ⛔ Судится СРЕДНЕЕ ПО НЕСКОЛЬКИМ ЗЁРНАМ, а не одно зерно (`A132`).
        /// Одно зерно на 1461 кэВ имеет СКО 2.2 %, и провал «расходится» там
        /// значил ровно то, что зерно легло неудачно: на физике 15 та же проба
        /// проходила НЕ потому, что физика была другой, а потому, что при
        /// другом потоке случайных чисел выпало другое зерно. Развёрткой по 12
        /// зёрнам это разведено числом: среднее 0.995 ± 0.65 % на физике 16 и
        /// 0.992 ± 0.69 % на физике 15 — обе единицы в пределах 1.2σ.
        ///
        /// Шум берётся АНАЛИТИЧЕСКИЙ (его считает сам симулятор) и делится на
        /// √k: развёртка показала, что он верен — 2.2 % против измеренных по
        /// зёрнам 2.14…2.29 %. Выборочное СКО по трём зёрнам взять нельзя: у
        /// него две степени свободы, и полоса по нему гуляла бы вдесятеро.
        /// Согласие обеих оценок проверяется тут же (см. ниже) — иначе проба
        /// молча обопрётся на устаревшую.
        /// </summary>
        static int Compare(GeometryModel a, GeometryModel b, int n, double tolerance)
        {
            double[] energies = { 60.0, 200.0, 662.0, 1461.0 };
            int k = Math.Max(1, seeds);
            int seed0 = physics.Seed ?? new EfficiencySimulator(a).Seed;

            int m = energies.Length;
            double[] sum = new double[m], sum2 = new double[m], noise = new double[m];
            double[] lastA = new double[m], lastB = new double[m];
            for (int s = 0; s < k; s++)
            {
                EfficiencySimulator sa = Make(a, n, seed0 + s);
                EfficiencySimulator sb = Make(b, n, seed0 + s);
                for (int i = 0; i < m; i++)
                {
                    double da, db;
                    double ea = sa.Efficiency(energies[i], out da);
                    double eb = sb.Efficiency(energies[i], out db);
                    double r = ea > 0.0 ? eb / ea : 0.0;
                    sum[i] += r;
                    sum2[i] += r * r;
                    noise[i] += 0.01 * Math.Sqrt(da * da + db * db);
                    lastA[i] = ea;
                    lastB[i] = eb;
                }
            }

            int bad = 0;
            for (int i = 0; i < m; i++)
            {
                double mean = sum[i] / k;
                double pop = Math.Sqrt(Math.Max(0.0, sum2[i] / k - mean * mean));
                // Несмещённая оценка СКО по k зёрнам: делить надо на (k−1).
                double sd = k > 1 ? pop * Math.Sqrt(k / (k - 1.0)) : 0.0;
                double analytic = noise[i] / k;
                double sigma = analytic / Math.Sqrt(k);
                double band = Math.Max(tolerance, Band * sigma);
                bool ok = Math.Abs(mean - 1.0) <= band;
                // ⛔ `P` не применяется (`T247`), см. довод выше по файлу.
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                  "   {0,6:F0} кэВ  {1:E3} / {2:E3} = {3:F4}  (шум {4:F1} %, зёрна {5:F1} %, полоса {6:F1} %)  {7}",
                                  energies[i], lastB[i], lastA[i], mean,
                                  100.0 * analytic, 100.0 * sd, 100.0 * band,
                                  ok ? "ок" : "РАСХОДИТСЯ"));

                // ⚠ ЧИТАТЕЛЬ ПРИЗНАКА ОТКАЗА — но СВОДНЫЙ, а не построчный.
                //
                // ⛔ Первая редакция судила согласие двух оценок шума на КАЖДОЙ
                // строке, и это была ровно та беда, которую здесь и чинят: у
                // СКО по трём зёрнам ДВЕ степени свободы, и даже
                // четырёхкратный запас пробивается в 9 % случаев на строку —
                // то есть примерно в половине прогонов. Измерено сразу же:
                // первый прогон дал «2.23 % против 0.32 %» и отказ на ровном
                // месте. Судить это можно только СЛОЖИВ строки: восемь по две
                // степени свободы дают шестнадцать, и вот там оценка уже
                // что-то значит. Итог печатается в конце прогона.
                sdPooled += sd * sd;
                anPooled += analytic * analytic;
                pooledCount++;

                if (!ok)
                {
                    bad++;
                    refused++;
                }
            }

            return bad;
        }

        /// <summary>
        /// МАССОВАЯ проверка (X1): каждой цилиндрической сцене строится кювета
        /// РАВНОЙ ПЛОЩАДИ ДНА, и обе считаются на четырёх энергиях. Ответы
        /// обязаны сойтись: телесный угол у круга и у квадрата равной площади
        /// различается только по углам.
        ///
        /// Зачем массово, когда есть третья проверка выше. Та стоит на ОДНОЙ
        /// сцене (40 мм на 50 мм), а «по углам» — величина, растущая с
        /// отношением размера к расстоянию: у сцены вплотную угол квадрата
        /// виден совсем не так, как у дальней. Настоящие сцены корпуса и
        /// поставки покрывают этот разброс — от съёмок впритык до 250 мм, — и
        /// именно они показывают, где приближение перестаёт работать.
        ///
        /// Маринелли и точечные пропускаются: кюветы у них нет по построению.
        /// </summary>
        static int Models(string dir, int n, double tolerance)
        {
            string[] files = Directory.GetFiles(dir, "*.in");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine();
            Console.WriteLine("4. Кювета против цилиндра на настоящих сценах: {0} ({1} файлов)",
                              dir, files.Length);

            int bad = 0, done = 0;
            foreach (string path in files)
            {
                GeometryModel cyl = GeometryModel.Load(path);
                if (cyl.SourceType != GeometrySourceType.Cylinder
                    || !(cyl.BeakerDiameter > 0.0) || !(cyl.SourceHeight > 0.0))
                {
                    continue;
                }

                GeometryModel box = cyl.Clone();
                box.SourceType = GeometrySourceType.Box;
                // Равная площадь дна: a² = πD²/4.
                box.BoxSourceX = box.BoxSourceY = 0.5 * cyl.BeakerDiameter * Math.Sqrt(Math.PI);
                box.BoxSourceHeight = cyl.SourceHeight;
                box.BoxToDetectorDistance = cyl.BeakerToDetectorDistance;
                box.BoxSideWallThickness = cyl.BeakerSideWallThickness;
                box.BoxEndWallThickness = cyl.BeakerEndWallThickness;
                Break(box);

                Console.WriteLine("   {0}  (Ø{1:F0} × {2:F0} мм на {3:F0} мм)",
                                  Path.GetFileNameWithoutExtension(path), cyl.BeakerDiameter,
                                  cyl.SourceHeight, cyl.BeakerToDetectorDistance);
                bad += Compare(cyl, box, n, tolerance);
                done++;
            }

            Console.WriteLine("   цилиндрических сцен проверено: {0}", done);
            return bad;
        }
    }
}
