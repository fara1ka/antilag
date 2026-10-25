# Güvenlik politikası

**Desteklenen sürüm:** yalnızca en son sürüm.

**Açık bildirme:** Güvenlik açığını herkese açık Issues'a yazma. GitHub'da bu deponun **Security > Report a vulnerability** düğmesiyle özel olarak bildir. İlk dönüş için hedef 7 gündür; bu tek kişilik bir projedir, süre taahhüdü değildir.

**Kapsam:** güncelleme zinciri (`latest.json` imzası, boyut ve SHA-256 doğrulaması, yalnızca github.com adresleri), yönetici yetkisiyle çalışan kod, `state.json` içinden gelen yol ve hizmet adlarının doğrulanması, kurulum dosyası.

## Bilinen sınırlar (gizlemiyoruz)

- **Kullanıcı klasörüne kurulur, yönetici yetkisi ister.** Antilag `%LOCALAPPDATA%\Programs\Antilag` altına kurulur ama yönetici olarak çalışır. Bu bir **yetki yükseltme yoludur**: aynı Windows hesabında çalışan, yönetici olmayan bir program `Antilag.exe`'yi değiştirebilir ve kullanıcı Antilag'ın UAC isteminde "Evet" dediğinde yönetici yetkisi kazanır. Antilag buna karşı koruma sağlamaz; bilgisayarında zaten çalışan bir zararlıya karşı bir güvenlik aracı değildir. Güncelleme indirme klasörü de aynı hesabın klasörüdür; dosya hiçbir zaman imza ve özet doğrulanmadan çalıştırılmaz, ama klasörün kendisi aynı hesaptaki bir programa karşı korunmaz. Yönetici izniyle `Program Files` altına kurulum ileride değerlendirilebilir.
- **İki ayrı imza vardır ve ikisi aynı şey değildir.** (1) Uygulama içi güncellemeler, proje sahibinin imza anahtarıyla imzalı bir `latest.json` ile doğrulanır; bunu yalnızca Antilag denetler. (2) Windows'un gösterdiği "yayıncı" bilgisi dijital kod imzasına (Authenticode) bağlıdır; dosya bu imzayla imzalı değilse Windows "Bilinmeyen yayıncı" ya da SmartScreen uyarısı gösterebilir. Durum için README'deki "Kod imzalama" bölümüne bak.
- **Güncelleme geri alınamaz.** Eski sürümler Releases sayfasında durur; uygulama hiçbir zaman eski bir sürüme kendiliğinden dönmez.
- **Eski ama imzalı bir sürüm bilgisi**, bir kopyanın sürümünden yüksekse yeniden önerilebilir (örneğin hesabı ele geçiren biri eski bir imzalı sürümü yeniden yayınlarsa): yalnızca imzalı sürümler arasından, hiçbir zaman imzasız dosya.
- **İstek adresleri:** güncelleme isteği yalnızca `github.com` ve `*.githubusercontent.com` adreslerine (https, 443) gider; yönlendirmeler tek tek denetlenir.
- **İmza anahtarı koruması:** günlük imza anahtarı Windows hesabına bağlı şifrelenmiş (DPAPI) bir dosyada durur; bu, dosyanın başka bir bilgisayara kopyalanmasına karşı korur, aynı hesapta çalışan zararlı bir programa karşı değil. Yedek anahtar parolayla şifrelidir.

## İmza anahtarı sızarsa

Güncelleme imzası için iki açık anahtar uygulamanın içindedir (`UpdateSigning.PublicKeys`): günlük kullanılan anahtar ve çevrimdışı saklanan yedek anahtar. Günlük anahtar kaybolursa ya da sızarsa yedek anahtarla yeni bir sürüm imzalanır; kurulu kopyalar onu kabul eder. İki anahtar da kaybolursa eski kopyalar yeni anahtarı kabul edemez: yeni bir kurulum dosyası duyurulur ve herkesin bir kez elle kurması gerekir. Özel anahtar hiçbir zaman depoda, günlükte ya da sohbette bulunmaz.
