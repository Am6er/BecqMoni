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
    /// чтение без падения (`AMBER43`, полоса П89, 17.09.2026), а с полосы П96
    /// (17.09.2026) — ещё и ЛЕСТНИЦА чтения до поставочной раскладки.
    ///
    /// ЗАЧЕМ. Задача Amber 15.09.2026: «Падение приложения, если файл настроек
    /// расположения окон в UI поломался при сохранении. Основная задача -
    /// избежать поломки этого файла ExpertMode.xml». Библиотечный
    /// <c>SaveAsXml(имя)</c> сперва обрезал файл до нуля и лишь потом писал;
    /// голый <c>LoadFromXml</c> в <c>MainForm_Load</c> на пустом файле бросал
    /// <c>XmlException</c> из <c>OnLoad</c>, и приложение падало на каждом
    /// запуске. Починка П89 — <c>BecquerelMonitor.LayoutFile</c> (в память →
    /// <c>.tmp</c> → <c>File.Replace</c> с <c>.bak</c>; чтение под заслоном:
    /// сломанный → <c>.broken-&lt;дата&gt;</c>, поднять <c>.bak</c>, иначе
    /// умолчание) и тонкие вызовы в <c>MainForm</c>.
    ///
    /// ⛔ ОСТАТОК, найденный экраном 17.09.2026 (П95): без <c>.bak</c> — а это
    /// ровно случай Amber 15.09, первое падение, копии ещё нет — «умолчание»
    /// было ПУСТЫМ окном: ни панелей, ни вкладок. Решение Amber 17.09.2026,
    /// дословно: «Поставочная раскладка как умолчание». Исполнение П96 —
    /// лестница <c>LayoutFile.Read</c>: целевой файл → <c>.bak</c> →
    /// поставочный <c>config\layout\ExpertMode.xml</c> из каталога приложения
    /// (только если его путь ≠ целевому: в портативной сборке это один файл)
    /// → встроенная копия того же файла (EmbeddedResource) → пусто; файла нет
    /// вовсе — та же лестница со ступени поставочного, молча.
    ///
    /// ЧТО МЕРЯЕТСЯ (новая сборка, помощник с лестницей есть):
    ///
    ///   1. ЗАПИСЬ файлового ядра <c>LayoutFile.Write</c> подсаженным
    ///      сериализатором (П89, как было): (в) удачная вторая запись —
    ///      <c>.bak</c> побайтно равен прежнему файлу, <c>.tmp</c> убран; (б)
    ///      сериализатор, бросающий на полпути, и сериализатор, не пишущий
    ///      ничего, — целевой файл прежний побайтно; осиротевший <c>.tmp</c>
    ///      следующей записи не мешает.
    ///   2. ЧТЕНИЕ файлового ядра <c>LayoutFile.Read</c> подсаженным читателем
    ///      (поток; «BAD…» — негоден): прежние плечи П89 (нет файла; годный;
    ///      сломанный при годном <c>.bak</c>; сломанный при сломанном
    ///      <c>.bak</c>; сломанный без <c>.bak</c>) и новые ступени:
    ///      (а) сломанный без <c>.bak</c> + поставочный файл → поставочный,
    ///      слово называет его; (б) то же без поставочного файла → встроенная;
    ///      (в) поставочный и встроенная тоже биты → умолчание, в слове ВСЕ
    ///      причины; нет файла + встроенная → встроенная молча;
    ///      встроенной нет в сборке (null) → отказ с причиной, не тишина.
    ///   3. НАСТОЯЩАЯ ПАНЕЛЬ <c>MainForm.dockPanel1</c> без окна (П89, как
    ///      было): <c>Save</c> байт в байт = библиотечный <c>SaveAsXml(имя)</c>;
    ///      (а) пустой файл при <c>.bak</c> — поднята копия; обрезанный без
    ///      <c>.bak</c> — не бросает, отложен (⚠ теперь исход
    ///      <c>LoadedFromBuiltin</c>, а не <c>Default</c>: лестница дошла до
    ///      встроенной, а десериализатор-пустышка содержимого не даёт);
    ///      тонкие вызовы <c>MainForm.LoadLayoutXml</c> / <c>SaveLayoutXml</c>;
    ///      (б) отказ сериализации настоящей панели — файл прежний.
    ///   4. ЛЕСТНИЦА НА НАСТОЯЩЕЙ ПАНЕЛИ с десериализатором, дающим содержимое:
    ///      <c>SupplyPathFor</c> (от каталога exe; свой же путь → null —
    ///      портативная сборка); <c>BuiltinLayout</c> байт в байт = поставочный
    ///      файл дерева; (а) пустой файл, нет <c>.bak</c>, поставочный рядом →
    ///      панель получила ВСЁ содержимое поставочной (число <c>Contents</c> =
    ///      <c>Count</c> из файла); (б) без поставочного → встроенная, то же
    ///      содержимое; (в) поставочный бит + встроенная бита → пусто, обе
    ///      причины в слове; поставочный бит + встроенная годна → встроенная;
    ///      тонкий вызов <c>MainForm.LoadLayoutXml</c> на пустом файле без
    ///      <c>.bak</c> — встроенная (случай портативной установки Amber),
    ///      слово в потоке ошибок; тот же вызов с поставочным файлом, положенным
    ///      НА ВРЕМЯ по пути приложения (<c>SupplyPathFor</c>), — поставочная,
    ///      слово называет путь; файла нет вовсе — встроенная, слова НЕТ.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — та же проба на ПРЕЖНИХ сборках (помощник
    /// ищется отражением, потому проба собирается против любой):
    ///   * сборка П89 (`0f738825`, помощник есть, лестницы нет — нет
    ///     <c>Read(string, string, Action&lt;Stream&gt;, Func&lt;Stream&gt;)</c>):
    ///     (а) пустой файл + нет <c>.bak</c> + поставочный рядом и (б) то же без
    ///     поставочного ОБЯЗАНЫ дать <c>Default</c> и ПУСТУЮ панель — это и есть
    ///     дефект «умолчание = пустое окно». Код 1 — воспроизведён; 2 — нет
    ///     (контроль сам сломан);
    ///   * сборка до П89 (помощника нет): прежний путь — (а)
    ///     <c>LoadFromXml</c> пустого и обрезанного файла ОБЯЗАН бросить
    ///     <c>XmlException</c>; (б) <c>SaveAsXml(имя)</c> с отказом сериализации
    ///     ОБЯЗАН испортить годный файл (остаются 2 байта `FF FE`). Код 1 / 2.
    ///
    /// Ключи: <c>--dir=&lt;путь&gt;</c> — каталог опыта (умолчание
    /// <c>%TEMP%\BecqMoni.LayoutFileProbe</c>); <c>--supply=&lt;файл&gt;</c> —
    /// поставочная раскладка-эталон (умолчание — ищется вверх от каталога exe:
    /// <c>BecquerelMonitor\config\layout\ExpertMode.xml</c> дерева). Каталог
    /// раскладки Amber (<c>%AppData%\BecqMoni</c>) не трогается; в каталог exe
    /// пробы на время одного плеча кладётся <c>config\layout\ExpertMode.xml</c>
    /// и снимается в <c>finally</c>. Файлы подсадок остаются в каталоге опыта —
    /// улики для журнала.
    ///
    ///     layoutfileprobe --dir=D:\BqMoni_Claude\p96\layout_probe
    ///
    /// Ожидание на новой сборке: «ВСЕ СОШЛИСЬ», код 0.
    /// </summary>
    static class Program
    {
        static int bad;
        static string dir;
        static string supplyReference;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // Культура ЦЕЛИКОМ инвариантная (правило Amber 05.09.2026, `T245`).
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            dir = Path.Combine(Path.GetTempPath(), "BecqMoni.LayoutFileProbe");
            supplyReference = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal))
                {
                    dir = a.Substring("--dir=".Length).Trim('"');
                }
                else if (a.StartsWith("--supply=", StringComparison.Ordinal))
                {
                    supplyReference = a.Substring("--supply=".Length).Trim('"');
                }
            }
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
            Directory.CreateDirectory(dir);
            Console.WriteLine("каталог опыта: {0}", dir);
            if (supplyReference == null)
            {
                supplyReference = FindSupplyReference();
            }
            Console.WriteLine("поставочная раскладка-эталон: {0}", supplyReference ?? "НЕ НАЙДЕНА (--supply=)");

            Type layoutFile = typeof(MainForm).Assembly.GetType("BecquerelMonitor.LayoutFile");
            MethodInfo ladder = layoutFile == null ? null
                : layoutFile.GetMethod("Read", new[] { typeof(string), typeof(string), typeof(Action<Stream>), typeof(Func<Stream>) });
            Console.WriteLine("сборка приложения: {0}", typeof(MainForm).Assembly.Location);
            Console.WriteLine("помощник BecquerelMonitor.LayoutFile: {0}", layoutFile != null ? "ЕСТЬ" : "НЕТ (сборка до П89)");
            Console.WriteLine("лестница Read(string, string, Action<Stream>, Func<Stream>): {0}",
                              ladder != null ? "ЕСТЬ" : "НЕТ (сборка П89 без поставочной ступени)");
            Console.WriteLine("Application.ExecutablePath = {0}", Application.ExecutablePath);
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
                else if (ladder == null)
                {
                    code = P89BuildSection(layoutFile, mainForm, panel);
                }
                else
                {
                    WriteCoreSection(layoutFile);
                    ReadCoreSection(layoutFile);
                    RealPanelSection(layoutFile, mainForm, panel, deserialize);
                    LadderPanelSection(layoutFile, mainForm, panel);
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
        // 2. ЧТЕНИЕ — файловое ядро LayoutFile.Read(string, string, Action<Stream>, Func<Stream>)
        // ------------------------------------------------------------------

        static void ReadCoreSection(Type layoutFile)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. чтение: LayoutFile.Read подсаженным читателем (лестница) ===");
            MethodInfo read = layoutFile.GetMethod("Read", new[] { typeof(string), typeof(string), typeof(Action<Stream>), typeof(Func<Stream>) });
            MethodInfo describe = layoutFile.GetMethod("Describe");
            MethodInfo describeAbsent = layoutFile.GetMethod("DescribeAbsent");
            Same("метод Read(string, string, Action<Stream>, Func<Stream>) есть", true, read != null);
            Same("метод Describe есть", true, describe != null);
            Same("метод DescribeAbsent есть", true, describeAbsent != null);
            string target = Path.Combine(dir, "core_read", "ExpertMode.xml");
            string bak = target + ".bak";
            string supply = Path.Combine(dir, "core_read", "app", "config", "layout", "ExpertMode.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            Directory.CreateDirectory(Path.GetDirectoryName(supply));

            // Подсаженный читатель: содержимое, начинающееся с «BAD», негодно.
            // Имя ступени — из потока: файл называется, встроенная — «builtin».
            List<string> seen = new List<string>();
            Action<Stream> loader = delegate (Stream s)
            {
                FileStream fs = s as FileStream;
                seen.Add(fs != null ? Path.GetFileName(fs.Name) : "builtin");
                string text = new StreamReader(s, Encoding.UTF8).ReadToEnd();
                if (text.StartsWith("BAD", StringComparison.Ordinal))
                {
                    throw new XmlException("подсаженный отказ разбора: " + (fs != null ? Path.GetFileName(fs.Name) : "builtin"));
                }
            };
            Func<Stream> goodBuiltin = delegate { return new MemoryStream(Encoding.UTF8.GetBytes("GOOD builtin")); };
            Func<Stream> badBuiltin = delegate { return new MemoryStream(Encoding.UTF8.GetBytes("BAD builtin")); };
            Func<Stream> noBuiltin = delegate { return null; };

            // 2.1 файла нет, ступеней нет — как в П89: Absent, не отказ, читатель не звался.
            object o = read.Invoke(null, new object[] { target, null, loader, null });
            Same("2.1 нет файла, ступеней нет: Kind = Absent", "Absent", Kind(o));
            Same("2.1 нет файла: не отказ", false, Failed(o));
            Same("2.1 нет файла: читатель не звался", 0, seen.Count);
            Same("2.1 нет файла: Describe пуст", "", (string)describe.Invoke(null, new[] { o }));
            Same("2.1 нет файла: DescribeAbsent пуст (ничего не отказало)", "", (string)describeAbsent.Invoke(null, new[] { o }));

            // 2.1б файла нет + встроенная: поднята молча.
            seen.Clear();
            o = read.Invoke(null, new object[] { target, supply, loader, goodBuiltin });
            Same("2.1б нет файла + встроенная: Kind = LoadedFromBuiltin", "LoadedFromBuiltin", Kind(o));
            Same("2.1б не отказ (файла и не ждали)", false, Failed(o));
            Same("2.1б поставочного не было", false, (bool)Get(o, "SupplyExisted"));
            Same("2.1б читатель звался один раз, встроенная", "builtin", string.Join("|", seen.ToArray()));
            Same("2.1б Describe пуст — окна нет", "", (string)describe.Invoke(null, new[] { o }));
            Same("2.1б DescribeAbsent пуст — ничего не отказало", "", (string)describeAbsent.Invoke(null, new[] { o }));

            // 2.1в файла нет + поставочный бит + встроенная годна: встроенная, а отказ поставочного — строкой без окон.
            File.WriteAllText(supply, "BAD supply");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, supply, loader, goodBuiltin });
            Same("2.1в нет файла, поставочный бит: Kind = LoadedFromBuiltin", "LoadedFromBuiltin", Kind(o));
            Same("2.1в не отказ", false, Failed(o));
            Same("2.1в читатель: поставочный, затем встроенная", "ExpertMode.xml|builtin", string.Join("|", seen.ToArray()));
            Same("2.1в причина поставочного названа", true, ((string)Get(o, "SupplyError") ?? "").Contains("подсаженный отказ разбора"));
            string note = (string)describeAbsent.Invoke(null, new[] { o });
            Console.WriteLine("      строка без окон: {0}", note);
            Same("2.1в DescribeAbsent называет поставочный и причину", true,
                 note.Contains(Path.GetFullPath(supply)) && note.Contains("подсаженный отказ разбора"));
            File.Delete(supply);

            // 2.2 годный файл.
            File.WriteAllText(target, "GOOD 1");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, supply, loader, goodBuiltin });
            Same("2.2 годный: Kind = Loaded", "Loaded", Kind(o));
            Same("2.2 годный: не отказ", false, Failed(o));
            Same("2.2 годный: читатель звался один раз", 1, seen.Count);
            Same("2.2 годный: Describe пуст", "", (string)describe.Invoke(null, new[] { o }));

            // 2.3 сломанный при годном .bak (поставочный и встроенная есть, но до них не доходит).
            File.WriteAllText(target, "BAD main");
            File.WriteAllText(bak, "GOOD bak");
            File.WriteAllText(supply, "GOOD supply");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, supply, loader, goodBuiltin });
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
            Same("2.3 слово НЕ называет поставочный (до него не дошло)", false, word.Contains(Path.GetFullPath(supply)));

            // 2.4 сломанный при сломанном .bak, ступеней нет — умолчание с обеими причинами (П89).
            Thread.Sleep(1100); // метка времени .broken- с точностью до секунды — не столкнуться с 2.3
            File.WriteAllText(target, "BAD main 2");
            File.WriteAllText(bak, "BAD bak 2");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, null, loader, null });
            Same("2.4 сломанный+сломанный .bak: Kind = Default", "Default", Kind(o));
            Same("2.4 отказ назван", true, Failed(o));
            Same("2.4 .bak был", true, (bool)Get(o, "BackupExisted"));
            Same("2.4 причина .bak названа", true, ((string)Get(o, "BackupError") ?? "").Contains("ExpertMode.xml.bak"));
            Same("2.4 основного файла на месте нет (отложен)", false, File.Exists(target));
            word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("2.4 слово называет причину основного файла", true, word.Contains("подсаженный отказ разбора: ExpertMode.xml"));
            Same("2.4 слово называет причину копии", true, word.Contains("подсаженный отказ разбора: ExpertMode.xml.bak"));

            // 2.5 сломанный без .bak, ступеней нет — умолчание (П89).
            Thread.Sleep(1100);
            File.Delete(bak);
            File.WriteAllText(target, "BAD main 3");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, null, loader, null });
            Same("2.5 сломанный без .bak: Kind = Default", "Default", Kind(o));
            Same("2.5 .bak не было", false, (bool)Get(o, "BackupExisted"));
            Same("2.5 читатель звался один раз", 1, seen.Count);
            word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("2.5 слово непустое", true, word.Length > 0);

            // 2.6 (а) сломанный без .bak + поставочный файл: поставочная, слово называет его.
            Thread.Sleep(1100);
            File.WriteAllText(target, "BAD main 4");
            File.WriteAllText(supply, "GOOD supply");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, supply, loader, goodBuiltin });
            Same("2.6 (а) сломанный без .bak + поставочный: Kind = LoadedFromSupply", "LoadedFromSupply", Kind(o));
            Same("2.6 (а) отказ назван", true, Failed(o));
            Same("2.6 (а) читатель: основной, затем поставочный (не .bak, не встроенная)", "ExpertMode.xml|ExpertMode.xml", string.Join("|", seen.ToArray()));
            Same("2.6 (а) поставочный был", true, (bool)Get(o, "SupplyExisted"));
            Same("2.6 (а) сломанный отложен", true, Get(o, "BrokenPath") != null && File.Exists((string)Get(o, "BrokenPath")));
            Same("2.6 (а) поставочный на место основного НЕ скопирован (закрытие запишет)", false, File.Exists(target));
            word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("2.6 (а) слово называет поставочный файл", true, word.Contains(Path.GetFullPath(supply)));
            Same("2.6 (а) слово говорит «поставочная»", true, SaysSupply(word));
            Same("2.6 (а) слово НЕ говорит «встроенная»", false, SaysBuiltin(word));

            // 2.7 (б) сломанный без .bak, поставочного файла нет — встроенная.
            Thread.Sleep(1100);
            File.Delete(supply);
            File.WriteAllText(target, "BAD main 5");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, supply, loader, goodBuiltin });
            Same("2.7 (б) без поставочного файла: Kind = LoadedFromBuiltin", "LoadedFromBuiltin", Kind(o));
            Same("2.7 (б) читатель: основной, затем встроенная", "ExpertMode.xml|builtin", string.Join("|", seen.ToArray()));
            Same("2.7 (б) поставочного не было", false, (bool)Get(o, "SupplyExisted"));
            word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("2.7 (б) слово говорит «встроенная»", true, SaysBuiltin(word));
            Same("2.7 (б) слово НЕ называет отсутствующий поставочный", false, word.Contains(Path.GetFullPath(supply)));

            // 2.8 (в) сломанный без .bak, поставочный бит, встроенная бита — умолчание, ВСЕ причины в слове.
            Thread.Sleep(1100);
            File.WriteAllText(target, "BAD main 6");
            File.WriteAllText(supply, "BAD supply 6");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, supply, loader, badBuiltin });
            Same("2.8 (в) всё бито: Kind = Default", "Default", Kind(o));
            Same("2.8 (в) читатель: основной, поставочный, встроенная", "ExpertMode.xml|ExpertMode.xml|builtin", string.Join("|", seen.ToArray()));
            Same("2.8 (в) причина поставочного названа", true, ((string)Get(o, "SupplyError") ?? "").Contains("подсаженный отказ разбора: ExpertMode.xml"));
            Same("2.8 (в) причина встроенной названа", true, ((string)Get(o, "BuiltinError") ?? "").Contains("подсаженный отказ разбора: builtin"));
            word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("2.8 (в) слово называет основной файл", true, word.Contains(Path.GetFullPath(target)));
            Same("2.8 (в) слово называет поставочный и его причину", true,
                 word.Contains(Path.GetFullPath(supply)) && word.Contains("подсаженный отказ разбора: ExpertMode.xml"));
            Same("2.8 (в) слово называет причину встроенной", true, word.Contains("подсаженный отказ разбора: builtin"));
            Same("2.8 (в) слово говорит «пусто»/«View»", true, SaysEmpty(word));

            // 2.9 сломанный + сломанный .bak + поставочный годен: поставочная, в слове отказ .bak и поставочный.
            Thread.Sleep(1100);
            File.WriteAllText(target, "BAD main 7");
            File.WriteAllText(bak, "BAD bak 7");
            File.WriteAllText(supply, "GOOD supply 7");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, supply, loader, goodBuiltin });
            Same("2.9 .bak бит, поставочный годен: Kind = LoadedFromSupply", "LoadedFromSupply", Kind(o));
            Same("2.9 читатель: основной, .bak, поставочный", "ExpertMode.xml|ExpertMode.xml.bak|ExpertMode.xml", string.Join("|", seen.ToArray()));
            word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("2.9 слово называет причину .bak", true, word.Contains("подсаженный отказ разбора: ExpertMode.xml.bak"));
            Same("2.9 слово называет поставочный", true, word.Contains(Path.GetFullPath(supply)));
            File.Delete(bak);
            File.Delete(supply);

            // 2.10 встроенной в сборке нет (поток null): отказ с причиной, не тишина.
            Thread.Sleep(1100);
            File.WriteAllText(target, "BAD main 8");
            seen.Clear();
            o = read.Invoke(null, new object[] { target, supply, loader, noBuiltin });
            Same("2.10 встроенной нет (null): Kind = Default", "Default", Kind(o));
            Same("2.10 причина встроенной названа (ресурса нет)", true, ((string)Get(o, "BuiltinError") ?? "").Contains("embedded layout resource is missing"));
            word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("2.10 слово называет причину встроенной", true, word.Contains("embedded layout resource is missing"));

            string[] brokenFiles = Directory.GetFiles(Path.GetDirectoryName(target), "ExpertMode.xml.broken-*");
            Same("2. отложенных улик восемь (2.3–2.10)", 8, brokenFiles.Length);
        }

        // ------------------------------------------------------------------
        // 3. НАСТОЯЩАЯ ПАНЕЛЬ MainForm.dockPanel1 без окна (П89)
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
            //     ⚠ П96: исход теперь LoadedFromBuiltin, а не Default — лестница идёт до встроенной
            //     копии (поставочного файла рядом с пробой нет); десериализатор-пустышка содержимого
            //     не даёт, панель остаётся пустой (пустышки библиотека снимает).
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
            Same("3.3 (а) обрезанный файл: Kind = LoadedFromBuiltin (копии нет, поставочного рядом нет)", "LoadedFromBuiltin", o != null ? Kind(o) : null);
            Console.WriteLine("      причина: {0}", o != null ? Get(o, "TargetError") : null);
            broken = o != null ? (string)Get(o, "BrokenPath") : null;
            Same("3.3 (а) обрезанный отложен, улика = обрезанные байты", true,
                 broken != null && File.Exists(broken) && Equal(half, File.ReadAllBytes(broken)));
            Same("3.3 (а) основного файла на месте нет", false, File.Exists(target));
            Same("3.3 (а) панель с десериализатором-пустышкой пуста", 0, panel.Contents.Count);
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
            Same("3.6 после снятия подсадки панель пуста", 0, panel.Contents.Count);
        }

        // ------------------------------------------------------------------
        // 4. ЛЕСТНИЦА НА НАСТОЯЩЕЙ ПАНЕЛИ — поставочная раскладка как умолчание (П96)
        // ------------------------------------------------------------------

        static void LadderPanelSection(Type layoutFile, MainForm mainForm, DockPanel panel)
        {
            Console.WriteLine();
            Console.WriteLine("=== 4. лестница на настоящей панели: поставочная раскладка как умолчание ===");
            MethodInfo load3 = layoutFile.GetMethod("Load", new[] { typeof(DockPanel), typeof(string), typeof(DeserializeDockContent) });
            MethodInfo load5 = layoutFile.GetMethod("Load", new[] { typeof(DockPanel), typeof(string), typeof(DeserializeDockContent), typeof(string), typeof(Func<Stream>) });
            MethodInfo supplyPathFor = layoutFile.GetMethod("SupplyPathFor", new[] { typeof(string) });
            MethodInfo builtinLayout = layoutFile.GetMethod("BuiltinLayout", Type.EmptyTypes);
            MethodInfo describe = layoutFile.GetMethod("Describe");
            Same("метод Load(DockPanel, string, DeserializeDockContent, string, Func<Stream>) есть", true, load5 != null);
            Same("метод SupplyPathFor(string) есть", true, supplyPathFor != null);
            Same("метод BuiltinLayout() есть", true, builtinLayout != null);
            Same("поставочная раскладка-эталон найдена", true, supplyReference != null && File.Exists(supplyReference));
            if (supplyReference == null || !File.Exists(supplyReference))
            {
                Console.WriteLine("      ⛔ без эталона раздел 4 мерить нечем — задайте --supply=");
                return;
            }
            byte[] reference = File.ReadAllBytes(supplyReference);
            int expectedContents = ContentsCount(supplyReference);
            Console.WriteLine("      эталон: {0} байт, Contents Count=\"{1}\"", reference.Length, expectedContents);
            Same("эталон: содержимого больше нуля (иначе плечу нечего мерить)", true, expectedContents > 0);

            string layoutDir = Path.Combine(dir, "ladder");
            Directory.CreateDirectory(layoutDir);
            string target = Path.Combine(layoutDir, "ExpertMode.xml");
            string bak = target + ".bak";
            string supply = Path.Combine(layoutDir, "app", "config", "layout", "ExpertMode.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(supply));
            DeserializeDockContent makeContent = MakeContent;
            Func<Stream> realBuiltin = delegate { return (Stream)builtinLayout.Invoke(null, null); };

            // 4.1 SupplyPathFor: от каталога exe; свой же путь → null (портативная сборка).
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            string appSupply = Path.Combine(exeDir, "config", "layout", "ExpertMode.xml");
            string computed = (string)supplyPathFor.Invoke(null, new object[] { target });
            Console.WriteLine("      SupplyPathFor(опыт) = {0}", computed ?? "null");
            Same("4.1 SupplyPathFor(чужой целевой) = <каталог exe>\\config\\layout\\ExpertMode.xml", true,
                 computed != null && string.Equals(Path.GetFullPath(computed), Path.GetFullPath(appSupply), StringComparison.OrdinalIgnoreCase));
            Same("4.1 SupplyPathFor(сам поставочный путь) = null — портативная сборка", null,
                 supplyPathFor.Invoke(null, new object[] { appSupply }));
            Same("4.1 SupplyPathFor(тот же путь другим регистром) = null", null,
                 supplyPathFor.Invoke(null, new object[] { appSupply.ToUpperInvariant() }));
            Same("4.1 поставочного файла рядом с пробой нет (иначе плечи (б) мерят не то)", false, File.Exists(appSupply));

            // 4.2 BuiltinLayout: поток есть, байт в байт = поставочный файл дерева.
            byte[] builtinBytes = null;
            using (Stream s = (Stream)builtinLayout.Invoke(null, null))
            {
                Same("4.2 BuiltinLayout() даёт поток (ресурс в сборке есть)", true, s != null);
                if (s != null)
                {
                    using (MemoryStream m = new MemoryStream()) { s.CopyTo(m); builtinBytes = m.ToArray(); }
                }
            }
            Console.WriteLine("      встроенная: {0} байт", builtinBytes != null ? builtinBytes.Length : -1);
            Same("4.2 встроенная байт в байт = поставочный файл дерева", true, builtinBytes != null && Equal(reference, builtinBytes));

            // 4.3 (а) пустой файл, нет .bak, поставочный рядом (подсажен) — панель получила содержимое.
            ClearPanel(panel);
            File.WriteAllBytes(target, new byte[0]);
            if (File.Exists(bak)) File.Delete(bak);
            File.WriteAllBytes(supply, reference);
            object o = Invoke(load5, panel, target, makeContent, supply, realBuiltin);
            Same("4.3 (а) пустой файл без .bak + поставочный: Kind = LoadedFromSupply", "LoadedFromSupply", Kind(o));
            Same("4.3 (а) отказ назван (пустой файл)", true, Failed(o));
            Same("4.3 (а) причина — XmlException", true, ((string)Get(o, "TargetError") ?? "").Contains("XmlException"));
            Same("4.3 (а) панель получила содержимое поставочной (Contents)", expectedContents, panel.Contents.Count);
            Same("4.3 (а) панели построены (Panes > 0)", true, panel.Panes.Count > 0);
            Console.WriteLine("      Contents={0}, Panes={1}, FloatWindows={2}", panel.Contents.Count, panel.Panes.Count, panel.FloatWindows.Count);
            string word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("4.3 (а) слово называет поставочный файл", true, word.Contains(Path.GetFullPath(supply)));
            Same("4.3 (а) слово говорит «поставочная»", true, SaysSupply(word));
            Same("4.3 (а) сломанный отложен", true, Get(o, "BrokenPath") != null);
            Same("4.3 (а) поставочный на место основного не скопирован", false, File.Exists(target));

            // 4.4 (б) то же без поставочного файла — встроенная копия, то же содержимое.
            ClearPanel(panel);
            Same("4.4 панель очищена перед плечом", 0, panel.Contents.Count);
            Thread.Sleep(1100);
            File.Delete(supply);
            File.WriteAllBytes(target, new byte[0]);
            o = Invoke(load5, panel, target, makeContent, supply, realBuiltin);
            Same("4.4 (б) пустой файл без .bak, без поставочного: Kind = LoadedFromBuiltin", "LoadedFromBuiltin", Kind(o));
            Same("4.4 (б) панель получила содержимое встроенной (Contents)", expectedContents, panel.Contents.Count);
            word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("4.4 (б) слово говорит «встроенная»", true, SaysBuiltin(word));

            // 4.5 (в) поставочный бит + встроенная бита — пусто, обе причины в слове.
            ClearPanel(panel);
            Thread.Sleep(1100);
            byte[] halfRef = new byte[reference.Length / 2];
            Array.Copy(reference, halfRef, halfRef.Length);
            File.WriteAllBytes(supply, halfRef);
            File.WriteAllBytes(target, new byte[0]);
            Func<Stream> brokenBuiltin = delegate { return new MemoryStream(halfRef); };
            o = Invoke(load5, panel, target, makeContent, supply, brokenBuiltin);
            Same("4.5 (в) поставочный бит + встроенная бита: Kind = Default", "Default", Kind(o));
            Same("4.5 (в) панель пуста", 0, panel.Contents.Count);
            Same("4.5 (в) причина поставочного — XmlException", true, ((string)Get(o, "SupplyError") ?? "").Contains("XmlException"));
            Same("4.5 (в) причина встроенной — XmlException", true, ((string)Get(o, "BuiltinError") ?? "").Contains("XmlException"));
            word = (string)describe.Invoke(null, new[] { o });
            Console.WriteLine("      слово:\n        {0}", word.Replace("\n", "\n        "));
            Same("4.5 (в) слово называет поставочный", true, word.Contains(Path.GetFullPath(supply)));
            Same("4.5 (в) слово называет причины поставочного и встроенной (два XmlException сверх основного)", true, Count(word, "XmlException") >= 3);
            Same("4.5 (в) слово говорит «пусто»/«View»", true, SaysEmpty(word));

            // 4.5б поставочный бит, встроенная годна — встроенная (битый поставочный лестницу не рвёт).
            ClearPanel(panel);
            Thread.Sleep(1100);
            File.WriteAllBytes(target, new byte[0]);
            o = Invoke(load5, panel, target, makeContent, supply, realBuiltin);
            Same("4.5б поставочный бит, встроенная годна: Kind = LoadedFromBuiltin", "LoadedFromBuiltin", Kind(o));
            Same("4.5б панель получила содержимое встроенной", expectedContents, panel.Contents.Count);
            word = (string)describe.Invoke(null, new[] { o });
            Same("4.5б слово называет битый поставочный и говорит «встроенная»", true, word.Contains(Path.GetFullPath(supply)) && SaysBuiltin(word));
            File.Delete(supply);

            // 4.6 тонкий вызов MainForm.LoadLayoutXml без окон: пустой файл, нет .bak, поставочного
            //     рядом с exe нет — встроенная (случай портативной установки Amber: файл = поставочный).
            MethodInfo loadLayout = typeof(MainForm).GetMethod("LoadLayoutXml", BindingFlags.Instance | BindingFlags.NonPublic);
            SetField(mainForm, "m_deserializeDockContent", makeContent);
            ClearPanel(panel);
            Thread.Sleep(1100);
            File.WriteAllBytes(target, new byte[0]);
            string err = CaptureError(delegate { loadLayout.Invoke(mainForm, new object[] { target, false }); });
            Console.WriteLine("      поток ошибок: {0}", err.Trim());
            Same("4.6 тонкий вызов, встроенная: панель получила содержимое", expectedContents, panel.Contents.Count);
            Same("4.6 слово в потоке ошибок называет файл", true, err.Contains(Path.GetFullPath(target)));
            Same("4.6 слово говорит «встроенная»", true, SaysBuiltin(err));

            // 4.7 тонкий вызов с поставочным файлом, положенным НА ВРЕМЯ по пути приложения — ClickOnce.
            ClearPanel(panel);
            Thread.Sleep(1100);
            File.WriteAllBytes(target, new byte[0]);
            Directory.CreateDirectory(Path.GetDirectoryName(appSupply));
            try
            {
                File.WriteAllBytes(appSupply, reference);
                err = CaptureError(delegate { loadLayout.Invoke(mainForm, new object[] { target, false }); });
            }
            finally
            {
                if (File.Exists(appSupply)) File.Delete(appSupply);
                TryRemoveEmptyDirs(exeDir, Path.GetDirectoryName(appSupply));
            }
            Console.WriteLine("      поток ошибок: {0}", err.Trim());
            Same("4.7 тонкий вызов, поставочный по пути приложения: панель получила содержимое", expectedContents, panel.Contents.Count);
            Same("4.7 слово называет поставочный по пути приложения", true, err.Contains(Path.GetFullPath(appSupply)));
            Same("4.7 слово говорит «поставочная»", true, SaysSupply(err));
            Same("4.7 подсадка снята: файла рядом с пробой нет", false, File.Exists(appSupply));
            Same("4.7 подсадка снята: каталога config\\layout рядом с пробой нет", false, Directory.Exists(Path.GetDirectoryName(appSupply)));

            // 4.8 файла нет вовсе, .bak нет — встроенная молча: не отказ, слова нет.
            ClearPanel(panel);
            if (File.Exists(target)) File.Delete(target);
            if (File.Exists(bak)) File.Delete(bak);
            o = Invoke(load3, panel, target, makeContent);
            Same("4.8 файла нет: Kind = LoadedFromBuiltin", "LoadedFromBuiltin", Kind(o));
            Same("4.8 файла нет: не отказ", false, Failed(o));
            Same("4.8 файла нет: Describe пуст", "", (string)describe.Invoke(null, new[] { o }));
            Same("4.8 файла нет: панель получила содержимое встроенной", expectedContents, panel.Contents.Count);
            ClearPanel(panel);
            err = CaptureError(delegate { loadLayout.Invoke(mainForm, new object[] { target, false }); });
            Same("4.8 тонкий вызов без файла: поток ошибок пуст (слова нет)", "", err.Trim());
            Same("4.8 тонкий вызов без файла: панель получила содержимое", expectedContents, panel.Contents.Count);
            ClearPanel(panel);
        }

        // ------------------------------------------------------------------
        // СБОРКА П89 — положительный контроль остатка П96 («умолчание = пустое окно»)
        // ------------------------------------------------------------------

        static int P89BuildSection(Type layoutFile, MainForm mainForm, DockPanel panel)
        {
            Console.WriteLine();
            Console.WriteLine("=== СБОРКА П89: помощник без поставочной ступени — умолчание = пустое окно ===");
            MethodInfo load3 = layoutFile.GetMethod("Load", new[] { typeof(DockPanel), typeof(string), typeof(DeserializeDockContent) });
            if (load3 == null || supplyReference == null || !File.Exists(supplyReference))
            {
                Console.WriteLine("⛔ КОНТРОЛЬ НЕ СОБРАТЬ: Load(DockPanel, string, DeserializeDockContent) {0}, эталон {1}. Код 2.",
                                  load3 != null ? "есть" : "НЕТ", supplyReference ?? "НЕ НАЙДЕН");
                return 2;
            }
            byte[] reference = File.ReadAllBytes(supplyReference);
            int expectedContents = ContentsCount(supplyReference);
            string layoutDir = Path.Combine(dir, "ladder_p89");
            Directory.CreateDirectory(layoutDir);
            string target = Path.Combine(layoutDir, "ExpertMode.xml");
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            string appSupply = Path.Combine(exeDir, "config", "layout", "ExpertMode.xml");
            DeserializeDockContent makeContent = MakeContent;
            int reproduced = 0;

            // (а) пустой файл + нет .bak + поставочный рядом с приложением (на время).
            File.WriteAllBytes(target, new byte[0]);
            Directory.CreateDirectory(Path.GetDirectoryName(appSupply));
            object o;
            try
            {
                File.WriteAllBytes(appSupply, reference);
                o = Invoke(load3, panel, target, makeContent);
            }
            finally
            {
                if (File.Exists(appSupply)) File.Delete(appSupply);
                TryRemoveEmptyDirs(exeDir, Path.GetDirectoryName(appSupply));
            }
            Console.WriteLine("  (а) пустой файл, нет .bak, поставочный рядом: Kind = {0}, Contents = {1} (у поставочной {2})",
                              Kind(o), panel.Contents.Count, expectedContents);
            if (Kind(o) == "Default" && panel.Contents.Count == 0) reproduced++;

            // (б) то же без поставочного.
            ClearPanel(panel);
            Thread.Sleep(1100);
            File.WriteAllBytes(target, new byte[0]);
            o = Invoke(load3, panel, target, makeContent);
            Console.WriteLine("  (б) пустой файл, нет .bak, поставочного нет: Kind = {0}, Contents = {1}", Kind(o), panel.Contents.Count);
            if (Kind(o) == "Default" && panel.Contents.Count == 0) reproduced++;

            Console.WriteLine();
            if (reproduced == 2)
            {
                Console.WriteLine("СБОРКА П89: остаток AMBER43 воспроизведён по обоим плечам — «умолчание» = пустая панель. Код 1.");
                return 1;
            }
            Console.WriteLine("⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ НЕ СРАБОТАЛ: воспроизведено {0} из 2 — контролю верить нельзя. Код 2.", reproduced);
            return 2;
        }

        // ------------------------------------------------------------------
        // СБОРКА ДО П89 — положительный контроль прежнего пути
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

        /// <summary>Десериализатор, дающий содержимое на КАЖДУЮ строку раскладки — чтобы панель наполнилась.</summary>
        static IDockContent MakeContent(string persistString)
        {
            DockContent content = new DockContent();
            content.Text = persistString;
            return content;
        }

        /// <summary>Снять с панели всё содержимое (и его окна), чтобы следующее чтение не упёрлось в «уже заполнена».</summary>
        static void ClearPanel(DockPanel panel)
        {
            List<IDockContent> contents = new List<IDockContent>();
            foreach (IDockContent c in panel.Contents) contents.Add(c);
            foreach (IDockContent c in contents)
            {
                c.DockHandler.DockPanel = null;
                c.DockHandler.Form.Dispose();
            }
            List<FloatWindow> floats = new List<FloatWindow>();
            foreach (FloatWindow f in panel.FloatWindows) floats.Add(f);
            foreach (FloatWindow f in floats) f.Dispose();
        }

        /// <summary>Поставочный файл дерева: вверх от каталога exe до BecquerelMonitor\config\layout\ExpertMode.xml.</summary>
        static string FindSupplyReference()
        {
            string d = Path.GetDirectoryName(Application.ExecutablePath);
            for (int i = 0; i < 8 && d != null; i++)
            {
                string candidate = Path.Combine(d, "BecquerelMonitor", "config", "layout", "ExpertMode.xml");
                if (File.Exists(candidate)) return candidate;
                d = Path.GetDirectoryName(d);
            }
            return null;
        }

        /// <summary>Count из &lt;DockPanel&gt;&lt;Contents Count="…"&gt; файла раскладки — ожидание, не константа.</summary>
        static int ContentsCount(string path)
        {
            using (XmlReader r = XmlReader.Create(path))
            {
                while (r.Read())
                {
                    if (r.NodeType == XmlNodeType.Element && r.Name == "Contents" && r.Depth == 1)
                    {
                        return int.Parse(r.GetAttribute("Count"), CultureInfo.InvariantCulture);
                    }
                }
            }
            return -1;
        }

        static void TryRemoveEmptyDirs(string root, string leaf)
        {
            try
            {
                string d = leaf;
                while (d != null && d.Length > root.Length && Directory.Exists(d)
                       && Directory.GetFileSystemEntries(d).Length == 0)
                {
                    Directory.Delete(d);
                    d = Path.GetDirectoryName(d);
                }
            }
            catch (Exception)
            {
            }
        }

        static string CaptureError(Action action)
        {
            using (StringWriter capture = new StringWriter())
            {
                TextWriter old = Console.Error;
                Console.SetError(capture);
                try { action(); }
                catch (TargetInvocationException ex) { Console.WriteLine("      бросил: {0}", ex.InnerException); bad++; }
                finally { Console.SetError(old); }
                return capture.ToString();
            }
        }

        static object Invoke(MethodInfo method, params object[] args)
        {
            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException ex)
            {
                Console.WriteLine("      бросил: {0}", ex.InnerException);
                bad++;
                return null;
            }
        }

        static bool SaysSupply(string word)
        {
            return word.IndexOf("supplied", StringComparison.OrdinalIgnoreCase) >= 0
                || word.IndexOf("поставочн", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool SaysBuiltin(string word)
        {
            return word.IndexOf("built into", StringComparison.OrdinalIgnoreCase) >= 0
                || word.IndexOf("встроен", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool SaysEmpty(string word)
        {
            return word.IndexOf("empty arrangement", StringComparison.OrdinalIgnoreCase) >= 0
                || word.IndexOf("пустая расстановка", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static int Count(string text, string piece)
        {
            int n = 0;
            for (int i = text.IndexOf(piece, StringComparison.Ordinal); i >= 0; i = text.IndexOf(piece, i + piece.Length, StringComparison.Ordinal)) n++;
            return n;
        }

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
            if (outcome == null) return null;
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
