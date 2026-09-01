using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.NucBase;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XrayLinesProbe
{
    /// <summary>
    /// Характеристический рентген ЭЛЕМЕНТА: путь от запроса «W» в NucBase до
    /// образа в полноспектральном разложении.
    ///
    /// Проверяется вся цепочка, потому что рвётся она молча в каждом звене:
    ///
    ///  1. **Запрос.** «W» и «Pb» — символы элементов, а не нуклиды: поиск
    ///     нуклида поднимает запрос в верхний регистр («PB»), и по нему в
    ///     таблице символов не находится ничего. Разбор обязан отличать символ
    ///     от нуклида и приводить регистр сам.
    ///  2. **Данные.** Линии берутся из `xray_fluorescence` — единственной
    ///     таблицы базы, посчитанной из неё же. Проверяются энергии (сверены со
    ///     справочником при импорте) и то, что веса — доли внутри K-серии,
    ///     в сумме 100 %.
    ///  3. **Подпись.** В файл определений линия уходит как «W x-ray». Массового
    ///     числа в подписи нет и быть не может — по этому её и отличают от
    ///     нуклида дальше по дороге. Период полураспада у элемента не определён,
    ///     и разбор его ячейки не должен ронять ввоз.
    ///  4. **Разложение.** Такой компонент — МЕШАЮЩИЙ образ со свободной
    ///     амплитудой: активности за ним нет, и в «пирог» долей он не входит.
    ///     Попади он туда как нуклид — доли всех остальных поехали бы, а на
    ///     экране это выглядит как правдоподобный ответ.
    ///  5. **Кривая эффективности.** Метод делит площадь пика на выход НА
    ///     РАСПАД, которого у флуоресценции нет вовсе. Линия с заполненной
    ///     «интенсивностью» выглядит для конструктора кривой годной — и портит
    ///     кривую тихо.
    ///
    ///     xrayprobe
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ». Собирать с `WeifenLuo.WinFormsUI.Docking.dll`;
    /// рядом с exe нужна `matdb.sqlite` — из неё читаются линии.
    /// </summary>
    static class Program
    {
        static int failed;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            QueryTellsElementFromNuclide();
            LinesComeFromDatabase();
            DefinitionNameHasNoMassNumber();
            ElementXrayIsNuisanceInFsa();
            ElementXrayStaysOutOfEfficiencyCurve();

            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + failed);
            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// Символ элемента против нуклида. Регистр приводится разбором: в
        /// таблице символ записан «Pb», а поиск нуклида поднял бы запрос в «PB».
        /// </summary>
        static void QueryTellsElementFromNuclide()
        {
            Console.WriteLine("=== Символ элемента отличается от нуклида");
            Symbol("W", "W");
            Symbol("w", "W");
            Symbol("PB", "Pb");
            Symbol("pb", "Pb");
            Symbol("Cs-137", null);
            Symbol("137CS", null);
            Symbol("Th232", null);
            // Двух букв, за которыми не стоит элемента, в таблице нет — запрос
            // должен уйти в обычный поиск нуклида, а не в пустой рентген.
            Symbol("Zz", null);
            Symbol("", null);
        }

        /// <summary>Энергии и веса — те, что в базе, и в сумме K-серия.</summary>
        static void LinesComeFromDatabase()
        {
            Console.WriteLine();
            Console.WriteLine("=== Линии из базы");
            NucBaseFramework fw = new NucBaseFramework();
            List<DecayRad> lines = fw.GetFluorescence("W");
            Same("линий у вольфрама", lines.Count, 3);
            if (lines.Count != 3)
            {
                return;
            }

            // Kα1 59.318 и Kα2 57.981 — сверено со справочником при импорте
            // таблицы (database/scheme.md). Порядок: Kα1, Kα2, Kβ.
            Near("W Kα1", lines[0].Energy, 59.318);
            Near("W Kα2", lines[1].Energy, 57.981);
            Near("W Kβ", lines[2].Energy, 68.117);
            Text("метка Kα1", lines[0].XrayType, "KA1");

            double sum = 0.0;
            foreach (DecayRad line in lines)
            {
                sum += line.Intensity;
            }

            Near("сумма долей K-серии, %", sum, 100.0, 0.01);
            if (lines[0].Intensity <= lines[1].Intensity)
            {
                Fail("Kα1 не сильнее Kα2 — веса встали не в том порядке");
            }
            else
            {
                Console.WriteLine("    ок: Kα1 сильнее Kα2 ({0:0.0} против {1:0.0} %)",
                                  lines[0].Intensity, lines[1].Intensity);
            }

            // Свинец домика — второй элемент, ради которого всё и делалось.
            List<DecayRad> lead = fw.GetFluorescence("Pb");
            Same("линий у свинца", lead.Count, 3);
            if (lead.Count == 3)
            {
                Near("Pb Kα1", lead[0].Energy, 74.969);
            }

            // Окно по энергии прикладывается и здесь: иначе «искать от 60 кэВ»
            // молча отдавало бы линии ниже окна.
            Same("в окне 59-70 кэВ", fw.GetFluorescence("W", lowEnergy: 59.0, highEnergy: 70.0).Count, 2);

            // ⛔ ЖЕЛЕЗО: проверка ЖДАЛА ПУСТОТЫ И ВАЛИЛАСЬ, потому что данные
            // с тех пор появились (найдено 23.08.2026). Старая `xray_fluorescence`
            // обрывалась на Z = 30 — энергии линий считались по разности краёв
            // L2/L3, а ниже тридцати этой пары в XCOM нет, — и железа у расчёта
            // не было вовсе. Измеренная `fluorescence_k` (87 элементов от Z = 12,
            // втянута при `F16`) заводит такие элементы ЦЕЛИКОМ, и `Fe` теперь
            // возвращает три линии с Kα1 6.405 кэВ.
            //
            // ⚠ Проба при этом печатала «РАСХОЖДЕНИЙ: 1» неизвестно сколько
            // прогонов подряд — а провал, который всегда есть, читатель
            // перестаёт читать. Проверка переписана на нынешнюю правду; сам
            // случай «элемента нет в таблицах» остался за Z > 100, где данных
            // действительно нет ни в одной из двух.
            List<DecayRad> iron = fw.GetFluorescence("Fe");
            Same("линий у железа (Z=26, `fluorescence_k`)", iron.Count, 3);
            if (iron.Count == 3)
            {
                Near("Fe Kα1", iron[0].Energy, 6.405, 0.01);
            }
        }

        /// <summary>
        /// Подпись «W x-ray» и период полураспада, которого нет.
        /// </summary>
        static void DefinitionNameHasNoMassNumber()
        {
            Console.WriteLine();
            Console.WriteLine("=== Подпись определения");
            string name = BecquerelMonitor.NucBase.NucBase.XrayDefinitionName("W");
            Text("подпись", name, "W x-ray");
            if (!NuclideDefinition.IsElementXrayName(name))
            {
                Fail("подпись «" + name + "» читается как нуклид — рентген пойдёт в активность");
            }
            else
            {
                Console.WriteLine("    ок: подпись читается как элемент, а не нуклид");
            }

            // Обратная сторона того же признака: настоящие нуклиды во всех
            // принятых написаниях обязаны остаться нуклидами.
            foreach (string nuclide in new string[] { "Cs-137", "Cs137", "137CS", "Bi-214 (Ra-226)", "Pa-234m1" })
            {
                if (NuclideDefinition.IsElementXrayName(nuclide))
                {
                    Fail("нуклид «" + nuclide + "» принят за элемент");
                }
                else
                {
                    Console.WriteLine("    ок: «{0}» — нуклид", nuclide);
                }
            }

            // Старая запись «X-ray» без элемента заведена руками и массового
            // числа тоже не имеет: она такой же мешающий образ.
            if (!NuclideDefinition.IsElementXrayName("X-ray"))
            {
                Fail("старая подпись «X-ray» перестала читаться как рентген");
            }
            else
            {
                Console.WriteLine("    ок: «X-ray» — рентген");
            }

            Near("период у рентгена, лет", BecquerelMonitor.NucBase.NucBase.HalfLifeYearsFromCell("0(s)"), 0.0);
            // Ячейку таблица заполняет через ToString() текущей культуры, и
            // разбирать её надо ею же: на русской машине «5.75» не читается, а
            // «5,75» читается. Проба поэтому строит строку тем же способом, что
            // и таблица, — иначе она проверяла бы культуру, а не разбор.
            Near("период " + (5.75).ToString() + "(Y)",
                 BecquerelMonitor.NucBase.NucBase.HalfLifeYearsFromCell((5.75).ToString() + "(Y)"),
                 5.75, 1e-6);
            Near("мусор в ячейке", BecquerelMonitor.NucBase.NucBase.HalfLifeYearsFromCell("-"), 0.0);
        }

        /// <summary>
        /// В разложении рентген элемента — мешающий образ, а не нуклид.
        /// </summary>
        static void ElementXrayIsNuisanceInFsa()
        {
            Console.WriteLine();
            Console.WriteLine("=== В разложении — мешающий образ");
            List<NuclideDefinition> definitions = new List<NuclideDefinition>
            {
                Definition("W x-ray", 59.318, 50.05),
                Definition("W x-ray", 57.981, 28.81),
                Definition("W x-ray", 68.117, 21.14),
                Definition("Cs-137", 661.657, 85.1),
            };

            List<Peak> peaks = new List<Peak>
            {
                Peak(definitions[0]),
                Peak(definitions[3]),
            };

            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, definitions);
            FsaComponent tungsten = Find(library, "W");
            FsaComponent cesium = Find(library, "Cs-137");
            if (tungsten == null || cesium == null)
            {
                Fail("в библиотеке нет вольфрама либо цезия: " + Names(library));
                return;
            }

            Same("линий вольфрама в образе", tungsten.Lines.Count, 3);
            if (tungsten.Kind != FsaComponentKind.Nuisance)
            {
                Fail("вольфрам вошёл как " + tungsten.Kind + " — попадёт в пирог долей активности");
            }
            else
            {
                Console.WriteLine("    ок: вольфрам — мешающий образ со свободной амплитудой");
            }

            if (cesium.Kind != FsaComponentKind.Single)
            {
                Fail("цезий перестал быть нуклидом: " + cesium.Kind);
            }
            else
            {
                Console.WriteLine("    ок: нуклид рядом остался нуклидом");
            }

            // Набор, заведённый до ввоза: линии есть, выходов нет. Образ должен
            // взяться встроенный, и он тоже мешающий.
            List<NuclideDefinition> old = new List<NuclideDefinition>
            {
                Definition("W x-ray", 59.718, 0.0),
                Definition("Cs-137", 661.657, 85.1),
            };
            List<FsaComponent> fallback = FsaLibrary.BuildFromPeaks(
                new List<Peak> { Peak(old[0]), Peak(old[1]) }, old);
            FsaComponent builtin = Find(fallback, "Xray-W");
            if (builtin == null)
            {
                Fail("без выходов встроенный образ рентгена не подставился: " + Names(fallback));
            }
            else if (builtin.Kind != FsaComponentKind.Nuisance)
            {
                Fail("встроенный образ рентгена вошёл как " + builtin.Kind);
            }
            else
            {
                Console.WriteLine("    ок: без выходов взят встроенный образ, тоже мешающий");
            }

            // Ключ подстановки — ТОКЕН имени, всё до первого пробела: «W x-ray»
            // ищется как «W», «Pb x-ray» как «Pb», а «X-ray» — целиком, пробела
            // в нём нет. Проверяются все три написания сразу, потому что строка
            // S29 утверждала, что не совпадает ни одно, и проверить это глазами
            // уже не вышло.
            Substitutes("W x-ray", "Xray-W");
            Substitutes("Pb x-ray", "Xray-Pb");

            // ⛔ А БЕЗЫМЯННЫЙ «X-ray» БОЛЬШЕ НЕ ПОДСТАВЛЯЕТ НИЧЕГО (решение
            // Amber 01.09.2026, `S110`): прежде он подставлялся СВИНЦОМ, то есть
            // разбор угадывал элемент за человека. Дословно: «не может быть
            // безымянных X-ray; если нуклид заносится из базы изотопов, он уже
            // имеет имя». Цепочка теперь такая: элемент набирается в `NucBase` →
            // его линии X-ray уезжают в `NuclideDefinition.xml` → финдер их
            // находит → FSA узнаёт о них ПО ИМЕНИ.
            Substitutes("X-ray", null);

            // А эти НЕ должны подставляться ничем: 15…55 кэВ на K-серию свинца
            // или вольфрама не похожи, и образ там был бы фантомом.
            Substitutes("x-rays", null);
            Substitutes("Low Bremsstrahlung x-rays", null);
        }

        /// <summary>
        /// Имя без выходов подставляет ожидаемый встроенный образ (или не
        /// подставляет ничего, если <paramref name="expected"/> пуст).
        /// </summary>
        static void Substitutes(string name, string expected)
        {
            List<NuclideDefinition> definitions = new List<NuclideDefinition>
            {
                Definition(name, 60.0, 0.0),
                Definition("Cs-137", 661.657, 85.1),
            };
            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(
                new List<Peak> { Peak(definitions[0]), Peak(definitions[1]) }, definitions);

            if (expected == null)
            {
                foreach (FsaComponent component in library)
                {
                    if (component.Name.StartsWith("Xray-", StringComparison.OrdinalIgnoreCase))
                    {
                        Fail("«" + name + "» подставил " + component.Name + " — это фантом");
                        return;
                    }
                }

                Console.WriteLine("    ок: «" + name + "» не подставляет ничего");
                return;
            }

            if (Find(library, expected) == null)
            {
                Fail("«" + name + "» не подставил " + expected + ": " + Names(library));
            }
            else
            {
                Console.WriteLine("    ок: «" + name + "» -> " + expected);
            }
        }

        /// <summary>
        /// Кривая эффективности стоит на выходе НА РАСПАД: линия рентгена в неё
        /// не идёт, даже когда «интенсивность» у неё заполнена.
        /// </summary>
        static void ElementXrayStaysOutOfEfficiencyCurve()
        {
            Console.WriteLine();
            Console.WriteLine("=== В кривую эффективности не идёт");
            NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();
            if (manager == null || manager.NuclideSets == null)
            {
                Fail("менеджер определений не поднялся — запускать надо из копии конфига");
                return;
            }

            NuclideSet set = new NuclideSet { Id = Guid.NewGuid(), Name = "~проба рентгена" };
            manager.NuclideSets.Add(set);
            // Две годные линии нуклида плюс рентген с заполненной долей: без
            // отбора он вошёл бы в кривую третьей точкой.
            manager.NuclideDefinitions.Add(InSet(Definition("Cs-137", 661.657, 85.1), set));
            manager.NuclideDefinitions.Add(InSet(Definition("K-40", 1460.822, 10.66), set));
            manager.NuclideDefinitions.Add(InSet(Definition("W x-ray", 59.318, 50.05), set));

            Dictionary<string, List<BecquerelMonitor.EfficiencyMaker.EfficiencyLine>> chains =
                BecquerelMonitor.EfficiencyMaker.EfficiencyLibrary.BuildChains();
            List<BecquerelMonitor.EfficiencyMaker.EfficiencyLine> lines;
            if (!chains.TryGetValue("~проба рентгена", out lines))
            {
                Fail("набор пробы в кривые не попал вовсе");
                return;
            }

            Same("линий в кривой", lines.Count, 2);
            foreach (BecquerelMonitor.EfficiencyMaker.EfficiencyLine line in lines)
            {
                if (Math.Abs(line.Energy - 59.318) < 0.01)
                {
                    Fail("рентген вольфрама вошёл в кривую — площадь пика поделят на долю K-серии");
                    return;
                }
            }

            Console.WriteLine("    ок: рентген отброшен, в кривой остались две линии распада");
        }

        // --------------------------------------------------------------

        static NuclideDefinition Definition(string name, double energy, double intensity)
        {
            return new NuclideDefinition
            {
                Name = name,
                Energy = energy,
                Intencity = intensity,
                Visible = true,
            };
        }

        static NuclideDefinition InSet(NuclideDefinition definition, NuclideSet set)
        {
            definition.Sets.Add(set.Id);
            return definition;
        }

        static Peak Peak(NuclideDefinition definition)
        {
            return new Peak { Energy = definition.Energy, Nuclide = definition };
        }

        static FsaComponent Find(List<FsaComponent> library, string name)
        {
            foreach (FsaComponent component in library)
            {
                if (string.Equals(component.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return component;
                }
            }

            return null;
        }

        static string Names(List<FsaComponent> library)
        {
            List<string> names = new List<string>();
            foreach (FsaComponent component in library)
            {
                names.Add(component.Name);
            }

            return string.Join(", ", names.ToArray());
        }

        static void Symbol(string query, string expected)
        {
            string got = BecquerelMonitor.NucBase.NucBase.ElementSymbol(query);
            if (!string.Equals(got, expected, StringComparison.Ordinal))
            {
                Fail(string.Format("«{0}»: разобрано как «{1}», ожидалось «{2}»",
                                   query, got ?? "нуклид", expected ?? "нуклид"));
            }
            else
            {
                Console.WriteLine("    ок: «{0}» -> {1}", query, got ?? "поиск нуклида");
            }
        }

        static void Near(string caption, double got, double want, double tolerance = 0.001)
        {
            if (Math.Abs(got - want) > tolerance)
            {
                Fail(string.Format(CultureInfo.InvariantCulture,
                                   "{0}: {1:0.####}, ожидалось {2:0.####}", caption, got, want));
            }
            else
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                                "    ок: {0} = {1:0.####}", caption, got));
            }
        }

        static void Text(string caption, string got, string want)
        {
            if (!string.Equals(got, want, StringComparison.Ordinal))
            {
                Fail(caption + ": «" + got + "», ожидалось «" + want + "»");
            }
            else
            {
                Console.WriteLine("    ок: {0} = «{1}»", caption, got);
            }
        }

        static void Same(string caption, int got, int want)
        {
            if (got != want)
            {
                Fail(caption + ": " + got + ", ожидалось " + want);
            }
            else
            {
                Console.WriteLine("    ок: {0} = {1}", caption, got);
            }
        }

        static void Fail(string message)
        {
            failed++;
            Console.WriteLine("    РАСХОЖДЕНИЕ: " + message);
        }
    }
}
