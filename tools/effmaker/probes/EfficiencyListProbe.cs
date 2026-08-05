using BecquerelMonitor;
using System;
using System.Collections.Generic;
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
            ForeignCurveSurvives();
            NoCurveAtAll();

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
