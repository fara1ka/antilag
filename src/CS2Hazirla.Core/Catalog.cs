namespace CS2Hazirla.Core;

/// <summary>
/// One switch in the UI. Title = its name as the user reads it, in plain Turkish (examples in brackets are fine).
/// Hint = one short plain line (at most 70 characters), only where the name alone is not clear to a non-technical person.
/// Caution = one short amber sentence, only where switching it on can really cost the user something.
/// </summary>
public record OptionDef(
    string Id,
    string Group,
    string Title,
    string? Hint = null,
    string? Caution = null);

public record ProcessCategory(
    string OptionId,
    string Title,
    string[] Names,
    string[] Prefixes,
    string[]? ContainsTokens = null)
{
    public bool Matches(string normalizedName)
    {
        if (Array.IndexOf(Names, normalizedName) >= 0) return true;
        foreach (var p in Prefixes) if (normalizedName.StartsWith(p, StringComparison.Ordinal)) return true;
        if (ContainsTokens != null)
            foreach (var c in ContainsTokens) if (normalizedName.Contains(c, StringComparison.Ordinal)) return true;
        return false;
    }
}

public static class Catalog
{
    public const string GroupApps = "Kapatılacak programlar";
    public const string GroupServices = "Arka plan hizmetleri (geçici durdurulur)";
    public const string GroupSystem = "Windows ayarları (geri yüklenince eski haline gelir)";
    public const string GroupNetwork = "İnternet";

    public const string Cs2ProcessName = "cs2";
    public const int Cs2AppId = 730;
    public const string UltimateTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    public const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    // Said once, briefly, for the programs whose real work is done by a Windows service: only the window and the tray icon can be closed.
    private const string ServiceNote = "Pencere ve simge kapanır; hizmet olarak çalışan kısmı açık kalır.";

    public static readonly IReadOnlyList<OptionDef> Options = new List<OptionDef>
    {
        // ------------------------------------------------------------------ programs
        // Discord and GoodbyeDPI come first and are two separate switches, each of them just that one program (the other block-bypass tools
        // have a switch of their own further down): the user changes each on its own, side by side
        new("proc.discord", GroupApps, "Discord"),
        new("proc.dpi", GroupApps, "GoodbyeDPI"),
        new("proc.bypass", GroupApps, "Diğer engel aşma programları (Zapret, Tor…)", Hint: ServiceNote),
        new("proc.browsers", GroupApps, "Tarayıcılar (Chrome, Edge, Firefox…)"),
        new("proc.cloud", GroupApps, "Yedekleme programları (OneDrive, Dropbox…)"),
        new("proc.launchers", GroupApps, "Diğer oyun mağazaları (Epic, Battle.net, EA…)"),
        new("proc.updaters", GroupApps, "Program güncelleyicileri (Adobe, Java, Google…)"),
        new("proc.oem", GroupApps, "Bilgisayar markası ve ışık programları (Armoury Crate, MSI Center…)"),
        new("proc.peripherals", GroupApps, "Klavye, fare ve kulaklık programları (Logitech, Razer, SteelSeries…)"),
        new("proc.overlay", GroupApps, "Ekran üstü araçlar (NVIDIA Overlay, Xbox Game Bar…)"),
        new("proc.vpn", GroupApps, "VPN ve oyun hızlandırıcılar (NordVPN, ExitLag…)", Hint: ServiceNote),
        new("proc.antivirus", GroupApps, "Sonradan kurulan antivirüs programı (Windows Defender hariç)",
            Hint: ServiceNote, Caution: "Kapanırsa bu programın koruması durabilir."),
        new("proc.anticheat", GroupApps, "Hile koruma programları (Vanguard, EasyAntiCheat, ESEA, Esportal…)",
            Hint: ServiceNote, Caution: "ESEA, Esportal, Gamers Club ya da Valorant kullanıyorsan bu anahtarı kapalı tut."),
        new("proc.windowed", GroupApps, "Açık duran diğer programlar", Caution: "Kaydedilmemiş iş kaybolur."),
        new("proc.claude", GroupApps, "Claude (yapay zekâ asistanı)", Caution: "Açık Claude oturumu kapanır."),
        new("proc.chat", GroupApps, "Müzik ve sohbet programları (Spotify, Teams…)"),
        new("proc.trayauto", GroupApps, "Tanınmayan arka plan programları",
            Hint: "Pencere açmadan çalışan, listede olmayan programlar.", Caution: "İndirme yöneticisi gibi bir program yarıda kalabilir."),
        new("proc.extra", GroupApps, "Her seferinde kapat listesindeki programlar"),

        // ------------------------------------------------------------------ Windows background services
        new("svc.update", GroupServices, "Windows güncellemelerini geçici durdur"),
        new("svc.other", GroupServices, "Gereksiz Windows arka plan hizmetlerini durdur",
            Hint: "Yazıcı, arama dizinleme vb. Yazdıracaksan kapalı tut."),

        // ------------------------------------------------------------------ Windows settings
        new("pwr.plan", GroupSystem, "En yüksek performans güç planı", Hint: "Bilgisayar tam güçte çalışır; dizüstünde pil çabuk biter."),
        new("sys.gamemode", GroupSystem, "Windows Oyun Modu", Hint: "Windows oyun sırasında arka plan işlerini kısar."),
        new("sys.gamedvr", GroupSystem, "Xbox Game Bar oyun kaydını kapat", Hint: "Arka planda oyun kaydı için hazır beklemeyi durdurur."),
        new("sys.notifications", GroupSystem, "Windows bildirimlerini sustur", Hint: "Bildirimler geri yüklenene kadar kapalı kalır."),
        new("sys.gpupref", GroupSystem, "CS2 için güçlü ekran kartını seç", Hint: "İki ekran kartlı bilgisayarlarda CS2’yi güçlü kartla açar."),
        new("sys.fso", GroupSystem, "CS2 için tam ekran iyileştirmesini kapat", Hint: "Windows’un tam ekran oyunlara eklediği uyumluluk katmanı."),
        new("sys.mouseaccel", GroupSystem, "Fare ivmesini kapat", Hint: "İmleç hızlı harekette orantısız hızlanmaz; yalnızca bu oturumda."),
        new("sys.hotkeys", GroupSystem, "Yapışkan Tuşlar penceresini engelle", Hint: "Shift’e 5 kez basınca açılan pencere oyunu küçültebilir."),

        // ------------------------------------------------------------------ internet
        new("net.flushdns", GroupNetwork, "DNS önbelleğini temizle", Hint: "Eski internet adresi kayıtlarını siler; ping’i düşürmez."),
        new("net.nicpower", GroupNetwork, "Ağ kartı güç tasarrufunu kapat", Hint: "Ağ kartının uyutulup kısa süre kopmasını önler."),
    };

