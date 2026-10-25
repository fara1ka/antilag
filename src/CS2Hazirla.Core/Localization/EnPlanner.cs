namespace CS2Hazirla.Core;

/// <summary>English for the texts of: Planner.cs. Only the owner of this area edits this file.</summary>
public static class EnPlanner
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // ------------------------------------------------------------------ a planning step that could not be checked
        ("{0} kontrol edilemedi, bu adım atlandı.", "{0} couldn't be checked, so this step was skipped."),
        // the six step names (data: they are passed to Guard as Turkish text and translated where the warning is made)
        ("Programlar", "Programs"),
        ("Windows hizmetleri", "Windows services"),
        ("Güç planı", "Power plan"),
        ("Windows ayarları", "Windows settings"),
        ("İnternet ayarları", "Internet settings"),
        ("Fare ve klavye ayarları", "Mouse and keyboard settings"),

        // ------------------------------------------------------------------ programs
        // the title of a row: name, memory, how many parts of the program run ("parça" = the separate pieces a program is made of)
        ("{0}  ({1}, {2} parça)", "{0}  ({1}, {2} {2?part:parts})"),
        // what a program is counted as (the line under its name; the categories of the catalog are translated in EnCatalog)
        ("Senin eklediğin program", "A program you added"),
        ("Tanınmayan arka plan programı", "Unrecognized background program"),
        ("Açık pencereli uygulama", "App with an open window"),
        ("Geri yüklenince açılmaz, elle açman gerekir", "Won't reopen when restored; open it yourself"),
        ("'{0}' Windows, Steam, FACEIT ya da ekran/ses sürücüsüne ait olduğundan kapatılmayacak.", "'{0}' is part of Windows, Steam, FACEIT or your graphics or sound drivers, so it won't be closed."),
        // the heading of the rows of the windowed programs (PlannedAction.Group: Turkish identity, translated where the heading is shown)
        ("Açık duran programlar (kaydedilmemiş iş kaybolur)", "Open programs (unsaved work is lost)"),

        // ------------------------------------------------------------------ Windows services
        ("'{0}' Windows için gerekli olduğundan durdurulmayacak.", "'{0}' is needed by Windows, so it won't be stopped."),
        ("'{0}' durdurulamıyor (Windows izin vermiyor), atlanacak.", "'{0}' can't be stopped (Windows doesn't allow it), so it will be skipped."),
        ("bağımlı: {0}", "dependent services: {0}"),
        // the next two are also texts of the Settings page (EnXaml): the English must stay the same there
        ("Geçici durdurulacak Windows hizmetleri", "Windows services to stop temporarily"),
        ("Yalnızca durdurulur, silinmez; bilgisayarı yeniden başlatınca ya da geri yüklenince geri gelir.", "They are only stopped, never deleted; they come back when you restart the computer or restore them."),

        // ------------------------------------------------------------------ power plan
        ("Güç planı zaten yüksek performans ({0}).", "Power plan is already set to high performance ({0})."),
        ("Bilgisayar en yüksek performans modunda çalışacak", "Your PC will run in its highest-performance mode"),
        ("Şu an '{0}' modu açık; mevcut Nihai Performans planı. Geri yüklenince eski mod geri gelir.",
         "The '{0}' mode is on right now; the existing Ultimate Performance plan will be used. The old mode comes back when you restore."),
        ("Şu an '{0}' modu açık; Nihai Performans planı oluşturulacak (olmazsa Yüksek Performans). Geri yüklenince eski mod geri gelir.",
         "The '{0}' mode is on right now; an Ultimate Performance plan will be created (or High Performance if that isn't possible). The old mode comes back when you restore."),

        // ------------------------------------------------------------------ Windows settings
        ("{0}: zaten ayarlı.", "{0}: already set."),
        ("Windows Oyun Modu açık", "Windows Game Mode on"),
        ("Windows oyun sırasında arka plan işlerini kısar. Geri yüklenince eski ayar geri gelir.", "Windows cuts back background tasks while you play. The old setting comes back when you restore."),
        ("Xbox Game Bar oyun kaydı kapalı", "Xbox Game Bar game recording off"),
        ("Windows'un oyunu arka planda kaydetmeye hazır beklemesi durur. OBS gibi programlar etkilenmez.", "Windows stops standing by in the background to record your game. Programs like OBS aren't affected."),
        ("Windows bildirimleri kapalı", "Windows notifications off"),
        ("Sağ alttan çıkan bildirim kutuları Geri yüklenene kadar kapalı kalır.", "Notification pop-ups at the bottom right stay off until you restore your settings."),
        ("CS2'nin kurulu olduğu yer bulunamadı: CS2 için ekran kartı ayarları atlandı.", "Couldn't find where CS2 is installed: the graphics card settings for CS2 were skipped."),
        ("CS2 için güçlü ekran kartı seçili", "Powerful graphics card chosen for CS2"),
        ("Windows Ayarlar > Ekran > Grafik bölümündeki seçeneğin aynısı; yalnızca CS2 için. Oyun dosyalarına ve oyunun kendisine dokunulmaz.",
         "Same as the option in Windows Settings > Display > Graphics; for CS2 only. The game files and the game itself aren't touched."),
        ("CS2 için tam ekran iyileştirmesi kapalı", "Fullscreen optimizations off for CS2"),
        ("Yalnızca CS2 için: Windows'un tam ekran oyunlara eklediği uyumluluk katmanını bu oyun için kapatır. Tam temizlik ve Yayıncı'da açıktır; Ayrıntılar'dan kapatabilirsin.",
         "For CS2 only: turns off the compatibility layer Windows adds to fullscreen games. It's on in the Full cleanup and Streamer presets; you can turn it off in Details."),

        // ------------------------------------------------------------------ internet
        // (PlannedAction.Group "İnternet": Turkish identity, same English as the catalog's group)
        ("İnternet", "Internet"),
        ("İnternet adres önbelleğini temizle (DNS)", "Clear the internet address cache (DNS)"),
        ("Eski internet adresi kayıtlarını siler; bir sonraki bağlantıda yenilenir. Ping'i düşürmez.", "Deletes old internet address records; they refresh on the next connection. It won't lower your ping."),
        ("Güç tasarrufunu kapat: {0}", "Turn off power saving: {0}"),
        ("'Güç tasarrufu için bu aygıtı kapatabilir' işareti kaldırılır, geri yüklenince geri gelir",
         "The 'Allow the computer to turn off this device to save power' box is unchecked; the check comes back when you restore"),

        // ------------------------------------------------------------------ mouse and keyboard
        ("Fare ivmesini kapat (bu oturum için)", "Turn off mouse acceleration (this session only)"),
        ("Kalıcı kaydedilmez; Windows'u yeniden başlatınca ya da geri yüklenince eski değer geri gelir", "Not saved permanently; the old setting comes back when you restart Windows or restore"),
        ("Fare ivmesi zaten kapalı.", "Mouse acceleration is already off."),
        ("Yapışkan/Filtre/Geçiş tuşları kısayollarını kapat (bu oturum için)", "Turn off the Sticky/Filter/Toggle Keys shortcuts (this session only)"),
        ("Shift x5 gibi kısayollar oyunu simge durumuna küçültebilir; kayıtlı ayarlar yeniden yazılmaz", "Shortcuts like pressing Shift 5 times can minimize the game; your saved settings aren't rewritten"),
        ("Yapışkan/Filtre/Geçiş tuşları kısayolları zaten kapalı.", "Sticky/Filter/Toggle Keys shortcuts are already off."),

        // ------------------------------------------------------------------ advice (read-only lines of the Details panel)
        ("Wi-Fi kullanıyorsun. Mümkünse internet kablosu (Ethernet) kullan; kablosuz bağlantıdaki dalgalanma ve paket kaybını hiçbir ayar düzeltemez.",
         "You're using Wi-Fi. If you can, use an internet cable (Ethernet); no setting can fix the fluctuation and packet loss of a wireless connection."),
        // {0} = the speed the memory runs at, {1} = the speed it was made for (MT/s)
        ("Belleğin (RAM) olabileceğinden yavaş çalışıyor olabilir ({0:F0} yerine {1:F0} MT/s hız için üretilmiş). Bilgisayarın BIOS ayarlarında 'XMP' ya da 'EXPO' diye bir seçenek varsa açmak oyunda fark edilebilir; emin değilsen bir uzmana sor. (Uygulama BIOS'a dokunmaz, bu yalnızca bir bilgi.)",
         "Your memory (RAM) may be running slower than it could (at {0:F0} MT/s instead of the {1:F0} MT/s it was made for). If your computer's BIOS settings have an option called 'XMP' or 'EXPO', turning it on can make a noticeable difference in games; if you're not sure, ask an expert. (The app doesn't touch the BIOS; this is just for your information.)"),
    };
}
