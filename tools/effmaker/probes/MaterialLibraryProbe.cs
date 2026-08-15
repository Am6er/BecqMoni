using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

/// <summary>
/// E20: заводится ли своё вещество БЕЗ пересборки — и доживает ли оно до
/// следующего запуска.
///
/// Зачем проба. Правка живёт в двух местах, и оба молчаливые: хранилище
/// (`GeometryMaterialStore`) и форма (`GeometryMaterialEditorForm`). Ошибка
/// здесь того же сорта, ради которого строка и заведена, — вещество, которое
/// человек считает своим, а расчёт берёт чужое: ровно так проба Lu₂O₃ осталась
/// воздухом и завысила кривую AS80x80 в 2.6 раза (`E19`). Поэтому проверяются
/// ЗНАЧЕНИЯ — доли элементов, содержимое файла, текст отказа, — а не то, что
/// «код отработал без исключения».
///
/// Семь случаев:
///   (а) файла нет            -> библиотека равна ВШИТОМУ засеву;
///   (б) круг записи-чтения   -> своё вещество переживает перечитывание;
///   (в) смесь                -> доли складываются с весами, и «1 и 1»
///                               равно «50 и 50»;
///   (г) удаление вшитого     -> держится, а не возвращается при сведении;
///   (д) кольцо в составе     -> названо, и расчёт не виснет;
///   (е) редактор             -> добавляет, показывает состав, отказывает по
///                               дубликату имени и по нулевой плотности;
///   (ж) сохранение из формы  -> файл на диске, библиотека перечитана.
///
/// ⚠ Пишет ТОЛЬКО в свой временный каталог: конфиг BecqMoni — на чтение. Перед
/// первой записью путь проверяется, и при попадании в хранилище пользователя
/// проба отказывает, а не «на всякий случай продолжает».
///
/// Сборка — `probes/build_all.ps1`. Запуск без ключей.
/// </summary>
static class MaterialLibraryProbe
{
    static string workdir;

    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        // Своя рабочая копия конфигурации. Библиотека веществ ложится в
        // `config\` ТЕКУЩЕГО каталога, а проба его и правит — значит, каталог
        // обязан быть свой: поставочный `config\` репозитория не место для
        // «Probe sand».
        //
        // Копия делается ДО подъёма менеджеров: в пустом каталоге
        // `GlobalConfigManager` не находит своего файла и показывает окно с
        // «ОК» — проба виснет молча, без единой строки вывода. Ровно так она и
        // повисла в первый запуск.
        if (!Directory.Exists("config"))
        {
            Console.WriteLine("ОТКАЗ: рядом нет каталога `config`.");
            Console.WriteLine("       Пробу запускают ИЗ КОРНЯ репозитория:");
            Console.WriteLine("       .\\tools\\effmaker\\probes\\build\\MaterialLibraryProbe.exe");
            Console.WriteLine("РАЗОШЛОСЬ");
            return 2;
        }

