// Читатель полосы П14, 10.09.2026: ПОДПИСЬ ПЕРИОДА ПОЛУРАСПАДА В КАРТОЧКЕ
// НУКЛИДА (`A304`).
//
// Окно `BecqMoni` в проверке не поднимают, поэтому подпись снимается
// ОТРАЖЕНИЕМ из собранной сборки: проба зовёт тот же `getNuclude`, что и
// карточка, и ту же складывалку подписи.
//
// ⛔ ПОДПИСЬ БЕРЁТСЯ ОТРАЖЕНИЕМ НАРОЧНО, а не прямым вызовом. Один и тот же
// исходник обязан собираться и против ПРАВЛЕНОЙ сборки, и против сборки
// чистого `HEAD`, где `NucBaseFramework.HalfLifeCaption` ещё нет: только так
// «до» и «после» меряются ОДНИМ читателем, а не двумя разными. Нет метода —
// складывается прежняя подпись, «период + пробел + единица», ровно как её
// складывала строка `NucBase.ShowIsotopeCard` до правки.
//
//   halflifelimitp14probe [--dump=<файл>] [--only=<NUCID>[,<NUCID>...]]
//
// Печатает: нашёлся ли метод подписи, сколько нуклидов показано, у скольких в
// подписи знак «>». С `--dump` пишет «nucid<TAB>подпись» по всем — два таких
// файла, снятых с двух сборок, и есть замер «сколько нуклидов сменили показ».
//
// Коды возврата: 0 — прогон состоялся; 2 — отказ оснастки.

using BecquerelMonitor.NucBase;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace HalfLifeLimitP14Probe
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

            MethodInfo caption = typeof(NucBaseFramework).GetMethod(
                "HalfLifeCaption", BindingFlags.Public | BindingFlags.Static);
            Console.WriteLine("складывалка подписи: {0}",
                              caption == null ? "НЕТ (сборка до правки A304)"
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
            int shown = 0, marked = 0, missing = 0;
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

                string text = caption != null
                    ? (string)caption.Invoke(null, new object[] { nuc })
                    : (nuc.HalfLife ?? "") + " " + (nuc.HalfLifeUOM ?? "");
                shown++;
                if (text.IndexOf('>') >= 0) marked++;
                lines.Add(name + "\t" + text);
            }

            Console.WriteLine("нуклидов спрошено {0}, карточка показана {1}, пусто {2}",
                              names.Count, shown, missing);
            Console.WriteLine("подписей со знаком «>»: {0}", marked);

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
