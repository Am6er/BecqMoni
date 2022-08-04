using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BecquerelMonitor.NucBase
{
    public class DataBase
    {
        SQLiteConnection sqlite_conn;

        public DataBase()
        {
            CreateConnection();
        }

        SQLiteConnection CreateConnection()
        {
            sqlite_conn = new SQLiteConnection("Data Source=NucBase/DB/nucdb.sqlite; Version = 3; New = True; Compress = True;");
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

        public SQLiteDataReader ReadData(string sqlcmd)
        {
            SQLiteDataReader sqlite_datareader;
            SQLiteCommand sqlite_cmd;
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
