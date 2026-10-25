namespace CS2Hazirla.Core;

/// <summary>English for the texts of: App.xaml.cs, MainWindow.xaml.cs, DialogWindow.*, PickerWindow.*, PreviewWindow.*, AppListCard.*. Only the owner of this area edits this file.</summary>
public static class EnWindows
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // ---- App.xaml.cs: the dialogs of start-up
        ("CS2 şu an çalışıyor. Oyun açıkken bu uygulama hiçbir şey yapmaz ve kapanır.\n\nOyunu kapattıktan sonra tekrar aç.",
         "CS2 is running right now. While the game is open, this app does nothing and closes.\n\nOpen it again after you close the game."),
        ("Antilag zaten çalışıyor (görev yöneticisinde Antilag.exe).",
         "Antilag is already running (see Antilag.exe in Task Manager)."),
        ("Beklenmeyen bir hata oluştu.\n\nAyrıntı günlük dosyasına yazıldı.",
         "Something unexpected went wrong.\n\nThe details were saved in the log file."),
        ("Uygulama başlatılamadı.\n\nAyrıntı günlük dosyasına yazıldı.",
         "The app couldn't start.\n\nThe details were saved in the log file."),
        ("Antilag yalnızca yönetici olarak çalışır (Windows hizmetlerini, güç planını ve ayarlarını değiştirmek için gerekli).\n\nUygulamaya sağ tıklayıp 'Yönetici olarak çalıştır' de; Windows izin isterse 'Evet'e bas.",
         "Antilag only runs as administrator (it needs this to change Windows services, the power plan and settings).\n\nRight-click the app, choose 'Run as administrator', and press 'Yes' if Windows asks for permission."),
        ("Yönetici yetkisi gerekli", "Administrator rights needed"),

        // ---- DialogWindow.xaml.cs: the buttons
        ("Hayır", "No"),
        ("Evet", "Yes"),
        ("Vazgeç", "Cancel"),
        ("Tamam", "OK"),

        // ---- PickerWindow.xaml.cs
        ("Ekle", "Add"),
        ("Listelenecek program yok.", "There are no programs to list."),
        ("Aramana uyan program yok.", "No programs match your search."),
        ("{0} seçenek", "{0} {0?option:options}"),
        ("{0} tanesi seçildi", "{0} selected"),

        // ---- MainWindow.xaml.cs
        ("Şu an bir işlem sürüyor. Bitmesini bekle; sonra Uygulamayı kapat'a bas.",
         "Something is still in progress. Wait for it to finish, then press Close app."),

        // ---- MainWindow.xaml.cs, DEBUG render demos (a dialog and the picker)
        ("Önceki çalıştırma tam geri yüklenemedi:\n• Güç planı geri yüklenemedi\n\nBu kayıt yok sayılıp bir kenara alınsın mı?\nHayır dersen kayıt korunur ve daha sonra tekrar denenebilir.",
         "The previous run couldn't be fully restored:\n• The power plan couldn't be restored\n\nShould this record be ignored and set aside?\nIf you choose No, the record is kept and you can try again later."),
        ("Geri yükleme tamamlanamadı", "Restore couldn't be completed"),
        ("Her zaman açık kalacak programları seç", "Choose the programs to always keep open"),
        ("Çalışan programlar listelendi. Kapanmasını asla istemediklerini işaretle.",
         "Running programs are listed. Select the ones you never want closed."),
        ("1,2 GB bellek kullanıyor · ekranda açık", "Using 1.2 GB of memory · open on screen"),
        ("310 MB bellek kullanıyor · ekranda açık", "Using 310 MB of memory · open on screen"),
        ("95 MB bellek kullanıyor · arka planda çalışıyor", "Using 95 MB of memory · running in the background"),
        ("480 MB bellek kullanıyor · ekranda açık", "Using 480 MB of memory · open on screen"),
    };
}
