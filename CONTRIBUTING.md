# Katkı

Önce bir [Issue](https://github.com/fara1ka/antilag/issues) aç; ne yapmak istediğini ve neden gerektiğini yaz.

- Testler geçmeli: `dotnet test tests\CS2Hazirla.Tests --filter "FullyQualifiedName!~Live"` (yalnızca Windows'ta çalışır).
- **Koruma listesi** (`ProtectedList`), **Katalog** (`Catalog`) ve **golden dosyası** (`tests/CS2Hazirla.Tests/Golden/selection.golden.txt`) güvenlik açısından önemlidir: bunları değiştiren bir değişiklik nedenini açıklamalı ve "bugün kapanan hiçbir şey kapanmaz hale gelmemeli" kuralını bozmamalıdır.
- Yeni bir özellik eklemeden önce, aynı sonucu bir şeyi silerek alıp alamayacağına bak (docs/GELISTIRICI.md §1).
- Oyun açıkken çalışan, izleyen ya da oyun işlemlerine dokunan hiçbir şey eklenmez (docs/GELISTIRICI.md §5).
- Fork yapıyorsan `UpdateClient.ReleasesUrl` ve `UpdateSigning.PublicKeys` değerlerini kendi deponla ve kendi anahtarınla değiştir; aksi halde uygulaman bu projenin sürümlerini arar. Değiştirilmiş sürümleri "Antilag" adıyla dağıtma.
- Lisans GPL-3.0-or-later'dır; gönderdiğin değişiklik aynı lisansla yayımlanır.
