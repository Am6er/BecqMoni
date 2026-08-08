using System;
using System.IO;
using Microsoft.Data.Sqlite;
using System.Windows.Forms;

namespace BecquerelMonitor.NucBase
{
    public class DataBase
    {
        SqliteConnection sqlite_conn;

        public DataBase()
        {
            CreateConnection();
        }

        SqliteConnection CreateConnection()
        {
            // Каталог приложения, а НЕ текущий: текущий меняет любой диалог открытия
            // файла, после чего база просто не находится. Все остальные читатели баз
            // берут путь так же (T23).
            string DBPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            sqlite_conn = new SqliteConnection("Data Source=" + DBPath + ";Mode=ReadOnly;Cache=Shared;");
            try
            {
                sqlite_conn.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error while open connection: " + ex.Message);
            }
            return sqlite_conn;
        }

        public SqliteDataReader ReadData(string sqlcmd)
        {
            SqliteDataReader sqlite_datareader;
            SqliteCommand sqlite_cmd;
            sqlite_cmd = sqlite_conn.CreateCommand();
            sqlite_cmd.CommandText = sqlcmd;

            sqlite_datareader = sqlite_cmd.ExecuteReader();

            return sqlite_datareader;
        }

        public void Close()
        {
            sqlite_conn.Close();
        }
    }
}
