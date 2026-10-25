namespace CS2Hazirla.Core;

/// <summary>English for the texts of: WindowsSystem.cs, ISystem.cs, Native.cs, Metrics.cs, Status.cs, StateGuard.cs, Storage.cs, ProfileSession.cs, Models.cs. Only the owner of this area edits this file.</summary>
public static class EnSystem
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // ------------------------------------------------------------------ Status.cs: the headline and the sentence under it on the home screen
        ("CS2 çalışıyor", "CS2 is running"),
        ("Oyun açıkken Antilag hiçbir şeyi değiştirmez. Oyunu kapattıktan sonra tekrar dene.", "While the game is open, Antilag changes nothing. Try again after you close the game."),
        ("Çalışıyor", "Working"),
        ("Bu yarım dakikayı bulabilir; lütfen bekle.", "This can take up to half a minute; please wait."),
        ("İşlem tamamlanamadı", "The action couldn't be completed"),
        ("Ayrıntı aşağıdaki listede.", "Details are in the list below."),
        // after Geri yükle: some programs did not start again ({0} = how many; the second value of the sentence below is the same number)
        ("Geri yüklendi, {0} program kendiliğinden açılamadı", "Restored, but {0} {0?program:programs} couldn't reopen on {0?its:their} own"),
        ("{0}. Gerekirse kendin aç.", "{0}. Open {1?it:them} yourself if needed."),
        ("Geri yüklendi", "Restored"),
        ("Ayarlar ve hizmetler eski haline döndü.", "Settings and services are back to how they were."),
        // after Başlat
        ("Bitti, ancak {0} program kapanmadı", "Done, but {0} {0?program:programs} didn't close"),
        ("Kapanmayanlar: {0}.", "Didn't close: {0}."),
        ("{0} işlem de yapılamadı.", "{0} more {0?action:actions} couldn't be done."),
        ("Bitti, ancak {0} işlem yapılamadı", "Done, but {0} {0?action:actions} couldn't be done"),
        ("Bitti", "Done"),
        ("{0} işlem yapıldı. Oyunu başlatmadan önce Antilag'ı kapat.", "{0} {0?action:actions} done. Close Antilag before you start the game."),
        ("Kapatılacak ya da değiştirilecek bir şey bulunamadı.", "Nothing to close or change was found."),
        // nothing started yet: which mode is ready ({0} = the name of the mode)
        ("Başlamaya hazır", "Ready to start"),
        ("{0} (değiştirilmiş) hazır", "{0} (changed) is ready"),
        ("{0} hazır", "{0} is ready"),
        // "A, B, C ve 2 diğeri": the first three names and the count of the rest
        ("{0} ve {1} diğeri", "{0} and {1} more"),

        // ------------------------------------------------------------------ Metrics.cs: the Ölçümler table
        ("Çalışan program ve arka plan işi sayısı", "Number of running programs and background tasks"),
        ("Boş bellek (RAM, GB)", "Free memory (RAM, GB)"),
        ("Windows'un RAM'de tuttuğu eski veriler (GB)", "Old data Windows keeps in RAM (GB)"),
        ("Boştaki işlemci (%)", "Idle processor (%)"),
        ("İnternet gecikmesi (ping, ms) → {0}", "Internet delay (ping, ms) → {0}"),
        ("Gecikme dalgalanması (jitter, ms)", "Delay fluctuation (jitter, ms)"),
        ("Paket kaybı (%)", "Packet loss (%)"),
        ("İnternet gecikmesi", "Internet delay"),
        ("Not", "Note"),
        ("İnternet gecikmesi ölçülemedi (bağlantı yok ya da sunucu yanıt vermedi).", "Internet delay couldn't be measured (no connection, or the server didn't respond)."),
        // the sentence under the table
        ("Ölçülen değerlerde anlamlı bir değişiklik yok. Bu normal: bilgisayarın zaten temizse kazanılacak bir şey yoktur. İnternet gecikmesi (ping) internet hattından gelir; bu uygulama onu düşüremez.",
         "There's no meaningful change in the measured values. That's normal: if your PC is already clean, there's nothing to gain. Internet delay (ping) comes from your internet line; this app can't lower it."),
        ("{0} değerde iyileşme var, kötüleşen yok. İnternet gecikmesi değişmediyse bu normaldir: o, internet hattına bağlıdır.",
         "{0} {0?value:values} improved, none got worse. If your internet delay didn't change, that's normal: it depends on your internet line."),
        ("{0} değer iyileşti, {1} değer kötüleşti. Ölçümler anlık ve dalgalıdır; tek bir ölçüme fazla anlam yükleme.",
         "{0} {0?value:values} improved, {1} {1?value:values} got worse. Measurements are snapshots and they fluctuate; don't read too much into a single one."),

        // ------------------------------------------------------------------ Storage.cs: settings.json could not be read (activity list and dialog)
        ("Ayarlar okunamadı, varsayılanlar kullanıldı; eski dosya {0} olarak saklandı.", "Your settings couldn't be read, so the defaults were used; the old file was kept as {0}."),
        ("Ayarlar okunamadı, varsayılanlar kullanıldı.", "Your settings couldn't be read, so the defaults were used."),

        // ------------------------------------------------------------------ ProfileSession.cs
        // the name a new profile gets when the user has not typed one (a profile's name is the user's own text once it is saved)
        ("Profil", "Profile"),
        // the names of the three ready-made presets (same words as the preset strip of the home page)
        ("Hafif", "Light"),
        ("Yayıncı", "Streamer"),
        ("Tam temizlik", "Full cleanup"),
    };
}
