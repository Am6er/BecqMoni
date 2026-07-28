using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BecquerelMonitor.NucBase
{
    // Одно семейство: код, подписи и пояснения на двух языках.
    public class NuclideFamily
    {
        public string Code { get; set; }
        public int SortOrder { get; set; }
        public string Title { get; set; }
        public string TitleRu { get; set; }
        public string Info { get; set; }
        public string InfoRu { get; set; }

        // Подпись на языке интерфейса; при отсутствии перевода — нейтральная.
        public string LocalizedTitle
        {
            get { return Pick(this.TitleRu, this.Title); }
        }

        public string LocalizedInfo
        {
            get { return Pick(this.InfoRu, this.Info); }
        }

        static string Pick(string ru, string neutral)
        {
            bool russian = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
            return russian && !string.IsNullOrEmpty(ru) ? ru : neutral;
        }
    }

    // Классификация нуклидов по областям применения (NORM / MED / IND / SNM по
    // ANSI N42.34, плюс POPULAR / FISS / NAA / WASTE вне стандарта). Живёт в
    // nucdb.sqlite рядом с самими ядерными данными — источник правды один, и правит
    // её пользователь отсюда же, из NucBase, а не внешним скриптом.
    //
    // Таблицы: families (справочник) и nuclide_families (привязки nucid -> код).
    // Читает их и конструктор ROI (RoiWizard.NuclideCatalog), поэтому после записи
    // его кэш сбрасывается — иначе список семейств в мастере остался бы прежним до
    // перезапуска.
    public static class NuclideFamilies
    {
        public static string DatabasePath
        {
            get { return DecayChains.DatabasePath; }
        }

        static string ConnectionString(bool writable)
        {
            return "Data Source=" + DatabasePath +
                   (writable ? ";Mode=ReadWrite" : ";Mode=ReadOnly;Cache=Shared");
        }

        public static bool IsAvailable
        {
            get
            {
                if (!File.Exists(DatabasePath))
                {
                    return false;
                }
                try
                {
                    using (SqliteConnection connection = new SqliteConnection(ConnectionString(false)))
                    {
                        connection.Open();
                        using (SqliteCommand command = connection.CreateCommand())
                        {
                            command.CommandText =
                                "select count(*) from sqlite_master where type='table' " +
                                "and name in ('families','nuclide_families')";
                            return Convert.ToInt32(command.ExecuteScalar()) == 2;
                        }
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static List<NuclideFamily> All()
        {
            List<NuclideFamily> result = new List<NuclideFamily>();
            using (SqliteConnection connection = new SqliteConnection(ConnectionString(false)))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select code, sort_order, title, title_ru, info, info_ru " +
                        "from families order by sort_order";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new NuclideFamily
                            {
                                Code = reader.GetString(0),
                                SortOrder = reader.GetInt32(1),
                                Title = Text(reader, 2),
                                TitleRu = Text(reader, 3),
                                Info = Text(reader, 4),
                                InfoRu = Text(reader, 5)
                            });
                        }
                    }
                }
            }
            return result;
        }

        // Коды семейств, приписанных нуклиду. nucid — ключ базы («241AM»).
        public static HashSet<string> Of(string nucid)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(nucid))
            {
                return result;
            }
            using (SqliteConnection connection = new SqliteConnection(ConnectionString(false)))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select code from nuclide_families where nucid = @nucid";
                    command.Parameters.Add("@nucid", SqliteType.Text).Value = nucid;
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(reader.GetString(0));
                        }
                    }
                }
            }
            return result;
        }

        // Полная замена набора семейств нуклида. Всё в одной транзакции: половина
        // применённой правки хуже, чем непринятая.
        // Покажет ли каталог мастера классификацию этого нуклида.
        //
        // Каталог берёт только нуклиды с известным периодом полураспада
        // (NuclideCatalog: `half_life_sec is not null`), и это верно — у
        // стабильного нет линий, из которых строить ROI. Но диалог семейств
        // принимал классификацию для любого нуклида молча, и запись уходила в
        // базу навсегда невидимой. Пусть говорит.
        public static bool IsVisibleInCatalog(string nucid)
        {
            if (string.IsNullOrEmpty(nucid))
            {
                return false;
            }
            try
            {
                using (SqliteConnection connection = new SqliteConnection(ConnectionString(false)))
                {
                    connection.Open();
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText =
                            "select 1 from nuclides where nucid = @nucid and half_life_sec is not null limit 1";
                        command.Parameters.Add("@nucid", SqliteType.Text).Value = nucid;
                        return command.ExecuteScalar() != null;
                    }
                }
            }
            catch (Exception)
            {
                return true;                 // не смогли проверить — не мешаем работать
            }
        }

        public static void Set(string nucid, IEnumerable<string> codes)
        {
            if (string.IsNullOrEmpty(nucid))
            {
                return;
            }
            using (SqliteConnection connection = new SqliteConnection(ConnectionString(true)))
            {
                connection.Open();
                using (SqliteTransaction transaction = connection.BeginTransaction())
                {
                    using (SqliteCommand clear = connection.CreateCommand())
                    {
                        clear.Transaction = transaction;
                        clear.CommandText = "delete from nuclide_families where nucid = @nucid";
                        clear.Parameters.Add("@nucid", SqliteType.Text).Value = nucid;
                        clear.ExecuteNonQuery();
                    }

                    if (codes != null)
                    {
                        using (SqliteCommand insert = connection.CreateCommand())
                        {
                            insert.Transaction = transaction;
                            insert.CommandText =
                                "insert or ignore into nuclide_families (nucid, code) " +
                                "values (@nucid, @code)";
                            insert.Parameters.Add("@nucid", SqliteType.Text).Value = nucid;
                            SqliteParameter code = insert.Parameters.Add("@code", SqliteType.Text);
                            foreach (string value in codes)
                            {
                                if (string.IsNullOrEmpty(value))
                                {
                                    continue;
                                }
                                code.Value = value.ToUpperInvariant();
                                insert.ExecuteNonQuery();
                            }
                        }
                    }

                    transaction.Commit();
                }
            }

            // Каталог конструктора держит классификацию в памяти и читается один раз
            // на процесс — без сброса правка была бы видна только после перезапуска.
            RoiWizard.NuclideCatalog.Invalidate();
        }

        // «Am-241» → «241AM». Формат ключа тот же, что у decay_radiations.
        public static string ToNucid(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return displayName;
            }
            string name = displayName.Trim();
            int dash = name.IndexOf('-');
            if (dash <= 0)
            {
                return name.ToUpperInvariant();
            }
            string symbol = name.Substring(0, dash).ToUpperInvariant();
            string mass = name.Substring(dash + 1);
            // суффикс изомера в базе строчный: 234PAm1, 108AGm
            string meta = "";
            int i = 0;
            while (i < mass.Length && char.IsDigit(mass[i]))
            {
                i++;
            }
            if (i < mass.Length)
            {
                meta = mass.Substring(i).ToLowerInvariant();
                mass = mass.Substring(0, i);
            }
            return mass + symbol + meta;
        }

        static string Text(SqliteDataReader reader, int index)
        {
            return reader.IsDBNull(index) ? null : reader.GetString(index);
        }
    }
}
