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
using System.ComponentModel;
using System.Runtime.CompilerServices;
using forms = System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Controls;

namespace YusufWidget
{
    public partial class MainWindow : Window
    {
        private const string RegistryAppName = "PannoWidgetApp";

        private forms.NotifyIcon _notifyIcon;

        // --- GLOBAL KISAYOL (HOTKEY) İÇİN WINDOWS API ---
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_P = 0x50; // Klavyedeki 'P' tuşu

        private DispatcherTimer _reminderTimer;
        public ObservableCollection<TodoItem> Todos { get; set; }
        public ObservableCollection<PersistentNote> Notes { get; set; }

        private Action _lastUndoAction; // undo

        private PersistentNote _noteToDelete;

        private string dbPath = "Data Source=PannoNotes.db";
        private string _currentTheme = "Yellow";

        public MainWindow()
        {
            SQLitePCL.Batteries.Init();
            DatabaseHelper.InitializeDatabase();

            InitializeComponent();

            CheckStartupStatus();

            // Windows bildirimlerindeki tıklamaları dinlemek için 
            ToastNotificationManagerCompat.OnActivated += ToastNotificationManagerCompat_OnActivated;

            // Sistem Tepsisi (System Tray) İkonu Kurulumu
            _notifyIcon = new forms.NotifyIcon();
            _notifyIcon.Icon = new System.Drawing.Icon("note.ico");
            _notifyIcon.Text = "Panno Widget App";
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, args) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
            };

            // Hatırlatıcı Zamanlayıcısı
            _reminderTimer = new DispatcherTimer();
            _reminderTimer.Interval = TimeSpan.FromSeconds(1);
            _reminderTimer.Tick += ReminderTimer_Tick;
            _reminderTimer.Start();

            Todos = new ObservableCollection<TodoItem>();
            Notes = new ObservableCollection<PersistentNote>();

            LoadDataFromDatabase();
            LoadSettings();

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
                    todoCmd.CommandText = "SELECT Id, Text, IsDone, ReminderTime, OrderIndex FROM Todos ORDER BY OrderIndex ASC";
                    using (var reader = todoCmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            Todos.Add(new TodoItem  
                            {
                                Id = reader.GetInt32(0),
                                Text = reader.GetString(1),
                                IsDone = reader.GetInt32(2) == 1,
                                ReminderTime = reader.IsDBNull(3) ? null : reader.GetString(3),
                                OrderIndex = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
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

        //--- GLOBAL KISAYOL (HOTKEY) İŞLEMLERİ ---
        // Pencere ilk yüklendiğinde Windows'a "Ben Ctrl+Shift+P'yi dinleyeceğim" diyoruz
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(HwndHook); // Dinleyiciyi tak

            // Ctrl (0x0002) + Shift (0x0004) + P (0x50) tuşunu sisteme kaydet
            RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_P);
        }

        // Windows'tan gelen her tuş sinyalini yakalayan kanca (Hook)
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            // Eğer basılan tuş bizim belirlediğimiz Ctrl+Shift+P ise
            // Eğer basılan tuş bizim belirlediğimiz Ctrl+Shift+P ise
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                if (this.Visibility == Visibility.Visible)
                {
                    // Yumuşak Kapanış Animasyonu (Fade Out)
                    DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
                    fadeOut.Completed += (s, args) =>
                    {
                        this.Hide();
                        this.Opacity = OpacitySlider.Value; // Asıl şeffaflığını geri ver
                    };
                    this.BeginAnimation(Window.OpacityProperty, fadeOut);

                    _notifyIcon.ShowBalloonTip(1000, "Panno", "Kısayolla gizlendi.", forms.ToolTipIcon.Info);
                }
                else
                {
                    // Yumuşak Açılış Animasyonu (Fade In)
                    this.Opacity = 0; // Önce tamamen görünmez yap
                    this.Show();
                    this.WindowState = WindowState.Normal;
                    this.Activate();

                    DoubleAnimation fadeIn = new DoubleAnimation(OpacitySlider.Value, TimeSpan.FromMilliseconds(200));
                    this.BeginAnimation(Window.OpacityProperty, fadeIn);
                }
                handled = true;
            }

