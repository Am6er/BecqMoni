using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

/// <summary>
/// Переживает ли конфигурация эффективности запись в конфиг устройства и чтение
/// обратно.
///
/// Проверять это глазами нельзя, а компилятор молчит: `XmlSerializer` не умеет
/// `Dictionary` и `readonly`, и обе ловушки сидели в геометрии — состав вещества
/// (Z -> массовая доля) и разобранные пары ключ-значение файла `.in`. Пропажа
/// состава не роняет ничего: кривая просто считается по веществу с нулевым
/// ослаблением, то есть выдаёт правдоподобные и неверные числа.
///
/// Сверяется не текст, а ВСЕ ПОЛЯ по отражению — тогда поле, добавленное в
/// геометрию завтра, проверится само, без правки пробы.
///
/// Сборка (после сборки основного проекта):
///   csc /target:exe /langversion:7.3 /out:&lt;wd&gt;\effcfgprobe.exe ^
///       /r:&lt;wd&gt;\BecquerelMonitor.exe /r:System.dll /r:System.Core.dll ^
///       /r:System.Xml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
///       tools\effmaker\probes\EfficiencyConfigProbe.cs
///
///   effcfgprobe &lt;модель.in&gt; &lt;кривая.txt&gt;
///
/// Ожидание: «ВСЕ СОШЛИСЬ».
/// </summary>
static class EfficiencyConfigProbe
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 2)
        {
            Console.WriteLine("effcfgprobe <модель.in> <кривая.txt> [пустой рабочий каталог]");
            return 2;
        }

        DeviceConfigInfo device = new DeviceConfigInfo();
        device.Guid = System.Guid.NewGuid().ToString();
        device.Name = "проба";

        EfficiencyConfigData config = new EfficiencyConfigData("маринелли 0.5")
        {
            Origin = EfficiencyOrigin.Lsrm,
            Geometry = GeometryModel.Load(args[0]),
            Curve = ReadCurve(args[1]),
        };

        device.EfficiencyConfigs.Add(config);
        device.ActiveEfficiencyGuid = config.Guid;

        Console.WriteLine("исходно: точек {0}, веществ с составом {1}",
                          config.Curve.Count, WithFractions(config.Geometry));

        XmlSerializer serializer = new XmlSerializer(typeof(DeviceConfigInfo));
        string xml;
        using (StringWriter writer = new StringWriter(CultureInfo.InvariantCulture))
        {
            serializer.Serialize(writer, device);
            xml = writer.ToString();
        }

        DeviceConfigInfo back;
        using (StringReader reader = new StringReader(xml))
        {
            back = (DeviceConfigInfo)serializer.Deserialize(reader);
        }

        Console.WriteLine("XML: {0} знаков", xml.Length);

        int bad = 0;
        if (back.EfficiencyConfigs.Count != 1)
        {
            Console.WriteLine("!! конфигураций после чтения: {0}", back.EfficiencyConfigs.Count);
            return 1;
        }

        EfficiencyConfigData r = back.EfficiencyConfigs[0];
        bad += Same("Guid", config.Guid, r.Guid);
        bad += Same("Name", config.Name, r.Name);
        bad += Same("Origin", config.Origin, r.Origin);
        bad += Same("ActiveEfficiencyGuid", device.ActiveEfficiencyGuid, back.ActiveEfficiencyGuid);
        bad += Same("ActiveEfficiency найдена", true, back.ActiveEfficiency != null);

        // Кривая
        bad += Same("точек кривой", config.Curve.Count, r.Curve.Count);
        int curveBad = 0;
        for (int i = 0; i < Math.Min(config.Curve.Count, r.Curve.Count); i++)
        {
            if (config.Curve[i].Energy != r.Curve[i].Energy
                || config.Curve[i].Efficiency != r.Curve[i].Efficiency
                || config.Curve[i].ErrorPercent != r.Curve[i].ErrorPercent)
            {
                curveBad++;
            }
        }

        bad += Same("точек разошлось", 0, curveBad);

        // Геометрия — все поля по отражению
        bad += CompareGeometry(config.Geometry, r.Geometry);

        // Второй путь, на котором конфигурация может пропасть, — копия.
        bad += CheckClone(device);

        // Третий — весь путь целиком, до файла на диске и обратно.
        if (args.Length >= 3)
        {
            bad += CheckDisk(args[2], config);
        }
        else
        {
            Console.WriteLine("(диск не проверялся: рабочий каталог не задан)");
        }

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("НЕ СОШЛОСЬ: {0}", bad));
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// Весь путь сохранения целиком, как его проходит форма: конфигурация
    /// прибора берётся из менеджера, правится в КОПИИ (форма работает с ней),
    /// уходит в `SaveConfig` — и читается обратно с диска тем же
    /// `XmlSerializer`, каким её читает запуск программы.
    ///
    /// Каждое звено по отдельности выглядит целым, а теряется кривая между
    /// ними: `SaveConfig` тоже копирует конфигурацию перед записью, так что
    /// пропущенное в конструкторе копирования поле не доезжает до файла, даже
    /// если в памяти оно было.
    ///
    /// Каталог задаётся снаружи и должен быть ОТДЕЛЬНЫМ: `Package` в отвязанной
    /// сборке считает конфиг от текущего каталога, и проба, запущенная не там,
    /// писала бы в чужие настройки.
    /// </summary>
    static int CheckDisk(string workdir, EfficiencyConfigData template)
    {
        string device = Path.Combine(workdir, "config", "device");
        Directory.CreateDirectory(device);
        Directory.SetCurrentDirectory(workdir);

        DeviceConfigManager manager = DeviceConfigManager.GetInstance();
        manager.LoadAllConfigFiles();
        DeviceConfigInfo created = manager.CreateConfig("effprobe.xml");
        if (created == null)
        {
            Console.WriteLine("!! диск: конфигурацию прибора завести не удалось");
            return 1;
        }

        // Ровно то, что делает форма: правится не объект менеджера, а копия.
        DeviceConfigInfo edited = created.Clone();
        EfficiencyConfigData added = template.Duplicate("маринелли 0.5");
        edited.EfficiencyConfigs.Add(added);
        edited.ActiveEfficiencyGuid = added.Guid;

        if (!manager.SaveConfig(edited))
        {
            Console.WriteLine("!! диск: SaveConfig отказался сохранять");
            return 1;
        }

        string path = Path.Combine(device, "effprobe.xml");
        if (!File.Exists(path))
        {
            Console.WriteLine("!! диск: файла {0} нет", path);
            return 1;
        }

        DeviceConfigInfo fromDisk;
        XmlSerializer serializer = new XmlSerializer(typeof(DeviceConfigInfo));
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
        {
            fromDisk = (DeviceConfigInfo)serializer.Deserialize(stream);
        }

        Console.WriteLine("диск: {0}, {1} знаков", path, new FileInfo(path).Length);

        int bad = 0;
        bad += Same("диск: конфигураций", 1,
                    fromDisk.EfficiencyConfigs == null ? -1 : fromDisk.EfficiencyConfigs.Count);
        bad += Same("диск: ActiveEfficiencyGuid", added.Guid, fromDisk.ActiveEfficiencyGuid);
        if (bad != 0)
        {
            return bad;
        }

        EfficiencyConfigData back = fromDisk.EfficiencyConfigs[0];
        bad += Same("диск: Name", added.Name, back.Name);
        bad += Same("диск: Origin", added.Origin, back.Origin);
        bad += Same("диск: точек кривой", added.Curve.Count, back.Curve.Count);
        bad += Same("диск: ActiveEfficiency найдена", true, fromDisk.ActiveEfficiency != null);
        bad += CompareGeometry(added.Geometry, back.Geometry);
        return bad;
    }

    /// <summary>
    /// Форма конфигураций работает не с объектом менеджера, а с его КОПИЕЙ:
    /// `DeviceConfigForm.ListupConfigFiles` кладёт в строку таблицы
    /// `deviceConfigInfo.Clone()`, и вкладка «Эффективность» правит именно её.
    /// Копия делается конструктором копирования, который перечисляет поля
    /// ПОИМЁННО, — новое поле в него само не попадает, и тогда список кривых
    /// пуст уже при открытии формы, а сохранение уносит пустой список на диск.
    ///
    /// Копия обязана быть ГЛУБОКОЙ, как и всё остальное там: общий список
    /// означал бы, что правка кривой переживает «Отмена».
    /// </summary>
    static int CheckClone(DeviceConfigInfo device)
    {
        DeviceConfigInfo copy = device.Clone();
        int bad = 0;
        bad += Same("копия: конфигураций", device.EfficiencyConfigs.Count,
                    copy.EfficiencyConfigs == null ? -1 : copy.EfficiencyConfigs.Count);
        bad += Same("копия: ActiveEfficiencyGuid", device.ActiveEfficiencyGuid, copy.ActiveEfficiencyGuid);
        if (bad != 0 || copy.EfficiencyConfigs.Count == 0)
        {
            return bad;
        }

        EfficiencyConfigData a = device.EfficiencyConfigs[0], b = copy.EfficiencyConfigs[0];
        bad += Same("копия: Guid", a.Guid, b.Guid);
        bad += Same("копия: точек кривой", a.Curve.Count, b.Curve.Count);
        bad += Same("копия: ActiveEfficiency найдена", true, copy.ActiveEfficiency != null);
        if (ReferenceEquals(a, b))
        {
            Console.WriteLine("!! копия: конфигурация та же самая, правка переживёт «Отмена»");
            bad++;
        }

        if (a.Geometry != null && ReferenceEquals(a.Geometry, b.Geometry))
        {
            Console.WriteLine("!! копия: геометрия та же самая, правка переживёт «Отмена»");
            bad++;
        }

        bad += CompareGeometry(a.Geometry, b.Geometry);
        return bad;
    }

    static int CompareGeometry(GeometryModel a, GeometryModel b)
    {
        if (b == null)
        {
            Console.WriteLine("!! геометрия после чтения пуста");
            return 1;
        }

        int bad = 0;
        int checkedFields = 0;
        foreach (FieldInfo f in typeof(GeometryModel).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.FieldType == typeof(GeometryMaterial))
            {
                bad += CompareMaterial(f.Name, (GeometryMaterial)f.GetValue(a), (GeometryMaterial)f.GetValue(b));
                continue;
            }

            // Разбор файла не хранится нарочно — см. GeometryModel.Raw.
            if (f.Name == "Raw" || f.Name == "Warnings")
            {
                continue;
            }

            checkedFields++;
            object va = f.GetValue(a), vb = f.GetValue(b);
            if (!Equals(va, vb))
            {
                Console.WriteLine("!! {0}: {1} -> {2}", f.Name, va, vb);
                bad++;
            }
        }

        Console.WriteLine("полей геометрии сверено: {0}", checkedFields);
        return bad;
    }

    static int CompareMaterial(string where, GeometryMaterial a, GeometryMaterial b)
    {
        if (a == null && b == null)
        {
            return 0;
        }

        if (a == null || b == null)
        {
            Console.WriteLine("!! {0}: одно из веществ пусто", where);
            return 1;
        }

        int bad = 0;
        if (a.Name != b.Name || a.Density != b.Density)
        {
            Console.WriteLine("!! {0}: {1}/{2} -> {3}/{4}", where, a.Name, a.Density, b.Name, b.Density);
            bad++;
        }

        if (a.Fractions.Count != b.Fractions.Count)
        {
            Console.WriteLine("!! {0}: элементов {1} -> {2}", where, a.Fractions.Count, b.Fractions.Count);
            return bad + 1;
        }

        foreach (KeyValuePair<int, double> pair in a.Fractions)
        {
            double got;
            if (!b.Fractions.TryGetValue(pair.Key, out got) || got != pair.Value)
            {
                Console.WriteLine("!! {0}: Z={1} доля {2} -> {3}", where, pair.Key, pair.Value, got);
                bad++;
            }
        }

        return bad;
    }

    static int WithFractions(GeometryModel g)
    {
        int n = 0;
        foreach (FieldInfo f in typeof(GeometryModel).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            GeometryMaterial m = f.GetValue(g) as GeometryMaterial;
            if (m != null && m.Fractions.Count > 0)
            {
                n++;
            }
        }

        return n;
    }

    static int Same(string what, object expected, object got)
    {
        if (Equals(expected, got))
        {
            return 0;
        }

        Console.WriteLine("!! {0}: {1} -> {2}", what, expected, got);
        return 1;
    }

    /// <summary>
    /// Экспорт кривой LSRM: заголовок, дальше «энергия, эффективность,
    /// погрешность %» через табуляции. Здесь разобран на месте — проба про
    /// хранение, а не про импорт.
    /// </summary>
    static List<ROIEfficiencyData> ReadCurve(string path)
    {
        List<ROIEfficiencyData> curve = new List<ROIEfficiencyData>();
        bool first = true;
        foreach (string line in File.ReadAllLines(path))
        {
            if (first)
            {
                first = false;
                continue;
            }

            List<string> parts = new List<string>();
            foreach (string p in line.Split('\t'))
            {
                if (p.Trim().Length > 0)
                {
                    parts.Add(p.Trim());
                }
            }

            double e, eff, err;
            if (parts.Count >= 3
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out e)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out eff)
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out err))
            {
                curve.Add(new ROIEfficiencyData { Energy = e, Efficiency = eff, ErrorPercent = err });
            }
        }

        return curve;
    }
}
