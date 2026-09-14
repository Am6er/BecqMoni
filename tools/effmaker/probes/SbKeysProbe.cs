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

namespace SbKeysProbe
{
    /// <summary>
    /// `A134`: ключи ПРЯМОУГОЛЬНОЙ кюветы `SB_*` у геометрии, где источник не
    /// прямоугольный.
    ///
    /// Что меряется и зачем. Карта полей редактора (`GeometryEditorPanel`)
    /// читает поля кюветы с ПОДСКАЗКОЙ: пока в модели их нет, поле открывается
    /// размером ЦИЛИНДРИЧЕСКОГО сосуда, чтобы человек не увидел пустых строк.
    /// Подсказка правильная, а вот запись до 04.09.2026 шла БЕЗ РАЗБОРА ФОРМЫ:
    /// `BuildModel` проходит по всей карте, и у цилиндрической (точечной,
    /// маринелли) геометрии подсказка оседала в модели как настоящие числа.
    /// Дальше `GeometryWriter` пишет `SB_*` всегда — значит текст `.in`
    /// менялся, а вместе с ним и `ResponseMatrix.ComputeStamp`, который берёт
    /// именно этот текст. Человек открыл геометрию, ничего не правил, сохранил
    /// — и получил «матрица устарела» и часы пересчёта при той же сцене.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ОБЯЗАТЕЛЕН. «Открытие-сохранение ничего не
    /// меняет» проходит и у пробы, которая не меряет ничего вовсе. Поэтому
    /// рядом с каждым таким пунктом стоит сдвиг РАБОЧЕГО размера, который
    /// ОБЯЗАН быть виден: у прямоугольной кюветы — `BoxSourceX`, у
    /// цилиндрической — `BeakerDiameter`.
    ///
    ///     sbkeysprobe [--dir=&lt;каталог с .in&gt;]...     машинная опись склада
    ///                 [--editor=&lt;каталог с .in&gt;]...  открытие-сохранение
    ///                 [--cyl=&lt;цилиндр.in&gt;] [--box=&lt;кювета.in&gt;]
    ///
    /// `--dir=` печатает опись (длина и sha256 текста, отпечаток, sha256 сцены)
    /// — по ней правка сверяется ПОБАЙТНО с прежней сборкой.
    /// `--editor=` гоняет каждый файл через редактор и печатает, какие ключи
    /// разошлись и сдвинулся ли отпечаток.
    ///
    /// Ожидание после правки: «ВСЕ СОШЛИСЬ», код 0.
    /// </summary>
    static class Program
    {
        const char Sep = '\n';

        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var dirs = new List<string>();
            var editorDirs = new List<string>();
            string cylPath = null, boxPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) dirs.Add(a.Substring(6));
                else if (a.StartsWith("--editor=", StringComparison.Ordinal)) editorDirs.Add(a.Substring(9));
                else if (a.StartsWith("--cyl=", StringComparison.Ordinal)) cylPath = a.Substring(6);
                else if (a.StartsWith("--box=", StringComparison.Ordinal)) boxPath = a.Substring(6);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            // Библиотека веществ читает matdb — без менеджера сцена не соберётся.
            GlobalConfigManager.GetInstance();

            foreach (string dir in dirs)
            {
                Inventory(dir);
            }

            foreach (string dir in editorDirs)
            {
                EditorRoundTrip(dir);
            }

            if (cylPath != null)
            {
                CylinderChecks(cylPath);
            }

