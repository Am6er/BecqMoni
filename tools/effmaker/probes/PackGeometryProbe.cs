// E6: форма молча усредняла пачку разных геометрий.
//
//     packgeometryprobe [<каталог со спектрами>]
//
// Проверяется на НАСТОЯЩИХ спектрах корпуса, а не на выдуманных: пачка одной
// группы обязана пройти молча, пачка из разных групп — быть названа. Своей
// копии логики у пробы нет — она дёргает `EfficiencyMakerForm.PackGeometryComplaints`
// отражением, подставив список файлов в то же поле, куда его кладёт форма.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using BecquerelMonitor;

static class PackGeometryProbe
{
    static int failures;

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
        System.Threading.Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
        DeviceType.InitializeDeviceTypes();
        GlobalConfigManager.GetInstance();

        string dir = args.Length > 0
            ? args[0]
            : Path.Combine("tools", "CORPUS", "corpus", "spectra");
        if (!Directory.Exists(dir))
        {
            Console.WriteLine("нет каталога {0} — запускать из КОРНЯ репозитория", dir);
            return 2;
        }

        // Одна группа — одна съёмка: спектры G1S24 сняты одним прибором.
        Case("одна группа (G1S24)", Pick(dir, "G1S24_", 4), false);

        // Разные приборы — заведомо разные геометрии.
        Case("сборная: G1S24 + RC103 + AS80",
             Pick(dir, "G1S24_", 2).Concat(Pick(dir, "RC103_", 1))
                                   .Concat(Pick(dir, "AS80_", 1)).ToList(), true);

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "СОШЛОСЬ" : "РАСХОЖДЕНИЙ: " + failures);
        return failures == 0 ? 0 : 1;
    }

    static List<string> Pick(string dir, string prefix, int count)
    {
        return Directory.GetFiles(dir, prefix + "*.xml").OrderBy(p => p).Take(count).ToList();
    }

    static void Case(string title, List<string> files, bool expectComplaints)
    {
        Console.WriteLine();
        Console.WriteLine("== {0}: {1} спектр(ов) ==", title, files.Count);
        if (files.Count == 0)
        {
            Console.WriteLine("   РАСХОЖДЕНИЕ: спектров не нашлось");
            failures++;
            return;
        }

        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            FieldInfo field = typeof(EfficiencyMakerForm).GetField(
                "spectrumFiles", BindingFlags.NonPublic | BindingFlags.Instance);
            List<string> pack = (List<string>)field.GetValue(form);
            pack.Clear();
            pack.AddRange(files);

            MethodInfo check = typeof(EfficiencyMakerForm).GetMethod(
                "PackGeometryComplaints", BindingFlags.NonPublic | BindingFlags.Instance);
            List<string> said = (List<string>)check.Invoke(form, null);

            if (said.Count == 0)
            {
                Console.WriteLine("   молчит");
            }

            foreach (string line in said)
            {
                Console.WriteLine("   {0}", line);
            }

            if ((said.Count > 0) != expectComplaints)
            {
                Console.WriteLine("   РАСХОЖДЕНИЕ: ждали {0}",
                                  expectComplaints ? "жалобу" : "молчание");
                failures++;
            }
        }
    }
}
