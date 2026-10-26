# Antilag

CS2'ye girmeden önce çalıştır: arka plandaki gereksiz programları kapatır, birkaç Windows ayarını oyun için düzeltir. Bitince
**Uygulamayı kapat**'a basarsın ve CS2'yi açarsın. Oyun açıkken Antilag hiçbir değişiklik yapmaz ve hiçbir şeyi izlemez. İnternete yalnızca üç iş için
çıkar: ping ölçümü, Kontrol sayfasındaki internet kontrolü ve yeni sürüm denetimi ([Gizlilik](#gizlilik)). Windows 10/11 (64 bit), tek `.exe`, yalnızca yönetici olarak çalışır.
Açık kaynaktır ([Lisans](#lisans)). Arayüz **Türkçe** ve **English**: dili Ayarlar'ın en üstündeki "Dil / Language" kartından seçersin (seçmediysen Windows Türkçeyse Türkçe, değilse English açılır).

> **Dürüst beklenti:** Antilag ping'i düşürmez ve sihirli bir FPS artışı vermez; bir kazanç garantisi de vermez, sonuçlar bilgisayarına göre değişir.
> Yaptığı şey, oyun sırasında bellek, işlemci, disk ve internet tüketen arka plan işlerini kapatmaktır. Bilgisayar zaten temizse
> **Ayrıntılar > Ölçümler** değişiklik olmadığını dürüstçe gösterir. Uygulamanın sol üstündeki "input lag'ı azaltmak için tasarlanmıştır" cümlesi bir **amaçtır**:
> Antilag input lag'ı ölçmez ve azalttığını garanti etmez; Hafif ayarı ise hiçbir program kapatmaz.

## Kurulum

[Sürümler](https://github.com/fara1ka/antilag/releases/latest) sayfasından `Antilag-Setup.exe`'yi indir (yaklaşık 62 MB; içinde .NET çalışma zamanı vardır),
çalıştır ve **Kur**'a bas (yönetici izni gerekmez). Masaüstüne ve Başlat menüsüne **Antilag** kısayolu konur. Açılışta çalışan hiçbir şey eklenmez.
Kaldırmak için Windows Ayarlar > Uygulamalar'dan "Antilag"ı kaldır; ayarlarını silip silmeyeceğini sorar. Açılış sayfasında kapattığın programlar kaldırınca da kapalı kalır ([Açılış ve Kontrol sayfaları](#açılış-ve-kontrol-sayfaları)).

Kurulum dosyası henüz Windows'un dijital imzasıyla (Authenticode) imzalı değildir, bu yüzden Windows "Bilinmeyen yayıncı" ya da "Windows bilgisayarınızı
korudu" uyarısı gösterebilir: **Daha fazla bilgi > Yine de çalıştır**. Dosyayı yalnızca bu sayfadan indir; sürüm notundaki SHA-256 değerini kontrol edebilirsin
(`Get-FileHash .\Antilag-Setup.exe`). Ayrıntı: [Kod imzalama](#kod-imzalama).
Elle kurarken Antilag açıksa önce **Uygulamayı kapat**'a bas. Uygulama içinden güncellerken Antilag kendini kapatır ve kurulum bitince yeniden açılır.

## Kullanım

1. **Antilag**'ı aç (yönetici izni ister, "Evet" de).
2. İstersen sağdaki şeritten (pencere darsa halkanın altındakinden) bir hazır ayar ya da profil seç. Altındaki listede, bilgisayarında **şu an açık olan** uygulamalar (Discord, tarayıcılar, Spotify, WhatsApp, GoodbyeDPI…) birer anahtarla görünür: seçtiğin ayara göre açık ya da kapalı başlarlar, istediğini tek tıkla değiştirirsin. Windows hizmetleri ve ayarları için **Ayrıntılar**'a bak.
3. Büyük **Başlat** düğmesine bas. Halkadaki çentikler biten adımlarla yanar (programların kapatılması tek adım sayılır); yapılan her şey altta **Neler yapıldı** listesine satır satır yazılır.
   Bitince **Bitti** yazar. Bir şey kapanmadıysa başlık sarı olur ve hangisinin kapanmadığını söyler.
4. **Uygulamayı kapat**'a bas, sonra **CS2'yi sen aç**. Antilag CS2'yi ve Steam'i başlatmaz.
5. Oyundan sonra Antilag'ı aç ve büyük düğmenin altındaki **Kapatılan uygulamaları geri aç**'a bas: ayarlar ve hizmetler eski haline döner, kapanan programlar yeniden açılır (hiçbir program kapanmadıysa düğmenin adı **Ayarları geri yükle** olur). Yeniden **Başlat**'a basmak hiçbir programı geri açmaz; yalnızca bu düğme, **en son çalıştırmada** kapanan programları açar.

Programlar kapatılırken **onay sorulmaz**. Önce nazikçe "kapan" denir, 6 saniyede kapanmayan program zorla kapatılır; kaydedilmemiş iş
kaybolur, o yüzden önce kaydet. Kendini yeniden açan bir program (bir hizmet ya da yardımcı program açıyor olabilir) birkaç kez daha kapatılır; toplam en çok 15 saniye uğraşılır.
Yine de geri gelen ya da kapanmayan program başlığın "Bitti, ancak … program kapanmadı" demesiyle ve **Neler yapıldı** listesiyle adıyla söylenir; nedeni günlük dosyasına yazılır.

**Ana ekrandaki program listesi.** Antilag açılırken bilgisayarına kısaca bakar ve **o anda açık olan**, oyunu etkilemeden kapatılabilecek uygulamaları listeler; önceden belirlenmiş bir liste yoktur,
yalnızca gerçekten çalışanlar görünür (kullanmadığın bir program listede çıkmaz). Her satır yalnızca bir ad ve bir anahtardır.
- Anahtarlar seçtiğin hazır ayara göre başlar (Hafif'te hepsi kapalı, Tam temizlik'te hepsi açık, Yayıncı'da Discord ve yayın araçları her zaman açık kalır). Bir satıra tıklamak yalnızca o programı değiştirir; bu seçim o profil için Antilag açık kaldığı sürece geçerlidir.
- Listede Discord ve GoodbyeDPI en üsttedir, sonra en çok bellek kullanan program gelir. Tarayıcılar tek satırdır. Arka planda çalışan yardımcılar (güncelleyiciler, yedekleme, marka ve ışık programları, ekran üstü araçlar, antivirüs, hile koruma parçaları, tanınmayan programlar, diğer engel aşma programları) listelenmez; onlar **Ayrıntılar > Özet / Tam liste**'de eskisi gibi kendi anahtarlarıyla kapanır.
- Sağdaki yenile düğmesi taramayı tekrarlar; pencere bir süre arkada kaldıktan sonra öne gelince de kendiliğinden yenilenir.
- Başlat'a basınca listenin yerini **Neler yapıldı** listesi alır; hiçbir şey yapılmadan önce o kutu görünmez.

**Görünüm.** Işıma, cam kenar ve degradeler kodla çizilir; yeni resim, yazı tipi ya da kitaplık eklenmediği için dosya büyümez. Arayüz kısa geçişler kullanır;
Windows'ta animasyonlar kapalıysa hiçbir şey hareket etmez. Tıklanabilir her düğmenin tüm kutusu tıklanır.

**Ayrıntılar** (ana ekranda) sağdan bir panel açar.
- En üstteki şeritten hazır ayarı ya da profili seçersin; aşağıdaki liste o ayara göre hemen değişir.
- **Özet**: kapanacak programlar. En üstte **Discord**, hemen altında **GoodbyeDPI** vardır; ikisi ayrı anahtardır, istediğini kolayca açıp kapatırsın.
- **Tam liste**: programların yanında Windows hizmetleri, Windows ayarları ve internet ayarları. Her satır bir anahtardır.
- Özet ve Tam liste'de, listenin üstünde bir **arama kutusu** var: kapatılmasını istemediğin bir şeyi bulmak için programın adını yaz ("dropbox", "discord"). Türkçe harf kullanmadan da bulur ("tarayicilar", "guc plani"). Arama her zaman tüm listeyi tarar; `Esc` aramayı siler.
- **Ölçümler**: önce / sonra değerleri.
- **Kullanım**: en çok kaynak kullanan 10 program; **Bellek / İşlemci / Okuma/yazma** düğmeleriyle sıralarsın. İşlemci (toplam işlemcinin yüzde kaçı) ve okuma/yazma (programın dosyalardan okuduğu ve dosyalara yazdığı veri; disk dahil, ama yalnızca disk değil) iki okumayla, sekmeyi açtığında ya da **Yenile**'ye bastığında 2 saniyede ölçülür; arka planda kendiliğinden ölçülmez. Korumalı adlar (CS2, Steam, FACEIT, Windows parçaları, ekran kartı ve ses sürücüsü bileşenleri) ölçülmez: adı korumalı listede olan hiçbir programa tutamak açılmaz. Diğer hile korumaları (Vanguard, EasyAntiCheat...) kapatılabilir programlar gibi listelenir ve ölçülür. **Kapat** soru sormadan kapatır (5 saniyede kapanmazsa zorla); ana ekrandaki geri açma düğmesi programı yeniden açar.
- **Tam liste**'nin sonunda **Öneriler** vardır: ekranın yenileme hızı düşükse (144 Hz'e kadar çıkabilen monitör 60 Hz'de çalışıyorsa), Wi-Fi ve bellek hızı. Antilag buna dokunmaz, yalnızca söyler; ekran notundaki **Ekran ayarlarını aç** düğmesi Windows'un kendi sayfasını açar.

Kendi anlaşılır adı olmayan birkaç satırın altında kısa bir ipucu, gerçek bir risk taşıyanın altında sarı bir uyarı vardır.

## Hazır ayarlar ve profiller

| Ayar | Ne yapar |
|---|---|
| **Hafif** | Hiçbir program kapatmaz; Windows güncellemelerini geçici durdurur ve birkaç Windows ayarı yapar (en yüksek performans güç planı, Oyun Modu…) |
| **Tam temizlik** (her açılışta ilk seçili) | **Her anahtar açık gelir**, işaretsiz satır yoktur. CS2, Steam, FACEIT, Windows, ekran kartı ve ses sürücüleri ile "Her zaman açık kalsın" listendekiler dışındaki programları kapatır; Windows bildirimlerini susturur (geri yüklenene kadar) ve CS2 için tam ekran iyileştirmesini kapatır. İstediğini Ayrıntılar'dan kapatabilirsin |
| **Yayıncı** | OBS, Streamlabs, Discord, Elgato gibi tanınan yayın araçları, tarayıcı, VPN, GoodbyeDPI, diğer engel aşma programları ve tanınmayan arka plan programları açık kalır; gerisi kapanır (hile koruma istemcileri dahil, aşağıya bak). Bildirimler susturulur (geri yüklenene kadar) ve tam ekran iyileştirmesi kapatılır. Başka bir yayın aracını Ayarlar'dan "Her zaman açık kalsın" listesine ekle |

Hazır ayarlar arasında geçerken soru sorulmaz. Bir anahtarı değiştirip başka ayara geçersen o ayar kendi varsayılanıyla açılır; geri dönünce
değişikliğin durur. Bu **yalnızca Antilag açıkken** geçerlidir: kapatıp açınca her şey varsayılana döner.

**Profiller** sayfasında kendi ayarlarını kaydedersin; profil, ana ekrandaki şeritte hazır ayarların yanında görünür. Profil kalıcıdır.

**Geçici durdurulan Windows hizmetleri** (yalnızca durdurulur, silinmez; geri yüklenince ya da bilgisayar yeniden başlayınca geri gelir):
Windows Update, Güncelleme Orkestratörü, BITS ve Teslim İyileştirme (Hafif dahil hepsinde); Windows Arama, Yazıcı hizmeti (**yazdıracaksan kapalı tut**), DiagTrack, SysMain,
İndirilmiş haritalar, Konum hizmeti, Hata raporlama ve üç Xbox hizmeti (Tam temizlik ve Yayıncı). Bunlar gizlilik özelliği değildir: yeniden başlatınca geri gelirler.

## Açılış ve Kontrol sayfaları

Sol çubukta Ana ekran ve Profiller'in yanında iki sayfa daha vardır. İkisi de oyunu hazırlamaktan bağımsızdır; **CS2 açıkken hiçbir şey okumaz, göndermez ya da değiştirmez** ve bir çalıştırma, önizleme ya da güncelleme sürerken de beklerler (ölçümü bozmasınlar diye).

**Açılış** Windows açılırken kendiliğinden başlayan programları gösterir: Çalıştır kayıt anahtarları ve iki Başlangıç klasörü (zamanlanmış görevler, hizmetler ve Mağaza uygulamaları yok). Her programın yanında bir anahtar vardır.
- Anahtarı kapatmak programı silmez, çalışıyorsa kapatmaz, kısayoluna ya da kaydına dokunmaz: yalnızca Windows'un kendi "başlangıç uygulamaları" bayrağını (`...\Explorer\StartupApproved`) yazar. Görev Yöneticisi'nin Başlangıç sekmesi ve Windows Ayarlar > Uygulamalar > Başlangıç aynı ayarı kullanır; istediğin zaman oralardan ya da aynı anahtardan geri açarsın.
- Senin hesabın için kurulu programların bayrağı kullanıcı kaydına (HKCU), bu bilgisayardaki herkes için kurulu olanların bayrağı makine kaydına (HKLM) yazılır; ikincisi yönetici izni ister (Antilag zaten yönetici olarak çalışır). Bir hesaptan öbürüne geçilmez: Antilag'ı başka bir yönetici hesabının parolasıyla açarsan sayfa o hesabın listesini gösterir ve değiştirir.
- Antilag'ın kendisi, güvenlik programları ve bilgisayarın çalışması için gerekli olabilecek programlar (Windows, ekran kartı, ses) kilitlidir. Bu anahtarlar geri yükleme kaydına girmez: **Geri yükle** onlara dokunmaz ve Antilag'ı kaldırsan da olduğu gibi kalırlar.

**Kontrol** yalnızca bakan iki kısa inceleme yapar; bilgisayarında hiçbir şeyi değiştirmez.
- **Bilgisayarın durumu**: disk boş yeri, Windows'un kaç gündür yeniden başlatılmadığı, bekleyen yeniden başlatma, pil, ekran kartı sürücüsünün tarihi, bozulabileceği düşünülen disk ve oyunun HDD'de olması gibi birkaç bilgi. Söyleyecek bir şey olmayan ya da okunamayan konu hiç gösterilmez. Sayfa açılınca ve pencere öne gelince bakar; ilgili Windows ayar sayfasını açan bir düğme dışında bir şey yapmaz.
- **İnternet kontrolü**: yalnızca **Kontrol et**'e basınca çalışır. Modeme, iki bilinen internet adresine ve (`settings.json` içine yazdıysan) kendi ping hedefine birkaç saniye ping atar, bir de adres çözme süresine bakar ve sorunun evde mi (Wi-Fi, modem), internet sağlayıcısında mı olduğunu söyler. Ping ile ölçtüğü için oyun sunucusuna giden yolu birebir göstermez ve ping'i engelleyen bir ağda yanıltabilir; sonuç bunu söyler.

## Neyi kapatmaz

CS2, Steam (kütüphane klasörleri dahil), FACEIT, Windows'un kendi parçaları, Windows Defender, ekran kartı ve ses sürücüsü bileşenleri ve Windows'un web bileşeni (WebView2).
Bunlar kapatılırsa oyun ya da Windows kararsızlaşabilir. **Ayarlar > Her zaman açık kalsın** listesine eklediğin programlar da hiçbir ayarda kapanmaz.

Bazı programların asıl işi bir **Windows hizmeti** olarak çalışır (antivirüs motoru, VPN tüneli, Vanguard gibi hile korumaları). Antilag bunlardan
yalnızca pencereyi ve tepsi simgesini kapatır; hizmetin kendisi çalışmaya devam eder. İlgili satırlarda bu yazılıdır.

## Geri yükleme neyi geri getirir, neyi getiremez

- **Geri getirir:** güç planı, durdurulan Windows hizmetleri, kayıt defteri ayarları, ağ kartı ayarı, fare ivmesi ve Yapışkan Tuşlar ayarı
  (birebir) ve **en son çalıştırmada** kapatılan programlar (kendi `.exe` dosyalarından yeniden açılır). Yeniden Başlat'a basınca önceki çalıştırmanın ayarları geri alınır ama programları açılmaz ve unutulur.
- **Getiremez:** kaydedilmemiş iş, tarayıcıda açık sekmeler (tarayıcı kendisi sorarsa kurtarır; yoksa Ctrl+Shift+T), kendini güncelleyen programlar,
  arka plan yardımcı parçaları, engel aşma programları (GoodbyeDPI, Zapret), diğer oyunların hile koruma parçaları ve Microsoft Store uygulamaları. Bunların hangileri açılamadıysa
  geri yükleme bittiğinde adlarıyla yazılır.
- **Dokunmaz:** Açılış sayfasındaki anahtarlar (Windows'un kendi ayarıdır; geri açmak için aynı anahtarı ya da Görev Yöneticisi'ni kullan).
- Antilag'ı açınca **hiçbir şey kendiliğinden geri yüklenmez**; kapattığın programlar kapalı kalır. Yeni bir Başlat da önce önceki çalıştırmanın ayarlarını sessizce eski haline getirir.
- Windows'u yeniden başlatınca yalnızca durdurulan hizmetler, fare ivmesi ve Yapışkan Tuşlar ayarı kendiliğinden düzelir; güç planı, kayıt defteri
  ve ağ kartı ayarı **geri yükleyene kadar** kalır.

## Güncelleme

Antilag açılırken (Ayarlar'da kapatılabilir; kapalıyken **Şimdi denetle** yine çalışır) GitHub'dan yalnızca en yeni sürüm numarasına bakar. Yeni sürüm varsa sol çubukta küçük bir kart çıkar.
**Varsayılan olarak yeni sürüm kendiliğinden kurulur** (Ayarlar > **Yeni sürümü kendiliğinden kur**): kartta ilerleme ve **Vazgeç** görünür, kurulum dosyası indirilir, **proje sahibinin anahtarıyla imzalı** sürüm bilgisine göre
doğrulanır (boyut, SHA-256 ve imza), Antilag kapanır, kurulum **pencere açmadan** çalışır ve Antilag yeniden açılır. Bu anahtarı kapatırsan kart yalnızca haber verir (**Güncelle** ya da **Sonra**) ve kurulum sen Güncelle'ye basınca olur.
Bir sürüm için kendiliğinden deneme **bir kez** yapılır: indirme ya da kurulum başlayıp başarısız olursa her açılışta yeniden denenmez (kartta **Tekrar dene** ya da **Güncelle** ile sen denersin). Yalnızca henüz hiçbir şey indirilmeden CS2 açıldıysa bir sonraki açılışta yeniden denenir. Antilag açık değilken hiçbir şey indirilmez ya da kurulmaz.

- Güncelleme yalnızca **boştayken** yapılır (Başlat çalışırken değil) ve **CS2 açıkken hiç yapılmaz**: o zaman denetim bile yapılmaz; indirme sürerken CS2 açılırsa indirme durur.
- Önceki çalıştırmanın geri yükleme kaydı güncellemeden etkilenmez; güncellemeden sonra da geri yükleyebilirsin.
- Güncelleme **geri alınamaz**; eski sürümler Sürümler sayfasında durur. Uygulama kendiliğinden eski bir sürüme dönmez.
- Bu imza (güncelleme imzası) ile Windows'un gösterdiği "yayıncı" bilgisi (Authenticode) ayrı şeylerdir: ilkini yalnızca Antilag denetler, ikincisini Windows. Bkz. [Kod imzalama](#kod-imzalama).

## Anti-cheat (VAC / FACEIT) ve oyun güvenliği

Antilag, Valve'ın, FACEIT'in ya da herhangi bir anti-cheat sağlayıcısının ürünü değildir ve onlar tarafından onaylanmamıştır. Hiçbir yazılım "sıfır risk" garantisi veremez;
bu bölüm Antilag'ın ne yaptığını ve ne yapmadığını anlatır, bir güvence vermez. Kullanım sorumluluğu sana aittir.

Antilag riski yapısal olarak azaltacak şekilde yazıldı:

- **Oyun açıkken hiçbir değişiklik yapmaz.** CS2 açıksa Antilag hiçbir değişiklik yapmadan kapanır; Antilag açıkken CS2 açılırsa kalan her şey iptal olur ve açık kalan Antilag yalnızca bekler.
  Yine de **Uygulamayı kapat**'a basıp CS2'yi ondan sonra aç. Arka planda işlem, tepsi simgesi, zamanlanmış görev ya da hizmet bırakmaz.
- **CS2, Steam ve FACEIT'e hiçbir tutamaç açılmaz** ve pencereleri sorgulanmaz. cs2.exe'ye öncelik, bellek, komut satırı, enjeksiyon, hook, sürücü yok.
- **Diğer anti-cheat programları kapatılabilir.** Tam temizlik ve Yayıncı (Hafif hiçbir program kapatmaz); Vanguard, EasyAntiCheat ve BattlEye gibi diğer oyunların hile korumalarını ve CS2 için kullanılan **ESEA, Esportal ve Gamers Club**
  istemcilerini de kapatır (önce normal kapatma isteği, 6 saniyede kapanmazsa zorla). Bunlardan birini kullanıyorsan **Ayarlar > Her zaman açık kalsın** listesine ekle ya da
  Ayrıntılar'dan bu anahtarı kapat. Valorant için bilgisayarı yeniden başlatman gerekebilir. FACEIT'in kendi parçaları her zaman korunur.
- Kayıt defterine yalnızca Windows'un kendi ayarları yazılır. CS2'nin yolunu içeren en fazla iki değer vardır: ekran kartı tercihi (hazır ayarlarda
  açık) ve tam ekran iyileştirmesinin kapatılması (Tam temizlik ve Yayıncı'da açık, Hafif'te kapalı). Bunları Windows oyun açılırken okur;
  Antilag'dan oyuna hiçbir kod girmez. Açılış sayfasındaki bir anahtar ayrıca yalnızca Windows'un kendi başlangıç bayrağını yazar (`...\Explorer\StartupApproved`), oyunla ilgisi yoktur.

Bizim kontrolümüz dışında kalan belirsizlik: Valve'ın ve FACEIT'in üçüncü taraf yazılımlara bakışı ve FACEIT'in çekirdek düzeyindeki anti-cheat'inin engelleme listesi zamanla değişebilir.
İlk kullanımda FACEIT'in "desteklenmeyen yazılım" uyarısı vermediğini kontrol et; verirse Antilag'ı kullanma ve [bildir](#sorun-bildirme-ve-güvenlik).

## Gizlilik

Veriler `%APPDATA%\CS2Hazirla\` altında durur (`settings.json`, `state.json` = geri yükleme kaydı, `logs\` = son 14 günün günlüğü). Klasör adı projenin ilk adından kalmıştır;
adı değiştirmek bekleyen bir geri yükleme kaydını kaybettirirdi. Telemetri ve hesap yoktur.

İnternete yalnızca üç iş için çıkılır; hiçbirinde hesap, kullanıcı adı, program listesi ya da dosya bilgisi gönderilmez. Yalnızca her internet isteğinde olduğu gibi IP adresin ve (sürüm denetiminde) "Antilag/sürüm" bilgisi karşı tarafın sunucusuna görünür:
- **Ping ölçümü:** Başlat'a bastığında, değişiklikten önce ve sonra olmak üzere en çok 20'şer ICMP yankı isteği `1.1.1.1`'e (Cloudflare) gider. Hedef `settings.json` içindeki `PingTarget` ile değişir. Hedef senin IP adresini görür.
- **İnternet kontrolü (Kontrol sayfası):** yalnızca **Kontrol et**'e basınca, birkaç saniye boyunca modemine (ev ağında kalır), `1.1.1.1` (Cloudflare), `8.8.8.8` (Google) ve `PingTarget` bunlardan farklıysa ona en çok 50'şer ICMP yankı isteği gider; ayrıca `steampowered.com` adı için bilgisayarının adres sunucusuna (DNS) bir ad çözme isteği yapılır. Hiçbir şey kaydedilmez; bu adresler ve DNS sunucun senin IP adresini görür. CS2 açıkken ve bir çalıştırma, önizleme ya da güncelleme sürerken yapılmaz.
- **Yeni sürüm denetimi:** Antilag açılırken `github.com/fara1ka/antilag/releases` adresinden (dosyalar `githubusercontent.com` üzerinden gelir) yalnızca küçük bir sürüm bilgisi dosyası istenir. Ayarlar'da kapatılabilir: kapalıyken kendiliğinden istek yapılmaz, yalnızca **Şimdi denetle**'ye basınca yapılır. Kurulum dosyası, "Yeni sürümü kendiliğinden kur" açıksa (varsayılan) yeni sürüm bulununca kendiliğinden, kapalıysa yalnızca sen Güncelle'ye basınca `%LOCALAPPDATA%\Antilag\update` altına iner ve bir sonraki açılışta silinir. Ayrıca kurulum, yanına bir günlük (`kurulum.log`) yazar; bu günlük dosya yollarını (Windows kullanıcı adın dahil) içerir ve internete gönderilmez; bir sorun bildirirken paylaşmadan önce kontrol et.

Günlük ve geri yükleme kaydı kapattığın programların adlarını ve dosya yollarını (Windows kullanıcı adın dahil) içerebilir; bir hata bildirirken paylaşmadan önce kontrol et.

## Kod imzalama

İki ayrı imza vardır:
- **Güncelleme imzası (var).** Her sürümün `latest.json` dosyası proje sahibinin imza anahtarıyla imzalıdır; Antilag güncellemeyi yalnızca bu imza, boyut ve SHA-256 uyuşursa kurar.
  GitHub hesabı ele geçirilse bile imza anahtarı olmadan sahte güncelleme yollanamaz. Bu imza Windows'un gösterdiği uyarıları değiştirmez.
- **Kurulum yeri ve yönetici izni.** Antilag `%LOCALAPPDATA%\Programs\Antilag` altına kurulur ama yönetici izniyle çalışır: aynı Windows hesabında çalışan bir program `Antilag.exe`'yi değiştirip UAC onayında yönetici yetkisi kazanabilir. Antilag bilgisayarında zaten çalışan bir zararlıya karşı koruma sağlamaz.
- **Windows dijital imzası, Authenticode (henüz yok).** Kurulum dosyası ve `Antilag.exe` bu imzayla imzalı değildir; Windows "Bilinmeyen yayıncı" ya da SmartScreen uyarısı gösterebilir.
  Ücretsiz açık kaynak imzalama programı için başvuru planlanıyor; imza gelince bu bölüm güncellenir. Güvendiğin tek kaynak bu depodaki Sürümler sayfasıdır.

## Lisans

Antilag özgür yazılımdır. Telif hakkı (C) 2026 fara1ka.
Bu programı Free Software Foundation tarafından yayımlanan GNU Genel Kamu Lisansı'nın 3. sürümü ya da (isteğe bağlı) sonraki bir sürümünün koşulları altında kullanabilir, değiştirebilir ve paylaşabilirsin.
Program yararlı olması umuduyla dağıtılır, ancak **HİÇBİR GARANTİ VERİLMEZ**; kullanımından doğan sonuçlar (kapanan bir program, kaybolan kaydedilmemiş iş, bir oyun ya da oyun platformunun vereceği bir yaptırım dahil) sana aittir.
Tam metin: [LICENSE](LICENSE). Üçüncü taraf bileşenler: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). "Antilag" adı ve simgesi bu lisansın kapsamı dışındadır (GPL-3.0 md. 7(e)); değiştirilmiş sürümlere başka bir ad ver.

Kodun büyük bölümü Claude (Anthropic'in yapay zekâ modeli) yardımıyla yazıldı; Anthropic projeyi onaylamamıştır. Uygulamanın sağ altındaki "created by claude via fara1k" yazısı budur.

## Markalar ve bağlantı

Antilag bağımsız, topluluk tarafından geliştirilen bir projedir; Valve, FACEIT, Microsoft, Discord, NVIDIA, AMD, Anthropic ya da bu belgede ve uygulamada adı geçen başka bir şirketle
bağlantılı değildir, onlar tarafından onaylanmamış, desteklenmemiş ya da sponsor edilmemiştir. Counter-Strike, CS2, Steam, VAC, Windows, Discord, Spotify, WhatsApp, Claude, NVIDIA ve diğer ürün adları
ilgili sahiplerinin ticari markalarıdır; yalnızca Antilag'ın hangi programlarla çalıştığını belirtmek için kullanılır. "Antilag" adının AMD'nin Radeon Anti-Lag özelliğiyle bir ilgisi yoktur.
Tam liste: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Sorun bildirme ve güvenlik

- **Hata ya da öneri:** [Issues](https://github.com/fara1ka/antilag/issues) sayfasında yeni kayıt aç. Antilag sürümünü (Ayarlar sayfasının altında yazar), Windows sürümünü ve ne olduğunu yaz.
  `%APPDATA%\CS2Hazirla\logs\` altındaki günlüğü eklemeden önce aç ve kullanıcı adın gibi kişisel bilgileri sil.
- **Güvenlik açığı:** herkese açık kayıt açma; [SECURITY.md](SECURITY.md) dosyasındaki özel bildirim yolunu kullan.
- **Katkı:** [CONTRIBUTING.md](CONTRIBUTING.md).

## Geliştirici

Mimari, test, derleme ve yayın: [docs/GELISTIRICI.md](docs/GELISTIRICI.md).
