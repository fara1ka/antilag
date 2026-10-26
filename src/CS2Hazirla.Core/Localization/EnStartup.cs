namespace CS2Hazirla.Core;

/// <summary>English for the texts of: Startup.cs (the startup-programs manager). Only the owner of this area edits this file.</summary>
public static class EnStartup
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // why an entry has no switch (one short sentence, shown next to the program)
        ("Antilag'ın kendisi; buradan değiştirilemez.", "This is Antilag itself; it can't be changed here."),
        ("Güvenlik programı; buradan değiştirilemez.", "This is a security program; it can't be changed here."),
        ("Bilgisayarın çalışması için gerekli olabilir; buradan değiştirilemez.", "It may be needed for your computer to work, so it can't be changed here."),

        // the messages of StartupException (shown as they are)
        ("Bu program korumalı; buradan değiştirilemez.", "This program is protected; it can't be changed here."),
        ("Oyun açıkken değişiklik yapılmaz; oyunu kapatınca tekrar dene.", "Nothing is changed while the game is open; try again after you close it."),
        ("Bu program artık Windows'la birlikte açılanlar arasında değil.", "This program is no longer one of those that open with Windows."),
        ("Windows bu değişikliğe izin vermedi (yönetici izni gerekebilir); hiçbir şey değişmedi.", "Windows didn't allow this change (administrator rights may be needed); nothing was changed."),
        ("Ayar kaydedilemedi; hiçbir şey değişmedi.", "The setting couldn't be saved; nothing was changed."),

        // StartupSummary.Line: how many programs open with Windows and how many are switched off
        ("Windows'la birlikte açılan program yok.", "No programs open with Windows."),
        ("Hiçbir program Windows'la birlikte açılmıyor; {0} program kapalı.", "No program opens with Windows; {0} {0?program is:programs are} switched off."),
        ("{0} program Windows'la birlikte açılıyor.", "{0} {0?program opens:programs open} with Windows."),
        ("{0} program Windows'la birlikte açılıyor; {1} program daha kapalı.", "{0} {0?program opens:programs open} with Windows; {1} more {1?program is:programs are} switched off."),

        // StartupSummary.SourceNote: where an entry comes from
        ("Senin hesabın için", "For your account"),
        ("Bu bilgisayardaki herkes için", "For everyone on this PC"),
        ("Senin Başlangıç klasörün", "Your Startup folder"),
        ("Herkesin Başlangıç klasörü", "Everyone's Startup folder"),
    };
}
