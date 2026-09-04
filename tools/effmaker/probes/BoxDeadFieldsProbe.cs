using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BoxDeadFieldsProbe
{
    /// <summary>
    /// `A94`: у БРУСКА полей `CrystalDiameter` и `CrystalHeight` не существует.
    ///
    /// Что здесь меряется и зачем. Эти два поля — размеры ЦИЛИНДРА. У бруска
    /// (`Shape == Box`) их не читает никто: в файл `.in` писатель кладёт
    /// ПРОИЗВОДНЫЙ цилиндр равной площади торца, посчитанный из
    /// `CrystalBoxX/Y/Z`, а сцену симулятор строит по `CrystalBoxInScene`.
    /// До 04.09.2026 поля при этом жили в модели: их можно было задать, они
    /// сохранялись — и не попадали ни в файл, ни в отпечаток, ни в сцену.
    /// Ровно об это сломался сторож ~~`A47`~~ и полгода выглядел как дыра в
    /// отпечатке: проба двигала `CrystalHeight` у бруска и удивлялась, что
    /// матрица осталась годной.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ОБЯЗАТЕЛЕН И ОН ЗДЕСЬ ЕСТЬ. Проверка «правка
    /// поля ничего не меняет» проходит и у сторожа, который не меряет ничего
    /// вовсе (сравнили пустоту с пустотой). Поэтому рядом с каждым таким
    /// пунктом стоит сдвиг РАБОЧЕГО размера — `CrystalBoxZ` у бруска,
    /// `CrystalHeight`/`CrystalDiameter` у цилиндра, — и он ОБЯЗАН изменить и
    /// текст геометрии, и отпечаток, и сцену. Не изменил — проба отказывает.
    ///
    /// Печатается машинный слепок (длина и sha256 текста геометрии, отпечаток,
    /// sha256 дампа сцены), по которому правка сверяется ПОБАЙТНО с прежней:
    /// снятие мёртвых полей не имеет права сдвинуть ни один посчитанный
    /// результат.
    ///
    ///     boxdeadfieldsprobe --box=&lt;брусок.in&gt; --cyl=&lt;цилиндр.in&gt;
    ///                        [--dir=&lt;каталог с .in&gt;]...
    ///
    /// `--dir=` печатает машинную опись ВСЕГО склада геометрий — по строке на
    /// файл, с длиной и sha256 текста, отпечатком и sha256 сцены. Опись
    /// снимается ДО правки и ПОСЛЕ, и сверяется построчно: одна пара файлов
    /// доказывает мало, а склад из десятков — то самое «ни один посчитанный
    /// результат не сдвинулся».
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0.
    /// </summary>
    static class Program
    {
        /// <summary>Разделитель строк дампа сцены (<c>DumpScene</c> клеит через него).</summary>
        const char Sep = '\n';

        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string boxPath = null, cylPath = null;
            var dirs = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--box=", StringComparison.Ordinal)) boxPath = a.Substring(6);
                else if (a.StartsWith("--cyl=", StringComparison.Ordinal)) cylPath = a.Substring(6);
                else if (a.StartsWith("--dir=", StringComparison.Ordinal)) dirs.Add(a.Substring(6));
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (boxPath == null || cylPath == null)
            {
                Console.Error.WriteLine("нужны --box=<файл .in> и --cyl=<файл .in>");
                return 2;
            }

            // Библиотека веществ читает matdb — без менеджера сцена не соберётся.
            GlobalConfigManager.GetInstance();

            foreach (string dir in dirs)
            {
                Inventory(dir);
            }

            GeometryModel box = GeometryModel.Load(boxPath);
            GeometryModel cyl = GeometryModel.Load(cylPath);

            Report(box.Shape == CrystalShape.Box, "{0}: форма {1}", Path.GetFileName(boxPath), box.Shape);
            Report(cyl.Shape == CrystalShape.Cylinder, "{0}: форма {1}", Path.GetFileName(cylPath), cyl.Shape);

            Slice("брусок", box);
            Slice("цилиндр", cyl);

            BoxChecks(boxPath, box);
            CylinderChecks(cyl);
            ConfigChecks(box);
            EditorChecks(box, cyl);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // Слепок: то, что обязано остаться побайтно тем же
        // ------------------------------------------------------------------

        /// <summary>
        /// Опись каталога: по строке на файл. Сравнивается ПОСТРОЧНО с описью,
        /// снятой до правки; расхождение хотя бы в одном знаке означает, что
        /// сдвинулся посчитанный результат.
        /// </summary>
        static void Inventory(string dir)
        {
            string[] files = Directory.GetFiles(dir, "*.in", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            Console.WriteLine();
            Console.WriteLine("=== ОПИСЬ {0}: {1} файлов ===", dir, files.Length);
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                try
                {
                    GeometryModel g = GeometryModel.Load(file);
                    string text = GeometryWriter.Render(g);
                    Console.WriteLine("{0,-44} {1,-8} len={2,-6} txt={3} stamp={4} scene={5}",
                                      name, g.Shape, text.Length, Sha(text).Substring(0, 16),
                                      Stamp(g).Substring(Stamp(g).IndexOf(';') + 1, 16),
                                      Sha(Scene(g)).Substring(0, 16));
                }
                catch (Exception e)
                {
                    Console.WriteLine("{0,-44} ОТКАЗ {1}: {2}", name, e.GetType().Name, e.Message);
                    bad++;
                }
            }
        }

        static void Slice(string title, GeometryModel g)
        {
            string text = GeometryWriter.Render(g);
            Console.WriteLine();
            Console.WriteLine("--- слепок «{0}» ---", title);
            Console.WriteLine("  поля цилиндра : D={0:R} H={1:R}", g.CrystalDiameter, g.CrystalHeight);
            Console.WriteLine("  поля бруска   : X={0:R} Y={1:R} Z={2:R}",
                              g.CrystalBoxX, g.CrystalBoxY, g.CrystalBoxZ);
            Console.WriteLine("  текст .in     : {0} знаков, sha256 {1}", text.Length, Sha(text));
            Console.WriteLine("  отпечаток     : {0}", Stamp(g));
            Console.WriteLine("  сцена         : sha256 {0}", Sha(Scene(g)));
            Console.WriteLine("  сцена, кристалл: {0}", CrystalRegion(g));
        }

        // ------------------------------------------------------------------
        // Брусок
        // ------------------------------------------------------------------

        static void BoxChecks(string path, GeometryModel box)
        {
            Console.WriteLine();
            Console.WriteLine("=== брусок: поля цилиндра сняты ===");

            // 1. Чтение файла их не оставляет. Файл при этом их СОДЕРЖИТ —
            //    писатель кладёт туда производный цилиндр, чтобы файл остался
            //    осмысленным для GMaster.
            Dictionary<string, double> raw = ReadCm(path);
            bool inFile = raw.ContainsKey("DS_CrystalDiameter") && raw.ContainsKey("DS_CrystalHeight");
            Report(inFile, "в файле ключи DS_CrystalDiameter/DS_CrystalHeight ЕСТЬ (старый формат)");

            Report(box.CrystalDiameter == 0.0 && box.CrystalHeight == 0.0,
                   "после чтения поля сняты: D={0:R} H={1:R} (ждём 0/0)",
                   box.CrystalDiameter, box.CrystalHeight);

            // 2. Запись их не берёт из модели, а ВЫВОДИТ из бруска.
            double dExpected = GeometryWriter.EquivalentDiameter(box.CrystalBoxX, box.CrystalBoxY);
            Report(Near(Value(raw, "DS_CrystalDiameter") * GeometryModel.MmPerCm, dExpected)
                   && Near(Value(raw, "DS_CrystalHeight") * GeometryModel.MmPerCm, box.CrystalBoxZ),
                   "в файле лежит ПРОИЗВОДНЫЙ цилиндр: D={0:F4} (ждём {1:F4}), H={2:F4} (ждём {3:F4}) мм",
                   Value(raw, "DS_CrystalDiameter") * GeometryModel.MmPerCm, dExpected,
                   Value(raw, "DS_CrystalHeight") * GeometryModel.MmPerCm, box.CrystalBoxZ);

            // 3. Мёртвое поле мертво: задать его можно, увидеть — негде.
            //    ⚠ Сам по себе этот пункт НИЧЕГО не доказывает (см. п. 4).
            string text0 = GeometryWriter.Render(box), stamp0 = Stamp(box), scene0 = Scene(box);
            GeometryModel spoiled = box.Clone();
            spoiled.CrystalDiameter = 999.0;
            spoiled.CrystalHeight = 777.0;
            Report(GeometryWriter.Render(spoiled) == text0
                   && Stamp(spoiled) == stamp0 && Scene(spoiled) == scene0,
                   "D=999, H=777 у бруска не меняют ни текст, ни отпечаток, ни сцену");

            // 4. ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. Рабочая длина бруска — Z, и её сдвиг
            //    ОБЯЗАН быть виден везде. Без этого пункта п. 3 проходил бы и у
            //    сторожа, сравнивающего пустоту с пустотой.
            GeometryModel moved = box.Clone();
            moved.CrystalBoxZ += 1.0;
            bool textMoved = GeometryWriter.Render(moved) != text0;
            bool stampMoved = Stamp(moved) != stamp0;
            bool sceneMoved = Scene(moved) != scene0;
            Report(textMoved && stampMoved && sceneMoved,
                   "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: CrystalBoxZ +1 мм виден в тексте({0}), отпечатке({1}), сцене({2})",
                   textMoved, stampMoved, sceneMoved);

            // 5. Обратная совместимость чтения: файл, в котором мёртвые ключи
            //    набраны ЧУЖИМИ числами, читается без отказа и даёт ТУ ЖЕ
            //    геометрию. Это и есть «не читаются» — проверенное числом, а не
            //    рассуждением.
            string tmp = Path.Combine(Path.GetTempPath(),
                                      "a94_legacy_" + Guid.NewGuid().ToString("N") + ".in");
            try
            {
                Rewrite(path, tmp);
                GeometryModel legacy = GeometryModel.Load(tmp);
                bool same = legacy.Shape == CrystalShape.Box
                            && GeometryWriter.Render(legacy) == text0
                            && Stamp(legacy) == stamp0
                            && Scene(legacy) == scene0;
                Report(same,
                       "старый файл с ЧУЖИМИ DS_CrystalDiameter=99/DS_CrystalHeight=88 см читается "
                       + "и даёт ту же геометрию (D={0:R} H={1:R})",
                       legacy.CrystalDiameter, legacy.CrystalHeight);
            }
            catch (Exception e)
            {
                Report(false, "старый файл ОТКАЗАЛ при чтении: {0}", e.Message);
            }
            finally
            {
                try { File.Delete(tmp); } catch (IOException) { }
            }
        }

        /// <summary>Тот же файл, но мёртвые ключи набраны заведомо чужими числами.</summary>
        static void Rewrite(string source, string target)
        {
            Encoding cp = Encoding.GetEncoding(1251);
            string[] lines = File.ReadAllLines(source, cp);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("DS_CrystalDiameter", StringComparison.Ordinal))
                {
                    lines[i] = "DS_CrystalDiameter = 99 cm";
                }
                else if (lines[i].StartsWith("DS_CrystalHeight", StringComparison.Ordinal))
                {
                    lines[i] = "DS_CrystalHeight = 88 cm";
                }
            }

            File.WriteAllLines(target, lines, cp);
        }

        // ------------------------------------------------------------------
        // Цилиндр — вторая ветвь, у него эти поля РАБОЧИЕ
        // ------------------------------------------------------------------

        static void CylinderChecks(GeometryModel cyl)
        {
            Console.WriteLine();
            Console.WriteLine("=== цилиндр: те же поля РАБОЧИЕ ===");

            Report(cyl.CrystalDiameter > 0.0 && cyl.CrystalHeight > 0.0,
                   "после чтения поля на месте: D={0:R} H={1:R}", cyl.CrystalDiameter, cyl.CrystalHeight);

            string text0 = GeometryWriter.Render(cyl), stamp0 = Stamp(cyl), scene0 = Scene(cyl);

            GeometryModel h = cyl.Clone();
            h.CrystalHeight += 1.0;
            Report(GeometryWriter.Render(h) != text0 && Stamp(h) != stamp0 && Scene(h) != scene0,
                   "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: CrystalHeight +1 мм виден в тексте, отпечатке и сцене");

            GeometryModel d = cyl.Clone();
            d.CrystalDiameter += 1.0;
            Report(GeometryWriter.Render(d) != text0 && Stamp(d) != stamp0 && Scene(d) != scene0,
                   "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: CrystalDiameter +1 мм виден в тексте, отпечатке и сцене");
        }

        // ------------------------------------------------------------------
        // Конфигурация: старый XML с записанными полями
        // ------------------------------------------------------------------

        static void ConfigChecks(GeometryModel box)
        {
            Console.WriteLine();
            Console.WriteLine("=== старый конфиг с записанными полями ===");

            var config = new EfficiencyConfigData("A94") { Guid = "a94", Geometry = box };
            var serializer = new XmlSerializer(typeof(EfficiencyConfigData));
            string xml;
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, config);
                xml = writer.ToString();
            }

            // Подделываем СТАРЫЙ конфиг: у бруска записаны оба мёртвых поля.
            string legacyXml = xml.Contains("<CrystalDiameter>")
                ? System.Text.RegularExpressions.Regex.Replace(
                      xml, @"<CrystalDiameter>[^<]*</CrystalDiameter>",
                      "<CrystalDiameter>333</CrystalDiameter>")
                : xml.Replace("<CrystalBoxX>", "<CrystalDiameter>333</CrystalDiameter><CrystalBoxX>");
            legacyXml = legacyXml.Contains("<CrystalHeight>")
                ? System.Text.RegularExpressions.Regex.Replace(
                      legacyXml, @"<CrystalHeight>[^<]*</CrystalHeight>",
                      "<CrystalHeight>444</CrystalHeight>")
                : legacyXml.Replace("<CrystalBoxX>", "<CrystalHeight>444</CrystalHeight><CrystalBoxX>");

            Report(legacyXml != xml, "подделка удалась: в XML стоят CrystalDiameter=333, CrystalHeight=444");

            try
            {
                EfficiencyConfigData back;
                using (var reader = new StringReader(legacyXml))
                {
                    back = (EfficiencyConfigData)serializer.Deserialize(reader);
                }

                bool same = back.Geometry != null
                            && back.Geometry.Shape == CrystalShape.Box
                            && GeometryWriter.Render(back.Geometry) == GeometryWriter.Render(box)
                            && Stamp(back.Geometry) == Stamp(box)
                            && Scene(back.Geometry) == Scene(box);
                Report(same, "старый конфиг читается и даёт ТУ ЖЕ геометрию (мёртвые числа не влияют)");
            }
            catch (Exception e)
            {
                Report(false, "старый конфиг ОТКАЗАЛ при чтении: {0}", e.Message);
            }
        }

        // ------------------------------------------------------------------
        // Редактор геометрии
        // ------------------------------------------------------------------

        /// <summary>
        /// Редактор — вторая граница записи. Проверяется, что он не заносит в
        /// брусок размеры цилиндра и СНИМАЕТ их у модели, где они уже лежали
        /// (это и есть старый конфиг, прошедший через редактор).
        ///
        /// ⚠ `TryCommit` не зовётся нарочно: на ошибке он показывает
        /// `MessageBox`, и безоконная проба повисла бы без человека у экрана.
        /// Собирается модель тем же закрытым `BuildModel`, что и при сохранении.
        /// </summary>
        static void EditorChecks(GeometryModel box, GeometryModel cyl)
        {
            Console.WriteLine();
            Console.WriteLine("=== редактор геометрии ===");

            // Сверяемся с КЛОНОМ: `BuildModel` работает на клоне. После `A139`
            // `Clone` переносит и `Raw` — разбор файла, из которого `Render`
            // берёт чужие блоки (коаксиал `DC_*`, вещества ЛСРМ), — поэтому
            // совпадение текста клона с исходником здесь уже не «повезло на
            // наших файлах», а ТРЕБОВАНИЕ: разошлись — значит `Clone` снова
            // потерял разбор, и всё, что ниже, меряет не то.
            GeometryModel boxRef = box.Clone();
            string boxText = GeometryWriter.Render(boxRef);
            string boxScene = SceneShape(boxRef);
            Report(GeometryWriter.Render(box) == boxText,
                   "клон и исходник дают ОДИН текст (A139: Clone переносит Raw)");

            using (var panel = new GeometryEditorPanel())
            {
                panel.CreateControl();

                // Модель СО СТАРЫМИ числами в мёртвых полях — ровно то, что
                // приезжает из конфигурации, записанной до 04.09.2026.
                GeometryModel stale = box.Clone();
                stale.CrystalDiameter = 333.0;
                stale.CrystalHeight = 444.0;
                panel.SetModel(stale);

                GeometryModel built = Build(panel);
                Report(built.Shape == CrystalShape.Box
                       && built.CrystalDiameter == 0.0 && built.CrystalHeight == 0.0,
                       "брусок через редактор: мёртвые 333/444 сняты (стало D={0:R} H={1:R})",
                       built.CrystalDiameter, built.CrystalHeight);
                // ⛔ ИСКЛЮЧЕНИЕ КЛЮЧЕЙ `SB_*` СНЯТО (`T148`, 04.09.2026). Оно
                // стояло на подстановке: редактор открывал прямоугольную кювету
                // размерами ЦИЛИНДРИЧЕСКОГО сосуда, и после сборки модели ключи
                // `SB_*` в файле оказывались заполненными — у геометрии, где
                // источник не прямоугольный. `A134` подстановку в МОДЕЛЬ убрал
                // (подсказка осталась только на экране), и расхождения больше
                // нет: измерено на этой же пробе до снятия — «разошлись: нет».
                // Поэтому требуется совпадение ВСЕГО текста: любой сдвиг —
                // кристалла, сосуда или кюветы — теперь валит пункт.
                string builtText = GeometryWriter.Render(built);
                List<string> drift = Drift(boxText, builtText);
                Report(drift.Count == 0,
                       "брусок через редактор: не сдвинулось НИЧЕГО, ключи кюветы SB_* тоже "
                       + "(разошлись: {0})", drift.Count == 0 ? "нет" : string.Join(", ", drift.ToArray()));
                Report(SceneShape(built) == boxScene, "брусок через редактор: СЦЕНА та же{0}",
                       Diff(boxScene, SceneShape(built)));

                // ⚠ `Control.Visible` врёт при невыбранной вкладке — он
                // ЭФФЕКТИВНЫЙ и гаснет вместе с родителем. Спрашивается СВОЙ
                // флаг (`Control.GetState(STATE_VISIBLE)`), а рядом стоит
                // положительный контроль: он ОБЯЗАН перевернуться при смене
                // формы, иначе проверка мерит пустоту.
                var boxPanel = (Control)Field(panel, "boxSizePanel");
                var cylPanel = (Control)Field(panel, "cylinderSizePanel");
                bool boxShown = OwnVisible(boxPanel), cylShown = OwnVisible(cylPanel);
                Report(boxShown && !cylShown,
                       "у бруска строк «диаметр/высота» на экране нет (своя видимость: брусок {0}, цилиндр {1})",
                       boxShown, cylShown);

                var cylRadio = (RadioButton)Field(panel, "cylinderRadio");
                cylRadio.Checked = true;
                bool boxShown2 = OwnVisible(boxPanel), cylShown2 = OwnVisible(cylPanel);
                Report(!boxShown2 && cylShown2,
                       "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: переключение на цилиндр переворачивает панели ({0}/{1})",
                       boxShown2, cylShown2);

                // Переключились на цилиндр — поля стали ВХОДНЫМИ, и в них
                // стоит производный цилиндр, а не нули: иначе форма отказала бы
                // человеку в собственной геометрии.
                GeometryModel asCylinder = Build(panel);
                double d = GeometryWriter.EquivalentDiameter(box.CrystalBoxX, box.CrystalBoxY);
                Report(asCylinder.Shape == CrystalShape.Cylinder
                       && Near(asCylinder.CrystalDiameter, d)
                       && Near(asCylinder.CrystalHeight, box.CrystalBoxZ),
                       "смена формы на цилиндр даёт РАВНОЦЕННЫЙ цилиндр D={0:F4} H={1:F4} (ждём {2:F4}/{3:F4})",
                       asCylinder.CrystalDiameter, asCylinder.CrystalHeight, d, box.CrystalBoxZ);
            }

            using (var panel = new GeometryEditorPanel())
            {
                panel.CreateControl();
                panel.SetModel(cyl.Clone());
                GeometryModel built = Build(panel);
                GeometryModel cylRef = cyl.Clone();
                string cylText = GeometryWriter.Render(cylRef);
                // `T148`: исключение `SB_*` снято и здесь — после `A134`
                // редактор не подставляет кювете размеры сосуда, и у ЦИЛИНДРА
                // ключи `SB_*` обязаны остаться такими же, как были в файле.
                List<string> drift = Drift(cylText, GeometryWriter.Render(built));
                Report(built.Shape == CrystalShape.Cylinder
                       && Near(built.CrystalDiameter, cyl.CrystalDiameter)
                       && Near(built.CrystalHeight, cyl.CrystalHeight)
                       && drift.Count == 0
                       && SceneShape(built) == SceneShape(cylRef),
                       "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: у ЦИЛИНДРА редактор те же поля СОХРАНЯЕТ "
                       + "(D={0:R} H={1:R}), сцена та же, разошлись: {2}",
                       built.CrystalDiameter, built.CrystalHeight,
                       drift.Count == 0 ? "нет" : string.Join(", ", drift.ToArray()));
            }
        }

        static GeometryModel Build(GeometryEditorPanel panel)
        {
            System.Reflection.MethodInfo m = typeof(GeometryEditorPanel).GetMethod(
                "BuildModel", System.Reflection.BindingFlags.Instance
                              | System.Reflection.BindingFlags.NonPublic);
            if (m == null)
            {
                throw new MissingMethodException("GeometryEditorPanel.BuildModel");
            }

            return (GeometryModel)m.Invoke(panel, null);
        }

        static object Field(object target, string name)
        {
            System.Reflection.FieldInfo f = target.GetType().GetField(
                name, System.Reflection.BindingFlags.Instance
                      | System.Reflection.BindingFlags.NonPublic);
            if (f == null)
            {
                throw new MissingFieldException(target.GetType().Name + "." + name);
            }

            return f.GetValue(target);
        }

        /// <summary>СВОЯ видимость контрола, без влияния родителей (STATE_VISIBLE = 2).</summary>
        static bool OwnVisible(Control c)
        {
            System.Reflection.MethodInfo m = typeof(Control).GetMethod(
                "GetState", System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.NonPublic);
            if (m == null)
            {
                throw new MissingMethodException("Control.GetState");
            }

            return (bool)m.Invoke(c, new object[] { 2 });
        }

        // ------------------------------------------------------------------
        // Мелочи
        // ------------------------------------------------------------------

        static string Stamp(GeometryModel g)
        {
            // Настройки фиксированные: меряется геометрия, а не они.
            var options = new ResponseMatrixOptions { NodeCount = 10, Histories = 4000, BinKev = 4.0 };
            return ResponseMatrix.ComputeStamp(g, options);
        }

        static string Scene(GeometryModel g)
        {
            return new EfficiencySimulator(g).DumpScene();
        }

        /// <summary>
        /// Сцена без строк ВЕЩЕСТВА — только области и источник.
        ///
        /// Зачем отдельно. Редактор берёт вещество из библиотеки по имени, а
        /// файл хранит доли округлёнными (0.488451 против 0.48845137728951588)
        /// и в другом порядке. Это не про `A94` и не про геометрию: тела сцены
        /// и её источник от этого не двигаются, а сравнение всего дампа
        /// расходилось бы на составе и прятало бы настоящий сдвиг.
        /// </summary>
        static string SceneShape(GeometryModel g)
        {
            var kept = new List<string>();
            foreach (string line in Scene(g).Split(Sep))
            {
                if (line.StartsWith("region", StringComparison.Ordinal)
                    || line.StartsWith("source", StringComparison.Ordinal))
                {
                    kept.Add(line);
                }
            }

            return string.Join(new string(Sep, 1), kept.ToArray());
        }

        static string CrystalRegion(GeometryModel g)
        {
            foreach (string line in Scene(g).Split('\n'))
            {
                if (line.StartsWith("region", StringComparison.Ordinal)
                    && line.EndsWith("crystal", StringComparison.Ordinal))
                {
                    return line;
                }
            }

            return "(кристалла в сцене нет)";
        }

        static string Sha(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return hex.ToString();
            }
        }

        /// <summary>Ключи строк, разошедшихся между двумя текстами `.in`.</summary>
        static List<string> Drift(string expected, string actual)
        {
            var keys = new List<string>();
            string[] a = expected.Split('\n'), b = actual.Split('\n');
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string x = i < a.Length ? a[i].TrimEnd('\r') : "";
                string y = i < b.Length ? b[i].TrimEnd('\r') : "";
                if (x == y)
                {
                    continue;
                }

                int eq = x.IndexOf('=');
                string key = eq > 0 ? x.Substring(0, eq).Trim() : "строка " + (i + 1);
                keys.Add(key.Length > 0 ? key : "строка " + (i + 1));
            }

            return keys;
        }

        /// <summary>Первая разошедшаяся строка — иначе «не совпало» ничего не говорит.</summary>
        static string Diff(string expected, string actual)
        {
            if (expected == actual)
            {
                return "";
            }

            string[] a = expected.Split('\n'), b = actual.Split('\n');
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string x = i < a.Length ? a[i].TrimEnd('\r') : "(нет строки)";
                string y = i < b.Length ? b[i].TrimEnd('\r') : "(нет строки)";
                if (x != y)
                {
                    return string.Format(CultureInfo.InvariantCulture,
                        "; строка {0}: «{1}» -> «{2}»", i + 1, x, y);
                }
            }

            return "; строки те же, длина разная";
        }

        static bool Near(double a, double b)
        {
            return Math.Abs(a - b) <= 1e-6 * Math.Max(1.0, Math.Abs(b));
        }

        static Dictionary<string, double> ReadCm(string path)
        {
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(path, Encoding.GetEncoding(1251)))
            {
                int comment = raw.IndexOf("//", StringComparison.Ordinal);
                string line = comment >= 0 ? raw.Substring(0, comment) : raw;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                var m = System.Text.RegularExpressions.Regex.Match(
                    line.Substring(eq + 1), @"^\s*(-?[0-9.]+(?:[eE][-+]?[0-9]+)?)");
                double value;
                if (key.Length > 0 && m.Success
                    && double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out value))
                {
                    map[key] = value;
                }
            }

            return map;
        }

        static double Value(Dictionary<string, double> map, string key)
        {
            double v;
            return map.TryGetValue(key, out v) ? v : double.NaN;
        }

        static void Report(bool ok, string format, params object[] args)
        {
            Console.WriteLine("[{0}] {1}", ok ? "  ok  " : "ПРОВАЛ",
                              string.Format(CultureInfo.InvariantCulture, format, args));
            if (!ok)
            {
                bad++;
            }
        }
    }
}