        workdir = Path.Combine(Path.GetTempPath(),
                               "MaterialLibraryProbe-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        CopyTree("config", Path.Combine(workdir, "config"));
        Directory.SetCurrentDirectory(workdir);

        DeviceType.InitializeDeviceTypes();
        GlobalConfigManager.GetInstance();

        if (!SafeToWrite())
        {
            return 2;
        }

        bool ok = true;
        try
        {
            ok &= CaseSeed();
            ok &= CaseRoundTrip();
            ok &= CaseMixture();
            ok &= CaseRemoveSeeded();
            ok &= CaseCycle();
            ok &= CaseEditor();
        }
        finally
        {
            try
            {
                Directory.SetCurrentDirectory(Path.GetTempPath());
                Directory.Delete(workdir, true);
            }
            catch (Exception)
            {
                // Мусор в TEMP — не повод объявлять проверку провалившейся.
            }
        }

        Console.WriteLine();
        Console.WriteLine(ok ? "СОШЛОСЬ" : "РАЗОШЛОСЬ");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Куда ляжет библиотека. Если не в наш временный каталог — не писать: у
    /// пользователя там его собственные вещества, и затереть их проба права не
    /// имеет.
    /// </summary>
    static bool SafeToWrite()
    {
        string path = Path.GetFullPath(GeometryMaterialStore.FilePath);
        Console.WriteLine("файл библиотеки: {0}", path);
        bool inside = path.StartsWith(Path.GetFullPath(workdir), StringComparison.OrdinalIgnoreCase);
        if (!inside)
        {
            Console.WriteLine("ОТКАЗ: путь ведёт не в каталог пробы, а в хранилище пользователя.");
            Console.WriteLine("       Конфиг BecqMoni — только на чтение; проба не пишет туда ничего.");
            Console.WriteLine("РАЗОШЛОСЬ");
        }

        return inside;
    }

    // ----------------------------------------------------------------------
    // (а) файла нет — библиотека равна вшитому засеву
    // ----------------------------------------------------------------------
    static bool CaseSeed()
    {
        Console.WriteLine();
        Console.WriteLine("== (а) файла нет: библиотека — вшитый засев ==");
        GeometryMaterialStore.Reload();
        List<GeometryMaterialLibrary.Entry> seed = GeometryMaterialLibrary.Seed();
        List<GeometryMaterialLibrary.Entry> now = GeometryMaterialStore.Entries;
        Console.WriteLine("   засев {0} веществ, библиотека {1}", seed.Count, now.Count);

        bool ok = seed.Count == now.Count && seed.Count > 0;
        for (int i = 0; ok && i < seed.Count; i++)
        {
            ok = string.Equals(seed[i].Name, now[i].Name, StringComparison.Ordinal);
        }

        // Проба Lu₂O₃ — то самое вещество, ради которого строка заведена.
        GeometryMaterialLibrary.Entry lu = GeometryMaterialLibrary.ByName("Lutetium oxide");
        Console.WriteLine("   Lutetium oxide в списке: {0}", lu != null ? "ДА" : "НЕТ");
        Console.WriteLine("   совпало: {0}", ok ? "ДА" : "НЕТ");
        return ok && lu != null;
    }

    // ----------------------------------------------------------------------
    // (б) своё вещество переживает запись и перечитывание
    // ----------------------------------------------------------------------
    static bool CaseRoundTrip()
    {
        Console.WriteLine();
        Console.WriteLine("== (б) круг записи-чтения ==");
        List<GeometryMaterialLibrary.Entry> list = Copy(GeometryMaterialStore.Entries);
        list.Add(new GeometryMaterialLibrary.Entry
        {
            Name = "Probe sand",
            Abbr = "PSand",
            Formula = "Si1 O2",
            Density = 1.63,
            Kind = GeometryMaterialLibrary.MaterialKind.Source,
        });

        GeometryMaterialStore.Save(list);
        GeometryMaterialStore.Reload();

        GeometryMaterialLibrary.Entry back = GeometryMaterialLibrary.ByName("Probe sand");
        if (back == null)
        {
            Console.WriteLine("   вещества после перечитывания НЕТ");
            return false;
        }

        GeometryMaterial material = GeometryMaterialLibrary.Make(back, back.Density);
        Console.WriteLine("   имя/сокращение: {0} / {1}", back.Name, back.Abbr);
        Console.WriteLine("   формула        : {0}", back.Formula);
        Console.WriteLine("   плотность      : {0}", back.Density.ToString("0.###", CultureInfo.InvariantCulture));
        Console.WriteLine("   состав         : {0}", GeometryMaterialLibrary.Describe(material));

        // Виден ли он ТАМ, где его будут выбирать, — в списке проб.
        bool listed = false;
        foreach (GeometryMaterialLibrary.Entry entry
                 in GeometryMaterialLibrary.Of(GeometryMaterialLibrary.MaterialKind.Source))
        {
            listed |= string.Equals(entry.Name, "Probe sand", StringComparison.Ordinal);
        }

        double si, o;
        material.Fractions.TryGetValue(14, out si);
        material.Fractions.TryGetValue(8, out o);
        bool composed = Near(si, 0.4674, 2e-3) && Near(o, 0.5326, 2e-3);

        bool ok = back.Abbr == "PSand" && back.Formula == "Si1 O2"
                  && Near(back.Density, 1.63, 1e-9) && composed && listed;
        Console.WriteLine("   в списке проб  : {0}", listed ? "ДА" : "НЕТ");
        Console.WriteLine("   совпало: {0}", ok ? "ДА" : "НЕТ");
        return ok;
    }

    // ----------------------------------------------------------------------
    // (в) смесь: доли складываются с весами
    // ----------------------------------------------------------------------
    static bool CaseMixture()
    {
        Console.WriteLine();
        Console.WriteLine("== (в) смесь из двух веществ ==");
        List<GeometryMaterialLibrary.Entry> list = Copy(GeometryMaterialStore.Entries);

        GeometryMaterialLibrary.Entry half = new GeometryMaterialLibrary.Entry
        {
            Name = "Probe wet salt",
            Abbr = "PWS",
            Density = 1.4,
            Kind = GeometryMaterialLibrary.MaterialKind.Source,
        };
        half.Components.Add(new GeometryMaterialComponent { Material = "Water, liquid", Weight = 50.0 });
        half.Components.Add(new GeometryMaterialComponent { Material = "Potassium chloride", Weight = 50.0 });
        list.Add(half);

        // То же самое, но веса — единицы. Относительность весов заявлена в
        // подсказке формы, значит, обязана быть проверена.
        GeometryMaterialLibrary.Entry ones = new GeometryMaterialLibrary.Entry
        {
            Name = "Probe wet salt ones",
            Density = 1.4,
            Kind = GeometryMaterialLibrary.MaterialKind.Source,
        };
        ones.Components.Add(new GeometryMaterialComponent { Material = "Water, liquid", Weight = 1.0 });
        ones.Components.Add(new GeometryMaterialComponent { Material = "Potassium chloride", Weight = 1.0 });
        list.Add(ones);

        GeometryMaterialStore.Save(list);
        GeometryMaterialStore.Reload();

        GeometryMaterialLibrary.Entry mix = GeometryMaterialLibrary.ByName("Probe wet salt");
        GeometryMaterialLibrary.Entry water = GeometryMaterialLibrary.ByName("Water, liquid");
        GeometryMaterialLibrary.Entry salt = GeometryMaterialLibrary.ByName("Potassium chloride");
        if (mix == null || water == null || salt == null)
        {
            Console.WriteLine("   вещества смеси не нашлись");
            return false;
        }

        GeometryMaterial got = GeometryMaterialLibrary.Make(mix, mix.Density);
        GeometryMaterial w = GeometryMaterialLibrary.Make(water, water.Density);
        GeometryMaterial s = GeometryMaterialLibrary.Make(salt, salt.Density);

        // Ожидание считается НЕ формулой смеси, а сложением долей составляющих:
        // это независимый ход, а не повторение проверяемого кода.
        Dictionary<int, double> want = new Dictionary<int, double>();
        foreach (KeyValuePair<int, double> pair in w.Fractions)
        {
            want[pair.Key] = 0.5 * pair.Value;
        }

        foreach (KeyValuePair<int, double> pair in s.Fractions)
        {
            double have;
            want.TryGetValue(pair.Key, out have);
            want[pair.Key] = have + 0.5 * pair.Value;
        }

        Console.WriteLine("   ожидалось: {0}", Describe(want));
        Console.WriteLine("   вышло    : {0}", GeometryMaterialLibrary.Describe(got));

        bool ok = want.Count == got.Fractions.Count;
        foreach (KeyValuePair<int, double> pair in want)
        {
            double have;
            ok &= got.Fractions.TryGetValue(pair.Key, out have) && Near(have, pair.Value, 1e-9);
        }

        double sum = 0.0;
        foreach (KeyValuePair<int, double> pair in got.Fractions)
        {
            sum += pair.Value;
        }

        Console.WriteLine("   сумма долей: {0}", sum.ToString("F6", CultureInfo.InvariantCulture));
        ok &= Near(sum, 1.0, 1e-9);

        GeometryMaterial byOnes = GeometryMaterialLibrary.Make(
            GeometryMaterialLibrary.ByName("Probe wet salt ones"), 1.4);
        bool same = byOnes.Fractions.Count == got.Fractions.Count;
        foreach (KeyValuePair<int, double> pair in got.Fractions)
        {
            double have;
            same &= byOnes.Fractions.TryGetValue(pair.Key, out have) && Near(have, pair.Value, 1e-12);
        }

        Console.WriteLine("   «1 и 1» = «50 и 50»: {0}", same ? "ДА" : "НЕТ");
        Console.WriteLine("   совпало: {0}", ok && same ? "ДА" : "НЕТ");
        return ok && same;
    }

    // ----------------------------------------------------------------------
    // (г) удаление вшитого держится
    // ----------------------------------------------------------------------
    static bool CaseRemoveSeeded()
    {
        Console.WriteLine();
        Console.WriteLine("== (г) удалённое вшитое не возвращается ==");
        List<GeometryMaterialLibrary.Entry> list = Copy(GeometryMaterialStore.Entries);
        GeometryMaterialLibrary.Entry doomed = null;
        foreach (GeometryMaterialLibrary.Entry entry in list)
        {
            if (string.Equals(entry.Name, "Calcium carbonate", StringComparison.Ordinal))
            {
                doomed = entry;
            }
        }

        if (doomed == null)
        {
            Console.WriteLine("   в засеве нет Calcium carbonate — проверять нечего");
            return false;
        }

        list.Remove(doomed);
        GeometryMaterialStore.Save(list);
        GeometryMaterialStore.Reload();

        bool gone = GeometryMaterialLibrary.ByName("Calcium carbonate") == null;
        string text = File.ReadAllText(GeometryMaterialStore.FilePath);
        bool remembered = text.Contains("Calcium carbonate");
        Console.WriteLine("   после перечитывания его нет : {0}", gone ? "ДА" : "НЕТ");
        Console.WriteLine("   удаление записано в файл    : {0}", remembered ? "ДА" : "НЕТ");
        Console.WriteLine("   совпало: {0}", gone && remembered ? "ДА" : "НЕТ");
        return gone && remembered;
    }

    // ----------------------------------------------------------------------
    // (д) кольцо в составе названо, и расчёт не виснет
    // ----------------------------------------------------------------------
    static bool CaseCycle()
    {
        Console.WriteLine();
        Console.WriteLine("== (д) смесь, входящая сама в себя ==");
        List<GeometryMaterialLibrary.Entry> list = Copy(GeometryMaterialStore.Entries);

        GeometryMaterialLibrary.Entry a = new GeometryMaterialLibrary.Entry
        {
            Name = "Probe ring A",
            Density = 1.0,
            Kind = GeometryMaterialLibrary.MaterialKind.Source,
        };
        GeometryMaterialLibrary.Entry b = new GeometryMaterialLibrary.Entry
        {
            Name = "Probe ring B",
            Density = 1.0,
            Kind = GeometryMaterialLibrary.MaterialKind.Source,
        };
        a.Components.Add(new GeometryMaterialComponent { Material = "Probe ring B", Weight = 1.0 });
        b.Components.Add(new GeometryMaterialComponent { Material = "Probe ring A", Weight = 1.0 });
        b.Components.Add(new GeometryMaterialComponent { Material = "Water, liquid", Weight = 1.0 });
        list.Add(a);
        list.Add(b);

        Func<string, GeometryMaterialLibrary.Entry> lookup = name =>
        {
            foreach (GeometryMaterialLibrary.Entry entry in list)
            {
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        };

        bool named = GeometryMaterialLibrary.HasCycle(a, lookup)
                     && GeometryMaterialLibrary.HasCycle(b, lookup);

        // Считаться оно всё равно должно — иначе форма повисла бы на показе
        // состава, ещё до того как человек дотянулся до отказа.
        GeometryMaterial material = GeometryMaterialLibrary.Make(a, 1.0, lookup);
        Console.WriteLine("   кольцо названо: {0}", named ? "ДА" : "НЕТ");
        Console.WriteLine("   состав A      : {0}", GeometryMaterialLibrary.Describe(material));

        // A -> B, у B кольцевая половина отброшена, остаётся вода целиком.
        double h, o;
        material.Fractions.TryGetValue(1, out h);
        material.Fractions.TryGetValue(8, out o);
        bool water = Near(h, 0.111894, 1e-4) && Near(o, 0.888106, 1e-4);
        Console.WriteLine("   осталась вода : {0}", water ? "ДА" : "НЕТ");

        // Прямое кольцо у не-смеси не мерещится.
        bool clean = !GeometryMaterialLibrary.HasCycle(
            GeometryMaterialLibrary.ByName("Water, liquid"), lookup);
        Console.WriteLine("   у формулы кольца нет: {0}", clean ? "ДА" : "НЕТ");
        Console.WriteLine("   совпало: {0}", named && water && clean ? "ДА" : "НЕТ");
        return named && water && clean;
    }

    // ----------------------------------------------------------------------
    // (е), (ж) редактор: добавляет, показывает состав, отказывает, сохраняет
    // ----------------------------------------------------------------------
    static bool CaseEditor()
    {
        Console.WriteLine();
        Console.WriteLine("== (е) редактор веществ ==");
        GeometryMaterialStore.Reload();
        int before = GeometryMaterialStore.Entries.Count;

        using (GeometryMaterialEditorForm form =
               new GeometryMaterialEditorForm(GeometryMaterialLibrary.MaterialKind.Source))
        {
            Invoke(form, "AddClicked", null, EventArgs.Empty);

            TextBox name = (TextBox)Field(form, "nameBox");
            TextBox density = (TextBox)Field(form, "densityBox");
            TextBox formula = (TextBox)Field(form, "formulaBox");
            Label composition = (Label)Field(form, "compositionLabel");
            Label problem = (Label)Field(form, "problemLabel");

            name.Text = "Probe editor chalk";
            formula.Text = "Ca1 C1 O3";
            density.Text = "1.5";
            Console.WriteLine("   состав на экране: {0}", composition.Text);
            Console.WriteLine("   отказ на экране : «{0}»", problem.Text);
            bool composed = composition.Text.Contains("Ca") && composition.Text.Contains("O");
            bool quiet = string.IsNullOrEmpty(problem.Text);

            // Дубликат имени — обязан быть НАЗВАН: имя это ключ, по нему
            // вещество ищут и смеси, и чтение файла геометрии.
            name.Text = "Water, liquid";
            bool saidDuplicate = !string.IsNullOrEmpty(problem.Text);
            Console.WriteLine("   про дубликат имени: «{0}»", problem.Text);

            name.Text = "Probe editor chalk";
            density.Text = "0";
            bool saidDensity = !string.IsNullOrEmpty(problem.Text);
            Console.WriteLine("   про нулевую плотность: «{0}»", problem.Text);
            density.Text = "1.5";

            // Отказ по формуле — тем же путём.
            formula.Text = "Xx1";
            bool saidFormula = !string.IsNullOrEmpty(problem.Text);
            Console.WriteLine("   про негодную формулу: «{0}»", problem.Text);
            formula.Text = "Ca1 C1 O3";

            Console.WriteLine();
            Console.WriteLine("== (ж) сохранение из формы ==");
            Invoke(form, "SaveClicked", null, EventArgs.Empty);
            bool saved = form.DialogResult == DialogResult.OK;
            Console.WriteLine("   форма закрылась сохранением: {0}", saved ? "ДА" : "НЕТ");

            GeometryMaterialStore.Reload();
            GeometryMaterialLibrary.Entry back = GeometryMaterialLibrary.ByName("Probe editor chalk");
            int after = GeometryMaterialStore.Entries.Count;
            Console.WriteLine("   веществ было {0}, стало {1}", before, after);
            Console.WriteLine("   заведённое читается с диска: {0}", back != null ? "ДА" : "НЕТ");

            bool ok = composed && quiet && saidDuplicate && saidDensity && saidFormula
                      && saved && back != null && after == before + 1
                      && Near(back.Density, 1.5, 1e-9);
            Console.WriteLine("   совпало: {0}", ok ? "ДА" : "НЕТ");
            return ok;
        }
    }

    // ----------------------------------------------------------------------

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
        }

        foreach (string dir in Directory.GetDirectories(from))
        {
            CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
    }

    static List<GeometryMaterialLibrary.Entry> Copy(List<GeometryMaterialLibrary.Entry> list)
    {
        List<GeometryMaterialLibrary.Entry> copy = new List<GeometryMaterialLibrary.Entry>();
        foreach (GeometryMaterialLibrary.Entry entry in list)
        {
            copy.Add(entry.Clone());
        }

        return copy;
    }

    static string Describe(Dictionary<int, double> fractions)
    {
        List<int> order = new List<int>(fractions.Keys);
        order.Sort();
        List<string> parts = new List<string>();
        foreach (int z in order)
        {
            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:F4}",
                                    GeometryMaterialLibrary.SymbolOf(z), fractions[z]));
        }

        return string.Join(", ", parts.ToArray());
    }

    static bool Near(double a, double b, double tolerance)
    {
        return Math.Abs(a - b) <= tolerance;
    }

    static object Field(object target, string name)
    {
        FieldInfo info = target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (info == null)
        {
            throw new InvalidOperationException("нет поля " + name);
        }

        return info.GetValue(target);
    }

    static void Invoke(object target, string name, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (info == null)
        {
            throw new InvalidOperationException("нет метода " + name);
        }

        info.Invoke(target, args);
    }
}
