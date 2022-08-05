using System;
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
            string DBPath = Environment.CurrentDirectory + "\\nucdb.sqlite;";
            sqlite_conn = new SqliteConnection("Data Source=" + DBPath + "Mode=ReadOnly;Cache=Shared;");
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
            //while (sqlite_datareader.Read())
            //{
            //    string myreader = sqlite_datareader.GetString(0);
            //}
        }

        public void Close()
        {
            sqlite_conn.Close();
        }
    }
}
