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
                // ⛔ ПРИЧИНА СОБИРАЕТСЯ ЕДИНСТВЕННОЙ ДВЕРЬЮ ДЕРЕВА (`A164`), а
                // не голым `ex.Message`. Соглашение одно на всё дерево
                // (<see cref="AppUi.Reason"/>): «Тип: сообщение» каждого звена
                // цепочки, по разу. `ex.Message` называет ОДНО сообщение и ни
                // одного имени класса — а по нему-то и отличают «инициализатор
                // типа выдал исключение» от «файла нет».
                //
                // ⚠ Цена нарушения ИЗМЕРЕНА при `A144` (сцена `F_a25_noconf`):
                // метка редактора нуклидов выросла на 110 знаков вместо 29,
                // потому что имени класса среднего звена в этом тексте не было
                // вовсе и дверь называла звено ЦЕЛИКОМ — вместе с сообщением,
                // которое здесь уже стояло. Точный признак `A129` (звено
                // приписывается, если его «Тип: сообщение» ДОСЛОВНО ещё не в
                // тексте) на половинную цитату не срабатывает нарочно: лишнее
                // срабатывание отняло бы причину. Значит чинить надо здесь —
                // цитировать полностью, а не ослаблять признак у двери.
                string text = string.Format(Resources.ERRNucBaseOpenDatabase, this.dbPath, AppUi.Reason(ex));
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
        ///
        /// ⛔ ПАРАМЕТРЫ ПОЯВИЛИСЬ ЗДЕСЬ, А НЕ У ВЫЗЫВАЮЩЕГО (`D45`). Перегрузки
        /// с параметрами у этого метода не было ВОВСЕ, поэтому весь
        /// <c>NucBaseFramework</c> собирал запрос склейкой — включая имена,
        /// приходящие из поля ввода редактора. Апостроф в имени закрывал
        /// литерал и ронял запрос: измерено 31.08.2026, <c>getNuclude("O'BRIEN")</c>
        /// давало <c>SqliteException</c> вместо пустого ответа. Двух соглашений
        /// о подстановке в проекте быть не должно: <c>CascadeAtomicData</c> и
        /// <c>FsaSampleLibrary</c> уже давно передают имя параметром <c>$n</c>,
        /// и ровно того же имени параметра ждёт
        /// <c>DecayParentRule.LevelClause</c>.
        /// </summary>
        public SqliteDataReader ReadData(string sqlcmd, params SqliteParameter[] parameters)
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
            if (parameters != null)
            {
                foreach (SqliteParameter parameter in parameters)
                {
                    if (parameter != null)
                    {
                        sqlite_cmd.Parameters.Add(parameter);
                    }
                }
            }

            sqlite_datareader = sqlite_cmd.ExecuteReader();

            return sqlite_datareader;
        }

        /// <summary>
        /// Параметр запроса. Имя — с тем же знаком <c>$</c>, что стоит в тексте
        /// запроса и в <c>DecayParentRule.LevelClause</c>.
        ///
        /// ⚠ <c>null</c> уезжает <see cref="DBNull"/>: поставщик на голом
        /// <c>null</c> в значении параметра бросает, а «имени не задано» —
        /// законный случай у читателей этой базы.
        /// </summary>
        public static SqliteParameter Param(string name, object value)
        {
            return new SqliteParameter(name, value ?? DBNull.Value);
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