            if (boxPath != null)
            {
                BoxChecks(boxPath);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // Опись: то, что обязано остаться побайтно тем же
        // ------------------------------------------------------------------

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
                    string stamp = Stamp(g);
                    Console.WriteLine("{0,-44} {1,-9} {2,-9} len={3,-6} txt={4} stamp={5} scene={6}",
                                      name, g.Shape, g.SourceType, text.Length,
                                      Sha(text).Substring(0, 16),
                                      stamp.Substring(stamp.IndexOf(';') + 1, 16),
                                      Sha(Scene(g)).Substring(0, 16));
                }
                catch (Exception e)
                {
                    Console.WriteLine("{0,-44} ОТКАЗ {1}: {2}", name, e.GetType().Name, e.Message);
                    bad++;
                }
            }
        }

        // ------------------------------------------------------------------
        // Главное: открытие-сохранение не имеет права двигать отпечаток
        // ------------------------------------------------------------------

        /// <summary>
        /// Каждый файл каталога прогоняется через редактор: загрузить в панель
        /// и собрать модель обратно тем же закрытым `BuildModel`, каким её
        /// собирает «Сохранить».
        ///
        /// ⚠ `TryCommit` не зовётся нарочно: на ошибке он показывает
        /// `MessageBox`, и безоконная проба повисла бы без человека у экрана.
        /// </summary>
        static void EditorRoundTrip(string dir)
        {
            string[] files = Directory.GetFiles(dir, "*.in", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            Console.WriteLine();
            Console.WriteLine("=== ОТКРЫТЬ-СОХРАНИТЬ {0}: {1} файлов ===", dir, files.Length);

            int sbMoved = 0, foreignMoved = 0, stampMovedClean = 0, stampMovedForeign = 0;
            int sceneMoved = 0, sceneUlp = 0;
            using (var panel = new GeometryEditorPanel())
            {
                panel.CreateControl();
                foreach (string file in files)
                {
                    string name = Path.GetFileName(file);
                    GeometryModel g = GeometryModel.Load(file);
                    string text0 = GeometryWriter.Render(g);
                    string stamp0 = Stamp(g);
                    string scene0 = SceneShape(g);

                    panel.SetModel(g);
                    GeometryModel built = Build(panel);

                    string text1 = GeometryWriter.Render(built);
                    string stamp1 = Stamp(built);
                    string scene1 = SceneShape(built);

                    List<string> drift = Drift(text0, text1);
                    var sb = new List<string>();
                    var other = new List<string>();
                    foreach (string k in drift)
                    {
                        if (k.StartsWith("SB_", StringComparison.Ordinal)) sb.Add(k);
                        else other.Add(k);
                    }

                    if (sb.Count > 0) sbMoved++;
                    if (other.Count > 0) foreignMoved++;
                    if (stamp0 != stamp1)
                    {
                        // Сдвиг отпечатка, объяснимый ЧУЖИМ расхождением (блок
                        // коаксиала и состав вещества — они теряются на `Clone`
                        // и на подстановке вещества из библиотеки, дефект давний
                        // и к `A134` отношения не имеет), считается отдельно.
                        if (other.Count > 0) stampMovedForeign++;
                        else stampMovedClean++;
                    }

                    if (!SameScene(scene0, scene1)) sceneMoved++;
                    else if (scene0 != scene1) sceneUlp++;

                    Console.WriteLine("{0,-44} {1,-9} отпечаток {2}  SB_: {3}  прочее: {4}",
                                      name, g.SourceType,
                                      stamp0 == stamp1 ? "ТОТ ЖЕ " : "СДВИНУТ",
                                      sb.Count == 0 ? "-" : string.Join(",", sb.ToArray()),
                                      other.Count == 0 ? "-" : Head(other));
                }
            }

            Console.WriteLine("--- итог {0}: файлов {1}; со сдвигом SB_ {2}; отпечаток сдвинут: "
                              + "по СВОИМ ключам {3}, по чужим (коаксиал/вещество) {4}; "
                              + "сцен сдвинуто {5}, из них последним битом {6}",
                              dir, files.Length, sbMoved, stampMovedClean, stampMovedForeign,
                              sceneMoved + sceneUlp, sceneUlp);
            Report(sbMoved == 0, "{0}: ключи SB_* не сдвинулись НИ У ОДНОГО файла (сдвинулось {1})",
                   dir, sbMoved);
            Report(stampMovedClean == 0,
                   "⛔ {0}: отпечаток после открытия-сохранения ТОТ ЖЕ везде, где текст сошёлся "
                   + "по своим ключам (сдвинулось {1})", dir, stampMovedClean);
            Report(sceneMoved == 0, "{0}: сцена та же У ВСЕХ, кроме последнего бита "
                   + "(сдвинулось по-настоящему {1}, последним битом {2})",
                   dir, sceneMoved, sceneUlp);
        }

        static string Head(List<string> keys)
        {
            if (keys.Count <= 4)
            {
                return string.Join(",", keys.ToArray());
            }

            return string.Join(",", keys.GetRange(0, 4).ToArray())
                   + " …и ещё " + (keys.Count - 4);
        }

        // ------------------------------------------------------------------
        // Цилиндрическая геометрия: чужих ключей не появляется
        // ------------------------------------------------------------------

        static readonly string[] SbKeys =
        {
            "SB_BoxToDetectorFrontDistance", "SB_SourceX", "SB_SourceY",
            "SB_BoxSideWallThickness", "SB_BoxEndWallThickness", "SB_SourceHeight",
        };

        static void CylinderChecks(string path)
        {
            Console.WriteLine();
            Console.WriteLine("=== {0}: источник ЦИЛИНДР ===", Path.GetFileName(path));

            GeometryModel g = GeometryModel.Load(path);
            Report(g.SourceType == GeometrySourceType.Cylinder,
                   "источник {0} (ждём Cylinder)", g.SourceType);

            Console.WriteLine("  сосуд: D={0:R} H={1:R} стенка={2:R}/{3:R} проба={4:R} зазор={5:R} мм",
                              g.BeakerDiameter, g.BeakerHeight, g.BeakerSideWallThickness,
                              g.BeakerEndWallThickness, g.SourceHeight, g.BeakerToDetectorDistance);
            Console.WriteLine("  кювета в модели: X={0:R} Y={1:R} H={2:R} стенка={3:R}/{4:R} зазор={5:R} мм",
                              g.BoxSourceX, g.BoxSourceY, g.BoxSourceHeight,
                              g.BoxSideWallThickness, g.BoxEndWallThickness, g.BoxToDetectorDistance);

            string text0 = GeometryWriter.Render(g);
            string stamp0 = Stamp(g);
            string scene0 = SceneShape(g);

            using (var panel = new GeometryEditorPanel())
            {
                panel.CreateControl();
                panel.SetModel(g);
                GeometryModel built = Build(panel);

                Console.WriteLine("  кювета после редактора: X={0:R} Y={1:R} H={2:R} "
                                  + "стенка={3:R}/{4:R} зазор={5:R} мм",
                                  built.BoxSourceX, built.BoxSourceY, built.BoxSourceHeight,
                                  built.BoxSideWallThickness, built.BoxEndWallThickness,
                                  built.BoxToDetectorDistance);

                Report(built.BoxSourceX == 0.0 && built.BoxSourceY == 0.0
                       && built.BoxSourceHeight == 0.0 && built.BoxSideWallThickness == 0.0
                       && built.BoxEndWallThickness == 0.0 && built.BoxToDetectorDistance == 0.0,
                       "у цилиндрического источника поля кюветы СНЯТЫ (не оседает подсказка)");

                string text1 = GeometryWriter.Render(built);
                Dictionary<string, double> after = ReadText(text1);
                var filled = new List<string>();
                foreach (string k in SbKeys)
                {
                    double v = Value(after, k);
                    if (!(Math.Abs(v) < 1e-12))
                    {
                        filled.Add(string.Format(CultureInfo.InvariantCulture, "{0}={1:R}", k, v));
                    }
                }

                Report(filled.Count == 0,
                       "в записанном тексте ключи SB_* нулевые (заполнились: {0})",
                       filled.Count == 0 ? "ни один" : string.Join(", ", filled.ToArray()));

                Report(text1 == text0, "текст .in после открытия-сохранения ТОТ ЖЕ{0}",
                       Diff(text0, text1));
                Report(Stamp(built) == stamp0,
                       "⛔ ОТПЕЧАТОК после открытия-сохранения ТОТ ЖЕ ({0} -> {1})",
                       Short(stamp0), Short(Stamp(built)));
                string scene1 = SceneShape(built);
                Report(SameScene(scene0, scene1), "сцена та же{0}",
                       SameScene(scene0, scene1) ? "" : Diff(scene0, scene1));
                if (SameScene(scene0, scene1) && scene0 != scene1)
                {
                    Console.WriteLine("  (сцена разошлась ПОСЛЕДНИМ БИТОМ, это давнее и не про "
                                      + "`A134`{0})", Diff(scene0, scene1));
                }
            }

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. Рабочий размер этой геометрии — диаметр
            // сосуда, и его сдвиг ОБЯЗАН быть виден везде. Без этого пункта
            // проверка выше проходила бы и у пробы, не мерящей ничего.
            GeometryModel moved = g.Clone();
            moved.BeakerDiameter += 1.0;
            bool tm = GeometryWriter.Render(moved) != text0;
            bool sm = Stamp(moved) != stamp0;
            bool cm = !SameScene(SceneShape(moved), scene0);
            Report(tm && sm && cm,
                   "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: BeakerDiameter +1 мм виден в тексте({0}), "
                   + "отпечатке({1}), сцене({2})", tm, sm, cm);

            // ⛔ ВТОРОЙ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, уже на редакторе: правка, которую
            // человек ДЕЙСТВИТЕЛЬНО сделал, обязана доехать до файла. Иначе
            // «ничего не изменилось» означало бы, что редактор не работает.
            using (var panel = new GeometryEditorPanel())
            {
                panel.CreateControl();
                panel.SetModel(g);
                SetField(panel, "BeakerDiameter", g.BeakerDiameter + 1.0);
                GeometryModel edited = Build(panel);
                bool moved2 = GeometryWriter.Render(edited) != text0 && Stamp(edited) != stamp0
                              && !SameScene(SceneShape(edited), scene0);
                Report(moved2 && Near(edited.BeakerDiameter, g.BeakerDiameter + 1.0),
                       "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: правка поля в редакторе доезжает "
                       + "(D={0:R} -> {1:R}, отпечаток сдвинут: {2})",
                       g.BeakerDiameter, edited.BeakerDiameter, Stamp(edited) != stamp0);
            }

            // Старый файл, в котором ключи кюветы УЖЕ набраны — так его писал
            // редактор до 04.09.2026. Читается без отказа, и первое же
            // сохранение через редактор снимает лишнее.
            string tmp = Path.Combine(Path.GetTempPath(),
                                      "a134_legacy_" + Guid.NewGuid().ToString("N") + ".in");
            try
            {
                WriteWithSb(path, tmp);
                GeometryModel legacy = GeometryModel.Load(tmp);
                Report(legacy.SourceType == GeometrySourceType.Cylinder
                       && Near(legacy.BoxSourceX, 990.0) && Near(legacy.BoxSourceHeight, 770.0),
                       "старый файл с набранными SB_SourceX=99 см/SB_SourceHeight=77 см читается "
                       + "БЕЗ ОТКАЗА (X={0:R} H={1:R} мм)", legacy.BoxSourceX, legacy.BoxSourceHeight);
                Report(SameScene(SceneShape(legacy), scene0),
                       "у него ТА ЖЕ сцена: чужие ключи в сцену не входят{0}",
                       SameScene(SceneShape(legacy), scene0) ? "" : Diff(scene0, SceneShape(legacy)));

                using (var panel = new GeometryEditorPanel())
                {
                    panel.CreateControl();
                    panel.SetModel(legacy);
                    GeometryModel cleaned = Build(panel);
                    Report(cleaned.BoxSourceX == 0.0 && cleaned.BoxSourceHeight == 0.0
                           && GeometryWriter.Render(cleaned) == text0 && Stamp(cleaned) == stamp0,
                           "и первое же сохранение через редактор ЛИШНЕЕ СНИМАЕТ: "
                           + "текст и отпечаток становятся канонические");
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch (IOException) { }
            }

            // Конфигурация прибора: XML со старыми числами в чужих полях
            // читается без отказа и даёт ту же сцену.
            GeometryModel stale = g.Clone();
            stale.BoxSourceX = 333.0;
            stale.BoxSourceHeight = 444.0;
            var serializer = new XmlSerializer(typeof(GeometryModel));
            var buffer = new StringWriter(CultureInfo.InvariantCulture);
            serializer.Serialize(buffer, stale);
            GeometryModel back;
            using (var reader = new StringReader(buffer.ToString()))
            {
                back = (GeometryModel)serializer.Deserialize(reader);
            }

            Report(Near(back.BoxSourceX, 333.0) && SameScene(SceneShape(back), scene0),
                   "старая конфигурация (XML) с BoxSourceX=333 читается, сцена та же");

            CleanBoxRoundTrip(g);
        }

        /// <summary>
        /// Прямоугольная кювета на файле, написанном НАШИМ писателем.
        ///
        /// Зачем отдельно от <see cref="BoxChecks"/>: единственная кювета склада
        /// (`Nano16Pro_box.in`) ввезена из ЛСРМ и тащит блок коаксиала, который
        /// теряется на `Clone` независимо от `A134`. Здесь кювета собирается из
        /// той же цилиндрической геометрии, пишется и читается обратно — и на
        /// таком файле требование побайтного совпадения законно.
        /// </summary>
        static void CleanBoxRoundTrip(GeometryModel cyl)
        {
            Console.WriteLine();
            Console.WriteLine("=== своя КЮВЕТА (собрана из этой же геометрии) ===");

            GeometryModel box = cyl.Clone();
            box.SourceType = GeometrySourceType.Box;
            box.BoxSourceX = 71.0;
            box.BoxSourceY = 43.0;
            box.BoxSourceHeight = 17.0;
            box.BoxToDetectorDistance = 23.0;
            box.BoxSideWallThickness = 1.5;
            box.BoxEndWallThickness = 2.5;

            string tmp = Path.Combine(Path.GetTempPath(),
                                      "a134_box_" + Guid.NewGuid().ToString("N") + ".in");
            try
            {
                File.WriteAllText(tmp, GeometryWriter.Render(box), Encoding.GetEncoding(1251));
                GeometryModel loaded = GeometryModel.Load(tmp);
                string text0 = GeometryWriter.Render(loaded);
                string stamp0 = Stamp(loaded);
                string scene0 = SceneShape(loaded);
                Report(loaded.SourceType == GeometrySourceType.Box
                       && Near(loaded.BoxSourceX, 71.0) && Near(loaded.BoxEndWallThickness, 2.5),
                       "кювета 71×43×17 мм записалась и прочиталась (X={0:R} стенка={1:R})",
                       loaded.BoxSourceX, loaded.BoxEndWallThickness);

                using (var panel = new GeometryEditorPanel())
                {
                    panel.CreateControl();
                    panel.SetModel(loaded);
                    GeometryModel built = Build(panel);
                    Report(GeometryWriter.Render(built) == text0,
                           "текст .in после открытия-сохранения ТОТ ЖЕ{0}",
                           Diff(text0, GeometryWriter.Render(built)));
                    Report(Stamp(built) == stamp0, "⛔ ОТПЕЧАТОК тот же ({0} -> {1})",
                           Short(stamp0), Short(Stamp(built)));
                    Report(SameScene(SceneShape(built), scene0), "сцена та же{0}",
                           SameScene(SceneShape(built), scene0) ? "" : Diff(scene0, SceneShape(built)));
                }

                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: правка размера кюветы В РЕДАКТОРЕ
                // обязана доехать до текста, отпечатка и сцены.
                using (var panel = new GeometryEditorPanel())
                {
                    panel.CreateControl();
                    panel.SetModel(loaded);
                    SetField(panel, "BoxSourceX", 72.0);
                    GeometryModel edited = Build(panel);
                    bool tm = GeometryWriter.Render(edited) != text0;
                    bool sm = Stamp(edited) != stamp0;
                    bool cm = !SameScene(SceneShape(edited), scene0);
                    Report(Near(edited.BoxSourceX, 72.0) && tm && sm && cm,
                           "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: BoxSourceX 71 -> {0:R} мм в редакторе виден "
                           + "в тексте({1}), отпечатке({2}), сцене({3})",
                           edited.BoxSourceX, tm, sm, cm);
                }
            }
            finally
            {
                try { File.Delete(tmp); } catch (IOException) { }
            }
        }

        // ------------------------------------------------------------------
        // Прямоугольная кювета: её собственные числа переживают редактор
        // ------------------------------------------------------------------

        static void BoxChecks(string path)
        {
            Console.WriteLine();
            Console.WriteLine("=== {0}: источник КЮВЕТА ===", Path.GetFileName(path));

            GeometryModel g = GeometryModel.Load(path);
            Report(g.SourceType == GeometrySourceType.Box,
                   "источник {0} (ждём Box)", g.SourceType);
            Console.WriteLine("  кювета: X={0:R} Y={1:R} H={2:R} стенка={3:R}/{4:R} зазор={5:R} мм",
                              g.BoxSourceX, g.BoxSourceY, g.BoxSourceHeight,
                              g.BoxSideWallThickness, g.BoxEndWallThickness, g.BoxToDetectorDistance);

            string text0 = GeometryWriter.Render(g);
            string stamp0 = Stamp(g);
            string scene0 = SceneShape(g);

            using (var panel = new GeometryEditorPanel())
            {
                panel.CreateControl();
                panel.SetModel(g);
                GeometryModel built = Build(panel);
                Report(Near(built.BoxSourceX, g.BoxSourceX) && Near(built.BoxSourceY, g.BoxSourceY)
                       && Near(built.BoxSourceHeight, g.BoxSourceHeight)
                       && Near(built.BoxToDetectorDistance, g.BoxToDetectorDistance),
                       "⛔ у КЮВЕТЫ её собственные размеры редактор СОХРАНЯЕТ "
                       + "(X={0:R} Y={1:R} H={2:R})",
                       built.BoxSourceX, built.BoxSourceY, built.BoxSourceHeight);
                // ⛔ ИСКЛЮЧЕНИЕ ЧУЖИХ КЛЮЧЕЙ СНЯТО (`T148`, 04.09.2026). Довод
                // был такой: `Nano16Pro_box.in` ввезён из ЛСРМ, у него есть блок
                // коаксиала (`DC_*`), а `Clone` не переносил `Raw`, из которого
                // писатель этот блок берёт, — и расхождение было на чистом
                // `HEAD`. После `A139` разбор копируется, и на всех 66 файлах
                // склада измерено «по чужим (коаксиал/вещество) 0». Поэтому
                // чужое расхождение теперь ОТКАЗ, а не строка в скобках: оно
                // означает, что писатель снова потерял чужие блоки файла.
                List<string> drift = Drift(text0, GeometryWriter.Render(built));
                var sbDrift = new List<string>();
                var foreign = new List<string>();
                foreach (string k in drift)
                {
                    if (k.StartsWith("SB_", StringComparison.Ordinal)) sbDrift.Add(k);
                    else foreign.Add(k);
                }

                Report(sbDrift.Count == 0, "ключи SB_* не сдвинулись ни один (сдвинулось: {0})",
                       sbDrift.Count == 0 ? "ни одного" : string.Join(", ", sbDrift.ToArray()));
                Report(foreign.Count == 0,
                       "чужие ключи файла (коаксиал `DC_*`, состав вещества) тоже на месте "
                       + "(разошлись: {0})",
                       foreign.Count == 0 ? "ни одного" : string.Join(", ", foreign.ToArray()));

                string scene1 = SceneShape(built);
                Report(SameScene(scene0, scene1), "сцена та же{0}",
                       SameScene(scene0, scene1) ? "" : Diff(scene0, scene1));
            }

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: у кюветы её размер РАБОЧИЙ, и сдвиг
            // обязан быть виден и в тексте, и в отпечатке, и в сцене.
            GeometryModel moved = g.Clone();
            moved.BoxSourceX += 1.0;
            bool tm = GeometryWriter.Render(moved) != text0;
            bool sm = Stamp(moved) != stamp0;
            bool cm = !SameScene(SceneShape(moved), scene0);
            Report(tm && sm && cm,
                   "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: BoxSourceX +1 мм виден в тексте({0}), "
                   + "отпечатке({1}), сцене({2})", tm, sm, cm);

            // Смена вида источника на цилиндрический СНИМАЕТ кювету — и это
            // правка, а не молчаливая порча: человек сам выбрал другой сосуд.
            using (var panel = new GeometryEditorPanel())
            {
                panel.CreateControl();
                panel.SetModel(g);
                var combo = (ComboBox)Field(panel, "sourceTypeCombo");
                combo.SelectedIndex = 1;      // цилиндрический сосуд
                GeometryModel asCyl = Build(panel);
                Report(asCyl.SourceType == GeometrySourceType.Cylinder
                       && asCyl.BoxSourceX == 0.0 && asCyl.BoxSourceY == 0.0
                       && asCyl.BoxSourceHeight == 0.0,
                       "смена вида источника на ЦИЛИНДР снимает поля кюветы "
                       + "(X={0:R} Y={1:R} H={2:R})",
                       asCyl.BoxSourceX, asCyl.BoxSourceY, asCyl.BoxSourceHeight);

                // И обратно: подсказка на месте — поля кюветы открываются
                // размерами сосуда, а не нулями.
                combo.SelectedIndex = 3;      // прямоугольная кювета
                GeometryModel asBox = Build(panel);
                Report(asBox.SourceType == GeometrySourceType.Box
                       && asBox.BoxSourceX > 0.0 && asBox.BoxSourceHeight > 0.0,
                       "возврат к КЮВЕТЕ снова даёт непустые поля (X={0:R} H={1:R})",
                       asBox.BoxSourceX, asBox.BoxSourceHeight);
            }
        }

        // ------------------------------------------------------------------
        // Мелочи
        // ------------------------------------------------------------------

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

        /// <summary>Набрать число в поле редактора — как это делает человек.</summary>
        static void SetField(GeometryEditorPanel panel, string key, double value)
        {
            var fields = (Dictionary<string, TextBox>)Field(panel, "fields");
            TextBox box;
            if (!fields.TryGetValue(key, out box))
            {
                throw new MissingFieldException("поля " + key + " в редакторе нет");
            }

            box.Text = value.ToString("G8", CultureInfo.InvariantCulture);
        }

        static string Stamp(GeometryModel g)
        {
            // Настройки фиксированные: меряется геометрия, а не они.
            var options = new ResponseMatrixOptions { NodeCount = 10, Histories = 4000, BinKev = 4.0 };
            return ResponseMatrix.ComputeStamp(g, options);
        }

        static string Short(string stamp)
        {
            int semi = stamp.IndexOf(';');
            return semi > 0 && stamp.Length > semi + 17 ? stamp.Substring(semi + 1, 16) : stamp;
        }

        static string Scene(GeometryModel g)
        {
            return new EfficiencySimulator(g).DumpScene();
        }

        /// <summary>
        /// Сцена без строк ВЕЩЕСТВА — только области и источник. Редактор берёт
        /// вещество из библиотеки по имени, а файл хранит доли округлёнными;
        /// это не про `A134` и тела сцены от этого не двигаются.
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

        /// <summary>
        /// Та же ли сцена ПО ЧИСЛАМ, а не по знакам.
        ///
        /// ⚠ Строгое сравнение дампа здесь врёт, и это измерено на чистом
        /// `HEAD`: модель держит миллиметры как `см × 10`, а редактор — текст
        /// поля, и обратный разбор даёт то же число с точностью до ПОСЛЕДНЕГО
        /// БИТА (−0.31 против −0.31000000000000005 см). В текст `.in` это не
        /// попадает (там `G8`), в отпечаток — тоже, и ни одна матрица от этого
        /// не устаревает. Настоящий сдвиг геометрии — миллиметры, он от
        /// 1e-9 отличается на девять порядков, так что сторож остаётся острым:
        /// положительный контроль рядом двигает размер на 1 мм и ОБЯЗАН
        /// сработать.
        /// </summary>
        static bool SameScene(string a, string b)
        {
            if (a == b)
            {
                return true;
            }

            string[] x = a.Split(Sep), y = b.Split(Sep);
            if (x.Length != y.Length)
            {
                return false;
            }

            char[] gap = { ' ' };
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] == y[i])
                {
                    continue;
                }

                string[] p = x[i].Split(gap, StringSplitOptions.RemoveEmptyEntries);
                string[] q = y[i].Split(gap, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length != q.Length)
                {
                    return false;
                }

                for (int k = 0; k < p.Length; k++)
                {
                    if (p[k] == q[k])
                    {
                        continue;
                    }

                    double u, v;
                    if (!double.TryParse(p[k], NumberStyles.Float, CultureInfo.InvariantCulture, out u)
                        || !double.TryParse(q[k], NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                        || Math.Abs(u - v) > 1e-9 * Math.Max(1.0, Math.Abs(v)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>Копия файла, в которой ключи кюветы НАБРАНЫ — старый формат.</summary>
        static void WriteWithSb(string source, string target)
        {
            Encoding cp = Encoding.GetEncoding(1251);
            string[] lines = File.ReadAllLines(source, cp);
            var outLines = new List<string>();
            bool seenX = false, seenH = false;
            foreach (string line in lines)
            {
                if (line.StartsWith("SB_SourceX", StringComparison.Ordinal))
                {
                    outLines.Add("SB_SourceX = 99 cm");
                    seenX = true;
                }
                else if (line.StartsWith("SB_SourceHeight", StringComparison.Ordinal))
                {
                    outLines.Add("SB_SourceHeight = 77 cm");
                    seenH = true;
                }
                else
                {
                    outLines.Add(line);
                }
            }

            if (!seenX) outLines.Add("SB_SourceX = 99 cm");
            if (!seenH) outLines.Add("SB_SourceHeight = 77 cm");
            File.WriteAllLines(target, outLines.ToArray(), cp);
        }

        static Dictionary<string, double> ReadText(string text)
        {
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0)
                {
                    line = line.Substring(0, comment);
                }

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
