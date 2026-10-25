# Antilag: geliştirici notları

Oyuncu için yazılmış belge `README.md`'dir. Bu dosya kodla uğraşan içindir: mimari, değişmezler, test, derleme ve yayın.

## 1. Tasarım ilkesi

Küçük bir "hazırla ve kapan" aracı: CS2'den ÖNCE çalışır, gereksiz her şeyi kapatır, kendini tamamen kapatır. **Oyun açıkken çalışan,
izleyen veya kontrol eden hiçbir şey yoktur** (tepsi ikonu, izleme döngüsü, otomatik geri yükleme, zamanlayıcı, servis: yok; güncelleme denetimi de CS2 açıkken yapılmaz, bkz. §10).
Ürün küçük ve sade kalmalıdır: yeni bir şey eklemeden önce neyi silerek aynı sonucun alınabileceğine bak.

```
CS2Hazirla.sln
├─ src/CS2Hazirla.Core    UI'sız mantık
│    ISystem               OS'a dokunan TEK arayüz  ·  WindowsSystem = gerçek uygulama
│    ProtectedList         kodda sabit korumalı liste (ad, önek, klasör)
│    Planner               salt-okunur plan (Önizleme)  ·  Orchestrator  Prepare / Revert
│    StateGuard            state.json içeriğini doğrular  ·  SessionStore  yazmadan-önce günlük
│    Status                ana ekranın başlığı ve alt yazısı (saf, test edilir)
│    Catalog, ProfileSession, ReopenPolicy, FriendlyNames, Metrics, SteamLocator
│    SearchText            Ayrıntılar aramasının normalleştirmesi (Türkçe harfsiz eşleşme)
│    HomeApps              ana ekran listesi: tarama sonucundan satırlar, mod + kullanıcı anahtarı, plana uygulama (Planner.ScanPrograms)
│    Update, UpdateFlow    uygulama içi güncelleme: imzalı sürüm bilgisi, indirme, doğrulama, kurulumu başlatma (§10)
├─ src/CS2Hazirla         WPF (koyu tema), yalnızca yönetici
│    Theme/Tokens.xaml · Icons.xaml · Controls.xaml    renk/yazı belirteçleri (degrade fırçalar), simgeler, denetim stilleri
│    TickRing.cs           64 çentikli halka (CS2'nin tick hızı); yalnızca çalışırken animasyon (en çok 30 kare/sn)
│    Motion.cs             giriş/yenileme yardımcıları + Breathing (halka ışığı) + tek hareket kapısı (Motion.Allowed / Available)
│    MainWindow.xaml       sol çubuk (Ana ekran/Profiller/Ayarlar, "Yeni sürüm var" kartı) + sayfalar + Ayrıntılar paneli
│    UpdateViewModel.cs    güncelleme kartı ve Ayarlar satırı (mantık UpdateFlow'da)
├─ tests/CS2Hazirla.Tests xUnit (sahte ISystem + gerçek Windows'a salt-okunur/izole testler)
├─ installer/Antilag.iss  Inno Setup (kullanıcı başına kurulum; güncelleme kipi)
├─ build.ps1              test + yayın + kurulum dosyası, sürüm numarası csproj'dan; -Yayinla ile imzalı GitHub sürümü
├─ tools/Yayinla          yalnızca sahibin kullandığı yayın aracı (anahtar üretme, imzalama, doğrulama); çözümde yok
├─ tools/make-icon.ps1    simgeyi üretir
├─ LICENSE, THIRD-PARTY-NOTICES.md, licenses/   GPL-3.0-or-later ve üçüncü taraf bildirimleri (kurulumla birlikte gelir)
└─ SECURITY.md, CONTRIBUTING.md, .github/       güvenlik politikası, katkı kuralları, hata formu, CI (yalnızca test)
```

## 2. Akış

