using Microsoft.Data.Sqlite;

namespace YusufWidget
{
    public static class DatabaseHelper
    {
        private static string dbPath = "Data Source=PannoNotes.db";

        public static void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();
                var command = connection.CreateCommand();

                // IsDone satırının sonundaki virgüle dikkat!
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Todos (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Text TEXT NOT NULL,
                        IsDone INTEGER NOT NULL,
                        ReminderTime TEXT
                        OrderIndex INTEGER DEFAULT 0
                    );

                    CREATE TABLE IF NOT EXISTS Notes (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Title TEXT NOT NULL,
                        Content TEXT
                    );

                    -- Ayarlar Tablosu
                    CREATE TABLE IF NOT EXISTS AppSettings (
                        Id INTEGER PRIMARY KEY,
                        Theme TEXT,
                        Opacity REAL
                    );
                    
                    -- Eğer tablo boşsa varsayılan ayarları (Sarı ve %100 görünür) ekle
                    INSERT OR IGNORE INTO AppSettings (Id, Theme, Opacity) VALUES (1, 'Yellow', 1.0);
                ";
                command.ExecuteNonQuery();
                try
                {
                    var alterCmd = connection.CreateCommand();
                    alterCmd.CommandText = "ALTER TABLE Todos ADD COLUMN OrderIndex INTEGER DEFAULT 0;";
                    alterCmd.ExecuteNonQuery();
                }
                catch
                {
                    // Eğer sütun zaten varsa SQLite hata fırlatır, catch bloğu ile bu hatayı sessizce yoksayarız.
                }
            }
        }
    }
}