using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
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
            int bad = 0;
            bad += CheckParsing();
            bad += CheckPersistence();
            bad += CheckConsumers();
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
