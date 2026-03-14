using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Windows.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace YusufWidget
{
    public partial class MainWindow : Window
    {
        private const string RegistryAppName = "PannoWidgetApp";

        private DispatcherTimer _reminderTimer;
        public ObservableCollection<TodoItem> Todos { get; set; }
        public ObservableCollection<PersistentNote> Notes { get; set; }

        private PersistentNote _noteToDelete;

        private string dbPath = "Data Source=PannoNotes.db";

        public MainWindow()
        {
            SQLitePCL.Batteries.Init();
            DatabaseHelper.InitializeDatabase();

            InitializeComponent();

            CheckStartupStatus();

            // Hatırlatıcı Zamanlayıcısı
            _reminderTimer = new DispatcherTimer();
            _reminderTimer.Interval = TimeSpan.FromSeconds(30);
            _reminderTimer.Tick += ReminderTimer_Tick;
            _reminderTimer.Start();

            Todos = new ObservableCollection<TodoItem>();
            Notes = new ObservableCollection<PersistentNote>();

            LoadDataFromDatabase();

            // XAML Binding'leri için DataContext'i set ediyoruz
            this.DataContext = this;
        }

        private void LoadDataFromDatabase()
        {
            try
            {
                using (var connection = new SqliteConnection(dbPath))
                {
                    connection.Open();

                    // 1. Görevleri Yükle
                    var todoCmd = connection.CreateCommand();
                    todoCmd.CommandText = "SELECT Id, Text, IsDone, ReminderTime FROM Todos";
                    using (var reader = todoCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Todos.Add(new TodoItem
                            {
                                Id = reader.GetInt32(0),
                                Text = reader.GetString(1),
                                IsDone = reader.GetInt32(2) == 1,
                                ReminderTime = reader.IsDBNull(3) ? null : reader.GetString(3)
                            });
                        }
                    }

                    // 2. Notları Yükle
                    var noteCmd = connection.CreateCommand();
                    noteCmd.CommandText = "SELECT Id, Title, Content FROM Notes";
                    using (var reader = noteCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Notes.Add(new PersistentNote
                            {
                                Id = reader.GetInt32(0),
                                Title = reader.GetString(1),
                                Content = reader.IsDBNull(2) ? "" : reader.GetString(2)
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show("Veri yükleme hatası: " + ex.Message); }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        // --- GÖREV İŞLEMLERİ ---
        private void AddTodo_Click(object sender, RoutedEventArgs e)
        {
            string text = TodoInput.Text;
            string timeInput = TodoTimeInput.Text.Trim();
            string fullReminderTime = null;

            if (!string.IsNullOrWhiteSpace(text))
            {
                // 1. Akıllı Saat Algılama Sistemi
                if (!string.IsNullOrWhiteSpace(timeInput))
                {
                    if (timeInput.Contains(":"))
                    {
                        // Kullanıcı "14:30" formatında belirli bir saat girdiyse
                        fullReminderTime = DateTime.Now.ToString("yyyy-MM-dd") + " " + timeInput;
                    }
                    else if (int.TryParse(timeInput, out int minutes))
                    {
                        // Kullanıcı sadece "1", "5" gibi bir sayı girdiyse (X dakika sonra)
                        fullReminderTime = DateTime.Now.AddMinutes(minutes).ToString("yyyy-MM-dd HH:mm");
                    }
                    else
                    {
                        MessageBox.Show("Lütfen saati '14:30' gibi girin veya '5' yazarak 5 dakika sonrasına kurun.", "Hatalı Saat", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return; // Yanlış formatta veritabanına boşuna kaydetmesini engelle
                    }
                }

                // 2. Veritabanına Kaydetme İşlemi
                using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbPath))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT INTO Todos (Text, IsDone, ReminderTime) VALUES (@Text, 0, @Time); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@Text", text);
                    cmd.Parameters.AddWithValue("@Time", (object)fullReminderTime ?? DBNull.Value);

                    int newId = Convert.ToInt32(cmd.ExecuteScalar());

                    Todos.Add(new TodoItem
                    {
                        Id = newId,
                        Text = text,
                        IsDone = false,
                        ReminderTime = fullReminderTime
                    });

                    TodoInput.Clear();
                    TodoTimeInput.Clear();
                }
            }
        }

        private void TodoInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddTodo_Click(sender, e);
        }

        private async void Todo_Checked(object sender, RoutedEventArgs e)
        {
            var todo = (sender as FrameworkElement)?.DataContext as TodoItem;
            if (todo == null) return;

            // Veritabanı güncelleme
            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE Todos SET IsDone = 1 WHERE Id = @Id";
                cmd.Parameters.AddWithValue("@Id", todo.Id);
                cmd.ExecuteNonQuery();
            }

            TodoList.Items.Refresh();

            // 10 saniye sonra silme kuralı
            await Task.Delay(10000);

            if (todo.IsDone)
            {
                using (var connection = new SqliteConnection(dbPath))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM Todos WHERE Id = @Id";
                    cmd.Parameters.AddWithValue("@Id", todo.Id);
                    cmd.ExecuteNonQuery();
                }
                Todos.Remove(todo);
            }
        }

        private void ReminderTimer_Tick(object sender, EventArgs e)
        {
            // Saniye farkını göz ardı etmek için sadece Yıl-Ay-Gün Saat:Dakika karşılaştırıyoruz
            string currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            foreach (var todo in Todos.ToList()) // Liste üzerinde dönerken silme/değişiklik ihtimaline karşı .ToList()
            {
                if (!todo.IsDone && todo.ReminderTime == currentTime)
                {
                    ShowNotification(todo.Text);

                    // Bildirim sonrası veritabanından ReminderTime'ı temizle (tekrar çalmasın)
                    UpdateReminderInDb(todo.Id, null);
                    todo.ReminderTime = null;
                }
            }
        }

        private void UpdateReminderInDb(int id, string time)
        {
            using (var connection = new SqliteConnection(dbPath))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE Todos SET ReminderTime = @Time WHERE Id = @Id";
                cmd.Parameters.AddWithValue("@Time", (object)time ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.ExecuteNonQuery();
            }
        }

        private void ShowNotification(string message)
        {
            new ToastContentBuilder()
                .AddText("📌 Hatırlatıcı")
                .AddText(message)
                .Show();
        }

        // --- NOT İŞLEMLERİ ---
        private void AddNewNote_Click(object sender, RoutedEventArgs e)
        {
            string title = NewNoteTitleInput.Text;
            if (!string.IsNullOrWhiteSpace(title))
            {
                using (var connection = new SqliteConnection(dbPath))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "INSERT INTO Notes (Title, Content) VALUES (@Title, ''); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@Title", title);

                    int newId = Convert.ToInt32(cmd.ExecuteScalar());
                    Notes.Add(new PersistentNote { Id = newId, Title = title, Content = "" });
                }
                NewNoteTitleInput.Clear();
            }
        }

        private void SaveNote_Click(object sender, RoutedEventArgs e)
        {
            var note = (sender as FrameworkElement)?.DataContext as PersistentNote;
            if (note != null)
            {
                using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbPath))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "UPDATE Notes SET Content = @Content WHERE Id = @Id";
                    cmd.Parameters.AddWithValue("@Content", note.Content ?? "");
                    cmd.Parameters.AddWithValue("@Id", note.Id);
                    cmd.ExecuteNonQuery();
                }

                // Klasik MessageBox YERİNE kendi modern bildirimimizi çağırıyoruz:
                ShowInAppToast("Not başarıyla kaydedildi! 💾");
            }
        }

        private void DeleteNote_Click(object sender, RoutedEventArgs e)
        {
            var note = (sender as FrameworkElement)?.DataContext as PersistentNote;
            if (note != null)
            {
                _noteToDelete = note; // Silinecek notu hafızaya al

                // Kullanıcıya kendi tasarımımızla soruyoruz:
                PopupMessage.Text = $"'{note.Title}' başlıklı notu silmek istediğine emin misin?";
                PopupButtons.Visibility = Visibility.Visible; // Evet/Hayır butonlarını göster
                PopupOverlay.Visibility = Visibility.Visible; // Karanlık arka planı aç
            }
        }

        // "Kaydedildi" gibi 2 saniye görünüp kaybolan tost mesajları için
        private async void ShowInAppToast(string message)
        {
            PopupMessage.Text = message;
            PopupButtons.Visibility = Visibility.Collapsed; // Butonlara gerek yok, gizle
            PopupOverlay.Visibility = Visibility.Visible;

            // 2 saniye boyunca uygulamayı dondurmadan bekle
            await Task.Delay(2000);

            // Ekranda hala bu mesaj varsa kapat (kullanıcı o arada başka bir şeye basmadıysa)
            if (PopupMessage.Text == message)
            {
                PopupOverlay.Visibility = Visibility.Collapsed;
            }
        }

        // "Evet, Sil" butonuna basıldığında
        private void PopupYes_Click(object sender, RoutedEventArgs e)
        {
            if (_noteToDelete != null)
            {
                // Veritabanından sil
                using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbPath))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM Notes WHERE Id = @Id";
                    cmd.Parameters.AddWithValue("@Id", _noteToDelete.Id);
                    cmd.ExecuteNonQuery();
                }

                Notes.Remove(_noteToDelete); // Listeden kaldır
                _noteToDelete = null;        // Hafızayı temizle
            }

            // Popup'ı kapat ve bilgi ver
            PopupOverlay.Visibility = Visibility.Collapsed;
            ShowInAppToast("Not silindi! 🗑️");
        }

        // "İptal" butonuna basıldığında
        private void PopupNo_Click(object sender, RoutedEventArgs e)
        {
            _noteToDelete = null; // İşlemi iptal et
            PopupOverlay.Visibility = Visibility.Collapsed; // Popup'ı gizle
        }

        // -- AYARLAR --
        // --- AYARLAR VE REGISTRY (BAŞLANGIÇTA ÇALIŞTIRMA) İŞLEMLERİ ---

        private void CheckStartupStatus()
        {
            try
            {
                // Windows Registry'ye bakıp bizim uygulama ekli mi diye kontrol ediyoruz
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        object value = key.GetValue(RegistryAppName);
                        // Eğer kayıt varsa CheckBox tikli gelsin, yoksa boş gelsin
                        AutoStartCheckBox.IsChecked = (value != null);
                    }
                }
            }
            catch { }
        }

        private void AutoStart_Checked(object sender, RoutedEventArgs e)
        {
            SetStartup(true);
        }

        private void AutoStart_Unchecked(object sender, RoutedEventArgs e)
        {
            SetStartup(false);
        }

        private void SetStartup(bool enable)
        {
            try
            {
                // Uygulamanın .exe dosyasının tam yolunu alıyoruz
                string appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (enable)
                    {
                        key.SetValue(RegistryAppName, appPath);
                        ShowInAppToast("Otomatik başlatma açıldı! 🚀");
                    }
                    else
                    {
                        key.DeleteValue(RegistryAppName, false);
                        ShowInAppToast("Otomatik başlatma kapatıldı. 🛑");
                    }
                }
            }
            catch (Exception ex)
            {
                // Yetki hatası vs. olursa kullanıcıya söyleyelim
                MessageBox.Show("Ayar değiştirilemedi: " + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class TodoItem
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public bool IsDone { get; set; }
        public string ReminderTime { get; set; }
    }

    public class PersistentNote
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
    }
}