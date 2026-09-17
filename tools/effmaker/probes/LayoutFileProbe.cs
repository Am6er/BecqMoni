using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using WeifenLuo.WinFormsUI.Docking;

namespace LayoutFileProbe
{
    /// <summary>
    /// ФАЙЛ РАСКЛАДКИ ПАНЕЛЕЙ `ExpertMode.xml`: запись без пустого файла и
    /// чтение без падения (`AMBER43`, полоса П89, 17.09.2026).
    ///
    /// ЗАЧЕМ. Задача Amber 15.09.2026: «Падение приложения, если файл настроек
    /// расположения окон в UI поломался при сохранении. Основная задача -
    /// избежать поломки этого файла ExpertMode.xml». Библиотечный
    /// <c>SaveAsXml(имя)</c> сперва обрезал файл до нуля и лишь потом писал;
    /// голый <c>LoadFromXml</c> в <c>MainForm_Load</c> на пустом файле бросал
    /// <c>XmlException</c> из <c>OnLoad</c>, и приложение падало на каждом
    /// запуске. Починка — <c>BecquerelMonitor.LayoutFile</c> (в память →
    /// <c>.tmp</c> → <c>File.Replace</c> с <c>.bak</c>; чтение под заслоном:
    /// сломанный → <c>.broken-&lt;дата&gt;</c>, поднять <c>.bak</c>, иначе
    /// умолчание) и тонкие вызовы в <c>MainForm</c>.
    ///
    /// ЧТО МЕРЯЕТСЯ (новая сборка, помощник есть):
    ///
    ///   1. ЗАПИСЬ файлового ядра <c>LayoutFile.Write</c> подсаженным
    ///      сериализатором: (в) удачная вторая запись — <c>.bak</c> побайтно
    ///      равен прежнему файлу, <c>.tmp</c> убран; (б) сериализатор,
    ///      бросающий на полпути, и сериализатор, не пишущий ничего, — целевой
    ///      файл прежний побайтно, <c>.bak</c> прежний, <c>.tmp</c> убран;
    ///      осиротевший <c>.tmp</c> «убитого» процесса следующей записи не мешает.
    ///   2. ЧТЕНИЕ файлового ядра <c>LayoutFile.Read</c> подсаженным читателем:
    ///      нет файла — не отказ; годный — прочитан; сломанный при годном
    ///      <c>.bak</c> — отложен в <c>.broken-…</c>, поднята копия, копия
    ///      вернулась на место; сломанный при сломанном <c>.bak</c> — умолчание с
    ///      обеими причинами; сломанный без <c>.bak</c> — умолчание; слово
    ///      <c>Describe</c> называет пути.
    ///   3. НАСТОЯЩАЯ ПАНЕЛЬ <c>MainForm.dockPanel1</c> без окна: <c>Save</c>
    ///      даёт файл БАЙТ В БАЙТ тот же, что библиотечный <c>SaveAsXml(имя)</c>
    ///      (кодировка Unicode сохранена); (а) пустой файл и файл, обрезанный
    ///      посередине, — <c>Load</c> не бросает, сломанный отложен, <c>.bak</c>
    ///      поднят / умолчание; тонкие вызовы <c>MainForm.LoadLayoutXml</c> и
    ///      <c>MainForm.SaveLayoutXml</c> без окон называют отказ строкой в поток
    ///      ошибок (перехвачен), не бросают и файл не портят; (б) отказ
    ///      сериализации настоящей панели (содержимое с бросающим
    ///      <c>GetPersistString</c>) — файл прежний побайтно.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — та же проба на СТАРОЙ сборке (без
    /// <c>LayoutFile</c>; ищется отражением, потому проба собирается против
    /// обеих). Там прогоняется прежний путь: (а) <c>dockPanel1.LoadFromXml</c>
    /// пустого и обрезанного файла ОБЯЗАН бросить <c>XmlException</c>; (б)
    /// <c>dockPanel1.SaveAsXml(имя)</c> с отказом сериализации ОБЯЗАН испортить
    /// годный файл (мерено: остаются 2 байта преамбулы `FF FE`, XML не
    /// читается — «Root element is missing», как в журнале падения Amber). Так
    /// «не бросает» и «файл прежний»
    /// на новой сборке — измерение, а не совпадение. Код возврата на старой
    /// сборке — 1 («СТАРАЯ СБОРКА: дефект воспроизведён»); если дефект НЕ
    /// воспроизвёлся — 2 (контроль сам сломан, верить нечему).
    ///
    /// Каталог опыта — ключ <c>--dir=&lt;путь&gt;</c> (умолчание
    /// <c>%TEMP%\BecqMoni.LayoutFileProbe</c>); каталог раскладки Amber
    /// (<c>%AppData%\BecqMoni</c>) не трогается. Файлы подсадок остаются в
    /// каталоге после прогона — это улики для журнала.
    ///
    ///     layoutfileprobe --dir=D:\BqMoni_Claude\p89\layout_probe
    ///
    /// Ожидание на новой сборке: «ВСЕ СОШЛИСЬ», код 0.
    /// </summary>
    static class Program
    {
        static int bad;
        static string dir;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // Культура ЦЕЛИКОМ инвариантная (правило Amber 05.09.2026, `T245`).
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            dir = Path.Combine(Path.GetTempPath(), "BecqMoni.LayoutFileProbe");
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal))
                {
                    dir = a.Substring("--dir=".Length).Trim('"');
                }
            }
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
            Directory.CreateDirectory(dir);
            Console.WriteLine("каталог опыта: {0}", dir);

            Type layoutFile = typeof(MainForm).Assembly.GetType("BecquerelMonitor.LayoutFile");
            Console.WriteLine("сборка приложения: {0}", typeof(MainForm).Assembly.Location);
            Console.WriteLine("помощник BecquerelMonitor.LayoutFile: {0}", layoutFile != null ? "ЕСТЬ" : "НЕТ (старая сборка)");
            Console.WriteLine("AppUi.HasWindows = {0}", AppUi.HasWindows);

            MainForm mainForm = new MainForm();
            DockPanel panel = Field<DockPanel>(mainForm, "dockPanel1");
            DeserializeDockContent deserialize = delegate (string s) { return null; };
            Console.WriteLine("dockPanel1: тема {0}, содержимого {1}",
                              panel.Theme != null ? panel.Theme.GetType().Name : "нет", panel.Contents.Count);

            int code;
            try
            {
                if (layoutFile == null)
                {
                    code = OldBuildSection(mainForm, panel, deserialize);
                }
                else
                {
                    WriteCoreSection(layoutFile);
                    ReadCoreSection(layoutFile);
                    RealPanelSection(layoutFile, mainForm, panel, deserialize);
                    Console.WriteLine();
                    Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
                    code = bad == 0 ? 0 : 1;
                }
            }
            finally
            {
                // Формы убираются явно: иначе процесс падает уже ПОСЛЕ итога, на
                // разборе окон (0xC000041D), и код возврата лжёт.
                mainForm.Dispose();
            }
            return code;
        }

        // ------------------------------------------------------------------
        // 1. ЗАПИСЬ — файловое ядро LayoutFile.Write(string, Action<Stream>)
        // ------------------------------------------------------------------

        static void WriteCoreSection(Type layoutFile)
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. запись: LayoutFile.Write подсаженным сериализатором ===");
            MethodInfo write = layoutFile.GetMethod("Write", new[] { typeof(string), typeof(Action<Stream>) });
            Same("метод Write(string, Action<Stream>) есть", true, write != null);
            string target = Path.Combine(dir, "core", "ExpertMode.xml");
            string tmp = target + ".tmp";
            string bak = target + ".bak";
            byte[] a = Encoding.UTF8.GetBytes("<A/>");
            byte[] b = Encoding.UTF8.GetBytes("<B>второй</B>");

            // 1.1 первая запись: каталога ещё нет — создаётся; .bak не появляется.
            Call(write, target, Bytes(a));
            Same("1.1 первая запись: файл есть", true, File.Exists(target));
            Same("1.1 первая запись: содержимое A", true, Equal(a, File.ReadAllBytes(target)));
            Same("1.1 первая запись: .tmp убран", false, File.Exists(tmp));
            Same("1.1 первая запись: .bak нет (нечего сохранять)", false, File.Exists(bak));

            // 1.2 (в) вторая запись: .bak побайтно равен прежнему файлу.
            Call(write, target, Bytes(b));
            Same("1.2 (в) вторая запись: содержимое B", true, Equal(b, File.ReadAllBytes(target)));
            Same("1.2 (в) .bak побайтно равен прежнему файлу (A)", true, File.Exists(bak) && Equal(a, File.ReadAllBytes(bak)));
            Same("1.2 (в) .tmp убран", false, File.Exists(tmp));

            // 1.3 (б) сериализатор бросает НА ПОЛПУТИ, уже записав часть байтов.
            Exception thrown = Fails(write, target, delegate (Stream s)
            {
                s.Write(a, 0, 2);
                throw new InvalidOperationException("подсаженный отказ сериализации");
            });
            Same("1.3 (б) отказ сериализации: исключение дошло до вызывающего", true, thrown != null);
            Console.WriteLine("      исключение: {0}", thrown != null ? thrown.GetType().Name + ": " + thrown.Message : "—");
            Same("1.3 (б) целевой файл прежний побайтно (B)", true, Equal(b, File.ReadAllBytes(target)));
            Same("1.3 (б) .bak прежний побайтно (A)", true, Equal(a, File.ReadAllBytes(bak)));
            Same("1.3 (б) .tmp убран", false, File.Exists(tmp));

            // 1.4 (б) сериализатор не пишет НИЧЕГО — это и есть пустой файл, от которого чинимся.
            thrown = Fails(write, target, delegate (Stream s) { });
            Same("1.4 (б) пустая сериализация: отказ, а не пустой файл", true, thrown != null);
            Console.WriteLine("      исключение: {0}", thrown != null ? thrown.GetType().Name + ": " + thrown.Message : "—");
            Same("1.4 (б) целевой файл прежний побайтно (B)", true, Equal(b, File.ReadAllBytes(target)));
            Same("1.4 (б) .tmp убран", false, File.Exists(tmp));

            // 1.5 осиротевший .tmp от «убитого» процесса следующей записи не мешает.
            File.WriteAllText(tmp, "огрызок убитого процесса");
            Call(write, target, Bytes(a));
            Same("1.5 осиротевший .tmp перезаписан, файл A", true, Equal(a, File.ReadAllBytes(target)));
            Same("1.5 .bak = прежний B", true, Equal(b, File.ReadAllBytes(bak)));
            Same("1.5 .tmp убран", false, File.Exists(tmp));
        }

        // ------------------------------------------------------------------
        // 2. ЧТЕНИЕ — файловое ядро LayoutFile.Read(string, Action<string>)
        // ------------------------------------------------------------------

        static void ReadCoreSection(Type layoutFile)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. чтение: LayoutFile.Read подсаженным читателем ===");
            MethodInfo read = layoutFile.GetMethod("Read", new[] { typeof(string), typeof(Action<string>) });
            MethodInfo describe = layoutFile.GetMethod("Describe");
            Same("метод Read(string, Action<string>) есть", true, read != null);
            Same("метод Describe есть", true, describe != null);
            string target = Path.Combine(dir, "core_read", "ExpertMode.xml");
            string bak = target + ".bak";
            Directory.CreateDirectory(Path.GetDirectoryName(target));

            // Подсаженный читатель: файл, начинающийся с «BAD», негоден.
            List<string> seen = new List<string>();
            Action<string> loader = delegate (string path)
            {
                seen.Add(Path.GetFileName(path));
                string text = File.ReadAllText(path);
                if (text.StartsWith("BAD", StringComparison.Ordinal))
                {
                    throw new XmlException("подсаженный отказ разбора: " + Path.GetFileName(path));
                }
            };

            // 2.1 файла нет.
            object o = read.Invoke(null, new object[] { target, loader });
            Same("2.1 нет файла: Kind = Absent", "Absent", Kind(o));
            Same("2.1 нет файла: не отказ", false, Failed(o));
            Same("2.1 нет файла: читатель не звался", 0, seen.Count);

            // 2.2 годный файл.
            File.WriteAllText(target, "GOOD 1");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, loader });
            Same("2.2 годный: Kind = Loaded", "Loaded", Kind(o));
            Same("2.2 годный: не отказ", false, Failed(o));
            Same("2.2 годный: читатель звался один раз", 1, seen.Count);
            Same("2.2 годный: Describe пуст", "", (string)describe.Invoke(null, new[] { o }));

            // 2.3 сломанный при годном .bak.
            File.WriteAllText(target, "BAD main");
            File.WriteAllText(bak, "GOOD bak");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, loader });
            Same("2.3 сломанный+.bak: Kind = LoadedFromBackup", "LoadedFromBackup", Kind(o));
            Same("2.3 сломанный+.bak: отказ назван", true, Failed(o));
            Same("2.3 сломанный+.bak: читатель звался дважды (main, bak)", "ExpertMode.xml|ExpertMode.xml.bak", string.Join("|", seen.ToArray()));
            string broken = (string)Get(o, "BrokenPath");
            Console.WriteLine("      отложен как: {0}", broken ?? "—");
            Same("2.3 сломанный отложен в .broken-<дата>", true,
                 broken != null && Path.GetFileName(broken).StartsWith("ExpertMode.xml.broken-", StringComparison.Ordinal) && File.Exists(broken));
            Same("2.3 улика: содержимое отложенного = сломанный", "BAD main", broken != null ? File.ReadAllText(broken) : null);
            Same("2.3 копия вернулась на место основного файла", "GOOD bak", File.Exists(target) ? File.ReadAllText(target) : null);
            string word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("2.3 слово называет основной файл", true, word.Contains(Path.GetFullPath(target)));
            Same("2.3 слово называет отложенный", true, broken != null && word.Contains(Path.GetFullPath(broken)));
            Same("2.3 слово называет .bak", true, word.Contains(Path.GetFullPath(bak)));
            Same("2.3 слово называет причину", true, word.Contains("подсаженный отказ разбора"));

            // 2.4 сломанный при сломанном .bak.
            Thread.Sleep(1100); // метка времени .broken- с точностью до секунды — не столкнуться с 2.3
            File.WriteAllText(target, "BAD main 2");
            File.WriteAllText(bak, "BAD bak 2");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, loader });
            Same("2.4 сломанный+сломанный .bak: Kind = Default", "Default", Kind(o));
            Same("2.4 отказ назван", true, Failed(o));
            Same("2.4 .bak был", true, (bool)Get(o, "BackupExisted"));
            Same("2.4 причина .bak названа", true, ((string)Get(o, "BackupError") ?? "").Contains("ExpertMode.xml.bak"));
            Same("2.4 основного файла на месте нет (отложен)", false, File.Exists(target));
            word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("2.4 слово называет причину основного файла", true, word.Contains("подсаженный отказ разбора: ExpertMode.xml"));
            Same("2.4 слово называет причину копии", true, word.Contains("подсаженный отказ разбора: ExpertMode.xml.bak"));

            // 2.5 сломанный без .bak.
            Thread.Sleep(1100);
            File.Delete(bak);
            File.WriteAllText(target, "BAD main 3");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, loader });
            Same("2.5 сломанный без .bak: Kind = Default", "Default", Kind(o));
            Same("2.5 .bak не было", false, (bool)Get(o, "BackupExisted"));
            Same("2.5 читатель звался один раз", 1, seen.Count);
            word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("2.5 слово непустое", true, word.Length > 0);

            string[] brokenFiles = Directory.GetFiles(Path.GetDirectoryName(target), "ExpertMode.xml.broken-*");
            Same("2. отложенных улик три", 3, brokenFiles.Length);
        }

        // ------------------------------------------------------------------
        // 3. НАСТОЯЩАЯ ПАНЕЛЬ MainForm.dockPanel1 без окна
        // ------------------------------------------------------------------

        static void RealPanelSection(Type layoutFile, MainForm mainForm, DockPanel panel, DeserializeDockContent deserialize)
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. настоящая панель dockPanel1 (без окна) ===");
            MethodInfo save = layoutFile.GetMethod("Save", new[] { typeof(DockPanel), typeof(string) });
            MethodInfo load = layoutFile.GetMethod("Load", new[] { typeof(DockPanel), typeof(string), typeof(DeserializeDockContent) });
            Same("метод Save(DockPanel, string) есть", true, save != null);
            Same("метод Load(DockPanel, string, DeserializeDockContent) есть", true, load != null);
            string layoutDir = Path.Combine(dir, "layout");
            Directory.CreateDirectory(layoutDir);
            string target = Path.Combine(layoutDir, "ExpertMode.xml");
            string bak = target + ".bak";
            string tmp = target + ".tmp";

            // 3.1 Save даёт байт в байт то же, что библиотечный SaveAsXml(имя).
            string libraryFile = Path.Combine(layoutDir, "library.xml");
            panel.SaveAsXml(libraryFile);
            save.Invoke(null, new object[] { panel, target });
            byte[] good = File.ReadAllBytes(target);
            byte[] lib = File.ReadAllBytes(libraryFile);
            Console.WriteLine("      библиотечный SaveAsXml(имя): {0} байт, начало {1}; LayoutFile.Save: {2} байт, начало {3}",
                              lib.Length, Head(lib), good.Length, Head(good));
            Same("3.1 LayoutFile.Save байт в байт = SaveAsXml(имя)", true, Equal(lib, good));
            Same("3.1 файл начинается с BOM UTF-16 LE (кодировка Unicode сохранена)", true, good.Length > 2 && good[0] == 0xFF && good[1] == 0xFE);
            Same("3.1 .tmp убран", false, File.Exists(tmp));
            Same("3.1 записанное читается панелью", true, Loads(panel, target, deserialize));

            // (в) на панели: вторая запись с другой раскладкой — .bak = прежний файл.
            double portion = panel.DockLeftPortion;
            panel.DockLeftPortion = portion + 17;
            save.Invoke(null, new object[] { panel, target });
            byte[] good2 = File.ReadAllBytes(target);
            Same("3.1 (в) вторая раскладка отличается от первой", false, Equal(good, good2));
            Same("3.1 (в) .bak побайтно = первая раскладка", true, File.Exists(bak) && Equal(good, File.ReadAllBytes(bak)));
            panel.DockLeftPortion = portion;

            // 3.2 (а) пустой файл на месте основного, .bak годен.
            File.WriteAllBytes(target, new byte[0]);
            File.WriteAllBytes(bak, good);
            object o = null;
            Exception thrown = null;
            try { o = load.Invoke(null, new object[] { panel, target, deserialize }); }
            catch (TargetInvocationException ex) { thrown = ex.InnerException; }
            Same("3.2 (а) пустой файл: Load не бросает", true, thrown == null);
            if (thrown != null) Console.WriteLine("      бросил: {0}", thrown);
            Same("3.2 (а) пустой файл: Kind = LoadedFromBackup", "LoadedFromBackup", o != null ? Kind(o) : null);
            Same("3.2 (а) причина — Root element is missing (XmlException)", true,
                 o != null && ((string)Get(o, "TargetError") ?? "").Contains("XmlException"));
            Console.WriteLine("      причина: {0}", o != null ? Get(o, "TargetError") : null);
            string broken = o != null ? (string)Get(o, "BrokenPath") : null;
            Same("3.2 (а) сломанный отложен в .broken-…, 0 байт", true, broken != null && File.Exists(broken) && new FileInfo(broken).Length == 0);
            Same("3.2 (а) копия вернулась на место основного", true, File.Exists(target) && Equal(good, File.ReadAllBytes(target)));
            Same("3.2 (а) панель после подъёма копии пуста, как и раскладка", 0, panel.Contents.Count);

            // 3.3 (а) файл, обрезанный посередине, без .bak.
            Thread.Sleep(1100);
            byte[] half = new byte[good.Length / 2];
            Array.Copy(good, half, half.Length);
            File.WriteAllBytes(target, half);
            File.Delete(bak);
            thrown = null;
            try { o = load.Invoke(null, new object[] { panel, target, deserialize }); }
            catch (TargetInvocationException ex) { thrown = ex.InnerException; }
            Same("3.3 (а) обрезанный файл: Load не бросает", true, thrown == null);
            if (thrown != null) Console.WriteLine("      бросил: {0}", thrown);
            Same("3.3 (а) обрезанный файл: Kind = Default (копии нет)", "Default", o != null ? Kind(o) : null);
            Console.WriteLine("      причина: {0}", o != null ? Get(o, "TargetError") : null);
            broken = o != null ? (string)Get(o, "BrokenPath") : null;
            Same("3.3 (а) обрезанный отложен, улика = обрезанные байты", true,
                 broken != null && File.Exists(broken) && Equal(half, File.ReadAllBytes(broken)));
            Same("3.3 (а) основного файла на месте нет", false, File.Exists(target));
            Same("3.3 (а) после отказа разбора панель не тронута (пуста)", 0, panel.Contents.Count);
            Same("3.3 (а) годный файл после этого читается (панель не «уже заполнена»)", true, Loads(panel, libraryFile, deserialize));

            // 3.4 тонкий вызов MainForm.LoadLayoutXml без окон: слово в поток ошибок, не бросок.
            MethodInfo loadLayout = typeof(MainForm).GetMethod("LoadLayoutXml", BindingFlags.Instance | BindingFlags.NonPublic);
            Same("3.4 MainForm.LoadLayoutXml(string, bool) есть", true, loadLayout != null);
            // m_deserializeDockContent у неоткрытой формы ещё null — Load отдаёт его библиотеке;
            // раскладка пуста, содержимого нет, и десериализатор не зовётся. Подставим свой на всякий случай.
            SetField(mainForm, "m_deserializeDockContent", deserialize);
            Thread.Sleep(1100);
            File.WriteAllBytes(target, new byte[0]);
            File.WriteAllBytes(bak, good);
            string err;
            object result = null;
            thrown = null;
            using (StringWriter capture = new StringWriter())
            {
                TextWriter old = Console.Error;
                Console.SetError(capture);
                try { result = loadLayout.Invoke(mainForm, new object[] { target, false }); }
                catch (TargetInvocationException ex) { thrown = ex.InnerException; }
                finally { Console.SetError(old); }
                err = capture.ToString();
            }
            Same("3.4 LoadLayoutXml на пустом файле не бросает", true, thrown == null);
            if (thrown != null) Console.WriteLine("      бросил: {0}", thrown);
            Same("3.4 без окон слово сказано сразу (возврат null, ждать Shown некому)", null, result);
            Console.WriteLine("      поток ошибок: {0}", err.Trim());
            Same("3.4 слово в потоке ошибок называет файл", true, err.Contains(Path.GetFullPath(target)));
            Same("3.4 слово в потоке ошибок называет причину", true, err.Contains("XmlException"));
            Same("3.4 и копия поднята на место", true, File.Exists(target) && Equal(good, File.ReadAllBytes(target)));

            // 3.5 тонкий вызов MainForm.SaveLayoutXml: файл = библиотечная сериализация ТОГО ЖЕ момента.
            //     ⚠ Не «= good»: LoadFromXml переставляет z-порядок DockWindow (BringToFront в цикле
            //     «Set DockWindow ZOrders»), и после подъёма копии в 3.4 ZOrderIndex в файле законно другой.
            //     Измерено первым прогоном 17.09.2026: сравнение с good красное при годном файле.
            MethodInfo saveLayout = typeof(MainForm).GetMethod("SaveLayoutXml", BindingFlags.Instance | BindingFlags.NonPublic);
            Same("3.5 MainForm.SaveLayoutXml(string, bool) есть", true, saveLayout != null);
            string momentFile = Path.Combine(layoutDir, "moment.xml");
            panel.SaveAsXml(momentFile);
            byte[] moment = File.ReadAllBytes(momentFile);
            byte[] previous = File.ReadAllBytes(target);
            result = saveLayout.Invoke(mainForm, new object[] { target, true });
            Same("3.5 SaveLayoutXml вернул true", true, result);
            Same("3.5 файл = библиотечная сериализация того же момента (байт в байт)", true, Equal(moment, File.ReadAllBytes(target)));
            Same("3.5 .bak = прежний файл побайтно", true, Equal(previous, File.ReadAllBytes(bak)));
            Same("3.5 .tmp убран", false, File.Exists(tmp));

            // 3.6 (б) отказ сериализации НАСТОЯЩЕЙ панели: содержимое с бросающим GetPersistString.
            //     Ставится последним: панель после него не пуста, и Load в неё уже не пойдёт.
            byte[] before = File.ReadAllBytes(target);
            ThrowingContent bomb = new ThrowingContent();
            bomb.DockPanel = panel;
            Same("3.6 (б) подсадка: содержимого в панели 1", 1, panel.Contents.Count);
            thrown = null;
            try { save.Invoke(null, new object[] { panel, target }); }
            catch (TargetInvocationException ex) { thrown = ex.InnerException; }
            Same("3.6 (б) LayoutFile.Save на бросающей панели бросает", true, thrown != null);
            Console.WriteLine("      исключение: {0}", thrown != null ? thrown.GetType().Name + ": " + thrown.Message : "—");
            Same("3.6 (б) целевой файл прежний побайтно", true, Equal(before, File.ReadAllBytes(target)));
            Same("3.6 (б) целевой файл читается как XML", true, IsXml(target));
            Same("3.6 (б) .tmp убран", false, File.Exists(tmp));

            using (StringWriter capture = new StringWriter())
            {
                TextWriter old = Console.Error;
                Console.SetError(capture);
                try { result = saveLayout.Invoke(mainForm, new object[] { target, true }); }
                finally { Console.SetError(old); }
                err = capture.ToString();
            }
            Same("3.6 (б) MainForm.SaveLayoutXml вернул false", false, result);
            Console.WriteLine("      поток ошибок: {0}", err.Trim());
            Same("3.6 (б) отказ назван строкой с путём", true, err.Contains(Path.GetFullPath(target)));
            Same("3.6 (б) целевой файл и после тонкого вызова прежний", true, Equal(before, File.ReadAllBytes(target)));
            bomb.DockPanel = null;
            bomb.Dispose();
        }

        // ------------------------------------------------------------------
        // СТАРАЯ СБОРКА — положительный контроль прежнего пути
        // ------------------------------------------------------------------

        static int OldBuildSection(MainForm mainForm, DockPanel panel, DeserializeDockContent deserialize)
        {
            Console.WriteLine();
            Console.WriteLine("=== СТАРАЯ СБОРКА: прежний путь dockPanel1.LoadFromXml / SaveAsXml ===");
            string layoutDir = Path.Combine(dir, "layout_old");
            Directory.CreateDirectory(layoutDir);
            string target = Path.Combine(layoutDir, "ExpertMode.xml");
            int reproduced = 0;

            panel.SaveAsXml(target);
            byte[] good = File.ReadAllBytes(target);
            Console.WriteLine("  годный файл: {0} байт", good.Length);

            // (а) пустой файл — как в журнале падения Amber.
            File.WriteAllBytes(target, new byte[0]);
            Exception ex = Throws(delegate { panel.LoadFromXml(target, deserialize); });
            Console.WriteLine("  (а) пустой файл: {0}", ex != null ? ex.GetType().FullName + ": " + ex.Message : "НЕ БРОСИЛ");
            if (ex is XmlException) reproduced++;

            // (а) обрезанный посередине.
            byte[] half = new byte[good.Length / 2];
            Array.Copy(good, half, half.Length);
            File.WriteAllBytes(target, half);
            ex = Throws(delegate { panel.LoadFromXml(target, deserialize); });
            Console.WriteLine("  (а) обрезанный файл: {0}", ex != null ? ex.GetType().FullName + ": " + ex.Message : "НЕ БРОСИЛ");
            if (ex is XmlException) reproduced++;

            // (б) отказ сериализации: годный файл на месте, SaveAsXml(имя) его портит — обрезает до нуля
            //     либо до одной преамбулы кодировки. ⚠ Мерено 17.09.2026: остаётся не 0, а 2 байта (BOM
            //     UTF-16 `FF FE` — единственное, что писатель успел сбросить), и XmlTextReader на них
            //     говорит ровно то же, что в журнале падения Amber: «Root element is missing». Критерий
            //     потому не «0 байт», а «файл испорчен»: короче годного и не читается как XML.
            File.WriteAllBytes(target, good);
            ThrowingContent bomb = new ThrowingContent();
            bomb.DockPanel = panel;
            ex = Throws(delegate { panel.SaveAsXml(target); });
            long after = File.Exists(target) ? new FileInfo(target).Length : -1;
            bool spoiled = after >= 0 && after < good.Length && !IsXml(target);
            Console.WriteLine("  (б) SaveAsXml(имя) с отказом сериализации: {0}; файл после: {1} байт (было {2}), байты: {3}, читается как XML: {4}",
                              ex != null ? ex.GetType().Name + ": " + ex.Message : "НЕ БРОСИЛ", after, good.Length,
                              after > 0 ? Head(File.ReadAllBytes(target)) : "—", IsXml(target));
            if (ex != null && spoiled) reproduced++;

            // (б) через тонкий вызов приложения — SaveLayoutXml(имя, true): результат тот же.
            File.WriteAllBytes(target, good);
            MethodInfo saveLayout = typeof(MainForm).GetMethod("SaveLayoutXml", BindingFlags.Instance | BindingFlags.NonPublic);
            string err = "";
            object result = null;
            if (saveLayout != null)
            {
                using (StringWriter capture = new StringWriter())
                {
                    TextWriter old = Console.Error;
                    Console.SetError(capture);
                    try { result = saveLayout.Invoke(mainForm, new object[] { target, true }); }
                    finally { Console.SetError(old); }
                    err = capture.ToString();
                }
            }
            after = File.Exists(target) ? new FileInfo(target).Length : -1;
            spoiled = after >= 0 && after < good.Length && !IsXml(target);
            Console.WriteLine("  (б) MainForm.SaveLayoutXml: вернул {0}; файл после: {1} байт, читается как XML: {2}; поток ошибок: {3}",
                              result, after, IsXml(target), err.Trim());
            if (result is bool && !(bool)result && spoiled) reproduced++;
            bomb.DockPanel = null;
            bomb.Dispose();

            Console.WriteLine();
            if (reproduced == 4)
            {
                Console.WriteLine("СТАРАЯ СБОРКА: дефект AMBER43 воспроизведён по всем четырём плечам — положительный контроль годен. Код 1.");
                return 1;
            }
            Console.WriteLine("⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ НЕ СРАБОТАЛ: воспроизведено {0} из 4 — контролю верить нельзя. Код 2.", reproduced);
            return 2;
        }

        /// <summary>Содержимое панели, чья сериализация бросает — подсадка отказа записи.</summary>
        sealed class ThrowingContent : DockContent
        {
            protected override string GetPersistString()
            {
                throw new InvalidOperationException("подсаженный отказ сериализации содержимого панели");
            }
        }

        // ------------------------------------------------------------------
        // помощники
        // ------------------------------------------------------------------

        static Action<Stream> Bytes(byte[] data)
        {
            return delegate (Stream s) { s.Write(data, 0, data.Length); };
        }

        static void Call(MethodInfo write, string target, Action<Stream> serialize)
        {
            write.Invoke(null, new object[] { target, serialize });
        }

        static Exception Fails(MethodInfo write, string target, Action<Stream> serialize)
        {
            try
            {
                write.Invoke(null, new object[] { target, serialize });
                return null;
            }
            catch (TargetInvocationException ex)
            {
                return ex.InnerException ?? ex;
            }
        }

        static Exception Throws(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        static bool Loads(DockPanel panel, string path, DeserializeDockContent deserialize)
        {
            try
            {
                panel.LoadFromXml(path, deserialize);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("      не прочиталось: {0}: {1}", ex.GetType().Name, ex.Message);
                return false;
            }
        }

        static bool IsXml(string path)
        {
            try
            {
                using (XmlReader r = XmlReader.Create(path))
                {
                    while (r.Read()) { }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string Kind(object outcome)
        {
            object k = Get(outcome, "Kind");
            return k != null ? k.ToString() : null;
        }

        static bool Failed(object outcome)
        {
            PropertyInfo p = outcome.GetType().GetProperty("Failed");
            return (bool)p.GetValue(outcome, null);
        }

        static object Get(object outcome, string name)
        {
            FieldInfo f = outcome.GetType().GetField(name);
            if (f == null) throw new InvalidOperationException("нет поля " + name + " у " + outcome.GetType().Name);
            return f.GetValue(outcome);
        }

        static T Field<T>(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) return (T)f.GetValue(target);
            }
            throw new InvalidOperationException("нет поля " + name + " у " + target.GetType().Name);
        }

        static void SetField(object target, string name, object value)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) { f.SetValue(target, value); return; }
            }
            throw new InvalidOperationException("нет поля " + name + " у " + target.GetType().Name);
        }

        static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static string Head(byte[] b)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < b.Length && i < 4; i++) sb.Append(b[i].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
            return sb.ToString().TrimEnd();
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-76} {2}{3}", ok ? "ok  " : "⛔ ", what, got ?? "null",
                              ok ? string.Empty : "  вместо " + (expected ?? "null"));
            if (!ok) bad++;
        }
    }
}
