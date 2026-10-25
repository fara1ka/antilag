namespace CS2Hazirla.Core;

/// <summary>English for the texts of: ViewModels.cs. Only the owner of this area edits this file.</summary>
public static class EnViewModels
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // ------------------------------------------------------------------ the activity list
        // the closing line of a run starts with this word: the list turns it green by looking for it (LogEntry.From), so the two lines below must start with it too
        ("Bitti.", "Done."),
        ("Bitti. {0} işlem yapıldı.", "Done. {0} {0?action:actions} completed."),
        ("Bitti. {0} işlem yapıldı, {1} işlem tam yapılamadı.", "Done. {0} {0?action:actions} completed, {1} {1?action:actions} couldn't be fully completed."),
        ("Antilag başladı (seçili ayar: {0}).", "Antilag started (selected preset: {0})."),
        ("Neler yapılacağı belirleniyor…", "Working out what to do…"),
        ("Plan hazır: {0} program kapatılacak, {1} Windows hizmeti durdurulacak, {2} ayar yapılacak.",
         "Plan ready: {0} {0?program:programs} will be closed, {1} Windows {1?service:services} will be stopped, {2} {2?setting:settings} will be changed."),
        ("Hizmetler yalnızca durdurulur, silinmez; bilgisayarı yeniden başlatınca ya da geri yüklenince geri gelir.",
         "Services are only stopped, never deleted; they come back when you restart the computer or restore your settings."),
        ("Atlandı: {0}", "Skipped: {0}"),
        ("Oyunu başlatmadan önce Antilag'ı kapat.", "Close Antilag before you start the game."),
        ("Hazırlık sürerken CS2 açıldı; kalan işlemler iptal edildi. Oyunu kapattıktan sonra Antilag'ı yeniden açıp geri yükle.",
         "CS2 started while Antilag was getting ready; the remaining actions were canceled. After you close the game, open Antilag again and restore your settings."),
        ("İşlem sırasında bir sorun çıktı.", "Something went wrong during the run."),
        ("Geri yükleme sırasında sorun çıktı.", "Something went wrong while restoring."),
        ("Önceki çalıştırma geri yüklenemedi.", "The previous run couldn't be restored."),
        ("Ayarlar kaydedilemedi.", "Your settings couldn't be saved."),
        ("Yönetici olarak çalışmıyorsun: bazı işlemler başarısız olabilir. Uygulamayı yönetici olarak başlat.",
         "You're not running as administrator: some actions may fail. Start the app as administrator."),
        ("CS2 şu an çalışıyor. Oyun açıkken Antilag hiçbir şey yapmaz (hile koruma programlarıyla sorun çıkmaması için). Oyunu kapattıktan sonra tekrar dene.",
         "CS2 is running right now. While the game is open, Antilag does nothing (so it can't cause problems with anti-cheat programs). Try again after you close the game."),
        ("CS2 çalışıyor: bu uygulama oyun açıkken hiçbir işlem yapmaz ve kapanıyor.",
         "CS2 is running: this app does nothing while the game is open, so it is closing."),

        // ------------------------------------------------------------------ the record of an earlier run (start-up)
        ("Önceki çalıştırmanın kaydı daha yeni bir Antilag sürümüyle yazılmış; bu sürüm onu geri yükleyemez.",
         "The previous run's record was written by a newer version of Antilag; this version can't restore it."),
        ("Önceki çalıştırmanın kaydı bu Antilag sürümünden daha yeni olduğu için geri yüklenemiyor. Kayıt silinmedi.\n\nAntilag'ı en son sürüme güncelle, sonra ana ekrandaki geri yükleme düğmesini kullan.",
         "The previous run's record can't be restored because it is newer than this version of Antilag. The record was not deleted.\n\nUpdate Antilag to the latest version, then use the restore button on the Home screen."),
        ("Antilag: önceki çalıştırma", "Antilag: previous run"),
        ("Önceki çalıştırmanın kaydı okunamadı, bir kenara alındı.", "The record of the previous run couldn't be read and was set aside."),
        ("Önceki çalıştırmanın kaydı okunamadığı için değişiklikler bu uygulamayla geri yüklenemiyor.\n\nWindows'u yeniden başlatmak durdurulan hizmetleri geri getirir. Kapattığın programları kendin açabilirsin; güç planı gibi ayarlar kalmış olabilir.",
         "The changes can't be restored with this app because the record of the previous run couldn't be read.\n\nRestarting Windows brings back the services that were stopped. You can reopen the closed programs yourself; settings such as the power plan may have stayed changed."),

        // ------------------------------------------------------------------ the question when a restore was not complete
        ("Önceki çalıştırma tam geri yüklenemedi:\n• {0}", "The previous run couldn't be fully restored:\n• {0}"),
        ("Ayrıca şu programlar kendiliğinden açılamadı; gerekirse kendin aç:\n• {0}",
         "These programs also couldn't reopen on their own; open them yourself if needed:\n• {0}"),
        ("Bu kayıt yok sayılıp bir kenara alınsın mı?\n(Windows'u yeniden başlatmak durdurulan hizmetleri geri getirir; ama güç planı, kayıt defteri ve ağ kartı ayarları kendiliğinden eski haline dönmez.)\nHayır dersen kayıt korunur ve daha sonra tekrar denenebilir.",
         "Should this record be ignored and set aside?\n(Restarting Windows brings back the services that were stopped, but the power plan, the registry and the network card settings don't go back to normal on their own.)\nIf you choose No, the record is kept and you can try again later."),

        // ------------------------------------------------------------------ the headline and the ring of the home page
        ("Hazırlanıyor", "Getting ready"),
        ("Önceki çalıştırmanın ayarları geri yükleniyor; hemen ardından yeniden başlayacak.",
         "The settings from the previous run are being restored; it will start again right after."),
        ("Kapatılan programlar açılıyor, ayarlar ve hizmetler eski haline getiriliyor.",
         "Closed programs are being reopened, and settings and services are being put back as they were."),
        ("Ayarlar ve hizmetler eski haline getiriliyor.", "Settings and services are being put back as they were."),
        ("Kapatılan uygulamaları geri aç", "Reopen closed apps"),
        ("Ayarları geri yükle", "Restore settings"),
        ("adım", "steps"),

        // ------------------------------------------------------------------ the tooltip of the restore button
        ("Henüz geri yüklenecek bir şey yok. Başlat'a bastıktan sonra buradan ayarları eski haline getirebilirsin.",
         "There's nothing to restore yet. After you press Start, you can put your settings back from here."),
        ("Kapatılan programları yeniden açar, değiştirilen ayarları ve durdurulan hizmetleri eski haline getirir. Kendini güncelleyen programlar, arka plan yardımcıları, engel aşma programları ve Mağaza uygulamaları açılamaz; hangileri açılmadıysa sonunda söylenir.",
         "Reopens the programs that were closed and puts changed settings and stopped services back as they were. Self-updating programs, background helpers, block-bypass programs and Store apps can't be reopened; any that weren't reopened are mentioned at the end."),
        ("Değiştirilen ayarları ve durdurulan hizmetleri eski haline getirir. Programları açmaz: Ayarlar'da 'Kapattığım programları geri açsın' kapalı.",
         "Puts changed settings and stopped services back as they were. Doesn't reopen programs: 'Reopen the programs I closed' is off in Settings."),

        // ------------------------------------------------------------------ the lists of Ayarlar and the pickers
        ("Her seferinde kapanacak programları seç", "Choose the programs to always close"),
        ("Çalışan programlar listelendi. Gruplarda olmayan ama her seferinde kapanmasını istediklerini işaretle.",
         "Running programs are listed. Select the ones that aren't in the groups but that you want closed every time."),
        ("Her zaman açık kalsın", "Always keep open"),
        ("Buraya eklediğin programlar, hangi ayar seçili olursa olsun kapatılmaz.", "Programs you add here are never closed, whichever preset is selected."),
        ("Henüz bir şey eklemedin.", "You haven't added anything yet."),
        ("Henüz bir şey eklemedin. Her zaman açık kalmasını istediğin bir program varsa ekle.",
         "You haven't added anything yet. If there's a program you always want to stay open, add it."),
        ("Her seferinde kapat", "Always close"),
        ("Gruplarda olmayan ama her seferinde kapanmasını istediğin programlar.", "Programs that aren't in the groups but that you want closed every time."),
        ("Senin eklediğin hizmet.", "A service you added."),
        ("{0} bellek kullanıyor · ekranda açık", "Using {0} of memory · open on screen"),
        ("{0} bellek kullanıyor · arka planda çalışıyor", "Using {0} of memory · running in the background"),
        ("Eklenebilecek başka bir program bulunamadı.", "There are no other programs to add."),
        ("Eklenebilecek başka bir hizmet bulunamadı.", "There are no other services to add."),
        ("Geçici durdurulacak hizmetleri seç", "Choose the services to stop temporarily"),
        ("Çalışan Windows hizmetleri listelendi. Bilmediğin bir hizmeti seçme. Hizmetler silinmez, yalnızca durdurulur.",
         "Running Windows services are listed. Don't pick a service you don't know. Services are never deleted, only stopped."),

        // ------------------------------------------------------------------ the Memory tab
        ("korumalı", "protected"),
        ("{0} parça", "{0} {0?part:parts}"),
        ("1 parça", "1 part"),

        // ------------------------------------------------------------------ the preview
        ("Önizleme hazırlanıyor (hiçbir şey değiştirilmiyor)…", "Preparing the preview (nothing is being changed)…"),
        ("Önizleme hazırlanamadı.", "The preview couldn't be prepared."),
        ("Önizleme kapandı.", "Preview closed."),
        ("Önceki çalıştırmanın değişiklikleri hâlâ geçerli (programlar kapalı, ayarlar uygulanmış). Bu liste yalnızca şu an geriye kalanları gösterir; Başlat'a basınca önce eski hale döner, sonra baştan yapılır.",
         "The changes from the previous run are still in effect (programs closed, settings applied). This list only shows what is left right now; when you press Start, everything first goes back to how it was, then it is all done again."),
        ("Başlat'a basınca hiçbir şey yapılmayacak. Şu an hiçbir şey değiştirilmedi.", "Pressing Start won't do anything. Nothing has been changed so far."),
        ("Başlat'a basınca {0} şey yapılacak. Şu an hiçbir şey değiştirilmedi.", "Pressing Start will do {0} {0?thing:things}. Nothing has been changed so far."),

        // ------------------------------------------------------------------ developer screenshots only (#if DEBUG): the demo activity list, measurements and home list
        ("Not Defteri", "Notepad"),
        ("Çalışan program sayısı", "Number of running programs"),
        ("İnternet gecikmesi (ping, ms)", "Internet delay (ping, ms)"),
        // the numbers of the demo table are written with a Turkish comma; English writes a point
        ("9,84", "9.84"),
        ("13,02", "13.02"),
        ("+3,18", "+3.18"),
        ("14,2", "14.2"),
        ("14,0", "14.0"),
        ("-0,2", "-0.2"),
        ("{0} değerde iyileşme var, kötüleşen yok.", "{0} {0?value:values} improved, none got worse."),
        ("Atlandı: Not Defteri kapanmadı (sonlandırılamadı)", "Skipped: Notepad didn't close (it couldn't be ended)"),
        ("Yapılamadı: Yazıcı hizmeti durdurulamadı (Windows izin vermedi).", "Not done: the printer service couldn't be stopped (Windows didn't allow it)."),
        ("Wi-Fi kullanıyorsun. Mümkünse internet kablosu (Ethernet) kullan; kablosuz bağlantıdaki dalgalanmayı hiçbir ayar düzeltemez.",
         "You're using Wi-Fi. If you can, use an internet cable (Ethernet); no setting can fix the fluctuation of a wireless connection."),
    };
}
