using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace ChainProbe
{
    /// <summary>
    /// Родительская цепочка у линии: поле <c>NuclideDefinition.Chain</c>.
    ///
    /// До него принадлежность линии к ряду жила ТОЛЬКО в хвосте подписи
    /// («Bi-214 (Ra-226)»), и разбирали этот хвост порознь конструктор кривой и
    /// сборка библиотеки образов. Проверяется то, чего не видит компилятор:
    ///
    /// 1. РАЗБОР подписи — на списке случаев, включая те, на которых наивный
    ///    разбор ломается: имя без скобок, пустые скобки, изомер.
    /// 2. ХРАНЕНИЕ. Поле обязано пережить запись в файл; файл, записанный БЕЗ
    ///    поля (а таковы все конфиги до сегодня), обязан читаться, и цепочка в
    ///    нём обязана восстановиться из подписи.
    /// 3. СОГЛАСИЕ ПОТРЕБИТЕЛЕЙ. Оба места, разбиравшие подпись сами, теперь
    ///    зовут общий разбор — сверяется, что они дают ровно его ответ. Это и
    ///    была причина заводить поле: два разбора могли разойтись, и никто бы
    ///    не заметил.
    /// 4. ФОРМА. Поле показывается и сохраняется. Признак, заведённый без
    ///    читателя на форме, — ошибка, на которой здесь уже попадались.
    ///
    ///     chainprobe
    ///
    /// Конфиг берётся ТОЛЬКО из текущего каталога: пробу запускают из копии.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            // То же, что делает MainForm при запуске (MainForm.cs:144). Числа
            // базы нуклидов разбираются ТЕКУЩЕЙ культурой, и на русской машине
            // без этой подмены «0.353» — не число: getDecayRad ловит
            // FormatException, показывает окно с ошибкой и возвращает null.
            // Проба без подмены висит на этом окне, а не падает.
            CultureInfo culture = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ".";
            Thread.CurrentThread.CurrentCulture = culture;

            int bad = 0;
            bad += CheckParsing();
            bad += CheckPersistence();
            bad += CheckConsumers();
            bad += CheckBranches();
            bad += CheckTwoWalks();
            bad += CheckKSeriesEditor();
            bad += CheckSearch();
            bad += CheckForm();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("НЕ СОШЛОСЬ: {0}", bad));
            return bad == 0 ? 0 : 1;
        }

        static int CheckParsing()
        {
            Console.WriteLine("=== разбор подписи ===");
            string[,] cases =
            {
                // подпись                  нуклид      ряд
                { "Bi-214 (Ra-226)",        "Bi-214",   "Ra-226" },
                { "Tl-208 (Th-232)",        "Tl-208",   "Th-232" },
                { "Pa-234m1 (U-238)",       "Pa-234m1", "U-238"  },
                { "Cs-137",                 "Cs-137",   ""       },
                { "  K-40  ",               "K-40",     ""       },
                // Скобки без содержимого — не ряд: пустая строка честнее, чем
                // цепочка с именем «».
                { "Ac-228 ()",              "Ac-228",   ""       },
                { "",                       "",         ""       },
            };

            int bad = 0;
            for (int i = 0; i < cases.GetLength(0); i++)
            {
                string name = cases[i, 0];
                bad += Same("нуклид из «" + name + "»", cases[i, 1], NuclideDefinition.NuclideNameOf(name));
                bad += Same("ряд из «" + name + "»", cases[i, 2], NuclideDefinition.ChainOf(name));
            }

            return bad;
        }

        static int CheckPersistence()
        {
            Console.WriteLine();
            Console.WriteLine("=== хранение ===");
            int bad = 0;

            NuclideDefinitionFile file = new NuclideDefinitionFile();
            file.NuclideDefinitions.Add(new NuclideDefinition
            {
                Name = "Bi-214 (Ra-226)", Energy = 609.32, Intencity = 45.49, Chain = "Ra-226"
            });
            // Цепочка НЕ обязана совпадать с хвостом подписи: аттестованный
            // источник Th-228 не содержит Ac-228, и его линии считаются рядом
            // Th-228, как бы ни была подписана линия. Поле главнее подписи.
            file.NuclideDefinitions.Add(new NuclideDefinition
            {
                Name = "Tl-208 (Th-232)", Energy = 2614.51, Intencity = 35.94, Chain = "Th-228"
            });
            file.NuclideDefinitions.Add(new NuclideDefinition
            {
                Name = "Cs-137", Energy = 661.657, Intencity = 85.1
            });

            NuclideDefinitionFile back = RoundTrip(file);
            bad += Same("цепочка после записи", "Ra-226", back.NuclideDefinitions[0].Chain);
            bad += Same("цепочка главнее подписи", "Th-228", back.NuclideDefinitions[1].Chain);
            bad += Same("одиночный нуклид без цепочки", "", back.NuclideDefinitions[2].Chain);

            // Файл, записанный ДО появления поля: элемента <Chain> в нём нет
            // вовсе. Так выглядят все конфиги пользователей.
            string legacy =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<NuclideDefinitionFile xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n" +
                "  <NuclideDefinitions>\r\n" +
                "    <Nuclide><Name>Bi-214 (Ra-226)</Name><Energy>609.32</Energy>" +
                "<Intencity>45.49</Intencity></Nuclide>\r\n" +
                "    <Nuclide><Name>Cs-137</Name><Energy>661.657</Energy>" +
                "<Intencity>85.1</Intencity></Nuclide>\r\n" +
                "  </NuclideDefinitions>\r\n" +
                "</NuclideDefinitionFile>\r\n";

            NuclideDefinitionFile old;
            using (StringReader reader = new StringReader(legacy))
            {
                old = (NuclideDefinitionFile)new XmlSerializer(typeof(NuclideDefinitionFile)).Deserialize(reader);
            }

            bad += Same("старый файл читается", 2, old.NuclideDefinitions.Count);
            bad += Same("до восстановления цепочки нет", "", old.NuclideDefinitions[0].Chain);

            Fill(old);
            bad += Same("цепочка восстановлена из подписи", "Ra-226", old.NuclideDefinitions[0].Chain);
            bad += Same("у одиночного так и осталась пустой", "", old.NuclideDefinitions[1].Chain);

            // Восстановление не должно трогать уже заполненное поле: иначе
            // подпись молча перебивала бы решение пользователя.
            old.NuclideDefinitions[1].Name = "Cs-137 (Ba-137m)";
            old.NuclideDefinitions[1].Chain = "";
            old.NuclideDefinitions[0].Chain = "Th-228";
            Fill(old);
            bad += Same("заполненное не переписывается", "Th-228", old.NuclideDefinitions[0].Chain);
            bad += Same("пустое дозаполняется", "Ba-137m", old.NuclideDefinitions[1].Chain);

            return bad;
        }

        /// <summary>
        /// Восстановление цепочек — приватная часть менеджера, и зовётся оно
        /// отражением нарочно: открытый путь к нему идёт через чтение файла
        /// конфигурации, а проба не должна зависеть от того, что в нём лежит.
        /// </summary>
        static void Fill(NuclideDefinitionFile file)
        {
            MethodInfo method = typeof(NuclideDefinitionManager).GetMethod(
                "FillChainsFromNames", BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new InvalidOperationException("нет NuclideDefinitionManager.FillChainsFromNames");
            }

            method.Invoke(null, new object[] { file });
        }

        static NuclideDefinitionFile RoundTrip(NuclideDefinitionFile file)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(NuclideDefinitionFile));
            StringBuilder text = new StringBuilder();
            using (StringWriter writer = new StringWriter(text))
            {
                serializer.Serialize(writer, file);
            }

            using (StringReader reader = new StringReader(text.ToString()))
            {
                return (NuclideDefinitionFile)serializer.Deserialize(reader);
            }
        }

        /// <summary>
        /// Оба места, где подпись разбиралась своими руками, обязаны давать
        /// ровно ответ общего разбора. Сверяется не текст кода, а ЧИСЛА: то же
        /// имя, поданное туда и сюда.
        /// </summary>
        static int CheckConsumers()
        {
            Console.WriteLine();
            Console.WriteLine("=== согласие потребителей ===");
            int bad = 0;

            MethodInfo token = typeof(FsaLibrary).GetMethod(
                "NuclideToken", BindingFlags.Static | BindingFlags.NonPublic);
            if (token == null)
            {
                Console.WriteLine("  !! нет FsaLibrary.NuclideToken");
                return 1;
            }

            string[] names = { "Bi-214 (Ra-226)", "Cs-137", "Pa-234m1 (U-238)", "Ac-228 ()", "" };
            foreach (string name in names)
            {
                bad += Same("библиотека образов на «" + name + "»",
                            NuclideDefinition.NuclideNameOf(name),
                            (string)token.Invoke(null, new object[] { name }));
            }

            // Конструктор кривой: имя нуклида в строке цепочки берётся у того
            // же разбора. Идёт через настоящий набор — иначе проверялась бы
            // копия кода, а не то, что выполняется.
            NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();
            Dictionary<string, List<EfficiencyLine>> chains = EfficiencyLibrary.BuildChains();
            int checkedLines = 0;
            foreach (KeyValuePair<string, List<EfficiencyLine>> chain in chains)
            {
                foreach (EfficiencyLine line in chain.Value)
                {
                    if (line.Nuclide.IndexOf('(') >= 0 || line.Nuclide.IndexOf(' ') >= 0)
                    {
                        Console.WriteLine("  !! в цепочке «{0}» имя нуклида не разобрано: «{1}»",
                                          chain.Key, line.Nuclide);
                        bad++;
                    }

                    checkedLines++;
                }
            }

            Console.WriteLine("  конструктор кривой: цепочек {0}, линий {1}", chains.Count, checkedLines);
            Console.WriteLine("  нуклидов в конфиге: {0}", manager.NuclideDefinitions.Count);
            return bad;
        }

        /// <summary>
        /// Ветвление от корня ряда. Числа сверяются с `chains.py` — НЕЗАВИСИМОЙ
        /// реализацией того же обхода, по которой собран корпус спектров. Две
        /// разные программы, сошедшиеся на 0.3594 у Tl-208, — это проверка, а
        /// повтор своей же формулы проверкой не был бы.
        /// </summary>
        static int CheckBranches()
        {
            Console.WriteLine();
            Console.WriteLine("=== ветвление от корня ===");
            int bad = 0;
            BecquerelMonitor.NucBase.NucBaseFramework fw = new BecquerelMonitor.NucBase.NucBaseFramework();

            Dictionary<string, double> th232 = fw.GetChainBranches("232TH");
            bad += Same("Th-232: членов ряда", 12, th232.Count);
            bad += Near("Th-232: сам корень", 1.0, Branch(th232, "232TH"));
            bad += Near("Th-232: Ac-228 весь", 1.0, Branch(th232, "228AC"));
            bad += Near("Th-232: Pb-212 весь", 1.0, Branch(th232, "212PB"));
            // Развилка Bi-212: 35.94 % на Tl-208, остальное на Po-212. Ровно
            // здесь врал бы обход по верхнему уровню — там у той же ветки 67 %.
            bad += Near("Th-232: Tl-208 через Bi-212", 0.3594, Branch(th232, "208TL"));
            bad += Near("Th-232: Po-212 вторая ветка", 0.6406, Branch(th232, "212PO"));

            Dictionary<string, double> th228 = fw.GetChainBranches("228TH");
            bad += Same("Th-228: членов ряда", 9, th228.Count);
            bad += Near("Th-228: Tl-208 та же развилка", 0.3594, Branch(th228, "208TL"));
            bad += Near("Th-228: Ac-228 в ряд не входит", 0.0, Branch(th228, "228AC"));

            Dictionary<string, double> ra226 = fw.GetChainBranches("226RA");
            bad += Near("Ra-226: Bi-214 весь", 1.0, Branch(ra226, "214BI"));
            bad += Near("Ra-226: Pb-214 почти весь", 0.9998, Branch(ra226, "214PB"));
            bad += Near("Ra-226: Tl-210 боковая ветка", 0.0002, Branch(ra226, "210TL"));

            // Выход линии на распад корня: 99.754 % своего нуклида через
            // ветвление 0.3594 дают 35.85 % — самая известная линия ториевого
            // ряда, и её число знают наизусть.
            double own = LineIntensity(fw, "208TL", 2614.51);
            bad += Near("Tl-208 2614.51: свой выход", 99.754, own);
            bad += Near("Tl-208 2614.51: на распад Th-232", 35.852, own * Branch(th232, "208TL"));

            return bad;
        }

        /// <summary>
        /// Поиск по ряду в самой форме базы: то, что видит пользователь и что
        /// уходит во ввоз. Считать отдельно и надеяться, что форма считает так
        /// же, — это ровно тот разрыв, из-за которого заводилось поле цепочки.
        /// </summary>
        static int CheckSearch()
        {
            Console.WriteLine();
            Console.WriteLine("=== поиск по ряду ===");
            int bad = 0;
            using (BecquerelMonitor.NucBase.NucBase form = new BecquerelMonitor.NucBase.NucBase())
            {
                Set(form, "IsotopeTextBox", "Th-232");
                ((System.Windows.Forms.CheckBox)Field(form, "IncludeDecayChainCheckBox")).Checked = true;
                Set(form, "IntencityTextBox", "1");
                Call(form, "DoSearch");

                System.Windows.Forms.DataGridView grid =
                    (System.Windows.Forms.DataGridView)Field(form, "ResultDataGridView");
                double tl2614 = Cell(grid, 2614.51);
                double ac911 = Cell(grid, 911.20);
                bad += Near("Tl-208 2614.51 в таблице", 35.852, tl2614);
                // Линия нуклида, который весь получается из корня, не должна
                // измениться ни на сколько: домножение на единицу.
                bad += Near("Ac-228 911.20 не тронута", 25.8, ac911);
                Console.WriteLine("  строк при пороге 1 %: {0}", grid.Rows.Count);

                // Тот же ряд без галочки — выходы свои, не рядовые.
                ((System.Windows.Forms.CheckBox)Field(form, "IncludeDecayChainCheckBox")).Checked = false;
                Set(form, "IsotopeTextBox", "Tl-208");
                Call(form, "DoSearch");
                bad += Near("без ряда: Tl-208 2614.51 свой выход", 99.754, Cell(grid, 2614.51));
            }

            return bad;
        }

        /// <summary>
        /// ЛОВУШКА K-СЕРИИ ВИДНА В РЕДАКТОРЕ (`D33`). Kβ лежит в
        /// `decay_radiations` дважды — итогом `KB` и разложением `KpB1`+`KpB2`,
        /// — и до 23.08.2026 обе записи стояли в таблице как равноправные
        /// линии: наивное сложение всех `K*` завышало K-выход Lu-176 до 40.53 %
        /// вместо 33.49 %, в 1.21 раза, и ничто об этом не говорило.
        ///
        /// Проверяется то, что видит человек: помечена ли лишняя половина и
        /// сходится ли сумма НЕПОМЕЧЕННЫХ строк с верным числом.
        /// </summary>
        static int CheckKSeriesEditor()
        {
            Console.WriteLine();
            Console.WriteLine("=== ловушка K-серии в редакторе ===");
            int bad = 0;
            BecquerelMonitor.NucBase.NucBaseFramework fw = new BecquerelMonitor.NucBase.NucBaseFramework();
            List<BecquerelMonitor.NucBase.DecayRad> lines = fw.getDecayRad("176LU");
            if (lines == null)
            {
                Console.WriteLine("  линий Lu-176 не пришло — сверять нечем");
                return 1;
            }

            double all = 0.0, kept = 0.0;
            int marked = 0;
            foreach (BecquerelMonitor.NucBase.DecayRad line in lines)
            {
                if (line.DecayLine != "X" || string.IsNullOrEmpty(line.XrayType)
                    || line.XrayType[0] != 'K')
                {
                    continue;
                }

                all += line.Intensity;
                if (line.Redundant) marked++; else kept += line.Intensity;
            }

            Console.WriteLine("  Lu-176: наивная сумма всех K-строк {0:F2} %, без помеченных {1:F2} %, помечено строк {2}",
                              all, kept, marked);
            bad += Near("Lu-176: наивная сумма (так было)", 40.53, all);
            bad += Near("Lu-176: сумма без помеченных", 33.49, kept);
            bad += Same("Lu-176: помеченных строк", 1, marked);

            // Обратный случай: у Th-227 разложение НЕПОЛНОЕ (одна `KpB1`), и
            // лишним оказывается оно, а не итог. Правило структурное, и здесь
            // это видно.
            List<BecquerelMonitor.NucBase.DecayRad> th = fw.getDecayRad("227TH");
            string markedAt227 = "";
            double kept227 = 0.0;
            if (th != null)
            {
                foreach (BecquerelMonitor.NucBase.DecayRad line in th)
                {
                    if (line.DecayLine != "X" || string.IsNullOrEmpty(line.XrayType)
                        || line.XrayType[0] != 'K')
                    {
                        continue;
                    }

                    if (line.Redundant) markedAt227 += (markedAt227.Length > 0 ? "+" : "") + line.XrayType;
                    else kept227 += line.Intensity;
                }
            }

            Console.WriteLine("  Th-227: помечено {0}, сумма без помеченных {1:F3} %",
                              markedAt227.Length > 0 ? markedAt227 : "ничего", kept227);
            bad += Same("Th-227: помечено разложение, не итог", "KpB1", markedAt227);
            bad += Near("Th-227: сумма без помеченных", 4.624, kept227);
            return bad;
        }

        /// <summary>
        /// ДВА ОБХОДА РЯДА ОБЯЗАНЫ СОЙТИСЬ (`S62`). Обходов в дереве три:
        /// `NucBase.NucBaseFramework.GetChainBranches` (форма базы и ввоз),
        /// `FullSpectrumAnalysis.FsaSampleLibrary.ChainBranches` (состав
        /// библиотеки разбора) и `tools/CORPUS/scripts/chains.py` (сборка
        /// корпуса). Сверять их было нечем, и они разошлись: до 23.08.2026
        /// второй раскрывал узел ОДИН РАЗ — значением, какое у того было в
        /// момент раскрытия, — и вклад, пришедший позже, детям не передавал.
        /// В радиевом ряду Pb-210 стоит в очереди раньше Po-214, который даёт
        /// ему почти всю долю: сам Pb-210 выходил верным, а 210BI, 210PO и
        /// 206PB получали 3e-5 вместо 1.0 и выпадали из библиотеки порогом
        /// 1e-3. Здесь появился читатель, которого у этого расхождения не было.
        /// </summary>
        static int CheckTwoWalks()
        {
            Console.WriteLine();
            Console.WriteLine("=== два обхода ряда: NucBase против FsaSampleLibrary ===");
            int bad = 0;
            BecquerelMonitor.NucBase.NucBaseFramework fw = new BecquerelMonitor.NucBase.NucBaseFramework();
            var report = new FsaSampleLibrary.Report();

            foreach (string root in new[] { "232TH", "228TH", "226RA", "238U", "235U" })
            {
                Dictionary<string, double> mine = FsaSampleLibrary.ChainBranches(root, report);
                Dictionary<string, double> theirs = fw.GetChainBranches(root);

                // Сверяются только члены выше порога, которым ряд живёт:
                // ниже 1e-3 обход и так никого не отдаёт (`MinChainBranch`).
                var names = new List<string>();
                foreach (KeyValuePair<string, double> row in mine)
                {
                    if (row.Value >= 1.0e-3 && !names.Contains(row.Key)) names.Add(row.Key);
                }

                foreach (KeyValuePair<string, double> row in theirs)
                {
                    if (row.Value >= 1.0e-3 && !names.Contains(row.Key)) names.Add(row.Key);
                }

                names.Sort(StringComparer.Ordinal);
                int off = 0;
                string worst = "";
                double worstGap = 0.0;
                foreach (string name in names)
                {
                    double a = Branch(mine, name), b = Branch(theirs, name);
                    double gap = Math.Abs(a - b);
                    if (gap > 1.0e-4)
                    {
                        off++;
                        if (gap > worstGap) { worstGap = gap; worst = name + " " + a.ToString("F5", CultureInfo.InvariantCulture) + " против " + b.ToString("F5", CultureInfo.InvariantCulture); }
                    }
                }

                bool ok = off == 0;
                Console.WriteLine("  {0,-6} членов выше 1e-3: {1,2}, расходится: {2}{3}   {4}",
                                  root, names.Count, off,
                                  off > 0 ? "  худший " + worst : "",
                                  ok ? "ok" : "⛔ РАСХОЖДЕНИЕ");
                if (!ok) bad++;
            }

            // Именной случай, на котором ошибка и нашлась: дети Pb-210 в
            // радиевом ряду стоят на равновесной единице, а не на 3e-5.
            Dictionary<string, double> ra = FsaSampleLibrary.ChainBranches("226RA", report);
            foreach (string member in new[] { "210PB", "210BI", "210PO", "206PB" })
            {
                bad += Near("Ra-226 → " + member, 1.0, Branch(ra, member));
            }

            return bad;
        }

        static double Branch(Dictionary<string, double> branches, string nucid)
        {
            double value;
            return branches.TryGetValue(nucid, out value) ? value : 0.0;
        }

        static double LineIntensity(BecquerelMonitor.NucBase.NucBaseFramework fw, string nucid, double energy)
        {
            List<BecquerelMonitor.NucBase.DecayRad> lines = fw.getDecayRad(nucid);
            if (lines == null)
            {
                return 0.0;
            }

            foreach (BecquerelMonitor.NucBase.DecayRad line in lines)
            {
                if (Math.Abs(line.Energy - energy) < 0.05)
                {
                    return line.Intensity;
                }
            }

            return 0.0;
        }

        static double Cell(System.Windows.Forms.DataGridView grid, double energy)
        {
            foreach (System.Windows.Forms.DataGridViewRow row in grid.Rows)
            {
                if (row.Cells[3].Value is double && Math.Abs((double)row.Cells[3].Value - energy) < 0.05)
                {
                    return (double)row.Cells[4].Value;
                }
            }

            return 0.0;
        }

        static void Set(object form, string name, string text)
        {
            ((System.Windows.Forms.Control)Field(form, name)).Text = text;
        }

        static int Near(string what, double expected, double got)
        {
            bool ok = Math.Abs(got - expected) <= 0.002 * Math.Max(1.0, Math.Abs(expected));
            Console.WriteLine("  {0,-44} {1} {2:G6}{3}", what, ok ? "=" : "!!", got,
                              ok ? "" : string.Format(" вместо {0:G6}", expected));
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Форма редактора: цепочка показывается в своём поле и уходит обратно
        /// в нуклид. Без единого клика — поля читаются и пишутся теми же
        /// методами, что зовёт сама форма.
        /// </summary>
        static int CheckForm()
        {
            Console.WriteLine();
            Console.WriteLine("=== форма редактора ===");
            int bad = 0;
            using (NuclideDefinitionForm form = new NuclideDefinitionForm())
            {
                System.Windows.Forms.TextBox box =
                    (System.Windows.Forms.TextBox)Field(form, "chainTextBox");
                NuclideDefinition nuclide = new NuclideDefinition
                {
                    Name = "Bi-214 (Ra-226)", Energy = 609.32, Intencity = 45.49, Chain = "Ra-226"
                };

                Call(form, "LoadFormContents", nuclide);
                bad += Same("цепочка показана", "Ra-226", box.Text);

                box.Text = "  Th-228  ";
                object saved = Call(form, "SaveFormContents", nuclide);
                bad += Same("сохранение прошло", true, saved);
                bad += Same("цепочка сохранена без пробелов", "Th-228", nuclide.Chain);

                box.Text = "";
                Call(form, "SaveFormContents", nuclide);
                // Очищенное поле означает «линия сама по себе», а не «разбери
                // подпись заново»: подпись у линии осталась прежней.
                bad += Same("очистка поля не откатывается к подписи", "", nuclide.Chain);
            }

            return bad;
        }

        static object Field(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
            {
                throw new InvalidOperationException("нет поля " + name);
            }

            return field.GetValue(target);
        }

        static object Call(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null)
            {
                throw new InvalidOperationException("нет метода " + name);
            }

            return method.Invoke(target, args);
        }

        static int Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0,-44} {1} {2}", what, ok ? "=" : "!!",
                              ok ? Show(got) : string.Format("{0} вместо {1}", Show(got), Show(expected)));
            return ok ? 0 : 1;
        }

        static string Show(object value)
        {
            string text = value == null ? "null" : value.ToString();
            return text.Length == 0 ? "«»" : text;
        }
    }
}
