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
        ("Yeni sürüm olunca haber ver", "Notify me about new versions"),
        ("Antilag açılırken GitHub'dan yalnızca en yeni sürüm numarasına bakar; hesap, program listesi ya da dosya bilgisi göndermez (GitHub, her internet isteğinde olduğu gibi IP adresini görür). Kapalıyken kendiliğinden istek yapılmaz; Şimdi denetle yine çalışır.",
         "When Antilag starts, it only looks up the newest version number on GitHub; it sends no account, program list or file information (as with every internet request, GitHub sees your IP address). While this is off, no request is sent automatically; Check now still works."),
        ("Yeni sürümü kendiliğinden kur", "Install new versions automatically"),
        ("Yeni sürüm bulununca Antilag onu kendisi indirip kurar; birkaç saniye kapanıp yeniden açılır. Yalnızca haber vermesini istersen kapat: o zaman kurulum sen Güncelle'ye basınca olur. CS2 açıkken Antilag zaten hiçbir şey yapmaz.",
         "When a new version is found, Antilag downloads and installs it by itself and briefly closes and reopens. Turn this off if you only want to be told: then you install it by pressing Update. While CS2 is running, Antilag does nothing anyway."),
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
        ("En çok kaynak kullanan 10 program. İşlemci: toplam işlemcinin yüzde kaçı. Okuma/yazma: dosyalardan okuduğu ve dosyalara yazdığı veri. İkisi de sekmeyi açtığın ya da Yenile'ye bastığın andaki 2 saniyeye göredir. Kapat, programı hemen kapatır (5 saniyede kapanmazsa zorla); kaydedilmemiş iş kaybolur, ana ekrandaki geri açma düğmesi onu yeniden açar. Korumalı olanlar kapatılamaz ve ölçülmez.",
         "The 10 programs using the most resources. Processor: the share of the whole processor. Read/write: the data a program reads from and writes to files. Both are measured over 2 seconds when you open this tab or press Refresh. Close shuts a program right away (by force if it hasn't closed within 5 seconds); unsaved work is lost, and the reopen button on the Home screen brings it back. Protected programs can't be closed and aren't measured."),
        ("Kullanım", "Usage"),
        ("İşlemci", "Processor"),
        ("Okuma/yazma", "Read/write"),
        ("Ekran ayarlarını aç", "Open display settings"),
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

        // ------------------------------------------------------------------ the Açılış page (programs that start with Windows)
        ("Açılış", "Startup"),
        ("Windows açılırken kendiliğinden başlayan programlar. Bilgisayarın daha çabuk hazır olmasını istiyorsan, her gün ihtiyacın olmayanları kapatabilirsin.",
         "Programs that start by themselves when Windows starts. If you want your computer to be ready sooner, you can switch off the ones you don't need every day."),
        ("Anahtarı kapatınca program silinmez; yalnızca bir sonraki Windows açılışında kendiliğinden başlamaz. İstediğin zaman geri açabilirsin. Görev Yöneticisi'nin Başlangıç sekmesindeki ayarla aynıdır.",
         "Switching a program off doesn't delete it; it just won't start by itself the next time Windows starts. You can switch it back on whenever you like. It's the same setting as the Startup apps tab of Task Manager."),
        ("Burada Çalıştır kayıtları ve Başlangıç klasörleri listelenir; zamanlanmış görev, hizmet ya da Mağaza uygulaması olarak başlayan programlar yoktur.",
         "This lists the Run entries and the Startup folders; programs that start as a scheduled task, a service or a Store app aren't included."),
        ("CS2 çalışıyor: oyun açıkken Antilag bu listeye bakmaz ve hiçbir şeyi değiştirmez. Oyunu kapatınca liste burada görünür.",
         "CS2 is running: while the game is open, Antilag doesn't look at this list and doesn't change anything. When you close the game, the list appears here."),
        ("Liste okunamadı.", "The list couldn't be read."),
        ("Windows'la birlikte kendiliğinden açılan bir program bulunamadı.", "No program that starts by itself with Windows was found."),
        ("Windows'un Ayarlar > Uygulamalar > Başlangıç sayfasını açar.", "Opens the Settings > Apps > Startup page of Windows."),
        ("Windows'un kendi sayfasını aç", "Open Windows' own page"),

        // ------------------------------------------------------------------ the Kontrol page (computer and internet check)
        ("Kontrol", "Check-up"),
        ("Oyundan önce işine yarayacak iki kısa bakış. İkisi de yalnızca bakar; bilgisayarında hiçbir şeyi değiştirmez.",
         "Two quick looks that help before a game. Both only look; they don't change anything on your computer."),
        ("CS2 çalışıyor: oyun açıkken Antilag bilgisayara bakmaz ve internete hiçbir şey göndermez. Oyunu kapatınca bu sayfa çalışır.",
         "CS2 is running: while the game is open, Antilag doesn't look at your computer and sends nothing to the internet. This page works again when you close the game."),
        ("Bilgisayarın durumu", "Your computer's state"),
        ("Yeniden bak", "Look again"),
        ("Disk alanı, yeniden başlatma, pil, ekran kartı sürücüsü gibi birkaç bilgi. Söyleyecek bir şey olmayan ya da okunamayan konu listede görünmez.",
         "A few facts such as disk space, restarts, the battery and the graphics driver. A topic with nothing to say, or one that can't be read, isn't in the list."),
        ("Bakılıyor…", "Looking…"),
        ("Şu an söylenecek bir şey bulunamadı.", "There's nothing to report right now."),
        ("İnternet kontrolü", "Internet check"),
        ("Modemine, iki bilinen internet adresine ve (dosyada yazdıysan) kendi ping hedefine birkaç saniye ping atar, bir de adres çözme süresine bakar; sorunun evde mi, internet sağlayıcında mı olduğunu söyler. Başka hiçbir şey gönderilmez ve kaydedilmez.",
         "Pings your modem, two well-known internet addresses and (if you wrote one in the settings file) your own ping target for a few seconds and looks at how long a name lookup takes; then tells you whether a problem is at home or with your internet provider. Nothing else is sent or saved."),
        ("Bir çalıştırma, önizleme ya da güncelleme sürerken bakılmaz; bitince burada görünür.", "Nothing is checked while a run, a preview or an update is going on; it appears here when that ends."),
        ("Kontrol et", "Check"),
    };
}