    /// <summary>Plain-language names/explanations for the Windows services the tool may stop.</summary>
    public static readonly IReadOnlyDictionary<string, (string Title, string Plain)> ServiceTexts =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["wuauserv"] = ("Windows Update", "Güncellemeleri arka planda indirir ve kurar."),
            ["UsoSvc"] = ("Güncelleme Orkestratörü", "Windows Update'in zamanlamasını yönetir."),
            ["BITS"] = ("Arka plan dosya aktarımı", "Güncellemeler gibi dosyaları arka planda indirir."),
            ["DoSvc"] = ("Teslim İyileştirme", "Güncellemeleri diğer bilgisayarlarla paylaşıp internet kullanır."),
            ["WSearch"] = ("Windows Arama", "Dosyaları aramayı hızlandırmak için diski sürekli tarar."),
            ["Spooler"] = ("Yazıcı hizmeti", "Yazdırma işleri içindir. Yazıcın yoksa gerekmez."),
            ["DiagTrack"] = ("Ölçüm ve veri gönderme", "Microsoft'a kullanım verisi gönderir."),
            ["SysMain"] = ("Tahmini önbellek (SysMain)", "Sık açtığın programları önceden belleğe yükler; RAM'i doldurur."),
            ["MapsBroker"] = ("İndirilmiş haritalar", "Çevrimdışı harita verisini günceller."),
            ["lfsvc"] = ("Konum hizmeti", "Bilgisayarın konumunu uygulamalara verir."),
            ["WerSvc"] = ("Hata raporlama", "Çöken programların raporunu Microsoft'a gönderir."),
            ["XblAuthManager"] = ("Xbox oturum hizmeti", "Xbox hesabıyla giriş içindir."),
            ["XblGameSave"] = ("Xbox oyun kaydı bulutu", "Xbox oyun kayıtlarını buluta yükler."),
            ["XboxNetApiSvc"] = ("Xbox ağ hizmeti", "Xbox çok oyunculu bağlantıları içindir."),
        };

    public static string ServiceTitle(string serviceName, string? fallback = null) =>
        ServiceTexts.TryGetValue(serviceName, out var t) ? t.Title : (fallback ?? serviceName);

    public static string ServicePlain(string serviceName) =>
        ServiceTexts.TryGetValue(serviceName, out var t) ? t.Plain : "";
    public static OptionDef? Find(string id) => Options.FirstOrDefault(o => o.Id == id);

    // ---- process categories (executable names without .exe, lowercase) ----
    public static readonly IReadOnlyList<ProcessCategory> ProcessCategories = new List<ProcessCategory>
    {
        new("proc.browsers", "Tarayıcılar",
            new[] { "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "iexplore", "waterfox", "librewolf" },
            Array.Empty<string>()),

        new("proc.cloud", "Yedekleme programları",
            new[] { "onedrive", "dropbox", "googledrivefs", "googledrive", "googledrivesync", "boxsync", "box", "megasync",
                    "iclouddrive", "icloudservices", "pcloud", "nextcloud", "syncthing" },
            Array.Empty<string>()),

        new("proc.launchers", "Diğer oyun mağazaları",
            new[] { "epicgameslauncher", "epicwebhelper", "battle.net", "eadesktop", "eabackgroundservice", "origin",
                    "originwebhelperservice", "upc", "ubisoftconnect", "uplay", "ubisoftgamelauncher", "galaxyclient",
                    "galaxyclient helper", "riotclientservices", "riotclientux", "riotclientuxrender", "playnite.desktopapp" },
            Array.Empty<string>()),

        new("proc.updaters", "Program güncelleyicileri",
            new[] { "adobeupdateservice", "adobeipcbroker", "adobegcclient", "adobe desktop service", "ccxprocess",
                    "cclibrary", "jusched", "jucheck", "googleupdate", "googlecrashhandler", "googlecrashhandler64",
                    "microsoftedgeupdate", "adobearm", "armsvc" },
            Array.Empty<string>()),

        new("proc.peripherals", "Klavye, fare ve kulaklık programları",
            new[] { "wootility", "ugenie", "synapse", "audeze hq", "gloriouscore", "zowie", "finalmouse", "endgamegear", "lamzu", "vxe" },
            new[] { "lghub", "logioptions", "logitech", "logi_", "logilda", "logitune", "logicapture", "logipluginservice",
                    "razer", "rzsdk", "rzsynapse", "rzmonitor", "rzappengine", "synapse",
                    "icue", "corsair", "steelseries", "hyperx", "ngenuity", "turtlebeach", "astrocommand", "glorious", "pulsar" }),

        new("proc.oem", "Bilgisayar markası ve ışık programları",
            new[] { "msicenter", "dragoncenter", "mysticlight", "rgbfusion", "signalrgb", "openrgb", "nzxt cam",
                    "wallpaper32", "wallpaper64", "webwallpaper32", "gccontrolcenter" },
            new[] { "armourycrate", "lenovovantage", "dellsupportassist", "supportassist", "omencommandcenter", "hpsupportassistant", "msi center" }),

        new("proc.overlay", "Ekran üstü araçlar",
            new[] { "nvidia overlay", "nvidia share", "nvidia geforce experience", "nvidia app", "nvidia web helper", "nvidia broadcast",
                    "nvsphelper64", "nvsphelper", "nvspcap64", "nvtray", "nvcpl", "nvbackend", "nvidia-share",
                    "gamebar", "gamebarftserver", "gamebarpresencewriter", "xboxpcapp", "xboxpcappft", "widgets", "widgetservice",
                    "phoneexperiencehost", "yourphone", "yourphoneserver", "cortana", "msiafterburner", "rtss", "rtsshooksloader",
                    "fraps", "lightshot", "sharex", "greenshot", "bandicam", "fpsmonitor", "dxtory" },
            new[] { "overwolf", "medal" }),

        // "whatsapp.root" is the Microsoft Store WhatsApp: it only hides in the tray on a polite close, so it has to be named here
        new("proc.chat", "Müzik ve sohbet programları",
            new[] { "teams", "ms-teams", "msteams", "spotify", "slack", "telegram", "whatsapp", "whatsapp.root", "whatsappdesktop", "skype", "zoom", "signal", "viber" },
            Array.Empty<string>()),

        new("proc.claude", "Claude",
            new[] { "claude" },
            Array.Empty<string>()),

        new("proc.vpn", "VPN ve oyun hızlandırıcılar",
            new[] { "warp-svc", "exitlag", "wtfast", "mudfish", "hamachi-2-ui", "hamachi" },
            new[] { "nordvpn", "expressvpn", "protonvpn", "openvpn", "wireguard", "tailscale", "zerotier", "windscribe", "surfshark",
                    "mullvad", "cloudflare warp", "cyberghost", "ipvanish", "privateinternetaccess", "pia-", "hotspotshield", "hide.me", "vpn" },
            new[] { "vpn" }),

        new("proc.antivirus", "Sonradan kurulan antivirüs",
            new[] { "avp", "egui", "ekrn", "mbam", "mbamtray", "mcshield" },
            new[] { "avast", "avg", "kaspersky", "bitdefender", "bdagent", "bdservicehost", "norton", "mcafee", "mfe", "eset", "malwarebytes",
                    "sophos", "avira", "webroot", "trendmicro", "tmbmsrv", "pccnt", "f-secure", "comodo", "panda", "gdata" },
            new[] { "antivirus" }),

        new("proc.anticheat", "Diğer oyunların hile korumaları",
            new[] { "vgc", "vgtray", "vgk", "beservice", "bedaisy", "eac_launcher" },
            new[] { "easyanticheat", "battleye", "esea", "esportal", "gamersclub", "xigncode", "gameguard", "nprotect" },
            new[] { "anticheat", "anti-cheat" }),

        // GoodbyeDPI is its own switch: the program the user means, and only that one
        new("proc.dpi", "GoodbyeDPI",
            Array.Empty<string>(),
            new[] { "goodbyedpi" }),

        new("proc.bypass", "Engel aşma programları",
            new[] { "tor", "outline", "lantern", "winws", "clash-verge", "clashverge", "v2rayn", "sing-box" },
            new[] { "zapret", "byedpi", "spoofdpi", "v2ray", "xray", "clash", "singbox", "shadowsocks", "proxifier", "cloudflared",
                    "psiphon", "obfs4", "wintun", "nekoray", "nekobox", "hiddify", "mihomo", "tun2socks", "ultrasurf" }),

        // Discord has its own switch (the block-bypass tools above have theirs) and, unlike them, it CAN be started again by hand
        new("proc.discord", "Discord",
            new[] { "discord", "discordptb", "discordcanary" },
            Array.Empty<string>()),
    };

    /// <summary>Words a search for this switch should find besides its title: the programs it closes (and the Windows services it stops).</summary>
    public static IEnumerable<string> SearchTerms(string optionId)
    {
        foreach (var c in ProcessCategories.Where(c => c.OptionId == optionId))
        {
            yield return c.Title;
            foreach (var n in c.Names) { yield return n; yield return FriendlyNames.ForProcess(n, null); }
            foreach (var p in c.Prefixes) yield return p;
        }
        if (optionId is "svc.update" or "svc.other")
            foreach (var r in DefaultServices().Where(r => r.Group == (optionId == "svc.update" ? "update" : "other")))
                yield return ServiceTitle(r.Name);
    }

    public static ProcessCategory? CategoryOf(string normalizedName)
        => ProcessCategories.FirstOrDefault(c => c.Matches(normalizedName));

    public static List<ServiceRule> DefaultServices() => new()
    {
        new() { Name = "wuauserv", Group = "update" },
        new() { Name = "UsoSvc", Group = "update" },
        new() { Name = "BITS", Group = "update" },
        new() { Name = "DoSvc", Group = "update" },
        new() { Name = "WSearch", Group = "other" },
        new() { Name = "Spooler", Group = "other" },
        new() { Name = "DiagTrack", Group = "other" },
        new() { Name = "SysMain", Group = "other" },
        new() { Name = "MapsBroker", Group = "other" },
        new() { Name = "lfsvc", Group = "other" },
        new() { Name = "WerSvc", Group = "other" },
        new() { Name = "XblAuthManager", Group = "other" },
        new() { Name = "XblGameSave", Group = "other" },
        new() { Name = "XboxNetApiSvc", Group = "other" },
    };

    // ---- profiles ----
    // Hafif closes NO program at all (the profile text promises exactly that): services and harmless settings only.
    private static readonly HashSet<string> SafeOn = new()
    {
        "svc.update", "pwr.plan", "sys.gamemode", "sys.gamedvr",
        "sys.gpupref", "sys.hotkeys", "net.flushdns",
    };

    // Tam temizlik = EVERYTHING on: every switch of the list is ticked, there is no unticked row (a test keeps it that way for new options too).
    private static readonly HashSet<string> MaximumOn = new(Options.Select(o => o.Id));

    // Stream: like Maximum, but browsers (chat/dashboard), unknown tray apps (stream tools), VPN, Discord and the block-bypass tools stay.
    // (Discord is protected in stream mode anyway; its switch must therefore show OFF instead of promising something that never happens.)
    private static readonly HashSet<string> StreamOn = new(MaximumOn.Where(x => x is not ("proc.browsers" or "proc.trayauto" or "proc.vpn" or "proc.dpi" or "proc.bypass" or "proc.discord")));

    public static bool ProfileDefault(ProfileKind kind, string optionId) => kind switch
    {
        ProfileKind.Safe => SafeOn.Contains(optionId),
        ProfileKind.Maximum => MaximumOn.Contains(optionId),
        ProfileKind.Stream => StreamOn.Contains(optionId),
        _ => false,
    };
}
