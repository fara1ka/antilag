namespace CS2Hazirla.Core;

/// <summary>English for the texts of: Orchestrator.cs. Only the owner of this area edits this file.</summary>
public static class EnOrchestrator
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // ---- refusals (thrown when a run is started in a state that does not allow it)
        ("Önceki oturum henüz geri yüklenmedi. Önce önceki çalıştırmayı geri yükle.",
         "The previous run hasn't been restored yet. Restore it first."),
        ("CS2 zaten çalışıyor. Antilag yalnızca oyun açılmadan ÖNCE çalışır; oyunu kapatıp tekrar dene.",
         "CS2 is already running. Antilag only works BEFORE the game starts. Close the game and try again."),

        // ---- the run (Başlat)
        ("✓ Eski ayarların yedeği alındı.", "✓ Your old settings were backed up."),
        ("Önce mevcut durumu ölçüyorum (internet gecikmesi, boş bellek, işlemci). Birkaç saniye sürer…",
         "First I'm measuring how things are now (internet delay, free memory, processor). This takes a few seconds…"),
        ("Sonucu ölçüyorum…", "Measuring the result…"),
        ("CS2 açıldığı için işlem durduruldu. Oyun açıkken hiçbir şeye dokunmuyorum.",
         "Stopped because CS2 started. I don't touch anything while the game is open."),
        ("✓ İnternet adres önbelleği (DNS) temizlendi.", "✓ Internet address cache (DNS) cleared."),
        ("✓ {0}.", "✓ {0}."),
        // a step that could not be done: also the front of "Şu adım yapılamadı: Güç planı" in the restore
        ("Şu adım yapılamadı: {0}", "Couldn't do this step: {0}"),
        ("Şu adım yapılamadı: Güç planı (uygun plan bulunamadı)", "Couldn't do this step: Power plan (no suitable plan found)"),

        // ---- closing programs
        // The skipped line "{0}: artık çalışmıyor" is read by the home screen (it hides these lines from the list with Contains on the
        // Turkish fragment): the English template contains the English of the fragment, which is the entry right below it.
        ("{0}: artık çalışmıyor", "{0}: no longer running"),
        ("artık çalışmıyor", "no longer running"),
        ("{0} program kapatılıyor… (önce nazikçe; kapanmayanlar en geç 6 saniye sonra zorla kapatılır)",
         "Closing {0} {0?program:programs}… (asking nicely first; any that don't close are closed by force within 6 seconds)"),
        ("Kendini yeniden açan {0} program tekrar kapatıldı.",
         "Closed {0} {0?program:programs} again after {0?it:they} reopened on {0?its:their} own."),
        ("{0} kapanmadı (Windows kapatmaya izin vermedi).", "{0} didn't close (Windows didn't allow it)."),
        ("{0} kapanmadı (kapatma isteği kabul edildi ama program hâlâ çalışıyor).",
         "{0} didn't close (the close request was accepted, but the program is still running)."),
        ("{0} tekrar açıldı, kapatılamadı.", "{0} opened again and couldn't be closed."),
        ("{0} kapatıldı", "{0} closed"),
        ("✓ {0} kapatıldı.", "✓ {0} closed."),
        ("✓ {0} kapatıldı (kapanmadığı için zorla).", "✓ {0} closed (by force, because it wouldn't close)."),
        ("{0} zorla kapatılamadı.", "{0} couldn't be closed by force."),
        ("{0}.exe korumalı: kapatılmadı.", "{0}.exe is protected: not closed."),
        ("CS2 çalışıyor: oyun açıkken hiçbir uygulama kapatılmaz.", "CS2 is running: no app is closed while the game is open."),
        ("Önceki çalıştırmanın kaydı daha yeni bir Antilag sürümüyle yazılmış: kayıt bozulmasın diye şimdi bir program kapatılmadı.",
         "The previous run's record was written by a newer version of Antilag, so no program was closed this time (to keep the record from being damaged)."),

        // ---- Windows services
        ("{0}: korumalı hizmet", "{0}: protected service"),
        ("{0}: çalışmıyor", "{0}: not running"),
        ("Hizmet durduruldu: {0}", "Service stopped: {0}"),
        ("✓ Windows hizmeti durduruldu: {0}.", "✓ Windows service stopped: {0}."),

        // ---- power plan
        ("Nihai Performans güç planı hazırlandı.", "The Ultimate Performance power plan was set up."),
        ("Nihai Performans güç planı hazırlanamadı; Yüksek Performans denenecek.",
         "Couldn't set up the Ultimate Performance power plan; trying High Performance."),
        ("Yüksek Performans güç planı da hazırlanamadı.", "Couldn't set up the High Performance power plan either."),
        ("Güç planı en yüksek performansa alındı", "Power plan set to highest performance"),
        ("✓ Güç planı en yüksek performansa alındı (eski mod: {0}). Geri yüklenince eski haline döner.",
         "✓ Power plan set to highest performance (previous mode: {0}). It goes back to how it was when you restore."),
        ("bilinmiyor", "unknown"),

        // ---- mouse, sticky keys, network card
        ("✓ Fare ivmesi bu oturum için kapatıldı.", "✓ Mouse acceleration turned off for this session."),
        ("✓ Yapışkan Tuşlar penceresi bu oturum için engellendi.", "✓ The Sticky Keys window was blocked for this session."),
        ("Ağ kartının enerji tasarrufu kapatıldı: {0}", "Network card power saving turned off: {0}"),
        ("✓ Ağ kartının ({0}) enerji tasarrufu kapatıldı.", "✓ Power saving turned off for the network card ({0})."),

        // ---- restore (Geri yükle)
        ("Önceki çalıştırmanın kaydı daha yeni bir Antilag sürümüyle yazılmış; bu sürüm onu geri yükleyemez. Antilag'ı güncelleyip yeniden dene.",
         "The previous run's record was written by a newer version of Antilag; this version can't restore it. Update Antilag and try again."),
        ("Geri yüklenecek bir şey yok.", "There's nothing to restore."),
        ("CS2 çalışıyor: oyun açıkken geri yükleme çalışmaz (anti-cheat güvenliği). Oyunu kapattıktan sonra tekrar dene.",
         "CS2 is running: restoring doesn't work while the game is open (anti-cheat safety). Try again after you close the game."),
        ("Önceki çalıştırmanın ayarları geri yükleniyor…", "Restoring the settings of the previous run…"),
        ("Geri yüklüyorum: ayarlar eski haline dönüyor, kapatılan programlar yeniden başlatılıyor…",
         "Restoring: settings are going back to how they were and closed programs are starting again…"),
        ("Önceki ayarlar geri yükleniyor (programlar şimdilik açılmıyor: Antilag onları zaten tekrar kapatacak)…",
         "Restoring the previous settings (programs aren't reopened for now: Antilag will close them again anyway)…"),

        // the name of a step, which goes behind "Şu adım yapılamadı: " when it fails
        ("Fare ivmesi", "Mouse acceleration"),
        ("Yapışkan/Filtre tuşları", "Sticky/Filter Keys"),
        ("Ağ kartı güç tasarrufu", "Network card power saving"),
        ("Windows ayarı", "Windows setting"),
        ("Hizmet: {0}", "Service: {0}"),
        ("Güç planı", "Power plan"),

        // what was put back
        ("Fare ivmesi eski haline döndü", "Mouse acceleration restored"),
        ("Yapışkan/Filtre/Geçiş tuşları eski haline döndü", "Sticky/Filter/Toggle Keys restored"),
        ("Ağ kartı güç tasarrufu eski haline döndü", "Network card power saving restored"),
        ("Windows ayarları: {0} değişiklik eski haline döndü", "Windows settings: {0} {0?change:changes} restored"),
        ("Arka plan hizmetleri: {0} hizmet eski durumuna döndü", "Background services: {0} {0?service:services} restored"),
        ("Güç planı eski haline döndü", "Power plan restored"),
        ("Güç planı eski haline döndü (oluşturulan plan silindi)", "Power plan restored (the plan that was created has been deleted)"),

        // the record file (state.json) holds something the app would never have written
        ("Kayıt dosyasındaki bir Windows ayarı tanınmadığı için atlandı (güvenlik).",
         "A Windows setting in the record file was skipped because it wasn't recognized (security)."),
        ("Kayıt dosyasındaki bir hizmet adı geçersiz olduğu için atlandı (güvenlik).",
         "A service name in the record file was skipped because it isn't valid (security)."),
        ("Kayıt dosyasındaki güç planı kimliği geçersiz, atlandı (güvenlik).",
         "The power plan ID in the record file isn't valid, so it was skipped (security)."),
        ("Kayıt dosyasındaki oluşturulan güç planı kimliği geçersiz, atlandı (güvenlik).",
         "The ID of the created power plan in the record file isn't valid, so it was skipped (security)."),

        // reopening closed programs
        ("CS2 çalıştığı için kalan programlar açılmadı. Oyunu kapattıktan sonra geri yüklemeyi tekrar dene.",
         "The remaining programs weren't reopened because CS2 is running. Try restoring again after you close the game."),
        ("Geri yükleme tam yapılamadı ({0} sorun). Yedek korundu, tekrar deneyebilirsin.",
         "Restoring wasn't fully completed ({0} {0?problem:problems}). The backup was kept, so you can try again."),
        // (the second one is also the headline text of the status area: the same English there)
        ("Önceki ayarlar eski haline döndü.", "The previous settings are back to how they were."),
        ("Ayarlar ve hizmetler eski haline döndü.", "Settings and services are back to how they were."),
        ("{0} bir Microsoft Store uygulaması: otomatik açılamaz, Başlat menüsünden açabilirsin.",
         "{0} is a Microsoft Store app: it can't be opened automatically, but you can open it from the Start menu."),
        ("{0} (Store uygulaması)", "{0} (Store app)"),
        ("{0}: başlatma yolu güvenli değil ya da dosya yok, atlandı.", "{0}: skipped because its path isn't safe or the file is missing."),
        ("{0} başlatıldı", "{0} started"),
        ("✓ {0} başlatıldı.", "✓ {0} started."),
        ("{0} başlatılamadı.", "{0} couldn't be started."),
        ("Şu programlar kendiliğinden açılamadı, gerekirse kendin aç: {0}.",
         "These programs couldn't reopen on their own. Open them yourself if you need them: {0}."),
        ("Geri yükleme yok sayıldı: kayıt dosyası (state.json) bir kenara alındı.",
         "Restore was ignored: the record file (state.json) was set aside."),
    };
}
