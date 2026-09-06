using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RoundTrip
{
    /// <summary>
    /// Круговая проверка записи геометрии: файл читается, пишется нашим
    /// writer'ом, читается снова — и по обоим считается эффективность.
    ///
    /// Сверяется не текст, а ЧИСЛО: совпадение строк ничего не доказывает
    /// (можно совпасть и с одинаково неверным разбором), а совпадение кривой
    /// при одинаковом зерне означает, что сцена собралась ровно та же.
    /// </summary>
    static class Program
    {
        static readonly double[] Energies = { 50, 100, 300, 662, 1461, 2614 };

        /// <summary>
        /// Порог расхождения кривой, % — и он НЕ «ноль» (`A131`, `T147`).
        ///
        /// ⛔ Прежде стояло 1e-9 %, то есть 1e-11 ОТНОСИТЕЛЬНЫХ, и это ниже
        /// того, что держит сама двойная арифметика на сумме по историям.
        /// Проба ловила собственное округление: `Nano16Pro_box.in` расходился
        /// на 3.8e-9…8.2e-9 % — печаталось «+0.000 %» и объявлялось
        /// РАСХОЖДЕНИЕМ. Отличить это от настоящей потери было нечем: у
        /// настоящей в том же столбце стояло такое же «+0.000 %».
        ///
        /// Порог берётся у ФОРМАТА, а не у наблюдения. Писатель кладёт `.in`
        /// в сантиметрах через `G8` — восемь значащих, — значит любой размер
        /// на круге вправе сдвинуться на 5e-9 относительных, и кривая за ним.
        /// 1e-5 % (1e-7 относительных) — двадцатикратный запас над этим
        /// пределом и в тысячи раз ниже самой мелкой НАСТОЯЩЕЙ потери,
        /// какую проба видела (0.16 %). Обе стороны названы числом нарочно:
        /// порог, взятый «чтобы прошло», молчит навсегда.
        /// </summary>
        const double CurveTolerancePercent = 1e-5;

        /// <summary>Подставленная порча — положительный контроль (`A131`).</summary>
        static string breakage = "";

        static int Main(string[] args)
        {
            var free = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--break=", StringComparison.Ordinal))
                {
                    breakage = a.Substring(8);
                }
                else
                {
                    free.Add(a);
                }
            }

            if (free.Count < 2)
            {
                Console.Error.WriteLine("roundtrip <каталог[;каталог...]> <каталог для записи> [--break=order|size]");
                return 1;
            }

            args = free.ToArray();
            if (breakage.Length > 0)
            {
                Console.WriteLine("### ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: подставлена порча «{0}» — проба ОБЯЗАНА отказать",
                                  breakage);
            }

            // ⛔ КАТАЛОГОВ МОЖЕТ БЫТЬ НЕСКОЛЬКО (`A163`, 05.09.2026). Штатный
            // прогон до сегодня брал ОДИН каталог — `tools\effmaker\models`, 15
            // сцен, — а 44 корпусные геометрии проверялись руками и от случая к
            // случаю. Именно там и жил дефект кодировки (`A161`): 72 отказа из
            // 88, и все 72 только на имени вещества. Список каталогов через `;`
            // делает корпус частью штатного прогона, а один каталог остаётся
            // прежней строкой без изменений.
            //
            // ⚠ Имена файлов в каталогах ПОВТОРЯЮТСЯ (`Nano16Pro.in` есть и в
            // `models`, и в `LSRM Geometries\Models`), поэтому цель нумеруется:
            // общее имя затирало бы одну сцену другой молча.
            var sources = new List<string>();
            foreach (string d in args[0].Split(';'))
            {
                if (d.Trim().Length == 0)
                {
                    continue;
                }

                if (!Directory.Exists(d.Trim()))
                {
                    Console.Error.WriteLine("НЕТ КАТАЛОГА: " + d.Trim());
                    return 1;
                }

                sources.Add(d.Trim());
            }

            int bad = 0, seen = 0;
            foreach (string dir in sources)
            {
                Console.WriteLine();
                Console.WriteLine("### каталог {0}", dir);
                int here = 0, count = 0;
                string[] found = Directory.GetFiles(dir, "*.in");
                Array.Sort(found, StringComparer.Ordinal);
                foreach (string path in found)
                {
                    count++;
                    here += Check(path, Target(args[1], seen++, "", path), false) ? 0 : 1;
                }

                // Вторая ветвь: кристалл-цилиндр. У всех наших моделей кристалл
                // прямоугольный, и диаметр с высотой там ПРОИЗВОДНЫЕ — не читаются
                // расчётом вовсе. Чтобы проверить и их, форма принудительно
                // сводится к цилиндру: тогда эти два поля становятся входными.
                Console.WriteLine();
                Console.WriteLine("### та же проверка с принудительно цилиндрическим кристаллом");
                foreach (string path in found)
                {
                    count++;
                    here += Check(path, Target(args[1], seen++, "cyl_", path), true) ? 0 : 1;
                }

                Console.WriteLine();
                Console.WriteLine("### итог каталога {0}: проверок {1}, расхождений {2}", dir, count, here);
                bad += here;
            }

            Console.WriteLine();
            if (breakage.Length > 0)
            {
                // ⚠ Порча, ни к одной сцене не приложившаяся, — это НЕ пройденный
                // контроль, а контроль, которого не было. Печатается числом.
                Console.WriteLine("### контроль «{0}»: испорчено сцен {1}, расхождений {2}",
                                  breakage, broken, bad);
                if (broken == 0)
                {
                    Console.WriteLine("### ⛔ ПОРЧА НИ К ЧЕМУ НЕ ПРИЛОЖИЛАСЬ — контроль НЕ СОСТОЯЛСЯ");
                    return 3;
                }
            }

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("РАСХОЖДЕНИЙ: {0}", bad));
            return bad == 0 ? 0 : 2;
        }

        /// <summary>
        /// Путь цели: НОМЕР проверки плюс имя исходника. Номер обязателен —
        /// тёзки из разных каталогов иначе пишутся в один файл (`A163`).
        /// </summary>
        static string Target(string dir, int index, string prefix, string source)
        {
            return Path.Combine(dir, index.ToString("000", CultureInfo.InvariantCulture)
                                     + "_" + prefix + Path.GetFileName(source));
        }

        static bool Check(string source, string target, bool forceCylinder)
        {
            GeometryModel a = GeometryModel.Load(source);
            if (forceCylinder)
            {
                // ⛔ (`A94`) Сведение к цилиндру ОБЯЗАНО задать его размеры. У
                // бруска полей `CrystalDiameter`/`CrystalHeight` нет — чтение
                // их больше не заводит, — и прежняя строка «сменить форму и
                // обнулить брусок» оставила бы кристалл нулевым: сцена пустая,
                // кривая нулевая, а проба отчиталась бы о РАСХОЖДЕНИИ, не
                // назвав причины. Равноценный цилиндр берётся тем же правилом
                // LSRM, каким его пишет в файл `GeometryWriter`.
                if (a.Shape == CrystalShape.Box)
                {
                    double d = AsWritten(GeometryWriter.EquivalentDiameter(a.CrystalBoxX, a.CrystalBoxY));
                    double h = AsWritten(a.CrystalBoxZ);
                    a.Shape = CrystalShape.Cylinder;
                    a.CrystalDiameter = d;
                    a.CrystalHeight = h;
                }

                a.Shape = CrystalShape.Cylinder;
                a.DropDeadCrystalSize();
            }

            Break(a, false);
            GeometryWriter.Save(a, target);
            GeometryModel b = GeometryModel.Load(target);
            Break(b, true);

            Console.WriteLine("=== {0}", Path.GetFileName(target));
            Console.WriteLine("    было : {0}", a.Describe());
            Console.WriteLine("    стало: {0}", b.Describe());

            bool ok = true;
            Dictionary<string, string> fa = Fields(a), fb = Fields(b);
            foreach (KeyValuePair<string, string> pair in fa)
            {
                // У прямоугольного кристалла диаметр и высота цилиндра —
                // ПРОИЗВОДНЫЕ: расчёт их не читает, а писатель нарочно
                // пересчитывает по правилу LSRM. Их расхождение не ошибка, но
                // молчать о нём нельзя — печатаем отдельной пометкой.
                bool derived = a.Shape == CrystalShape.Box
                               && (pair.Key == "CrystalDiameter" || pair.Key == "CrystalHeight");
                string other;
                if (!fb.TryGetValue(pair.Key, out other) || other != pair.Value)
                {
                    Console.WriteLine("    {0} {1}: {2} -> {3}",
                                      derived ? "производное" : "ПОЛЕ", pair.Key, pair.Value, other ?? "нет");
                    ok = ok && derived;
                }
            }

            EfficiencySimulator sa = new EfficiencySimulator(a) { Histories = 60000 };
            EfficiencySimulator sb = new EfficiencySimulator(b) { Histories = 60000 };
            foreach (double e in Energies)
            {
                double ea, eb, err;
                ea = sa.Efficiency(e, out err);
                eb = sb.Efficiency(e, out err);
                double delta = ea > 0.0 ? (eb / ea - 1.0) * 100.0 : (eb > 0.0 ? 100.0 : 0.0);
                if (Math.Abs(delta) > CurveTolerancePercent)
                {
                    // ⚠ (`A131`) Отклонение печатается ЕЩЁ И порядком величины.
                    // «+0.000 %» стояло у расхождения в 1e-11 и у расхождения в
                    // 2 %, и по столбцу они выглядели одинаково: разбор,
                    // потерявший поле, и последний бит округления читались как
                    // одна беда. Разряд `T147`.
                    Console.WriteLine("    {0,6:F0} кэВ: {1:E4} -> {2:E4}  ({3:+0.000;-0.000} %, |откл| {4:E2} %)",
                                      e, ea, eb, delta, Math.Abs(delta));
                    ok = false;
                }
            }

            Console.WriteLine(ok ? "    кривая совпала точно" : "    РАСХОЖДЕНИЕ");
            return ok;
        }

        /// <summary>
        /// Подставить порчу — положительный контроль (`A131`).
        ///
        /// ⛔ Без него вывод «стало сходиться» ничего не стоит: проба, у
        /// которой ослаблен порог, сходится и на сломанном входе тоже.
        ///
        /// `order` — перевернуть порядок элементов ПРОБЫ. Круг его выправит
        /// (писатель сортирует, читатель с `A131` тоже), значит модель до и
        /// после разойдутся порядком.
        ///
        /// ⛔ **ЧЕМ ЭТО ЛОВИТСЯ — с 05.09.2026 ДРУГИМ (`A162`).** Прежде здесь
        /// стояло «ловит только кривая», и это было верно: порядок был входом
        /// розыгрыша (`PickAtom`, `SampleFluorescence`), и перестановка уводила
        /// поток случайных чисел. `A162` канонизировал порядок НА ПОТРЕБЛЕНИИ —
        /// `EfficiencySimulator` сортирует состав своей копии модели, — и
        /// кривая от перестановки больше НЕ ДВИГАЕТСЯ ВООБЩЕ. Измерено на этом
        /// самом контроле: 30 отказов из 30, и все тридцать — по полю
        /// `Source.Order`, строк с расхождением кривой НОЛЬ.
        ///
        /// То есть контроль жив, но держит его теперь `.Order` в списке полей
        /// (заведён `A131`), а не кривая. ⚠ Убрав `.Order` из <see cref="Fields"/>,
        /// вы обесточите этот контроль полностью и не увидите этого ничем.
        ///
        /// `size` — сдвинуть ДЛИНУ КРИСТАЛЛА на 1e-6 относительных: настоящая
        /// мелкая потеря, которую ослабленный порог обязан по-прежнему видеть.
        /// ⚠ Именно кристалл, и ПО ФОРМЕ (`A47`): первая редакция двигала
        /// стенку сосуда, а у одиннадцати сцен из пятнадцати источник точечный
        /// или маринелли — стенки в сцене нет вовсе, кривая не шелохнулась, и
        /// отказ пришёл ТОЛЬКО от списка полей. Контроль порога КРИВОЙ обязан
        /// двигать то, что в сцене есть всегда.
        ///
        /// ⛔ СТОРОНА ПОРЧИ — половина дела, и на ней я споткнулся 05.09.2026.
        /// Первая редакция портила ОБЕ модели: порча вносилась ДО записи, файл
        /// её честно переносил (`G8` держит 8 значащих, сдвиг 1e-6 в них
        /// влезает), и обе стороны круга приходили одинаково испорченными —
        /// «расхождений 0», контроль показал ПУСТОТУ. Поэтому:
        /// `order` вносится ДО записи (круг обязан его выправить — писатель
        /// сортирует), а `wall` — ПОСЛЕ чтения, иначе он доедет до обеих.
        /// Признак «испорчено сцен N, расхождений 0» ловит именно этот случай.
        /// </summary>
        static void Break(GeometryModel g, bool afterRead)
        {
            if (breakage.Length == 0)
            {
                return;
            }

            if (breakage == "order")
            {
                if (afterRead)
                {
                    return;
                }

                GeometryMaterial m = g.Source;
                if (m == null || m.Fractions.Count < 2)
                {
                    return;
                }

                List<int> zs = new List<int>(m.Fractions.Keys);
                List<double> vs = new List<double>();
                foreach (int z in zs)
                {
                    vs.Add(m.Fractions[z]);
                }

                zs.Reverse();
                vs.Reverse();
                m.Fractions.Clear();
                for (int i = 0; i < zs.Count; i++)
                {
                    m.Fractions[zs[i]] = vs[i];
                }

                broken++;
            }
            else if (breakage == "size")
            {
                if (!afterRead)
                {
                    return;
                }

                if (g.Shape == CrystalShape.Box)
                {
                    g.CrystalBoxZ *= 1.000001;
                }
                else
                {
                    g.CrystalHeight *= 1.000001;
                }

                broken++;
            }
            else
            {
                throw new ArgumentException("не знаю порчи: " + breakage);
            }
        }

        static int broken;

        /// <summary>
        /// То же число, но с ТОЧНОСТЬЮ ФАЙЛА.
        ///
        /// Писатель кладёт `.in` в сантиметрах через `G8`, и равноценный
        /// диаметр 18.5411617 мм возвращается из файла как 18.541162. Кладя в
        /// принудительный цилиндр полную точность, круговая проверка
        /// расходилась бы на восьмом знаке у каждой геометрии-бруска и мерила
        /// бы ФОРМАТ ЗАПИСИ, а не разбор: измерено 04.09.2026 — девять
        /// расхождений из тринадцати были ровно этим.
        /// </summary>
        static double AsWritten(double mm)
        {
            double cm = mm / GeometryModel.MmPerCm;
            return double.Parse(cm.ToString("G8", CultureInfo.InvariantCulture),
                                NumberStyles.Float, CultureInfo.InvariantCulture)
                   * GeometryModel.MmPerCm;
        }

        /// <summary>Всё, что наш разбор берёт из файла, — плоским списком.</summary>
        static Dictionary<string, string> Fields(GeometryModel g)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            Action<string, double> n = (k, v) => map[k] = v.ToString("G10", CultureInfo.InvariantCulture);
            n("CrystalDiameter", g.CrystalDiameter);
            n("CrystalHeight", g.CrystalHeight);
            n("BoxX", g.CrystalBoxX); n("BoxY", g.CrystalBoxY); n("BoxZ", g.CrystalBoxZ);
            n("FrontRefl", g.FrontReflectorThickness); n("SideRefl", g.SideReflectorThickness);
            n("FrontClad", g.FrontCladdingThickness); n("SideClad", g.SideCladdingThickness);
            n("Mount", g.MountingThickness);
            n("PointDistance", g.PointDistance);
            n("BeakerDist", g.BeakerToDetectorDistance); n("BeakerD", g.BeakerDiameter);
            n("BeakerH", g.BeakerHeight); n("BeakerSide", g.BeakerSideWallThickness);
            n("BeakerEnd", g.BeakerEndWallThickness); n("SrcH", g.SourceHeight);
            n("MarD", g.MarinelliBeakerDiameter); n("MarH", g.MarinelliBeakerHeight);
            n("MarHoleD", g.MarinelliHoleDiameter); n("MarHoleH", g.MarinelliHoleHeight);
            n("MarSide", g.MarinelliSideThickness); n("MarEnd", g.MarinelliEndWallThickness);
            n("MarHoleSide", g.MarinelliHoleSideThickness); n("MarHoleEnd", g.MarinelliHoleEndWallThickness);
            n("MarSrcH", g.MarinelliSourceHeight); n("MarDist", g.MarinelliToDetectorDistance);
            map["Shape"] = g.Shape.ToString();
            map["SourceType"] = g.SourceType.ToString();
            map["Scint"] = g.IsScintillator.ToString();
            Mat(map, "Crystal", g.Crystal); Mat(map, "Reflector", g.Reflector);
            Mat(map, "Cladding", g.Cladding); Mat(map, "Wall", g.BeakerWall);
            Mat(map, "Source", g.Source);
            return map;
        }

        static void Mat(Dictionary<string, string> map, string name, GeometryMaterial m)
        {
            map[name + ".Name"] = m.Name;
            map[name + ".Ro"] = m.Density.ToString("G8", CultureInfo.InvariantCulture);
            List<int> zs = new List<int>(m.Fractions.Keys);
            zs.Sort();
            string text = "";
            foreach (int z in zs)
            {
                text += string.Format(CultureInfo.InvariantCulture, "{0}:{1:F6} ", z, m.Fractions[z]);
            }

            map[name + ".Comp"] = text.Trim();

            // ⛔ (`A131`) ПОРЯДОК элементов — не украшение, а вход расчёта.
            // Состав лежит в `Dictionary<int,double>`, порядок в нём —
            // порядок вставки, и по нему строятся массивы, из которых
            // `PickAtom` и `SampleFluorescence` ВЫБИРАЮТ элемент розыгрышем.
            // Переставь два элемента — то же самое случайное число попадёт в
            // другой элемент, поток разойдётся, и кривая уедет на величину
            // шума. Сортированный `Comp` выше этого не видит по построению.
            List<int> order = new List<int>(m.Fractions.Keys);
            string seq = "";
            foreach (int z in order)
            {
                seq += z.ToString(CultureInfo.InvariantCulture) + " ";
            }

            map[name + ".Order"] = seq.Trim();
        }
    }
}