            return IntPtr.Zero;
        }

        // Uygulama tamamen kapanırken (Çarpıya basınca) Windows'a "Tuşu geri bıraktım" diyoruz
        protected override void OnClosed(EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HOTKEY_ID);
            base.OnClosed(e);
        }

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
                        fullReminderTime = DateTime.Now.ToString("yyyy-MM-dd") + " " + timeInput + ":00 ";
                    }
                    else if (int.TryParse(timeInput, out int minutes))
                    {
                        // Kullanıcı sadece "1", "5" gibi bir sayı girdiyse (X dakika sonra)
                        fullReminderTime = DateTime.Now.AddMinutes(minutes).ToString("yyyy-MM-dd HH:mm:ss");
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

                UpdateTodoOrderInDb();
            }
        }

        // Farenin sol tuşuna basılı tutup sürüklemeye başladığında
        private void TodoItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var panel = sender as StackPanel;
                var todo = panel?.DataContext as TodoItem;
                if (todo != null)
                {
                    // Sürükleme işlemini başlat (Sistem bu veriyi hafızaya alır)
                    DragDrop.DoDragDrop(panel, todo, DragDropEffects.Move);
                }
            }
        }

        // Görevi yeni yerine bıraktığında
        private void TodoList_Drop(object sender, DragEventArgs e)
        {
            var droppedTodo = e.Data.GetData(typeof(TodoItem)) as TodoItem;
            var targetTodo = (e.OriginalSource as FrameworkElement)?.DataContext as TodoItem;

            if (droppedTodo != null && targetTodo != null && droppedTodo != targetTodo)
            {
                // Listeden eski yerinden sil, yeni hedefin yerine ekle
                int oldIndex = Todos.IndexOf(droppedTodo);
                int newIndex = Todos.IndexOf(targetTodo);

                Todos.RemoveAt(oldIndex);
                Todos.Insert(newIndex, droppedTodo);

                // Veritabanındaki sıralamayı (OrderIndex) toptan güncelle
                UpdateTodoOrderInDb();
            }
        }

        private void UpdateTodoOrderInDb()
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbPath))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "UPDATE Todos SET OrderIndex = @OrderIndex WHERE Id = @Id";
                    cmd.Parameters.Add("@OrderIndex", Microsoft.Data.Sqlite.SqliteType.Integer);
                    cmd.Parameters.Add("@Id", Microsoft.Data.Sqlite.SqliteType.Integer);

                    for (int i = 0; i < Todos.Count; i++)
                    {
                        Todos[i].OrderIndex = i;
                        cmd.Parameters["@OrderIndex"].Value = i;
                        cmd.Parameters["@Id"].Value = Todos[i].Id;
                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
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
            DateTime now = DateTime.Now;

            foreach (var todo in Todos.ToList())
            {
                if (!todo.IsDone && todo.HasReminder)
                {
                    // Metin formatındaki saati gerçek tarihe çeviriyoruz
                    if (DateTime.TryParse(todo.ReminderTime, out DateTime reminderDate))
                    {
                        TimeSpan diff = reminderDate - now;

                        if (diff.TotalSeconds <= 0)
                        {
                            // SÜRE BİTTİ
                            ShowAlarmNotification(todo); 
                            
                            todo.ReminderTime = null;
                            todo.TimeLeft = "⏰ Çalıyor..."; // Geri sayım yerine bu yazacak
                            UpdateReminderInDb(todo.Id, null);
                        }
                        else
                        {
                            // SÜRE VAR: Geri sayımı ekranda anlık güncelle
                            if (diff.TotalHours >= 1)
                                todo.TimeLeft = $"{(int)diff.TotalHours}s {diff.Minutes}d kaldı";
                            else if (diff.TotalMinutes >= 1)
                                todo.TimeLeft = $"{diff.Minutes}d {diff.Seconds}sn kaldı";
                            else
                                todo.TimeLeft = $"{diff.Seconds}sn kaldı";
                        }
                    }
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

        private void ShowAlarmNotification(TodoItem todo) 
        {
            new ToastContentBuilder()
                .AddText("⏰ Panno Alarm!")
                .AddText(todo.Text)
                .SetToastScenario(ToastScenario.Alarm) 

                // Kendi Sustur butonumuz:
                .AddButton(new ToastButton()
                    .SetContent("Sustur")
                    .AddArgument("action", "dismiss")
                    .AddArgument("todoId", todo.Id.ToString()))

                // Kendi Ertele butonumuz:
                .AddButton(new ToastButton()
                    .SetContent("5 Dk Ertele")
                    .AddArgument("action", "snooze")
                    .AddArgument("snoozeTime", "5")
                    .AddArgument("todoId", todo.Id.ToString()))
                .Show();
        }

        private void ToastNotificationManagerCompat_OnActivated(ToastNotificationActivatedEventArgsCompat toastArgs)
        {
            ToastArguments args = ToastArguments.Parse(toastArgs.Argument);

            if (args.TryGetValue("todoId", out string idStr) && args.TryGetValue("action", out string action))
            {
                int id = int.Parse(idStr);

                // Ekran güncellemeleri için WPF'nin ana iş parçacığına (UI Thread) dönüyoruz
                Dispatcher.Invoke(() =>
                {
                    var todo = Todos.FirstOrDefault(t => t.Id == id);
                    if (todo != null)
                    {
                        if (action == "snooze")
                        {
                            int mins = int.Parse(args["snoozeTime"]);
                            // Saniyeler dahil yeni zamanı hesaplayıp listeye ve veritabanına yazıyoruz!
                            todo.ReminderTime = DateTime.Now.AddMinutes(mins).ToString("yyyy-MM-dd HH:mm:ss");
                            UpdateReminderInDb(todo.Id, todo.ReminderTime);
                            ShowInAppToast($"{mins} dakika ertelendi! 💤");
                        }
                        else if (action == "dismiss")
                        {
                            todo.ReminderTime = null;
                            todo.TimeLeft = "";
                            UpdateReminderInDb(todo.Id, null);
                        }
                    }
                });
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            // Görev çubuğundan tamamen gizle ve gizli simge çubuğuna at
            this.Hide();
            _notifyIcon.ShowBalloonTip(2000, "Panno", "Arka planda çalışmaya devam ediyorum.", forms.ToolTipIcon.Info);
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            // Durumu tam tersine çevir
            this.Topmost = !this.Topmost;

            // Eğer üstte değilse butonu biraz soluklaştırarak kullanıcıya belli et
            PinButton.Opacity = this.Topmost ? 1.0 : 0.5;

            ShowInAppToast(this.Topmost ? "Ekrana sabitlendi 📌" : "Sabitleme kaldırıldı 🔓");
        }

        private void CancelReminder_Click(object sender, RoutedEventArgs e)
        {
            var todo = (sender as FrameworkElement)?.DataContext as TodoItem;
            if (todo != null)
            {
                // Veritabanından ve ekrandan hatırlatıcıyı siler
                UpdateReminderInDb(todo.Id, null);
                todo.ReminderTime = null;
                todo.TimeLeft = "";
                ShowInAppToast("Hatırlatıcı iptal edildi! 🔕");
            }
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

        // Kopyala Butonuna basıldığında tetiklenir
        private void CopyNote_Click(object sender, RoutedEventArgs e)
        {
            var note = (sender as FrameworkElement)?.DataContext as PersistentNote;

            // Eğer not boş değilse içindeki metni Windows Panosuna (Clipboard) kopyala
            if (note != null && !string.IsNullOrWhiteSpace(note.Content))
            {
                System.Windows.Clipboard.SetText(note.Content);
                ShowInAppToast("Panoya kopyalandı! 📋");
            }
            else
            {
                ShowInAppToast("Kopyalanacak metin yok! 🤷‍♂️");
            }
        }

        // "Kaydedildi" gibi 2 saniye görünüp kaybolan tost mesajları için
        // Tost mesajını Geri Al destekli hale getirdik
        private async void ShowInAppToast(string message, bool showUndo = false)
        {
            PopupMessage.Text = message;
            PopupButtons.Visibility = Visibility.Collapsed;
            UndoButton.Visibility = showUndo ? Visibility.Visible : Visibility.Collapsed;
            PopupOverlay.Visibility = Visibility.Visible;

            await Task.Delay(3000); // Geri almak için 3 saniye süre veriyoruz

            if (PopupMessage.Text == message)
            {
                PopupOverlay.Visibility = Visibility.Collapsed;
                if (showUndo) _lastUndoAction = null; // Süre dolunca hafızayı sil
            }
        }

        // Geri Al butonuna basıldığında
        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_lastUndoAction != null)
            {
                _lastUndoAction.Invoke(); // Kaydedilen geri alma işlemini çalıştır
                _lastUndoAction = null;
                ShowInAppToast("İşlem geri alındı! ✨");
            }
            else
            {
                PopupOverlay.Visibility = Visibility.Collapsed;
            }
        }

        // "Evet, Sil" butonuna basıldığında
        private void PopupYes_Click(object sender, RoutedEventArgs e)
        {
            if (_noteToDelete != null)
            {
                // 1. ADIM: Silmeden HEMEN ÖNCE yedeği güvene alıyoruz!
                var deletedNote = _noteToDelete;

                // 2. ADIM: Veritabanından sil
                using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbPath))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM Notes WHERE Id = @Id";
                    cmd.Parameters.AddWithValue("@Id", deletedNote.Id); // _noteToDelete yerine yedeği kullandık
                    cmd.ExecuteNonQuery();
                }

                Notes.Remove(deletedNote); // Listeden kaldır
                _noteToDelete = null;      // Artık güvenle ana hafızayı temizleyebiliriz

                // 3. ADIM: Geri Al (Undo) Hafızası
                _lastUndoAction = () =>
                {
                    // Geri Al denirse veritabanına tekrar ekle
                    using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbPath))
                    {
                        connection.Open();
                        var cmd = connection.CreateCommand();
                        cmd.CommandText = "INSERT INTO Notes (Id, Title, Content) VALUES (@Id, @Title, @Content)";
                        cmd.Parameters.AddWithValue("@Id", deletedNote.Id);
                        cmd.Parameters.AddWithValue("@Title", deletedNote.Title);
                        cmd.Parameters.AddWithValue("@Content", deletedNote.Content);
                        cmd.ExecuteNonQuery();
                    }
                    Notes.Add(deletedNote); // Ekrana geri getir
                };

                // Popup'ı kapat ve mesajı yolla
                PopupOverlay.Visibility = Visibility.Collapsed;
                ShowInAppToast("Not silindi! 🗑️", true);
            }
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

        // Slider her hareket ettiğinde tetiklenir
        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (this.IsLoaded)
            {
                // DİKKAT: Animasyonun Opacity (Saydamlık) üzerindeki kilidini kaldırıyoruz!
                this.BeginAnimation(Window.OpacityProperty, null);

                this.Opacity = OpacitySlider.Value;
            }
        }

        // SADECE fare tıkını bıraktığında (sürükleme bitince) veritabanına kaydet
        private void OpacitySlider_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SaveSettings(_currentTheme, this.Opacity);
        }

        // Tema butonlarına tıklandığında çalışır
        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            _currentTheme = btn.Tag.ToString();
            ApplyTheme(_currentTheme);

            // Temayı kaydet
            SaveSettings(_currentTheme, this.Opacity);
        }

        // Renk kodlarını Widget'a uygular
        private void ApplyTheme(string theme)
        {
            var brushConverter = new System.Windows.Media.BrushConverter();

            // ÖNCE HER ŞEYİ VARSAYILAN (AYDINLIK) RENKLERE ÇEKELİM
            this.Resources["HeaderBrush"] = brushConverter.ConvertFromString("#854D0E");
            this.Resources["TextBrush"] = brushConverter.ConvertFromString("Black");

            System.Windows.Media.Brush bgBrush = null;
            System.Windows.Media.Brush borderBrush = null;

            switch (theme)
            {
                case "Yellow":
                    bgBrush = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#FEF9C3");
                    borderBrush = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#EAB308");
                    break;
                case "Blue":
                    bgBrush = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#DBEAFE");
                    borderBrush = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#3B82F6");
                    break;
                case "Green":
                    bgBrush = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#DCFCE7");
                    borderBrush = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#22C55E");
                    break;
                case "Pink":
                    bgBrush = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#FCE7F3");
                    borderBrush = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#EC4899");
                    break;
                case "Dark":
                    bgBrush = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#1E293B");
                    borderBrush = (System.Windows.Media.Brush)brushConverter.ConvertFromString("#334155");

                    this.Resources["HeaderBrush"] = brushConverter.ConvertFromString("#F8FAFC");
                    this.Resources["TextBrush"] = brushConverter.ConvertFromString("#E2E8F0");
                    break;
            }

            // Seçilen renkleri hem ana pencereye hem de Popup'a uygula
            if (bgBrush != null && borderBrush != null)
            {
                MainBorder.Background = bgBrush;
                MainBorder.BorderBrush = borderBrush;

                if (PopupBorder != null)
                {
                    PopupBorder.Background = bgBrush;
                    PopupBorder.BorderBrush = borderBrush;
                }
            }
        }

        // --- AYAR HAFIZASI (PERSISTENCE) ---
        private void LoadSettings()
        {
            try
            {
                using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbPath))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Theme, Opacity FROM AppSettings WHERE Id = 1";
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            _currentTheme = reader.GetString(0);
                            double savedOpacity = reader.GetDouble(1);

                            // Arayüzü güncelle
                            OpacitySlider.Value = savedOpacity;
                            this.Opacity = savedOpacity; // BURA ÇOK ÖNEMLİ: Açılışta doğrudan uygula
                            ApplyTheme(_currentTheme);
                        }
                    }
                }
            }
            catch { /* İlk açılışta veritabanı kilitli vs. olursa çökmesin */ }
        }

        private void SaveSettings(string theme, double opacity)
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbPath))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE AppSettings SET Theme = @Theme, Opacity = @Opacity WHERE Id = 1";
                cmd.Parameters.AddWithValue("@Theme", theme);
                cmd.Parameters.AddWithValue("@Opacity", opacity);
                cmd.ExecuteNonQuery();
            }
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

    // Geri sayımın ekranda anlık güncellenmesini sağlayan akıllı sınıf
    public class TodoItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public bool IsDone { get; set; }

        private int _orderIndex;
        public int OrderIndex
        {
            get => _orderIndex;
            set { _orderIndex = value; OnPropertyChanged(); }
        }

        private string _reminderTime;
        public string ReminderTime
        {
            get => _reminderTime;
            set { _reminderTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasReminder)); }
        }

        private string _timeLeft;
        public string TimeLeft
        {
            get => _timeLeft;
            set { _timeLeft = value; OnPropertyChanged(); }
        }

        // Hatırlatıcı varsa iptal butonunu göstermek için bir kontrol
        public bool HasReminder => !string.IsNullOrEmpty(ReminderTime);

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class PersistentNote
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
    }
}