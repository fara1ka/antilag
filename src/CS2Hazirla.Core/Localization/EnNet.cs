namespace CS2Hazirla.Core;

/// <summary>English for the texts of: NetCheck.cs (the internet check). Only the owner of this area edits this file.</summary>
public static class EnNet
{
    public static readonly (string Tr, string En)[] Entries =
    {
        // ---- what the check is not (shown under every result)
        ("Bu ölçüm birkaç saniye sürer ve ping kullanır; oyun sunucusuna giden yolu birebir göstermez. Ad çözme süresi Windows'un önbelleğine göre değişebilir.",
         "This check takes a few seconds and uses ping; it doesn't show the exact path to the game server. The name lookup time can vary with Windows' cache."),

        // ---- names of the things that are pinged
        ("Modemin / yönlendiricin", "Your modem / router"),
        ("İnternet ({0})", "Internet ({0})"),
        ("Ping hedefin ({0})", "Your ping target ({0})"),

        // ---- progress (one line for the whole wait, then one for the end)
        ("Ping atılıyor; birkaç saniye sürer…", "Pinging; this takes a few seconds…"),
        ("Sonuç hazırlanıyor…", "Preparing the result…"),

        // ---- the one sentence that says what the result is
        ("Bağlantın sağlıklı görünüyor.", "Your connection looks healthy."),
        ("Sorun büyük olasılıkla evdeki ağda (Wi-Fi ya da modem).", "The problem is most likely in your home network (Wi-Fi or modem)."),
        ("Sorun büyük olasılıkla internet sağlayıcında.", "The problem is most likely with your internet provider."),
        ("İnternete ulaşılamadı.", "Couldn't reach the internet."),
        ("Ping internet adreslerine ulaşmadı, ama adres çözme çalışıyor.", "Ping didn't reach the internet addresses, but name lookup works."),
        ("Ping çalışıyor ama adres çözme yanıt vermedi.", "Ping works, but the name lookup didn't answer."),
        ("İnternetinde bir sorun var, ama nerede olduğu belli değil.", "Your internet has a problem, but it's not clear where."),

        // ---- the numbers (the percent sign comes first in Turkish and last in English)
        ("Modem: ortalama {0} ms, paket kaybı %{1}, dalgalanma {2} ms; internet: ortalama {3} ms, paket kaybı %{4}, dalgalanma {5} ms.",
         "Modem: average {0} ms, packet loss {1}%, fluctuation {2} ms; internet: average {3} ms, packet loss {4}%, fluctuation {5} ms."),
        ("İnternet: ortalama {0} ms, paket kaybı %{1}, dalgalanma {2} ms.",
         "Internet: average {0} ms, packet loss {1}%, fluctuation {2} ms."),
        ("Modem: ortalama {0} ms, paket kaybı %{1}, dalgalanma {2} ms; internet adreslerinden hiçbiri yanıt vermedi.",
         "Modem: average {0} ms, packet loss {1}%, fluctuation {2} ms; none of the internet addresses answered."),
        ("Ne modemden ne de internet adreslerinden yanıt geldi.", "Neither your modem nor the internet addresses answered."),
        ("İnternet adreslerinden hiçbiri yanıt vermedi.", "None of the internet addresses answered."),

        // ---- what it means
        ("Kayıp, gecikme ve dalgalanma oyunda hissedilen sınırların içinde.",
         "Loss, delay and fluctuation are within the limits that matter in a game."),
        ("Modem ping'e yanıt vermedi, bu yüzden ev ağı ayrı ölçülemedi; internet ise oyunda hissedilen sınırların içinde.",
         "Your modem didn't answer the ping, so your home network couldn't be measured on its own; the internet is within the limits that matter in a game."),
        ("Modem bulunamadı, bu yüzden ev ağı ayrı ölçülemedi; internet ise oyunda hissedilen sınırların içinde.",
         "Your modem wasn't found, so your home network couldn't be measured on its own; the internet is within the limits that matter in a game."),
        ("Bu değerler oyunda takılma ya da gecikme olarak hissedilebilir; sorun modeme giden yolda ya da modemin kendisinde görünüyor.",
         "These values can feel like stutter or lag in a game; the problem seems to be on the way to your modem, or in the modem itself."),
        ("Modem sağlıklı, ama internete çıkışta oyunda hissedilebilecek kayıp ya da gecikme var.",
         "Your modem is fine, but there's loss or delay on the way out to the internet that you can feel in a game."),
        ("Oyunda hissedilebilecek kayıp ya da gecikme var, ama modem ping'e yanıt vermediği için sorunun evde mi, internet sağlayıcında mı olduğu söylenemez.",
         "There's loss or delay you can feel in a game, but since your modem didn't answer the ping, there's no telling whether the problem is at home or with your internet provider."),
        ("Oyunda hissedilebilecek kayıp ya da gecikme var, ama modem bulunamadığı için sorunun evde mi, internet sağlayıcında mı olduğu söylenemez.",
         "There's loss or delay you can feel in a game, but since your modem wasn't found, there's no telling whether the problem is at home or with your internet provider."),
        ("Modeme ulaşılıyor ama internete çıkılamıyor gibi görünüyor.", "Your modem answers, but the internet doesn't seem to be reachable."),
        ("Bağlantın kopmuş olabilir.", "Your connection may be down."),
        ("Ping engelleniyor olabilir; internetin çalışıyor olabilir.", "Ping may be blocked; your internet may be working."),
        ("Bu, sitelerin ve oyun sunucularının adlarının bulunamaması anlamına gelebilir; bir kez daha dene.",
         "This can mean that the names of websites and game servers can't be found; try once more."),

        // ---- what to do (one thing)
        ("Mümkünse bilgisayarını modeme kabloyla (Ethernet) bağla.", "If you can, connect your computer to the modem with a cable (Ethernet)."),
        ("Modemi kapatıp 30 saniye sonra yeniden aç.", "Turn the modem off, wait 30 seconds, then turn it back on."),
        ("Modemi kapatıp 30 saniye sonra yeniden aç; düzelmezse internet sağlayıcını ara.",
         "Turn the modem off, wait 30 seconds, then turn it back on; if that doesn't help, call your internet provider."),
        ("Sorun sürerse internet sağlayıcını arayıp hattını sor.", "If the problem continues, call your internet provider and ask about your line."),
        ("Wi-Fi'ın açık olduğunu ya da kablonun takılı olduğunu kontrol et.", "Check that Wi-Fi is on or that the cable is plugged in."),

        // ---- a remark, only when everything else is healthy
        ("{0} adresi ping'e yanıt vermedi; bazı sunucular ping'i kapatır.", "{0} didn't answer the ping; some servers turn ping off."),
        ("Ama {0} adresine giden yol daha kötü: ortalama {1} ms, paket kaybı %{2}, dalgalanma {3} ms.",
         "But the way to {0} is worse: average {1} ms, packet loss {2}%, fluctuation {3} ms."),
        ("Adres çözme yavaş ({0} ms): bu, oyuna girişi geciktirebilir. Sorun sürerse internet sağlayıcına danış.",
         "Name lookup is slow ({0} ms): it can delay getting into a game. If it continues, ask your internet provider."),
    };
}
