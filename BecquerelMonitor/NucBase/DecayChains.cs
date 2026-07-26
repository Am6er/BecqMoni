using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BecquerelMonitor.NucBase
{
    // Кумулятивное ветвление ряда распада: какая доля распадов КОРНЯ ряда проходит
    // через каждый его член. Живёт рядом с базой, потому что нужно двоим — импорту
    // определений из NucBase и каталогу конструктора (RoiWizard.NuclideCatalog), — а
    // две независимые реализации одного обхода разъехались бы.
    //
    // Зачем это вообще. В decay_radiations интенсивность дана НА РАСПАД САМОГО
    // ИЗЛУЧАЮЩЕГО НУКЛИДА: у Tl-208 линия 583.19 кэВ имеет 85 %. Но в равновесном ряду
    // Th-232 через Tl-208 идёт лишь 35.94 % распадов (ветка Bi-212), поэтому на один
    // распад Th-232 та же линия даёт 85 × 0.3594 = 30.5 %.
    //
    // Разница не косметическая: BR-связка LibraryPeakFitter берёт веса компонент прямо
    // из NuclideDefinition.Intencity, и группа из линий Tl-208 и Bi-212 при нескольченных
    // интенсивностях делит амплитуду бленда в неверной пропорции.
    public static class DecayChains
    {
        // Дальше этого ряд не считается: защита от петли в данных, а не физический предел.
        const int MemberLimit = 100;

        // Ветви слабее этого не прослеживаются — их линии всё равно ниже любого порога.
        const double MinFraction = 1e-6;

        // Путь к базе — от каталога сборки, а не от Environment.CurrentDirectory:
        // рабочий каталог процесса меняет любой файловый диалог, и после «Открыть
        // спектр» из другой папки база перестала бы находиться.
        public static string DatabasePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite"); }
        }

        public static string ConnectionString
        {
            get { return "Data Source=" + DatabasePath + ";Mode=ReadOnly;Cache=Shared;"; }
        }

        // {nucid -> доля распадов корня, проходящая через нуклид}. Корень всегда 1.0.
        // Пустой словарь, если базы нет: вызывающий тогда работает по нескольченным
        // интенсивностям, как раньше, а не падает.
        public static Dictionary<string, double> BranchingFrom(string rootNucid)
        {
            Dictionary<string, double> fraction =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(rootNucid) || !File.Exists(DatabasePath))
            {
                return fraction;
            }

            try
            {
                using (SqliteConnection connection = new SqliteConnection(ConnectionString))
                {
                    connection.Open();
                    Fill(connection, rootNucid, fraction);
                }
            }
            catch (Exception)
            {
                // База недоступна или повреждена — импорт не должен из-за этого падать;
                // без множителей он даёт прежний (нескорректированный) результат.
                fraction.Clear();
            }
            return fraction;
        }

        // Заполняет fraction и возвращает членов ряда В ПОРЯДКЕ ОБХОДА, сверху вниз от
        // корня. Порядок — не украшение: по нему берутся «члены ниже данного», когда в
        // набор добавляют хвост цепочки, и сортировка по доле его бы уничтожила
        // (у Ra-228, Ac-228, Th-228 и Pb-212 доля одна и та же — единица).
        public static List<string> Fill(SqliteConnection connection, string rootNucid,
                                        Dictionary<string, double> fraction)
        {
            List<string> order = new List<string>();
            fraction[rootNucid] = 1.0;
            order.Add(rootNucid);

            // Следуются только переходы с минимальным l_seqno на пару «родитель →
            // дочерний»: строки с бо́льшим уровнем описывают распад возбуждённого уровня
            // и дублируют переход с другим процентом. У Bi-212 наряду с настоящими
            // 35.94 / 64.06 % лежат 67 / 33 % с уровня 5.
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select d.daughter_nucid, d.perc from decay_chain d " +
                    "where d.nucid = @nucid and d.perc is not null " +
                    "  and d.l_seqno = (select min(x.l_seqno) from decay_chain x " +
                    "                   where x.nucid = d.nucid " +
                    "                     and x.daughter_nucid = d.daughter_nucid " +
                    "                     and x.dec_type = d.dec_type)";
                SqliteParameter parameter = command.Parameters.Add("@nucid", SqliteType.Text);

                for (int i = 0; i < order.Count && order.Count < MemberLimit; i++)
                {
                    string current = order[i];
                    parameter.Value = current;

                    // Читатель закрывается до правки словаря: следующая итерация
                    // переиспользует ту же команду, а второй открытый reader на том же
                    // соединении её выполнить не даст.
                    List<KeyValuePair<string, double>> steps =
                        new List<KeyValuePair<string, double>>();
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string daughter = reader.GetString(0);
                            if (string.Equals(daughter, current, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;                  // самопетля, встречается у U-238
                            }
                            double percent;
                            string text = reader.IsDBNull(1) ? null : reader.GetString(1);
                            if (!double.TryParse(text, NumberStyles.Float,
                                                 CultureInfo.InvariantCulture, out percent))
                            {
                                continue;                  // «?» вместо процента
                            }
                            steps.Add(new KeyValuePair<string, double>(daughter, percent));
                        }
                    }

                    foreach (KeyValuePair<string, double> step in steps)
                    {
                        double share = fraction[current] * step.Value / 100.0;
                        if (share < MinFraction)
                        {
                            continue;
                        }
                        if (fraction.ContainsKey(step.Key))
                        {
                            fraction[step.Key] += share;
                        }
                        else
                        {
                            fraction[step.Key] = share;
                            order.Add(step.Key);
                        }
                    }
                }
            }

            return order;
        }

        // Множитель для нуклида; 1.0 — если ряд не посчитан или нуклида в нём нет
        // (тогда интенсивность остаётся «на распад самого нуклида», как в базе).
        public static double FactorOf(Dictionary<string, double> branching, string nucid)
        {
            double value;
            return branching != null && nucid != null && branching.TryGetValue(nucid, out value)
                ? value
                : 1.0;
        }
    }
}
