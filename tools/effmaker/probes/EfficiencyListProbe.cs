using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace EfficiencyListProbe
{
    /// <summary>
    /// Список кривых эффективности в панели измерения —
    /// <see cref="DCControlPanel.BuildEfficiencyItems"/>.
    ///
    /// Проверяются два порока, найденные пользователем на экране:
    ///
    ///  1. Кривой, только что созданной в конфигурации прибора, в списке не
    ///     было. Список брался у копии конфигурации, лежащей в спектре, а
    ///     сохранение прибора заменяет объект в менеджере целиком — копия
    ///     оставалась прежней. Здесь это выражено проще: составитель списка
    ///     обязан показать РОВНО то, что лежит в переданной ему конфигурации
    ///     прибора, а искать живую — забота вызывающего (CurrentDeviceConfig).
    ///
    ///  2. Родную кривую спектра в списке было не отличить: при совпадении Guid
    ///     показывалась строка ПРИБОРА с его именем, а стоило переключиться на
    ///     другую — родная исчезала совсем, и вернуться к ней было нечем.
    ///
    ///  3. Список кривых спрашивает не только панель измерения: диалог «нет
    ///     кривой эффективности» перед разложением (FSA) составлял свой,
    ///     который не знал ни родной кривой спектра, ни живой конфигурации
    ///     прибора — у спектра с кривой ИЗ ФАЙЛА и прибором без кривых выбирать
    ///     было не из чего вовсе. Оба места теперь зовут один составитель и
    ///     один <see cref="DCControlPanel.EfficiencyFromItem"/>; здесь
    ///     проверяется и он: способ присвоения (та же ссылка против копии) и
    ///     есть вся разница между «родной» и «кривой прибора».
    ///
    ///   efflistprobe
    /// </summary>
    static class Program
    {
        static int failed;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            LiveDeviceConfigWins();
            SameGuidStillDistinct();
            SwitchingAwayKeepsOwn();
            DeviceCurveShownWhenSpectrumHasNone();
            OwnCurveShownWhenDeviceHasNone();
            ForeignCurveSurvives();
            NoCurveAtAll();
            ChosenItemBecomesEfficiency();
            PanelHearsTheDocument();

            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + failed);
            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// Кривая, созданная в конфигурации прибора, в списке не появлялась.
        /// Причина не в составителе списка, а в том, ЧЬЮ конфигурацию ему
        /// давали: сохранение прибора кладёт в менеджер КЛОН, а спектр держит
        /// ссылку на прежний объект, где новой кривой нет. Здесь это ровно и
        /// воспроизведено: у спектра старая копия, в менеджере — новая с
        /// кривой, и выбрать надо ту, что в менеджере.
        /// </summary>
        static void LiveDeviceConfigWins()
        {
            Console.WriteLine("=== Конфигурация прибора берётся живая");
            DeviceConfigInfo stale = Device();
            stale.Guid = "dev-1";

            DeviceConfigInfo live = Device(Curve("aaaa", "Цилиндр1"));
            live.Guid = "dev-1";

            DeviceConfigInfo other = Device(Curve("bbbb", "чужая"));
            other.Guid = "dev-2";

            DeviceConfigInfo got = DCControlPanel.CurrentDeviceConfig(
                stale, new List<DeviceConfigInfo> { other, live });
            Same("кривых в найденной конфигурации", got.EfficiencyConfigs.Count, 1);
            if (!object.ReferenceEquals(got, live))
            {
                Fail("взята не та конфигурация прибора — новая кривая в список не попадёт");
            }
            else
            {
                Console.WriteLine("    ок: взята конфигурация из менеджера, а не копия спектра");
            }

            // Прибора, которого у нас нет вовсе (чужой файл спектра), терять
            // нельзя: его копия — единственное, по чему в файле есть кривые.
            DeviceConfigInfo unknown = Device(Curve("cccc", "из чужого файла"));
            unknown.Guid = "dev-9";
            DeviceConfigInfo kept = DCControlPanel.CurrentDeviceConfig(
                unknown, new List<DeviceConfigInfo> { live });
            if (!object.ReferenceEquals(kept, unknown))
            {
                Fail("конфигурация чужого файла потеряна");
            }
            else
            {
                Console.WriteLine("    ок: незнакомый прибор остался при своей копии");
            }
        }

        /// <summary>
        /// Тот случай, на котором это и поймали: кривую у прибора переименовали
        /// и пересчитали, Guid остался прежний. В списке обязаны быть ОБЕ.
        /// </summary>
        static void SameGuidStillDistinct()
        {
            Console.WriteLine();
            Console.WriteLine("=== Совпал Guid — строки всё равно разные");
            EfficiencyConfigData own = Curve("7c7ed64b", "Цилиндр");
            EfficiencyConfigData device = Curve("7c7ed64b", "Цилиндр1");
            int selected;
            List<object> items = DCControlPanel.BuildEfficiencyItems(
                own, own, Device(device, Curve("aaaa", "Точка")), out selected);

            Same("строк в списке", items.Count, 4);
            IsOwn("вторая строка — родная", items[1], "Цилиндр");
            IsDevice("третья строка — прибора", items[2], "Цилиндр1");
            Same("выбрана родная", selected, 1);
        }

        /// <summary>
        /// Выбрали кривую прибора — родная остаётся в списке. Иначе отказаться
        /// от правки было бы нечем: в файле-то лежит она.
        /// </summary>
        static void SwitchingAwayKeepsOwn()
        {
            Console.WriteLine();
            Console.WriteLine("=== Выбрана чужая — родная не пропала");
            EfficiencyConfigData own = Curve("7c7ed64b", "Цилиндр");
            EfficiencyConfigData device = Curve("aaaa", "Точка");
            // Панель кладёт в спектр КОПИЮ кривой прибора, а не её саму.
            int selected;
            List<object> items = DCControlPanel.BuildEfficiencyItems(
                device.Copy(), own, Device(device), out selected);

            Same("строк в списке", items.Count, 3);
            IsOwn("родная на месте", items[1], "Цилиндр");
            Same("выбрана кривая прибора", selected, 2);
        }

        /// <summary>Спектр без своей кривой: список — это кривые прибора.</summary>
        static void DeviceCurveShownWhenSpectrumHasNone()
        {
            Console.WriteLine();
            Console.WriteLine("=== У спектра кривой нет");
            EfficiencyConfigData a = Curve("aaaa", "Цилиндр1");
            EfficiencyConfigData b = Curve("bbbb", "Точка");
            int selected;
            List<object> items = DCControlPanel.BuildEfficiencyItems(
                null, null, Device(a, b), out selected);

            Same("строк в списке", items.Count, 3);
            IsDevice("первая кривая прибора", items[1], "Цилиндр1");
            IsDevice("вторая кривая прибора", items[2], "Точка");
            Same("выбрано «нет»", selected, 0);
        }

        /// <summary>
        /// Обратный случай, и на нём поймали диалог разложения: кривая есть
        /// только В ФАЙЛЕ спектра, у прибора её нет вовсе. Список обязан
        /// показать родную — иначе выбирать не из чего, а кривая-то есть.
        ///
        /// Спектр при этом БЕЗ выбранной кривой (current == null): именно так и
        /// приходят в диалог, он для того и открывается.
        /// </summary>
        static void OwnCurveShownWhenDeviceHasNone()
        {
            Console.WriteLine();
            Console.WriteLine("=== Кривая только в файле, у прибора ни одной");
            EfficiencyConfigData own = Curve("7c7ed64b", "Точка");
            int selected;
            List<object> items = DCControlPanel.BuildEfficiencyItems(
                null, own, Device(), out selected);

            Same("строк в списке", items.Count, 2);
            IsOwn("родная кривая предложена", items[1], "Точка");
            Same("выбрано «нет»", selected, 0);
        }

        /// <summary>
        /// Кривая, которой нет ни в файле, ни у прибора, — по ней всё равно
        /// сейчас считается активность, и молча выбросить её нельзя.
        /// </summary>
        static void ForeignCurveSurvives()
        {
            Console.WriteLine();
            Console.WriteLine("=== Кривая ниоткуда");
            EfficiencyConfigData stray = Curve("cccc", "чужая");
            int selected;
            List<object> items = DCControlPanel.BuildEfficiencyItems(
                stray, null, Device(Curve("aaaa", "Точка")), out selected);

            Same("строк в списке", items.Count, 3);
            IsDevice("последней стоит чужая", items[2], "чужая");
            Same("она и выбрана", selected, 2);
        }

        static void NoCurveAtAll()
        {
            Console.WriteLine();
            Console.WriteLine("=== Ни кривых, ни прибора");
            int selected;
            List<object> items = DCControlPanel.BuildEfficiencyItems(null, null, null, out selected);
            Same("строк в списке", items.Count, 1);
            Same("выбрано «нет»", selected, 0);
        }

        /// <summary>
        /// Что ложится в спектр по выбранной строке. Проверяется способ
        /// присвоения, а не значения: родная кривая — ТА ЖЕ ссылка (по ней и
        /// узнают «выбрана родная»), кривая прибора — копия (правка её у
        /// прибора не должна задним числом менять посчитанную активность).
        /// Строки списка берутся у самого составителя — так проверяется вся
        /// связка, а не выдуманные объекты.
        /// </summary>
        static void ChosenItemBecomesEfficiency()
        {
            Console.WriteLine();
            Console.WriteLine("=== Выбранная строка — в спектр");
            EfficiencyConfigData own = Curve("7c7ed64b", "Точка");
            EfficiencyConfigData device = Curve("aaaa", "Цилиндр1");
            int selected;
            List<object> items = DCControlPanel.BuildEfficiencyItems(
                null, own, Device(device), out selected);

            if (DCControlPanel.EfficiencyFromItem(items[0]) != null)
            {
                Fail("строка «нет кривой» дала кривую");
            }
            else
            {
                Console.WriteLine("    ок: «нет кривой» — кривой нет");
            }

            EfficiencyConfigData got = DCControlPanel.EfficiencyFromItem(items[1]);
            if (!object.ReferenceEquals(got, own))
            {
                Fail("родная кривая пришла копией — признак «выбрана родная» потерян,"
                     + " спектр будет помечен изменённым на пустом месте");
            }
            else
            {
                Console.WriteLine("    ок: родная кривая — тем же объектом");
            }

            got = DCControlPanel.EfficiencyFromItem(items[2]);
            if (got == null || object.ReferenceEquals(got, device))
            {
                Fail("кривая прибора пришла ссылкой — правка у прибора меняла бы"
                     + " активность уже измеренного спектра");
            }
            else if (got.Guid != device.Guid)
            {
                Fail("кривая прибора пришла не той: " + got.Guid);
            }
            else
            {
                Console.WriteLine("    ок: кривая прибора — копией");
            }
        }

        /// <summary>
        /// У сигнала есть потребитель. Кривую можно сменить и из документа —
        /// диалогом перед разложением, — а панель управления измерением
        /// обновляет ряд «Efficiency» только по <c>ShowDocumentStatus</c>, на
        /// своих же событиях. Поэтому документ говорит событием
        /// <c>EfficiencyChanged</c>, а `MainForm` обязан на него подписаться и
        /// отписаться. Заведённое событие, на которое никто не подписан, —
        /// ошибка того же вида, что и здесь чинилась: на экране «(none)» при
        /// работающей кривой, и ни компилятор, ни глаз кода этого не видят.
        ///
        /// Проверка грубая: в теле <c>SubscribeDocumentEvent</c> ищется вызов
        /// <c>add_EfficiencyChanged</c>, в <c>UnsubscribeDocumentEvent</c> —
        /// <c>remove_</c>. Собрать `MainForm` без окна нельзя, а больше о
        /// подписке спросить негде.
        /// </summary>
        static void PanelHearsTheDocument()
        {
            Console.WriteLine();
            Console.WriteLine("=== Событие смены кривой слышит панель");
            if (typeof(DocEnergySpectrum).GetEvent("EfficiencyChanged") == null)
            {
                Fail("события EfficiencyChanged у документа нет");
                return;
            }

            Console.WriteLine("    ок: событие у документа есть");
            Type main = typeof(DocEnergySpectrum).Assembly.GetType("BecquerelMonitor.MainForm");
            Calls(main, "SubscribeDocumentEvent", "add_EfficiencyChanged");
            Calls(main, "UnsubscribeDocumentEvent", "remove_EfficiencyChanged");
        }

        /// <summary>
        /// Зовёт ли метод <paramref name="callee"/>. Тело читается как байты, и
        /// токены разбираются подряд — разметку IL проба не строит: ложное
        /// совпадение должно было бы разрешиться в метод РОВНО с этим именем,
        /// чего случайным числом не бывает.
        /// </summary>
        static void Calls(Type type, string method, string callee)
        {
            MethodInfo info = type == null ? null : type.GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (info == null)
            {
                Fail("метода " + method + " в MainForm нет");
                return;
            }

            byte[] il = info.GetMethodBody().GetILAsByteArray();
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F)
                {
                    continue;   // не call и не callvirt
                }

                try
                {
                    MethodBase target = type.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
                    if (target != null && target.Name == callee)
                    {
                        Console.WriteLine("    ок: {0} зовёт {1}", method, callee);
                        return;
                    }
                }
                catch (Exception)
                {
                    // Не всякая четвёрка байт — токен: смещение угадано неверно.
                }
            }

            Fail(method + " не зовёт " + callee + " — сигнал заведён, читателя нет");
        }

        // --------------------------------------------------------------

        static EfficiencyConfigData Curve(string guid, string name)
        {
            EfficiencyConfigData config = new EfficiencyConfigData(name);
            config.Guid = guid;
            return config;
        }

        static DeviceConfigInfo Device(params EfficiencyConfigData[] curves)
        {
            DeviceConfigInfo device = new DeviceConfigInfo();
            device.EfficiencyConfigs.Clear();
            foreach (EfficiencyConfigData curve in curves)
            {
                device.EfficiencyConfigs.Add(curve);
            }

            return device;
        }

        static void IsOwn(string caption, object item, string name)
        {
            DCControlPanel.SpectrumEfficiencyItem own = item as DCControlPanel.SpectrumEfficiencyItem;
            if (own == null)
            {
                Fail(caption + ": строка не помечена как родная");
                return;
            }

            if (own.Config.Name != name)
            {
                Fail(caption + ": имя «" + own.Config.Name + "», ожидалось «" + name + "»");
                return;
            }

            // Подпись обязана отличаться от голого имени — иначе пометки нет.
            string text = own.ToString();
            if (text == name || text.IndexOf(name, StringComparison.Ordinal) < 0)
            {
                Fail(caption + ": подпись «" + text + "» не называет кривую с пометкой");
                return;
            }

            Console.WriteLine("    ок: {0} — «{1}»", caption, text);
        }

        static void IsDevice(string caption, object item, string name)
        {
            EfficiencyConfigData config = item as EfficiencyConfigData;
            if (config == null)
            {
                Fail(caption + ": строка не кривая прибора");
            }
            else if (config.Name != name)
            {
                Fail(caption + ": имя «" + config.Name + "», ожидалось «" + name + "»");
            }
            else
            {
                Console.WriteLine("    ок: {0} — «{1}»", caption, config.Name);
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
