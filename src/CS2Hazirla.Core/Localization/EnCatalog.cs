namespace CS2Hazirla.Core;

/// <summary>English for the texts of: Catalog.cs, FriendlyNames.cs, HomeApps.cs, ReopenPolicy.cs, ProtectedList.cs, SearchText.cs, SteamLocator.cs. Only the owner of this area edits this file.</summary>
public static class EnCatalog
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // ------------------------------------------------------------------ group headings (Catalog.Group*; the Turkish text stays the identity of the group)
        ("Kapatılacak programlar", "Programs to close"),
        ("Arka plan hizmetleri (geçici durdurulur)", "Background services (stopped temporarily)"),
        ("Windows ayarları (geri yüklenince eski haline gelir)", "Windows settings (back to how they were when restored)"),
        ("İnternet", "Internet"),

        // ------------------------------------------------------------------ programs: titles
        ("Discord", "Discord"),
        ("GoodbyeDPI", "GoodbyeDPI"),
        ("Diğer engel aşma programları (Zapret, Tor…)", "Other block-bypass programs (Zapret, Tor…)"),
        ("Tarayıcılar (Chrome, Edge, Firefox…)", "Browsers (Chrome, Edge, Firefox…)"),
        ("Yedekleme programları (OneDrive, Dropbox…)", "Backup programs (OneDrive, Dropbox…)"),
        ("Diğer oyun mağazaları (Epic, Battle.net, EA…)", "Other game stores (Epic, Battle.net, EA…)"),
        ("Program güncelleyicileri (Adobe, Java, Google…)", "Program updaters (Adobe, Java, Google…)"),
        ("Bilgisayar markası ve ışık programları (Armoury Crate, MSI Center…)", "PC maker and lighting programs (Armoury Crate, MSI Center…)"),
        ("Klavye, fare ve kulaklık programları (Logitech, Razer, SteelSeries…)", "Keyboard, mouse and headset programs (Logitech, Razer, SteelSeries…)"),
        ("Ekran üstü araçlar (NVIDIA Overlay, Xbox Game Bar…)", "On-screen overlay tools (NVIDIA Overlay, Xbox Game Bar…)"),
        ("VPN ve oyun hızlandırıcılar (NordVPN, ExitLag…)", "VPN and game accelerators (NordVPN, ExitLag…)"),
        ("Sonradan kurulan antivirüs programı (Windows Defender hariç)", "Other antivirus programs (except Windows Defender)"),
        ("Hile koruma programları (Vanguard, EasyAntiCheat, ESEA, Esportal…)", "Anti-cheat programs (Vanguard, EasyAntiCheat, ESEA, Esportal…)"),
        ("Açık duran diğer programlar", "Other open programs"),
        ("Claude (yapay zekâ asistanı)", "Claude (AI assistant)"),
        ("Müzik ve sohbet programları (Spotify, Teams…)", "Music and chat programs (Spotify, Teams…)"),
        ("Tanınmayan arka plan programları", "Unrecognized background programs"),
        ("Her seferinde kapat listesindeki programlar", "Programs on your 'Always close' list"),

        // ------------------------------------------------------------------ programs: hints and cautions
        ("Pencere ve simge kapanır; hizmet olarak çalışan kısmı açık kalır.", "The window and icon close; the part that runs as a service stays open."),
        ("Pencere açmadan çalışan, listede olmayan programlar.", "Programs that run without a window and aren't on the list."),
        ("Kapanırsa bu programın koruması durabilir.", "If it closes, this program's protection may stop."),
        ("ESEA, Esportal, Gamers Club ya da Valorant kullanıyorsan bu anahtarı kapalı tut.", "If you use ESEA, Esportal, Gamers Club or Valorant, keep this switch off."),
        ("Kaydedilmemiş iş kaybolur.", "Unsaved work is lost."),
        ("Açık Claude oturumu kapanır.", "Your open Claude session will close."),
        ("İndirme yöneticisi gibi bir program yarıda kalabilir.", "A program like a download manager may be interrupted."),

        // ------------------------------------------------------------------ Windows background services: switches
        ("Windows güncellemelerini geçici durdur", "Temporarily stop Windows updates"),
        ("Gereksiz Windows arka plan hizmetlerini durdur", "Stop unneeded Windows background services"),
        ("Yazıcı, arama dizinleme vb. Yazdıracaksan kapalı tut.", "Printer, search indexing, etc. Keep it off if you need to print."),

        // ------------------------------------------------------------------ Windows settings
        ("En yüksek performans güç planı", "Highest-performance power plan"),
        ("Bilgisayar tam güçte çalışır; dizüstünde pil çabuk biter.", "Your PC runs at full power; on a laptop the battery drains fast."),
        ("Windows Oyun Modu", "Windows Game Mode"),
        ("Windows oyun sırasında arka plan işlerini kısar.", "Windows cuts back background tasks while you play."),
        ("Xbox Game Bar oyun kaydını kapat", "Turn off Xbox Game Bar game recording"),
        ("Arka planda oyun kaydı için hazır beklemeyi durdurur.", "Stops Windows from standing by in the background to record your game."),
        ("Windows bildirimlerini sustur", "Mute Windows notifications"),
        ("Bildirimler geri yüklenene kadar kapalı kalır.", "Notifications stay off until your settings are restored."),
        ("CS2 için güçlü ekran kartını seç", "Pick the powerful graphics card for CS2"),
        ("İki ekran kartlı bilgisayarlarda CS2’yi güçlü kartla açar.", "On PCs with two graphics cards, opens CS2 on the powerful one."),
        ("CS2 için tam ekran iyileştirmesini kapat", "Turn off fullscreen optimizations for CS2"),
        ("Windows’un tam ekran oyunlara eklediği uyumluluk katmanı.", "A compatibility layer Windows adds to fullscreen games."),
        ("Fare ivmesini kapat", "Turn off mouse acceleration"),
        ("İmleç hızlı harekette orantısız hızlanmaz; yalnızca bu oturumda.", "The cursor won't speed up unevenly on fast moves; this session only."),
        ("Yapışkan Tuşlar penceresini engelle", "Block the Sticky Keys window"),
        ("Shift’e 5 kez basınca açılan pencere oyunu küçültebilir.", "The window that opens when you press Shift 5 times can minimize the game."),

        // ------------------------------------------------------------------ internet
        ("DNS önbelleğini temizle", "Clear the DNS cache"),
        ("Eski internet adresi kayıtlarını siler; ping’i düşürmez.", "Deletes old internet address records; it won't lower your ping."),
        ("Ağ kartı güç tasarrufunu kapat", "Turn off network card power saving"),
        ("Ağ kartının uyutulup kısa süre kopmasını önler.", "Stops the network card from sleeping and dropping out briefly."),

        // ------------------------------------------------------------------ program categories (the names the plan and the lists give to what a switch closes)
        ("Tarayıcılar", "Browsers"),
        ("Yedekleme programları", "Backup programs"),
        ("Diğer oyun mağazaları", "Other game stores"),
        ("Program güncelleyicileri", "Program updaters"),
        ("Klavye, fare ve kulaklık programları", "Keyboard, mouse and headset programs"),
        ("Bilgisayar markası ve ışık programları", "PC maker and lighting programs"),
        ("Ekran üstü araçlar", "On-screen overlay tools"),
        ("Müzik ve sohbet programları", "Music and chat programs"),
        ("Claude", "Claude"),
        ("VPN ve oyun hızlandırıcılar", "VPN and game accelerators"),
        ("Sonradan kurulan antivirüs", "Other antivirus programs"),
        ("Diğer oyunların hile korumaları", "Other games' anti-cheat"),
        ("Engel aşma programları", "Block-bypass programs"),

        // ------------------------------------------------------------------ Windows services: the name and the plain explanation
        ("Windows Update", "Windows Update"),
        ("Güncellemeleri arka planda indirir ve kurar.", "Downloads and installs updates in the background."),
        ("Güncelleme Orkestratörü", "Update Orchestrator"),
        ("Windows Update'in zamanlamasını yönetir.", "Manages when Windows Update runs."),
        ("Arka plan dosya aktarımı", "Background file transfer"),
        ("Güncellemeler gibi dosyaları arka planda indirir.", "Downloads files such as updates in the background."),
        ("Teslim İyileştirme", "Delivery Optimization"),
        ("Güncellemeleri diğer bilgisayarlarla paylaşıp internet kullanır.", "Shares updates with other computers and uses your internet."),
        ("Windows Arama", "Windows Search"),
        ("Dosyaları aramayı hızlandırmak için diski sürekli tarar.", "Keeps scanning the disk to make file searches faster."),
        ("Yazıcı hizmeti", "Printer service"),
        ("Yazdırma işleri içindir. Yazıcın yoksa gerekmez.", "It handles printing. You don't need it if you have no printer."),
        ("Ölçüm ve veri gönderme", "Usage data reporting"),
        ("Microsoft'a kullanım verisi gönderir.", "Sends usage data to Microsoft."),
        ("Tahmini önbellek (SysMain)", "Predictive cache (SysMain)"),
        ("Sık açtığın programları önceden belleğe yükler; RAM'i doldurur.", "Preloads the programs you open often into memory; it fills up RAM."),
        ("İndirilmiş haritalar", "Downloaded maps"),
        ("Çevrimdışı harita verisini günceller.", "Updates offline map data."),
        ("Konum hizmeti", "Location service"),
        ("Bilgisayarın konumunu uygulamalara verir.", "Gives your computer's location to apps."),
        ("Hata raporlama", "Error reporting"),
        ("Çöken programların raporunu Microsoft'a gönderir.", "Sends reports about crashed programs to Microsoft."),
        ("Xbox oturum hizmeti", "Xbox sign-in service"),
        ("Xbox hesabıyla giriş içindir.", "Used to sign in with an Xbox account."),
        ("Xbox oyun kaydı bulutu", "Xbox cloud game saves"),
        ("Xbox oyun kayıtlarını buluta yükler.", "Uploads Xbox game saves to the cloud."),
        ("Xbox ağ hizmeti", "Xbox network service"),
        ("Xbox çok oyunculu bağlantıları içindir.", "Used for Xbox multiplayer connections."),
    };
}
