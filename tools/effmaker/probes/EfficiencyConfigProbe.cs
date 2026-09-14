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
///
/// ⛔ ТРЕТЬЕГО ДОВОДА БОЛЬШЕ НЕТ (`T155`, 05.09.2026). Он назывался «пустой
/// рабочий каталог» и обещал управлять местом записи на диск, а после `S102`
/// место записи задаёт каталог СБОРКИ (`Package.DeviceDir`), и довод остался
/// со смыслом «диск проверять» — имя обещало не то, что делалось. Живых
/// читателей у него не нашлось (по дереву: ни один `.ps1`/`.py` пробу не
/// зовёт; в `README.md` и в журнале 05.09.2026 он стоит как пример). Поэтому
/// довод снят, а проверка диска идёт ВСЕГДА — она и есть то звено, ради
/// которого заведена `T153`. Лишний довод — отказ словами, а не молчаливое
/// поедание: прежняя команда с тремя доводами обязана упасть, иначе никто не
/// узнает, что «каталог» перестал что-либо значить.
/// </summary>
static class EfficiencyConfigProbe
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length != 2)
        {
            Console.WriteLine("effcfgprobe <модель.in> <кривая.txt>");
            if (args.Length > 2)
            {
                // `T155`: третий довод прежде звался «рабочий каталог», а местом
                // записи не управлял. Съесть его молча значило бы оставить
                // вызывающему веру, что он что-то задал.
                Console.WriteLine("!! доводов {0}, а нужно 2: третий («{1}») снят — местом записи управляет каталог "
                                  + "сборки (Package.DeviceDir), а не он (T155, S102)",
                                  args.Length, args[2]);
            }

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

        // Третий — весь путь целиком, до файла на диске и обратно. Всегда
        // (`T155`): куда писать, решает сборка, спрашивать нечего.
        bad += CheckDisk(config);

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
    /// Каталог снаружи НЕ задаётся (`T155`): `Package` в отвязанной сборке
    /// считает конфиг от каталога СБОРКИ, и запись идёт рядом с exe пробы —
    /// в каталог проб. Что проба там завела, она за собой убирает.
    /// </summary>
    static int CheckDisk(EfficiencyConfigData template)
    {
        // ⛔ КУДА ПИШЕТ ПРИЛОЖЕНИЕ, РЕШАЕТ КАТАЛОГ СБОРКИ, А НЕ ТЕКУЩИЙ (`T153`).
        //
        // Проба строила путь сама — `<workdir>\config\device\effprobe.xml` — и
        // переводила туда текущий каталог. Пока портативные пути `Package`
        // были ОТНОСИТЕЛЬНЫМИ, это работало. Правка `S102` (27.08.2026) увела
        // их на `AppDomain.CurrentDomain.BaseDirectory` — каталог, где лежит
        // exe, — потому что текущий каталог приложению меняет любой диалог
        // открытия файла. С тех пор `SetCurrentDirectory` на место записи не
        // влияет ВОВСЕ: менеджер пишет рядом с exe, а проба искала файл в
        // `workdir` и честно отказывала «файла нет». Измерено 04.09.2026:
        // `LoadAllConfigFiles` даже не дошёл до записи — свалился на
        // отсутствии `<каталог проб>\config\device`, назвав его вслух.
        //
        // Поэтому каталог спрашивается У ПРИЛОЖЕНИЯ, тем же `Package`, каким
        // пользуется менеджер: второй копии этого правила в пробе быть не
        // должно — она и разошлась. Довода «рабочий каталог» больше нет
        // (`T155`): он этим местом не управлял.
        string device = Package.GetInstance().DeviceDir;
        // ⛔ ЗАВЕДЁННЫЙ КАТАЛОГ — ТОЖЕ СЛЕД, И ЕГО ТОЖЕ УБИРАЕМ (`T155`, найдено
        // 05.09.2026). `config\device`, оставленный пробой в каталоге проб, —
        // ровно та «пустая заготовка в чужом каталоге», из-за которой заведены
        // `S100` и `A90`: у `DeviceConfigManager` без окон отказ поднимается
        // только на ОТСУТСТВУЮЩЕМ каталоге, а пустой существующий он читает
        // молча, — и одна прогонка этой пробы меняла исход всех последующих
        // (`RefusalWordsProbe --arm=fsa` в том же каталоге переставал падать).
        // Мерено: `build_b1_bare\config\device` от 04.09.2026 22:47 — пустой и
        // никем не убранный; завести его без окон могла только эта проба —
        // единственный `CreateDirectory(config\device)` в дереве, кроме её, у
        // `DeviceConfigManager.cs:149`, и тот стоит под `HasWindows`. Убираем,
        // если завели сами и он остался пуст; чужой каталог с чужими
        // конфигурациями не трогаем.
        bool madeDir = !Directory.Exists(device);
        Directory.CreateDirectory(device);
        Console.WriteLine("диск: каталог записи (Package.DeviceDir) {0}{1}", device,
                          madeDir ? " — заведён пробой, будет убран" : " — был");

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

        // ⚠ ЗА СОБОЙ УБИРАЕМ (`T153`). Запись идёт в каталог СБОРКИ, то есть в
        // каталог проб, и оставленный `effprobe.xml` попал бы в
        // `LoadAllConfigFiles` всех следующих прогонов — конфигурация прибора,
        // которой нет ни в одном списке источников. Не удалось убрать —
        // говорим вслух, а не молчим.
        try
        {
            File.Delete(path);
        }
        catch (Exception e)
        {
            Console.WriteLine("!! диск: не удалось убрать {0}: {1}", path, e.Message);
            bad++;
        }

        if (madeDir)
        {
            try
            {
                // Только пустой: появись там чужой файл за время прогона,
                // сносить его не наше дело.
                if (Directory.GetFileSystemEntries(device).Length == 0)
                {
                    Directory.Delete(device);
                    Console.WriteLine("диск: заведённый каталог {0} убран", device);
                }
                else
                {
                    Console.WriteLine("!! диск: заведённый каталог {0} не пуст — оставлен", device);
                    bad++;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("!! диск: не удалось убрать каталог {0}: {1}", device, e.Message);
                bad++;
            }
        }

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

            // ⛔ РАЗБОР ФАЙЛА (`Raw`) СВЕРЯЕТСЯ (`T152`, 04.09.2026). Прежде он
            // из сверки исключался, и это было верно, пока `Raw` не хранился в
            // конфигурации. После `A139` он хранится (`GeometryModel.RawList`,
            // `[XmlArray("Raw")]`) — и если потеряется при записи-чтении, то
            // писатель перестанет находить чужие блоки файла, текст `.in`
            // изменится, а с ним и `ComputeStamp`: человек получит «матрица
            // устарела» и часы пересчёта при той же геометрии. Именно это
            // исключение и делало пропажу невидимой.
            //
            // ⚠ `Equals` на `Dictionary` — сравнение ССЫЛОК, и на разных
            // объектах оно ложно ВСЕГДА. Поэтому пары сверяются поимённо.
            if (f.Name == "Raw")
            {
                checkedFields++;
                bad += CompareRaw(a, b);
                continue;
            }

            // `Warnings` — список замечаний РАЗБОРА, он заполняется при чтении
            // `.in` и в конфигурацию не пишется. Сверять его нечем и незачем.
            if (f.Name == "Warnings")
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

    /// <summary>
    /// Разбор файла `.in` (`GeometryModel.Raw`): пары ключ-значение, из которых
    /// писатель берёт чужие блоки — коаксиал `DC_*`, вещества ЛСРМ. `T152`.
    ///
    /// ⛔ СВЕРЯЕТСЯ НЕ ВЕСЬ РАЗБОР, А ЕГО ПЕРЕНОСИМАЯ ДОЛЯ, и это не поблажка.
    /// `GeometryModel.RawList` (форма записи `Raw` для XML) хранит ровно
    /// `GeometryWriter.CarriedFrom` — то, что писатель ЧИТАЕТ обратно; остальное
    /// это те же числа, что уже лежат в полях модели, и хранить их значило бы
    /// раздувать конфигурацию вдвое (мерено при `A139` на `Nano16Pro.in`: 191
    /// ключ против 55, XML 18960 знаков против 11000). Требовать все 191 —
    /// значит требовать от кода того, чего он не обещал: измерено 04.09.2026,
    /// первая редакция этой сверки дала 288 «расхождений», и все до одного были
    /// НЕпереносимыми ключами.
    ///
    /// Потеря же переносимого ключа — настоящая беда, ради которой сверка и
    /// заводится: писатель перестанет находить чужой блок, текст `.in`
    /// изменится, а с ним и `ComputeStamp` — «матрица устарела» на пустом месте.
    ///
    /// ⚠ ЛИШНИЕ ключи в копии расхождением НЕ считаются. Два пути хранения
    /// доносят разный объём — измерено 04.09.2026 на `Nano16Pro_Marinelli.in`:
    /// круговорот `XmlSerializer` в памяти доносит все 191 пару, а запись
    /// менеджером на диск и чтение обратно — 48 переносимых. Вреда от лишнего
    /// нет: писатель читает только своё. Поэтому проверяется ПРОПАЖА и ПОДМЕНА,
    /// а рядом стоит прямая мера смысла — совпадение самого текста `.in`.
    /// </summary>
    static int CompareRaw(GeometryModel a, GeometryModel b)
    {
        if (a.Raw == null || b.Raw == null)
        {
            Console.WriteLine("!! Raw: разбор равен null (было {0}, стало {1})",
                              a.Raw == null ? "null" : a.Raw.Count.ToString(),
                              b.Raw == null ? "null" : b.Raw.Count.ToString());
            return 1;
        }

        // Ожидаемое берётся у САМОГО писателя, а не переписывается сюда
        // списком: второй список «что переносится» разошёлся бы молча.
        Dictionary<string, string> carried = GeometryWriter.CarriedFrom(a);

        int bad = 0;
        foreach (KeyValuePair<string, string> pair in carried)
        {
            string got;
            if (!b.Raw.TryGetValue(pair.Key, out got))
            {
                Console.WriteLine("!! Raw: ПЕРЕНОСИМЫЙ ключ {0} ПРОПАЛ (было «{1}»)", pair.Key, pair.Value);
                bad++;
            }
            else if (got != pair.Value)
            {
                Console.WriteLine("!! Raw: {0}: «{1}» -> «{2}»", pair.Key, pair.Value, got);
                bad++;
            }
        }

        // ⛔ ПРЯМАЯ МЕРА: ради чего разбор хранится, то и спрашивается — текст
        // `.in`, отрисованный писателем, обязан совпасть до знака. Ключи могут
        // храниться как угодно; сойтись обязан результат.
        string ta = GeometryWriter.Render(a), tb = GeometryWriter.Render(b);
        if (ta != tb)
        {
            Console.WriteLine("!! текст .in после круговорота РАЗОШЁЛСЯ: {0} знаков -> {1}", ta.Length, tb.Length);
            bad++;
        }

        Console.WriteLine("Raw: разбор {0} пар, переносимых {1}, доехало {2}; текст .in {3}",
                          a.Raw.Count, carried.Count, b.Raw.Count, ta == tb ? "ТОТ ЖЕ" : "РАЗОШЁЛСЯ");
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
