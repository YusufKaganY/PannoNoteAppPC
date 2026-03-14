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
                    );

                    CREATE TABLE IF NOT EXISTS Notes (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Title TEXT NOT NULL,
                        Content TEXT
                    );
                ";
                command.ExecuteNonQuery();
            }
        }
    }
}