Başlangıçta tek seferlik "CS2 çalışıyor mu?" kontrolü → (önceki oturum varsa ana ekranda hiçbir yazı çıkmaz, yalnızca geri açma düğmesi görünür; **kendiliğinden geri yüklenmez**; kayıt okunamazsa ya da daha yeni bir sürüm tarafından yazılmışsa bir uyarı penceresi çıkar) →
`Planner.Build()` (salt-okunur; her `Plan*` adımı `Guard` içindedir, bir yoklama patlarsa uyarıya döner ve plan sürer) →
`Orchestrator.PrepareAsync()` (her değişiklikten ÖNCE eski değer `state.json`'a yazılır; `Progress(biten, planlanan)` halkayı sürer: oran 64 çentiğe yayılır, programların kapatılması tek toplu adımdır, başarısız ya da atlanan adım da biten sayılır, ilk adım bitene kadar kısa bir yay döner) →
`Status.Compute()` başlığı üretir: yeşil "Bitti" yalnızca hiçbir şey ters gitmediğinde çıkar → kullanıcı **Uygulamayı kapat**'a basar →
`Shutdown` + `Environment.Exit` (arkada çalışan hiçbir şey kalmaz; yapılan sistem değişiklikleri geri yüklenene kadar durur).
CS2'yi kullanıcı başlatır; uygulama CS2'yi/Steam'i başlatmaz. Geri alma yalnızca büyük düğmenin altındaki küçük düğmeyle yapılır (`RestoreLabel`: açılabilir kapatılmış program varsa **Kapatılan uygulamaları geri aç**, yoksa **Ayarları geri yükle**).

Kapatma: tüm hedeflere WM_CLOSE → ortak 6 sn bekleme → kalanlar zorla sonlandırılır (kabul edilen ama hâlâ listede görünen işleme bir kez daha istek ve 6 sn bekleme; reddedileni beklemeye gerek yok) →
kendini yeniden açanlar için en çok 10 tarama / toplam 15 sn / bir ad için en çok 3 geri dönüş (iki sessiz bakışta biter; her taramada önce oyuna bakılır) → son bir kontrol
(geri gelenler `NotClosed` olur, "kapatıldı" denmez; neden günlük dosyasında `Detail` olarak: reddedildi mi, kabul edilip yaşamaya mı devam etti, kendiliğinden mi geri açıldı).
`WindowsSystem.HasEnded`: bitmiş ama başka bir işlemin tutamacıyla listede kalan (iş parçacığı sayısı 0) işlem "çalışıyor" sayılmaz. Günlükte her program için `ProcessRecord{Name,Path,CanReopen,RestartOnRevert,Reverted}` tutulur.
Geri yükleme `ReopenPolicy`'ye göre kendi `.exe` yolundan, Windows Gezgini aracılığıyla (yönetici yetkisi olmadan) açar;
güncelleyici, yardımcı parça, yorumlayıcı, parametreli engel-aşma/anti-cheat ve Store uygulamaları açılamaz, adlarıyla listelenir.

## 3. Profiller

`ProfileSession` (Core) etkin profili ve bu çalışmaya özel değişiklikleri tutar: her başlangıç Tam temizlik varsayılanıdır
(`AppSettings.Profile/Options` JSON'a yazılmaz), hazır ayarlar arası geçiş soru sormaz ve her ayar kendi değişikliğini oturum boyunca hatırlar.
Yalnızca `AppSettings.SavedProfiles` kalıcıdır. Ana ekrandaki şerit hazır ayarları ve kayıtlı profilleri tek `Chips` listesinde gösterir.
Profiller sayfası kendi taslak `AppSettings`'i üzerinde düzenler; sayfadan çıkarken ya da başka profile geçerken kaydedilmemiş düzenleme
**kaydedilir** (sessizce atılmaz). Silme sayfa içinde sorulur ("Silinsin mi?"), pencere açılmaz.

## 4. Arayüz

- Pencere: solda 216 px çubuk, sağda içerik. Ana ekran, içerik genişliği 880 px ve üstündeyse iki sütundur (`ApplyHomeLayout`): solda halka + durum başlığı + alt yazı (300 px),
  sağda hazır ayar şeridi (eşit 3 sütun) + **program listesi** + yazı düğmeleri (Ayrıntılar, Önizleme; çalıştırmadan sonra Önizleme yerine Tekrar çalıştır) (500 px).
  Daha darsa tek sütun, halka önde. Sağ sütun üstten hizalıdır ve geniş düzende blok yüksekliği sabittir (`HomeGrid.MinHeight`), ayar değişince şerit yerinden oynamaz.
  **Ana ekran sadedir:** başlıkta yalnızca "X hazır"; boştayken alt yazı, ayar açıklaması ve uyarı yok; ipucu (tooltip) yalnızca iki düğmede var (geri açma ve Tekrar çalıştır). Alt yazı yalnızca bir sonuçtan söz ederken çıkar (`Status`: Bitti, Geri yüklendi...).
  "Neler yapıldı" yalnızca Başlat çalışırken, bittikten sonra ya da bir işlem başarısız olunca görünür ve listenin yerini alır (`HomeView.Of`: liste ile günlük hiçbir zaman birlikte görünmez); geri yükleme bitince liste yeniden taranıp geri gelir ve başlık sonucu söyler. Büyük düğmenin baya altında, yer ayrılmış (Hidden) küçük bir düğme:
  **Kapatılan uygulamaları geri aç** (`ShowRestore` = açık oturum var ve çalışan iş yok) → `RevertCommand`.
- **Program listesi** (`HomeApps`): açılışta `Planner.ScanPrograms` (yalnızca program yoklaması, salt-okunur, tüm anahtarlar açık ayarla) o an çalışan kapatılabilir programları bulur; `HomeApps.From`
  yalnızca tanıdık kategorileri (Discord, GoodbyeDPI, tarayıcılar [tek satır], oyun mağazaları, müzik/sohbet, Claude, VPN, açık pencereli programlar) satıra çevirir. Önceden belirlenmiş liste yoktur.
  Satır durumu = kullanıcının anahtarı (`ProfileSession.AppChoices`, profil başına, yalnızca bu uygulama oturumu) yoksa seçili modun anahtarı (`HomeApps.Closes`). Yayıncı'da Discord/OBS gibi korumalı programlar
  kilitli ve kapalıdır. Başlat ve Önizleme, planı kurduktan sonra `HomeApps.Apply` ile bu anahtarları plana işler (kapalı → `Enabled=false`; modun bıraktığı bir program açıldıysa tüm-açık taramadan eklenir).
  Taramanın yenilenmesi: açılış, yenile düğmesi, geri yüklemeden sonra, pencere 20 sn'den uzun arkada kalıp öne gelince.
- **Önizleme penceresi yalnızca bilgi verir** (`PreviewWindow`, `PreviewViewModel`): Başlat'ın yapacağı işlerin (yalnızca `Enabled` olanların) grup grup yazısı; onay kutusu, "Her zaman açık kalsın" bağlantısı ve plana dokunan hiçbir şey yoktur, tek düğme "Kapat". Bir şeyin yapılmasını istemeyen ana listeden ya da Ayrıntılar'dan kapatır; "Her zaman açık kalsın" listesi Ayarlar'dadır.
- Geri yükleme kuralı: yeni Başlat, önceki çalıştırmanın ayarlarını ve hizmetlerini geri alır ama programlarını **açmaz ve saklamaz** (`Revert(restartApps:false, forgetPrograms:true)`); programları yalnızca
  geri açma düğmesi açar ve yalnızca en son çalıştırmanınkileri. (`ReopenDeferred`/`carryOver` düzeneği uygulamada artık kullanılmaz; testler için duruyor.)
- **Ayrıntılar** sağdan açılan paneldir (Esc ya da dışarı tıklama kapatır; arama doluysa ilk Esc aramayı siler, ikincisi paneli kapatır; Ölçümler / Bellek'te arama kutusu olmadığından Esc doğrudan kapatır). En üstte hazır ayar / profil şeridi (ana ekrandakiyle
  aynı `Chips` listesi ve `ChipTemplate`): panelden ayar değiştirince liste hemen o ayarın anahtarlarını gösterir. Sekmeler: **Özet** (yalnızca programlar), **Tam liste** (dört grup), **Ölçümler**, **Bellek**.
  Her satır yalnızca ad; `Hint` yalnızca adı kendi başına anlaşılmayanlarda, `Caution` yalnızca gerçek riskte. Arama (`SearchText`) başlık, ipucu ve anahtarın arkasındaki program/hizmet
  adlarına (`Catalog.SearchTerms`) bakar, her zaman tüm gruplarda arar, Türkçe harfleri ayırt etmez.
- Tam temizlik = her anahtar açık (`Catalog.MaximumOn` tüm `Options`'tan türetilir; yeni bir seçenek eklenince de açık gelir). Discord (`proc.discord`) ve GoodbyeDPI (`proc.dpi`, yalnızca o program) ayrı anahtarlardır ve listenin en üstündedir; diğer engel aşma programları (`proc.bypass`) kendi anahtarındadır ve ana ekran listesinde görünmez. 3.1'den önce kaydedilmiş bir profilde (3.0 dahil) `proc.discord` yoksa `proc.dpi`'nin değerini alır (`ProfileSession.Complete`); böylece eski profil eskisiyle aynı programları kapatır.
- Eski "Sade/Detaylı" kip anahtarı yoktur (Özet / Tam liste yalnızca neyin gösterileceğini seçer, neyin kapanacağını değiştirmez). `OptionDef` = `Id, Group, Title, Hint?, Caution?`: yeni seçenek eklerken `Hint`'i yalnızca gerektiğinde yaz.
- Renk: tek vurgu ailesi (lavanta-çivit); yeşil/sarı/kırmızı yalnızca durumdur ve hep bir sözcükle birlikte görünür (halka ışığı da duruma göre renk alır).
  Yüzeyler degrade, kenarlar 1 px degrade "cam kenarı" (`RimBrush`), ışımalar yarı saydam radyal degrade (bitiş durağı aynı RGB, alfa 0: gri kenar kalmaz).
  **Effect / BlurEffect / gölgelendirici / yeni resim, yazı tipi, kitaplık YOK; exe büyümez.** Arka plan ışığı (3 leke) ve ince tane dokusu (açılışta bellekte ~80 KB, bant oluşmasın diye)
  bir kez çizilip önbelleğe alınır (`Atmosphere`, `CacheMode=BitmapCache`); içinde hareket eden bir şey durmaz.
- Hareket kuralları: yalnızca Opacity ve render dönüşümleri hareket eder (yerleşim, Effect yok). Girişler (`Motion.Reveal`) bir kez çalışır ve biter, bitince animasyon kaldırılır (saat kalmaz).
  Tek tekrar eden animasyon `Breathing` (halka ışığı, 20 kare/sn, ~40 sn, sonra durur) ve yalnızca pencere önde, görünür, ana sayfada, Başlat çalışmıyor, Ayrıntılar kapalı ve `Motion.Allowed`
  (Windows animasyonları açık, GPU katmanı ≥ 2, uzak oturum değil, ekran görüntüsü çalışması değil) iken döner. Halkanın süpürme yayı da `Motion.Available` kapısından geçer.
  Şablon içi geçişler süreleri `MotionDictionary`'den alır (animasyonlar kapalıysa 0); üstüne gelme geçişleri yalnızca etkin denetimde çalışır (`MultiTrigger` + `IsEnabled`).
- Tıklama alanı: tıklanabilir her şablonun kökünde `Background` olmalı (şeffaf bile olsa; arkası boş şablon yalnızca yazının pikselinde tıklanır), `MinHeight` ≥ 36-40, simge düğmeleri 40×40,
  onay kutusu satırlarında yazı da kutunun içinde. Tek bilinçli istisna `HeroButton`: kök `{x:Null}`, tıklanabilir alan kare değil küredir. `HitAreaTests` bunu korur.
- Yazı: Segoe UI Variable; durum sözcüğü ve adım sayacı Bahnschrift. Yalnızca Windows'la gelen yazı tipleri.
- Sözlük: Özet · Tam liste · Kapatılır · Açık kalır · Ayarlar · Başlat · Tekrar çalıştır · Kapatılan uygulamaları geri aç · Ayarları geri yükle · Önizleme · Ayrıntılar · Neler yapıldı · Profiller · Bu profili seç · Ayarlar · Uygulamayı kapat · Listeden seç · Çıkar ·
  "Her zaman açık kalsın" · "Her seferinde kapat". Güç ayarı hep "güç planı", geri alma hep "geri yükle" ya da (programlar için) "geri aç" / "eski haline döndü"; "Geri yükle" adında bir düğme yoktur.

## 5. Anti-cheat güvenlik katmanları

1. Oyun açıkken uygulama zaten çalışmıyor.
2. `ISystem`'de oyuna dokunabilecek hiçbir üye yok (öncelik, affinity, bellek, komut satırı, enjeksiyon yok; test yapısal olarak doğrular).
3. Korumalı adlara (CS2, Steam, FACEIT, GPU/ses sürücüsü, Defender, Windows çekirdeği) **tutamaç açılmaz**, pencereleri sorgulanmaz;
   CS2 çalışırken hiçbir süreç için tutamaç açılmaz. Diğer oyunların hile korumaları kapatılabilir bir gruptur (korumalı değildir).
4. `WindowsSystem` içinde her durum değiştiren çağrının önünde "CS2 çalışıyor mu" kapısı var; Orchestrator her değişiklikten önce tekrar kontrol eder.
5. Kapatma/sonlandırma pid + gerçek ad ile doğrulanır (pid yeniden kullanımı başka sürece isabet edemez).
6. Korumalı kök klasörler: Steam kurulumu ve kütüphaneleri, FACEIT (ve FACEIT AC), Valve.
7. Fare/erişilebilirlik ayarları `SystemParametersInfo` ile **bayraksız (0)** uygulanır: diğer pencerelere WM_SETTINGCHANGE yayını yok, kalıcı yazım yok.
8. Release derlemesinde güvenlik kontrollerini atlayan bir mod yoktur (`--screenshot` vb. hepsi `#if DEBUG`).

## 6. Neyin kapandığı değişmez kuralı

"Bugün kapatılan hiçbir şey, bilerek yapılmadıkça kapatılmaz hale gelmemeli." Bunu `GoldenSelectionTests` korur: üç hazır ayar ve bazı anahtar
birleşimleri için Planner'ın seçtiği kapatılacak program ve durdurulacak hizmet kimlikleri `tests/CS2Hazirla.Tests/Golden/` altındaki kayıtla
birebir karşılaştırılır. Katalogdaki bir ad listesini, önekini ya da korumalı listeyi değiştirirsen bu test kırılır; değişiklik bilerek ise
kaydı güncelle ve nedenini commit mesajına yaz. Not: bir adı kategoriye eklemek, o adı "tanınmayan arka plan" taramasından çıkarıp kategori anahtarı
kapalıyken kapatılmaz hale getirebilir; bu yüzden ad eklemek de bir davranış değişikliğidir.

Golden kayıt 2.0.0'ın dilinde konuşur: `With(...)` yardımcısı `proc.discord` ve `proc.bypass`'ı `proc.dpi` ile birlikte kurar (2.0.0'da tek anahtar Discord, GoodbyeDPI ve diğer engel aşma programlarını birden sürüyordu), kayıt
dosyası hiç değişmedi. Anahtarların ayrı çalışması golden'ın kapsamı dışındadır ve `SplitSwitchTests` / `HomeScreenTests` ile sınanır; bölünmüş anahtar için golden'ı "güncelleme",
yeni davranışı onlara ekle.

## 7. Geri yükleme kapsamı (state.json)

Güç planı (+ oluşturulan plan), servis durumu + başlangıç türü, kayıt defteri (tür/veri birebir, oluşturulan anahtar silinir), fare ivmesi,
erişilebilirlik bayrakları, adaptör güç tasarrufu, kapatılan işlemlerin yolları. `state.json` kullanıcı tarafından yazılabildiğinden geri yüklemeden
önce her kayıt izin listesine karşı doğrulanır (`StateGuard`: servis adı, GUID, kayıt defteri anahtarı, yerel `.exe` yolu). Eski sürümlerin yazdığı
bir HKLM kaydı da (artık yazılmayan Nagle ayarı) güvenle geri alınır.

## 8. Test

```
dotnet test tests\CS2Hazirla.Tests --filter "FullyQualifiedName!~Live"
```

Sahte (bellek içi) sistemde: korumalı liste ∩ kapatma listesi = ∅ · dry-run = 0 yazma çağrısı · CS2 açıkken 0 değişiklik · hazırlık sırasında CS2
açılırsa durur · tepsiye küçülen pencereli uygulama 6 sn sonra zorla sonlandırılır · geri yükleme sonrası güç planı/servis/registry birebir aynı · çökme
sonrası kurtarma (adım adım enjekte edilir) · state.json tahrifatı · Planner'ın yazdığı her kayıt `StateGuard`'dan geçer · `Status` her dal · ayar dosyası
bozuksa yedeklenir · golden kapatma kümesi · ana ekran listesi ve geri yükleme kuralı (`HomeScreenTests`: yardımcı ve hizmet programları listelenmez, kapatılabilir uygulamalar listelenir, mod + kullanıcı anahtarı, Yayıncı'da Discord kilitli, plana işleme, Başlat program açmaz) ·
tıklama alanı koruması (`HitAreaTests`: şablon kökünde Background, en küçük yükseklikler) · güncelleme (`UpdateTests`: imza, sahte imza, eski sürüm, yönlendirme kuralları, indirme doğrulaması, klasör bağlantısı saldırısı, `UpdateFlow` her dal, CS2 açıkken hiçbir istek yok) ve kayıt biçimi (`JournalFormatTests`: daha yeni sürümün kaydına dokunulmaz). `Live*` sınıfları gerçek Windows'a dokunur (salt-okunur yoklama, izole kayıt anahtarı); CS2 açıkken tasarım
gereği çalışmazlar, bu yüzden `build.ps1` onları çıkarır (`-Canli` ile, CS2 KAPALIYKEN çalışır; korumalı işlemlere tutamak açılmadığını gerçek makinede
doğrulayan test de onların içindedir). `CS2H_LIVE=1` ortam değişkeni güç planını bir an değiştirip geri yükleyen testi açar.

Görünüm katmanı (XAML) için DEBUG derlemesi ekran görüntüsü alabilir; pencere ekran dışında açılır, odak almaz, hiçbir pencere açmaz:

```
Antilag.exe --screenshot out.png [--no-init] [--demo idle|busy|done|warn] [--drawer] [--full] [--search metin] [--hafif] [--stream] [--chips] [--metrics] [--memory] [--profiles] [--settings] [--preview] [--picker] [--dialog] [--narrow] [--idle-test saniye] [--lang tr|en] [--reopen]
```

`--lang en` ekranı İngilizce çizer ve `<png>.missing.txt` içine İngilizcesi bulunamayan metinleri yazar (boş olmalı). `--reopen` dil değiştirmeyi gerçekten yapar (Profil: Yayıncı seçilir, Ayarlar'dan English'e geçilir), yerine açılan pencereyi çizer ve `<png>.reopen.txt` içine sonucu yazar.

`--idle-test N` bir ekran görüntüsü almaz: pencere ekran dışında kalır, "önde" sayılır (halka ışığı döner), `<png>.idle.txt` içine hareket kapısının durumunu yazar ve N saniye sonra çıkar.
Çağıran, işlemci süresini bu aralıkta örnekler (son ölçüm, 16 mantıksal çekirdekli bir makinede: tüm makinenin %0,04'ü). Her görsel değişiklikten sonra bunu ve `publish\Antilag.exe` boyutunu (~66 MB) yeniden bak.

Ayarlar `CS2HAZIRLA_DATA` ortam değişkeniyle başka klasöre alınabilir (ekran görüntüsü çalışmaları otomatik geçici klasör kullanır).

## 9. Derleme ve yayın

**Derleme (herkes için).** Windows 10/11, .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`) ve Inno Setup 6 (`winget install JRSoftware.InnoSetup`) gerekir.
Derleme ve testler yalnızca Windows'ta desteklenir (WPF, WMI, hizmetler).

```
git clone https://github.com/fara1ka/antilag.git
cd antilag
.\build.ps1            # testler + yayın + kurulum dosyası (dist\Antilag-Setup.exe)
.\build.ps1 -Sessiz    # aynısı, ama düşük öncelikle (derleme sırasında bilgisayarı yormaz)
```

Sürüm numarası tek yerden gelir: `src/CS2Hazirla/CS2Hazirla.csproj` içindeki `<Version>`. Çıktılar: `publish\Antilag.exe` (tek dosya, ~66 MB, .NET
kurulu olmasa da çalışır, `requireAdministrator` manifestli) ve `dist\Antilag-Setup.exe`; ikisi de git'e girmez.
Boyutu küçültmenin gerçekçi bir yolu yoktur: kırpma (trimming) WPF'te risklidir; "framework-dependent" ise kullanıcıdan ayrıca .NET kurmasını ister.

**Fork yapıyorsan** `UpdateClient.ReleasesUrl` ve `UpdateSigning.PublicKeys` değerlerini kendi deponla ve kendi anahtarınla değiştir; aksi halde uygulaman bu projenin sürümlerini arar
ve (anahtarlar uyuşmadığı için) hiçbirini kurmaz. Değiştirilmiş sürümleri "Antilag" adıyla dağıtma (README > Lisans).

**Yayın (yalnızca proje sahibi).**

1. `<Version>` artır (`installer/Antilag.iss` sürümü /DAppVersion ile build.ps1'den alır). Değişiklikleri commit et ve push et.
2. `.\build.ps1 -Sessiz -Yayinla "Bu sürümde neler değişti"` şunları yapar: testler → yayın → kurulum dosyası → (kaydedilmemiş değişiklik ya da gönderilmemiş commit varsa durur) →
   `tools/Yayinla imzala` ile `dist\latest.json` (sürüm, boyut, SHA-256, ECDSA imzası; kurulum dosyasının kendi sürümü csproj'dakiyle aynı değilse imzalamaz) → `dogrula` →
   etiket `v<sürüm>` → GitHub'da **taslak** sürüm (`Antilag-Setup.exe` + `latest.json`) → iki dosya da taslakta görülünce yayınlanır → `kontrol` ile herkese açık adresten en yeni sürüme bakılır.
   Taslak adımı bilerek vardır: `latest.json` olmadan kimsenin "en son sürümü" olmaz; GitHub'da yayınlanmış bir sürüme dosya eklenemeyebilir (değişmez sürümler).
3. Windows dijital imzası (Authenticode) eklendiğinde, `Antilag.exe` ve kurulum dosyası imzalandıktan **sonra** `latest.json` üretilir (SHA-256 imzalı dosyanın SHA-256'sıdır); sıra değişirse tüm kopyalar güncellemeyi reddeder.
4. Elle bir kez dene: bir önceki sürümden güncelleme kartı çıkıyor mu, Güncelle çalışıyor mu (`tools/Yayinla kontrol --surum <eski>` yalnızca imza ve sürüm bilgisini sınar).

Yayın geri alınamaz: eski sürümleri **silme**. Hatalı bir sürüm için tek yol, eski kodu içeren daha yüksek bir sürüm yayınlamaktır.

**İmza anahtarları.** `tools/Yayinla anahtar-uret` günlük anahtarı `%LOCALAPPDATA%\Antilag-Yayin\imza.anahtar` içine, Windows hesabına bağlı şifreli (DPAPI) yazar; düz metin olarak hiçbir yerde durmaz.
`yedek-anahtar-uret` ikinci (yedek) anahtarı senin seçtiğin parolayla şifreler; bu klasörü bir USB belleğe de kopyala, parola kaybolursa yedek işe yaramaz.
İkisinin de **genel** anahtarı `UpdateSigning.PublicKeys` içindedir (kurulu kopyalara sonradan anahtar eklenemez, bu yüzden ikisi birlikte yayınlanır). Özel anahtar repoda, günlükte, sohbette ya da GitHub'da bulunmaz;
`.gitignore` `*.anahtar`, `*.pfx`, `*.pem` ve benzerlerini dışarıda tutar. Anahtar sızarsa/kaybolursa bkz. `SECURITY.md`.

## 10. Uygulama içi güncelleme

Hedef: sahibi yeni sürüm yayınlayınca kurulu her Antilag bir sonraki açılışta bildirim gösterir; isteyen tek tıkla günceller. Hiçbir şey arka planda çalışmaz.

- **Denetim:** `MainViewModel.InitAsync`'in **son** adımı; CS2 açıkken uygulama zaten kapandığı için o yola hiç varılmaz. `UpdateFlow.CheckAsync` ayrıca CS2'yi bir kez daha sorar (`UpdateCheck.SkippedGame`; Ayarlar "CS2 çalışırken denetlenmez" der, "güncel" ya da "internet yok" demez). Tek istek:
  `https://github.com/fara1ka/antilag/releases/latest/download/latest.json` (GitHub API değil: hız sınırı yok). Ayarlar'daki anahtar kapalıysa kendiliğinden hiç istek yapılmaz; "Şimdi denetle" yine çalışır (kullanıcı istedi). Bildirim yalnızca bir kart; pencere açılmaz.
- **Güven zinciri:** `latest.json` = `{version, size, sha256, signature}`; imza `antilag-update|<sürüm>|<boyut>|<sha256>` metninin ECDSA P-256/SHA-256 imzasıdır (`UpdateManifest.Canonical`, kültürden bağımsız).
  `UpdateSigning.PublicKeys` içindeki herhangi bir anahtarla doğrulanmayan, biçimi bozuk (`UpdateManifest.Problem`), eski ya da aynı sürümlü bilgi kullanılmaz. İndirme adresi **imzalı sürüm numarasından** üretilir, bilgi dosyasından alınmaz.
- **Ağ kuralları (`UpdateClient`):** yönlendirmeler elle izlenir, her adımda yalnızca https, 443, kullanıcı bilgisi yok, IP yok, ana makine `github.com` ya da `*.githubusercontent.com`; en çok 6 adım. HTML yanıt, yanlış boyut = "ağ sorunu" (bütünlük hatası değil);
  bütünlük hatası yalnızca tam gelen ama imzalı dosyadan farklı olan dosyadır. Hata kullanıcıya düz bir cümleyle söylenir, ayrıntı yalnızca günlük dosyasına yazılır (`ILog.Detail`).
- **Klasörler:** `%LOCALAPPDATA%\Antilag\update\<32 hex rastgele>\`. Yönetici yetkili uygulama kullanıcının yazabildiği klasörde çalıştığı için kök ve üstü bağlantı (junction) ise reddedilir, temizlik yalnızca bu sınıfın yarattığı dosya türlerini (`Antilag-Setup-*.exe`, `.update`) kendi klasörlerinde siler ve bağlantıya girmez.
  İndirilen dosya, ağ trafiğinden **önce** oluşturulur ve tamamlanana kadar açık tutulur (başkası okuyabilir, yazamaz/silemez): klasör hiç boş kalmaz. Bu, aynı hesaptaki bir programa karşı tam koruma değildir (SECURITY.md).
- **Başlatma:** `UpdateFlow.InstallAsync` indirir → boyut+SHA-256 → (kullanıcı beklerken) tekrar doğrular → yanına `<dosya>.update` notu bırakır → `ISystem.RestartProcess` (explorer.exe aracılığıyla, yönetici hakları olmadan) →
  notun **kurulum tarafından silinmesini** bekler (en çok 40 sn; silinmezse uygulama kapanmaz, "Kurulum başlatılamadı" der) → ancak o zaman `ExitRequested`.
  İndirme sürerken CS2 saniyede bir kontrol edilir (anlık işlem listesi, tutamaçsız; ilerleme geri çağrısından): görünürse indirme iptal edilir ve dosya silinir. İndirme kesilirse "İndirilemedi", zaman aşımı da öyle; yalnızca kabuğa verme ya da not hatası "Kurulum başlatılamadı" der.
- **Kurulum kipi (`installer/Antilag.iss`):** not varsa `UpdateMode`. Önce uygulamanın kapanmasına bağlı olmayan kararlar verilir ve **not yerinde bırakılır**: oyun (`cs2.exe`, işlem listesinden, tutamaçsız) açıksa ya da daha yeni bir sürüm kuruluysa kurulum sessizce çıkar (uygulama açık kalır, süre dolunca "Kurulum başlatılamadı" der). Sonra not silinir ("üstlendim, kapanabilirsin"), uygulamanın penceresi kapanana **ve** `Antilag.exe` kilidi çözülene kadar (20 sn) beklenir; sormaz (görev ve hazır sayfaları atlanır, bitiş sayfası kendiliğinden geçer);
  bitince oyun açık değilse uygulamayı yeniden açar. Not yoksa her şey eskisi gibidir (açık mı kontrolü pencereye ve dosya kilidine birlikte bakar); daha yeni bir sürümün üstüne eski kurulum elle çalıştırılırsa sorar, güncelleme kipinde sessizce reddeder. `CloseApplications=no`, `SetupMutex=AntilagSetup`. İlk güncelleme kuran sürüm (3.4.0) kendi kurulum akışını denemeden yayınlanır: kurulum kipi her yayın öncesi `iss-sandbox` tarzı bir deneme klasöründe (gerçek kurulumdan ayrı AppId, yollar ve pencere adıyla) sınanmalıdır.
- **Pencere açılmaz (3.5.1):** güncelleme uygulamanın kendi kartında görünür (indirme yüzdesi, "Kuruluyor…"). Kabuk (`explorer.exe`) komut satırı bayrağı veremediği için kurulum bayraksız başlar; not varsa ve `WizardSilent()` değilse aynı kurulumu `cmd.exe /c ping -n 2 127.0.0.1 >nul & "<kurulum>" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` ile bir saniye sonra yeniden başlatıp kendisi hemen biter (bir saniye: ilk kopya kurulum mutex'ini bıraksın). Not yerinde kalır; sessiz kopya karar verir, notu siler, uygulamanın kapanmasını bekler. Günlük `%LOCALAPPDATA%\Antilag\update\kurulum.log` (klasör varsa). Pencere olmadığı için "dosya hâlâ kullanımda" gibi bir hata kutusunu kimse görmez: uygulama penceresi yoksa eski Antilag yeniden açılır. Yeniden başlatma başarısız olursa pencereli kurulum olduğu gibi sürer. Eski sürümlerin uygulaması da (bayraksız başlatan) böylece pencere görmez. Sınav: kurulum kum havuzunda (`iss-sandbox`: bayraksız başlatma 5/5 pencere 0, A-H, dosya tutulu kalırsa eski uygulama açılır).
- **Kilit:** güncelleme sürerken (`UpdateVM.IsWorking`) `MainViewModel.IsIdle` false'tur: Başlat, Önizleme, geri yükleme, profil değiştirme ve pencereyi kapatma bekler. "Vazgeç" indirmeyi iptal eder ve dosyayı siler.
- **Kayıt biçimi:** `SessionState.Schema` (şimdi 1). Daha yeni bir sürümün yazdığı `state.json` "bozuk" sayılıp kenara alınmaz; dokunulmaz, geri yükleme "daha yeni sürüm" der. Güncelleme `%APPDATA%\CS2Hazirla`'ya hiç dokunmaz.
- **Sınırlar:** (1) kurulum kullanıcı klasöründe, uygulama yönetici: aynı hesapta çalışan bir zararlı `Antilag.exe`'yi değiştirebilir ve UAC onayında yönetici yetkisi kazanır (SECURITY.md); (2) ilk kurulum dosyasının imzası yoktur (Authenticode); (3) tekrar oynatma: eski ama imzalı bir bilgi, istemcinin sürümünden yüksekse kabul edilir (yalnızca daha yeni sürüm sunar); (4) günlük imza anahtarı yalnızca DPAPI ile bağlıdır, aynı hesaptaki zararlıya karşı korumaz.
- **Yayın zinciri sınırı:** imza aracı, imzalayan anahtarın **son yayınlanan etiketteki** (kurulu kopyaların güvendiği) anahtarlardan biri olmasını ve hiçbirinin listeden düşmemesini şart koşar (`--onceki`). Sürüm bilgisi imzası doğrulanamazsa Ayarlar "Sürüm bilgisi doğrulanamadı; güncelleme sunulmadı" der, internet suçlanmaz.
## 11. Diller (Türkçe ve English)

- **Kaynak dil Türkçedir ve anahtardır.** Koddaki Türkçe metin olduğu gibi kalır; gösterildiği yerde `Loc.T("Türkçe metin")` (sabit metin) ya da `Loc.F("{0} program kapatıldı", n)` (değerli metin) kullanılır. Türkçedeyken hiçbir şey aranmaz, metin aynen çıkar: Türkçe metin okuyan tüm testler olduğu gibi çalışır. İlk argüman TEK bir metin sabitidir (`+` ile bağlanmış sabitler olur; `$"..."`, değişken ya da `const` olmaz): testler anahtarı kaynaktan okur.
- **İngilizce** `src/CS2Hazirla.Core/Localization/En*.cs` dosyalarında, alan başına bir dosya (`EnCatalog`, `EnPlanner`, `EnOrchestrator`, `EnSystem`, `EnUpdate`, `EnViewModels`, `EnProfiles`, `EnWindows`, `EnXaml`, `EnCommon`); `EnglishStrings` hepsini birleştirir. Aynı Türkçe metnin iki farklı İngilizcesi olamaz. Çoğul yalnızca İngilizce şablonda: `{0} {0?program:programs}` (0. değer 1 ise iki nokta üstüste öncesi, değilse sonrası).
- **Çevrilmeyen şeyler:** kimlikler (seçenek kimliği, işlem/hizmet adı, kayıt defteri yolu), dosya adları, marka adları, kullanıcının yazdığı adlar. Hem kimlik hem gösterim olan metin (grup başlığı gibi) Türkçe kalır ve yalnızca gösterildiği yerde çevrilir (`Title = Loc.T(g.Key)`); sabit sınıf alanlarında ve `static` başlatıcılarda `Loc` çağrılmaz (dil çalışma sırasında seçilir). Katalog gibi veri tablolarında Türkçe kalır; İngilizcesi elle `En*.cs` içine yazılır (`Every_text_of_the_catalog_has_an_English_entry` kontrol eder).
- **XAML:** sabit metin `Text="{local:T 'Ayarlar'}"` biçiminde yazılır (`TExtension`; metin içindeki `'` işareti `\'` olur). Metin özelliği (`Text, Content, ToolTip, Header, Title, Tag, AutomationProperties.Name/.HelpText`) doğrudan Türkçe yazılırsa test başarısız olur. Bağlanan (`{Binding}`) metni görünüm modeli zaten doğru dilde üretir; `StringFormat` içinde sözcük olmaz, bunun için görünüm modeline özellik yazılır (`RemoveLabel`, `CloseLabel`).
- **Dil seçimi:** `AppSettings.Language` (`tr`, `en`; boş = hiç seçilmedi: Windows Türkçeyse `tr`, değilse `en`, bkz. `Loc.Detect`). Açılışta ayar dosyasından ilk iş `SettingsStore.PeekLanguage()` okunur (dosyaya dokunmaz). Ayarlar'ın en üstündeki "Dil / Language" kartı (`LanguageViewModel.cs`) dili kaydeder ve `App.ReopenIn` aynı pencereyi seçilen dilde yeniden açar: aynı yer ve boyut, Ayarlar sayfası, bu çalıştırmada yapılan profil ve anahtar seçimleri (`ProfileSession.Snapshot`) korunur; çalışan bir iş varsa kart zaten kapalıdır (`IsIdle`). Her metin pencere kurulurken üretilir; pencere açıkken dil değişmez.
- **Testler** (`LanguageTests`): kaynaktaki her `Loc.T/Loc.F` sabitinin, her XAML metninin ve kataloğun her metninin İngilizcesi var; yer tutucular ({0}...) iki dilde aynı; İngilizce metinde Türkçe harf kalmamış; çakışma yok. Bütün takımı İngilizcede çalıştırmak için `CS2H_TEST_LANG=en`: Türkçe metin okuyan testler tasarım gereği başarısız olur, ama DAVRANIŞ testi başarısız olursa bir yerde çevrilmiş metin kimlik yerine kullanılmıştır (bir grup adı karşılaştırılıyor, bir anahtar kaydediliyor...).
