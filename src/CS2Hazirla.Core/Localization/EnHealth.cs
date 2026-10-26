namespace CS2Hazirla.Core;

/// <summary>English for the texts of: Health.cs (the computer check-up). Only the owner of this area edits this file.</summary>
public static class EnHealth
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // ------------------------------------------------------------------ free space ({0} = drive such as C:, {1} = size such as 215 GB)
        // The "Free space is low" texts are for the red level, which a small drive with under 15 GB free and a huge one with 49 GB free both reach:
        // they say that room may be limited, never that anything does not fit.
        ("{0} {1} boş", "{0} {1} free"),
        ("Oyun diski {0} {1} boş", "Game drive {0} {1} free"),
        // the drive that holds Windows and the games
        ("Windows ve oyunlar için yeterince yer var.", "There is plenty of room for Windows and your games."),
        ("Yer azalıyor. Windows ve oyun güncellemeleri için biraz yer açmak iyi olur.", "Space is getting low. It's a good idea to free up some room for Windows and game updates."),
        ("Boş yer az. Windows ve oyun güncellemeleri için yer kısıtlı olabilir; biraz yer aç.", "Free space is low. Room for Windows and game updates may be limited, so free up some space."),
        // the drive of Windows when the game is somewhere else (or not known)
        ("Windows için yeterince yer var.", "There is plenty of room for Windows."),
        ("Yer azalıyor. Windows güncellemeleri için biraz yer açmak iyi olur.", "Space is getting low. It's a good idea to free up some room for Windows updates."),
        ("Boş yer az. Windows güncellemeleri için yer kısıtlı olabilir; biraz yer aç.", "Free space is low. Room for Windows updates may be limited, so free up some space."),
        // the drive of the game when it is another drive than Windows'
        ("Oyunlar için yeterince yer var.", "There is plenty of room for your games."),
        ("Yer azalıyor. Oyun güncellemeleri için biraz yer açmak iyi olur.", "Space is getting low. It's a good idea to free up some room for game updates."),
        ("Boş yer az. Oyun güncellemeleri için yer kısıtlı olabilir; biraz yer aç.", "Free space is low. Room for game updates may be limited, so free up some space."),
        ("Depolama ayarlarını aç", "Open storage settings"),

        // ------------------------------------------------------------------ the game on a hard disk ({0} = drive)
        ("Oyun {0} diskinde (HDD)", "The game is on an HDD ({0})"),
        ("Oyun, HDD'de SSD'ye göre biraz daha geç yüklenir.", "Loading is a little slower on an HDD than on an SSD."),

        // ------------------------------------------------------------------ disk health
        ("Disk için arıza uyarısı", "Disk failure warning"),
        ("Windows, disklerden birinin bozulabileceğini düşünüyor. Bozuldu demek değil; ama önemli dosyalarını yedekle.",
         "Windows thinks one of your disks may fail. That doesn't mean it's broken, but back up your important files."),

        // ------------------------------------------------------------------ how long Windows has not been restarted ({0} = whole days, 7 or more)
        // (with Fast Startup, "Shut down" does not start Windows afresh: only a restart does, so the texts never say the PC "was on")
        ("Windows {0} gündür yeniden başlatılmadı", "Windows hasn't restarted in {0} days"),
        ("Yeniden başlatmak belleği temizler ve bekleyen güncellemeleri bitirir; kapatıp açmak yetmeyebilir.",
         "Restarting clears memory and finishes any waiting updates; shutting down and starting again may not be enough."),
        ("Bu epey uzun bir süre. Başlat menüsünden Yeniden başlat'ı seç: bellek temizlenir, bekleyen güncellemeler biter.",
         "That's quite a long time. Choose Restart from the Start menu: memory is cleared and waiting updates are finished."),

        // ------------------------------------------------------------------ Windows waits for a restart
        ("Windows yeniden başlatma bekliyor", "Windows is waiting for a restart"),
        ("Güncellemeler yeniden başlatılmayı bekliyor. Maçın ortasına denk gelmesin diye oyundan önce yeniden başlat.",
         "Updates are waiting for a restart. Restart before you play so it doesn't land in the middle of a match."),

        // ------------------------------------------------------------------ on battery
        ("Bilgisayar pille çalışıyor", "Your PC is running on battery"),
        ("Pilde çalışırken bilgisayar genelde daha yavaş olur. Şarj cihazını tak.", "A computer is usually slower on battery. Plug in the charger."),
        ("Güç ayarlarını aç", "Open power settings"),

        // ------------------------------------------------------------------ graphics card driver ({0} = name of the card, {1} = month and year such as "September 2026")
        ("Ekran kartı sürücüsü", "Graphics card driver"),
        ("{0}: sürücü {1} tarihli.", "{0}: the driver is from {1}."),
        ("{0}: sürücü {1} tarihli. Üreticinin daha yenisi olabilir.", "{0}: the driver is from {1}. The maker may have a newer one."),
    };
}
