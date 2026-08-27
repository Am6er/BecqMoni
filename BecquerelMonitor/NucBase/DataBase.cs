using System;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.NucBase
{
    public class DataBase
    {
        SqliteConnection sqlite_conn;

        // Путь, по которому базу искали. Держится полем, потому что называть
        // его обязаны ВСЕ сообщения об отказе этого класса, а не только первое.
        string dbPath;

        public DataBase()
        {
            CreateConnection();
        }

        /// <summary>
        /// ⛔ Отказ соединения ВЕШАЛ БЕЗОКОННЫЙ ПРОЦЕСС НАСМЕРТЬ (<c>T87</c>).
        /// Здесь стоял голый <c>MessageBox.Show</c>, и проба, запущенная там,
        /// где <c>nucdb.sqlite</c> нет, не падала и не печатала — она ЖДАЛА,
        /// пока окно закроют, а закрыть его было некому. Измерено 27.08.2026
        /// встречной проверкой <c>D42</c> и повторено здесь: каталог без базы,
        /// <c>getDecayRad("176LU")</c> — процесс убит по сроку 20 с, класс окна
        /// <c>#32770</c>. Снаружи это неотличимо от долгого счёта.
        ///
        /// Поэтому показ ошибки и её ВОЗБУЖДЕНИЕ разведены, и разведены той же
        /// единственной дверью, что у менеджеров-одиночек (<see cref="AppUi"/>,
        /// <c>S100</c>): в окнах — прежнее модальное окно, читатель у него
        /// человек; без окон — исключение, читатель у него КОД ВОЗВРАТА пробы.
        /// Просто «убрать MessageBox» было нельзя: тогда отказ остался бы без
        /// читателя вообще, а <see cref="ReadData"/> ниже упал бы жалобой
        /// поставщика на состояние соединения — жалобой, в которой нет ни
        /// пути, ни слова о том, что базы попросту нет рядом.
        ///
        /// ⚠ Молча продолжать после отказа НЕЛЬЗЯ и в окнах тоже, поэтому
        /// соединение возвращается закрытым, а <see cref="ReadData"/> проверяет
        /// его состояние и называет ту же причину.
        /// </summary>
        SqliteConnection CreateConnection()
        {
            // Каталог приложения, а НЕ текущий: текущий меняет любой диалог
            // открытия файла, после чего база просто не находится. Все
            // остальные читатели баз берут путь так же (T23).
            this.dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            try
            {
                // ⛔ САМО СОЗДАНИЕ СОЕДИНЕНИЯ ТОЖЕ ВНУТРИ `try` (`D46`). Стояло
                // оно выше, и первый же `new SqliteConnection` тянет ИНИЦИАЛИЗАТОР
                // ТИПА поставщика: без перенаправления версий `SQLitePCLRaw.core`
                // (файл `<приложение>.exe.config` рядом) он бросает
                // `TypeInitializationException` — измерено 28.08.2026, процесс
                // умирал кодом −532462766 молча. Причина у такого отказа ровно та
                // же, что у неоткрывшегося файла, и назвать её надо так же —
                // вместе с путём.
                sqlite_conn = new SqliteConnection("Data Source=" + this.dbPath + ";Mode=ReadOnly;Cache=Shared;");
                sqlite_conn.Open();
            }
            catch (Exception ex)
            {
                string text = string.Format(Resources.ERRNucBaseOpenDatabase, this.dbPath, ex.Message);
                if (!AppUi.HasWindows)
                {
                    throw new InvalidOperationException("BecqMoni: " + text, ex);
                }
                AppUi.Report(text, Resources.ErrorDialogTitle, MessageBoxIcon.Hand);
            }
            return sqlite_conn;
        }

        /// <summary>
        /// ⚠ Второй путь отказа этого файла: соединение не открылось, а запрос
        /// всё равно пришёл. В окнах так и бывает — <see cref="CreateConnection"/>
        /// там показывает окно и возвращает управление, — и прежде отсюда
        /// вылетала жалоба поставщика на состояние соединения, без пути и без
        /// причины. Причина называется здесь же, вместе с путём.
        ///
        /// ⚠ Текст прежней жалобы в этом примечании НЕ приводится: он
        /// принадлежит поставщику, а измерен здесь не был.
        /// </summary>
        public SqliteDataReader ReadData(string sqlcmd)
        {
            if (sqlite_conn == null || sqlite_conn.State != ConnectionState.Open)
            {
                throw new InvalidOperationException(
                    "BecqMoni: nuclide database is not open, the query was not run: "
                    + (this.dbPath == null ? "<no path>" : this.dbPath));
            }

            SqliteDataReader sqlite_datareader;
            SqliteCommand sqlite_cmd;
            sqlite_cmd = sqlite_conn.CreateCommand();
            sqlite_cmd.CommandText = sqlcmd;

            sqlite_datareader = sqlite_cmd.ExecuteReader();

            return sqlite_datareader;
        }

        /// <summary>
        /// ⚠ Третий путь: закрытие НЕОТКРЫТОГО соединения. Зовут его и с пути
        /// отказа тоже (у <c>NucBaseFramework</c> <c>Close</c> стоит после
        /// <c>catch</c>), так что падать здесь нельзя — иначе исключение с
        /// путём отказа подменилось бы исключением уборки.
        /// </summary>
        public void Close()
        {
            if (sqlite_conn == null)
            {
                return;
            }
            sqlite_conn.Close();
        }
    }
}
