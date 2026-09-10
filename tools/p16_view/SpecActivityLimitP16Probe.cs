// Читатель полосы П16, 10.09.2026: ПОДПИСЬ УДЕЛЬНОЙ АКТИВНОСТИ В КАРТОЧКЕ
// НУКЛИДА (остаток `A304`, решение Amber 10.09.2026: «Ставить „<“ тем же
// признаком»).
//
// Окно `BecqMoni` в проверке не поднимают, поэтому подпись снимается
// ОТРАЖЕНИЕМ из собранной сборки: проба зовёт тот же `getNuclude`, что и
// карточка, и ту же складывалку подписи.
//
// ⛔ ОБЕ СКЛАДЫВАЛКИ БЕРУТСЯ ОТРАЖЕНИЕМ НАРОЧНО, а не прямым вызовом — тем же
// доводом, что у `tools/p14_view/HalfLifeLimitP14Probe.cs`. Один и тот же
// исходник обязан собираться и против ПРАВЛЕНОЙ сборки, и против сборки без
// правки, где `NucBaseFramework.SpecificActivityCaption` ещё нет: только так
// «до» и «после» меряются ОДНИМ читателем, а не двумя разными. Нет метода —
// складывается прежняя подпись, ровно та склейка, что стояла в
// `NucBase.ShowIsotopeCard` до правки:
//
//     nuc.SpecialActivity.ToString("e2", CultureInfo.InvariantCulture)
//         + " " + Resources.Bkg
//
// ⚠ Подпись ПЕРИОДА снимается той же пробой и в тот же файл — это
// положительный контроль остатка: правка про активность не смеет сдвинуть
// период, а он уже помечен «>» у тех же 81 нуклида (полоса П14).
//
//   specactivitylimitp16probe [--dump=<файл>] [--only=<NUCID>[,<NUCID>...]]
//
// Печатает: нашлись ли складывалки, сколько нуклидов показано, у скольких в
// подписи активности знак «<» и у скольких в подписи периода знак «>».
// С `--dump` пишет «nucid<TAB>период<TAB>активность» по всем — два таких
// файла, снятых с двух сборок, и есть замер «сколько нуклидов сменили показ».
//
// Коды возврата: 0 — прогон состоялся; 2 — отказ оснастки.

using BecquerelMonitor.NucBase;
using BecquerelMonitor.Properties;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace SpecActivityLimitP16Probe
{
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string dump = null;
            var only = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--dump=", StringComparison.Ordinal)) dump = a.Substring(7);
                else if (a.StartsWith("--only=", StringComparison.Ordinal))
                    only.AddRange(a.Substring(7).Split(','));
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            MethodInfo actCaption = typeof(NucBaseFramework).GetMethod(
                "SpecificActivityCaption", BindingFlags.Public | BindingFlags.Static);
            MethodInfo hlCaption = typeof(NucBaseFramework).GetMethod(
                "HalfLifeCaption", BindingFlags.Public | BindingFlags.Static);
            Console.WriteLine("складывалка активности: {0}",
                              actCaption == null ? "НЕТ (сборка до остатка A304)"
                                                 : "NucBaseFramework.SpecificActivityCaption");
            Console.WriteLine("складывалка периода:    {0}",
                              hlCaption == null ? "НЕТ (сборка до A304)"
                                                : "NucBaseFramework.HalfLifeCaption");

            List<string> names;
            try
            {
                names = only.Count > 0 ? only : AllNuclides();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ОТКАЗ ОСНАСТКИ: база не читается: " + ex.Message);
                return 2;
            }

            var framework = new NucBaseFramework();
            var lines = new List<string>();
            int shown = 0, markedLess = 0, markedMore = 0, missing = 0;
            foreach (string name in names)
            {
                Nuclide nuc = framework.getNuclude(name);
                if (nuc == null)
                {
                    missing++;
                    if (framework.LastError != null)
                    {
                        Console.Error.WriteLine("ОТКАЗ на «" + name + "»: " + framework.LastError);
                        return 2;
                    }

                    continue;
                }

                string act = actCaption != null
                    ? (string)actCaption.Invoke(null, new object[] { nuc })
                    : nuc.SpecialActivity.ToString("e2", CultureInfo.InvariantCulture)
                          + " " + Resources.Bkg;
                string half = hlCaption != null
                    ? (string)hlCaption.Invoke(null, new object[] { nuc })
                    : (nuc.HalfLife ?? "") + " " + (nuc.HalfLifeUOM ?? "");

                shown++;
                if (act.IndexOf('<') >= 0) markedLess++;
                if (half.IndexOf('>') >= 0) markedMore++;
                lines.Add(name + "\t" + half + "\t" + act);
            }

            Console.WriteLine("нуклидов спрошено {0}, карточка показана {1}, пусто {2}",
                              names.Count, shown, missing);
            Console.WriteLine("подписей активности со знаком «<»: {0}", markedLess);
            Console.WriteLine("подписей периода со знаком «>»:    {0}", markedMore);

            if (dump != null)
            {
                lines.Sort(StringComparer.Ordinal);
                File.WriteAllLines(dump, lines, new UTF8Encoding(false));
                Console.WriteLine("подписи выписаны: {0} строк -> {1}", lines.Count, dump);
            }

            return 0;
        }

        /// <summary>
        /// Имена всех нуклидов базы — своим соединением ТОЛЬКО НА ЧТЕНИЕ. Путь
        /// тот же, что берёт `DataBase`: каталог сборки, а не текущий.
        /// </summary>
        static List<string> AllNuclides()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            var names = new List<string>();
            using (var conn = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
            {
                conn.Open();
                SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = "select distinct nucid from nuclides where half_life not null order by nucid";
                using (SqliteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read()) names.Add(reader.GetString(0));
                }
            }

            return names;
        }
    }
}
