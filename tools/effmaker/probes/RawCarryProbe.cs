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

namespace RawCarryProbe
{
    /// <summary>
    /// `A139`: разбор файла (`GeometryModel.Raw`) теряется на `Clone`, и вместе
    /// с ним из записанного текста `.in` пропадают ЧУЖИЕ БЛОКИ — коаксиальный
    /// детектор (16 ключей `DC_*` плюс пять его веществ) и «пустое место»
    /// сосуда/маринелли (`M_SC_EmptySpace`, `M_SM_EmptySpace`).
    ///
    /// Чем это больно. `ResponseMatrix.ComputeStamp` берёт ВЕСЬ текст `.in`
    /// (`GeometryText` -> `GeometryWriter.Render`), а писатель эти блоки берёт
    /// из `Raw` (`Carried`, `Carry`, `CarrySlot`); нет `Raw` — пишутся нули и
    /// умолчания. Значит одна и та же геометрия даёт ДВА разных отпечатка:
    /// «как из файла» и «после копии». Матрица, посчитанная по первому,
    /// объявляется устаревшей при втором — человек ничего не правил, а получил
    /// часы пересчёта.
    ///
    /// Мерится ТРИ круга, каждый по отдельности — иначе не видно, какой из них
    /// виноват:
    ///   * `--clone`   голая `GeometryModel.Clone()`;
    ///   * `--editor`  открытие-сохранение через `GeometryEditorPanel`
    ///                 (`SetModel` + закрытый `BuildModel`, тот же, что у
    ///                 кнопки «Сохранить»);
    ///   * `--xml`     сохранение геометрии в конфигурацию прибора и чтение
    ///                 обратно (`XmlSerializer`) — так она живёт между
    ///                 запусками приложения.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ОБЯЗАТЕЛЕН и стоит рядом с каждым «ничего не
    /// изменилось»: `--spoil` убирает из разбора ОДИН ключ `DC_*` руками, и
    /// приёмка обязана ОТКАЗАТЬ. Без него проверка проходит и у пробы, не
    /// мерящей ничего.
    ///
    ///     rawcarryprobe [--dir=&lt;каталог с .in&gt;]...   опись склада
    ///                   [--clone=&lt;каталог&gt;]...        круг через Clone
    ///                   [--editor=&lt;каталог&gt;]...       круг через редактор
    ///                   [--xml=&lt;каталог&gt;]...          круг через конфигурацию
    ///                   [--show=&lt;файл.in&gt;]            построчно, что именно уехало
    ///                   [--spoil=&lt;файл.in&gt;]           положительный контроль
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
            var cloneDirs = new List<string>();
            var editorDirs = new List<string>();
            var xmlDirs = new List<string>();
            var carriedDirs = new List<string>();
            var spoilPaths = new List<string>();
            string showPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) dirs.Add(a.Substring(6));
                else if (a.StartsWith("--carried=", StringComparison.Ordinal)) carriedDirs.Add(a.Substring(10));
                else if (a.StartsWith("--clone=", StringComparison.Ordinal)) cloneDirs.Add(a.Substring(8));
                else if (a.StartsWith("--editor=", StringComparison.Ordinal)) editorDirs.Add(a.Substring(9));
                else if (a.StartsWith("--xml=", StringComparison.Ordinal)) xmlDirs.Add(a.Substring(6));
                else if (a.StartsWith("--show=", StringComparison.Ordinal)) showPath = a.Substring(7);
                else if (a.StartsWith("--spoil=", StringComparison.Ordinal)) spoilPaths.Add(a.Substring(8));
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            // Библиотека веществ читает matdb — без менеджера сцена не соберётся.
            GlobalConfigManager.GetInstance();

            foreach (string dir in dirs) Inventory(dir);
            foreach (string dir in carriedDirs) Carried(dir);
            foreach (string dir in cloneDirs) Round(dir, "КЛОН", CloneOf);
            foreach (string dir in editorDirs) Round(dir, "РЕДАКТОР", EditorOf);
            foreach (string dir in xmlDirs) Round(dir, "КОНФИГУРАЦИЯ (XML)", XmlOf);
            if (showPath != null) Show(showPath);
            foreach (string path in spoilPaths) Spoil(path);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // Опись — тот же формат, что у `SbKeysProbe` (`A134`): по ней правка
        // сверяется ПОБАЙТНО с чистым `HEAD`.
        // ------------------------------------------------------------------

        static void Inventory(string dir)
        {
            string[] files = Files(dir);
            Console.WriteLine();
            Console.WriteLine("=== ОПИСЬ {0}: {1} файлов ===", Path.GetFileName(dir.TrimEnd('\\')), files.Length);
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
        // ⛔ Полнота УРЕЗАННОГО разбора — счётом, а не чтением писателя
        // ------------------------------------------------------------------

        /// <summary>
        /// В конфигурацию прибора едет не весь разбор, а урезанный
        /// (`GeometryWriter.CarriedFrom`, `A139`): хранить две сотни ключей,
        /// из которых читаются пятьдесят, незачем. Урезание опасно ровно одним
        /// — забытым ключом, и проверять его чтением кода нельзя.
        ///
        /// Проверка ПЕРЕБОРОМ: у каждого ключа разбора по очереди убирается
        /// значение и текст `.in` собирается заново. Изменился — значит ключ
        /// писателю НУЖЕН и обязан быть в урезанном наборе. Обратное тоже
        /// говорится вслух: сколько ключей хранится напрасно.
        /// </summary>
        static void Carried(string dir)
        {
            string[] files = Files(dir);
            Console.WriteLine();
            Console.WriteLine("=== ПОЛНОТА УРЕЗАННОГО РАЗБОРА: {0}, {1} файлов ===",
                              Path.GetFileName(dir.TrimEnd('\\')), files.Length);

            int missing = 0, spare = 0, rawTotal = 0, keptTotal = 0, neededTotal = 0;
            foreach (string file in files)
            {
                GeometryModel g = GeometryModel.Load(file);
                string text0 = GeometryWriter.Render(g);
                Dictionary<string, string> kept = GeometryWriter.CarriedFrom(g);
                var needed = new List<string>();
                var lost = new List<string>();
                foreach (string key in new List<string>(g.Raw.Keys))
                {
                    GeometryModel cut = g.Clone();
                    cut.Raw.Remove(key);
                    if (GeometryWriter.Render(cut) == text0)
                    {
                        continue;
                    }

                    needed.Add(key);
                    if (!kept.ContainsKey(key))
                    {
                        lost.Add(key);
                    }
                }

                rawTotal += g.Raw.Count;
                keptTotal += kept.Count;
                neededTotal += needed.Count;
                if (lost.Count > 0)
                {
                    missing++;
                    Console.WriteLine("{0,-44} ⛔ УРЕЗАНО ЛИШНЕЕ: {1}", Path.GetFileName(file),
                                      Head(lost));
                }

                if (kept.Count > needed.Count)
                {
                    spare += kept.Count - needed.Count;
                }
            }

            Console.WriteLine("--- итог: ключей в разборе {0}, хранится {1}, писателю нужно {2}",
                              rawTotal, keptTotal, neededTotal);
            Report(missing == 0, "⛔ урезанный разбор ПОЛОН: нет ни одного файла, у которого "
                   + "нужный писателю ключ не хранится (таких файлов {0})", missing);
            Report(keptTotal < rawTotal, "и он ДЕЙСТВИТЕЛЬНО урезан: {0} ключей вместо {1} "
                   + "(про запас сверх нужного {2})", keptTotal, rawTotal, spare);
        }

        // ------------------------------------------------------------------
        // Круг: геометрию провели через копию — текст и отпечаток обязаны
        // остаться теми же
        // ------------------------------------------------------------------

        static void Round(string dir, string what, Func<GeometryModel, GeometryModel> pass)
        {
            string[] files = Files(dir);
            Console.WriteLine();
            Console.WriteLine("=== {0}: {1}, {2} файлов ===", what,
                              Path.GetFileName(dir.TrimEnd('\\')), files.Length);

            int textMoved = 0, stampMoved = 0, sceneMoved = 0, sceneUlp = 0, imported = 0, importedMoved = 0;
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                GeometryModel g = GeometryModel.Load(file);
                string text0 = GeometryWriter.Render(g);
                string stamp0 = Stamp(g);
                string scene0 = SceneShape(g);
                bool isImported = Imported(g);
                if (isImported) imported++;

                GeometryModel back = pass(g);
                string text1 = GeometryWriter.Render(back);
                string stamp1 = Stamp(back);
                string scene1 = SceneShape(back);

                List<string> drift = Drift(text0, text1);
                if (drift.Count > 0) textMoved++;
                if (stamp0 != stamp1)
                {
                    stampMoved++;
                    if (isImported) importedMoved++;
                }

                if (!SameScene(scene0, scene1)) sceneMoved++;
                else if (scene0 != scene1) sceneUlp++;

                Console.WriteLine("{0,-44} {1,-8} отпечаток {2}  строк разошлось {3,-4} {4}",
                                  name, isImported ? "ВВЕЗЁН" : "свой",
                                  stamp0 == stamp1 ? "ТОТ ЖЕ " : "СДВИНУТ",
                                  drift.Count, Groups(drift));
            }

            Console.WriteLine("--- итог {0}/{1}: файлов {2} (ввезённых {3}); текст разошёлся у {4}; "
                              + "отпечаток сдвинут у {5} (из них ввезённых {6}); "
                              + "сцен сдвинуто {7}, из них последним битом {8}",
                              what, Path.GetFileName(dir.TrimEnd('\\')), files.Length, imported,
                              textMoved, stampMoved, importedMoved, sceneMoved + sceneUlp, sceneUlp);
            Report(textMoved == 0, "⛔ {0}: текст .in ТОТ ЖЕ у всех {1} файлов (разошёлся у {2})",
                   what, files.Length, textMoved);
            Report(stampMoved == 0, "⛔ {0}: ОТПЕЧАТОК тот же у всех {1} файлов (сдвинут у {2}, "
                   + "ввезённых среди них {3})", what, files.Length, stampMoved, importedMoved);
            Report(sceneMoved == 0, "{0}: сцена та же у всех, кроме последнего бита "
                   + "(сдвинулось по-настоящему {1}, последним битом {2})",
                   what, sceneMoved, sceneUlp);
        }

        static GeometryModel CloneOf(GeometryModel g)
        {
            return g.Clone();
        }

        /// <summary>
        /// Открытие-сохранение: та же дорога, какой идёт кнопка «Сохранить».
        ///
        /// ⚠ `TryCommit` не зовётся нарочно: на ошибке он показывает
        /// `MessageBox`, и безоконная проба повисла бы без человека у экрана.
        /// </summary>
        static GeometryModel EditorOf(GeometryModel g)
        {
            using (var panel = new GeometryEditorPanel())
            {
                panel.CreateControl();
                panel.SetModel(g);
                return Build(panel);
            }
        }

        /// <summary>Как геометрия переживает запись в конфигурацию прибора.</summary>
        static GeometryModel XmlOf(GeometryModel g)
        {
            var serializer = new XmlSerializer(typeof(GeometryModel));
            var buffer = new StringWriter(CultureInfo.InvariantCulture);
            serializer.Serialize(buffer, g);
            using (var reader = new StringReader(buffer.ToString()))
            {
                return (GeometryModel)serializer.Deserialize(reader);
            }
        }

        // ------------------------------------------------------------------
        // Построчно: что именно уезжает
        // ------------------------------------------------------------------

        static void Show(string path)
        {
            Console.WriteLine();
            Console.WriteLine("=== ЧТО УЕЗЖАЕТ: {0} ===", Path.GetFileName(path));

            GeometryModel g = GeometryModel.Load(path);
            Console.WriteLine("  ключей в разборе (Raw): {0}; DC_* среди них: {1}",
                              g.Raw.Count, CountPrefix(g.Raw, "DC_"));

            string text0 = GeometryWriter.Render(g);
            string[] rounds = { "Clone", "редактор", "XML" };
            Func<GeometryModel, GeometryModel>[] passes = { CloneOf, EditorOf, XmlOf };
            for (int i = 0; i < rounds.Length; i++)
            {
                GeometryModel back = passes[i](g);
                Console.WriteLine("  --- через {0}: в разборе осталось {1} ключей ---",
                                  rounds[i], back.Raw.Count);
                string text1 = GeometryWriter.Render(back);
                Console.WriteLine("      отпечаток {0} -> {1}", Short(Stamp(g)), Short(Stamp(back)));
                Lines(text0, text1);
            }
        }

        static int CountPrefix(Dictionary<string, string> raw, string prefix)
        {
            int n = 0;
            foreach (string key in raw.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) n++;
            }

            return n;
        }

        static void Lines(string expected, string actual)
        {
            string[] a = expected.Split(Sep), b = actual.Split(Sep);
            int shown = 0, total = 0;
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string x = i < a.Length ? a[i].TrimEnd('\r') : "(нет строки)";
                string y = i < b.Length ? b[i].TrimEnd('\r') : "(нет строки)";
                if (x == y) continue;
                total++;
                if (shown < 24)
                {
                    Console.WriteLine("      {0,4}: «{1}» -> «{2}»", i + 1, x, y);
                    shown++;
                }
            }

            if (total > shown) Console.WriteLine("      …и ещё {0} строк", total - shown);
            if (total == 0) Console.WriteLine("      (текст побайтно тот же)");
        }

        // ------------------------------------------------------------------
        // ⛔ Положительный контроль: подставленная порча обязана быть НАЙДЕНА
        // ------------------------------------------------------------------

        static void Spoil(string path)
        {
            Console.WriteLine();
            Console.WriteLine("=== ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: {0} ===", Path.GetFileName(path));

            GeometryModel g = GeometryModel.Load(path);
            string text0 = GeometryWriter.Render(g);
            string stamp0 = Stamp(g);
            // Отпечаток файла НА ДИСКЕ — чтобы в конце показать, что порча была
            // подставлена в копиях, а исходник вернулся побайтно.
            string fileSha0 = ShaOfFile(path);
            Report(Imported(g), "файл ввезённый: в разборе есть непустой DC_CrystalDiameter ({0})",
                   Value(g, "DC_CrystalDiameter"));

            // (1) Один ключ коаксиала убран РУКАМИ. Приёмка обязана его назвать.
            GeometryModel one = g.Clone();
            one.Raw.Remove("DC_CrystalDiameter");
            List<string> drift1 = Drift(text0, GeometryWriter.Render(one));
            Report(drift1.Count > 0 && Stamp(one) != stamp0,
                   "убран ОДИН ключ DC_CrystalDiameter — приёмка ОТКАЗЫВАЕТ "
                   + "(строк разошлось {0}: {1}; отпечаток {2} -> {3})",
                   drift1.Count, Head(drift1), Short(stamp0), Short(Stamp(one)));

            // (2) Весь разбор убран — так вело себя `Clone` до правки.
            GeometryModel none = g.Clone();
            none.Raw.Clear();
            List<string> drift2 = Drift(text0, GeometryWriter.Render(none));
            Report(drift2.Count > 0 && Stamp(none) != stamp0,
                   "убран ВЕСЬ разбор — приёмка ОТКАЗЫВАЕТ (строк разошлось {0}; "
                   + "отпечаток {1} -> {2})", drift2.Count, Short(stamp0), Short(Stamp(none)));

            // (3) Рабочий размер сдвинут на 1 мм — обязан быть виден везде.
            //
            // ⚠ Размер берётся ПО ФОРМЕ КРИСТАЛЛА (`A94`): у бруска полей
            // цилиндра не существует, и сдвиг `CrystalDiameter` у него не
            // доехал бы никуда — проверка молча мерила бы пустоту.
            GeometryModel moved = g.Clone();
            string movedName;
            if (g.Shape == CrystalShape.Box)
            {
                moved.CrystalBoxZ += 1.0;
                movedName = "CrystalBoxZ";
            }
            else
            {
                moved.CrystalHeight += 1.0;
                movedName = "CrystalHeight";
            }

            bool tm = GeometryWriter.Render(moved) != text0;
            bool sm = Stamp(moved) != stamp0;
            bool cm = !SameScene(SceneShape(moved), SceneShape(g));
            Report(tm && sm && cm, "{0} +1 мм (форма {1}) виден в тексте({2}), отпечатке({3}), "
                   + "сцене({4})", movedName, g.Shape, tm, sm, cm);

            // (3б) И та же правка, проведённая ЧЕРЕЗ РЕДАКТОР, обязана доехать:
            // иначе «открытие-сохранение ничего не меняет» значило бы, что
            // редактор не работает вовсе.
            using (var panel = new GeometryEditorPanel())
            {
                panel.CreateControl();
                panel.SetModel(g);
                SetField(panel, movedName, (g.Shape == CrystalShape.Box ? g.CrystalBoxZ : g.CrystalHeight) + 1.0);
                GeometryModel edited = Build(panel);
                Report(GeometryWriter.Render(edited) != text0 && Stamp(edited) != stamp0
                       && !SameScene(SceneShape(edited), SceneShape(g)),
                       "та же правка В РЕДАКТОРЕ доезжает до текста, отпечатка и сцены");
            }

            // (4) Старый файл БЕЗ блока коаксиала читается без отказа и даёт
            //     свой, канонический текст: правка ничего не требует от файла.
            string tmp = Path.Combine(Path.GetTempPath(),
                                      "a139_nodc_" + Guid.NewGuid().ToString("N") + ".in");
            try
            {
                WriteWithout(path, tmp, "DC_");
                GeometryModel plain = GeometryModel.Load(tmp);
                Report(CountPrefix(plain.Raw, "DC_") == 0,
                       "файл БЕЗ единого ключа DC_* читается без отказа (в разборе DC_*: {0})",
                       CountPrefix(plain.Raw, "DC_"));
                Report(SameScene(SceneShape(plain), SceneShape(g)),
                       "и даёт ТУ ЖЕ сцену: чужие блоки в сцену не входят");
                GeometryModel plainBack = EditorOf(plain);
                Report(GeometryWriter.Render(plainBack) == GeometryWriter.Render(plain)
                       && Stamp(plainBack) == Stamp(plain),
                       "у него открытие-сохранение тоже ничего не двигает");
            }
            finally
            {
                try { File.Delete(tmp); } catch (IOException) { }
            }

            // (5) Старый файл с ЧУЖИМ значением в ключе коаксиала: читается,
            //     переносится дословно, сцена та же.
            string tmp2 = Path.Combine(Path.GetTempPath(),
                                       "a139_odd_" + Guid.NewGuid().ToString("N") + ".in");
            try
            {
                WriteWith(path, tmp2, "DC_CrystalDiameter", "DC_CrystalDiameter = 99 cm");
                GeometryModel odd = GeometryModel.Load(tmp2);
                string oddText = GeometryWriter.Render(odd);
                Report(oddText.Contains("DC_CrystalDiameter = 99"),
                       "чужое значение DC_CrystalDiameter = 99 см читается и ПЕРЕНОСИТСЯ дословно");
                Report(SameScene(SceneShape(odd), SceneShape(g)), "сцена у него та же");
                GeometryModel oddBack = EditorOf(odd);
                Report(GeometryWriter.Render(oddBack) == oddText && Stamp(oddBack) == Stamp(odd),
                       "и открытие-сохранение его не двигает");
            }
            finally
            {
                try { File.Delete(tmp2); } catch (IOException) { }
            }

            // (6) ⛔ ПОРЧА В САМОМ ФАЙЛЕ: из копии убрана ОДНА строка `DC_*`.
            //     Опись обязана НАЗВАТЬ такой файл другим — длиной, sha256
            //     текста и отпечатком. Приёмка, которая этого не видит, не
            //     видит и настоящей потери блока.
            string tmp3 = Path.Combine(Path.GetTempPath(),
                                       "a139_cut_" + Guid.NewGuid().ToString("N") + ".in");
            try
            {
                CutLine(path, tmp3, "DC_DetectorCapDiameter");
                GeometryModel cut = GeometryModel.Load(tmp3);
                string cutText = GeometryWriter.Render(cut);
                // ⚠ ДЛИНА ОДНА И ТА ЖЕ, и это не промах приёмки: писатель
                //   печатает ключ ВСЕГДА, а пропавшее значение подставляет
                //   нулём — «9 cm» становится «0 cm», знак в знак. Длиной
                //   такую порчу не поймать, и опись держится на sha256 и
                //   отпечатке, а не на длине.
                Report(Sha(cutText) != Sha(text0) && Stamp(cut) != stamp0,
                       "порча В ФАЙЛЕ (убрана строка DC_DetectorCapDiameter) НАЗВАНА описью: "
                       + "txt {0} -> {1}, отпечаток {2} -> {3} (длина та же, {4}: писатель "
                       + "подставляет нуль)",
                       Sha(text0).Substring(0, 16), Sha(cutText).Substring(0, 16),
                       Short(stamp0), Short(Stamp(cut)), cutText.Length);
            }
            finally
            {
                try { File.Delete(tmp3); } catch (IOException) { }
            }

            // (6б) ⛔ СТАРАЯ КОНФИГУРАЦИЯ ПРИБОРА — та, что записана ДО `A139`
            //      и разбора в себе не держит. Читается без отказа, геометрия
            //      целая, а чужие блоки в ней те же нули и умолчания, что были
            //      у неё и раньше: правка ничего не требует от старых файлов.
            var ser = new XmlSerializer(typeof(GeometryModel));
            var buf = new StringWriter(CultureInfo.InvariantCulture);
            ser.Serialize(buf, g);
            string withRaw = buf.ToString();
            string withoutRaw = System.Text.RegularExpressions.Regex.Replace(
                withRaw, @"\s*<Raw>.*?</Raw>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
            Report(withoutRaw.Length < withRaw.Length,
                   "конфигурация ДО правки собрана снятием блока <Raw>: {0} знаков вместо {1} "
                   + "(разбор в XML занимает {2})",
                   withoutRaw.Length, withRaw.Length, withRaw.Length - withoutRaw.Length);
            GeometryModel old;
            using (var rd = new StringReader(withoutRaw))
            {
                old = (GeometryModel)ser.Deserialize(rd);
            }

            Report(old.Raw.Count == 0 && SameScene(SceneShape(old), SceneShape(g))
                   && Near(old.CrystalBoxZ, g.CrystalBoxZ) && Near(old.CrystalHeight, g.CrystalHeight),
                   "СТАРАЯ конфигурация (без <Raw>) читается БЕЗ ОТКАЗА, геометрия и сцена целы");
            GeometryModel oldBack = EditorOf(old);
            Report(GeometryWriter.Render(oldBack) == GeometryWriter.Render(old)
                   && Stamp(oldBack) == Stamp(old),
                   "и открытие-сохранение её не двигает (отпечаток {0}) — прежние матрицы годны",
                   Short(Stamp(old)));

            // (6в) ⛔ ВТОРАЯ ВЕТВЬ карты веществ: «состав из библиотеки».
            //      Открытие геометрии состав СОХРАНЯЕТ, но правка САМОЙ
            //      библиотеки обязана его заменить — иначе она бы не работала.
            //      Ветвь эта иначе не гоняется ничем: `EditMaterials` открывает
            //      модальное окно, и безоконная проба на нём повисла бы.
            using (var panel = new GeometryEditorPanel())
            {
                panel.CreateControl();
                panel.SetModel(g);
                GeometryModel kept = Build(panel);
                bool sameAsFile = Same(kept.BeakerWall, g.BeakerWall);

                SelectFromLibrary(panel, "BeakerWall", g.BeakerWall);
                GeometryModel taken = Build(panel);
                bool tookLibrary = !Same(taken.BeakerWall, g.BeakerWall)
                                   && taken.BeakerWall.Fractions.Count > 0;
                Report(sameAsFile && tookLibrary,
                       "состав стенки: открытие СОХРАНЯЕТ файловый ({0}), правка библиотеки "
                       + "ЗАМЕНЯЕТ его ({1}); доли {2} -> {3}",
                       sameAsFile, tookLibrary, First(g.BeakerWall), First(taken.BeakerWall));
            }

            // (7) И ВОЗВРАТ: порча ставилась только в копиях, исходник на диске
            //     и то, что из него собирается, — побайтно те же.
            string fileSha1 = ShaOfFile(path);
            GeometryModel again = GeometryModel.Load(path);
            Report(fileSha1 == fileSha0 && Sha(GeometryWriter.Render(again)) == Sha(text0)
                   && Stamp(again) == stamp0,
                   "ВОЗВРАТ побайтный: sha256 файла {0} (был {1}), текста {2}, отпечаток {3}",
                   fileSha1.Substring(0, 16), fileSha0.Substring(0, 16),
                   Sha(GeometryWriter.Render(again)).Substring(0, 16), Short(Stamp(again)));
        }

        static string ShaOfFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(File.ReadAllBytes(path));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
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

        /// <summary>Копия файла БЕЗ одной строки — подставленная порча.</summary>
        static void CutLine(string source, string target, string key)
        {
            Encoding cp = Encoding.GetEncoding(1251);
            var kept = new List<string>();
            foreach (string line in File.ReadAllLines(source, cp))
            {
                string t = line.TrimStart();
                if (t.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase)
                    || t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                kept.Add(line);
            }

            File.WriteAllLines(target, kept.ToArray(), cp);
        }

        static string Value(GeometryModel g, string key)
        {
            string v;
            return g.Raw.TryGetValue(key, out v) ? v.Trim() : "(нет)";
        }

        /// <summary>Ввезённый из ЛСРМ: у него непустой блок коаксиала.</summary>
        static bool Imported(GeometryModel g)
        {
            string raw;
            if (!g.Raw.TryGetValue("DC_CrystalDiameter", out raw)) return false;
            double v;
            var m = System.Text.RegularExpressions.Regex.Match(raw, @"^\s*(-?[0-9.]+)");
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                                                CultureInfo.InvariantCulture, out v) && Math.Abs(v) > 1e-12;
        }

        // ------------------------------------------------------------------
        // Мелочи
        // ------------------------------------------------------------------

        static string[] Files(string dir)
        {
            string[] files = Directory.GetFiles(dir, "*.in", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
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
        /// Сцена без строк ВЕЩЕСТВА — только области и источник: редактор берёт
        /// вещество из библиотеки по имени, а файл хранит доли округлёнными.
        /// Тела сцены от этого не двигаются.
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
                foreach (byte b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        static List<string> Drift(string expected, string actual)
        {
            var keys = new List<string>();
            string[] a = expected.Split(Sep), b = actual.Split(Sep);
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                string x = i < a.Length ? a[i].TrimEnd('\r') : "";
                string y = i < b.Length ? b[i].TrimEnd('\r') : "";
                if (x == y) continue;
                int eq = x.IndexOf('=');
                string key = eq > 0 ? x.Substring(0, eq).Trim() : "строка " + (i + 1);
                keys.Add(key.Length > 0 ? key : "строка " + (i + 1));
            }

            return keys;
        }

        /// <summary>Разошедшиеся ключи по блокам — иначе перечень нечитаем.</summary>
        static string Groups(List<string> keys)
        {
            if (keys.Count == 0) return "-";
            int dc = 0, sc = 0, sm = 0, ds = 0, sb = 0, other = 0;
            foreach (string k in keys)
            {
                if (k.StartsWith("DC_", StringComparison.Ordinal)
                    || k.StartsWith("M_DC_", StringComparison.Ordinal)) dc++;
                else if (k.StartsWith("SB_", StringComparison.Ordinal)) sb++;
                else if (k.StartsWith("SC_", StringComparison.Ordinal)
                         || k.StartsWith("M_SC_", StringComparison.Ordinal)) sc++;
                else if (k.StartsWith("SM_", StringComparison.Ordinal)
                         || k.StartsWith("M_SM_", StringComparison.Ordinal)) sm++;
                else if (k.StartsWith("DS_", StringComparison.Ordinal)
                         || k.StartsWith("M_DS_", StringComparison.Ordinal)) ds++;
                else other++;
            }

            var parts = new List<string>();
            if (dc > 0) parts.Add("коаксиал " + dc);
            if (sc > 0) parts.Add("сосуд " + sc);
            if (sm > 0) parts.Add("маринелли " + sm);
            if (ds > 0) parts.Add("сцинтиллятор " + ds);
            if (sb > 0) parts.Add("кювета " + sb);
            if (other > 0) parts.Add("прочее " + other);
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>
        /// Позвать карту веществ так, как её зовёт ВОЗВРАТ ИЗ ПРАВКИ БИБЛИОТЕКИ:
        /// состав берётся из неё, а не из пришедшей геометрии. Флаг `loading`
        /// поднят по той же причине, по какой его поднимает `EditMaterials`:
        /// это не правка руками, и признак изменённости взводить нельзя.
        /// </summary>
        static void SelectFromLibrary(GeometryEditorPanel panel, string key, GeometryMaterial material)
        {
            System.Reflection.FieldInfo loading = typeof(GeometryEditorPanel).GetField(
                "loading", System.Reflection.BindingFlags.Instance
                           | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo select = typeof(GeometryEditorPanel).GetMethod(
                "SelectMaterial", System.Reflection.BindingFlags.Instance
                                  | System.Reflection.BindingFlags.NonPublic);
            if (loading == null || select == null)
            {
                throw new MissingMethodException("GeometryEditorPanel.SelectMaterial/loading");
            }

            object was = loading.GetValue(panel);
            loading.SetValue(panel, true);
            try
            {
                select.Invoke(panel, new object[] { key, material, false });
            }
            finally
            {
                loading.SetValue(panel, was);
            }
        }

        static bool Same(GeometryMaterial a, GeometryMaterial b)
        {
            if (a == null || b == null || a.Fractions.Count != b.Fractions.Count)
            {
                return false;
            }

            foreach (KeyValuePair<int, double> pair in a.Fractions)
            {
                double other;
                if (!b.Fractions.TryGetValue(pair.Key, out other) || !Near(pair.Value, other))
                {
                    return false;
                }
            }

            return true;
        }

        static string First(GeometryMaterial m)
        {
            if (m == null || m.Fractions.Count == 0)
            {
                return "(пусто)";
            }

            var order = new List<int>(m.Fractions.Keys);
            order.Sort();
            return string.Format(CultureInfo.InvariantCulture, "Z={0} {1:G8}", order[0],
                                 m.Fractions[order[0]]);
        }

        static bool Near(double a, double b)
        {
            return Math.Abs(a - b) <= 1e-6 * Math.Max(1.0, Math.Abs(b));
        }

        static string Head(List<string> keys)
        {
            if (keys.Count <= 4) return string.Join(",", keys.ToArray());
            return string.Join(",", keys.GetRange(0, 4).ToArray()) + " …и ещё " + (keys.Count - 4);
        }

        /// <summary>
        /// Та же ли сцена ПО ЧИСЛАМ, а не по знакам (`T147`): модель держит мм
        /// как «см × 10», редактор — текст `G8`, и обратный разбор даёт то же
        /// число с точностью до ПОСЛЕДНЕГО БИТА. В текст `.in` и в отпечаток это
        /// не попадает. Настоящий сдвиг геометрии — миллиметры, он от 1e-9
        /// отличается на девять порядков.
        /// </summary>
        static bool SameScene(string a, string b)
        {
            if (a == b) return true;
            string[] x = a.Split(Sep), y = b.Split(Sep);
            if (x.Length != y.Length) return false;
            char[] gap = { ' ' };
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] == y[i]) continue;
                string[] p = x[i].Split(gap, StringSplitOptions.RemoveEmptyEntries);
                string[] q = y[i].Split(gap, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length != q.Length) return false;
                for (int k = 0; k < p.Length; k++)
                {
                    if (p[k] == q[k]) continue;
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

        /// <summary>Копия файла БЕЗ строк, начинающихся с указанной приставки.</summary>
        static void WriteWithout(string source, string target, string prefix)
        {
            Encoding cp = Encoding.GetEncoding(1251);
            var kept = new List<string>();
            foreach (string line in File.ReadAllLines(source, cp))
            {
                if (line.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || line.TrimStart().StartsWith("M_DC_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                kept.Add(line);
            }

            File.WriteAllLines(target, kept.ToArray(), cp);
        }

        /// <summary>Копия файла, в которой одна строка заменена.</summary>
        static void WriteWith(string source, string target, string key, string replacement)
        {
            Encoding cp = Encoding.GetEncoding(1251);
            var outLines = new List<string>();
            bool seen = false;
            foreach (string line in File.ReadAllLines(source, cp))
            {
                if (line.TrimStart().StartsWith(key + " ", StringComparison.OrdinalIgnoreCase)
                    || line.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    outLines.Add(replacement);
                    seen = true;
                }
                else
                {
                    outLines.Add(line);
                }
            }

            if (!seen) outLines.Add(replacement);
            File.WriteAllLines(target, outLines.ToArray(), cp);
        }

        static void Report(bool ok, string format, params object[] args)
        {
            Console.WriteLine("[{0}] {1}", ok ? "  ok  " : "ПРОВАЛ",
                              string.Format(CultureInfo.InvariantCulture, format, args));
            if (!ok) bad++;
        }
    }
}
