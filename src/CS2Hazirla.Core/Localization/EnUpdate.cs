namespace CS2Hazirla.Core;

/// <summary>English for the texts of: Update.cs, UpdateFlow.cs, UpdateViewModel.cs. Only the owner of this area edits this file.</summary>
public static class EnUpdate
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // the failure texts of UpdateFlow (FailedDownload, FailedIntegrity, FailedStart, FailedGame): constants, so they are written here by hand;
        // UpdateFlow keeps them Turkish (the flow and its tests compare them) and the update card translates them where it shows them
        ("İndirilemedi. İnternet bağlantını kontrol edip tekrar dene.", "Download failed. Check your internet connection and try again."),
        ("Dosya doğrulanamadığı için kurulmadı.", "The file couldn't be verified, so it wasn't installed."),
        ("Kurulum başlatılamadı. Tekrar dene.", "Couldn't start the installation. Try again."),
        ("CS2 çalışıyor: oyun açıkken güncelleme yapılmaz.", "CS2 is running: Antilag won't update while the game is open."),

        // the update card in the left bar: headline, line under it, buttons
        ("Yeni sürüm var", "New version available"),
        ("İndiriliyor… %{0}", "Downloading… {0}%"),
        ("Kuruluyor…", "Installing…"),
        ("Güncelleme yapılamadı", "Update failed"),
        ("Bitince Antilag kapanıp yeniden açılır.", "When it's done, Antilag closes and opens again."),
        ("Antilag kapanıp yeniden açılacak.", "Antilag will close and open again."),
        ("Tekrar dene", "Try again"),
        ("Güncelle", "Update"),
        ("Kapat", "Close"),
        ("Sonra", "Later"),

        // what the window says when it is closed during an update
        ("Güncelleme indiriliyor. Bitmesini bekle ya da kartın üstündeki Vazgeç'e bas; sonra Uygulamayı kapat'a bas.",
         "The update is downloading. Wait for it to finish or press Cancel on the card, then press Close app."),
        ("Güncelleme başlıyor, birkaç saniye bekle.", "The update is starting. Wait a few seconds."),

        // the update row in Ayarlar: "Sürüm 3.4.0 · <status>"
        ("Sürüm {0}", "Version {0}"),
        ("Güncelleniyor…", "Updating…"),
        ("Yeni sürüm var: {0}", "New version available: {0}"),
        ("Denetleniyor…", "Checking…"),
        ("CS2 çalışırken denetlenmez.", "Not checked while CS2 is running."),
        ("Denetim kapalı.", "Update checking is off."),
        ("Sürüm bilgisi doğrulanamadı; güncelleme sunulmadı.", "Version info couldn't be verified; no update was offered."),
        ("Antilag güncel.", "Antilag is up to date."),
        ("Denetlenemedi. İnternet bağlantını kontrol et.", "Couldn't check for updates. Check your internet connection."),

        // Hakkında: the legal notice the GPL asks for (copyright, licence, no warranty, no link to the companies named); {0} is the version number
        ("Antilag {0}. Telif hakkı (C) 2026 fara1ka. Bu program özgür yazılımdır: GNU Genel Kamu Lisansı'nın 3. sürümü ya da (isteğe bağlı) sonraki bir sürümünün koşulları altında " +
         "dağıtılır ve HİÇBİR GARANTİ VERİLMEZ. Valve, FACEIT, Discord ve adı geçen diğer şirketlerle bağlantılı değildir. Kaynak kod ve lisans metinleri aşağıdadır.",
         "Antilag {0}. Copyright (C) 2026 fara1ka. This program is free software: it is distributed under the terms of version 3 of the GNU General Public License, " +
         "or (at your option) any later version, and comes WITHOUT ANY WARRANTY. It is not affiliated with Valve, FACEIT, Discord or the other companies named. " +
         "The source code and license texts are below."),
    };
}
