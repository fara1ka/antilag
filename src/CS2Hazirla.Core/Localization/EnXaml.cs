namespace CS2Hazirla.Core;

/// <summary>English for the texts of: the literal texts of every .xaml file (MainWindow, PreviewWindow, PickerWindow, DialogWindow, AppListCard). Only the owner of this area edits this file.</summary>
public static class EnXaml
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // ------------------------------------------------------------------ the left bar
        ("Antilag", "Antilag"),
        ("Input lag'ı azaltmak için tasarlanmıştır. Arka planda çalışan gereksiz uygulamaları kapatır.", "Designed to reduce input lag. Closes unnecessary apps running in the background."),
        ("Ana ekran", "Home"),
        ("Profiller", "Profiles"),
        ("Ayarlar", "Settings"),
        ("Uygulamayı tamamen kapatır; arkada çalışan hiçbir şey kalmaz. Yaptığın değişiklikler yerinde durur.", "Closes the app completely; nothing is left running in the background. The changes you made stay in place."),
        ("Uygulamayı kapat", "Close app"),
        ("Vazgeç", "Cancel"),
        ("created by claude via fara1k", "created by claude via fara1k"),

        // ------------------------------------------------------------------ home page
        ("Yönetici değil: bazı işlemler başarısız olur", "Not running as administrator: some actions will fail"),
        ("Başlat", "Start"),
        ("Seçilen programları kapatır ve ayarları uygular. Neler yapıldığı aşağıda satır satır görünür.", "Closes the selected programs and applies the settings. What was done appears line by line below."),
        ("Uygulamayı tamamen kapatır; arkada hiçbir şey çalışmaya devam etmez. Sonra CS2'yi sen aç.", "Closes the app completely; nothing keeps running in the background. Then open CS2 yourself."),
        // the two lines of the big round Close button (each Run is looked up alone: "Close" over "app")
        ("Uygulamayı", "Close"),
        ("kapat", "app"),
        ("Kapatılacak uygulamalar", "Apps to close"),
        ("taranıyor…", "scanning…"),
        ("Yeniden tara", "Scan again"),
        ("Kapatılabilecek büyük bir uygulama bulunamadı.", "No large apps that can be closed were found."),
        ("Neler yapıldı", "What was done"),
        ("çalışıyor…", "working…"),
        ("Neyin kapanacağını ve hangi ayarların yapılacağını tek tek göster", "Shows what will be closed and which settings will be applied, one by one"),
        ("Ayrıntılar", "Details"),
        ("Hiçbir şey yapmadan, şu an nelerin kapanacağını listeler.", "Lists what would be closed right now, without doing anything."),
        ("Önizleme", "Preview"),
        ("Önceki çalıştırmanın ayarlarını geri alıp temizliği baştan yapar.", "Restores the settings from the previous run, then does the cleanup again from the start."),
        ("Tekrar çalıştır", "Run again"),

        // ------------------------------------------------------------------ profiles page
        ("Ayarlarını bir profil olarak kaydet. Ana ekranda profili seçip Başlat'a basınca o ayarlarla çalışır.", "Save your settings as a profile. Choose the profile on the Home screen and press Start to run with those settings."),
        ("seçili", "in use"),
        ("Henüz profilin yok.", "You don't have any profiles yet."),
        ("Yeni profil", "New profile"),
        ("Soldan bir profil seç ya da yeni bir profil oluştur.", "Choose a profile on the left or create a new one."),
        ("Profilin adı", "Profile name"),
        ("Profil adı", "Profile name"),
        ("Başlangıç noktası", "Starting point"),
        ("Hafif", "Light"),
        ("Tam temizlik", "Full cleanup"),
        ("Yayıncı", "Streamer"),
        ("Bu profili seç", "Use this profile"),
        ("Kaydeder ve ana ekrana döner; Başlat'a basınca bu profildeki ayarlarla çalışır.", "Saves and goes back to the Home screen; when you press Start, it runs with this profile's settings."),
        ("Kaydet", "Save"),
        ("Profili sil", "Delete profile"),
        ("Silinsin mi?", "Delete it?"),
        ("Evet, sil", "Yes, delete"),
        ("Bu profilde neler kapansın?", "What should this profile close?"),
        ("Anahtarı açık olanlar, bu profille Başlat'a basınca yapılır. Kaydet'e basınca profile yazılır.", "Anything switched on happens when you press Start with this profile. Press Save to store your choices in the profile."),

        // ------------------------------------------------------------------ settings page
        // the language card says both names whatever language is on (same entries as in EnCommon)
        ("Dil / Language", "Dil / Language"),
        ("Türkçe", "Türkçe"),
        ("English", "English"),
        ("CS2 çalışıyor: oyun açıkken Antilag hiçbir şey yapmaz; bu sayfadaki liste düğmeleri oyun kapanınca çalışır.", "CS2 is running: Antilag does nothing while the game is open; the list buttons on this page work once the game is closed."),
        ("Her şey listeden seçilir; bir şey yazman gerekmez. Değişiklikler kendiliğinden kaydedilir.", "Everything is chosen from lists; you never have to type anything. Changes are saved automatically."),
        ("Kapattığım programları geri açsın", "Reopen the programs I closed"),
        ("Kapalıyken geri yükleme yalnızca ayarları ve hizmetleri eski haline getirir. Kendini güncelleyen programlar, arka plan yardımcıları, engel aşma programları ve Mağaza uygulamaları açılamaz; hangileri açılmadıysa sonunda adlarıyla yazılır.",
         "When this is off, restoring only puts the settings and services back as they were. Self-updating programs, background helpers, block-bypass programs and Store apps can't be reopened; any that weren't reopened are listed by name at the end."),
        ("Yeni sürüm olunca haber ver", "Notify me about new versions"),
        ("Antilag açılırken GitHub'dan yalnızca en yeni sürüm numarasına bakar; hesap, program listesi ya da dosya bilgisi göndermez (GitHub, her internet isteğinde olduğu gibi IP adresini görür). Kapalıyken kendiliğinden istek yapılmaz; Şimdi denetle yine çalışır. İndirme ve kurulum yalnızca sen Güncelle'ye basınca olur.",
         "When Antilag starts, it only looks up the newest version number on GitHub; it sends no account, program list or file information (as with every internet request, GitHub sees your IP address). While this is off, no request is sent automatically; Check now still works. Downloading and installing only happen when you press Update."),
        ("Şimdi denetle", "Check now"),
        ("Geçici durdurulacak Windows hizmetleri", "Windows services to stop temporarily"),
        ("Yalnızca durdurulur, silinmez; bilgisayarı yeniden başlatınca ya da geri yüklenince geri gelir.", "They are only stopped, never deleted; they come back when you restart the computer or restore them."),
        ("Listeden hizmet seç", "Choose a service from the list"),
        ("Her zaman korunanlar", "Always protected"),
        ("Bunlar bilgisayarın ve oyunun çalışması için şart olduğundan hiçbir ayarla kapanmaz: CS2 ve Steam, FACEIT, Windows'un kendi parçaları, Windows Defender, ekran kartı ve ses kartı sürücüleri.",
         "These are essential for the computer and the game to work, so no setting ever closes them: CS2 and Steam, FACEIT, Windows' own parts, Windows Defender, and the graphics card and sound card drivers."),
        ("Hakkında", "About"),
        ("Kaynak kod", "Source code"),
        ("Lisans ve bildirimler", "License and notices"),

        // ------------------------------------------------------------------ the Details panel
        ("Seçimler, Başlat'a bastığında uygulanır.", "Your choices are applied when you press Start."),
        ("Ayrıntıları kapat", "Close details"),
        ("Özet", "Summary"),
        ("Tam liste", "Full list"),
        ("Ölçümler", "Measurements"),
        ("Bellek", "Memory"),
        ("Program ya da ayar ara…", "Search programs or settings…"),
        ("Ayrıntılarda ara", "Search details"),
        ("Aramayı temizle", "Clear search"),
        ("Eşleşen bir şey yok. Başka bir sözcük dene. CS2, Steam, Windows parçaları ve “Her zaman açık kalsın” listendeki programlar hiçbir ayarda kapanmaz; o yüzden burada çıkmaz.",
         "Nothing matches. Try another word. CS2, Steam, Windows parts and the programs on your “Always keep open” list are never closed by any setting, so they don't show up here."),
        ("Windows hizmetleri, Windows ayarları ve internet ayarları için Tam liste'ye bak.", "For Windows services, Windows settings and internet settings, see Full list."),
        ("Öneriler", "Suggestions"),
        ("Başlat'tan sonra önceki ve sonraki değerler burada görünür. Antilag ping'i düşürmez ve FPS'i sihirli biçimde artırmaz; arka plandaki yükü azaltır. Bilgisayar zaten temizse değerler değişmez, bu normaldir.",
         "After you press Start, the values from before and after show up here. Antilag doesn't lower your ping or magically raise your FPS; it reduces the load in the background. If your computer is already clean, the values don't change, and that's normal."),
        ("Ölçüm", "Measurement"),
        ("Önce", "Before"),
        // the "after" column header is not here: "Sonra" is also the update card's button ("Later", EnUpdate) and one Turkish text has one English, so MainViewModel.MetricsAfterHeader chooses it
        ("Fark", "Change"),
        ("En çok bellek kullanan 10 program. Kapat, programı hemen kapatır (5 saniyede kapanmazsa zorla); kaydedilmemiş iş kaybolur. Kapatılan programı ana ekrandaki geri açma düğmesi yeniden açar. Korumalı olanlar kapatılamaz.",
         "The 10 programs using the most memory. Close shuts a program right away (by force if it hasn't closed within 5 seconds); unsaved work is lost. The reopen button on the Home screen opens a closed program again. Protected programs can't be closed."),
        ("Kapat", "Close"),
        ("Yenile", "Refresh"),

        // ------------------------------------------------------------------ the list picker window
        ("Listeden seç", "Choose from list"),
        ("Listede ara…", "Search the list…"),
        ("Listede ara", "Search the list"),
        ("Listeyi süzmek için yazabilirsin", "You can type to filter the list"),

        // ------------------------------------------------------------------ the preview window
        ("Bir şeyin yapılmasını istemiyorsan ana ekrandaki listeden ya da Ayrıntılar'dan kapat.", "If you don't want something to be done, switch it off in the list on the Home screen or in Details."),

        // ------------------------------------------------------------------ the small buttons of a list row
        ("Çıkar", "Remove"),
    };
}
