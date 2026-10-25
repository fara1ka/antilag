namespace CS2Hazirla.Core;

/// <summary>English for the texts of: ProfilesViewModel.cs, Mvvm.cs, TickRing.cs, Motion.cs. Only the owner of this area edits this file.</summary>
public static class EnProfiles
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // the status line under the profile editor
        ("Dosyaya yazılamadı: profil bu açılışta çalışır ama Antilag'ı kapatınca kaybolur.", "Couldn't write to the file: the profile works this time, but it's lost when you close Antilag."),
        ("Kaydedilmemiş değişiklikler var.", "You have unsaved changes."),
        ("✓ Kaydedildi.", "✓ Saved."),

        // the three ready-made presets of the strip on the home page: name and what it does
        ("Hafif", "Light"),
        ("Tam temizlik", "Full cleanup"),
        ("Yayıncı", "Streamer"),
        ("Hiçbir programı kapatmaz; Windows güncellemelerini geçici durdurur ve birkaç Windows ayarı yapar (en yüksek performans güç planı, Oyun Modu…).",
         "Doesn't close any programs; temporarily stops Windows updates and changes a few Windows settings (highest-performance power plan, Game Mode…)."),
        ("CS2, Steam, FACEIT, Windows, ekran kartı ve ses sürücüleri ile 'Her zaman açık kalsın' listesindekiler dışındaki programları kapatır. Windows bildirimlerini susturur (geri yüklenene kadar) ve CS2 için tam ekran iyileştirmesini kapatır.",
         "Closes every program except CS2, Steam, FACEIT, Windows, graphics and sound drivers, and the ones on your 'Always keep open' list. Mutes Windows notifications (until restored) and turns off fullscreen optimizations for CS2."),
        ("OBS, Streamlabs, Discord, Elgato gibi tanınan yayın araçları, tarayıcı, VPN, GoodbyeDPI, diğer engel aşma programları ve tanınmayan arka plan programları açık kalır; gerisi kapanır. Bildirimler susturulur (geri yüklenene kadar), tam ekran iyileştirmesi kapatılır. Başka bir yayın aracını Ayarlar'dan 'Her zaman açık kalsın' listesine ekle.",
         "Well-known streaming tools (such as OBS, Streamlabs, Discord and Elgato), browsers, VPNs, GoodbyeDPI, other block-bypass programs and unrecognized background programs stay open; everything else closes. Notifications are muted (until restored) and fullscreen optimizations are turned off. To keep another streaming tool open, add it to the 'Always keep open' list in Settings."),

        // the tooltip of a profile the user saved: the name is the user's own
        ("Senin kaydettiğin profil: {0}", "Your saved profile: {0}"),

        // developer screenshots only (#if DEBUG): names of the two demo profiles
        ("Maç günü", "Match day"),
        ("Yayın", "Streaming"),
    };
}
